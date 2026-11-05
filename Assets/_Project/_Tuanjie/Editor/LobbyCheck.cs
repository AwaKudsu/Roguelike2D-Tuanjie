#if UNITY_EDITOR
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 等待大厅自检：不按 Play，直接把它搭出来，验证「两张卡 + 选职业 + 开始游戏」这条链。
    ///
    /// 大厅是 `[RuntimeInitializeOnLoadMethod]` 在运行时建的，编辑模式里并不存在实例，
    /// 所以这里手工造一个临时物体、反射叫 Awake，用完销毁（不存盘）。
    /// </summary>
    public static class LobbyCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ReportPath = "_Tuanjie/lobby-check-report.txt";

        [MenuItem("团结/验证等待大厅")]
        public static void Run()
        {
            var sb = new StringBuilder();
            int fail = 0;

            sb.AppendLine("[INFO] 等待大厅自检");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine($"[INFO] 已打开场景 | {ScenePath}");

            Report(sb, typeof(LobbyUI) != null, "大厅脚本存在 | LobbyUI", ref fail);

            var bootField = typeof(GameBootstrap).GetProperty("Lobby", BindingFlags.Public | BindingFlags.Static);
            Report(sb, bootField != null, "入口挂上了大厅 | GameBootstrap.Lobby", ref fail);

            var switcher = Object.FindObjectOfType<PlayerVisualSwitcher>();
            Report(sb, switcher != null, "玩家有职业切换器 | PlayerVisualSwitcher", ref fail);
            if (switcher != null)
            {
                Report(sb, switcher.Classes != null && switcher.Classes.Count >= 2,
                       $"职业表 | {switcher.Classes.Count} 个（{string.Join(" / ", ClassNames(switcher))}）", ref fail);
            }

            // 真搭一次
            GameObject host = null;
            LobbyUI lobby = null;
            bool wasFrozen;

            try
            {
                host = new GameObject("__LobbyProbe", typeof(RectTransform));
                lobby = host.AddComponent<LobbyUI>();

                // 编辑模式下 AddComponent 不会跑 Awake，手动叫一次
                typeof(LobbyUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(lobby, null);

                lobby.Show();
                wasFrozen = TimeFreeze.IsFrozen;

                Report(sb, lobby.CardCount == 2, $"卡片数量 | {lobby.CardCount} 张（战士 / 法师）", ref fail);
                Report(sb, lobby.Current != null, $"默认选中 | {(lobby.Current != null ? lobby.Current.className : "★空")}", ref fail);
                Report(sb, wasFrozen, "进大厅会冻结世界 | TimeFreeze 已登记大厅", ref fail);

                Canvas canvas = host.GetComponentInChildren<Canvas>(true);
                Report(sb, canvas != null && canvas.enabled, "大厅界面搭出来了 | Canvas 存在且启用", ref fail);
                Report(sb, canvas != null && canvas.sortingOrder == 350,
                       $"排序 | {(canvas != null ? canvas.sortingOrder : -1)}（暂停菜单 400 会把大厅盖住）", ref fail);

                var playerReader = Object.FindObjectOfType<PlayerInputReader>();
                Report(sb, playerReader == null || !playerReader.enabled, "进大厅会关掉玩家输入 | 不会在选角色时乱跑", ref fail);

                // 关掉之后要能解冻、能把输入还回去（否则「开始游戏」就是假的）
                lobby.Hide();
                Report(sb, !TimeFreeze.IsFrozen, "离开大厅解冻 | TimeFreeze 已清掉", ref fail);
                Report(sb, playerReader == null || playerReader.enabled, "离开大厅恢复输入 | PlayerInputReader 已启用", ref fail);

                // 换选择：走 public 的 Focus 入口不存在，所以直接验「点第二张卡会选中第二个职业」
                GameObject second = host.transform.Find("LobbyCanvas/Panel/Cards/Card_1")?.gameObject;
                Report(sb, second != null, "第二张卡存在 | Card_1", ref fail);

                // 端到端：选第二张卡 → 开始游戏 → 玩家真的变成那个职业
                // 编辑模式不会自动跑 OnEnable，而「换职业 → 换表现层」是走 ClassChanged 事件的，
                // 所以要先手工叫一次 OnEnable 把订阅补上，否则测出来的是假失败。
                InvokeIfPresent(switcher, "OnEnable");

                lobby.Show();
                typeof(LobbyUI).GetMethod("Focus", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(lobby, new object[] { 1 });
                ClassData picked = lobby.Current;
                Report(sb, picked != null && picked.className.Contains("法"),
                       $"选中第二张卡 | {(picked != null ? picked.className : "★空")}", ref fail);

                lobby.StartGame();

                var stats = Object.FindObjectOfType<CharacterStats>();
                bool becameMage = switcher != null && switcher.IsMage
                                  && stats != null && stats.Class != null && stats.Class == picked;
                Report(sb, becameMage,
                       $"开始游戏会把职业装上 | 现在是用的是 {(stats != null && stats.Class != null ? stats.Class.className : "★空")}",
                       ref fail);

                // 还原成战士，别把工程里的场景状态带歪（场景本来也不存盘）
                var warriorClass = AssetDatabase.LoadAssetAtPath<ClassData>("Assets/_Project/Data/classes/Class_Warrior.asset");
                if (switcher != null && warriorClass != null) switcher.SetClass(warriorClass);
            }
            catch (System.Exception e)
            {
                Report(sb, false, $"搭大厅时抛异常 | {e.Message}", ref fail);
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                TimeFreeze.UnfreezeAll();
            }

            // 职业展示信息
            var warrior = AssetDatabase.LoadAssetAtPath<ClassData>("Assets/_Project/Data/classes/Class_Warrior.asset");
            var mage = AssetDatabase.LoadAssetAtPath<ClassData>("Assets/_Project/Data/classes/Class_Mage.asset");
            Report(sb, warrior != null && warrior.icon != null, "战士头像 | 已接（大厅卡片用）", ref fail);
            Report(sb, mage != null && mage.icon != null, "法师头像 | 已接（大厅卡片用）", ref fail);
            Report(sb, warrior != null && !string.IsNullOrEmpty(warrior.description), "战士说明 | 有文案", ref fail);
            Report(sb, mage != null && !string.IsNullOrEmpty(mage.description), "法师说明 | 有文案", ref fail);

            sb.AppendLine($"[SUMMARY] 失败 | {fail}");

            System.IO.Directory.CreateDirectory("_Tuanjie");
            System.IO.File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[LobbyCheck]\n" + sb);
        }

        private static System.Collections.Generic.List<string> ClassNames(PlayerVisualSwitcher switcher)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (ClassData data in switcher.Classes)
            {
                names.Add(data != null ? data.className : "空");
            }
            return names;
        }

        private static void Report(StringBuilder sb, bool ok, string text, ref int fail)
        {
            if (!ok) fail++;
            sb.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {text}");
        }

        /// <summary>反射调一个 0 参方法（比如编辑模式不会自己跑的 OnEnable），没有就跳过</summary>
        private static void InvokeIfPresent(object target, string method)
        {
            if (target == null) return;
            try
            {
                target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                      ?.Invoke(target, null);
            }
            catch (System.Exception)
            {
                // 叫不醒就算了：这个检查本来是为了避免假失败，不该自己变成新失败
            }
        }
    }
}
#endif
