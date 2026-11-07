using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏入口：不依赖场景里的任何物体，靠 [RuntimeInitializeOnLoadMethod] 自动把大厅/暂停菜单装起来。
///
/// 为什么不在场景里挂一个物体：手搭 UI 要拖几十个引用，而且中文字体是运行时动态字体（不是资产），
/// 序列化不进场景 —— 所以整个菜单都在代码里搭，这里只负责「什么时候搭、什么时候显示」。
///
/// 流程：
///   进 Play → 先显示大厅（时间冻结）→ 点「开始游戏」解冻 → 玩 →
///   按 Esc 暂停 → 「返回大厅」重新加载场景并回到大厅。
/// </summary>
public static class GameBootstrap
{
    private static GameObject _root;
    private static GameMenuUI _menu;
    private static LobbyUI _lobby;

    /// <summary>下一次场景加载完是否直接进大厅</summary>
    public static bool ShowLobbyOnStart = true;

    /// <summary>菜单实例（调试或其它系统要用时取）</summary>
    public static GameMenuUI Menu => _menu;

    /// <summary>大厅实例（自检要拿它验「选角色 → 开局」）</summary>
    public static LobbyUI Lobby => _lobby;

    /// <summary>
    /// 每次进 Play 前把静态状态清干净。
    ///
    /// 编辑器可以关掉「进入播放模式时重载域」，那时候静态字段会**跨 Play 保留**：
    /// 上一局点过「开始游戏」把 ShowLobbyOnStart 置成 false，下一次按 Play 就直接开打、
    /// 再也不出大厅 —— 表现正是「等待大厅没了」。这个回调在两种设置下都会跑。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _root = null;
        _menu = null;
        _lobby = null;
        ShowLobbyOnStart = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_root != null) return;

        GameSettings.Load();

        // 带 RectTransform 建出来：GameMenuUI 的整块界面靠锚点铺满屏幕，普通 Transform 撑不起来
        _root = new GameObject("__GameMenu", typeof(RectTransform));

        // 编辑模式的自检会直接调 Boot，那时不许调 DontDestroyOnLoad（Unity 会报错）
        if (Application.isPlaying) Object.DontDestroyOnLoad(_root);

        _menu = _root.AddComponent<GameMenuUI>();

        // 大厅和暂停菜单各一份：大厅是开局前选角色，暂停菜单是局内 Esc
        _lobby = _root.AddComponent<LobbyUI>();

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        HandleScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleScene();
    }

    /// <summary>每次场景加载完都走一遍：该进大厅就进大厅，否则确保菜单是关着的</summary>
    private static void HandleScene()
    {
        if (_menu == null) return;

        // 清掉上一个场景可能残留的冻结登记（例如正在三选一时点了返回大厅）
        TimeFreeze.UnfreezeAll();

        if (ShowLobbyOnStart)
        {
            if (_lobby != null) _lobby.Show();
            else _menu.ShowLobby();
        }
        else
        {
            if (_lobby != null) _lobby.Hide();
            else _menu.Close();
        }
    }

    /// <summary>大厅里点「开始游戏」</summary>
    public static void StartRun()
    {
        ShowLobbyOnStart = false;
        if (_lobby != null) _lobby.Hide();
        if (_menu != null) _menu.Close();
    }

    /// <summary>暂停菜单里点「返回大厅」：重载当前场景，并把下一次加载标记成「进大厅」</summary>
    public static void ReturnToLobby()
    {
        ShowLobbyOnStart = true;

        // 先解冻再切场景，否则新场景一加载就被冻住
        TimeFreeze.UnfreezeAll();

        Scene active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.buildIndex >= 0 ? active.buildIndex : 0);
    }
}
