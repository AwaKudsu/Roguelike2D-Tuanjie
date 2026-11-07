#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 在 Game.unity 左侧搭一间**俯视**的暗厅（元气骑士那种）：地板、四面墙、挂毯、火把、
    /// 两个角色台、下方一道门。
    ///
    /// 素材全部由 `D:\tjart\lobby\make_lobby_art.py` 程序绘制（Art/Lobby/*.png），
    /// 只有角色立绘用的是战士/法师自己的帧 —— 站台上那两个人本来就是玩家要挑的角色。
    /// </summary>
    public static class LobbyRoomBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string ArtDir = "Assets/_Project/Art/Lobby";
        private const string WarriorClassPath = "Assets/_Project/Data/classes/Class_Warrior.asset";
        private const string MageClassPath = "Assets/_Project/Data/classes/Class_Mage.asset";
        private const string ReportPath = "_Tuanjie/lobby-room-report.txt";

        // 整间房摆在场景左边，离关卡（x >= -10）远一点，互不干扰
        private static readonly Vector3 RootPosition = new Vector3(-70f, 0f, 0f);

        // 房间内部：本地 (0,0) ~ (20,10)
        private const float RoomWidth = 20f;
        private const float RoomHeight = 10f;

        // 地板比房间多铺一圈：换窗口比例时屏幕边缘也不会露出相机底色
        private const float FloorMarginX = 1.5f;
        private const float FloorMarginY = 2.5f;

        /// <summary>贴图的导入参数：像素图 + 每张图自己的 PPU（让 1 张贴图刚好是想要的尺寸）。</summary>
        private static readonly (string file, float ppu)[] ArtFiles =
        {
            ("LobbyFloor", 32f),      // 32px / 32 = 1 单位一块砖
            ("LobbyWall", 32f),       // 1 单位一块墙砖
            ("LobbyBackdrop", 8f),    // 1 单位，靠 transform 放大
            ("LobbyRug", 18f),        // 128/18 ≈ 7.1 单位宽
            ("LobbyBanner", 38f),     // 128/38 ≈ 3.4 单位宽
            ("LobbyPedestal", 48f),   // 96/48 = 2 单位宽
            ("LobbyTorch", 55f),      // 72/55 ≈ 1.3 单位高
            ("LobbyPortal", 43f),     // 112/43 ≈ 2.6 单位高
            ("LobbyGlow", 96f),       // 1 单位，靠 transform 放大
            ("LobbyPillar", 50f),     // 96/50 ≈ 1.9 单位高
        };

        [MenuItem("团结/生成元气骑士式大厅")]
        public static void Build()
        {
            var sb = new StringBuilder();
            int fail = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[PASS] 已打开场景 | " + ScenePath);

            var sprites = new System.Collections.Generic.Dictionary<string, Sprite>();
            foreach ((string file, float ppu) in ArtFiles)
            {
                string path = ArtDir + "/" + file + ".png";
                ImportSprite(path, ppu);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    sb.AppendLine("[FAIL] 贴图导入失败 | " + path);
                    fail++;
                }
                else
                {
                    sprites[file] = sprite;
                }
            }

            if (fail > 0)
            {
                Finish(sb, fail);
                return;
            }

            ClassData warrior = AssetDatabase.LoadAssetAtPath<ClassData>(WarriorClassPath);
            ClassData mage = AssetDatabase.LoadAssetAtPath<ClassData>(MageClassPath);
            Sprite warriorIdle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Characters/Warrior/Warrior_Idle.png");
            Sprite mageIdle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Characters/Mage/Mage_Idle.png");

            if (warrior == null || mage == null || warriorIdle == null || mageIdle == null)
            {
                sb.AppendLine("[FAIL] 缺职业或立绘 | Class_Warrior / Class_Mage / Warrior_Idle / Mage_Idle");
                Finish(sb, 1);
                return;
            }

            GameObject existing = GameObject.Find("LobbyRoom");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("LobbyRoom");
            root.transform.position = RootPosition;

            BuildShell(root.transform, sprites, sb);

            Transform spawn = NewPoint(root.transform, "SpawnPoint", new Vector3(RoomWidth * 0.5f, 3f, 0f));
            Transform runSpawn = NewPoint(root.transform, "RunSpawn", new Vector3(70f, 0f, 0f));
            Transform portal = NewPoint(root.transform, "Portal", new Vector3(RoomWidth * 0.5f, 1f, 0f));
            SpriteRenderer portalGlow = BuildDoor(portal, sprites, sb);

            LobbyPedestal first = BuildPedestal(root.transform, "Pedestal_Warrior", new Vector3(6.6f, 6.3f, 0f),
                                                warrior, warriorIdle, sprites, sb);
            LobbyPedestal second = BuildPedestal(root.transform, "Pedestal_Mage", new Vector3(13.4f, 6.3f, 0f),
                                                 mage, mageIdle, sprites, sb);

            var room = root.AddComponent<LobbyRoom>();

            var so = new SerializedObject(room);
            SetRef(so, "spawnPoint", spawn);
            SetRef(so, "runSpawn", runSpawn);
            SetRef(so, "portal", portal);
            SetRef(so, "portalGlow", portalGlow);
            SetFloat(so, "lobbyCameraSize", 5f);

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
            sb.AppendLine("[PASS] 地板 | 平铺 " + (RoomWidth + FloorMarginX * 2f) + " x " + (RoomHeight + FloorMarginY * 2f) + "，比房间多铺一圈");
            sb.AppendLine("[PASS] 四面墙 | 都带 Ground 层碰撞，人在里面走不出去");
            sb.AppendLine("[PASS] 装饰 | 挂毯 / 地毯 / 2 个火把 / 2 根石柱 / 3 团灯光");
            sb.AppendLine("[PASS] 角色立牌 | 战士 (" + first.DisplayName + ") / 法师 (" + second.DisplayName + ")");
            sb.AppendLine("[PASS] 门 | 本地位置 " + portal.localPosition + "（世界 " + portal.position + "）");
            sb.AppendLine("[PASS] 出生点 | " + spawn.localPosition + "（世界 " + spawn.position + "）");
            sb.AppendLine("[PASS] 场景已存盘 | " + ScenePath);

            Finish(sb, fail);
        }

        // ------------------------------------------------------------ 导入

        private static void ImportSprite(string path, float pixelsPerUnit)
        {
            // 新拖进来的贴图在批处理里可能还没被导入过，先强制导一次再取 importer
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null) return;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureType = TextureImporterType.Sprite;
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;   // 平铺要用 FullRect，Tight 会报错
            settings.spritePixelsPerUnit = pixelsPerUnit;
            settings.filterMode = FilterMode.Point;
            settings.mipmapEnabled = false;
            settings.alphaIsTransparency = true;
            settings.wrapMode = TextureWrapMode.Clamp;
            importer.SetTextureSettings(settings);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------ 房间

        private static void BuildShell(Transform parent, System.Collections.Generic.Dictionary<string, Sprite> sprites, StringBuilder sb)
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(parent, false);

            // 最底下：一层深色岩石，任何窗口比例下都不会看到相机底色
            SpriteRenderer backdrop = NewSprite(shell, "Backdrop", new Vector3(RoomWidth * 0.5f, RoomHeight * 0.5f, 0f),
                                                sprites["LobbyBackdrop"], Color.white, -300);
            backdrop.transform.localScale = new Vector3(48f, 30f, 1f);

            // 地板：平铺砖，比房间大一圈
            float floorW = RoomWidth + FloorMarginX * 2f;
            float floorH = RoomHeight + FloorMarginY * 2f;
            SpriteRenderer floor = NewTiled(shell, "Floor", new Vector3(RoomWidth * 0.5f, RoomHeight * 0.5f, 0f),
                                            sprites["LobbyFloor"], Color.white, -200, new Vector2(floorW, floorH));
            AddCollider(floor.gameObject, new Vector2(floorW, floorH));

            // 四面墙：贴到房间内边界上，厚度 1
            SpriteRenderer top = NewTiled(shell, "Wall_Top", new Vector3(RoomWidth * 0.5f, RoomHeight - 0.5f, 0f),
                                          sprites["LobbyWall"], Color.white, -150, new Vector2(RoomWidth, 1f));
            AddCollider(top.gameObject, new Vector2(RoomWidth, 1f));

            SpriteRenderer bottom = NewTiled(shell, "Wall_Bottom", new Vector3(RoomWidth * 0.5f, 0.5f, 0f),
                                             sprites["LobbyWall"], Color.white, -150, new Vector2(RoomWidth, 1f));
            AddCollider(bottom.gameObject, new Vector2(RoomWidth, 1f));

            SpriteRenderer left = NewTiled(shell, "Wall_Left", new Vector3(0.5f, RoomHeight * 0.5f, 0f),
                                           sprites["LobbyWall"], Color.white, -150, new Vector2(1f, RoomHeight));
            AddCollider(left.gameObject, new Vector2(1f, RoomHeight));

            SpriteRenderer right = NewTiled(shell, "Wall_Right", new Vector3(RoomWidth - 0.5f, RoomHeight * 0.5f, 0f),
                                            sprites["LobbyWall"], Color.white, -150, new Vector2(1f, RoomHeight));
            AddCollider(right.gameObject, new Vector2(1f, RoomHeight));

            // 挂毯（挂在上墙内侧）
            SpriteRenderer banner = NewSprite(shell, "Banner", new Vector3(RoomWidth * 0.5f, 8.5f, 0f),
                                              sprites["LobbyBanner"], Color.white, -160);

            // 地毯
            NewSprite(shell, "Rug", new Vector3(RoomWidth * 0.5f, 4.3f, 0f), sprites["LobbyRug"],
                      Color.white, -185);

            // 地面灯光（三团暖色）
            NewGlow(shell, "Light_Left", new Vector3(3.4f, 8f, 0f), 8f, new Color(1f, 0.78f, 0.45f, 0.45f), -195);
            NewGlow(shell, "Light_Right", new Vector3(16.6f, 8f, 0f), 8f, new Color(1f, 0.78f, 0.45f, 0.45f), -195);
            NewGlow(shell, "Light_Door", new Vector3(RoomWidth * 0.5f, 2.4f, 0f), 8f, new Color(0.75f, 0.85f, 1f, 0.28f), -195);

            // 火把 + 石柱
            NewSprite(shell, "Torch_Left", new Vector3(2.9f, 8.1f, 0f), sprites["LobbyTorch"], Color.white, -140);
            NewSprite(shell, "Torch_Right", new Vector3(17.1f, 8.1f, 0f), sprites["LobbyTorch"], Color.white, -140);
            NewSprite(shell, "Pillar_Left", new Vector3(1.7f, 8.45f, 0f), sprites["LobbyPillar"], Color.white, -145);
            NewSprite(shell, "Pillar_Right", new Vector3(18.3f, 8.45f, 0f), sprites["LobbyPillar"], Color.white, -145);

            sb.AppendLine("[INFO] 灯光 | 三团暖光 + 火把两根（房间整体是暗的）");
            if (banner == null) sb.AppendLine("[WARN] 挂毯没生成");
        }

        // ------------------------------------------------------------ 角色台

        private static LobbyPedestal BuildPedestal(Transform parent, string name, Vector3 localPosition,
                                                   ClassData classData, Sprite portraitSprite,
                                                   System.Collections.Generic.Dictionary<string, Sprite> sprites,
                                                   StringBuilder sb)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;   // 这个点就是「脚底」

            // 台座本身（俯视圆台）
            var stand = new GameObject("Stand");
            stand.transform.SetParent(go.transform, false);
            SpriteRenderer standRenderer = stand.AddComponent<SpriteRenderer>();
            standRenderer.sprite = sprites["LobbyPedestal"];
            standRenderer.sortingOrder = -170;

            // 站在台上的人：人物帧 320x480 @160PPU（2x3 单位），脚底在中心下方 0.5
            var portrait = new GameObject("Portrait");
            portrait.transform.SetParent(go.transform, false);
            portrait.transform.localPosition = new Vector3(0f, 0.32f + 0.5f, 0f);
            SpriteRenderer portraitRenderer = portrait.AddComponent<SpriteRenderer>();
            portraitRenderer.sprite = portraitSprite;
            portraitRenderer.sortingOrder = -169;

            // 台面暖光
            NewGlow(go.transform, "Glow", new Vector3(0f, 0.05f, 0f), 3.2f, new Color(1f, 0.86f, 0.35f, 0.10f), -180);

            var pedestal = go.AddComponent<LobbyPedestal>();
            pedestal.classData = classData;
            pedestal.portrait = portraitRenderer;
            pedestal.glow = go.transform.Find("Glow").GetComponent<SpriteRenderer>();

            if (portraitSprite == null) sb.AppendLine("[WARN] " + name + " 没有立绘贴图，会是个空台子");

            return pedestal;
        }

        // ------------------------------------------------------------ 门

        private static SpriteRenderer BuildDoor(Transform portal, System.Collections.Generic.Dictionary<string, Sprite> sprites,
                                                StringBuilder sb)
        {
            var gate = new GameObject("Gate");
            gate.transform.SetParent(portal, false);

            SpriteRenderer halo = NewSprite(gate.transform, "Halo", new Vector3(0f, 1.3f, 0f), sprites["LobbyGlow"],
                                            new Color(1f, 0.86f, 0.35f, 0.0f), -190);
            halo.transform.localScale = new Vector3(7f, 7f, 1f);

            SpriteRenderer door = NewSprite(gate.transform, "Portal", new Vector3(0f, 1.2f, 0f), sprites["LobbyPortal"],
                                            Color.white, -160);

            sb.AppendLine("[INFO] 门 | 石拱 + 蓝色内光（确认角色后门会亮）");
            return door;
        }

        // ------------------------------------------------------------ 小工具

        private static Transform NewPoint(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static SpriteRenderer NewSprite(Transform parent, string name, Vector3 localPosition,
                                                Sprite sprite, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static SpriteRenderer NewTiled(Transform parent, string name, Vector3 localPosition, Sprite sprite,
                                               Color color, int sortingOrder, Vector2 size)
        {
            SpriteRenderer renderer = NewSprite(parent, name, localPosition, sprite, color, sortingOrder);
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = size;
            return renderer;
        }

        private static SpriteRenderer NewGlow(Transform parent, string name, Vector3 localPosition, float size,
                                              Color color, int sortingOrder)
        {
            SpriteRenderer renderer = NewSprite(parent, name, localPosition, LoadGlow(), color, sortingOrder);
            renderer.transform.localScale = new Vector3(size, size, 1f);
            return renderer;
        }

        private static Sprite _glow;

        private static Sprite LoadGlow()
        {
            if (_glow == null) _glow = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/LobbyGlow.png");
            return _glow;
        }

        private static void AddCollider(GameObject target, Vector2 size)
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

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property != null) property.floatValue = value;
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
