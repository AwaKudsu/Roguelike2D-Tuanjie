using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家的 HUD：左上角的「立绘 + 血条 + 蓝条 + 技能栏」。
///
/// ★ 它每帧去「问」Health / ManaPool 现在是多少，而不是等它们发事件通知。
///   这是刻意的取舍，不是偷懒：
///     - 逻辑层（伤害结算、死亡判定、技能冷却）用事件，因为它们必须精确地「发生一次」；
///     - 显示层用轮询，因为它只是把当前状态画出来，早一帧晚一帧无所谓。
///   反过来做的代价更大：漏订阅一个事件，血条就永远不动，而且不报错。
///   本项目已经吃过一次「静默失效」的亏（LayerMask 留空），所以显示层一律轮询。
///
///   代价是每帧读两个 float，可以忽略不计。
///
/// 布局（整块是 Canvas 下的 PlayerHud 容器，锚在左上角）：
///   ┌──────┐ ██████████████████░░  122/140
///   │ 立绘 │ ████████████░░░░░░░   48/60
///   └──────┘ [K][L][U][I]
///
/// 数字和立绘都是「有就更新、没有就跳过」，所以老场景里只接了血条蓝条也能跑。
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("数据源（留空则自动找 Tag 为 Player 的对象）")]
    [SerializeField] private Health playerHealth;
    [SerializeField] private ManaPool playerMana;
    [SerializeField] private CharacterStats playerStats;

    [Header("显示")]
    [Tooltip("血条。它的 StatBar 会把 Health.Normalized 画出来")]
    [SerializeField] private StatBar healthBar;

    [Tooltip("蓝条。法师特别需要它，战士也不能没有")]
    [SerializeField] private StatBar manaBar;

    [Tooltip("左上角的职业立绘。留空就不画，但也不会报错")]
    [SerializeField] private Image portrait;

    [Tooltip("血条上的数字（122 / 140）")]
    [SerializeField] private Text healthText;

    [Tooltip("蓝条上的数字（48 / 60）")]
    [SerializeField] private Text manaText;

    [Header("规则")]
    [Tooltip("玩家死亡时把血条蓝条藏起来")]
    [SerializeField] private bool hideBarsOnDeath = true;

    private Sprite _portraitSprite;

    // 数字的缓存：字符串拼接每帧都会产生垃圾，而且 Text 每帧重设会触发 UI 重建。
    // 只在「整数变了」的时候才写 —— 血量是浮点数，但显示的是整数，所以大部分帧其实不用动
    private int _healthCur = int.MinValue;
    private int _healthMax = int.MinValue;
    private int _manaCur = int.MinValue;
    private int _manaMax = int.MinValue;

    /// <summary>自动找玩家。只在需要的时候找，避免每帧都调用 FindGameObjectWithTag</summary>
    private void ResolveReferences()
    {
        if (playerHealth != null && playerMana != null && playerStats != null) return;

        var go = GameObject.FindGameObjectWithTag("Player");
        if (go == null) return;

        if (playerHealth == null) playerHealth = go.GetComponent<Health>();
        if (playerMana == null)   playerMana   = go.GetComponent<ManaPool>();
        if (playerStats == null)  playerStats  = go.GetComponent<CharacterStats>();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        // 开局先硬同步一次，否则第一帧会闪一下 Inspector 里的占位数值
        SnapBars();
        RefreshPortrait();
        RefreshNumbers();
    }

    private void Update()
    {
        // 玩家是场景里的对象，正常不会中途消失；但换场景 / 重开时会短暂找不到，
        // 所以留一条重找的后路，免得血条从此再也不动
        if (playerHealth == null || playerMana == null) ResolveReferences();

        bool alive = playerHealth == null || playerHealth.IsAlive;

        if (healthBar != null)
        {
            if (playerHealth != null) healthBar.SetValue(playerHealth.Normalized);
            SetBarVisible(healthBar, alive || !hideBarsOnDeath);
        }

        if (manaBar != null)
        {
            if (playerMana != null) manaBar.SetValue(playerMana.Normalized);
            SetBarVisible(manaBar, alive || !hideBarsOnDeath);
        }

        RefreshNumbers();
        // 立绘每帧对一次：Start 的时候玩家还在大厅里，职业是默认那个；
        // 在大厅点「确认」才会换成法师，ClassChanged 不一定有人转告 HUD，
        // 所以这里靠「引用没变就不写」的缓存兜住，帧开销只有两次比较。
        RefreshPortrait();
        if (!alive) HideText(healthText);
    }

    private void SnapBars()
    {
        if (healthBar != null && playerHealth != null) healthBar.SnapToValue(playerHealth.Normalized);
        if (manaBar != null && playerMana != null)     manaBar.SnapToValue(playerMana.Normalized);
        SetBarVisible(healthBar, true);
        SetBarVisible(manaBar, true);
    }

    // ---------------------------------------------------------------- 立绘 / 数字

    /// <summary>
    /// 立绘只在「职业图标换了」的时候才写 sprite。
    /// 每帧给 Image.sprite 赋同一个值会触发 UI 重建，条只有两根、技能四格，
    /// 但这类浪费是纯送出去的，能免则免。
    /// </summary>
    private void RefreshPortrait()
    {
        if (portrait == null) return;

        Sprite wanted = playerStats != null && playerStats.Class != null ? playerStats.Class.icon : null;

        if (wanted != null && wanted != _portraitSprite)
        {
            _portraitSprite = wanted;
            portrait.sprite = wanted;
        }

        portrait.enabled = wanted != null;
    }

    private void RefreshNumbers()
    {
        if (healthText != null)
        {
            if (playerHealth != null)
            {
                int cur = Mathf.CeilToInt(Mathf.Max(0f, playerHealth.Current));
                int max = Mathf.CeilToInt(Mathf.Max(0f, playerHealth.Max));

                if (cur != _healthCur || max != _healthMax)
                {
                    _healthCur = cur;
                    _healthMax = max;
                    healthText.text = cur + " / " + max;
                }

                healthText.enabled = true;
            }
            else HideText(healthText);
        }

        if (manaText != null)
        {
            if (playerMana != null)
            {
                int cur = Mathf.CeilToInt(Mathf.Max(0f, playerMana.Current));
                int max = Mathf.CeilToInt(Mathf.Max(0f, playerMana.Max));

                if (cur != _manaCur || max != _manaMax)
                {
                    _manaCur = cur;
                    _manaMax = max;
                    manaText.text = cur + " / " + max;
                }

                manaText.enabled = true;
            }
            else HideText(manaText);
        }
    }

    private static void HideText(Text text)
    {
        if (text != null && text.enabled) text.enabled = false;
    }

    private static void SetBarVisible(StatBar bar, bool visible)
    {
        if (bar == null) return;
        if (bar.gameObject.activeSelf != visible) bar.gameObject.SetActive(visible);
    }
}
