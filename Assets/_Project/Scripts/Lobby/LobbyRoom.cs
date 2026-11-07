using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 元气骑士式的等待大厅：一间**俯视**的暗房间。用鼠标点角色 → 点「确认」→ 人物才能走 →
/// 走到门口开始游戏（出门之后才是横板关卡）。
///
/// ★ 为什么大厅是俯视、关卡是横板？
///   元气骑士的大厅本来就是俯视走动的房间，而这一作关卡是横板动作。
///   同一个玩家对象要能两种视角切换，所以大厅期间把横板控制器（重力/跳跃/冲刺）
///   整个关掉，换成 <see cref="LobbyAvatar"/> 的四方向走路；出发时再换回去。
///
/// ★ 为什么大厅不新开场景？
///   玩家是场景里的物体、不是预制体。另开场景要么复制一份玩家（两份以后必然走样），
///   要么先做成预制体（改动面大）。同一场景左边搭一间房，换位置最省事。
/// </summary>
[DisallowMultipleComponent]
public class LobbyRoom : MonoBehaviour
{
    private enum Stage
    {
        Selecting,   // 还没确认角色：人不能动
        Walking,     // 已确认：在大厅里自由走动，走到门口开局
        Left         // 已经出发
    }

    [Header("位置")]
    [Tooltip("进大厅时玩家站在这里（俯视房间的正中偏下）")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("出发后玩家被放到这里（关卡出生点）")]
    [SerializeField] private Transform runSpawn;

    [Tooltip("大厅里的角色立牌")]
    [SerializeField] private LobbyPedestal[] pedestals = new LobbyPedestal[0];

    [Tooltip("大厅门口：走到这里就能开始游戏")]
    [SerializeField] private Transform portal;

    [SerializeField] private SpriteRenderer portalGlow;

    [Header("交互")]
    [Tooltip("鼠标点角色时，离脚底多近算点中")]
    [SerializeField] private float pickRadius = 1.1f;

    [Tooltip("走到门口多近算「到门口了」")]
    [SerializeField] private float doorRadius = 1.3f;

    [Tooltip("站到门口自动进门要等多久（给玩家反应时间；按 E 可以立刻进）")]
    [SerializeField] private float autoEnterDelay = 0.8f;

    [Header("视角")]
    [Tooltip("大厅用多大的正交视野（比关卡大一点，能看见整间房）")]
    [SerializeField] private float lobbyCameraSize = 6f;

    [Tooltip("房间内部尺寸（宽 x 高）。相机按它算取景，保证整间房都在画面里")]
    [SerializeField] private Vector2 roomSize = new Vector2(20f, 10f);

    [Tooltip("取景时四周留多少空")]
    [SerializeField] private float cameraMargin = 1.2f;

    [Header("门口的颜色")]
    [SerializeField] private Color portalLockedColor = new Color(0.30f, 0.33f, 0.40f, 0.9f);
    [SerializeField] private Color portalReadyColor = new Color(1f, 0.80f, 0.34f, 0.95f);

    private Transform _player;
    private Rigidbody2D _body;
    private SpriteRenderer _playerSprite;
    private PlayerController _controller;
    private PlayerInputReader _input;
    private PlayerAttacker _attacker;
    private SkillCaster _caster;
    private LobbyAvatar _avatar;
    private PlayerVisualSwitcher _switcher;
    private RoomManager _rooms;
    private Camera _camera;
    private float _cameraSizeBefore;
    private Transform _cameraParentBefore;
    private Vector3 _cameraLocalBefore;
    private Vector3 _cameraPositionBefore;
    private Canvas _hudCanvas;
    private bool _hudWasEnabled;

    private Canvas _canvas;
    private Text _titleText;
    private Text _hintText;
    private GameObject _panel;
    private Text _panelName;
    private Text _panelDesc;
    private LobbyPedestal _hovered;
    private LobbyPedestal _picked;

    private Stage _stage = Stage.Left;
    private float _doorTimer;

