using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一个房间。
///
/// 房间是一个预制体，里面装着：地形、敌人、出生点、一扇门。
/// 它只关心一件事：**敌人是不是都死光了**。死光了就开门，玩家进门就换下一间。
///
/// 它不知道自己排在第几间，也不知道下一间是什么 —— 那是 RoomManager 的事。
/// 这条边界很重要：将来要做「Boss 房」「商店房」「宝箱房」，
/// 都是新的房间预制体 + 一点判断逻辑，不用动 RoomManager 的核心流程。
/// </summary>
public class Room : MonoBehaviour
{
    [Header("房间结构")]
    [Tooltip("玩家进入这个房间时站的位置。留空则用房间原点")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("清光敌人后要打开的门。留空则往上/往下自动找")]
    [SerializeField] private Door door;

    [Header("规则")]
    [Tooltip("勾上则必须清光敌人才开门。没有敌人的房间会立刻开门")]
    [SerializeField] private bool requireClear = true;

    [Header("随机刷怪")]
    [Tooltip("把房间里的「占位怪」（挂 EnemyController 但没配 EnemyConfig 的方块）当成刷怪点，" +
             "每局按种子随机换成真怪。关掉就保留占位怪原样")]
    [SerializeField] private bool spawnRandomEnemies = true;

    [Tooltip("从第几间房开始，每个刷怪点多刷一只")]
    [SerializeField] private int extraFromRoom = 3;

    [Tooltip("再往后几间房，每个刷怪点再多刷一只（2 = 每两间多一只）")]
    [SerializeField] private int extraStepRooms = 2;

    [Tooltip("一个刷怪点最多刷几只（含第一只）")]
    [SerializeField] private int maxPerPoint = 3;

    [Tooltip("多刷出来的怪相对刷怪点的横向散开距离，避免完全叠在一起")]
    [SerializeField] private float scatterX = 1.2f;

    [Tooltip("刷怪点离门太近时，往房间内侧让开这么多（单位）。避免怪刷在门的另一侧，近战够不着")]
    [SerializeField] private float doorClearance = 1.6f;

    [Tooltip("怪被挤到「门的另一侧」时自动拉回房间里。门锁着的时候才有意义，是防卡死的兜底")]
    [SerializeField] private bool keepEnemiesInside = true;

    [Tooltip("多久检查一次「有没有怪跑到门的另一侧」")]
    [SerializeField] private float insideCheckInterval = 0.35f;

    /// <summary>玩家进入本房间时的落点</summary>
    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

    /// <summary>
    /// 本房间的类型（战斗 / 精英 / 商店 / 宝箱 / Boss）。
    /// 由 RoomManager 在实例化后注入，房间自己不写死 —— 同一个预制体
    /// 可以当普通房也能当精英房，区别只在外面的房间池配置。
    /// </summary>
    public RoomType Type { get; private set; } = RoomType.Battle;

    /// <summary>本房间的门</summary>
    public Door Door => door;

    /// <summary>敌人是否已清光（门是否已开）</summary>
    public bool IsCleared { get; private set; }

    /// <summary>还活着的敌人数量。将来 HUD 显示「剩余 3」就靠它</summary>
    public int AliveEnemies { get; private set; }

    /// <summary>清光时触发一次</summary>
    public event Action<Room> Cleared;

    private readonly List<Health> _enemies = new();

    /// <summary>房间预制体里摆的「占位怪」——刷怪点。Start 里会被换成真怪并销毁</summary>
    private readonly List<GameObject> _spawnMarkers = new();

    /// <summary>上一次检查「怪有没有跑到门后面」的时间</summary>
    private float _insideCheckTimer;

    private RoomManager _manager;

    /// <summary>
    /// 由 RoomManager 在实例化之后立刻调用，把「谁负责换房间」告诉它。
    ///
    /// 为什么不用 Inspector 里拖引用？
    /// 因为房间是预制体，预制体里不能可靠地存「场景里的某个物体」的引用 ——
    /// 那样一旦换场景或换 RoomManager，引用就全断了。
    /// 让创建者主动注入，预制体就能保持自包含。
    /// </summary>
    public void Initialize(RoomManager manager)
    {
        Initialize(manager, RoomType.Battle);
    }

