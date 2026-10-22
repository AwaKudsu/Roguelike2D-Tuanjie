using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Esc 菜单 + 大厅 + 设置（音量 / 改键）。
///
/// 全屏界面都在运行时用代码搭，理由和 RelicChoiceUI 一样：
/// 中文字形必须问系统要动态字体，而动态字体不是资产，序列化不进场景；
/// 顺带也省掉了「手搭 30 个 RectTransform 再一个个拖引用」的机械劳动。
///
/// 三个页面共用一个全屏暗幕容器，靠 SetActive 切换：
///   Main     —— 大厅时是「开始游戏」，游戏中是「继续游戏」
///   Settings —— 三条音量 + 试听
///   Keys     —— 七条可改键动作 + 全部恢复默认
/// </summary>
public class GameMenuUI : MonoBehaviour
{
    private enum Page
    {
        None,
        Main,
        Settings,
        Keys,
    }

    // ---------------- 配色 ----------------
    private static readonly Color BackdropColor  = new Color(0f, 0f, 0f, 0.88f);
    private static readonly Color TextColor      = new Color(0.94f, 0.95f, 0.97f);
    private static readonly Color DimTextColor   = new Color(0.62f, 0.66f, 0.74f);
    private static readonly Color ButtonColor    = new Color(0.16f, 0.18f, 0.23f, 1f);
    private static readonly Color RowColor       = new Color(0.11f, 0.12f, 0.16f, 0.92f);
    private static readonly Color AccentColor    = new Color(0.42f, 0.68f, 1f);
    private static readonly Color WarnTextColor  = new Color(1f, 0.78f, 0.35f);

    // ---------------- 音量页 ----------------
    private const int VolumeCount = 3;
    private static readonly string[] VolumeLabels = { "主音量", "音乐", "音效" };
    private readonly Image[] _volumeFills = new Image[VolumeCount];
    private readonly Text[] _volumeValues = new Text[VolumeCount];

    // ---------------- 改键页 ----------------
    private const int KeyCount = 7;
    private static readonly string[] KeyLabels = { "跳跃", "冲刺", "攻击", "技能 1", "技能 2", "技能 3", "技能 4" };
    private readonly Text[] _keyValues = new Text[KeyCount];
    private readonly Button[] _keyButtons = new Button[KeyCount];

    private Text _keyStatus;
    private int _rebindingIndex = -1;
    private InputActionRebindingExtensions.RebindingOperation _rebindOp;

    // ---------------- 页面骨架 ----------------
    private Page _page = Page.None;
    private bool _lobby;

    private RectTransform _panel;
    private RectTransform _mainPage;
    private RectTransform _settingsPage;
    private RectTransform _keysPage;

    private Text _title;
    private Text _subtitle;
    private Text _continueLabel;
    private Button _lobbyButton;

    private static Sprite _whiteSprite;
    private static Font _uiFont;

    // =====================================================================
    // 生命周期
    // =====================================================================

    private void Awake()
    {
        Build();
        Close();
    }

    private void OnDestroy()
    {
        if (_rebindOp != null)
        {
            _rebindOp.Dispose();
            _rebindOp = null;
        }

        TimeFreeze.Unfreeze(this);
    }

    private void Update()
    {
        // 改键期间 Esc 由 rebinding 自己吃掉，不能顺手把菜单关了
        if (_rebindOp != null) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

        if (_page == Page.None)
        {
            // 有别的东西（词条三选一）已经冻结了时间，这时不开暂停菜单，免得两层弹窗叠在一起
            if (TimeFreeze.IsFrozen) return;
            ShowPause();
        }
        else if (_page != Page.Main)
        {
            SetPage(Page.Main);
        }
        else if (!_lobby)
        {
            Close();
        }
    }

    // =====================================================================
    // 对外接口（GameBootstrap 调）
    // =====================================================================

    /// <summary>打开暂停菜单</summary>
    public void ShowPause()
    {
        _lobby = false;

        if (_title != null) _title.text = "已暂停";
        if (_subtitle != null) _subtitle.text = "Esc 返回游戏";
        if (_continueLabel != null) _continueLabel.text = "继续游戏";
        if (_lobbyButton != null) _lobbyButton.gameObject.SetActive(true);

        SetPage(Page.Main);
        _panel.gameObject.SetActive(true);
        TimeFreeze.Freeze(this);
        SetPlayerInputEnabled(false);
    }

