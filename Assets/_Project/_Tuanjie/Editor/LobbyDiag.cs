#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 大厅取景诊断：把「进大厅之后相机能看见什么」逐项打出来 —— 相机位置/视野、玩家位置、
    /// 房间各块的尺寸与中心、立牌位置、HUD 是否还挂着。
    /// 用于回答「为什么画面里房间偏上、玩家看不见、血条还在」这类问题。
    /// </summary>
    public static class LobbyDiag
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ReportPath = "_Tuanjie/lobby-diag.txt";

        [MenuItem("团结/诊断大厅取景")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var sb = new StringBuilder();
            LobbyRoom room = UnityEngine.Object.FindObjectOfType<LobbyRoom>();
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Camera camera = Camera.main;

            if (room == null || player == null || camera == null)
            {
                Write("# 缺东西\nroom=" + (room != null) + " player=" + (player != null) + " camera=" + (camera != null));
                return;
            }

            AwakeAll(player);
            AwakeAll(room.gameObject);

            room.Enter();

            sb.AppendLine("# 相机");
            sb.AppendLine("  位置 = " + V3(camera.transform.position) + "  父级 = " + (camera.transform.parent != null ? camera.transform.parent.name : "无"));
            sb.AppendLine("  正交 = " + camera.orthographic + "  size = " + camera.orthographicSize.ToString("0.00") +
                          "  aspect = " + camera.aspect.ToString("0.000"));
            sb.AppendLine("  可视范围 X[" + (camera.transform.position.x - camera.orthographicSize * camera.aspect).ToString("0.00") +
                          ", " + (camera.transform.position.x + camera.orthographicSize * camera.aspect).ToString("0.00") + "]" +
                          " Y[" + (camera.transform.position.y - camera.orthographicSize).ToString("0.00") +
                          ", " + (camera.transform.position.y + camera.orthographicSize).ToString("0.00") + "]");

            sb.AppendLine("# 玩家");
            sb.AppendLine("  位置 = " + V3(player.transform.position));
            SpriteRenderer playerSprite = player.GetComponent<SpriteRenderer>();
            sb.AppendLine("  贴图 = " + (playerSprite != null && playerSprite.sprite != null ? playerSprite.sprite.name : "★空") +
                          "  显示 = " + (playerSprite != null && playerSprite.enabled));
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            sb.AppendLine("  刚体 = " + (body != null ? body.bodyType + " 重力=" + body.gravityScale + " 速度=" + body.velocity : "无"));

            sb.AppendLine("# 大厅里的物件（世界坐标 / 世界尺寸）");
            DumpTree(sb, room.transform, 1);

            sb.AppendLine("# 立牌");
            for (int i = 0; i < room.Pedestals.Count; i++)
            {
                LobbyPedestal pedestal = room.Pedestals[i];
                if (pedestal == null) continue;

                SpriteRenderer portrait = pedestal.portrait;
                sb.AppendLine("  [" + i + "] " + pedestal.name + " 位置=" + V3(pedestal.transform.position) +
                              "  立绘=" + (portrait != null && portrait.sprite != null ? portrait.sprite.name : "★空") +
                              "  立绘世界坐标=" + (portrait != null ? V3(portrait.transform.position) : "?") +
                              "  立绘世界尺寸=" + (portrait != null ? portrait.bounds.size.x.ToString("0.00") + "x" + portrait.bounds.size.y.ToString("0.00") : "?"));
            }

            sb.AppendLine("# HUD / Canvas");
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null || !canvas.gameObject.scene.IsValid()) continue;

                sb.AppendLine("  Canvas '" + PathOf(canvas.transform) + "' 模式=" + canvas.renderMode +
                              " 排序=" + canvas.sortingOrder + " 启用=" + canvas.enabled +
                              " 激活=" + canvas.gameObject.activeInHierarchy);

                int images = 0;
                foreach (Image image in canvas.GetComponentsInChildren<Image>(true))
                {
                    if (image.enabled && image.gameObject.activeInHierarchy) images++;
                }

                sb.AppendLine("    可见 Image = " + images);
            }

            Write(sb.ToString());
        }

        private static void DumpTree(StringBuilder sb, Transform parent, int depth)
        {
            foreach (Transform child in parent)
            {
                SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
                BoxCollider2D box = child.GetComponent<BoxCollider2D>();

                string line = new string(' ', depth * 2) + child.name + " 世界=" + V3(child.position);
                if (renderer != null)
                {
                    line += " 尺寸=" + renderer.bounds.size.x.ToString("0.00") + "x" + renderer.bounds.size.y.ToString("0.00");
                    line += " 贴图=" + (renderer.sprite != null ? renderer.sprite.name : "★空");
                    line += " 排序=" + renderer.sortingOrder;
                }

                if (box != null) line += " 碰撞=" + box.size.x.ToString("0.00") + "x" + box.size.y.ToString("0.00") + " 层=" + LayerMask.LayerToName(child.gameObject.layer);

                sb.AppendLine(line);
                DumpTree(sb, child, depth + 1);
            }
        }

        private static void AwakeAll(GameObject root)
        {
            Assembly gameAssembly = typeof(LobbyRoom).Assembly;

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Assembly != gameAssembly) continue;

                MethodInfo awake = behaviour.GetType().GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);

                if (awake == null || awake.DeclaringType != behaviour.GetType()) continue;

                try { awake.Invoke(behaviour, null); } catch (Exception) { }
            }
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private static string V3(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private static void Write(string text)
        {
            System.IO.File.WriteAllText(ReportPath, text, new UTF8Encoding(false));
            Debug.Log("[LobbyDiag] 报告已写入 " + ReportPath);
        }
    }
}
#endif