    /// <summary>
    /// 带类型的版本：RoomManager 按房间池里配的类型注入。
    /// 保留上面那个单参数重载，是为了不打断任何还在用旧签名的调用。
    /// </summary>
    public void Initialize(RoomManager manager, RoomType type)
    {
        _manager = manager;
        Type = type;
    }

    private void Awake()
    {
        if (door == null) door = GetComponentInChildren<Door>(true);

        // 房间里的所有敌人都订阅一遍。用 EnemyController 而不是 Health 来找，
        // 是因为可破坏的箱子、木桶也可能有 Health，但它们不该算进「清场」条件
        foreach (var enemy in GetComponentsInChildren<EnemyController>(true))
        {
            // 没挂 EnemyConfig 的是「占位怪」：它只是标出「这里要刷一只怪」，
            // 不是真敌人。先在 Awake 把它的贴图和碰撞体关掉，等 Start 换成真怪 ——
            // 关贴图必须在 Awake 做，否则会闪一帧紫色方块；
            // 关碰撞体是因为 Destroy 要到帧末才生效，这中间它会和刚刷出来的怪互相挤。
            if (spawnRandomEnemies && !enemy.HasConfig)
            {
                _spawnMarkers.Add(enemy.gameObject);

                foreach (var renderer in enemy.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer != null) renderer.enabled = false;
                }

                foreach (var collider in enemy.GetComponentsInChildren<Collider2D>(true))
                {
                    if (collider != null) collider.enabled = false;
                }

                continue;
            }

            var health = enemy.GetComponent<Health>();
            if (health == null) continue;

            _enemies.Add(health);
        }

