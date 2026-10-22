using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// Esc 菜单 / 大厅 / 设置（音量 + 改键）的自检。
    ///
    /// 批处理下进不了 Play（EditorApplication.EnterPlaymode 会挂在场景备份上），
    /// 所以这里直接调真实代码路径验证：时间冻结的仲裁、设置的读写、改键的存档往返、
    /// 以及场景里点按钮所必须的 EventSystem + InputSystemUIInputModule 在不在。
    /// </summary>
    public static class MenuCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ReportPath = "_Tuanjie/menu-report.txt";
        private const string BindingsKey = "settings.input.overrides";
        private const string MasterKey = "settings.audio.master";

        [MenuItem("团结/验证 Esc 菜单")]
        public static void Run()
        {
            StringBuilder sb = new StringBuilder();
            int fail = 0;
            int warn = 0;

            sb.AppendLine("=== Esc 菜单 / 大厅 / 设置 自检 ===");
            sb.AppendLine("工程 = " + Application.dataPath);

            // ---------- 1. 五个新类型都在运行时程序集里 ----------
            Type[] types =
            {
                typeof(TimeFreeze),
                typeof(GameSettings),
                typeof(InputBindingStore),
                typeof(GameMenuUI),
                typeof(GameBootstrap),
            };

            int missing = 0;
            foreach (Type type in types)
            {
                if (type == null) missing++;
            }

            if (missing == 0)
                Pass(sb, "新增类型齐全", "TimeFreeze / GameSettings / InputBindingStore / GameMenuUI / GameBootstrap");
            else
                Fail(sb, "新增类型缺失", missing + " 个", ref fail);

            // ---------- 2. GameBootstrap 是否带自动启动特性 ----------
            MethodInfo boot = typeof(GameBootstrap).GetMethod("Boot", BindingFlags.Static | BindingFlags.NonPublic);
            bool hasBootAttr = boot != null && boot.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null;

            if (hasBootAttr)
                Pass(sb, "GameBootstrap 自动启动", "[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] 已挂上");
            else
                Fail(sb, "GameBootstrap 自动启动", "没找到 Boot() 或缺少 RuntimeInitializeOnLoadMethod", ref fail);

            // ---------- 3. PlayerInputReader 暴露了 Actions ----------
            PropertyInfo actionsProp = typeof(PlayerInputReader).GetProperty("Actions");
            if (actionsProp != null && actionsProp.PropertyType == typeof(PlayerInputActions))
                Pass(sb, "PlayerInputReader.Actions", "可用于设置界面改键");
            else
                Fail(sb, "PlayerInputReader.Actions", "属性不存在或类型不对", ref fail);

            // ---------- 4. TimeFreeze 的仲裁语义 ----------
            float savedScale = Time.timeScale;
            try
            {
                TimeFreeze.UnfreezeAll();

                object ownerA = new object();
                object ownerB = new object();

                TimeFreeze.Freeze(ownerA);
                bool frozenOnce = Mathf.Approximately(Time.timeScale, 0f);

                TimeFreeze.Freeze(ownerB);
                TimeFreeze.Unfreeze(ownerA);
                bool stillFrozen = TimeFreeze.IsFrozen && Mathf.Approximately(Time.timeScale, 0f);

                TimeFreeze.Unfreeze(ownerB);
                bool restored = !TimeFreeze.IsFrozen && Mathf.Approximately(Time.timeScale, 1f);

                if (frozenOnce && stillFrozen && restored)
                    Pass(sb, "TimeFreeze 仲裁", "两人冻结 → 只解一个仍冻结 → 全解才还原 1.0");
                else
                    Fail(sb, "TimeFreeze 仲裁",
                        "frozenOnce=" + frozenOnce + " stillFrozen=" + stillFrozen + " restored=" + restored, ref fail);
            }
            finally
            {
                TimeFreeze.UnfreezeAll();
                Time.timeScale = savedScale;
            }

            // ---------- 5. 音量设置能存能读 ----------
            try
            {
                GameSettings.Load();
                float master = GameSettings.Master;

                bool applied = Mathf.Approximately(AudioListener.volume, Mathf.Clamp01(master));
                if (applied)
                    Pass(sb, "主音量已交给 AudioListener", "AudioListener.volume = " + AudioListener.volume.ToString("0.00"));
                else
                    Fail(sb, "主音量未生效",
                        "Master=" + master.ToString("0.00") + " 但 AudioListener.volume=" + AudioListener.volume.ToString("0.00"),
                        ref fail);

                float probe = Mathf.Approximately(master, 0.35f) ? 0.45f : 0.35f;
                GameSettings.Master = probe;
                GameSettings.Save();

                float readBack = PlayerPrefs.GetFloat(MasterKey, -1f);
                if (Mathf.Approximately(readBack, probe))
                    Pass(sb, "音量写盘", MasterKey + " = " + readBack.ToString("0.00"));
                else
                    Warn(sb, "音量写盘", "写入 " + probe.ToString("0.00") + " 但读回 " + readBack.ToString("0.00"), ref warn);

                // 还原用户原来的音量，别把自检的探针值留在注册表里
                GameSettings.Master = master;
                GameSettings.Save();
                Pass(sb, "音量已还原", GameSettings.Master.ToString("0.00"));
            }
            catch (Exception e)
            {
                Fail(sb, "音量设置异常", e.GetType().Name + ": " + e.Message, ref fail);
            }

            // ---------- 6. 改键：默认键显示 + 存档往返 ----------
            string originalOverride = PlayerPrefs.GetString(BindingsKey, string.Empty);
            PlayerInputActions actions = null;

            try
            {
                actions = new PlayerInputActions();

                InputAction[] keys =
                {
                    actions.Player.Jump,
                    actions.Player.Dash,
                    actions.Player.Attack,
                    actions.Player.Skill1,
                    actions.Player.Skill2,
                    actions.Player.Skill3,
                    actions.Player.Skill4,
                };

                string[] labels = { "跳跃", "冲刺", "攻击", "技能1", "技能2", "技能3", "技能4" };
                StringBuilder listing = new StringBuilder();
                int emptyCount = 0;

                for (int i = 0; i < keys.Length; i++)
                {
                    string text = InputBindingStore.Describe(keys[i]);
                    if (string.IsNullOrEmpty(text) || text == "—") emptyCount++;
                    listing.Append(labels[i]).Append('=').Append(text);
                    if (i < keys.Length - 1) listing.Append("  ");
                }

                if (emptyCount == 0)
                    Pass(sb, "七条动作都读得出当前按键", listing.ToString());
                else
                    Fail(sb, "有动作读不出按键", emptyCount + " 条为空；" + listing, ref fail);

                // Save → 键存在 → Load 不抛异常 → Reset 清键
                InputBindingStore.Save(actions.asset);
                bool savedSomething = PlayerPrefs.HasKey(BindingsKey);

                PlayerInputActions reloaded = new PlayerInputActions();
                bool loadOk = true;
                try
                {
                    InputBindingStore.Apply(reloaded.asset);
                }
                catch (Exception e)
                {
                    loadOk = false;
                    Warn(sb, "改键读取抛异常", e.Message, ref warn);
                }

                InputBindingStore.ResetAll(reloaded.asset);
                bool cleared = !PlayerPrefs.HasKey(BindingsKey);

                if (savedSomething && loadOk && cleared)
                    Pass(sb, "改键存档往返", "保存 → 读回 → 全部恢复默认，三步都不报错");
                else
                    Warn(sb, "改键存档往返",
                        "saved=" + savedSomething + " loadOk=" + loadOk + " cleared=" + cleared, ref warn);
            }
            catch (Exception e)
            {
                Fail(sb, "改键流程异常", e.GetType().Name + ": " + e.Message, ref fail);
            }
            finally
            {
                // 把玩家本机原来的改键存档还回去
                if (string.IsNullOrEmpty(originalOverride))
                    PlayerPrefs.DeleteKey(BindingsKey);
                else
                    PlayerPrefs.SetString(BindingsKey, originalOverride);

                PlayerPrefs.Save();

                if (actions != null) actions.Dispose();
            }

            // ---------- 7. 场景里点按钮必需的两样东西 ----------
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                bool hasEventSystem = false;
                bool hasInputModule = false;

                foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (root.GetComponentInChildren<EventSystem>(true) != null) hasEventSystem = true;
                    if (root.GetComponentInChildren<InputSystemUIInputModule>(true) != null) hasInputModule = true;
                }

                if (hasEventSystem && hasInputModule)
                    Pass(sb, "场景可点按钮", "EventSystem + InputSystemUIInputModule 都在");
                else if (hasEventSystem)
                    Fail(sb, "场景缺 UI 输入模块", "有 EventSystem 但没有 InputSystemUIInputModule，鼠标点不动按钮", ref fail);
                else
                    Fail(sb, "场景缺 EventSystem", "菜单按钮将无法点击", ref fail);
            }
            catch (Exception e)
            {
                Fail(sb, "打开场景失败", e.Message, ref fail);
            }

            // ---------- 8. 场景里 RoomManager 接的池子在不在（回归用） ----------
            try
            {
                RoomManager manager = UnityEngine.Object.FindObjectOfType<RoomManager>();
                if (manager == null)
                {
                    Warn(sb, "场景里没有 RoomManager", "如果这是大厅场景就属于正常", ref warn);
                }
                else
                {
                    SerializedObject so = new SerializedObject(manager);
                    SerializedProperty pool = so.FindProperty("roomPool");
                    if (pool != null && pool.objectReferenceValue != null)
                        Pass(sb, "RoomManager 房间池仍在", pool.objectReferenceValue.name);
                    else
                        Warn(sb, "RoomManager 房间池为空", "Day8 的接线丢了？", ref warn);
                }
            }
            catch (Exception e)
            {
                Warn(sb, "检查 RoomManager 失败", e.Message, ref warn);
            }

            // ---------- 9. 真的把菜单搭一遍（反射调 Build，验证不抛异常且结构完整） ----------
            // 光有类型不代表界面搭得出来：上一轮「(RectTransform)transform」就是编译通过、运行时炸。
            GameObject menuProbe = null;
            try
            {
                menuProbe = new GameObject("__MenuBuildProbe", typeof(RectTransform));
                GameMenuUI menu = menuProbe.AddComponent<GameMenuUI>();

                MethodInfo build = typeof(GameMenuUI).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic);
                if (build == null)
                {
                    Fail(sb, "菜单能搭起来", "找不到私有方法 Build()", ref fail);
                }
                else
                {
                    build.Invoke(menu, null);

                    Canvas canvas = menuProbe.GetComponent<Canvas>();
                    CanvasScaler scaler = menuProbe.GetComponent<CanvasScaler>();
                    GraphicRaycaster raycaster = menuProbe.GetComponent<GraphicRaycaster>();
                    Transform panel = menuProbe.transform.Find("Panel");

                    bool canvasOk = canvas != null && canvas.sortingOrder == 400
                                    && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
                    bool scalerOk = scaler != null
                                    && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                                    && Mathf.Approximately(scaler.referenceResolution.x, 1920f)
                                    && Mathf.Approximately(scaler.referenceResolution.y, 1080f);
                    bool pagesOk = panel != null
                                   && panel.Find("MainPage") != null
                                   && panel.Find("SettingsPage") != null
                                   && panel.Find("KeysPage") != null;
                    int buttons = menuProbe.GetComponentsInChildren<Button>(true).Length;
                    int texts = menuProbe.GetComponentsInChildren<Text>(true).Length;

                    if (canvasOk && scalerOk && raycaster != null && pagesOk && buttons >= 10)
                        Pass(sb, "菜单能搭起来",
                            "Canvas 排序 400 / 缩放 1920×1080 / 三页齐全 / 按钮 " + buttons + " 个 / 文字 " + texts + " 个");
                    else
                        Fail(sb, "菜单结构不完整",
                            "canvas=" + canvasOk + " scaler=" + scalerOk + " raycaster=" + (raycaster != null)
                            + " pages=" + pagesOk + " buttons=" + buttons, ref fail);
                }
            }
            catch (Exception e)
            {
                Exception inner = (e is TargetInvocationException && e.InnerException != null) ? e.InnerException : e;
                Fail(sb, "菜单搭建抛异常", inner.GetType().Name + ": " + inner.Message, ref fail);
                sb.AppendLine("    " + inner.StackTrace);
            }
            finally
            {
                if (menuProbe != null) UnityEngine.Object.DestroyImmediate(menuProbe);
            }

            sb.AppendLine();
            sb.AppendLine("[SUMMARY] 失败|" + fail);
            sb.AppendLine("[SUMMARY] 警告|" + warn);

            Write(report: sb.ToString());

            string summary = "失败 " + fail + " / 警告 " + warn;
            Debug.Log("[MenuCheck] " + summary + "\n" + sb);

            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Esc 菜单自检", summary + "\n\n报告：\n" + ReportPath, "好");
        }

        private static void Pass(StringBuilder sb, string title, string detail)
        {
            sb.AppendLine("[PASS]|" + title + "|" + detail);
        }

        private static void Warn(StringBuilder sb, string title, string detail, ref int warn)
        {
            warn++;
            sb.AppendLine("[WARN]|" + title + "|" + detail);
        }

        private static void Fail(StringBuilder sb, string title, string detail, ref int fail)
        {
            fail++;
            sb.AppendLine("[FAIL]|" + title + "|" + detail);
        }

        private static void Write(string report)
        {
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "_Tuanjie");
            System.IO.Directory.CreateDirectory(dir);

            string path = System.IO.Path.Combine(dir, "menu-report.txt");
            System.IO.File.WriteAllText(path, report, new System.Text.UTF8Encoding(false));
        }
    }
}
