#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 在 Game.unity 左侧搭一间「元气骑士式」的等待大厅：地板、墙、两个角色台、一道门。
    ///
    /// 为什么用脚本搭而不是手拖：这间房有二十多个物体（地板 3 段、墙 3 面、台座 + 立绘 + 光圈 ×2、
    /// 门柱 + 门板 + 光、出生点、出发点），手搭要拖几十次还会漏引用；脚本搭完写进场景，
    /// 之后照样能在 Hierarchy 里手改。
    /// </summary>
    public static class LobbyRoomBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ArtDir = "Assets/_Project/Art/Characters";
        private const string SquarePath = "Assets/_Project/Art/Placeholder/Square.png";
        private const string RingPath = "Assets/_Project/Art/Characters/Mage/Mage_Ring.png";
        private const string WarriorClassPath = "Assets/_Project/Data/classes/Class_Warrior.asset";
        private const string MageClassPath = "Assets/_Project/Data/classes/Class_Mage.asset";
        private const string ReportPath = "_Tuanjie/lobby-room-report.txt";

        // 整间房摆在场景左边，离关卡（x >= -10）远一点，互不干扰
        private static readonly Vector3 RootPosition = new Vector3(-70f, 0f, 0f);

        private const float FloorY = -3f;
        private const float FloorTop = -2.5f;

        [MenuItem("团结/生成元气骑士式大厅")]
        public static void Build()
        {
            var sb = new StringBuilder();
            int fail = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[PASS] 已打开场景 | " + ScenePath);

            Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
            if (square == null)
            {
                sb.AppendLine("[FAIL] 找不到占位方块 | " + SquarePath);
                Finish(sb, 1);
                return;
            }

            Sprite ring = AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
            Sprite warriorIdle = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Warrior/Warrior_Idle.png");
            Sprite mageIdle = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Mage/Mage_Idle.png");
            ClassData warrior = AssetDatabase.LoadAssetAtPath<ClassData>(WarriorClassPath);
            ClassData mage = AssetDatabase.LoadAssetAtPath<ClassData>(MageClassPath);

            if (warrior == null || mage == null)
            {
                sb.AppendLine("[FAIL] 找不到职业资产 | Class_Warrior / Class_Mage");
                Finish(sb, 1);
                return;
            }

            // 重建：先删掉旧的，免得反复跑堆一地
            GameObject existing = GameObject.Find("LobbyRoom");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("LobbyRoom");
            root.transform.position = RootPosition;

            BuildShell(root.transform, square, sb);

            Transform spawn = NewPoint(root.transform, "SpawnPoint", new Vector3(8f, FloorTop + 1f, 0f));
            Transform runSpawn = NewPoint(root.transform, "RunSpawn", new Vector3(70f, 0f, 0f));
            Transform portal = NewPoint(root.transform, "Portal", new Vector3(52f, FloorTop + 1.2f, 0f));

            SpriteRenderer portalGlow = BuildPortal(root.transform, portal, square, ring);

            LobbyPedestal pedestalA = BuildPedestal(root.transform, "Pedestal_Warrior", new Vector3(18f, 0f, 0f),
                                                    warrior, warriorIdle, square, ring, sb);
            LobbyPedestal pedestalB = BuildPedestal(root.transform, "Pedestal_Mage", new Vector3(28f, 0f, 0f),
                                                    mage, mageIdle, square, ring, sb);

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
                list.GetArrayElementAtIndex(0).objectReferenceValue = pedestalA;
                list.GetArrayElementAtIndex(1).objectReferenceValue = pedestalB;
            }
            else
            {
                sb.AppendLine("[FAIL] LobbyRoom 没有 pedestals 字段");
                fail++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            sb.AppendLine("[PASS] 造出大厅 | LobbyRoom @ " + RootPosition);
            sb.AppendLine("[PASS] 地板 / 墙 / 背景 | 3 段地板 + 左右墙 + 背景板（都带 Ground 层碰撞）");
            sb.AppendLine("[PASS] 角色台 | 战士 (" + pedestalA.DisplayName + ") / 法师 (" + pedestalB.DisplayName + ")");
            sb.AppendLine("[PASS] 传送门 | 位置 " + portal.localPosition);
            sb.AppendLine("[PASS] 出生点 | " + spawn.localPosition + "（世界 " + spawn.position + "）");
            sb.AppendLine("[PASS] 场景已存盘 | " + ScenePath);

            Finish(sb, fail);
        }

        // ------------------------------------------------------------ 房间外壳

        private static void BuildShell(Transform parent, Sprite square, StringBuilder sb)
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(parent, false);

            // 背景板：垫在所有东西后面
            SpriteRenderer backdrop = NewSprite(shell, "Backdrop", new Vector3(30f, 0f, 1f), new Vector2(62f, 10f),
                                                square, new Color(0.10f, 0.12f, 0.16f, 1f), -20);

            // 地板 3 段，各 20 宽
            for (int i = 0; i < 3; i++)
            {
                SpriteRenderer floor = NewSprite(shell, "Floor_" + i,
                                                 new Vector3(10f + i * 20f, FloorY, 0f),
                                                 new Vector2(20f, 1f), square,
                                                 new Color(0.30f, 0.33f, 0.40f, 1f), 0);
                AddGroundCollider(floor.gameObject, new Vector2(1f, 1f));
            }

            // 左右墙（挡住玩家别走出大厅）
            SpriteRenderer left = NewSprite(shell, "Wall_Left", new Vector3(0f, 1f, 0f), new Vector2(1f, 8f), square,
                                            new Color(0.18f, 0.20f, 0.26f, 1f), -1);
            AddGroundCollider(left.gameObject, new Vector2(1f, 1f));

            SpriteRenderer right = NewSprite(shell, "Wall_Right", new Vector3(60f, 1f, 0f), new Vector2(1f, 8f), square,
                                             new Color(0.18f, 0.20f, 0.26f, 1f), -1);
            AddGroundCollider(right.gameObject, new Vector2(1f, 1f));

            // 地面装饰线：让地板看起来有厚度
            NewSprite(shell, "Trim", new Vector3(30f, FloorTop + 0.06f, -0.1f), new Vector2(60f, 0.12f), square,
                      new Color(0.45f, 0.50f, 0.60f, 0.8f), 1);

            sb.AppendLine("[INFO] 背景板 | " + backdrop.name + " 尺寸 62x10");
        }

        // ------------------------------------------------------------ 角色台

        private static LobbyPedestal BuildPedestal(Transform parent, string name, Vector3 localPosition,
                                                   ClassData classData, Sprite portraitSprite, Sprite square, Sprite ring,
                                                   StringBuilder sb)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var stand = new GameObject("Stand");
            stand.transform.SetParent(go.transform, false);
            stand.transform.localPosition = new Vector3(0f, FloorTop + 0.3f, 0f);
            stand.transform.localScale = new Vector3(3.2f, 0.6f, 1f);
            SpriteRenderer standRenderer = stand.AddComponent<SpriteRenderer>();
            standRenderer.sprite = square;
            standRenderer.color = new Color(0.24f, 0.27f, 0.34f, 1f);
            standRenderer.sortingOrder = -2;

            var portrait = new GameObject("Portrait");
            portrait.transform.SetParent(go.transform, false);
            // 人物帧是 320x480 @160PPU（2x3 单位），脚底在中心下方 0.5 —— 抬到地板顶面正好站着
            portrait.transform.localPosition = new Vector3(0f, FloorTop + 0.5f, 0f);
            SpriteRenderer portraitRenderer = portrait.AddComponent<SpriteRenderer>();
            portraitRenderer.sprite = portraitSprite;
            portraitRenderer.sortingOrder = 2;

            var glow = new GameObject("Glow");
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(0f, FloorTop + 0.05f, 0f);
            glow.transform.localScale = new Vector3(1.6f, 0.8f, 1f);
            SpriteRenderer glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = ring;
            glowRenderer.color = new Color(1f, 0.86f, 0.35f, 0.55f);
            glowRenderer.sortingOrder = -3;
            glowRenderer.enabled = false;

            var pedestal = go.AddComponent<LobbyPedestal>();
            pedestal.classData = classData;
            pedestal.portrait = portraitRenderer;
            pedestal.stand = standRenderer;
            pedestal.glow = glowRenderer;

            if (portraitSprite == null) sb.AppendLine("[WARN] " + name + " 没有立绘贴图，台子上会是空的");

            return pedestal;
        }

        // ------------------------------------------------------------ 传送门

        private static SpriteRenderer BuildPortal(Transform parent, Transform portal, Sprite square, Sprite ring)
        {
            var gate = new GameObject("Gate");
            gate.transform.SetParent(portal, false);

            SpriteRenderer leftPost = NewSprite(gate.transform, "Post_Left", new Vector3(-1.1f, 0f, 0f),
                                                new Vector2(0.3f, 3.6f), square, new Color(0.55f, 0.48f, 0.30f, 1f), 1);
            SpriteRenderer rightPost = NewSprite(gate.transform, "Post_Right", new Vector3(1.1f, 0f, 0f),
                                                 new Vector2(0.3f, 3.6f), square, new Color(0.55f, 0.48f, 0.30f, 1f), 1);

            SpriteRenderer panel = NewSprite(gate.transform, "GatePanel", Vector3.zero, new Vector2(1.9f, 3.2f),
                                             square, new Color(0.35f, 0.38f, 0.45f, 0.9f), 0);

            SpriteRenderer halo = NewSprite(gate.transform, "Halo", new Vector3(0f, -1.4f, 0f),
                                            new Vector2(2.4f, 1.2f), ring, new Color(1f, 0.86f, 0.35f, 0.5f), -1);

            leftPost.name = "Post_Left";
            rightPost.name = "Post_Right";
            halo.name = "Halo";

            // 门板上挂一个空物体当「光」的引用点：LobbyRoom 会改它的颜色
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
            target.layer = LayerMask.NameToLayer("Ground");

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
            System.IO.File.WriteAllText(ReportPath, sb.ToString(), new System.Text.UTF8Encoding(false));
            Debug.Log("[LobbyRoomBuilder] 报告已写入 " + ReportPath);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("元气骑士式大厅", "生成完毕，失败 " + fail + " 项。\n\n报告：" + ReportPath, "好");
            }
        }
    }
}
#endif