        // ★ 这里绝对不能调 RecountAlive()。
        //
        // RecountAlive() 读的是 Health.Current，而 Current 是在 Health.Awake() 里
        // 才被赋成 maxHealth 的。Room 挂在房间根物体上、Enemy 是子物体，
        // Unity 实例化预制体时【根物体的 Awake 先跑】—— 这一刻 Health.Current 还是 0，
        // 所有敌人都会被判成「已经死了」，AliveEnemies 算出来是 0，
        // 紧接着 Start() 就认定「已清场」，门一开局就是开的。
        //
        // 症状：敌人明明活着，走过去碰一下门就被传回出生点。
        // 教训：Unity 只保证「所有 Awake 跑完才跑 Start」，**跨物体的 Awake 顺序不能假设**。
        // 凡是「我要读的值是别的组件在 Awake 里写的」，就必须挪到 Start 去读。
    }

    private void OnEnable()
    {
        foreach (var h in _enemies) h.Died += OnEnemyDied;
    }

    private void OnDisable()
    {
        foreach (var h in _enemies) h.Died -= OnEnemyDied;
    }

    private void Start()
    {
        // 刷怪要排在「数人头」之前：刷出来的怪也属于这一间，得算进清场条件
        SpawnRandomEnemies();

        // 数「还活着几个敌人」必须在这里数，不能放在 Awake —— 理由见 Awake 里的注释。
        // 此刻所有物体的 Awake 都已经跑完，Health.Current 一定是 maxHealth
        RecountAlive();

        // 开局先判一次：没有敌人的房间（奖励房、走廊）应该一进来门就是开的，
        // 否则玩家会被永久关在里面 —— 这种 bug 很致命而且不容易想到
        EvaluateClear();

        // 留一行开局日志。房间这类「门为什么开了/没开」的问题，
        // 光看画面很难判断，看数字一眼就清楚
        Debug.Log($"[Room] {name}（{RoomTypeNames.Of(Type)}）：敌人 {AliveEnemies} 个，门{(IsCleared ? "已开" : "锁着")}", this);
    }

    /// <summary>
    /// 把占位怪换成随机真怪：每个刷怪点一只起步，越往后的房间刷得越多。
    ///
    /// ★ 随机源用的是 RoomManager 的种子 + 房间号：
    ///   同一个种子跑两次，这一间刷出来的怪必须一模一样，否则「复现这一局」就是空话。
    /// ★ 抽怪按「洗过牌的下标」走：同一间房里尽量六只各来一次，而不是六只哥布林。
    /// </summary>
    private void SpawnRandomEnemies()
    {
        if (_spawnMarkers.Count == 0) return;

        if (!spawnRandomEnemies)
        {
            // 关掉了随机刷怪：占位怪就是这一间的敌人（老行为）
            return;
        }

        int pool = EnemySpawning.Count;
        if (pool == 0)
        {
            // 一只怪都找不到（多半是 Resources/Enemies 被删了）：
            // 把刷怪点清掉，别留一堆看不见、也打不死的空壳卡住门
            Debug.LogWarning($"[Room] {name} 找不到任何怪物预制体，这一间只能空着。", this);
            foreach (GameObject marker in _spawnMarkers)
            {
                if (marker != null) Destroy(marker);
            }
            _spawnMarkers.Clear();
            return;
        }

        int roomNumber = _manager != null ? Mathf.Max(1, _manager.RoomsCleared) : 1;
        System.Random rng = MakeRng(roomNumber);
        int[] order = EnemySpawning.ShuffledOrder(rng);
        int cursor = 0;

        int perPoint = Mathf.Clamp(1 + ExtraAt(roomNumber), 1, Mathf.Max(1, maxPerPoint));
        var spawnedNames = new List<string>(_spawnMarkers.Count * perPoint);

        foreach (GameObject marker in _spawnMarkers)
        {
            if (marker == null) continue;

            Vector3 origin = ClampInside(marker.transform.position);

            for (int i = 0; i < perPoint; i++)
            {
                // 第一只就站在刷怪点上，多出来的左右散开
                float offset = i == 0 ? 0f : (float)(rng.NextDouble() * 2.0 - 1.0) * scatterX;

                // 散开之后还要再收一次：不能让任何一只落到门的另一侧（近战会打不到）
                Vector3 position = ClampInside(origin + new Vector3(offset, 0f, 0f));

                GameObject prefab = EnemySpawning.Pool[order[cursor % order.Length]];
                cursor++;

                GameObject spawned = EnemySpawning.Spawn(prefab, position, transform);
                if (spawned == null) continue;

                spawnedNames.Add(spawned.name);

                // 刷出来的怪要自己进场登记：Awake/OnEnable 都跑过了，_enemies 不会自动带上它
                var health = spawned.GetComponent<Health>();
                if (health == null) continue;

                _enemies.Add(health);
                health.Died += OnEnemyDied;
            }

            // 占位怪本身不是敌人了，换完就销毁（贴图在 Awake 里已经关掉，不会闪）
            Destroy(marker);
        }

        _spawnMarkers.Clear();

        // 留一行「这一间刷了谁」的日志：同一颗种子应该刷出一模一样的名单，方便复现
        Debug.Log($"[Room] 第 {roomNumber} 间刷怪：{string.Join("、", spawnedNames)}（每点 {perPoint} 只）", this);
    }

    /// <summary>第 roomNumber 间房，每个刷怪点额外多刷几只</summary>
    private int ExtraAt(int roomNumber)
    {
        if (roomNumber < extraFromRoom) return 0;

        int extra = 1 + (roomNumber - extraFromRoom) / Mathf.Max(1, extraStepRooms);

        // 精英房多一只：类型是 RoomManager 抽房间时注入的，不用改房间预制体
        if (Type == RoomType.Elite) extra += 1;

        return Mathf.Clamp(extra, 0, Mathf.Max(0, maxPerPoint - 1));
    }

    // ---------------- 门的「内侧」判定 ----------------
    //
    // 门锁着的时候是一面实心墙，怪刷在它后面 / 被挤到它后面，近战就永远够不着，
    // 房间也就永远清不完（门不会开）。所以刷怪点要往房间内侧收，
    // 而且每 0.35 秒兜底检查一次：真的被挤过去了，就把它拉回来。

    /// <summary>门在出生点的哪一侧：+1 = 门在右边（绝大多数房间），-1 = 在左边，0 = 没门</summary>
    private int DoorSide
    {
        get
        {
            if (door == null) return 0;
            float offset = door.transform.position.x - SpawnPoint.position.x;
            if (Mathf.Abs(offset) < 0.01f) return 0;
            return offset > 0f ? 1 : -1;
        }
    }

    private float DoorX => door != null ? door.transform.position.x : 0f;

    /// <summary>允许刷怪 / 允许站人的最远 x：门内侧再留 <see cref="doorClearance"/> 的余量</summary>
    private float InsideLimitX
    {
        get
        {
            int side = DoorSide;
            if (side == 0) return 0f;
            return DoorX - side * Mathf.Max(0.1f, doorClearance);
        }
    }

    /// <summary>把一个位置往房间内侧收到门的这一边</summary>
    private Vector3 ClampInside(Vector3 position)
    {
        int side = DoorSide;
        if (side == 0) return position;

        float limit = InsideLimitX;
        if (side > 0 && position.x > limit) position.x = limit;
        else if (side < 0 && position.x < limit) position.x = limit;
        return position;
    }

    private void Update()
    {
        if (!keepEnemiesInside) return;
        if (door == null || door.IsUnlocked) return;    // 门开了就不用管了（玩家能走过去打）
        if (DoorSide == 0) return;

        _insideCheckTimer -= Time.deltaTime;
        if (_insideCheckTimer > 0f) return;
        _insideCheckTimer = Mathf.Max(0.1f, insideCheckInterval);

        int side = DoorSide;
        float doorX = DoorX;
        const float epsilon = 0.15f;
        float limit = InsideLimitX;

        for (int i = 0; i < _enemies.Count; i++)
        {
            Health health = _enemies[i];
            if (health == null || !health.IsAlive) continue;

            Vector3 position = health.transform.position;
            bool behindDoor = side > 0 ? position.x > doorX + epsilon : position.x < doorX - epsilon;
            if (!behindDoor) continue;

            Vector3 rescued = position;
            rescued.x = limit;

            // 有刚体时必须以刚体的坐标为准，只改 transform 下一帧会被物理拉回去
            var body = health.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = rescued;
                body.velocity = Vector2.zero;
            }
            health.transform.position = rescued;

            Debug.LogWarning(
                $"[Room] {health.name} 跑到门后面了（门还锁着），已拉回房间内侧 x={limit:0.00}", this);
        }
    }

    /// <summary>
    /// 这一间用的随机源。种子 = 本局种子 × 31 + 房间号 × 977（乘质数是为了让相邻房间的序列岔开）。
    /// 没有 RoomManager（场景里手摆的房间）时退回时间种子，至少还能随机。
    /// </summary>
    private System.Random MakeRng(int roomNumber)
    {
        int seed = _manager != null
            ? _manager.RunSeed
            : unchecked((int)(System.DateTime.Now.Ticks & 0x7FFFFFFF));
        return new System.Random(unchecked(seed * 31 + roomNumber * 977));
    }

    private void OnEnemyDied()
    {
        RecountAlive();
        EvaluateClear();
    }

    private void RecountAlive()
    {
        int count = 0;
        for (int i = 0; i < _enemies.Count; i++)
        {
            // 敌人被 Destroy 之后 _enemies 里的引用会变成「假 null」，
            // Unity 重载了 == 所以 h != null 能正确识别，这里不用手动清理列表
            if (_enemies[i] != null && _enemies[i].IsAlive) count++;
        }
        AliveEnemies = count;
    }

    private void EvaluateClear()
    {
        if (IsCleared) return;

        if (requireClear && AliveEnemies > 0) return;

        IsCleared = true;

        if (door != null) door.Unlock();

        Cleared?.Invoke(this);
    }

    /// <summary>由 Door 调用：玩家走进了这扇门</summary>
    public void RequestAdvance()
    {
        // 门没开就根本走不进来（门是实心的），这里再判一次是双保险。
        // 万一将来有人把门改成常开的触发器，也不会出现「没打完就能跑」
        if (!IsCleared) return;

        if (_manager != null) _manager.Advance();
        else Debug.LogWarning($"[Room] {name} 没有 RoomManager，换不了房间。检查 RoomManager 是否调用了 Initialize。", this);
    }
}
