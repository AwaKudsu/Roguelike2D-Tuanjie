using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 房间流水线：一间的敌人清光了、玩家进了门，就淡出 → 换下一间 → 淡入。
///
/// 它只管「现在该是第几间、下一间长什么样」，不管房间里有什么。
/// 将来关卡的难度曲线（第 5 间开始出精英怪、每 5 间一个 Boss）也挂在这里。
///
/// 换房间的完整顺序（这个顺序不能乱）：
///   1. 淡出到全黑        ← 玩家此时什么都看不见
///   2. 销毁旧房间
///   3. 实例化新房间
///   4. 把玩家瞬移到新房间的出生点，并清零速度
///   5. 淡入
/// 第 4 步的清速度很容易忘：不清的话玩家会带着「进门那一下的冲刺惯性」
/// 直接飞出出生点，看起来像被弹射出去。
/// </summary>
public class RoomManager : MonoBehaviour
{
    [Header("房间池（推荐）")]
    [Tooltip("房间资产：权重、类型、第几间开始出现都在这里配。留空则退回下面的旧列表")]
    [SerializeField] private RoomPool roomPool;

    [Tooltip("旧版房间列表：只能表达「有这几间」，没有权重和类型。\n" +
             "房间池填了就以房间池为准，这个列表会被忽略")]
    [SerializeField] private List<GameObject> roomPrefabs = new();

    [Header("随机种子")]
    [Tooltip("0 = 每局随机一个，并打印在 Console 里。\n" +
             "填具体数字 = 固定关卡顺序：出了 bug 能让别人一模一样地复现，也是写单元测试的前提")]
    [SerializeField] private int seed;

    [Tooltip("房间生成的位置。留空则用本物体的位置")]
    [SerializeField] private Transform roomAnchor;

    [Header("过渡")]
    [Tooltip("黑幕。留空则换房间时不会有淡入淡出（会很突兀）")]
    [SerializeField] private ScreenFader fader;

    [Header("规则")]
    [Tooltip("勾上则用「袋子抽取」：一轮之内每个房间各出现一次，抽完再重新装袋。" +
             "比纯随机体感好得多 —— 纯随机经常连着抽到同一间，玩家会以为游戏坏了")]
    [SerializeField] private bool useBagRandom = true;

    [Tooltip("开局自动生成第 1 间。关掉的话场景里得先摆好一间带 Room 组件的房间")]
    [SerializeField] private bool buildFirstRoomOnStart = true;

    [Tooltip("换房间时清掉场上飞着的投射物。不清的话上一间的火球会飞进新房间")]
    [SerializeField] private bool cleanupProjectiles = true;

    /// <summary>已经进入过的房间数（第 1 间生出来时就是 1）</summary>
    public int RoomsCleared { get; private set; }

    /// <summary>每换一间触发一次，参数是新的 RoomsCleared。将来的难度曲线、HUD 计数都订阅它</summary>
    public event Action<int> RoomChanged;

    /// <summary>当前房间。还没生成时为 null</summary>
    public Room CurrentRoom => _currentRoom;

    /// <summary>正在换房间的过渡中。此时不应该再触发一次</summary>
    public bool IsSwitching => _switching;

    /// <summary>当前房间的类型（战斗 / 精英 / 商店 / 宝箱 / Boss）</summary>
    public RoomType CurrentRoomType { get; private set; } = RoomType.Battle;

    /// <summary>
    /// 本局实际用的种子。seed 填 0 时这里是开局随机出来的那个数。
    /// 想复现一局：把 Console 里打印的这个数字填回 Seed。
    /// </summary>
    public int Seed => _resolvedSeed;

    /// <summary>是否在用房间池（否则走旧列表）</summary>
    public bool UsingPool => roomPool != null && roomPool.Count > 0;

    /// <summary>当前生效的候选总数</summary>
    private int ActiveCount => UsingPool ? roomPool.Count : roomPrefabs.Count;

    private GameObject _currentInstance;
    private Room _currentRoom;
    private Transform _player;
    private Rigidbody2D _playerBody;
    private bool _switching;

    // 袋子随机的状态：_bag 是「还没抽到的房间下标」，抽一个少一个，空了重新装袋
    private readonly List<int> _bag = new();
    private int _lastIndex = -1;

    // 全局唯一的随机源。所有抽取都走它，绝不碰 UnityEngine.Random —— 见 EnsureRandom
    private System.Random _rng;
    private int _resolvedSeed;

    // 上一次抽到的池子条目下标，用来避免连着两间抽到同一间
    private int _lastPoolPick = -1;

    private Vector3 AnchorPosition => roomAnchor != null ? roomAnchor.position : transform.position;

