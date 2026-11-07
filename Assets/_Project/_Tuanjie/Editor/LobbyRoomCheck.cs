#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 验「元气骑士式大厅」：场景里那间房搭齐了没有、进大厅会不会真的把玩家挪进去、
    /// 走近台子能不能选职业、走到门口能不能出发。
    ///
    /// 批处理下不能按 Play（`EnterPlaymode` 会卡在加载备份场景上），所以这里用反射直接调
    /// 运行时会跑的那几个方法，等价于「玩家已经在场景里走了两步」。
    /// </summary>
    public static class LobbyRoomCheck
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ReportPath = "_Tuanjie/lobby-room-check-report.txt";

        private static readonly List<string> Lines = new List<string>();

        [MenuItem("团结/验证元气骑士式大厅")]
        public static void Run()
        {
            Lines.Clear();
            int fail = 0;
            int pass = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Info(sb => sb.AppendLine("[PASS]|已打开场景|" + ScenePath), ref pass);

            LobbyRoom room = UnityEngine.Object.FindObjectOfType<LobbyRoom>();
            Check(room != null, "[FAIL]|场景里没有 LobbyRoom|先跑「团结/生成元气骑士式大厅」", "[PASS]|大厅组件|LobbyRoom 在场景里", ref fail, ref pass);
            if (room == null)
            {
                Finish(fail, pass);
                return;
            }

            Check(room.Pedestals != null && room.Pedestals.Count == 2,
                  "[FAIL]|角色台数量|期望 2 个，实际 " + (room.Pedestals == null ? 0 : room.Pedestals.Count),
                  "[PASS]|角色台数量|2 个", ref fail, ref pass);

            for (int i = 0; i < room.Pedestals.Count; i++)
            {
                LobbyPedestal pedestal = room.Pedestals[i];
                string tag = "角色台 " + i;
                Check(pedestal != null && pedestal.classData != null,
                      "[FAIL]|" + tag + "|没接职业", "[PASS]|" + tag + "|" + (pedestal != null ? pedestal.DisplayName : "?"),
                      ref fail, ref pass);
                Check(pedestal != null && pedestal.portrait != null && pedestal.portrait.sprite != null,
                      "[FAIL]|" + tag + " 立绘|没有贴图（台子上会是空的）",
                      "[PASS]|" + tag + " 立绘|" + (pedestal != null && pedestal.portrait != null ? pedestal.portrait.sprite.name : "?"),
                      ref fail, ref pass);
            }

            // 场地：地板要有碰撞，否则玩家会掉下去
            GameObject floor = GameObject.Find("LobbyRoom/Shell/Floor_1");
            BoxCollider2D floorCollider = floor != null ? floor.GetComponent<BoxCollider2D>() : null;
            Check(floorCollider != null && floor.layer == LayerMask.NameToLayer("Ground"),
                  "[FAIL]|地板碰撞|Floor_1 缺 BoxCollider2D 或不在 Ground 层",
                  "[PASS]|地板碰撞|Floor_1 在 Ground 层，带 BoxCollider2D", ref fail, ref pass);

            // RoomManager 得能"停"和"从大厅开局"
            RoomManager rooms = UnityEngine.Object.FindObjectOfType<RoomManager>();
            MethodInfo suspend = typeof(RoomManager).GetMethod("SuspendForLobby", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo startRun = typeof(RoomManager).GetMethod("StartRunFromLobby", BindingFlags.Instance | BindingFlags.Public);
            Check(rooms != null && suspend != null && startRun != null,
                  "[FAIL]|RoomManager 大厅接口|缺 SuspendForLobby / StartRunFromLobby",
                  "[PASS]|RoomManager 大厅接口|SuspendForLobby / StartRunFromLobby 都在", ref fail, ref pass);

            // GameBootstrap 拿到的类型要对
            PropertyInfo lobbyProperty = typeof(GameBootstrap).GetProperty("Lobby", BindingFlags.Static | BindingFlags.Public);
            Check(lobbyProperty != null && lobbyProperty.PropertyType == typeof(LobbyRoom),
                  "[FAIL]|启动入口类型|GameBootstrap.Lobby 不是 LobbyRoom",
                  "[PASS]|启动入口类型|GameBootstrap.Lobby 是 LobbyRoom", ref fail, ref pass);

            // ---- 真跑一遍：进大厅 → 选职业 → 出发
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Check(player != null, "[FAIL]|找不到玩家|场景里没有 Tag=Player 的物体", "[PASS]|玩家|找到 Player", ref fail, ref pass);
            if (player == null || rooms == null)
            {
                Finish(fail, pass);
                return;
            }

            Vector3 playerOrigin = player.transform.position;
            RoomManager roomManager = rooms;
            bool attackerWasOn = false;
            bool casterWasOn = false;
            PlayerAttacker attacker = player.GetComponent<PlayerAttacker>();
            SkillCaster caster = player.GetComponent<SkillCaster>();
            PlayerVisualSwitcher switcher = player.GetComponent<PlayerVisualSwitcher>();

            try
            {
                AwakeAll(player);
                AwakeAll(room.gameObject);

                if (attacker != null) attackerWasOn = attacker.enabled;
                if (caster != null) casterWasOn = caster.enabled;

                room.Enter();

                bool suspended = (bool)(typeof(RoomManager)
                    .GetField("_suspended", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(roomManager) ?? false);
                Check(suspended, "[FAIL]|进大厅会停建房|RoomManager 还在建房", "[PASS]|进大厅会停建房|RoomManager 已待命", ref fail, ref pass);

                float toSpawn = Vector2.Distance(player.transform.position,
                    GameObject.Find("LobbyRoom/SpawnPoint").transform.position);
                Check(toSpawn < 1.5f, "[FAIL]|玩家被挪进大厅|离出生点 " + toSpawn.ToString("0.00"),
                      "[PASS]|玩家被挪进大厅|离出生点 " + toSpawn.ToString("0.00"), ref fail, ref pass);

                Check(attacker != null && !attacker.enabled && caster != null && !caster.enabled,
                      "[FAIL]|大厅里不开火|攻击/技能还开着", "[PASS]|大厅里不开火|攻击与技能已停", ref fail, ref pass);

                Transform hud = room.transform.Find("LobbyHud");
                Check(hud != null && hud.gameObject.activeSelf,
                      "[FAIL]|大厅提示文字|没搭出 LobbyHud", "[PASS]|大厅提示文字|LobbyHud 已显示", ref fail, ref pass);

                // 走近第二个台子（法师）
                player.transform.position = room.Pedestals[1].transform.position;
                InvokePrivate(room, "Update");
                Check(room.FocusIndex == 1, "[FAIL]|走近台子会被识别|FocusIndex=" + room.FocusIndex,
                      "[PASS]|走近台子会被识别|FocusIndex=1（法师台）", ref fail, ref pass);

                room.Select(1);
                Check(room.HasChosen, "[FAIL]|按 E 能选职业|HasChosen=False", "[PASS]|按 E 能选职业|已选 " + (switcher != null ? switcher.CurrentClassName : "?"),
                      ref fail, ref pass);

                if (switcher != null)
                {
                    Check(switcher.CurrentClassName != null && switcher.CurrentClassName.Contains("法"),
                          "[FAIL]|选了法师就换人|当前是 " + switcher.CurrentClassName,
                          "[PASS]|选了法师就换人|当前是 " + switcher.CurrentClassName, ref fail, ref pass);
                }

                // 走到门口出发
                Transform portal = GameObject.Find("LobbyRoom/Portal").transform;
                player.transform.position = portal.position;
                InvokePrivate(room, "Update");
                Check(room.PortalFocused, "[FAIL]|走到门口会被识别|PortalFocused=False",
                      "[PASS]|走到门口会被识别|PortalFocused=True", ref fail, ref pass);

                room.Leave();

                bool stillSuspended = (bool)(typeof(RoomManager)
                    .GetField("_suspended", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(roomManager) ?? true);
                Check(!stillSuspended, "[FAIL]|出发会恢复建房|RoomManager 还在停着",
                      "[PASS]|出发会恢复建房|RoomManager 已恢复", ref fail, ref pass);

                Check(roomManager.CurrentRoom != null, "[FAIL]|出发后建出第一间|CurrentRoom 为空",
                      "[PASS]|出发后建出第一间|" + (roomManager.CurrentRoom != null ? roomManager.CurrentRoom.name : "?"), ref fail, ref pass);

                Check(attacker != null && attacker.enabled && caster != null && caster.enabled,
                      "[FAIL]|出发后战斗恢复|攻击/技能还停着", "[PASS]|出发后战斗恢复|攻击与技能已恢复", ref fail, ref pass);
            }
            catch (Exception e)
            {
                Lines.Add("[FAIL]|大厅流程抛异常|" + e.GetType().Name + ": " + e.Message);
                fail++;
            }
            finally
            {
                // 还原现场：玩家位置、组件开关、房间、静态标记，一律不留痕
                player.transform.position = playerOrigin;
                if (attacker != null) attacker.enabled = attackerWasOn;
                if (caster != null) caster.enabled = casterWasOn;

                rooms.SuspendForLobby();
                GameBootstrap.ShowLobbyOnStart = true;

                Transform hud = room.transform.Find("LobbyHud");
                if (hud != null) UnityEngine.Object.DestroyImmediate(hud.gameObject);
            }

            Finish(fail, pass);
        }

        // ------------------------------------------------------------ 工具

        private static void Check(bool ok, string failLine, string passLine, ref int fail, ref int pass)
        {
            if (ok)
            {
                Lines.Add(passLine);
                pass++;
            }
            else
            {
                Lines.Add(failLine);
                fail++;
            }
        }

        private static void Info(Action<StringBuilder> write, ref int pass)
        {
            var sb = new StringBuilder();
            write(sb);
            Lines.Add(sb.ToString().TrimEnd());
            pass++;
        }

        /// <summary>
        /// 编辑模式不会自己跑 Awake：自检里要手动叫一遍，
        /// 只叫游戏程序集里的、且声明类型就是自己的（别把基类的方法重复叫）。
        /// </summary>
        private static void AwakeAll(GameObject root)
        {
            Assembly gameAssembly = typeof(LobbyRoom).Assembly;

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Assembly != gameAssembly) continue;

                MethodInfo awake = behaviour.GetType().GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, Type.EmptyTypes, null);

                if (awake == null || awake.DeclaringType != behaviour.GetType()) continue;

                try { awake.Invoke(behaviour, null); }
                catch (Exception) { /* 自检不因为某个 Awake 报错就中断 */ }
            }
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, Type.EmptyTypes, null);

            info?.Invoke(target, null);
        }

        private static void Finish(int fail, int pass)
        {
            Lines.Add("[SUMMARY] 失败 | " + fail);
            Lines.Add("[SUMMARY] 通过 | " + pass);
            System.IO.File.WriteAllText(ReportPath, string.Join("\n", Lines) + "\n", new UTF8Encoding(false));
            Debug.Log("[LobbyRoomCheck] 报告已写入 " + ReportPath);
        }
    }
}
#endif