    /// <summary>打开大厅（主菜单）</summary>
    public void ShowLobby()
    {
        _lobby = true;

        if (_title != null) _title.text = "Roguelike2D";
        if (_subtitle != null) _subtitle.text = "横板动作肉鸽 · 团结引擎 2022.3.62t16";
        if (_continueLabel != null) _continueLabel.text = "开始游戏";
        if (_lobbyButton != null) _lobbyButton.gameObject.SetActive(false);

        SetPage(Page.Main);
        _panel.gameObject.SetActive(true);
        TimeFreeze.Freeze(this);
        SetPlayerInputEnabled(false);
    }

    /// <summary>关掉菜单、解冻、把输入还给玩家</summary>
    public void Close()
    {
        SetPage(Page.None);

        if (_panel != null) _panel.gameObject.SetActive(false);

        TimeFreeze.Unfreeze(this);
        SetPlayerInputEnabled(true);
    }

    /// <summary>当前菜单是否开着</summary>
    public bool IsOpen => _page != Page.None;

    // =====================================================================
    // 按钮回调
    // =====================================================================

    private void OnContinue()
    {
        if (_lobby)
        {
            GameBootstrap.StartRun();
        }
        else
        {
            Close();
        }
    }

    private void OnReturnToLobby()
    {
        // 先解冻再切场景：否则新场景刚加载就被上一轮的冻结状态压住
        Close();
        GameBootstrap.ReturnToLobby();
    }

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnResetKeys()
    {
        PlayerInputReader reader = FindObjectOfType<PlayerInputReader>();
        if (reader == null || reader.Actions == null)
        {
            SetKeyStatus("找不到玩家对象，先进入游戏再改键");
            return;
        }

        InputBindingStore.ResetAll(reader.Actions.asset);
        SetKeyStatus("已恢复默认按键");
        RefreshKeys();
    }

    // =====================================================================
    // 音量
    // =====================================================================

    private static float GetVolume(int index)
    {
        switch (index)
        {
            case 0: return GameSettings.Master;
            case 1: return GameSettings.Music;
            default: return GameSettings.Sfx;
        }
    }

    private static void SetVolume(int index, float value)
    {
        switch (index)
        {
            case 0: GameSettings.Master = value; break;
            case 1: GameSettings.Music = value; break;
            default: GameSettings.Sfx = value; break;
        }
    }

    private void NudgeVolume(int index, float delta)
    {
        SetVolume(index, Mathf.Clamp01(GetVolume(index) + delta));
        RefreshVolumes();

        // 主音量和音效拖的时候给一声试听；音乐没有素材，听也没意义
        if (index != 1) GameSettings.PlayTestTone();
    }

    private void RefreshVolumes()
    {
        for (int i = 0; i < VolumeCount; i++)
        {
            float value = GetVolume(i);

            if (_volumeFills[i] != null) _volumeFills[i].fillAmount = value;
            if (_volumeValues[i] != null) _volumeValues[i].text = Mathf.RoundToInt(value * 100f) + "%";
        }
    }

    // =====================================================================
    // 改键
    // =====================================================================

    private void BeginRebind(int index)
    {
        if (_rebindOp != null) return;

        PlayerInputReader reader = FindObjectOfType<PlayerInputReader>();
        if (reader == null || reader.Actions == null)
        {
            SetKeyStatus("找不到玩家对象，先进入游戏再改键");
            return;
        }

        InputAction action = GetKeyAction(reader.Actions, index);
        if (action == null)
        {
            SetKeyStatus("这条动作不存在，请检查 PlayerInputActions");
            return;
        }

        _rebindingIndex = index;
        SetKeyInteractable(false);
        SetKeyStatus("请按下新的按键……（Esc 取消）");
        RefreshKeys();

        PlayerInputReader captured = reader;
        _rebindOp = InputBindingStore.StartRebind(action, () =>
        {
            _rebindOp = null;
            _rebindingIndex = -1;

            InputActionAsset asset = captured != null && captured.Actions != null ? captured.Actions.asset : null;
            InputBindingStore.Save(asset);

            SetKeyInteractable(true);
            SetKeyStatus("已保存");
            RefreshKeys();
        });
    }