    private void Start()
    {
        ResolvePlayer();

        if (buildFirstRoomOnStart)
        {
            if (ActiveCount == 0)
            {
                Debug.LogWarning(
                    "[RoomManager] 房间池是空的，一间都生不出来。" +
                    "请给 Room Pool 指定一个 RoomPool 资产，或往 Room Prefabs 里至少拖一个房间预制体。", this);
                return;
            }

            BuildRoom(PickIndex(1));
        }
        else if (_currentRoom == null)
        {
            // 场景里已经手摆了一间，把它认领过来，这样它也能触发换房间
            _currentRoom = FindObjectOfType<Room>();
            if (_currentRoom != null) _currentRoom.Initialize(this, CurrentRoomType);
        }
    }

    /// <summary>
    /// 建随机源。整个工程只有这一个 System.Random 决定关卡顺序。
    ///
    /// ★★ 为什么不用 UnityEngine.Random？
    ///   它是全局静态状态：同一个种子跑两次结果不同。
    ///   关卡不可复现 → 出了 bug 无法复现 → 也写不了「给定种子生成的关卡是否一致」的单元测试。
    ///   （这一条在 00-行动清单.md 的 2.1 里点名要求）
    /// </summary>
    private void EnsureRandom()
    {
        if (_rng != null) return;

        // seed 填 0 视为「没指定」：用时间戳现抽一个。
        // 用 & 0x7FFFFFFF 是为了避免负数——System.Random 接受负数但打印出来容易让人困惑
        _resolvedSeed = seed != 0 ? seed : (int)(DateTime.Now.Ticks & 0x7FFFFFFF);
        _rng = new System.Random(_resolvedSeed);

        Debug.Log(
            $"[RoomManager] 本局种子 = {_resolvedSeed}（想复现这一局，把它填进 Seed 字段；填 0 表示每局随机）", this);
    }

