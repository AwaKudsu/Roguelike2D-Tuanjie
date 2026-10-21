using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 房间类型。将来「打完一间出 2~3 个门让你选」就靠它区分门后是什么。
///
/// 为什么要先有这个枚举，而不是等做门的时候再加？
/// 因为房间池要按类型配权重（第 1 间不能出 Boss、第 5 间开始出精英），
/// 类型是池子的一部分，不是门的装饰。
/// </summary>
public enum RoomType
{
    /// <summary>普通战斗</summary>
    Battle,

    /// <summary>精英战斗：更硬的怪，更好的掉落</summary>
    Elite,

    /// <summary>商店</summary>
    Shop,

    /// <summary>宝箱</summary>
    Treasure,

    /// <summary>Boss 房</summary>
    Boss,
}

/// <summary>把类型翻译成中文。日志、将来的门牌 UI 都用它</summary>
public static class RoomTypeNames
{
    public static string Of(RoomType type)
    {
        switch (type)
        {
            case RoomType.Elite: return "精英";
            case RoomType.Shop: return "商店";
            case RoomType.Treasure: return "宝箱";
            case RoomType.Boss: return "BOSS";
            default: return "战斗";
        }
    }
}

/// <summary>
/// 房间池里的一条：一个预制体 + 它是什么类型 + 参数（权重 / 第几间开始出现）。
///
/// 同一个预制体可以登记多条（比如同一间房在前期权重 1、后期权重 0.3），
/// 所以「一条」不等于「一间房」。
/// </summary>
[Serializable]
public class RoomPoolEntry
{
    [Tooltip("房间预制体，必须带 Room 组件")]
    public GameObject prefab;

    [Tooltip("这一间是什么类型。房间内容（怪物强度、有没有商人）按它来")]
    public RoomType type = RoomType.Battle;

    [Tooltip("抽取权重。0 = 永不出现。建议：普通 1、精英 0.35、商店 0.2")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("最早第几间房可以出现（1 = 开局就能出现）")]
    [Min(1)] public int minRoomIndex = 1;

    [Tooltip("最晚第几间房可以出现（0 = 不限）")]
    [Min(0)] public int maxRoomIndex = 0;

    /// <summary>这一条在第 roomIndex 间房时是否允许被抽到</summary>
    public bool AllowAt(int roomIndex)
    {
        if (prefab == null) return false;
        if (weight <= 0f) return false;
        if (roomIndex < Mathf.Max(1, minRoomIndex)) return false;
        if (maxRoomIndex > 0 && roomIndex > maxRoomIndex) return false;
        return true;
    }
}

/// <summary>
/// 房间池。把「有哪些房间、什么时候能出现、各占多少权重」从代码里搬到资产里。
///
/// 为什么必须做成 ScriptableObject 而不是 Inspector 里的 List：
///   1. 一个 List 只能表达「有这几间」，表达不了权重和出现时机；
///   2. 关卡曲线要调的时候，改资产比改预制体、改场景安全得多；
///   3. 将来写单元测试可以直接 new 一个池子喂进去，不用开场景。
/// </summary>
[CreateAssetMenu(fileName = "RoomPool_", menuName = "Roguelike/Room Pool")]
public class RoomPool : ScriptableObject
{
    [SerializeField] private string displayName = "默认房间池";

    [Tooltip("房间清单。空槽位会被自动跳过")]
    [SerializeField] private List<RoomPoolEntry> entries = new();

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    public IReadOnlyList<RoomPoolEntry> Entries => entries;

    /// <summary>登记了多少条（可能包含空槽位，抽取时会跳过）</summary>
    public int Count => entries.Count;

    /// <summary>给编辑器脚本用：追加一条</summary>
    public void Add(GameObject prefab, RoomType type, float weight = 1f, int minRoomIndex = 1, int maxRoomIndex = 0)
    {
        entries.Add(new RoomPoolEntry
        {
            prefab = prefab,
            type = type,
            weight = weight,
            minRoomIndex = minRoomIndex,
            maxRoomIndex = maxRoomIndex,
        });
    }

    /// <summary>给编辑器脚本用：清空</summary>
    public void Clear()
    {
        entries.Clear();
    }

    /// <summary>
    /// 抽一间。返回 entries 的下标，抽不到返回 -1。
    /// </summary>
    /// <param name="rng">随机源。<b>必须是外面传进来的 System.Random</b>，见类注释</param>
    /// <param name="roomIndex">这是第几间房（第 1 间传 1）</param>
    /// <param name="excludeIndex">要排除的下标（用来避免连着两间一样），传 -1 表示不排除</param>
    ///
    /// ★★ 随机源为什么不能直接用 UnityEngine.Random？
    ///    它是全局静态状态，同一个种子跑两次结果不一样 ——
    ///    这样关卡就没法复现，出了 bug 没法复现，也写不了单元测试。
    ///    验收「给定种子生成的关卡是否一致」全靠这一条。
    public int Pick(System.Random rng, int roomIndex, int excludeIndex = -1)
    {
        if (rng == null) rng = new System.Random();

        int pick = WeightedPick(rng, roomIndex, excludeIndex);

        // 排除之后一个合法项都不剩（比如池子里只有一间房），
        // 就退回「允许重复」，否则玩家会被永远卡在原地
        if (pick < 0 && excludeIndex >= 0) pick = WeightedPick(rng, roomIndex, -1);

        return pick;
    }

    /// <summary>按权重抽一次。权重为 0 / 不满足出现条件 / 被排除的条目不参与</summary>
    private int WeightedPick(System.Random rng, int roomIndex, int excludeIndex)
    {
        float total = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            if (i == excludeIndex) continue;
            if (!entries[i].AllowAt(roomIndex)) continue;
            total += entries[i].weight;
        }

        if (total <= 0f) return -1;

        // NextDouble() 返回 [0,1)，乘上总权重就是在权重轴上取一点
        double roll = rng.NextDouble() * total;

        for (int i = 0; i < entries.Count; i++)
        {
            if (i == excludeIndex) continue;
            if (!entries[i].AllowAt(roomIndex)) continue;

            roll -= entries[i].weight;
            if (roll <= 0.0) return i;
        }

        // 浮点累加误差的兜底：往回找最后一个合法项，保证一定有结果
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (i == excludeIndex) continue;
            if (entries[i].AllowAt(roomIndex)) return i;
        }

        return -1;
    }

    private void OnValidate()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;

            if (e.prefab == null)
            {
                Debug.LogWarning($"[RoomPool] {name} 第 {i + 1} 条没有预制体，抽到它会什么都不生成。", this);
            }
            else if (e.prefab.GetComponent<Room>() == null)
            {
                Debug.LogWarning(
                    $"[RoomPool] {name} 第 {i + 1} 条「{e.prefab.name}」上没有 Room 组件，" +
                    "出生点和门都不会生效。", this);
            }

            if (e.type != RoomType.Battle && e.minRoomIndex <= 1)
            {
                Debug.LogWarning(
                    $"[RoomPool] {name} 第 {i + 1} 条是「{RoomTypeNames.Of(e.type)}」但最早第 1 间就能出现，" +
                    "确认这是你想要的（Boss 一般要往后挪）。", this);
            }
        }
    }
}