    private void SetKeyInteractable(bool interactable)
    {
        for (int i = 0; i < _keyButtons.Length; i++)
        {
            if (_keyButtons[i] != null) _keyButtons[i].interactable = interactable;
        }
    }

    private void RefreshKeys()
    {
        PlayerInputReader reader = FindObjectOfType<PlayerInputReader>();

        for (int i = 0; i < KeyCount; i++)
        {
            if (_keyValues[i] == null) continue;

            if (_rebindingIndex == i)
            {
                _keyValues[i].text = "按新键…";
                _keyValues[i].color = WarnTextColor;
                continue;
            }

            _keyValues[i].color = AccentColor;

            if (reader == null || reader.Actions == null)
            {
                _keyValues[i].text = "—";
                continue;
            }

            _keyValues[i].text = InputBindingStore.Describe(GetKeyAction(reader.Actions, i));
        }
    }

    private void SetKeyStatus(string message)
    {
        if (_keyStatus != null) _keyStatus.text = message;
    }

    private static InputAction GetKeyAction(PlayerInputActions actions, int index)
    {
        if (actions == null) return null;

        switch (index)
        {
            case 0: return actions.Player.Jump;
            case 1: return actions.Player.Dash;
            case 2: return actions.Player.Attack;
            case 3: return actions.Player.Skill1;
            case 4: return actions.Player.Skill2;
            case 5: return actions.Player.Skill3;
            case 6: return actions.Player.Skill4;
            default: return null;
        }
    }

    // =====================================================================
    // 页面切换
    // =====================================================================

    private void SetPage(Page page)
    {
        _page = page;

        if (_mainPage != null) _mainPage.gameObject.SetActive(page == Page.Main);
        if (_settingsPage != null) _settingsPage.gameObject.SetActive(page == Page.Settings);
        if (_keysPage != null) _keysPage.gameObject.SetActive(page == Page.Keys);

        if (page == Page.Settings) RefreshVolumes();

        if (page == Page.Keys)
        {
            RefreshKeys();
            SetKeyStatus(string.Empty);
        }
    }

    private static void SetPlayerInputEnabled(bool allowInput)
    {
        PlayerInputReader reader = FindObjectOfType<PlayerInputReader>();
        if (reader != null) reader.enabled = allowInput;
    }

    // =====================================================================
    // 搭界面
    // =====================================================================

    private void Build()
    {
        // UI 的根必须是 RectTransform：挂到普通 Transform 上时锚点不生效，整块面板会缩成 0×0
        // （症状就是「代码没报错，但屏幕上一个像素都没有」）。这里兜底再建一层，绝不假设调用方建对了。
        RectTransform root = transform as RectTransform;
        if (root == null)
        {
            Debug.LogWarning("[菜单] GameMenuUI 挂在了没有 RectTransform 的物体上，已自动新建一层 __MenuCanvas。", this);
            root = NewRect("__MenuCanvas", transform);
            Stretch(root);
        }

        GameObject host = root.gameObject;

        Canvas canvas = host.GetComponent<Canvas>();
        if (canvas == null) canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;   // 必须压过 HUD(默认 0) 和词条面板(300)

        CanvasScaler scaler = host.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = host.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (host.GetComponent<GraphicRaycaster>() == null)
            host.AddComponent<GraphicRaycaster>();

        _panel = NewRect("Panel", root);
        Stretch(_panel);
        Image backdrop = NewImage(_panel, BackdropColor);
        backdrop.raycastTarget = true;   // 挡住背后的一切点击

        RectTransform titleRect = NewRect("Title", _panel);
        PlaceTopCenter(titleRect, -110f, 1400f, 96f);
        _title = NewText(titleRect, "已暂停", 52, TextColor, TextAnchor.MiddleCenter);

        RectTransform subtitleRect = NewRect("Subtitle", _panel);
        PlaceTopCenter(subtitleRect, -200f, 1400f, 40f);
        _subtitle = NewText(subtitleRect, "Esc 返回游戏", 22, DimTextColor, TextAnchor.MiddleCenter);

        BuildMainPage();
        BuildSettingsPage();
        BuildKeysPage();

        SetPage(Page.None);
        RefreshVolumes();
    }

