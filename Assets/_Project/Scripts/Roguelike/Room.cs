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

    /// <summary>玩家进入本房间时的落点</summary>
    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

    /// <summary>本房间的门</summary>
    public Door Door => door;

    /// <summary>敌人是否已清光（门是否已开）</summary>
    public bool IsCleared { get; private set; }

    /// <summary>还活着的敌人数量。将来 HUD 显示「剩余 3」就靠它</summary>
    public int AliveEnemies { get; private set; }

    /// <summary>清光时触发一次</summary>
    public event Action<Room> Cleared;

    private readonly List<Health> _enemies = new();
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
        _manager = manager;
    }

    private void Awake()
    {
        if (door == null) door = GetComponentInChildren<Door>(true);

        // 房间里的所有敌人都订阅一遍。用 EnemyController 而不是 Health 来找，
        // 是因为可破坏的箱子、木桶也可能有 Health，但它们不该算进「清场」条件
        foreach (var enemy in GetComponentsInChildren<EnemyController>(true))
        {
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
        // 数「还活着几个敌人」必须在这里数，不能放在 Awake —— 理由见 Awake 里的注释。
        // 此刻所有物体的 Awake 都已经跑完，Health.Current 一定是 maxHealth
        RecountAlive();

        // 开局先判一次：没有敌人的房间（奖励房、走廊）应该一进来门就是开的，
        // 否则玩家会被永久关在里面 —— 这种 bug 很致命而且不容易想到
        EvaluateClear();

        // 留一行开局日志。房间这类「门为什么开了/没开」的问题，
        // 光看画面很难判断，看数字一眼就清楚
        Debug.Log($"[Room] {name}：敌人 {AliveEnemies} 个，门{(IsCleared ? "已开" : "锁着")}", this);
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
