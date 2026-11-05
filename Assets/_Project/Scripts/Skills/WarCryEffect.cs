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
            var go = new GameObject("ring");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _ring = go.AddComponent<SpriteRenderer>();
            _ring.sprite = skill.ringSprite;
            _ring.color = new Color(1f, 0.86f, 0.45f, 0.9f);
            _ring.sortingOrder = _ringSortingOrder;
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

    private void AnimateRing()
    {
        if (_ring == null) return;

        float ringDuration = _skill != null ? Mathf.Max(0.1f, _skill.ringDuration) : 0.45f;
        float radius = _skill != null ? Mathf.Max(1f, _skill.fearRadius) : 12f;

        float t = Mathf.Clamp01(_age / ringDuration);

        // 圆环贴图是 1 单位直径（128px @ 128PPU），所以要扩到 2 倍半径
        float diameter = Mathf.Lerp(0.8f, radius * 2f, t * t);   // 一开始涨得慢、后来一下子铺开
        _ring.transform.localScale = new Vector3(diameter, diameter, 1f);

        Color c = _ring.color;
        c.a = 0.95f * (1f - t * t);
        _ring.color = c;
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
