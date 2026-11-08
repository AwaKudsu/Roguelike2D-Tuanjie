using System.Collections;
using UnityEngine;

/// <summary>
/// 箭雨：在玩家身前的一片区域里，从天上往下撒一串箭。
///
/// 为什么不做成「一次性生成 9 支箭」：
///   · 同时落下来看起来像一堵箭墙，没有「雨」的节奏；
///   · 分批落才能让敌人「走进雨里」也吃伤害，技能才有地形压制感。
///
/// 实现上复用已有的 <see cref="Projectile"/>（方向向下 + 覆盖下落速度），所以箭撞地面 / 撞敌人 /
/// 穿墙判定全都不用重写；这里只负责「什么时候、在哪个 x、从多高生成」。
/// 落点用一张压扁的圆环贴图当预警（素材复用 SkillData.ringSprite）。
/// </summary>
public class ArrowRainCast : MonoBehaviour
{
    [Header("预警圈")]
    [Tooltip("落点预警压扁到多高（世界单位）。贴地的一圈，不是一个大椭圆")]
    [SerializeField] private float markerHeight = 0.7f;

    [Tooltip("预警圈的颜色")]
    [SerializeField] private Color markerColor = new Color(1f, 0.72f, 0.25f, 0.55f);

    private SkillData _skill;
    private CharacterStats _stats;
    private int _facing = 1;
    private Vector2 _center;

    private SpriteRenderer _marker;
    private Vector3 _markerBaseScale;
    private float _markerAge;
    private float _markerLife;

    private Coroutine _routine;

    /// <summary>这一轮实际放出来几支箭（自检用）</summary>
    public int SpawnedCount { get; private set; }

    /// <summary>还在下吗</summary>
    public bool IsRunning => _routine != null;

    /// <summary>落点中心，自检用</summary>
    public Vector2 Center => _center;

    public void Setup(SkillData skill, CharacterStats stats, int facing)
    {
        if (skill == null) return;

        _skill = skill;
        _stats = stats;
        _facing = facing >= 0 ? 1 : -1;
        SpawnedCount = 0;

        float distance = Mathf.Max(0f, skill.rainDistance);
        _center = (Vector2)transform.position
                + new Vector2(distance * _facing, 0f)
                - new Vector2(0f, 0.45f);   // 玩家轴心在身高一半；再往下一点，贴地面

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(RainRoutine());

        BuildMarker();
    }

    private IEnumerator RainRoutine()
    {
        int count = Mathf.Clamp(_skill.rainCount, 1, 40);
        float duration = Mathf.Max(0.1f, _skill.rainDuration);
        float gap = count > 1 ? duration / count : 0f;

        // 起手留 0.18 秒给玩家看预警圈，箭不是凭空砸下来的
        yield return new WaitForSeconds(0.18f);

        for (int i = 0; i < count; i++)
        {
            // 技能可能在中途被换房间掐断，逐帧检查一下再往下走
            if (_skill == null) yield break;

            SpawnArrow();
            if (i < count - 1) yield return new WaitForSeconds(gap);
        }

        // 等最后一支落地，再收掉预警圈
        float speed = Mathf.Max(1f, _skill.rainFallSpeed);
        yield return new WaitForSeconds(Mathf.Clamp(_skill.rainHeight / speed, 0.25f, 1.2f));

        _routine = null;
        DestroyMarker();
        Destroy(this);
    }

    private void SpawnArrow()
    {
        float radius = Mathf.Max(0.5f, _skill.rainRadius);

        // 横向随机落在区域里；纵向随高度给一点浮动，避免所有箭整整齐齐同一排下来
        float x = _center.x + Random.Range(-radius, radius);
        float height = Mathf.Max(1f, _skill.rainHeight) + Random.Range(0f, 1.5f);
        Vector2 origin = new Vector2(x, _center.y + height);

        float speed = Mathf.Max(1f, _skill.rainFallSpeed);
        float life = Mathf.Clamp(height / speed + 0.5f, 0.4f, 3f);

        int fired = ProjectileLauncher.FireDirected(
            data:             _skill.attack,
            prefab:           _skill.projectilePrefab,
            stats:            _stats,
            owner:            gameObject,
            origin:           origin,
            direction:        Vector2.down,
            count:            1,
            spreadAngle:      0f,
            speedOverride:    speed,
            lifeTimeOverride: life);

        SpawnedCount += fired;
    }

    /// <summary>
    /// 立刻收手（换房间 / 被技能层要求停下时调）。
    /// 预警圈是独立物体、不吃玩家销毁，所以必须显式回收，否则上一间房的圈会留在新房间里。
    /// </summary>
    public void StopNow()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        DestroyMarker();
        Destroy(this);
    }

    // ---------------- 落点预警 ----------------

    private void BuildMarker()
    {
        if (_skill == null || _skill.ringSprite == null) return;

        var go = new GameObject("RainMarker");
        // 不挂在玩家下面：预警圈标记的是「放技能时选定的那片地」，
        // 挂成子物体会跟着玩家跑，箭却还落在原地，看起来就像两套坐标
        go.transform.SetParent(null, false);
        go.transform.position = _center;
        go.transform.rotation = Quaternion.identity;

        _marker = go.AddComponent<SpriteRenderer>();
        _marker.sprite = _skill.ringSprite;
        _marker.color = markerColor;
        // 地面预警要压在地板之上、角色之下：20 是刀光层，画在脚上会糊住人
        _marker.sortingOrder = 1;

        float radius = Mathf.Max(0.5f, _skill.rainRadius);
        // x 是整片区域（半径×2），y 固定压扁 —— 贴地的一圈预警，不是一个大椭圆
        _markerBaseScale = new Vector3(radius * 2f, Mathf.Max(0.05f, markerHeight), 1f);
        _marker.transform.localScale = _markerBaseScale;

        _markerAge = 0f;
        _markerLife = Mathf.Max(0.4f, _skill.rainDuration + 0.6f);
    }

    private void Update()
    {
        if (_marker == null) return;

        _markerAge += Time.deltaTime;
        float t = Mathf.Clamp01(_markerAge / _markerLife);

        // 一开始快速亮起，之后慢慢淡出；同时轻微呼吸，避免看着像贴图死了
        Color c = markerColor;
        c.a = (t < 0.12f ? Mathf.Lerp(0f, markerColor.a, t / 0.12f)
                         : Mathf.Lerp(markerColor.a, 0f, (t - 0.12f) / 0.88f));
        _marker.color = c;

        float pulse = 1f + Mathf.Sin(_markerAge * 9f) * 0.02f;
        _marker.transform.localScale = new Vector3(
            _markerBaseScale.x * pulse, _markerBaseScale.y, _markerBaseScale.z);
    }

    private void DestroyMarker()
    {
        if (_marker == null) return;

#if UNITY_EDITOR
        // 编辑器自检会直接调这些方法；编辑模式里 Destroy 不生效还会报一行错
        if (!Application.isPlaying) DestroyImmediate(_marker.gameObject);
        else Destroy(_marker.gameObject);
#else
        Destroy(_marker.gameObject);
#endif

        _marker = null;
    }

    private void OnDestroy()
    {
        DestroyMarker();
    }
}
