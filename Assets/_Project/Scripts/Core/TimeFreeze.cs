using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 时间冻结的仲裁中心。
///
/// 游戏里有不止一处需要「把游戏停住」：
///   · 按 Esc 打开暂停菜单
///   · 打完一间房弹三选一
///   · （将来）顿帧、过场、死亡特写
///
/// 如果大家都直接写 Time.timeScale = 0 / = 1，后关的那一方会覆盖前一方：
/// 典型症状是「关掉三选一之后游戏还在暂停」，或者「关掉暂停菜单后三选一被解除冻结」。
///
/// 所以统一走这里：谁要冻结就登记一个 owner，解除时只撤销自己的登记；
/// 只有登记清空了，才真的把 timeScale 还原成冻结前的值。
/// owner 建议传 this（界面组件），这样同一个界面重复调用也不会叠加。
/// </summary>
public static class TimeFreeze
{
    private static readonly HashSet<object> Owners = new HashSet<object>();

    /// <summary>冻结前的时间倍速。只在「从没冻结变成冻结」的那一刻记录一次</summary>
    private static float _restoreScale = 1f;

    /// <summary>当前是否处于冻结状态</summary>
    public static bool IsFrozen => Owners.Count > 0;

    /// <summary>当前有几个 owner 在冻结（排查问题时打印用）</summary>
    public static int OwnerCount => Owners.Count;

    /// <summary>登记一次冻结。同一个 owner 重复调用只算一次</summary>
    public static void Freeze(object owner)
    {
        if (owner == null) return;

        if (Owners.Count == 0)
        {
            // 万一之前有人把倍速调成 0，别把它当成「要还原的值」
            _restoreScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        }

        Owners.Add(owner);
        Time.timeScale = 0f;
    }

    /// <summary>撤销自己的冻结登记。最后一个 owner 撤销后才真正还原倍速</summary>
    public static void Unfreeze(object owner)
    {
        if (owner == null) return;
        if (!Owners.Remove(owner)) return;

        if (Owners.Count == 0) Time.timeScale = _restoreScale;
    }

    /// <summary>
    /// 强制清空所有登记并还原倍速。
    /// 切场景前调用，防止「上一个场景里的界面已经被销毁、登记却还留着」把新场景冻死。
    /// </summary>
    public static void UnfreezeAll()
    {
        Owners.Clear();
        _restoreScale = 1f;
        Time.timeScale = 1f;
    }
}
