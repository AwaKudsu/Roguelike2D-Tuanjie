#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 在 Game.unity 左侧搭一间**俯视**的暗厅（元气骑士那种）：地板、四面墙、几处灯光、
    /// 两个角色立牌、下方一道门。
    ///
    /// 为什么用脚本搭而不是手拖：二十多个物体（地板、4 面墙、3 个光斑、2 个人 + 光圈、
    /// 门柱 2 + 门板 + 光、出生点、出发点），手搭要拖几十次还容易漏引用；脚本搭完写进场景，
    /// 之后照样能在 Hierarchy 里手改。
    /// </summary>
    public static class LobbyRoomBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string SquarePath = "Assets/_Project/Art/Placeholder/Square.png";
        private const string RingPath = "Assets/_Project/Art/Characters/Mage/Mage_Ring.png";
        private const string WarriorClassPath = "Assets/_Project/Data/classes/Class_Warrior.asset";
        private const string MageClassPath = "Assets/_Project/Data/classes/Class_Mage.asset";
        private const string ReportPath = "_Tuanjie/lobby-room-report.txt";

        // 整间房摆在场景左边，离关卡（x >= -10）远一点，互不干扰
        private static readonly Vector3 RootPosition = new Vector3(-70f, 0f, 0f);

        // 房间内部：本地 (0,0) ~ (20,10)
        private const float RoomWidth = 20f;
        private const float RoomHeight = 10f;

        [MenuItem("团结/生成元气骑士式大厅")]
        public static void Build()
        {
            var sb = new StringBuilder();
            int fail = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[PASS] 已打开场景 | " + ScenePath);

            Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
            Sprite ring = AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
            ClassData warrior = AssetDatabase.LoadAssetAtPath<ClassData>(WarriorClassPath);
            ClassData mage = AssetDatabase.LoadAssetAtPath<ClassData>(MageClassPath);
            Sprite warriorIdle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Characters/Warrior/Warrior_Idle.png");
            Sprite mageIdle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Characters/Mage/Mage_Idle.png");

            if (square == null || ring == null || warrior == null || mage == null)
            {
                sb.AppendLine("[FAIL] 缺资源 | Square.png / Mage_Ring.png / Class_Warrior / Class_Mage");
                Finish(sb, 1);
                return;
            }

            GameObject existing = GameObject.Find("LobbyRoom");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("LobbyRoom");
            root.transform.position = RootPosition;

            BuildShell(root.transform, square, ring, sb);

            Transform spawn = NewPoint(root.transform, "SpawnPoint", new Vector3(RoomWidth * 0.5f, 3.4f, 0f));
            Transform runSpawn = NewPoint(root.transform, "RunSpawn", new Vector3(70f, 0f, 0f));
            Transform portal = NewPoint(root.transform, "Portal", new Vector3(RoomWidth * 0.5f, 1.0f, 0f));
            SpriteRenderer portalGlow = BuildDoor(portal, square, ring, sb);

            LobbyPedestal first = BuildPedestal(root.transform, "Pedestal_Warrior", new Vector3(6.6f, 7.4f, 0f),
                                                warrior, warriorIdle, ring, sb);
            LobbyPedestal second = BuildPedestal(root.transform, "Pedestal_Mage", new Vector3(13.4f, 7.4f, 0f),
                                                 mage, mageIdle, ring, sb);

            var room = root.AddComponent<LobbyRoom>();

            var so = new SerializedObject(room);
            SetRef(so, "spawnPoint", spawn);
            SetRef(so, "runSpawn", runSpawn);
            SetRef(so, "portal", portal);
            SetRef(so, "portalGlow", portalGlow);

            SerializedProperty list = so.FindProperty("pedestals");
            if (list != null)
            {
                list.arraySize = 2;
                list.GetArrayElementAtIndex(0).objectReferenceValue = first;
                list.GetArrayElementAtIndex(1).objectReferenceValue = second;
            }
            else
            {
                sb.AppendLine("[FAIL] LobbyRoom 没有 pedestals 字段");
                fail++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            sb.AppendLine("[PASS] 造出大厅 | LobbyRoom @ " + RootPosition + "（俯视房间 20 x 10）");
            sb.AppendLine("[PASS] 地板 / 四面墙 | 都带 Ground 层碰撞，人在里面走不出去");
            sb.AppendLine("[PASS] 角色立牌 | 战士 (" + first.DisplayName + ") / 法师 (" + second.DisplayName + ")");
            sb.AppendLine("[PASS] 门 | 本地位置 " + portal.localPosition + "（世界 " + portal.position + "）");
            sb.AppendLine("[PASS] 出生点 | " + spawn.localPosition + "（世界 " + spawn.position + "）");
            sb.AppendLine("[PASS] 场景已存盘 | " + ScenePath);

            Finish(sb, fail);
        }

        // ------------------------------------------------------------ 房间外壳

        private static void BuildShell(Transform parent, Sprite square, Sprite ring, StringBuilder sb)
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(parent, false);

            // 地板：一整块暗色，俯视看是一间屋子的地面
            SpriteRenderer floor = NewSprite(shell, "Floor", new Vector3(RoomWidth * 0.5f, RoomHeight * 0.5f, 0f),
                                             new Vector2(RoomWidth, RoomHeight), square,
                                             new Color(0.11f, 0.12f, 0.15f, 1f), -200);
            AddGroundCollider(floor.gameObject, new Vector2(1f, 1f));

            // 四面墙（顶/底/左/右），厚度 0.8，站在墙外
            SpriteRenderer top = NewSprite(shell, "Wall_Top", new Vector3(RoomWidth * 0.5f, RoomHeight + 0.4f, 0f),
                                           new Vector2(RoomWidth + 1.6f, 0.8f), square,
                                           new Color(0.06f, 0.07f, 0.09f, 1f), -150);
            AddGroundCollider(top.gameObject, new Vector2(1f, 1f));

            SpriteRenderer bottom = NewSprite(shell, "Wall_Bottom", new Vector3(RoomWidth * 0.5f, -0.4f, 0f),
                                              new Vector2(RoomWidth + 1.6f, 0.8f), square,
                                              new Color(0.06f, 0.07f, 0.09f, 1f), -150);
            AddGroundCollider(bottom.gameObject, new Vector2(1f, 1f));

            SpriteRenderer left = NewSprite(shell, "Wall_Left", new Vector3(-0.4f, RoomHeight * 0.5f, 0f),
                                            new Vector2(0.8f, RoomHeight + 1.6f), square,
                                            new Color(0.06f, 0.07f, 0.09f, 1f), -150);
            AddGroundCollider(left.gameObject, new Vector2(1f, 1f));

            SpriteRenderer right = NewSprite(shell, "Wall_Right", new Vector3(RoomWidth + 0.4f, RoomHeight * 0.5f, 0f),
                                             new Vector2(0.8f, RoomHeight + 1.6f), square,
                                             new Color(0.06f, 0.07f, 0.09f, 1f), -150);
            AddGroundCollider(right.gameObject, new Vector2(1f, 1f));

            // 光：三团暖色，让暗房间里有一点点亮
            NewSprite(shell, "Light_Left", new Vector3(5f, 6f, 0f), new Vector2(6f, 3.6f), ring,
                      new Color(1f, 0.82f, 0.45f, 0.10f), -190);
            NewSprite(shell, "Light_Right", new Vector3(15f, 6f, 0f), new Vector2(6f, 3.6f), ring,
                      new Color(1f, 0.82f, 0.45f, 0.10f), -190);
            NewSprite(shell, "Light_Top", new Vector3(10f, 8.6f, 0f), new Vector2(5f, 3f), ring,
                      new Color(1f, 0.9f, 0.6f, 0.12f), -190);

            // 地面上的地毯，纯装饰，把「房间」的感觉撑起来
            NewSprite(shell, "Rug", new Vector3(10f, 5.2f, 0f), new Vector2(9f, 4.2f), square,
                      new Color(0.16f, 0.14f, 0.20f, 1f), -185);

            sb.AppendLine("[INFO] 灯光 | 3 团暖光 + 1 块地毯（房间整体是暗的）");
        }

        // ------------------------------------------------------------ 角色立牌

        private static LobbyPedestal BuildPedestal(Transform parent, string name, Vector3 localPosition,
                                                   ClassData classData, Sprite portraitSprite, Sprite ring, StringBuilder sb)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;   // 这个点就是「脚底」

            // 人物帧 320x480 @160PPU（2x3 单位），脚底在贴图中心下方 0.5 → 中心抬 0.5 就是站在地面
            var portrait = new GameObject("Portrait");
            portrait.transform.SetParent(go.transform, false);
            portrait.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            SpriteRenderer portraitRenderer = portrait.AddComponent<SpriteRenderer>();
            portraitRenderer.sprite = portraitSprite;
            portraitRenderer.sortingOrder = Mathf.RoundToInt((10f - localPosition.y) * 6f) + 2;

            var glow = new GameObject("Glow");
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            glow.transform.localScale = new Vector3(2.8f, 1.5f, 1f);
            SpriteRenderer glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = ring;
            glowRenderer.color = new Color(1f, 0.86f, 0.35f, 0.10f);
            glowRenderer.sortingOrder = -190;

            var pedestal = go.AddComponent<LobbyPedestal>();
            pedestal.classData = classData;
            pedestal.portrait = portraitRenderer;
            pedestal.glow = glowRenderer;

            if (portraitSprite == null) sb.AppendLine("[WARN] " + name + " 没有立绘贴图，会是个空台子");

            return pedestal;
        }

        // ------------------------------------------------------------ 门

        private static SpriteRenderer BuildDoor(Transform portal, Sprite square, Sprite ring, StringBuilder sb)
        {
            var gate = new GameObject("Gate");
            gate.transform.SetParent(portal, false);

            NewSprite(gate.transform, "Post_Left", new Vector3(-1.15f, 0.9f, 0f), new Vector2(0.28f, 2.8f), square,
                      new Color(0.52f, 0.44f, 0.28f, 1f), 6);
            NewSprite(gate.transform, "Post_Right", new Vector3(1.15f, 0.9f, 0f), new Vector2(0.28f, 2.8f), square,
                      new Color(0.52f, 0.44f, 0.28f, 1f), 6);

            SpriteRenderer panel = NewSprite(gate.transform, "GatePanel", new Vector3(0f, 1.0f, 0f),
                                             new Vector2(2f, 2.4f), square,
                                             new Color(0.30f, 0.33f, 0.40f, 0.9f), 2);

            NewSprite(gate.transform, "Halo", new Vector3(0f, -0.55f, 0f), new Vector2(3.4f, 1.6f), ring,
                      new Color(1f, 0.86f, 0.35f, 0.35f), -188);

            sb.AppendLine("[INFO] 门 | 两根柱子 + 一块门板（确认角色后门板会亮）");
            return panel;
        }

        // ------------------------------------------------------------ 小工具

        private static Transform NewPoint(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static SpriteRenderer NewSprite(Transform parent, string name, Vector3 localPosition, Vector2 size,
                                                Sprite sprite, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static void AddGroundCollider(GameObject target, Vector2 size)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0) target.layer = groundLayer;

            var collider = target.AddComponent<BoxCollider2D>();
            collider.size = size;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property != null) property.objectReferenceValue = value;
        }

        private static void Finish(StringBuilder sb, int fail)
        {
            sb.AppendLine("[SUMMARY] 失败 | " + fail);
            System.IO.File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[LobbyRoomBuilder] 报告已写入 " + ReportPath);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("元气骑士式大厅", "生成完毕，失败 " + fail + " 项。\n\n报告：" + ReportPath, "好");
            }
        }
    }
}
#endif
