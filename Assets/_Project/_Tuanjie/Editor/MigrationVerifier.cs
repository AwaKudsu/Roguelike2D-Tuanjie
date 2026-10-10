using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 移植自检。用批处理模式跑：
    ///   Tuanjie.exe -batchmode -nographics -projectPath D:\TuanjieRoguelike2D
    ///               -executeMethod Tuanjie.PortCheck.MigrationVerifier.Run
    ///               -logFile &lt;log&gt; -quit
    /// 判定结果全部写进 <项目根&gt;/_Tuanjie/verification-report.txt，
    /// 每行格式为 [PASS]/[FAIL]/[WARN]|[检查名]|详情，便于 PowerShell 解析。
    /// </summary>
    public static class MigrationVerifier
    {
        // 用 Application.dataPath（= <工程根>/Assets）推导工程根，别依赖当前工作目录
        private static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
        private static readonly string ReportPath =
            Path.Combine(ProjectRoot, "_Tuanjie", "verification-report.txt");

        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _fail;
        private static int _warn;

        [MenuItem("团结/运行移植自检")]
        public static void Run()
        {
            _sb.Clear();
            _fail = 0;
            _warn = 0;

            Line("INFO", "编辑器版本", Application.unityVersion);
            Line("INFO", "工程路径", ProjectRoot);

            CheckScripts();
            CheckInputWrapperClass();
            CheckRequiredTypes();
            CheckScriptableObjects();
            CheckPrefabs();
            CheckSceneAssets();
            CheckBuildSettings();
            CheckInputSettings();
            CheckBrokenAssetRefs();

            Line("SUMMARY", "失败", _fail.ToString());
            Line("SUMMARY", "警告", _warn.ToString());

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, _sb.ToString(), new UTF8Encoding(false));

            Debug.Log($"[PortCheck] 报告已写入 {ReportPath} —— 失败 {_fail} 项，警告 {_warn} 项");
            if (Application.isBatchMode) EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------
        // 修复：场景/预制体引用 Unity 内置精灵（GUID 已失配）导致「运行后什么都看不见」
        //
        // 背景：原工程所有 SpriteRenderer 的 m_Sprite 都指向 Unity 内置资源：
        //   {fileID: 7482667652216324306, guid: 311925a002f4447b3a28927169b83ea6}  ← 内置白色方块（Square）
        //   {fileID: -2413806693520163455, guid: a86470a33a6bf42c4b3595704624658b}  ← 内置白色圆形（Knob）
        // 团结把内置资源包重新打包成 Resources/tuanjie_builtin_extra，这些 GUID 全部解析为空，
        // 于是工程里 18 个 SpriteRenderer（场景 Player 1 + Room_01 7 + Room_02 8 + Fireball 1 + MageBolt 1）
        // 全部变成无 sprite，运行时只剩相机背景色和 HUD。
        // 修法：按「原来的形状」各生成一个白色占位精灵，再把失配引用改指到对应形状。
        // 注意不能一律换成方块 —— 两个投射物（Fireball / MageBolt）用的圆形 Knob，
        // 全换方块会让法术从圆的变成方的。
        // ------------------------------------------------------------------

        public static readonly string PlaceholderDir = "Assets/_Project/Art/Placeholder";
        public static readonly string PlaceholderSquarePath = "Assets/_Project/Art/Placeholder/Square.png";
        public static readonly string PlaceholderCirclePath = "Assets/_Project/Art/Placeholder/Circle.png";

        // 原内置资源 fileID → 占位精灵路径。改指后 fileID 会变成 PNG 里的 21300000。
        private static readonly Dictionary<long, string> BuiltInSpriteMap = new Dictionary<long, string>
        {
            { 7482667652216324306L, PlaceholderSquarePath },   // 内置 Square
            { -2413806693520163455L, PlaceholderCirclePath },  // 内置 Knob（圆）
        };

        [MenuItem("团结/修复精灵引用")]
        public static void FixBrokenSprites()
        {
            var log = new StringBuilder();
            var square = CreatePlaceholderSprite(PlaceholderSquarePath, 32, false);
            var circle = CreatePlaceholderSprite(PlaceholderCirclePath, 32, true);
            log.AppendLine($"[占位精灵] 方块 {PlaceholderSquarePath} guid={AssetDatabase.AssetPathToGUID(PlaceholderSquarePath)}");
            log.AppendLine($"[占位精灵] 圆形 {PlaceholderCirclePath} guid={AssetDatabase.AssetPathToGUID(PlaceholderCirclePath)}");

            var sprites = new Dictionary<long, Sprite>
            {
                { 7482667652216324306L, square },
                { -2413806693520163455L, circle },
            };

            int fixedTotal = 0, shaped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:SceneAsset t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                int n = FixOneAsset(path, square);
                if (n > 0) { log.AppendLine($"  {path} → 填补 {n} 处无形状信息的失效引用（按方块处理）"); fixedTotal += n; }
                int m = RemapByOldInstanceId(path, sprites);
                if (m > 0) { log.AppendLine($"  {path} → 按原形状改指 {m} 处"); shaped += m; }
            }
            log.AppendLine($"[修复] 按方块填补 {fixedTotal} 处；按原形状改指 {shaped} 处");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var outPath = Path.Combine(ProjectRoot, "_Tuanjie", "sprite-fix-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, log.ToString(), new UTF8Encoding(false));
            Debug.Log("[PortCheck] 精灵修复完成：" + log.ToString().Replace("\r", "").Replace("\n", " | "));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// 按「引用里记录的原始内置 fileID」把 sprite 改指到对应形状的占位精灵。
        /// 这是必须的：内置资源失配后 sprite 是 null，光看对象层分不出它原本是方块还是圆，
        /// 只能读 SerializedProperty.objectReferenceInstanceIDValue 才知道原形状。
        /// </summary>
        private static int RemapByOldInstanceId(string path, Dictionary<long, Sprite> sprites)
        {
            int changed = 0;
            var isScene = path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

            if (isScene)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var sr in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true))
                    changed += ReplaceSprite(sr, "m_Sprite", sprites);
                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                    EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
                return changed;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                    changed += ReplaceSprite(sr, "m_Sprite", sprites);
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changed;
        }

        private static int ReplaceSprite(Component comp, string propName, Dictionary<long, Sprite> sprites)
        {
            if (comp == null) return 0;
            var so = new SerializedObject(comp);
            var p = so.FindProperty(propName);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) return 0;

            long oldId = p.objectReferenceInstanceIDValue;
            if (!sprites.TryGetValue(oldId, out var target) || target == null) return 0;

            // 已经是目标精灵就不用动（本方法可重复执行）
            if (p.objectReferenceValue == target) return 0;

            p.objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();
            return 1;
        }

        // ------------------------------------------------------------------
        // 校验投射物用的圆形占位精灵：确认 Circle.png 可加载，且两个投射物预制体
        // （Fireball / MageBolt）的 SpriteRenderer 真的取到了它。
        // ------------------------------------------------------------------
        [MenuItem("团结/校验投射物圆精灵")]
        public static void VerifyCircleSprites()
        {
            var sb = new StringBuilder();
            string circlePath = null;
            if (Directory.Exists(PlaceholderDir))
            {
                foreach (var f in Directory.GetFiles(PlaceholderDir, "Circle.png", SearchOption.AllDirectories))
                    circlePath = f.Replace('\\', '/');
            }

            if (string.IsNullOrEmpty(circlePath))
            {
                sb.AppendLine("[FAIL] 找不到 Circle.png");
                WriteVerifyReport(sb.ToString(), 1);
                return;
            }

            sb.AppendLine($"[INFO] 圆精灵资产 | {circlePath}");
            sb.AppendLine($"[INFO] guid | {AssetDatabase.AssetPathToGUID(circlePath)}");

            var circle = AssetDatabase.LoadAssetAtPath<Sprite>(circlePath);
            if (circle == null)
            {
                sb.AppendLine("[FAIL] 资产加载 | Circle.png 没有导入成 Sprite");
                WriteVerifyReport(sb.ToString(), 1);
                return;
            }

            var tex = circle.texture;
            sb.AppendLine($"[PASS] 资产加载 | sprite='{circle.name}' 贴图={tex.width}x{tex.height} rect={circle.rect.width}x{circle.rect.height} ppu={circle.pixelsPerUnit}");

            int fail = 0;
            foreach (var p in new[] { "Assets/_Project/Prefabs/MageBolt.prefab", "Assets/_Project/Prefabs/Fireball.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(p);
                try
                {
                    var sr = root.GetComponentInChildren<SpriteRenderer>(true);
                    if (sr == null) { sb.AppendLine($"[FAIL] {Path.GetFileName(p)} | 没有 SpriteRenderer"); fail++; }
                    else if (sr.sprite == null) { sb.AppendLine($"[FAIL] {Path.GetFileName(p)} | sprite 仍是 null"); fail++; }
                    else if (sr.sprite.name != circle.name)
                    {
                        var t = sr.sprite.texture;
                        sb.AppendLine($"[FAIL] {Path.GetFileName(p)} | sprite='{sr.sprite.name}'（{t.width}x{t.height}）不是圆精灵");
                        fail++;
                    }
                    else sb.AppendLine($"[PASS] {Path.GetFileName(p)} | sprite='{sr.sprite.name}'（{sr.sprite.texture.width}x{sr.sprite.texture.height}）颜色={sr.color}");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            sb.AppendLine(fail == 0 ? "[SUMMARY] 属性校验 | 全部通过" : "[SUMMARY] 属性校验 | 失败");
            WriteVerifyReport(sb.ToString(), fail);
        }

        private static void WriteVerifyReport(string content, int fail)
        {
            var outPath = Path.Combine(ProjectRoot, "_Tuanjie", "circle-sprite-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, content, new UTF8Encoding(false));
            Debug.Log("[PortCheck] 圆精灵校验：" + content.Replace("\r", "").Replace("\n", " | "));
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        private static int FixOneAsset(string path, Sprite sprite)
        {
            int fixedCount = 0;
            var isScene = path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

            if (isScene)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var sr in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true))
                    if (IsBrokenSpriteRef(sr, "m_Sprite")) { sr.sprite = sprite; fixedCount++; }

                foreach (var img in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>(true))
                    if (IsBrokenSpriteRef(img, "m_Sprite")) { img.sprite = sprite; fixedCount++; }

                if (fixedCount > 0)
                {
                    EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                    EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
                return fixedCount;
            }

            // 预制体必须先把内容加载进来再扫（工程里未打开的预制体不在 FindObjectsOfType 的搜索范围内）
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                    if (IsBrokenSpriteRef(sr, "m_Sprite")) { sr.sprite = sprite; fixedCount++; }

                foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    if (IsBrokenSpriteRef(img, "m_Sprite")) { img.sprite = sprite; fixedCount++; }

                if (fixedCount > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return fixedCount;
        }

        // ------------------------------------------------------------------
        // 扫描场景与预制体里所有失效的资产引用（内置资源失配 / 丢 GUID）
        // ------------------------------------------------------------------
        private static void CheckBrokenAssetRefs()
        {
            var broken = new List<string>();
            int checkedRefs = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:SceneAsset t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                        ScanBrokenRefs(go, path, broken, ref checkedRefs);
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                try { ScanBrokenRefs(root, path, broken, ref checkedRefs); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            Line("INFO", "扫描的资产引用数", checkedRefs.ToString());
            if (broken.Count == 0)
                Line("PASS", "失效资产引用", "0 处");
            else
            {
                _warn += broken.Count;
                foreach (var b in broken) Line("WARN", "失效资产引用", b);
            }
        }

        private static void ScanBrokenRefs(GameObject go, string assetPath, List<string> broken, ref int checkedRefs)
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null)   // 组件本身取不到 = Missing Script，由别的检查项负责
                    continue;

                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (it.objectReferenceInstanceIDValue == 0) continue;   // 本来就是空的，正常
                    checkedRefs++;
                    if (it.objectReferenceValue != null) continue;          // 引用有效
                    // fileID 非 0 却取不到对象 = 引用失配
                    broken.Add($"{PathOf(go)} → {comp.GetType().Name}.{it.name}");
                }
            }
            foreach (Transform child in go.transform)
                ScanBrokenRefs(child.gameObject, assetPath, broken, ref checkedRefs);
        }

        /// <summary>
        /// 判断精灵引用是否已失效。注意：失效引用反序列化后 sprite 就是 null，
        /// 无法从对象层区分「从来没设过」和「指向了不存在的资产」，必须看 SerializedProperty：
        /// 走本地 fileID 引用（objectReferenceInstanceIDValue != 0）却取不到资产 —— 就是失配的内置资源引用。
        /// </summary>
        private static bool IsBrokenSpriteRef(Component c, string propName)
        {
            if (c == null) return false;
            var so = new SerializedObject(c);
            var p = so.FindProperty(propName);
            if (p == null) return false;
            if (p.objectReferenceValue != null) return false;
            // 内置资源走本地 fileID 引用；工程内资产走 GUID。fileID 非 0 而资产为空 → 内置资源失配。
            return p.objectReferenceInstanceIDValue != 0;
        }

        /// <summary>
        /// 生成 32x32 纯白占位精灵并作为 Sprite 导入（pixelsPerUnit=32 → 世界尺寸正好 1 单位）。
        /// round=true 时画一个内切圆（对应原内置的 Knob，用于投射物），否则是实心方块（对应原内置的 Square）。
        /// </summary>
        private static Sprite CreatePlaceholderSprite(string assetPath, int size, bool round)
        {
            var dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(assetPath))
            {
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                float center = size * 0.5f;
                float radius = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        byte a = 255;
                        if (round)
                        {
                            // 到圆心的距离；在半径 ±0.5 像素之间做渐变，边缘不会出现硬锯齿
                            float d = Mathf.Sqrt((x + 0.5f - center) * (x + 0.5f - center) + (y + 0.5f - center) * (y + 0.5f - center));
                            a = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                        }
                        px[y * size + x] = new Color32(255, 255, 255, a);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(assetPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void CheckScripts()
        {
            // 注意：MonoScript.GetClass() 只对「继承 MonoBehaviour/ScriptableObject 的脚本」返回非 null。
            // 输入系统生成的代码包装类 PlayerInputActions 是普通 partial class，它必然返回 null —— 那不是错误。
            // 所以先收集「被场景/预制体挂载的脚本」路径，只有这些脚本 GetClass()==null 才算真失败。
            var referenced = CollectScriptsReferencedByAssets();

            var guids = AssetDatabase.FindAssets("t:MonoScript");
            int total = 0;
            var broken = new List<string>();   // 被挂载但加载不出类 → 真失败
            var noClass = new List<string>();  // 未被挂载且没有组件类 → 正常（如生成的包装类）

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/_Project")) continue;
                if (!path.EndsWith(".cs")) continue;

                total++;
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                bool hasClass = script != null && script.GetClass() != null;
                if (hasClass) continue;

                if (referenced.Contains(path)) broken.Add(path + "（脚本被场景/预制体引用，却加载不出组件类）");
                else noClass.Add(path);
            }

            Line("INFO", "Assets/_Project 下脚本总数", total.ToString());
            Line("INFO", "被场景/预制体引用的脚本数", referenced.Count.ToString());
            if (broken.Count == 0)
                Line("PASS", "全部被引用的脚本均能编译并加载出组件类", referenced.Count + " 个");
            else
                Line("FAIL", "有被引用的脚本加载失败", string.Join("; ", broken));

            if (noClass.Count > 0)
                Line("INFO", "非组件类脚本（不含 MonoBehaviour，GetClass 返回 null 属正常）", string.Join(", ", noClass));
        }

        /// <summary>扫描场景与预制体，收集其中 m_Script 指向的脚本资产路径。</summary>
        private static HashSet<string> CollectScriptsReferencedByAssets()
        {
            var set = new HashSet<string>();
            var exts = new[] { "*.unity", "*.prefab", "*.asset" };

            foreach (var ext in exts)
            {
                foreach (var file in Directory.GetFiles(Application.dataPath, ext, SearchOption.AllDirectories))
                {
                    string[] lines;
                    try { lines = File.ReadAllLines(file); }
                    catch { continue; }

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].IndexOf("m_Script:", System.StringComparison.Ordinal) < 0) continue;

                        var guid = ExtractGuid(lines[i]);
                        if (string.IsNullOrEmpty(guid)) continue;

                        var p = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(p) && p.EndsWith(".cs")) set.Add(p);
                    }
                }
            }
            return set;
        }

        private static string ExtractGuid(string line)
        {
            const string key = "guid: ";
            int i = line.IndexOf(key, System.StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length;
            int j = i;
            while (j < line.Length && line[j] != ',' && line[j] != '}' && !char.IsWhiteSpace(line[j])) j++;
            return line.Substring(i, j - i);
        }

        /// <summary>输入系统的代码包装类：确认 PlayerInputActions 类型真的存在于运行时程序集里。</summary>
        private static void CheckInputWrapperClass()
        {
            var t = System.Type.GetType("PlayerInputActions, Assembly-CSharp")
                    ?? System.Type.GetType("PlayerInputActions");
            if (t == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("PlayerInputActions", false);
                    if (t != null) break;
                }
            }

            if (t == null)
            {
                Line("FAIL", "输入包装类 PlayerInputActions", "类型未在任何程序集中编译出来（PlayerInputReader 会编译失败）");
                return;
            }

            var assetProp = t.GetProperty("asset");
            Line("PASS", "输入包装类 PlayerInputActions",
                $"{t.FullName} @ {t.Assembly.GetName().Name}；asset 属性={(assetProp != null ? "有" : "无")}");
        }

        private static void CheckRequiredTypes()
        {
            // 场景/预制体上挂的组件类型，缺任何一个都会变成 Missing Script
            var required = new[]
            {
                "PlayerController", "PlayerInputReader", "EnemyController", "Room", "RoomManager", "Door",
                "Health", "HitReaction", "PlayerAttacker", "ProjectileLauncher", "DamageZone",
                "Projectile", "SkillCaster", "SkillData", "AttackData", "ClassData",
                "CharacterStats", "CharacterVisual", "ManaPool", "RunManager",
                "HUDController", "ScreenFader", "SkillBarUI", "StatBar",
            };

            var assembly = typeof(MigrationVerifier).Assembly; // Assembly-CSharp-Editor
            var runtimeAsm = System.AppDomain.CurrentDomain.GetAssemblies();
            var found = new List<string>();
            var absent = new List<string>();

            foreach (var name in required)
            {
                bool ok = false;
                foreach (var asm in runtimeAsm)
                {
                    if (asm.GetType(name, false) != null) { ok = true; break; }
                    // 也允许带命名空间的写法
                    foreach (var t in SafeGetTypes(asm))
                    {
                        if (t != null && t.Name == name) { ok = true; break; }
                    }
                    if (ok) break;
                }
                if (ok) found.Add(name); else absent.Add(name);
            }

            Line("INFO", "运行时程序集数量", runtimeAsm.Length.ToString());
            Line("INFO", "Assembly-CSharp-Editor", assembly.GetName().Name);
            if (absent.Count == 0)
                Line("PASS", "全部关键类型已编译", found.Count + " 个");
            else
                Line("FAIL", "以下类型没有编译出来（应出现在 Console 的 CS 错误里）", string.Join(", ", absent));
        }

        private static System.Type[] SafeGetTypes(System.Reflection.Assembly asm)
        {
            try { return asm.GetTypes(); } catch { return new System.Type[0]; }
        }

        private static void CheckScriptableObjects()
        {
            var expected = new[]
            {
                "Assets/_Project/Data/classes/Class_Mage.asset",
                "Assets/_Project/Data/classes/Class_Warrior.asset",
                "Assets/_Project/Data/Skills/Skill_Fireball.asset",
                "Assets/_Project/Data/Skills/Skill_Whirlwind.asset",
                "Assets/_Project/Data/Weapons/Attack_Basic.asset",
                "Assets/_Project/Data/Weapons/Attack_Enemy.asset",
                "Assets/_Project/Data/Weapons/Attack_Fireball.asset",
                "Assets/_Project/Data/Weapons/Attack_MageBolt.asset",
                "Assets/_Project/Data/Weapons/Attack_Whirlwind.asset",
            };

            var bad = new List<string>();
            foreach (var p in expected)
            {
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(p);
                if (obj == null) { bad.Add(p + "（加载失败，可能脚本引用丢失）"); continue; }

                // 序列化一遍，能把 Unity 6 遗留的坏字段暴露出来
                var json = EditorJsonUtility.ToJson(obj);
                if (string.IsNullOrEmpty(json) || json.Length < 5)
                    bad.Add(p + "（序列化为空）");
            }

            if (bad.Count == 0)
                Line("PASS", "9 个 ScriptableObject 全部可加载", expected.Length.ToString());
            else
                Line("FAIL", "ScriptableObject 有问题", string.Join("; ", bad));
        }

        private static void CheckPrefabs()
        {
            var expected = new[]
            {
                "Assets/_Project/Prefabs/Fireball.prefab",
                "Assets/_Project/Prefabs/MageBolt.prefab",
                "Assets/_Project/Prefabs/Rooms/Room_01.prefab",
                "Assets/_Project/Prefabs/Rooms/Room_02.prefab",
            };

            var bad = new List<string>();
            foreach (var p in expected)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) { bad.Add(p + "（加载失败）"); continue; }
                var miss = CountMissingScripts(go);
                if (miss > 0) bad.Add($"{p}（{miss} 个 Missing Script）");
            }

            if (bad.Count == 0)
                Line("PASS", "4 个预制体全部可加载且无 Missing Script", expected.Length.ToString());
            else
                Line("FAIL", "预制体有问题", string.Join("; ", bad));
        }

        private static void CheckSceneAssets()
        {
            const string scenePath = "Assets/_Project/Scenes/Game.unity";

            if (!File.Exists(scenePath)) { Line("FAIL", "场景文件不存在", scenePath); return; }

            // 1) 以资产方式加载，暴露反序列化损坏
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            Line(asset != null ? "PASS" : "FAIL", "场景可被识别为 SceneAsset", asset != null ? scenePath : "LoadAssetAtPath 返回 null");

            // 2) 真正打开场景，检查缺失脚本与引用的资产
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Line("FAIL", "场景无法打开", scenePath); return; }

            int roots = scene.rootCount;
            int missing = 0;
            int nullRefFields = 0;
            var details = new List<string>();

            foreach (var root in scene.GetRootGameObjects())
            {
                missing += CountMissingScripts(root);
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) { nullRefFields++; continue; }
                    // 检查该组件上所有对象引用字段是否指向丢失的资产
                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        {
                            nullRefFields++;
                            details.Add($"{c.GetType().Name}.{it.propertyPath}");
                        }
                    }
                }
            }

            Line("INFO", "场景根物体数", roots.ToString());
            Line(missing == 0 ? "PASS" : "FAIL", "场景内 Missing Script 数", missing.ToString());
            Line(nullRefFields == 0 ? "PASS" : "WARN",
                "场景内指向已丢失资产的引用数", nullRefFields + (details.Count > 0 ? "：" + string.Join(", ", details.GetRange(0, System.Math.Min(10, details.Count))) : ""));

            // 3) 关键对象是否还在
            foreach (var n in new[] { "EventSystem" })
            {
                var go = GameObject.Find(n);
                Line(go != null ? "PASS" : "WARN", $"场景内存在 {n}", go != null ? "找到" : "没找到");
            }
            var pm = UnityEngine.Object.FindObjectOfType<UnityEngine.InputSystem.PlayerInput>();
            Line("INFO", "场景内 PlayerInput 组件", pm == null ? "无（本工程用 PlayerInputReader 直接驱动，符合预期）" : pm.name);
        }

        private static int CountMissingScripts(GameObject go)
        {
            int n = 0;
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c == null) n++;
            return n;
        }

        private static void CheckBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            Line("INFO", "Build Settings 场景数", scenes.Length.ToString());
            var bad = new List<string>();
            foreach (var s in scenes)
            {
                if (!File.Exists(s.path)) bad.Add(s.path + "（文件不存在）");
                else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) == null) bad.Add(s.path + "（无法加载）");
            }
            if (bad.Count == 0) Line("PASS", "Build Settings 里的场景都存在", scenes.Length.ToString());
            else Line("FAIL", "Build Settings 有无效场景", string.Join("; ", bad));

            if (scenes.Length > 0 && scenes[0].path != "Assets/_Project/Scenes/Game.unity")
                Line("WARN", "首个启用场景不是 Game.unity", scenes[0].path);
        }

        private static void CheckInputSettings()
        {
            var assets = AssetDatabase.FindAssets("t:InputActionAsset");
            Line("INFO", "InputActionAsset 数量", assets.Length.ToString());
            foreach (var g in assets)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(p);
                if (a == null) { Line("FAIL", "InputActionAsset 加载失败", p); continue; }
                int maps = a.actionMaps.Count, acts = 0;
                foreach (var m in a.actionMaps) acts += m.actions.Count;
                Line(acts > 0 ? "PASS" : "FAIL", "输入资产解析", $"{p}：{maps} 个 ActionMap / {acts} 个 Action");
            }

            var actionTypes = typeof(UnityEngine.InputSystem.InputAction);
            Line("INFO", "Input System 程序集版本",
                actionTypes.Assembly.GetName().Version + " @ " + actionTypes.Assembly.Location);
        }

        private static void Line(string level, string name, string detail)
        {
            if (level == "FAIL") _fail++;
            else if (level == "WARN") _warn++;
            _sb.Append('[').Append(level).Append("]|").Append(name).Append('|').Append(detail).Append('\n');
        }

        // ================= 可见性诊断（排查「运行后看不到东西」） =================
        // 用法：
        //   Tuanjie.exe -batchmode -projectPath D:\TuanjieRoguelike2D \
        //       -executeMethod Tuanjie.PortCheck.MigrationVerifier.DiagnoseVisibility -quit
        [MenuItem("团结/诊断可见性")]
        public static void DiagnoseVisibility()
        {
            var sbd = new StringBuilder();
            var scenePath = "Assets/_Project/Scenes/Game.unity";

            if (!EditorSceneManager.GetActiveScene().IsValid() ||
                EditorSceneManager.GetActiveScene().path != scenePath)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            // 1) 相机
            sbd.AppendLine("===== 相机 =====");
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                var o = cam.orthographic;
                float halfH = o ? cam.orthographicSize : cam.fieldOfView * 0.5f;
                float halfW = halfH * cam.aspect;
                sbd.AppendLine($"相机 '{cam.name}' 位置={cam.transform.position} 旋转={cam.transform.eulerAngles} " +
                    $"正交={o} size={cam.orthographicSize} fov={cam.fieldOfView} aspect={cam.aspect:F3} " +
                    $"near={cam.nearClipPlane} far={cam.farClipPlane} clearFlags={cam.clearFlags} bg={cam.backgroundColor} " +
                    $"cullingMask=0x{cam.cullingMask:X8}");
                sbd.AppendLine($"  可视范围(世界) X[{cam.transform.position.x - halfW:F2}, {cam.transform.position.x + halfW:F2}] " +
                    $"Y[{cam.transform.position.y - halfH:F2}, {cam.transform.position.y + halfH:F2}]");
            }

            // 2) 所有 SpriteRenderer
            sbd.AppendLine();
            sbd.AppendLine("===== SpriteRenderer =====");
            var sprites = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true);
            sbd.AppendLine($"总数 = {sprites.Length}");
            var totalBounds = new Bounds();
            bool hasBounds = false;

            foreach (var sr in sprites)
            {
                var go = sr.gameObject;
                var t = sr.transform;
                bool activeChain = go.activeInHierarchy;
                bool enabled = sr.enabled;

                string boundInfo = "无sprite→无包围盒";
                if (sr.sprite != null)
                {
                    var b = sr.bounds;
                    boundInfo = $"包围盒 中心={b.center} 尺寸={b.size}";
                    if (!hasBounds) { totalBounds = b; hasBounds = true; } else totalBounds.Encapsulate(b);
                }

                sbd.AppendLine($"[{(activeChain ? "激活" : "★未激活")}] {(enabled ? "启用" : "★禁用")} '{PathOf(go)}' " +
                    $"世界坐标={t.position} 缩放={t.lossyScale} " +
                    $"sprite={(sr.sprite != null ? "'" + sr.sprite.name + "'(" + sr.sprite.texture.width + "x" + sr.sprite.texture.height + ")" : "★null")} " +
                    $"颜色={sr.color} 排序层ID={sr.sortingLayerID} 排序层名='{sr.sortingLayerName}' 排序序号={sr.sortingOrder} " +
                    $"layer={LayerMask.LayerToName(go.layer)}({go.layer}) {boundInfo}");
            }
            if (hasBounds)
                sbd.AppendLine($"全部 SpriteRenderer 合并包围盒 = {totalBounds}");

            // 3) UI（Image / Text / Canvas / CanvasScaler）
            sbd.AppendLine();
            sbd.AppendLine("===== UI =====");
            foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            {
                var scaler = cv.GetComponent<UnityEngine.UI.CanvasScaler>();
                sbd.AppendLine($"Canvas '{PathOf(cv.gameObject)}' 渲染模式={cv.renderMode} 世界相机={(cv.worldCamera != null ? cv.worldCamera.name : "null")} " +
                    $"排序序号={cv.sortingOrder} 缩放={(cv.transform.lossyScale)} " +
                    (scaler != null ? $"CanvasScaler 模式={scaler.uiScaleMode} 参考分辨率={scaler.referenceResolution} match={scaler.matchWidthOrHeight}" : "★无 CanvasScaler"));
            }
            foreach (var img in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>(true))
            {
                var rt = img.rectTransform;
                sbd.AppendLine($"[{(img.gameObject.activeInHierarchy ? "激活" : "★未激活")}] Image '{PathOf(img.gameObject)}' " +
                    $"sprite={(img.sprite != null ? "'" + img.sprite.name + "'" : "★null")} 颜色={img.color} " +
                    $"世界坐标={rt.position} 尺寸={rt.rect.size} 屏幕像素={RectTransformUtility.WorldToScreenPoint(null, rt.position)}");
            }
            foreach (var txt in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>(true))
            {
                sbd.AppendLine($"Text '{PathOf(txt.gameObject)}' 内容=\"{txt.text}\" 颜色={txt.color} 字号={txt.fontSize} 字体={(txt.font != null ? txt.font.name : "★null")}");
            }

            // 4) 场景根物体概览
            sbd.AppendLine();
            sbd.AppendLine("===== 场景根物体 =====");
            var active = EditorSceneManager.GetActiveScene();
            foreach (var root in active.GetRootGameObjects())
            {
                sbd.AppendLine($"'{root.name}' [{PathOf(root)}] 激活={root.activeInHierarchy} 坐标={root.transform.position} 缩放={root.transform.lossyScale}");
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) { sbd.AppendLine($"    ★ Missing Script"); continue; }
                    string extra = "";
                    var col = c as Collider2D;
                    if (col != null) extra = $" 尺寸={col.bounds.size} 中心={col.bounds.center}";
                    var rb = c as Rigidbody2D;
                    if (rb != null) extra = $" 类型={rb.bodyType} 重力={rb.gravityScale} 模拟={rb.simulated}";
                    sbd.AppendLine($"    - {c.GetType().Name}{extra}");
                }
            }

            // 5) Sprite 资产盘点
            sbd.AppendLine();
            sbd.AppendLine("===== 工程里的 Sprite 资产 =====");
            var guids = AssetDatabase.FindAssets("t:Sprite");
            sbd.AppendLine($"t:Sprite 命中 = {guids.Length}");
            int shown = 0;
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.StartsWith("Packages/")) continue; // 忽略包内置精灵
                if (shown++ > 40) break;
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                sbd.AppendLine($"  {p}  {(s != null ? s.texture.width + "x" + s.texture.height : "加载失败")}");
            }

            // 6) 排序层
            sbd.AppendLine();
            sbd.AppendLine("===== 排序层 (Sorting Layers) =====");
            foreach (var l in SortingLayer.layers)
                sbd.AppendLine($"  id={l.id} name='{l.name}' value={l.value}");

            // 7) 内置 Sprite 归属：场景里 Player 的 SpriteRenderer.m_Sprite 指向内置精灵，能否解析？
            sbd.AppendLine();
            sbd.AppendLine("===== 内置 Sprite 解析 =====");
            const string builtinGuid = "311925a002f4447b3a28927169b83ea6"; // 场景中被引用的那个
            sbd.AppendLine($"AssetDatabase.GUIDToAssetPath(\"{builtinGuid}\") = '{AssetDatabase.GUIDToAssetPath(builtinGuid)}'");
            sbd.AppendLine($"GUIDToAssetPath 是否为空 = {string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(builtinGuid))}");

            foreach (var guid in new[]
            {
                "311925a002f4447b3a28927169b83ea6", // 场景引用
                "0000000000000000f000000000000000", // Default-Material / 内置
                "fe87c0e1cc204ed48ad3b37840f39efc", // uGUI Image 脚本
            })
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                sbd.AppendLine($"  guid={guid} → path='{p}'");
            }

            // Player 的 SpriteRenderer 到底解析成了什么
            var playerGo = GameObject.Find("Player");
            if (playerGo != null)
            {
                var sr = playerGo.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sbd.AppendLine($"Player.SpriteRenderer.sprite = {(sr.sprite == null ? "★null" : "'" + sr.sprite.name + "'")}");
                    var so = new SerializedObject(sr);
                    var prop = so.FindProperty("m_Sprite");
                    if (prop != null)
                    {
                        var direct = prop.objectReferenceValue;
                        sbd.AppendLine($"  SerializedProperty m_Sprite: objectReferenceValue={(direct == null ? "★null" : direct.name)} " +
                            $"instanceID={prop.objectReferenceInstanceIDValue} " +
                            $"类型={(direct == null ? "?" : direct.GetType().FullName)}");
                    }
                }
            }

            // 遍历编辑器内置资源里的精灵，把名字列出来
            sbd.AppendLine();
            sbd.AppendLine("===== 编辑器内置 Sprite（Resources/unity_builtin_extra 等）=====");
            int found = 0;
            foreach (var obj in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;   // 只看有资产路径的
                if (!path.StartsWith("Resources/") && !path.StartsWith("Library/")) continue;
                if (found++ > 60) break;
                var tex = obj.texture;
                sbd.AppendLine($"  '{obj.name}' 尺寸={(tex != null ? tex.width + "x" + tex.height : "?")} path='{path}'");
            }
            sbd.AppendLine($"  合计列出 {found} 个内置精灵");

            // 直接按名字找那个内置精灵
            sbd.AppendLine();
            sbd.AppendLine("===== 按名字定位「内置 UISprite」=====");
            foreach (var obj in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (obj == null) continue;
                if (obj.name != "UISprite" && obj.name != "Background" && obj.name != "UISprite (Editor)") continue;
                var path = AssetDatabase.GetAssetPath(obj);
                var tex = obj.texture;
                sbd.AppendLine($"  '{obj.name}' 尺寸={(tex != null ? tex.width + "x" + tex.height : "?")} " +
                    $"rect={obj.rect} pixelsPerUnit={obj.pixelsPerUnit} path='{path}' instanceID={obj.GetInstanceID()}");
            }

            // 8) 内置资源包全表：找出可用替代（团结把 unity_builtin_extra 改名成 tuanjie_builtin_extra，旧 GUID 失配）
            sbd.AppendLine();
            sbd.AppendLine("===== 内置资源包路径 =====");
            foreach (var p in new[]
            {
                "Resources/tuanjie_builtin_extra", "Resources/unity_builtin_extra",
                "Resources/tuanjie default resources", "Resources/unity default resources",
                "Library/tuanjie_builtin_extra", "Library/unity_builtin_extra",
            })
            {
                var o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                sbd.AppendLine($"  '{p}' → {(o == null ? "★null" : o.GetType().Name + " '" + o.name + "'")}");
            }

            sbd.AppendLine();
            sbd.AppendLine("===== 内置资源包里的全部 Sprite（用于挑替代品）=====");
            int n = 0;
            foreach (var obj in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || path.IndexOf("builtin", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n++ >= 200) break;
                var tex = obj.texture;
                sbd.AppendLine($"  id={obj.GetInstanceID()} '{obj.name}' 尺寸={(tex != null ? tex.width + "x" + tex.height : "?")} " +
                    $"rect={obj.rect.width}x{obj.rect.height} ppu={obj.pixelsPerUnit} path='{path}'");
            }
            sbd.AppendLine($"  合计 {n} 个");

            sbd.AppendLine();
            sbd.AppendLine("===== 失配的那个 fileID 在团结里是什么 =====");
            foreach (var id in new[] { 7482667652216324306L, 10905L })
            {
                var o = Resources.InstanceIDToObject((int)id);
                sbd.AppendLine($"  fileID={id} → {(o == null ? "★null（不存在）" : o.GetType().Name + " '" + o.name + "' path='" + AssetDatabase.GetAssetPath(o) + "'")}");
            }

            var outPath = Path.Combine(ProjectRoot, "_Tuanjie", "visibility-diagnosis.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sbd.ToString(), new UTF8Encoding(false));
            Debug.Log($"[PortCheck] 可见性诊断已写入 {outPath}");

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static string PathOf(GameObject go)
        {
            var s = go.name;
            var t = go.transform.parent;
            while (t != null) { s = t.name + "/" + s; t = t.parent; }
            return s;
        }

        // ------------------------------------------------------------------
        // 进入 Play 模式跑若干帧的冒烟测试：确认运行时真的渲染出东西
        // ------------------------------------------------------------------
        private static readonly StringBuilder _playSb = new StringBuilder();
        private static int _playFrame;
        private static bool _playStarted;

        [MenuItem("团结/Play 冒烟测试")]
        public static void PlaySmokeTest()
        {
            _playSb.Clear();
            _playFrame = 0;
            _playStarted = false;
            EditorApplication.update += PlayTick;
            EditorApplication.EnterPlaymode();

            // 安全网：60 秒还没跑完就强行收尾
            EditorApplication.delayCall += () =>
            {
                if (!_playStarted) return;
                EditorApplication.update -= PlayTick;
                FinishPlayTest("★超时");
            };
        }

        private static void PlayTick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (!_playStarted) return;
                EditorApplication.update -= PlayTick;
                FinishPlayTest("已退出 Play 模式但未采到样本");
                return;
            }

            _playStarted = true;
            _playFrame++;

            if (_playFrame < 8) return;   // 等 RoomManager.Start()、UI、物理都跑起来

            EditorApplication.update -= PlayTick;

            _playSb.AppendLine("===== 运行时状态 =====");
            _playSb.AppendLine($"  屏幕分辨率 = {Screen.width}x{Screen.height}   帧数 = {Time.frameCount}   时间 = {Time.time:F2}s");

            var cam = Camera.main;
            if (cam == null) _playSb.AppendLine("  ★主相机未找到");
            else
            {
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                var cp = cam.transform.position;
                _playSb.AppendLine($"  相机 '{cam.name}' 位置={cp} 正交={cam.orthographic} size={halfH} aspect={cam.aspect:F3}");
                _playSb.AppendLine($"    可视范围(世界) X[{cp.x - halfW:F2}, {cp.x + halfW:F2}] Y[{cp.y - halfH:F2}, {cp.y + halfH:F2}]");
            }

            _playSb.AppendLine();
            _playSb.AppendLine("===== 运行时 SpriteRenderer =====");
            var all = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
            _playSb.AppendLine($"  总数 = {all.Length}");
            int withSprite = 0, inView = 0;
            foreach (var sr in all.OrderBy(o => o.name))
            {
                bool has = sr.sprite != null;
                if (has) withSprite++;
                string inViewText = "?";
                if (cam != null && sr.enabled && has)
                {
                    var p = sr.transform.position;
                    float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
                    var cp = cam.transform.position;
                    bool vis = p.x >= cp.x - halfW && p.x <= cp.x + halfW && p.y >= cp.y - halfH && p.y <= cp.y + halfH;
                    if (vis) inView++;
                    inViewText = vis ? "在视野内" : "★在视野外";
                }
                _playSb.AppendLine($"  [{(sr.enabled ? "启用" : "★禁用")}] '{PathOf(sr.gameObject)}' @{sr.transform.position} " +
                    $"sprite={(has ? "'" + sr.sprite.name + "'" : "★null")} 尺寸={(has ? sr.bounds.size.x.ToString("F2") + "x" + sr.bounds.size.y.ToString("F2") : "-")} " +
                    $"颜色={sr.color} 排序层='{SortingLayer.IDToName(sr.sortingLayerID)}'/{sr.sortingOrder} {inViewText}");
            }
            _playSb.AppendLine($"  有 sprite 的 = {withSprite} / {all.Length}；在相机视野内的 = {inView}");

            _playSb.AppendLine();
            _playSb.AppendLine("===== 运行时场景根物体 =====");
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                _playSb.AppendLine($"  '{(go.activeInHierarchy ? go.name : go.name + "(★未激活)")}' @{go.transform.position}");

            _playSb.AppendLine();
            _playSb.AppendLine("===== 直接渲染结果（读回相机像素）=====");
            try
            {
                const int w = 160, h = 90;
                var rt = new RenderTexture(w, h, 24);
                var prev = cam != null ? cam.targetTexture : null;
                if (cam != null)
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    var px = tex.GetPixels32();
                    var hist = new Dictionary<string, int>();
                    foreach (var p in px)
                    {
                        var key = $"{p.r / 16 * 16},{p.g / 16 * 16},{p.b / 16 * 16}";
                        hist[key] = hist.TryGetValue(key, out var c) ? c + 1 : 1;
                    }
                    _playSb.AppendLine($"  采样 {w}x{h}；不同颜色数（16 级量化）= {hist.Count}");
                    foreach (var kv in hist.OrderByDescending(k => k.Value).Take(8))
                        _playSb.AppendLine($"    颜色 {kv.Key} → {kv.Value} 像素 ({100.0 * kv.Value / px.Length:F1}%)");
                    RenderTexture.active = null;
                    cam.targetTexture = prev;
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                UnityEngine.Object.DestroyImmediate(rt);
            }
            catch (Exception e) { _playSb.AppendLine("  ★渲染采样失败：" + e.Message); }

            FinishPlayTest(null);
        }

        private static void FinishPlayTest(string note)
        {
            if (note != null) _playSb.AppendLine("  注：" + note);
            var outPath = Path.Combine(ProjectRoot, "_Tuanjie", "play-smoke-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, _playSb.ToString(), new UTF8Encoding(false));
            Debug.Log("[PortCheck] Play 冒烟报告 → " + outPath);

            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) EditorApplication.Exit(0);
            };
        }

        // ------------------------------------------------------------------
        // 编辑模式预览：按「运行时会发生什么」摆一遍，算清楚相机能不能看见
        // （不做 EnterPlaymode —— 批处理下进 Play 会卡在 Temp/__Backupscenes 备份）
        // ------------------------------------------------------------------
        [MenuItem("团结/可见性预览")]
        public static void PreviewVisibility()
        {
            var sb = new StringBuilder();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Game.unity", OpenSceneMode.Single);

            var cam = Camera.main;
            if (cam == null) { sb.AppendLine("★主相机未找到"); WritePreview(sb); return; }

            var cp = cam.transform.position;
            float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
            sb.AppendLine("===== 相机 =====");
            sb.AppendLine($"  '{PathOf(cam.gameObject)}' 位置={cp} 正交={cam.orthographic} size={halfH} aspect={cam.aspect:F3}");
            sb.AppendLine($"  可视范围(世界) X[{cp.x - halfW:F2}, {cp.x + halfW:F2}] Y[{cp.y - halfH:F2}, {cp.y + halfH:F2}]");

            Func<Vector3, string> vis = p =>
                (p.x >= cp.x - halfW && p.x <= cp.x + halfW && p.y >= cp.y - halfH && p.y <= cp.y + halfH)
                ? "在视野内" : "★在视野外";

            sb.AppendLine();
            sb.AppendLine("===== 场景自带的 SpriteRenderer =====");
            foreach (var sr in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true))
                sb.AppendLine($"  '{(sr.enabled ? PathOf(sr.gameObject) : PathOf(sr.gameObject) + "(★禁用)")}' @{sr.transform.position} " +
                    $"sprite={(sr.sprite != null ? "'" + sr.sprite.name + "' 尺寸 " + sr.bounds.size.x.ToString("F2") + "x" + sr.bounds.size.y.ToString("F2") : "★null")} " +
                    $"颜色={sr.color} {vis(sr.transform.position)}");

            sb.AppendLine();
            sb.AppendLine("===== 模拟 RoomManager.BuildRoom() =====");
            var rm = UnityEngine.Object.FindObjectOfType<RoomManager>();
            if (rm == null) { sb.AppendLine("  ★场景里没有 RoomManager"); WritePreview(sb); return; }

            Transform anchor = rm.transform;
            sb.AppendLine($"  RoomManager 位置(锚点) = {anchor.position}");

            var so = new SerializedObject(rm);
            var listProp = so.FindProperty("roomPrefabs");
            sb.AppendLine($"  roomPrefabs 槽位数 = {(listProp != null ? listProp.arraySize.ToString() : "?")}");
            string target = null;
            if (listProp != null && listProp.arraySize > 0)
            {
                var go = listProp.GetArrayElementAtIndex(0).objectReferenceValue as GameObject;
                if (go != null) { target = AssetDatabase.GetAssetPath(go); sb.AppendLine($"  第 0 个房间 = {target}"); }
            }
            if (target == null) { sb.AppendLine("  ★房间池为空 → 运行时什么都不会生成（RoomManager 会打警告后 return）"); WritePreview(sb); return; }

            var contents = PrefabUtility.LoadPrefabContents(target);
            try
            {
                contents.transform.position = anchor.position;
                contents.transform.rotation = Quaternion.identity;
                var srs = contents.GetComponentsInChildren<SpriteRenderer>(true);
                sb.AppendLine($"  按锚点摆放后，房间内 SpriteRenderer = {srs.Length} 个");
                int visCount = 0;
                foreach (var sr in srs)
                {
                    var p = sr.transform.position;
                    bool v = p.x >= cp.x - halfW && p.x <= cp.x + halfW && p.y >= cp.y - halfH && p.y <= cp.y + halfH;
                    if (v) visCount++;
                    sb.AppendLine($"    '{(sr.enabled ? PathOf(sr.gameObject) : PathOf(sr.gameObject) + "(★禁用)")}' 局部{sr.transform.localPosition} 世界{p} " +
                        $"缩放{sr.transform.lossyScale} sprite={(sr.sprite != null ? "'" + sr.sprite.name + "'" : "★null")} " +
                        $"尺寸 {sr.bounds.size.x:F2}x{sr.bounds.size.y:F2} 颜色={sr.color} {(v ? "在视野内" : "★在视野外")}");
                }
                sb.AppendLine($"  房间内落在相机视野里的 = {visCount} / {srs.Length}");

                var roomComp = contents.GetComponent<Room>();
                if (roomComp != null && roomComp.SpawnPoint != null)
                {
                    var sp = roomComp.SpawnPoint.position;
                    sb.AppendLine($"  出生点世界坐标 = {sp} —— 玩家会被 MovePlayerToSpawn() 挪到这里 → {vis(sp)}");
                }
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    sb.AppendLine($"  玩家当前坐标 = {player.transform.position} → {vis(player.transform.position)}");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            WritePreview(sb);
        }

        private static void WritePreview(StringBuilder sb)
        {
            var outPath = Path.Combine(ProjectRoot, "_Tuanjie", "preview-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[PortCheck] 可见性预览已写入 {outPath}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