    private void BuildMainPage()
    {
        _mainPage = NewRect("MainPage", _panel);
        Stretch(_mainPage);

        const float startY = -330f;
        const float step = 92f;

        MenuButton(_mainPage, "继续游戏", startY, OnContinue, out _continueLabel);
        MenuButton(_mainPage, "设置", startY - step, () => SetPage(Page.Settings), out _);
        MenuButton(_mainPage, "按键调整", startY - step * 2f, () => SetPage(Page.Keys), out _);
        _lobbyButton = MenuButton(_mainPage, "返回大厅", startY - step * 3f, OnReturnToLobby, out _);
        MenuButton(_mainPage, "退出游戏", startY - step * 4f, OnQuit, out _);
    }

    private void BuildSettingsPage()
    {
        _settingsPage = NewRect("SettingsPage", _panel);
        Stretch(_settingsPage);

        RectTransform header = NewRect("Header", _settingsPage);
        PlaceTopCenter(header, -150f, 1400f, 72f);
        NewText(header, "设置", 44, TextColor, TextAnchor.MiddleCenter);

        const float startY = -270f;
        const float step = 110f;

        for (int i = 0; i < VolumeCount; i++)
        {
            BuildVolumeRow(_settingsPage, i, startY - step * i);
        }

        Button test = CreateButton(_settingsPage, "试听音效", 26, new Color(0.2f, 0.24f, 0.32f, 1f), GameSettings.PlayTestTone, out _);
        PlaceTopCenter((RectTransform)test.transform, startY - step * 3f - 6f, 320f, 62f);

        Button back = CreateButton(_settingsPage, "返回", 30, ButtonColor, () => SetPage(Page.Main), out _);
        PlaceTopCenter((RectTransform)back.transform, startY - step * 3f - 112f, 300f, 66f);
    }

    private void BuildVolumeRow(RectTransform parent, int index, float y)
    {
        int captured = index;

        RectTransform row = NewRect("Volume_" + index, parent);
        PlaceTopCenter(row, y, 900f, 84f);
        NewImage(row, RowColor);

        RectTransform nameRect = NewRect("Name", row);
        PlaceLeftMiddle(nameRect, 24f, 0f, 200f, 60f);
        NewText(nameRect, VolumeLabels[index], 28, TextColor, TextAnchor.MiddleLeft);

        RectTransform barRect = NewRect("Bar", row);
        PlaceLeftMiddle(barRect, 230f, 0f, 320f, 24f);
        NewImage(barRect, new Color(0.08f, 0.09f, 0.12f, 1f));

        RectTransform fillRect = NewRect("Fill", barRect);
        Stretch(fillRect);
        Image fill = NewImage(fillRect, AccentColor);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0.5f;
        _volumeFills[index] = fill;

        RectTransform valueRect = NewRect("Value", row);
        PlaceLeftMiddle(valueRect, 570f, 0f, 100f, 60f);
        _volumeValues[index] = NewText(valueRect, "0%", 26, TextColor, TextAnchor.MiddleLeft);

        Button minus = CreateButton(row, "−", 34, new Color(0.2f, 0.24f, 0.32f, 1f), () => NudgeVolume(captured, -0.1f), out _);
        PlaceLeftMiddle((RectTransform)minus.transform, 680f, 0f, 56f, 56f);

        Button plus = CreateButton(row, "+", 34, new Color(0.2f, 0.24f, 0.32f, 1f), () => NudgeVolume(captured, 0.1f), out _);
        PlaceLeftMiddle((RectTransform)plus.transform, 748f, 0f, 56f, 56f);
    }

