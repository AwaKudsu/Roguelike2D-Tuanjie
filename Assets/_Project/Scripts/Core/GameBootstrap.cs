using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏入口：不依赖场景里的任何物体，靠 [RuntimeInitializeOnLoadMethod] 自动把菜单装起来。
///
/// 等待大厅不是代码搭的覆盖层，而是场景里真实搭出来的一间房（<see cref="LobbyRoom"/>）：
/// 玩家能走、能走到角色台前按 E 选职业、走到门口出发。这里只负责「什么时候把玩家放进去」。
///
/// 流程：
///   进 Play → 先把玩家挪进大厅（战斗系统停着）→ 走到台子前按 E 选职业 →
///   走到门按 E 出发（建房、放玩家到出生点）→ 玩 → 按 Esc 暂停 →「返回大厅」重新加载场景。
/// </summary>
public static class GameBootstrap
{
    private static GameObject _root;
    private static GameMenuUI _menu;
    private static LobbyRoom _lobby;

    /// <summary>下一次场景加载完是否直接进大厅</summary>
    public static bool ShowLobbyOnStart = true;

    /// <summary>菜单实例（调试或其它系统要用时取）</summary>
    public static GameMenuUI Menu => _menu;

    /// <summary>大厅实例（自检要拿它验「选角色 → 开局」）</summary>
    public static LobbyRoom Lobby => _lobby;

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

        // 大厅是场景里搭好的那间房，不是代码建出来的界面 —— 这里只是把它找出来
        _lobby = Object.FindObjectOfType<LobbyRoom>();

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
            if (_lobby != null) _lobby.Enter();
            else _menu.ShowLobby();
        }
        else
        {
            if (_lobby != null) _lobby.Leave();
            else _menu.Close();
        }
    }

    /// <summary>大厅里按 E 进门（或卡住时的「开始游戏」按钮）：离开大厅、开局</summary>
    public static void StartRun()
    {
        ShowLobbyOnStart = false;
        if (_lobby != null) _lobby.Leave();
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
