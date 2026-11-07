using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 元气骑士式的等待大厅：一间能走动的房间，走到角色台前按 E 选职业，走到传送门按 E 出发。
///
/// ★ 为什么不是全屏卡片那种覆盖层？
///   卡片层能选职业，但玩家在「还没开打」的时候只能看不能动，进游戏像按了两次按钮。
///   元气骑士的做法是把这一步做成场景里的一间房：可以先走两步、看清每个角色长什么样，
///   选完往门口一走就开局。所以这里真的在 `Game.unity` 左侧搭了一间房，进大厅时把玩家挪过去。
///
/// ★ 为什么大厅和关卡放在同一个场景里？
///   玩家（Player）是场景里的物体、不是预制体。另开一个场景就得复制一份玩家，
///   两份之后必然走样（改了战士忘了改法师）。同场景里换位置是最省事也最不容易出错的。
/// </summary>
[DisallowMultipleComponent]
public class LobbyRoom : MonoBehaviour
{
    [Header("位置")]
    [Tooltip("进大厅时玩家站在这里")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("出发后玩家被放到这里（关卡出生点）")]
    [SerializeField] private Transform runSpawn;

    [Tooltip("大厅里可以选的角色台")]
    [SerializeField] private LobbyPedestal[] pedestals = new LobbyPedestal[0];

    [Tooltip("走到这里按 E 出发")]
    [SerializeField] private Transform portal;

    [Header("手感")]
    [Tooltip("离台子/门多近算「走到跟前」")]
    [SerializeField] private float interactRadius = 1.5f;

    [Tooltip("进大厅时把玩家放到出生点的偏移")]
    [SerializeField] private Vector3 spawnOffset = Vector3.zero;

    [Header("门面")]
    [Tooltip("传送门里那块发光板，选好角色后会更亮")]
    [SerializeField] private SpriteRenderer portalGlow;

    [Tooltip("没选角色时的门颜色")]
    [SerializeField] private Color portalLockedColor = new Color(0.35f, 0.38f, 0.45f, 0.9f);

    [Tooltip("选好角色后的门颜色")]
    [SerializeField] private Color portalReadyColor = new Color(1f, 0.78f, 0.32f, 0.95f);

    private Transform _player;
    private PlayerVisualSwitcher _switcher;
    private RoomManager _rooms;
    private PlayerAttacker _attacker;
    private SkillCaster _caster;
    private Canvas _canvas;
    private Text _hintText;
    private Text _nameText;
    private int _focusIndex = -1;
    private bool _portalFocused;
    private bool _inside;
    private bool _chosen;

    /// <summary>现在正站在大厅里（战斗系统应该停着）</summary>
    public bool IsInside => _inside;

    /// <summary>已经选好职业，可以出发</summary>
    public bool HasChosen => _chosen;

    /// <summary>当前被走近的角色台下标，没有就是 -1</summary>
    public int FocusIndex => _focusIndex;

    /// <summary>是否站在传送门前</summary>
    public bool PortalFocused => _portalFocused;

