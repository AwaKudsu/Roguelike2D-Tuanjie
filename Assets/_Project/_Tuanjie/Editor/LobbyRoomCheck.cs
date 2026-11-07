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
    /// 验「元气骑士式俯视大厅」：房间搭齐没有、进大厅人是不是被藏起来不能动、
    /// 鼠标选中 + 确认之后能不能走、走到门口会不会出发。
    ///
    /// 批处理下不能按 Play（`EnterPlaymode` 会卡在加载备份场景上），所以这里用反射直接调
    /// 运行时会跑的那几个方法，等价于「玩家已经点完角色、走到门口」。
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
            Lines.Add("[PASS]|已打开场景|" + ScenePath);
            pass++;

            LobbyRoom room = UnityEngine.Object.FindObjectOfType<LobbyRoom>();
            if (room == null)
            {
                Lines.Add("[FAIL]|场景里没有 LobbyRoom|先跑「团结/生成元气骑士式大厅」");
                Finish(1, pass);
                return;
            }

            Lines.Add("[PASS]|大厅组件|LobbyRoom 在场景里");
            pass++;

            Check(room.Pedestals != null && room.Pedestals.Count == 2,
                  "[FAIL]|角色立牌数量|期望 2 个，实际 " + (room.Pedestals == null ? 0 : room.Pedestals.Count),
                  "[PASS]|角色立牌数量|2 个", ref fail, ref pass);

            for (int i = 0; i < room.Pedestals.Count; i++)
            {
                LobbyPedestal pedestal = room.Pedestals[i];
                string tag = "立牌 " + i;
                Check(pedestal != null && pedestal.classData != null,
                      "[FAIL]|" + tag + "|没接职业",
                      "[PASS]|" + tag + "|" + (pedestal != null ? pedestal.DisplayName : "?"), ref fail, ref pass);
                Check(pedestal != null && pedestal.portrait != null && pedestal.portrait.sprite != null,
                      "[FAIL]|" + tag + " 立绘|没有贴图",
                      "[PASS]|" + tag + " 立绘|" + (pedestal != null && pedestal.portrait != null ? pedestal.portrait.sprite.name : "?"),
                      ref fail, ref pass);
            }

            GameObject floor = GameObject.Find("LobbyRoom/Shell/Floor");
            BoxCollider2D floorCollider = floor != null ? floor.GetComponent<BoxCollider2D>() : null;
            Check(floorCollider != null && floor.layer == LayerMask.NameToLayer("Ground"),
                  "[FAIL]|地板碰撞|Floor 缺 BoxCollider2D 或不在 Ground 层",
                  "[PASS]|地板碰撞|Floor 在 Ground 层，带 BoxCollider2D", ref fail, ref pass);

            int wallCount = 0;
            foreach (string wall in new[] { "Wall_Top", "Wall_Bottom", "Wall_Left", "Wall_Right" })
            {
                GameObject go = GameObject.Find("LobbyRoom/Shell/" + wall);
                if (go != null && go.GetComponent<BoxCollider2D>() != null) wallCount++;
            }

            Check(wallCount == 4, "[FAIL]|四面墙|只有 " + wallCount + " 面带碰撞",
                  "[PASS]|四面墙|4 面都在，带碰撞", ref fail, ref pass);

            RoomManager rooms = UnityEngine.Object.FindObjectOfType<RoomManager>();
            MethodInfo suspend = typeof(RoomManager).GetMethod("SuspendForLobby", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo startRun = typeof(RoomManager).GetMethod("StartRunFromLobby", BindingFlags.Instance | BindingFlags.Public);
            Check(rooms != null && suspend != null && startRun != null,
                  "[FAIL]|RoomManager 大厅接口|缺 SuspendForLobby / StartRunFromLobby",
                  "[PASS]|RoomManager 大厅接口|SuspendForLobby / StartRunFromLobby 都在", ref fail, ref pass);

            PropertyInfo lobbyProperty = typeof(GameBootstrap).GetProperty("Lobby", BindingFlags.Static | BindingFlags.Public);
            Check(lobbyProperty != null && lobbyProperty.PropertyType == typeof(LobbyRoom),
                  "[FAIL]|启动入口类型|GameBootstrap.Lobby 不是 LobbyRoom",
                  "[PASS]|启动入口类型|GameBootstrap.Lobby 是 LobbyRoom", ref fail, ref pass);

            Check(typeof(LobbyAvatar) != null && typeof(LobbyRoom).GetMethod("Pick") != null,
                  "[FAIL]|俯视走路组件|缺 LobbyAvatar 或 Pick()",
                  "[PASS]|俯视走路组件|LobbyAvatar + Pick/ConfirmPick 都在", ref fail, ref pass);

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Lines.Add("[FAIL]|找不到玩家|场景里没有 Tag=Player 的物体");
                Finish(fail + 1, pass);
                return;
            }

            Lines.Add("[PASS]|玩家|找到 Player");
            pass++;

            Vector3 playerOrigin = player.transform.position;
            bool attackerWasOn = false;
            bool casterWasOn = false;
            bool controllerWasOn = false;
            float cameraBefore = 0f;
            Transform cameraParentBefore = null;
            Vector3 cameraLocalBefore = Vector3.zero;

            PlayerAttacker attacker = player.GetComponent<PlayerAttacker>();
            SkillCaster caster = player.GetComponent<SkillCaster>();
            PlayerController controller = player.GetComponent<PlayerController>();
            PlayerVisualSwitcher switcher = player.GetComponent<PlayerVisualSwitcher>();
            SpriteRenderer sprite = player.GetComponent<SpriteRenderer>();
            Camera camera = Camera.main;

            HUDController hudComponent = UnityEngine.Object.FindObjectOfType<HUDController>();
            Canvas hudCanvas = hudComponent != null ? hudComponent.GetComponentInParent<Canvas>() : null;
            bool hudWasEnabled = hudCanvas != null && hudCanvas.enabled;

            try
            {
                AwakeAll(player);
                AwakeAll(room.gameObject);

                if (attacker != null) attackerWasOn = attacker.enabled;
                if (caster != null) casterWasOn = caster.enabled;
                if (controller != null) controllerWasOn = controller.enabled;
                if (camera != null)
                {
                    cameraBefore = camera.orthographicSize;
                    cameraParentBefore = camera.transform.parent;
                    cameraLocalBefore = camera.transform.localPosition;
                }

                room.Enter();

                Check(room.IsSelecting, "[FAIL]|进大厅是「选人」状态|" + room.IsInside,
                      "[PASS]|进大厅是「选人」状态|等待鼠标点角色", ref fail, ref pass);

                bool suspended = (bool)(typeof(RoomManager)
                    .GetField("_suspended", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(rooms) ?? false);
                Check(suspended, "[FAIL]|进大厅会停建房|RoomManager 还在建房",
                      "[PASS]|进大厅会停建房|RoomManager 已待命", ref fail, ref pass);

                float toSpawn = Vector2.Distance(player.transform.position,
                    GameObject.Find("LobbyRoom/SpawnPoint").transform.position);
                Check(toSpawn < 1.5f, "[FAIL]|玩家被放进暗房间|离出生点 " + toSpawn.ToString("0.00"),
                      "[PASS]|玩家被放进暗房间|离出生点 " + toSpawn.ToString("0.00"), ref fail, ref pass);

                Check(sprite != null && !sprite.enabled,
                      "[FAIL]|确认前玩家不露面|人物已经画出来了", "[PASS]|确认前玩家不露面|还没选人，人物是隐藏的", ref fail, ref pass);

                LobbyAvatar avatar = player.GetComponent<LobbyAvatar>();
                Check(avatar != null && !avatar.IsActive,
                      "[FAIL]|确认前不能动|俯视控制器已经开了", "[PASS]|确认前不能动|俯视控制器没开", ref fail, ref pass);

                Check(controller != null && !controller.enabled && attacker != null && !attacker.enabled,
                      "[FAIL]|大厅里横板控制器与战斗都关着|还有组件开着",
                      "[PASS]|大厅里横板控制器与战斗都关着|重力/跳跃/攻击全停", ref fail, ref pass);

                Vector3 roomCenter = room.transform.position + new Vector3(10f, 5f, 0f);
                float centerOffset = camera != null
                    ? Vector2.Distance(camera.transform.position, roomCenter)
                    : 999f;
                Check(camera != null && centerOffset < 0.05f,
                      "[FAIL]|相机对准房间正中|偏了 " + centerOffset.ToString("0.00"),
                      "[PASS]|相机对准房间正中|偏 " + centerOffset.ToString("0.00"), ref fail, ref pass);

                float halfW = camera != null ? camera.orthographicSize * camera.aspect : 0f;
                float halfH = camera != null ? camera.orthographicSize : 0f;
                Check(halfW >= 10f + 0.5f && halfH >= 5f + 0.5f,
                      "[FAIL]|整间房都在画面里|可视 " + (halfW * 2f).ToString("0.0") + " x " + (halfH * 2f).ToString("0.0"),
                      "[PASS]|整间房都在画面里|可视 " + (halfW * 2f).ToString("0.0") + " x " + (halfH * 2f).ToString("0.0") +
                      "（房 20 x 10）", ref fail, ref pass);

                Check(hudCanvas != null && !hudCanvas.enabled,
                      "[FAIL]|大厅里不显示血条蓝条|HUD 还画着",
                      "[PASS]|大厅里不显示血条蓝条|HUD 已关", ref fail, ref pass);

                Transform hud = room.transform.Find("LobbyHud");
                Check(hud != null && hud.gameObject.activeSelf,
                      "[FAIL]|大厅提示界面|没搭出 LobbyHud", "[PASS]|大厅提示界面|LobbyHud 已显示", ref fail, ref pass);

                // 鼠标选中（自检里直接调 Pick，等价于点中第二个人）
                room.Pick(1);
                Transform panel = room.transform.Find("LobbyHud/ConfirmPanel");
                Check(room.PickedIndex == 1 && panel != null && panel.gameObject.activeSelf,
                      "[FAIL]|选中角色会弹确认框|PickedIndex=" + room.PickedIndex,
                      "[PASS]|选中角色会弹确认框|已选法师，确认框出现", ref fail, ref pass);

                room.ConfirmPick();
                Check(room.IsConfirmed, "[FAIL]|确认后进入可走动状态|仍是选人",
                      "[PASS]|确认后进入可走动状态|可以走动了", ref fail, ref pass);

                Check(avatar != null && avatar.IsActive, "[FAIL]|确认后俯视控制器打开|没打开",
                      "[PASS]|确认后俯视控制器打开|四方向走路生效", ref fail, ref pass);

                Check(sprite != null && sprite.enabled, "[FAIL]|确认后玩家出现|人物还是隐藏的",
                      "[PASS]|确认后玩家出现|按选的职业显示", ref fail, ref pass);

                Check(switcher != null && switcher.CurrentClassName != null && switcher.CurrentClassName.Contains("法"),
                      "[FAIL]|确认后职业已装上|当前是 " + (switcher != null ? switcher.CurrentClassName : "?"),
                      "[PASS]|确认后职业已装上|当前是 " + (switcher != null ? switcher.CurrentClassName : "?"), ref fail, ref pass);

                // 走到门口
                Transform portal = GameObject.Find("LobbyRoom/Portal").transform;
                player.transform.position = portal.position;
                InvokePrivate(room, "Update");
                Check(room.IsNearDoor, "[FAIL]|走到门口会被识别|IsNearDoor=False",
                      "[PASS]|走到门口会被识别|IsNearDoor=True", ref fail, ref pass);

                room.Leave();

                Check(!room.IsInside, "[FAIL]|出发后离开大厅|IsInside 仍为 True",
                      "[PASS]|出发后离开大厅|", ref fail, ref pass);

                bool stillSuspended = (bool)(typeof(RoomManager)
                    .GetField("_suspended", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(rooms) ?? true);
                Check(!stillSuspended, "[FAIL]|出发会恢复建房|RoomManager 还在停着",
                      "[PASS]|出发会恢复建房|RoomManager 已恢复", ref fail, ref pass);

                Check(rooms.CurrentRoom != null, "[FAIL]|出发后建出第一间|CurrentRoom 为空",
                      "[PASS]|出发后建出第一间|" + (rooms.CurrentRoom != null ? rooms.CurrentRoom.name : "?"), ref fail, ref pass);

                Check(controller != null && controller.enabled && attacker != null && attacker.enabled && caster != null && caster.enabled,
                      "[FAIL]|出发后横板控制器与战斗恢复|还有组件停着",
                      "[PASS]|出发后横板控制器与战斗恢复|重力/跳跃/攻击回来了", ref fail, ref pass);

                Check(camera != null && Mathf.Abs(camera.orthographicSize - cameraBefore) < 0.01f,
                      "[FAIL]|出发后视角还原|" + (camera != null ? camera.orthographicSize.ToString("0.0") : "?"),
                      "[PASS]|出发后视角还原|正交 " + (camera != null ? camera.orthographicSize.ToString("0.0") : "?"), ref fail, ref pass);

                Check(camera != null && camera.transform.parent == cameraParentBefore,
                      "[FAIL]|出发后相机回到玩家身上|父级是 " + (camera != null && camera.transform.parent != null ? camera.transform.parent.name : "无"),
                      "[PASS]|出发后相机回到玩家身上|父级 " + (camera != null && camera.transform.parent != null ? camera.transform.parent.name : "无"), ref fail, ref pass);

                Check(hudCanvas != null && hudCanvas.enabled == hudWasEnabled,
                      "[FAIL]|出发后血条蓝条回来|HUD 没恢复",
                      "[PASS]|出发后血条蓝条回来|HUD 已恢复", ref fail, ref pass);
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Lines.Add("[FAIL]|大厅流程抛异常|" + inner.GetType().Name + ": " + inner.Message);
                fail++;
            }
            finally
            {
                player.transform.position = playerOrigin;
                if (attacker != null) attacker.enabled = attackerWasOn;
                if (caster != null) caster.enabled = casterWasOn;
                if (controller != null) controller.enabled = controllerWasOn;
                if (sprite != null) sprite.enabled = true;
                if (camera != null && cameraBefore > 0.1f) camera.orthographicSize = cameraBefore;
                if (camera != null && cameraParentBefore != null && camera.transform.parent != cameraParentBefore)
                {
                    camera.transform.SetParent(cameraParentBefore, false);
                    camera.transform.localPosition = cameraLocalBefore;
                }

                if (hudCanvas != null) hudCanvas.enabled = hudWasEnabled;

                LobbyAvatar avatar = player.GetComponent<LobbyAvatar>();
                if (avatar != null) avatar.SetActive(false);

                Rigidbody2D body = player.GetComponent<Rigidbody2D>();
                if (body != null) body.gravityScale = 1f;

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
