/// <summary>
/// 角色动画的统一入口。技能层只认这个接口，不认具体是战士还是法师。
///
/// 为什么要接口：`SkillCaster` 里旋风斩要「转起来」、战吼要「举剑」、法师要「举杖施法」，
/// 但如果它直接 `GetComponent&lt;WarriorVisual&gt;()`，换成法师就什么都播不出来。
/// 让两套表现层各自实现这个接口，技能层就不用知道玩家现在是哪个职业。
/// </summary>
public interface IPlayerAnim
{
    /// <summary>持续型攻击（旋风斩）：duration 秒内保持旋转/挥扫的姿态</summary>
    void PlaySpin(float duration);

    /// <summary>战吼这类「举起来吼一声」的姿态，hold 秒后回到待机</summary>
    void PlayWarcry(float hold);

    /// <summary>蓄力中开/关。法师进入蓄力姿态，战士没有这回事所以空实现</summary>
    void PlayCharge(bool on);

    /// <summary>施法姿态（法师回血举杖），hold 秒</summary>
    void PlayCast(float hold);

    /// <summary>
    /// 立刻掐断正在播的动作，回到待机第一帧。
    ///
    /// 动作序列（三段连击、旋风斩、施法、受击）都是有倒计时的「一次性姿态」，
    /// 而房间切换要淡出淡入几百毫秒 —— 这期间动作还在走，进了新房间人就会
    /// 带着上一间的挥砍姿势站着（看起来像「还在攻击」）。换房间时清一次即可。
    /// </summary>
    void CancelAction();
}
