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
        private const string ArcherClassPath = "Assets/_Project/Data/classes/Class_Archer.asset";
        private const string ReportPath = "_Tuanjie/lobby-room-report.txt";

        // 整间房摆在场景左边，离关卡（x >= -10）远一点，互不干扰
        private static readonly Vector3 RootPosition = new Vector3(-70f, 0f, 0f);

        // 房间内部：本地 (0,0) ~ (20,10)
        private const float RoomWidth = 20f;
        private const float RoomHeight = 10f;

        // 地板比房间多铺一圈：换窗口比例时屏幕边缘也不会露出相机底色，
        // 顺带留出相机跟人走时能挪动的余量（见 LobbyRoom.ClampCameraToFloor）
        private const float FloorMarginX = 5f;
        private const float FloorMarginY = 3.5f;

        // 火把：挂在房间上沿，贴图 1.3 单位高、中心对齐，火苗在中心上方约 0.4
        private const float TorchLeftX = 2.9f;
        private const float TorchRightX = 17.1f;
        private const float TorchY = 8.1f;
        private const float TorchFlameOffsetY = 0.4f;

        // 灯光：光斑中心对准火苗；再靠一块房间大小的 SpriteMask 把光剪在房内，不让它糊到墙外
        private const float LightSize = 4.5f;
        private const int LightOrder = -195;
        private const int LightMaskBack = -199;    // 只遮灯光这一档（地板 -200、地毯 -185 都不动）
        private const int LightMaskFront = -190;

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
            ("LobbyBookshelf", 480f), // 1424/480 ≈ 3.0 单位高（脚底对齐用 bounds 算，不写死）
            ("LobbyNoticeBoard", 470f),// 1152/470 ≈ 2.5 单位宽
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
            ClassData archer = AssetDatabase.LoadAssetAtPath<ClassData>(ArcherClassPath);
            Sprite warriorIdle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Characters/Warrior/Warrior_Idle.png");
            Sprite mageIdle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Characters/Mage/Mage_Idle.png");
            Sprite archerIdle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Characters/Archer/Archer_Idle.png");

            if (warrior == null || mage == null || warriorIdle == null || mageIdle == null)
            {
                sb.AppendLine("[FAIL] 缺职业或立绘 | Class_Warrior / Class_Mage / Warrior_Idle / Mage_Idle");
                Finish(sb, 1);
                return;
            }

            if (archer == null || archerIdle == null)
            {
                sb.AppendLine("[WARN] 缺射手 | Class_Archer / Archer_Idle 没找到，这次只摆两座台");
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

            // 三座台均匀摆开：房间内部 20 宽，5.4 的间距既不会挤在一起，
            // 也能让鼠标在 6 号正交视野下轻松点到中间那个
            LobbyPedestal first = BuildPedestal(root.transform, "Pedestal_Warrior", new Vector3(4.6f, 6.3f, 0f),
                                                warrior, warriorIdle, sprites, sb);
            LobbyPedestal middle = archer != null && archerIdle != null
                ? BuildPedestal(root.transform, "Pedestal_Archer", new Vector3(10f, 6.3f, 0f),
                                archer, archerIdle, sprites, sb)
                : null;
            LobbyPedestal second = BuildPedestal(root.transform, "Pedestal_Mage", new Vector3(15.4f, 6.3f, 0f),
                                                 mage, mageIdle, sprites, sb);

            var room = root.AddComponent<LobbyRoom>();

            var so = new SerializedObject(room);
            SetRef(so, "spawnPoint", spawn);
            SetRef(so, "runSpawn", runSpawn);
            SetRef(so, "portal", portal);
            SetRef(so, "portalGlow", portalGlow);
            SetFloat(so, "lobbyCameraSize", 6f);
            SetBool(so, "cameraFollowsPlayer", true);
            SetFloat(so, "cameraFollowLerp", 8f);
            SetVector(so, "floorPadding", new Vector2(FloorMarginX, FloorMarginY));

            SerializedProperty list = so.FindProperty("pedestals");
            if (list != null)
            {
                int count = middle != null ? 3 : 2;
                list.arraySize = count;

                int at = 0;
                list.GetArrayElementAtIndex(at++).objectReferenceValue = first;
                if (middle != null) list.GetArrayElementAtIndex(at++).objectReferenceValue = middle;
                list.GetArrayElementAtIndex(at).objectReferenceValue = second;
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
            sb.AppendLine("[PASS] 角色立牌 | 战士 (" + first.DisplayName + ")" +
                          (middle != null ? " / 射手 (" + middle.DisplayName + ")" : "") +
                          " / 法师 (" + second.DisplayName + ")" +
                          (middle != null ? "" : "（射手资产没找到，只摆了两座）"));
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
            // 地板不挂碰撞体：俯视大厅没有重力，玩家是靠速度驱动的 Dynamic 刚体，
            // 一整间房大小的实体盒子会把人从内部挤出去（曾把确认后的角色顶到上墙夹住）。
            // 房间边界交给下面四面墙的碰撞体。

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
            // ★ 排序必须比台座(-170)和立绘(-169)更低：中间那座台是射手，
            //   人和挂毯在画面上是重叠的，挂毯画在前面就会把射手的头挡住（曾经就是这样）。
            //   -176 = 在石台光圈(-180)之后、台座(-170)之前，人站在挂毯前面。
            SpriteRenderer banner = NewSprite(shell, "Banner", new Vector3(RoomWidth * 0.5f, 8.5f, 0f),
                                              sprites["LobbyBanner"], Color.white, -176);

            // 地毯
            NewSprite(shell, "Rug", new Vector3(RoomWidth * 0.5f, 4.3f, 0f), sprites["LobbyRug"],
                      Color.white, -185);

            // 灯光：光斑的中心挂在火苗上（不是火把杆上），尺寸 4.5 能让光铺到墙根
            // 火苗在 y = 8.1 + 0.4；光斑一半会越过上墙，交给下面的 LightMask 剪掉
            float flameY = TorchY + TorchFlameOffsetY;
            NewLight(shell, "Light_Left", new Vector3(TorchLeftX, flameY, 0f), LightSize,
                     new Color(1f, 0.78f, 0.45f, 0.3f));
            NewLight(shell, "Light_Right", new Vector3(TorchRightX, flameY, 0f), LightSize,
                     new Color(1f, 0.78f, 0.45f, 0.3f));
            NewLight(shell, "Light_Door", new Vector3(RoomWidth * 0.5f, 2.4f, 0f), LightSize,
                     new Color(0.75f, 0.85f, 1f, 0.18f));

            // 火把 + 石柱
            NewSprite(shell, "Torch_Left", new Vector3(TorchLeftX, TorchY, 0f), sprites["LobbyTorch"], Color.white, -140);
            NewSprite(shell, "Torch_Right", new Vector3(TorchRightX, TorchY, 0f), sprites["LobbyTorch"], Color.white, -140);
            NewSprite(shell, "Pillar_Left", new Vector3(1.7f, 8.45f, 0f), sprites["LobbyPillar"], Color.white, -145);
            NewSprite(shell, "Pillar_Right", new Vector3(18.3f, 8.45f, 0f), sprites["LobbyPillar"], Color.white, -145);

            // 光的「墙」：一块和房间一样大的遮罩，只作用在灯光那一档
            BuildLightMask(shell, sprites["LobbyBackdrop"]);

            BuildProps(shell, sprites, sb);

            sb.AppendLine("[INFO] 灯光 | 两团暖光挂在火苗上 + 门光；SpriteMask 把光剪在房间内，不糊到墙外");
            if (banner == null) sb.AppendLine("[WARN] 挂毯没生成");
        }

        // ------------------------------------------------------------ 中世纪陈设

        /// <summary>
        /// 墙根的中世纪陈设：书架（纯装饰）、告示板（技能树交互点）。
        ///
        /// 摆放规则只有一条：**只写在墙根**（y 贴近 1），因为大厅是俯视的，
        /// 房间中间要留给玩家走位和两座石台，往中间摆东西只会挡路。
        /// </summary>
        private static void BuildProps(Transform shell,
                                       System.Collections.Generic.Dictionary<string, Sprite> sprites,
                                       StringBuilder sb)
        {
            // 书架：贴后墙，左侧（靠门那一侧留给火把）。开口朝右，镜像一下才是「正面」。
            // 高度 2.3 单位（原图按 480 PPU 是 2.97，压一点更像"家具"而不是"柜子"）。
            // 脚底放在 7.7，顶就是 10.0，正好齐墙面，不会顶穿到墙外的石头上。
            // 0.34 是「挡人的厚度」：俯视视角下，家具真正占地的只有贴地那一条，做浅一点走位更舒服。
            NewProp(shell, "Bookshelf", new Vector2(5.0f, 7.7f),
                    sprites["LobbyBookshelf"], targetHeight: 2.3f, flipX: true, solidDepth: 0.34f);

            // 告示板：也在后墙，靠右一点，和书架一左一右夹住中间的门。
            // 1.8 单位高：原图 1.84，几乎原大；脚底 8.15 + 1.8 = 9.95，落在墙面那一格里。
            GameObject boardGo = NewProp(shell, "NoticeBoard", new Vector2(14.6f, 8.15f),
                                         sprites["LobbyNoticeBoard"], targetHeight: 1.8f, solidDepth: 0.26f);
            if (boardGo != null)
            {
                LobbyInteractable board = boardGo.AddComponent<LobbyInteractable>();
                board.prompt = "查看技能树";
                board.title = "技能树";
                board.body = "这里以后放「技能树」：花点数解锁/强化技能。\n现在只是个占位 —— 交互链路先跑通。";
                board.radius = 1.6f;

                SpriteRenderer boardRenderer = boardGo.GetComponent<SpriteRenderer>();
                // 光圈要落在脚底：告示板的轴心在中心，子物体得往回挪半个高度
                float boardHalfHeight = boardRenderer != null && boardRenderer.sprite != null
                    ? boardRenderer.sprite.bounds.size.y * 0.5f
                    : 0f;
                SpriteRenderer glow = NewGlow(boardGo.transform, "Glow",
                                              new Vector3(0f, -boardHalfHeight + 0.08f, 0f), 2.4f,
                                              board.idleColor,
                                              boardRenderer != null ? boardRenderer.sortingOrder - 1 : -1);
                board.glow = glow;
            }

            sb.AppendLine("[INFO] 陈设 | 后墙：书架(5.0) + 告示板(14.6，技能树交互点，按 E 弹占位面板) —— 两件都带脚底碰撞盒（0.34 / 0.26 厚），走过去会被挡住");
        }

        /// <summary>
        /// 放一个「脚底对齐」的道具：贴图轴心在中心，所以位置要往上抬半个高度。
        /// 排序按 y 算，和 <see cref="LobbyAvatar"/> 的规则一致；同一深度时压在人后面 1 档。
        /// <paramref name="targetHeight"/> 大于 0 时按它缩放 —— 两张陈设图的分辨率差得远
        /// （书架 800×1424、告示板 1152×864），不归一化的话一个两米九、一个快两米，站在一块儿不像一套家具。
        /// <paramref name="solidDepth"/> 大于 0 时给道具加一个「站在地上的盒子」（只覆盖脚底那一块，
        /// 不是整张贴图那么高）—— 俯视游戏里家具挡路就靠这个，人走过去会被挡在正面。
        /// </summary>
        private static GameObject NewProp(Transform parent, string name, Vector2 feetPosition, Sprite sprite,
                                          float targetHeight = 0f, bool flipX = false, float solidDepth = 0f)
        {
            if (sprite == null) return null;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            float spriteHeight = sprite.bounds.size.y;
            float spriteWidth = sprite.bounds.size.x;
            float scale = targetHeight > 0.01f && spriteHeight > 0.01f ? targetHeight / spriteHeight : 1f;
            go.transform.localScale = new Vector3(scale, scale, 1f);

            float halfHeight = spriteHeight * scale * 0.5f;
            go.transform.localPosition = new Vector3(feetPosition.x, feetPosition.y + halfHeight, 0f);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.flipX = flipX;
            renderer.sortingOrder = Mathf.RoundToInt((10f - feetPosition.y) * 6f) - 1;

            if (solidDepth > 0.01f && scale > 0.0001f)
            {
                // 盒子的 size/offset 都在「局部单位」里（会再乘回 localScale）。
                // 竖直方向只盖脚底一段：贴图往上画了两三米，但真正挡人的只有下面这半米；
                // 竖直的那两项要除 scale（局部单位），横向本来就是按贴图宽度算的，不用除。
                // 宽度只取贴图的 0.72：家具两边是「视觉边缘」，留一点余量，人贴着边走过去不会卡住。
                var body = go.AddComponent<BoxCollider2D>();
                body.size = new Vector2(spriteWidth * 0.72f, solidDepth / scale);
                body.offset = new Vector2(0f, -spriteHeight * 0.5f + solidDepth * 0.5f / scale);
            }

            return go;
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

        /// <summary>
        /// 一团灯光：和 <see cref="NewGlow"/> 一样，但只画在 LightMask 里面 —— 光不会穿墙。
        /// </summary>
        private static SpriteRenderer NewLight(Transform parent, string name, Vector3 localPosition, float size, Color color)
        {
            SpriteRenderer renderer = NewGlow(parent, name, localPosition, size, color, LightOrder);
            renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            return renderer;
        }

        /// <summary>
        /// 让光不穿墙：一块和房间一样大的 <see cref="SpriteMask"/>，只作用于 sortingOrder 落在灯光那一档的渲染器。
        ///
        /// SpriteMask 拿 sprite 的 alpha 当遮罩，这里借不透明的岩石贴图放大成整间房 —— 遮罩自己不渲染，
        /// 所以就算把 1 单位的贴图拉成 20×10 也看不出来。范围用 custom range 卡死，
        /// 否则地板（-200）、地毯（-185）、石台光圈（-180）会被一起剪掉。
        /// </summary>
        private static void BuildLightMask(Transform parent, Sprite maskSprite)
        {
            var go = new GameObject("LightMask");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(RoomWidth * 0.5f, RoomHeight * 0.5f, 0f);

            Vector2 spriteSize = maskSprite != null ? (Vector2)maskSprite.bounds.size : Vector2.one;
            go.transform.localScale = new Vector3(RoomWidth / Mathf.Max(0.0001f, spriteSize.x),
                                                  RoomHeight / Mathf.Max(0.0001f, spriteSize.y), 1f);

            var mask = go.AddComponent<SpriteMask>();
            mask.sprite = maskSprite;
            mask.isCustomRangeActive = true;
            mask.backSortingOrder = LightMaskBack;
            mask.frontSortingOrder = LightMaskFront;
            mask.alphaCutoff = 0.2f;
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

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property != null) property.boolValue = value;
        }

        private static void SetVector(SerializedObject so, string field, Vector2 value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property != null) property.vector2Value = value;
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