    public bool IsInside => _stage != Stage.Left;
    public bool IsSelecting => _stage == Stage.Selecting;
    public bool IsConfirmed => _stage == Stage.Walking;
    public bool IsNearDoor { get; private set; }
    public int PickedIndex { get; private set; } = -1;
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
            if (go != null)
            {
                _player = go.transform;
                _body = go.GetComponent<Rigidbody2D>();
                _playerSprite = go.GetComponent<SpriteRenderer>();
                _controller = go.GetComponent<PlayerController>();
                _input = go.GetComponent<PlayerInputReader>();
                _attacker = go.GetComponent<PlayerAttacker>();
                _caster = go.GetComponent<SkillCaster>();
                _switcher = go.GetComponent<PlayerVisualSwitcher>();
                _avatar = go.GetComponent<LobbyAvatar>();
                if (_avatar == null) _avatar = go.AddComponent<LobbyAvatar>();
            }
        }

        if (_rooms == null) _rooms = FindObjectOfType<RoomManager>();
        if (_camera == null) _camera = Camera.main;
    }

    // ---------------------------------------------------------------- 进出

    /// <summary>进大厅：把玩家放进暗房间、停掉所有战斗能力、等玩家用鼠标挑人</summary>
    public void Enter()
    {
        ResolveReferences();
        _stage = Stage.Selecting;
        _doorTimer = 0f;
        _picked = null;
        PickedIndex = -1;

        if (_rooms != null) _rooms.SuspendForLobby();

        if (_controller != null) _controller.enabled = false;
        if (_attacker != null) _attacker.enabled = false;
        if (_caster != null) _caster.enabled = false;
        if (_avatar != null) _avatar.SetActive(false);

        if (_body != null)
        {
            _body.velocity = Vector2.zero;
            _body.gravityScale = 0f;
        }

        // 还没选人：先把玩家藏起来，选完再出现 —— 否则会和立牌站在一起，看不出在挑谁
        if (_playerSprite != null) _playerSprite.enabled = false;

        Teleport(spawnPoint != null ? spawnPoint.position : transform.position);

        EnterCamera();
        SetHudVisible(false);

        BuildHud();
        SetPanelVisible(false);
        RefreshHint("用鼠标点一个角色，再点「确认」");
        if (_titleText != null) _titleText.text = "选择角色";
    }

    /// <summary>出发：恢复横板控制器与战斗、把玩家放到关卡里、让 RoomManager 建第一间</summary>
    public void Leave()
    {
        if (_stage == Stage.Left) return;
        _stage = Stage.Left;

        if (_avatar != null) _avatar.SetActive(false);
        if (_controller != null) _controller.enabled = true;
        if (_attacker != null) _attacker.enabled = true;
        if (_caster != null) _caster.enabled = true;

        if (_playerSprite != null)
        {
            _playerSprite.enabled = true;
            _playerSprite.sortingOrder = 0;
        }

        ExitCamera();
        SetHudVisible(true);

        if (_rooms != null) _rooms.StartRunFromLobby();

        Teleport(runSpawn != null ? runSpawn.position : Vector3.zero);

        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    private void Teleport(Vector3 position)
    {
        if (_player == null) return;

        if (_body != null)
        {
            _body.position = position;
            _body.velocity = Vector2.zero;
            _body.gravityScale = 0f;
        }

        _player.position = position;
    }

    // ---------------------------------------------------------------- 相机 / HUD

    /// <summary>
    /// 大厅取景：相机原本挂在玩家身上（横板时一直跟着人），但大厅要「看见整间房」，
    /// 跟着站在门边的玩家就会把上半间房顶出画面。所以这里把相机摘下来放到房间正中，
    /// 再按房间尺寸和当前窗口比例算正交大小 —— 宽屏按宽度算、窄屏按高度算，两边都装得下。
    /// </summary>
    private void EnterCamera()
    {
        if (_camera == null) return;

        _cameraSizeBefore = _camera.orthographicSize;
        _cameraPositionBefore = _camera.transform.position;
        _cameraParentBefore = _camera.transform.parent;
        _cameraLocalBefore = _camera.transform.localPosition;

        Vector3 roomCenter = transform.position + new Vector3(roomSize.x * 0.5f, roomSize.y * 0.5f, 0f);
        _camera.transform.SetParent(null, true);
        _camera.transform.position = new Vector3(roomCenter.x, roomCenter.y, _cameraPositionBefore.z);

        float halfWidth = roomSize.x * 0.5f + cameraMargin;
        float halfHeight = roomSize.y * 0.5f + cameraMargin;
        float aspect = Mathf.Max(0.5f, _camera.aspect);

        _camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect, 0.1f);
        lobbyCameraSize = _camera.orthographicSize;
    }

    private void ExitCamera()
    {
        if (_camera == null) return;

        _camera.orthographicSize = _cameraSizeBefore > 0.1f ? _cameraSizeBefore : 5f;

        if (_cameraParentBefore != null)
        {
            _camera.transform.SetParent(_cameraParentBefore, false);
            _camera.transform.localPosition = _cameraLocalBefore;
        }
        else
        {
            _camera.transform.position = _cameraPositionBefore;
        }
    }

    /// <summary>
    /// 大厅里不显示血条 / 蓝条 / 技能栏：那套 HUD 的坐标是按关卡算的，
    /// 挂在大厅画面上既没用又挡视线（而且大厅里根本不会掉血）。
    /// </summary>
    private void SetHudVisible(bool visible)
    {
        if (_hudCanvas == null)
        {
            HUDController hud = FindObjectOfType<HUDController>();
            if (hud != null) _hudCanvas = hud.GetComponentInParent<Canvas>();
            if (_hudCanvas == null) _hudCanvas = FindHudCanvas();
            if (_hudCanvas == null) return;

            _hudWasEnabled = _hudCanvas.enabled;
        }

        _hudCanvas.enabled = visible && _hudWasEnabled;
    }

    private static Canvas FindHudCanvas()
    {
        foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (canvas == null || !canvas.gameObject.scene.IsValid()) continue;
            if (canvas.GetComponentInParent<LobbyRoom>() != null) continue;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (canvas.GetComponentInChildren<StatBar>(true) != null) return canvas;
        }

        return null;
    }

    // ---------------------------------------------------------------- 每帧

    private void Update()
    {
        if (_stage == Stage.Left) return;

        float dt = Time.deltaTime;

        foreach (LobbyPedestal pedestal in pedestals)
        {
            if (pedestal != null) pedestal.Breathe(dt);
        }

        if (_stage == Stage.Selecting) UpdateSelecting();
        else UpdateWalking(dt);
    }

    private void UpdateSelecting()
    {
        Vector2 mouse = MouseWorld();

        _hovered = null;
        for (int i = 0; i < pedestals.Length; i++)
        {
            LobbyPedestal pedestal = pedestals[i];
            if (pedestal == null) continue;
            if (pedestal.HitTest(mouse, pickRadius))
            {
                _hovered = pedestal;
                break;
            }
        }

        for (int i = 0; i < pedestals.Length; i++)
        {
            LobbyPedestal pedestal = pedestals[i];
            if (pedestal == null) continue;
            pedestal.SetState(pedestal == _hovered, pedestal == _picked);
        }

        bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (click && !PointerOverUI())
        {
            if (_hovered != null) Pick(IndexOf(_hovered));
        }

        UpdatePortalGlow();
    }

    private void UpdateWalking(float dt)
    {
        Vector3 playerPosition = _player != null ? _player.position : Vector3.zero;
        float distance = portal != null ? Vector2.Distance(playerPosition, portal.position) : float.MaxValue;

        IsNearDoor = distance <= doorRadius;

        for (int i = 0; i < pedestals.Length; i++)
        {
            if (pedestals[i] != null) pedestals[i].SetState(false, pedestals[i] == _picked);
        }

        UpdatePortalGlow();

        if (!IsNearDoor)
        {
            _doorTimer = 0f;
            RefreshHint("走到门口开始游戏");
            return;
        }

        _doorTimer += dt;
        RefreshHint(IsStartPressed() || _doorTimer >= autoEnterDelay ? "进门！" : "到了，按 E 或稍等一下");

        if (IsStartPressed() || _doorTimer >= autoEnterDelay)
        {
            GameBootstrap.StartRun();
        }
    }

    // ---------------------------------------------------------------- 选人

    /// <summary>选中第 index 个立牌（只是选中，还没确认）</summary>
    public void Pick(int index)
    {
        if (index < 0 || index >= pedestals.Length) return;

        LobbyPedestal pedestal = pedestals[index];
        if (pedestal == null || pedestal.classData == null) return;

        _picked = pedestal;
        PickedIndex = index;

        if (_panelName != null) _panelName.text = pedestal.DisplayName;
        if (_panelDesc != null)
        {
            _panelDesc.text = string.IsNullOrEmpty(pedestal.classData.description)
                ? "（这个职业还没写介绍）"
                : pedestal.classData.description;
        }

        SetPanelVisible(true);
        RefreshHint("点「确认」就用 " + pedestal.DisplayName + " 开局");
    }

    /// <summary>确认：把职业装到玩家身上、显示玩家、交给俯视控制器</summary>
    public void ConfirmPick()
    {
        if (_stage != Stage.Selecting || _picked == null) return;

        if (_switcher != null) _switcher.SetClass(_picked.classData);

        if (_playerSprite != null) _playerSprite.enabled = true;
        if (_avatar != null) _avatar.SetActive(true);

        if (_body != null) _body.gravityScale = 0f;

        _stage = Stage.Walking;
        SetPanelVisible(false);
        if (_titleText != null) _titleText.text = "出发";
        RefreshHint("走到下面的门口开始游戏");
    }

    private int IndexOf(LobbyPedestal pedestal)
    {
        for (int i = 0; i < pedestals.Length; i++)
        {
            if (pedestals[i] == pedestal) return i;
        }

        return -1;
    }

    // ---------------------------------------------------------------- 输入

    /// <summary>
    /// 读新 Input System 的键盘/鼠标。老的 `Input.GetKeyDown` 在本工程会直接抛异常
    /// （Active Input Handling 是 Input System Package），批处理里 `Keyboard.current` 也可能是 null。
    /// </summary>
    private static bool IsStartPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) return true;

        Gamepad pad = Gamepad.current;
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }

    private Vector2 MouseWorld()
    {
        if (Mouse.current == null || _camera == null) return new Vector2(-999f, -999f);

        Vector2 screen = Mouse.current.position.ReadValue();
        Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, Mathf.Abs(_camera.transform.position.z)));
        return world;
    }

    private static bool PointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void UpdatePortalGlow()
    {
        if (portalGlow == null) return;

        Color target = _stage == Stage.Walking ? portalReadyColor : portalLockedColor;
        if (_stage == Stage.Walking && IsNearDoor) target = Color.Lerp(target, Color.white, 0.4f);
        portalGlow.color = Color.Lerp(portalGlow.color, target, 0.2f);
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
        _canvas.sortingOrder = 360;   // 压过 HUD（0）与词条面板（300），低于暂停菜单（400）

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();

        _titleText = MakeText(root.transform, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -64f),
                              new Vector2(1400f, 90f), 46, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.76f, 1f));
        _titleText.text = "选择角色";

        _hintText = MakeText(root.transform, "Hint", new Vector2(0.5f, 0f), new Vector2(0f, 64f),
                             new Vector2(1500f, 64f), 30, TextAnchor.MiddleCenter, new Color(0.90f, 0.93f, 1f, 0.95f));

        BuildPanel(root.transform);
    }

    private void BuildPanel(Transform parent)
    {
        var panel = new GameObject("ConfirmPanel", typeof(RectTransform));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 120f);
        rect.sizeDelta = new Vector2(720f, 250f);

        Image background = panel.AddComponent<Image>();
        background.sprite = WhiteSprite();
        background.color = new Color(0.08f, 0.09f, 0.12f, 0.92f);

        _panelName = MakeText(panel.transform, "Name", new Vector2(0.5f, 1f), new Vector2(0f, -44f),
                              new Vector2(680f, 60f), 40, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.55f, 1f));

        _panelDesc = MakeText(panel.transform, "Desc", new Vector2(0.5f, 1f), new Vector2(0f, -104f),
                              new Vector2(660f, 80f), 24, TextAnchor.UpperCenter, new Color(0.86f, 0.89f, 0.95f, 1f));

        MakeButton(panel.transform, "Ok", "确认", new Vector2(-130f, 34f), new Vector2(220f, 62f),
                   new Color(0.30f, 0.55f, 0.32f, 1f), ConfirmPick);

        MakeButton(panel.transform, "Cancel", "换一个", new Vector2(130f, 34f), new Vector2(220f, 62f),
                   new Color(0.30f, 0.32f, 0.38f, 1f), () =>
                   {
                       _picked = null;
                       PickedIndex = -1;
                       SetPanelVisible(false);
                       RefreshHint("用鼠标点一个角色，再点「确认」");
                   });

        _panel = panel;
        _panel.SetActive(false);
    }

    private void SetPanelVisible(bool visible)
    {
        if (_panel != null) _panel.SetActive(visible);
    }

    private void RefreshHint(string text)
    {
        if (_hintText != null) _hintText.text = text;
    }

    private static Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size,
                                 int fontSize, TextAnchor alignment, Color color)
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
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = string.Empty;
        return text;
    }

    private void MakeButton(Transform parent, string name, string label, Vector2 offset, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        Image background = go.AddComponent<Image>();
        background.sprite = WhiteSprite();
        background.color = color;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        Text text = MakeText(go.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, size, 30,
                             TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.96f));
        text.text = label;
        text.rectTransform.anchoredPosition = Vector2.zero;
    }

    private static Sprite _white;

    private static Sprite WhiteSprite()
    {
        if (_white != null) return _white;

        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++) texture.SetPixel(x, y, Color.white);
        }

        texture.Apply();
        _white = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
        _white.name = "LobbyWhite";
        return _white;
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