    private void ResolvePlayer()
    {
        if (_player != null) return;

        var go = GameObject.FindGameObjectWithTag("Player");
        if (go == null)
        {
            Debug.LogWarning("[RoomManager] 找不到 Tag 为 Player 的对象，换房间时没法传送玩家。", this);
            return;
        }

        _player = go.transform;
        _playerBody = go.GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// 换下一间。由 Door 调用。
    /// 过渡途中重复调用会被忽略 —— 否则玩家站在门口不动就会连开好几个协程。
    /// </summary>
    public void Advance()
    {
        if (_switching) return;

        if (ActiveCount == 0)
        {
            Debug.LogWarning("[RoomManager] 房间池是空的，Advance 被忽略了。", this);
            return;
        }

        StartCoroutine(AdvanceRoutine());
    }

    private IEnumerator AdvanceRoutine()
    {
        _switching = true;

        if (fader != null) yield return fader.FadeOut();

        // ★ 这一层 try/catch 是有血的教训的：
        //   BuildRoom 末尾会 Invoke RoomChanged，任何订阅者抛异常都会把协程当场炸停。
        //   那样 fader.FadeIn() 永远跑不到，画面就永久停在黑幕上，
        //   而且 _switching 卡在 true，之后按门再也没反应。
        //   订阅者是外挂的（词条、成就、统计……），不能让它决定换房能不能收尾。
        try
        {
            // 要建的是「第几间」：已经清过 RoomsCleared 间，所以下一间就是 +1
            BuildRoom(PickIndex(RoomsCleared + 1));
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
        }

        if (fader != null) yield return fader.FadeIn();

        _switching = false;
    }

    private void BuildRoom(int index)
    {
        if (index < 0 || index >= ActiveCount) return;

        // 先确定「建哪一间、是什么类型」。两条路：新房间池 / 旧列表
        GameObject prefab;
        RoomType type;

        if (UsingPool)
        {
            var entry = roomPool.Entries[index];
            prefab = entry.prefab;
            type = entry.type;
        }
        else
        {
            prefab = roomPrefabs[index];
            type = RoomType.Battle;   // 旧列表没有类型信息，一律当普通战斗
        }

        if (prefab == null)
        {
            Debug.LogWarning($"[RoomManager] 第 {index + 1} 个候选是空槽位，这一间没法生成。", this);
            return;
        }

        if (cleanupProjectiles) CleanupProjectiles();

        // 先销毁旧的：地刺、残留的敌人、掉在地上的东西一起清掉。
        // 玩家不是房间的子物体，所以不会被一起销毁 —— 这是刻意的
        if (_currentInstance != null) Destroy(_currentInstance);

        _currentInstance = Instantiate(prefab, AnchorPosition, Quaternion.identity);
        _currentInstance.name = prefab.name;   // 去掉名字后面的 (Clone)，Hierarchy 里好认

        CurrentRoomType = type;

        _currentRoom = _currentInstance.GetComponent<Room>();
        if (_currentRoom == null)
        {
            Debug.LogWarning(
                $"[RoomManager] 预制体「{prefab.name}」上没有 Room 组件，" +
                "出生点和门都不会生效，玩家会被留在原地。", this);
        }
        else
        {
            // 类型由「谁创建的」注入，而不是房间自己写死 ——
            // 同一个预制体将来可以当普通房也能当精英房，区别只在外面的配置
            _currentRoom.Initialize(this, type);
        }

        MovePlayerToSpawn();

        RoomsCleared++;

        Debug.Log(
            $"[RoomManager] 进入第 {RoomsCleared} 间：{RoomTypeNames.Of(CurrentRoomType)}「{prefab.name}」", this);

        RoomChanged?.Invoke(RoomsCleared);
    }

    private void MovePlayerToSpawn()
    {
        ResolvePlayer();
        if (_player == null) return;

        Vector3 target = _currentRoom != null ? _currentRoom.SpawnPoint.position : AnchorPosition;

        // 有刚体时必须以刚体的坐标为准：只改 transform.position 的话，
        // 物理系统下一帧会把物体拉回它自己记录的位置，表现为「传送失败」
        if (_playerBody != null)
        {
            _playerBody.position = target;
            _playerBody.velocity = Vector2.zero;
        }

        _player.position = target;
    }

    /// <summary>
    /// 投射物不是房间的子物体，销毁房间带不走它们。
    /// 不清的话会出现「上一间的火球飞进新房间把刚出生的玩家打死」这种荒唐事。
    /// </summary>
    private void CleanupProjectiles()
    {
        var flying = FindObjectsOfType<Projectile>();
        for (int i = 0; i < flying.Length; i++)
        {
            if (flying[i] != null) Destroy(flying[i].gameObject);
        }
    }

    /// <summary>抽一间。返回候选列表的下标，抽不到返回 -1</summary>
    /// <param name="roomIndex">这是第几间房（第 1 间传 1）。房间池靠它判断哪些房间已经可以出现了</param>
    private int PickIndex(int roomIndex)
    {
        EnsureRandom();

        // ---- 新路：房间池（有权重、有类型、有出现窗口）----
        if (UsingPool)
        {
            int pick = roomPool.Pick(_rng, roomIndex, _lastPoolPick);
            if (pick < 0)
            {
                Debug.LogWarning(
                    $"[RoomManager] 房间池「{roomPool.name}」在第 {roomIndex} 间抽不到任何房间。" +
                    "检查每条的最小/最大出现序号是不是把这一间排除了。", this);
                return -1;
            }

            _lastPoolPick = pick;
            return pick;
        }

        // ---- 旧路：只有「有这几间」，没有权重和类型 ----
        int count = roomPrefabs.Count;
        if (count == 0) return -1;

        // 只有一间房，或者关掉了袋子随机 —— 那就退化成纯随机
        if (!useBagRandom || count == 1) return _rng.Next(0, count);

        if (_bag.Count == 0) RefillBag();

        int bagPick = _bag[0];
        _bag.RemoveAt(0);
        _lastIndex = bagPick;
        return bagPick;
    }

    private void RefillBag()
    {
        int count = roomPrefabs.Count;

        _bag.Clear();
        for (int i = 0; i < count; i++) _bag.Add(i);

        // Fisher-Yates 洗牌。用它而不是「每次随机挑一个」是因为这样每个房间
        // 在一轮里出现且仅出现一次，玩家的体验是「内容很丰富」而不是「怎么又是这间」
        for (int i = _bag.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(0, i + 1);
            (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
        }

        // 洗完之后如果队首恰好是上一轮的最后一间，就和队尾换一下。
        // 不换的话会出现「连着两轮抽到同一间」——袋子随机唯一的破绽
        if (count > 1 && _bag[0] == _lastIndex)
        {
            (_bag[0], _bag[count - 1]) = (_bag[count - 1], _bag[0]);
        }
    }

    private void OnValidate()
    {
        bool hasPool = roomPool != null && roomPool.Count > 0;
        bool hasLegacy = roomPrefabs != null && roomPrefabs.Count > 0;

        if (!hasPool && !hasLegacy)
        {
            Debug.LogWarning(
                "[RoomManager] 房间池和旧列表都是空的，一间房都生不出来。" +
                "推荐给 Room Pool 指定一个 RoomPool 资产。", this);
            return;
        }

        if (hasPool)
        {
            if (hasLegacy)
            {
                Debug.LogWarning(
                    "[RoomManager] 同时配了 Room Pool 和 Room Prefabs —— 会以 Room Pool 为准，" +
                    "旧列表被忽略。想用旧的就把 Room Pool 清空。", this);
            }

            return;   // 池子自身的检查在 RoomPool.OnValidate 里
        }

        for (int i = 0; i < roomPrefabs.Count; i++)
        {
            if (roomPrefabs[i] == null)
            {
                Debug.LogWarning($"[RoomManager] 旧列表第 {i + 1} 格是空的，抽到它会什么都不生成。", this);
            }
        }
    }
}