    /// <summary>大厅里的台子（自检用）</summary>
    public IReadOnlyList<LobbyPedestal> Pedestals => pedestals;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (_player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) _player = go.transform;
        }

        if (_switcher == null && _player != null) _switcher = _player.GetComponent<PlayerVisualSwitcher>();
        if (_attacker == null && _player != null) _attacker = _player.GetComponent<PlayerAttacker>();
        if (_caster == null && _player != null) _caster = _player.GetComponent<SkillCaster>();
        if (_rooms == null) _rooms = FindObjectOfType<RoomManager>();
    }

    // ---------------------------------------------------------------- 进出

    /// <summary>进大厅：把人挪进来、把战斗停掉、让 RoomManager 别建房</summary>
    public void Enter()
    {
        ResolveReferences();
        _inside = true;
        _chosen = false;

        if (_rooms != null) _rooms.SuspendForLobby();

        if (_attacker != null) _attacker.enabled = false;
        if (_caster != null) _caster.enabled = false;

        Teleport(_player, spawnPoint != null ? spawnPoint.position + spawnOffset : transform.position);

        BuildHud();
        RefreshHud("走近角色台按 E 选职业");
    }

    /// <summary>出发：恢复战斗、把玩家放到关卡里、让 RoomManager 建第一间</summary>
    public void Leave()
    {
        _inside = false;

        if (_attacker != null) _attacker.enabled = true;
        if (_caster != null) _caster.enabled = true;

        if (_rooms != null) _rooms.StartRunFromLobby();

        Teleport(_player, runSpawn != null ? runSpawn.position : Vector3.zero);

        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    private static void Teleport(Transform target, Vector3 position)
    {
        if (target == null) return;

        Rigidbody2D body = target.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = position;
            body.velocity = Vector2.zero;
        }

        target.position = position;
    }

    // ---------------------------------------------------------------- 每帧

    private void Update()
    {
        if (!_inside) return;

        float dt = Time.deltaTime;
        Vector3 playerPosition = _player != null ? _player.position : Vector3.zero;

        // 谁离得最近：先找台子，都没有再看门
        int nearest = -1;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < pedestals.Length; i++)
        {
            LobbyPedestal pedestal = pedestals[i];
            if (pedestal == null) continue;

            if (pedestal.portrait != null) pedestal.Breathe(dt);

            float distance = Vector2.Distance(playerPosition, pedestal.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = distance <= interactRadius ? i : -1;
            }
        }

        _focusIndex = nearest;

        float portalDistance = portal != null ? Vector2.Distance(playerPosition, portal.position) : float.MaxValue;
        _portalFocused = portal == null || (portalDistance <= interactRadius && nearest < 0);

        for (int i = 0; i < pedestals.Length; i++)
        {
            LobbyPedestal pedestal = pedestals[i];
            if (pedestal == null) continue;
            pedestal.SetState(i == _focusIndex, IsChosenPedestal(pedestal));
        }

        UpdatePortalGlow();
        UpdateHint();

        if (InteractPressed()) Interact();
    }

    /// <summary>
    /// 按 E 交互。这里读新 Input System 的键盘，不能用老的 `Input.GetKeyDown`：
    /// 工程的 Active Input Handling 是「Input System Package」，老接口一调就抛
    /// `InvalidOperationException`，大厅直接卡死在第一帧。
    /// 批处理里没有键盘（`Keyboard.current == null`），也要能安全地返回 false。
    /// </summary>
    private static bool InteractPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.eKey.wasPressedThisFrame;
    }

    private void Interact()
    {
        if (_focusIndex >= 0)
        {
            Select(_focusIndex);
            return;
        }

        if (_portalFocused)
        {
            if (!_chosen && pedestals.Length > 0)
            {
                // 没选就进门：默认第一个，省得玩家卡住
                Select(0);
            }

            GameBootstrap.StartRun();
        }
    }

    /// <summary>选中某个台子上的职业（也会立刻把玩家形象换过去，看得见反馈）</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= pedestals.Length) return;

        LobbyPedestal pedestal = pedestals[index];
        if (pedestal == null || pedestal.classData == null) return;

        if (_switcher != null) _switcher.SetClass(pedestal.classData);

        _chosen = true;
        RefreshHud($"已选择 {pedestal.DisplayName} · 走到右边的门按 E 出发");
    }

    private bool IsChosenPedestal(LobbyPedestal pedestal)
    {
        if (!_chosen || _switcher == null || pedestal == null) return false;
        return pedestal.classData != null && _switcher.CurrentClassName == pedestal.DisplayName;
    }

    private void UpdatePortalGlow()
    {
        if (portalGlow == null) return;

        Color target = _chosen ? portalReadyColor : portalLockedColor;
        if (_portalFocused)
        {
            target = Color.Lerp(target, Color.white, 0.35f);
        }

        portalGlow.color = Color.Lerp(portalGlow.color, target, 0.25f);
    }

    private void UpdateHint()
    {
        if (_hintText == null) return;

        if (_focusIndex >= 0)
        {
            LobbyPedestal pedestal = pedestals[_focusIndex];
            RefreshHud($"按 E 选择 {pedestal.DisplayName}");
        }
        else if (_portalFocused)
        {
            RefreshHud(_chosen ? "按 E 出发" : "按 E 出发（没选就用第一个角色）");
        }
        else
        {
            RefreshHud("A / D 走动 · 走到角色台前按 E");
        }
    }

    // ---------------------------------------------------------------- 界面

    private void BuildHud()
    {
        if (_canvas != null)
        {
            _canvas.gameObject.SetActive(true);
            return;
        }

        var root = new GameObject("LobbyHud", typeof(RectTransform));
        root.transform.SetParent(transform, false);

        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 360;   // 压过 HUD（0）和词条面板（300），低于暂停菜单（400）

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();

        _nameText = MakeText(root.transform, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -70f),
                             new Vector2(1200f, 90f), 52, TextAnchor.MiddleCenter, new Color(1f, 0.94f, 0.8f, 1f));
        _nameText.text = "选择角色";

        _hintText = MakeText(root.transform, "Hint", new Vector2(0.5f, 0f), new Vector2(0f, 80f),
                             new Vector2(1400f, 70f), 34, TextAnchor.MiddleCenter, new Color(0.92f, 0.94f, 1f, 0.95f));
    }

    private void RefreshHud(string hint)
    {
        if (_hintText != null) _hintText.text = hint;
    }

    private static Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, int fontSize, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        Text text = go.AddComponent<Text>();
        text.font = UiFont();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = string.Empty;
        return text;
    }

    private static Font _uiFont;

    private static Font UiFont()
    {
        if (_uiFont != null) return _uiFont;

        _uiFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "PingFang SC", "Arial" }, 32);

        if (_uiFont == null)
        {
            _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Debug.LogWarning("[LobbyRoom] 没找到系统中文字体，退回内置字体（中文会显示成方块）");
        }

        return _uiFont;
    }
}
