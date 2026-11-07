using UnityEngine;

/// <summary>
/// 战吼的表现层。
///
/// 三件事，按时间顺序：
///   1. 头顶亮出一把巨剑（0.30 秒内从 0.35 倍涨到 0.75 倍，然后淡出）
///   2. 以玩家为圆心炸出一圈冲击波（0.45 秒内从贴身处扩到 fearRadius，一边扩一边淡）
///   3. 冲击波扫到一半时把半径内的敌人全部吓住（EnemyController.ApplyFear）
///
/// 为什么「吓人」要放在扫到一半、而不是按下就生效：
/// 视觉上冲击波还在往外走，敌人就已经开始跑了 —— 顺序反了会显得「圆环只是贴图」。
///
/// 美术资源从 SkillData 上取（ringSprite / swordSprite），没配就只放逻辑、不放图。
/// </summary>
public class WarCryEffect : MonoBehaviour
{
    private SkillData _skill;
    private SpriteRenderer _ring;
    private SpriteRenderer _ringInner;
    private SpriteRenderer _sword;

    private float _age;
    private bool _applied;
    private float _life;

    private readonly int _ringSortingOrder = SwordTrail.SortingOrder + 1;
    private readonly int _swordSortingOrder = SwordTrail.SortingOrder + 2;

    /// <summary>吓到几个敌人（自检用）</summary>
    public int FrightenedCount { get; private set; }

    public void Setup(SkillData skill, Vector2 origin)
    {
        _skill = skill;
        _age = 0f;
        _applied = false;
        FrightenedCount = 0;

        float ringDuration = skill != null ? Mathf.Max(0.1f, skill.ringDuration) : 0.45f;
        _life = Mathf.Max(ringDuration + 0.05f, 0.55f);

        transform.position = origin;

        if (skill != null && skill.ringSprite != null)
        {
            // 两圈：外圈大而慢（红色冲击波）、内圈小而快（白热的核心），
            // 一圈看不出「往外推」的层次，看起来只是贴图在放大
            _ring = CreateRing("ring", skill.ringSprite, new Color(1f, 0.55f, 0.28f, 0.95f), _ringSortingOrder);
            _ringInner = CreateRing("ringInner", skill.ringSprite, new Color(1f, 0.95f, 0.8f, 0.9f), _ringSortingOrder + 1);
        }

        if (skill != null && skill.swordSprite != null)
        {
            var go = new GameObject("sword");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            _sword = go.AddComponent<SpriteRenderer>();
            _sword.sprite = skill.swordSprite;
            _sword.color = Color.white;
            _sword.sortingOrder = _swordSortingOrder;
        }
    }

    private void Update()
    {
        _age += Time.deltaTime;
        AnimateSword();
        AnimateRing();

        if (!_applied)
        {
            float ringDuration = _skill != null ? Mathf.Max(0.1f, _skill.ringDuration) : 0.45f;
            if (_age >= ringDuration * 0.45f)
            {
                _applied = true;
                ApplyFear();
            }
        }

        if (_age >= _life) Destroy(this);   // 只摘组件，玩家不动
    }

    /// <summary>
    /// 立刻收手（换房间时用）。组件本身挂在玩家身上，
    /// 只 Destroy(this) 会把 ring / ringInner / sword 三个子物体留在玩家身上。
    /// </summary>
    public void StopNow()
    {
        CleanupChildren();
        Destroy(this);
    }

    private void OnDestroy()
    {
        CleanupChildren();
    }

    private void CleanupChildren()
    {
        if (_ring != null) Destroy(_ring.gameObject);
        if (_ringInner != null) Destroy(_ringInner.gameObject);
        if (_sword != null) Destroy(_sword.gameObject);
    }

    private void AnimateSword()
    {
        if (_sword == null) return;

        const float rise = 0.30f;
        float t = Mathf.Clamp01(_age / rise);
        float eased = 1f - (1f - t) * (1f - t);

        float scale = Mathf.Lerp(0.35f, 0.75f, eased);
        _sword.transform.localScale = new Vector3(scale, scale, 1f);
        _sword.transform.localPosition = new Vector3(0f, Mathf.Lerp(0.6f, 1.6f, eased), 0f);

        Color c = _sword.color;
        // 前 60% 不透明，之后淡出 —— 剑要「立住」一下才看得清
        c.a = _age < rise * 0.6f ? 1f : Mathf.Clamp01(1f - (_age - rise * 0.6f) / (rise * 0.9f));
        _sword.color = c;
    }

    private SpriteRenderer CreateRing(string name, Sprite sprite, Color color, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private void AnimateRing()
    {
        if (_ring == null) return;

        float ringDuration = _skill != null ? Mathf.Max(0.1f, _skill.ringDuration) : 0.45f;
        float radius = _skill != null ? Mathf.Max(1f, _skill.fearRadius) : 12f;

        float t = Mathf.Clamp01(_age / ringDuration);

        // 圆环贴图是 1 单位直径（128px @ 128PPU），所以要扩到 2 倍半径
        float diameter = Mathf.Lerp(0.8f, radius * 2f, t * t);   // 一开始涨得慢、后来一下子铺开
        _ring.transform.localScale = new Vector3(diameter, diameter, 1f);
        _ring.transform.Rotate(0f, 0f, 60f * Time.deltaTime);    // 慢慢转，尖刺才不会像贴纸

        Color c = _ring.color;
        c.a = 0.95f * (1f - t * t);
        _ring.color = c;

        if (_ringInner == null) return;

        // 内圈跑前半程的 60%，比外圈快、比外圈小，像冲击波前面那层白热
        float tInner = Mathf.Clamp01(t * 1.6f);
        float innerDiameter = Mathf.Lerp(0.6f, radius * 2f * 0.55f, tInner * tInner);
        _ringInner.transform.localScale = new Vector3(innerDiameter, innerDiameter, 1f);

        Color ci = _ringInner.color;
        ci.a = 0.9f * (1f - tInner) * (1f - tInner);
        _ringInner.color = ci;
    }

    private void ApplyFear()
    {
        if (_skill == null) return;

        float radius = Mathf.Max(1f, _skill.fearRadius);
        float duration = Mathf.Max(0.2f, _skill.fearDuration);
        Vector2 origin = transform.position;

        var enemies = FindObjectsOfType<EnemyController>();
        int count = 0;

        foreach (var enemy in enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled) continue;
            if (!enemy.IsAlive) continue;
            if (Vector2.Distance(origin, enemy.transform.position) > radius) continue;

            enemy.ApplyFear(duration, origin);
            count++;
        }

        FrightenedCount = count;
        Debug.Log($"[WarCry] 战吼吓到 {count} 个敌人（半径 {radius:0.#}，恐慌 {duration:0.#} 秒）");
    }
}
