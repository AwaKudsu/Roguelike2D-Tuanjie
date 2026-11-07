using UnityEngine;

/// <summary>
/// 大厅里的一个「角色台」：台子上站着一个职业，走近就能选。
///
/// 只放数据与零件引用，判定和动画都在 <see cref="LobbyRoom"/> 里做 ——
/// 台子多了以后，逻辑集中一处更好改。
/// </summary>
public class LobbyPedestal : MonoBehaviour
{
    [Tooltip("这个台子代表哪个职业")]
    public ClassData classData;

    [Tooltip("台子上站着的那个人（显示用，不参与战斗）")]
    public SpriteRenderer portrait;

    [Tooltip("台座本身，走近时会被顶起来一点")]
    public SpriteRenderer stand;

    [Tooltip("选中时亮起来的光圈，可以是空的")]
    public SpriteRenderer glow;

    [Tooltip("走近时台座抬多高")]
    public float raiseHeight = 0.12f;

    [Header("颜色")]
    public Color standIdle = new Color(0.24f, 0.27f, 0.34f, 1f);
    public Color standFocus = new Color(0.36f, 0.42f, 0.55f, 1f);
    public Color standChosen = new Color(0.42f, 0.36f, 0.16f, 1f);

    [Tooltip("台子上那个人的待机图（会做很慢的呼吸）")]
    public Sprite[] idleFrames;

    private float _idleTimer;
    private int _idleIndex;
    private Vector3 _standBasePosition;

    public string DisplayName => classData != null && !string.IsNullOrEmpty(classData.className) ? classData.className : "未知职业";

    private void Awake()
    {
        if (stand != null) _standBasePosition = stand.transform.localPosition;
        if (portrait == null) portrait = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>每帧由大厅调用：跟着时间轻轻上下呼吸</summary>
    public void Breathe(float deltaTime)
    {
        if (portrait == null) return;

        if (idleFrames != null && idleFrames.Length > 1)
        {
            _idleTimer += deltaTime;
            _idleIndex = Mathf.FloorToInt(_idleTimer / 0.62f) % idleFrames.Length;
            Sprite frame = idleFrames[_idleIndex];
            if (frame != null && portrait.sprite != frame) portrait.sprite = frame;
        }
    }

    /// <summary>三种状态：远处 / 走近 / 已经被选中</summary>
    public void SetState(bool focused, bool chosen)
    {
        if (stand != null)
        {
            stand.color = chosen ? standChosen : focused ? standFocus : standIdle;

            Vector3 p = _standBasePosition;
            if (focused) p.y += raiseHeight;
            stand.transform.localPosition = Vector3.Lerp(stand.transform.localPosition, p, 0.35f);
        }

        if (glow != null) glow.enabled = focused || chosen;
    }
}