    private void BuildKeysPage()
    {
        _keysPage = NewRect("KeysPage", _panel);
        Stretch(_keysPage);

        RectTransform header = NewRect("Header", _keysPage);
        PlaceTopCenter(header, -110f, 1400f, 70f);
        NewText(header, "按键调整", 44, TextColor, TextAnchor.MiddleCenter);

        RectTransform hint = NewRect("Hint", _keysPage);
        PlaceTopCenter(hint, -180f, 1500f, 36f);
        NewText(hint, "点「改键」再按下新的键；按 Esc 取消。改完会自动保存到本机。", 20, DimTextColor, TextAnchor.MiddleCenter);

        const float startY = -240f;
        const float step = 76f;

        for (int i = 0; i < KeyCount; i++)
        {
            int captured = i;

            RectTransform row = NewRect("Key_" + i, _keysPage);
            PlaceTopCenter(row, startY - step * i, 900f, 64f);
            NewImage(row, RowColor);

            RectTransform nameRect = NewRect("Name", row);
            PlaceLeftMiddle(nameRect, 24f, 0f, 220f, 60f);
            NewText(nameRect, KeyLabels[i], 26, TextColor, TextAnchor.MiddleLeft);

            RectTransform valueRect = NewRect("Value", row);
            PlaceLeftMiddle(valueRect, 260f, 0f, 380f, 60f);
            _keyValues[i] = NewText(valueRect, "—", 26, AccentColor, TextAnchor.MiddleLeft);

            Button rebind = CreateButton(row, "改键", 24, new Color(0.2f, 0.26f, 0.36f, 1f), () => BeginRebind(captured), out _);
            RectTransform rebindRect = (RectTransform)rebind.transform;
            rebindRect.anchorMin = new Vector2(1f, 0.5f);
            rebindRect.anchorMax = new Vector2(1f, 0.5f);
            rebindRect.pivot = new Vector2(1f, 0.5f);
            rebindRect.sizeDelta = new Vector2(140f, 50f);
            rebindRect.anchoredPosition = new Vector2(-16f, 0f);
            _keyButtons[i] = rebind;
        }

        float statusY = startY - step * KeyCount - 6f;

        RectTransform statusRect = NewRect("Status", _keysPage);
        PlaceTopCenter(statusRect, statusY, 900f, 40f);
        _keyStatus = NewText(statusRect, string.Empty, 22, WarnTextColor, TextAnchor.MiddleCenter);

        Button reset = CreateButton(_keysPage, "全部恢复默认", 24, new Color(0.3f, 0.2f, 0.22f, 1f), OnResetKeys, out _);
        PlaceTopCenter((RectTransform)reset.transform, statusY - 62f, 320f, 58f);

        Button back = CreateButton(_keysPage, "返回", 30, ButtonColor, () => SetPage(Page.Main), out _);
        PlaceTopCenter((RectTransform)back.transform, statusY - 136f, 300f, 62f);
    }

    // =====================================================================
    // 小工具
    // =====================================================================

    private Button MenuButton(RectTransform parent, string caption, float y, UnityAction onClick, out Text label)
    {
        Button button = CreateButton(parent, caption, 32, ButtonColor, onClick, out label);
        PlaceTopCenter((RectTransform)button.transform, y, 460f, 74f);
        return button;
    }

    private static Button CreateButton(RectTransform parent, string caption, int fontSize, Color background, UnityAction onClick, out Text label)
    {
        RectTransform rect = NewRect("Btn_" + caption, parent);

        Image image = NewImage(rect, background);
        image.raycastTarget = true;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.45f, 1.7f, 1f);
        colors.pressedColor = new Color(0.75f, 0.82f, 1f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.05f;
        button.colors = colors;

        if (onClick != null) button.onClick.AddListener(onClick);

        RectTransform labelRect = NewRect("Label", rect);
        Stretch(labelRect);
        label = NewText(labelRect, caption, fontSize, TextColor, TextAnchor.MiddleCenter);

        return button;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = WhiteSprite;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text NewText(RectTransform rect, string content, int size, Color color, TextAnchor anchor)
    {
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = UiFont;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void PlaceTopCenter(RectTransform rect, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    private static void PlaceLeftMiddle(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    private static Sprite WhiteSprite
    {
        get
        {
            if (_whiteSprite != null) return _whiteSprite;

            Texture2D tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "MenuUI_White";
            return _whiteSprite;
        }
    }

    private static Font UiFont
    {
        get
        {
            if (_uiFont != null) return _uiFont;

            _uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "PingFang SC", "Arial" },
                32);

            if (_uiFont == null)
            {
                Debug.LogWarning("[菜单] 找不到任何系统字体，中文会显示成方块。请装一个中文字体，或在 UiFont 里加字体名。");
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return _uiFont;
        }
    }
}
