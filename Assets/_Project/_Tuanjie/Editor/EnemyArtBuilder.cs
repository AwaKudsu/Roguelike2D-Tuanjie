using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 菜单「团结 → 切怪物帧」。
///
/// 输入是 6 张「帧条」（每张 1536×1024：左边一列中文行标签，右边 5 行 × 若干帧，
/// 从上到下依次是 待机 / 移动 / 攻击 / 受击 / 死亡）。
///
/// 这个工具做四件事：
///   1. 按「背景色差异」把每一行切成一个个独立的框（不依赖固定帧数 —— 六只怪的帧数并不一样）；
///   2. 每只怪的所有帧统一贴进同一张画布（底部对齐），存成 PNG；
///   3. 导入时把 PPU 反算成「这只怪的个子正好是 TargetHeight 个单位」，轴心放脚底；
///   4. 把 Sprite 数组和 fps 写进对应的 EnemyConfig，并把预制体的 SpriteRenderer 换成待机首帧。
///
/// ★ 为什么按颜色差异切、而不是按固定列宽：
///   帧与帧之间的间距并不均匀（移动那行最密、死亡那行最松），固定列宽必然把相邻两帧切到一起。
/// </summary>
public static class EnemyArtBuilder
{
    private const string ArtRoot = "Assets/_Project/Art/Enemies";
    private const string ConfigDir = "Assets/_Project/Data/enemies";
    private const string PrefabDir = "Assets/_Project/Prefabs/Enemies";

    private const string AttachmentRoot =
        @"D:\Deepseek_Hardness\dsh-home\attachments\v1\objects\";

    private sealed class Sheet
    {
        public string Name;          // 与预制体 / 配置同名
        public string Sha256;        // 帧条在附件库里的文件名
        public float TargetHeight;   // 这只怪在世界里多高（单位）
        public float IdleFps, MoveFps, AttackFps, HurtFps, DeathFps;
    }

    private static readonly Sheet[] Sheets =
    {
        new Sheet { Name = "Goblin",         Sha256 = "55c740d5dcbc507192a419ffbf77db5af1d87860dc2cb89ce4e7359e3440f5a4", TargetHeight = 1.5f, IdleFps = 6f,  MoveFps = 10f, AttackFps = 12f, HurtFps = 12f, DeathFps = 8f },
        new Sheet { Name = "SkeletonArcher", Sha256 = "6f4c05183be19bfcbec917f78cbe670d10397d355e632e55e7e752663909f28e", TargetHeight = 1.7f, IdleFps = 5f,  MoveFps = 9f,  AttackFps = 10f, HurtFps = 12f, DeathFps = 8f },
        new Sheet { Name = "Slime",          Sha256 = "07875762bc386d322a10e5a7e01e3f087ab53d6fa35eb488bb78da774918200c", TargetHeight = 1.1f, IdleFps = 5f,  MoveFps = 8f,  AttackFps = 8f,  HurtFps = 12f, DeathFps = 8f },
        new Sheet { Name = "Bat",            Sha256 = "1a0fbf65254555fbf3475c04e8d9d122c86f32ac0d34eb0316fdbbee3b17ba59", TargetHeight = 1.1f, IdleFps = 10f, MoveFps = 14f, AttackFps = 14f, HurtFps = 12f, DeathFps = 10f },
        new Sheet { Name = "FireMage",       Sha256 = "5d55573f31b9232470ebb0ffef4d6d57b079dedc502f8a53b111dd19473b6f01", TargetHeight = 1.6f, IdleFps = 5f,  MoveFps = 9f,  AttackFps = 10f, HurtFps = 12f, DeathFps = 8f },
        new Sheet { Name = "ShieldKnight",   Sha256 = "fbb99c4da5b3d37aa030cc0326f2fff364c480ae1a86e127534210b11d0b6bba", TargetHeight = 1.9f, IdleFps = 5f,  MoveFps = 8f,  AttackFps = 10f, HurtFps = 12f, DeathFps = 8f },
    };

    [MenuItem("团结/切怪物帧", false, 72)]
    public static void Slice()
    {
        int done = 0, failed = 0;
        foreach (Sheet sheet in Sheets)
        {
            string source = AttachmentRoot + sheet.Sha256.Substring(0, 2) + "/" + sheet.Sha256;
            if (!File.Exists(source))
            {
                Debug.LogError("[EnemyArt] 找不到帧条：" + source);
                failed++;
                continue;
            }

            if (SliceSheet(sheet, source)) done++; else failed++;
        }

        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Debug.Log("[EnemyArt] 完成：成功 " + done + " 只，失败 " + failed + " 只。");
    }

    private static bool SliceSheet(Sheet sheet, string sourcePath)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(File.ReadAllBytes(sourcePath)))
        {
            Debug.LogError("[EnemyArt] 读不了：" + sourcePath);
            return false;
        }

        int width = source.width;
        int height = source.height;

        // ★ 不能假设「5 等分」：1024 / 5 = 204.8，取 204 就会差几像素，
        //   把上一行的脚、下一行的帽子带进本条带里 —— 于是每一列都有内容，
        //   整行被切成了一帧（前两次就是这个坑）。
        //   这里先按「横向投影」找出真正有内容的 5 条行带，再逐带切帧。
        const int labelWidth = 200;

        var rowHasContent = new bool[height];
        for (int y = 0; y < height; y++)
        {
            for (int x = labelWidth; x < width; x++)
            {
                Color32 c = source.GetPixel(x, y);
                if (IsSprite(c)) { rowHasContent[y] = true; break; }
            }
        }

        var bands = new List<Vector2Int>();   // x = 起始 y（纹理坐标，0 在底部），y = 高度
        int scan = 0;
        while (scan < height)
        {
            if (!rowHasContent[scan]) { scan++; continue; }

            int start = scan;
            int last = scan;
            int gap = 0;
            while (scan < height)
            {
                if (rowHasContent[scan]) { last = scan; gap = 0; }
                else if (++gap > 12) break;
                scan++;
            }

            if (last - start > 40) bands.Add(new Vector2Int(start, last - start + 1));
        }

        if (bands.Count != 5)
            Debug.LogWarning("[EnemyArt] " + sheet.Name + " 找到 " + bands.Count + " 条行带（应为 5），按找到的顺序切。");

        var rows = new List<List<Texture2D>>();
        for (int i = bands.Count - 1; i >= 0; i--)          // 纹理 y 向上，倒着数才是「待机→死亡」
            rows.Add(SliceRow(source, bands[i].x, bands[i].y, width));

        while (rows.Count < 5) rows.Add(new List<Texture2D>());
        if (rows.Count > 5) rows.RemoveRange(5, rows.Count - 5);

        string[] rowNames = { "待机", "移动", "攻击", "受击", "死亡" };
        for (int row = 0; row < 5; row++)
            Debug.Log("[EnemyArt] " + sheet.Name + " · " + rowNames[row] + " 行切出 " + rows[row].Count + " 帧（行带高 "
                      + (row < bands.Count ? bands[bands.Count - 1 - row].y.ToString() : "-") + "）");

        if (rows[0].Count == 0)
        {
            Debug.LogError("[EnemyArt] " + sheet.Name + " 一行都没切出来，检查背景色阈值。");
            return false;
        }

        int maxW = 1, maxH = 1;
        foreach (List<Texture2D> row in rows)
            foreach (Texture2D frame in row)
            {
                maxW = Mathf.Max(maxW, frame.width);
                maxH = Mathf.Max(maxH, frame.height);
            }

        string folder = ArtRoot + "/" + sheet.Name;
        EnsureFolder(folder);

        float ppu = maxH / sheet.TargetHeight;

        string[] names = { "Idle", "Move", "Attack", "Hurt", "Death" };
        var sprites = new Sprite[5][];
        for (int i = 0; i < 5; i++)
        {
            sprites[i] = new Sprite[rows[i].Count];
            for (int f = 0; f < rows[i].Count; f++)
            {
                string assetPath = folder + "/" + sheet.Name + "_" + names[i] + "_" + f + ".png";
                SavePacked(rows[i][f], maxW, maxH, assetPath);
                ConfigureImporter(assetPath, ppu);
                sprites[i][f] = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }
        }

        // 帧图用完就扔，别在编辑器里攒一堆纹理
        foreach (List<Texture2D> row in rows)
            foreach (Texture2D frame in row) Object.DestroyImmediate(frame);
        Object.DestroyImmediate(source);

        AssetDatabase.SaveAssets();

        AssignConfig(sheet, sprites);
        AssignPrefab(sheet, sprites[0]);
        return true;
    }

    /// <summary>
    /// 取「这一行的底色」= 该行出现次数最多的颜色。
    ///
    /// ★ 之前这里取的是左边标签外侧（x=170）的页面底色 —— 错的：
    ///   每只怪的人物都画在一块深绿/白的「行底板」上，底板本身就是一个大色块，
    ///   于是「与页面底色不同」的列覆盖了整行，整行被当成一帧切出来，
    ///   底板颜色也一起烘进贴图 → 游戏里出现一条巨大的绿条。
    ///   行底板才是背景，必须按行统计主色。
    /// </summary>
    private static Color32 RowBackground(Texture2D source, int y0, int rowHeight, int width)
    {
        var counts = new Dictionary<int, int>();
        int stepX = Mathf.Max(1, width / 400);
        int stepY = Mathf.Max(1, rowHeight / 80);

        for (int y = y0 + stepY; y < y0 + rowHeight; y += stepY)
        {
            for (int x = 0; x < width; x += stepX)
            {
                Color32 c = source.GetPixel(x, y);
                int key = (c.r << 16) | (c.g << 8) | c.b;
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
        }

        int best = 0;
        int bestCount = -1;
        foreach (KeyValuePair<int, int> pair in counts)
        {
            if (pair.Value <= bestCount) continue;
            bestCount = pair.Value;
            best = pair.Key;
        }

        return new Color32((byte)((best >> 16) & 0xFF), (byte)((best >> 8) & 0xFF), (byte)(best & 0xFF), 255);
    }

    /// <summary>
    /// 判断一个像素是不是「人物自己」的像素。
    ///
    /// ★ 不能再用「和底色不同」：每行的底板本身就是一块比页面亮的色块，
    ///   它一整天都被当成内容 → 五行连成一条带 → 整张图被裁成一帧（前几次就是这个）。
    ///   人物有两个可靠特征：2px 的深黑描边（很暗），以及彩色的亮部。
    ///   底板和页面都是中间调，两个条件都不满足。
    /// </summary>
    private static bool IsSprite(Color32 c)
    {
        float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
        return lum < 0.10f || lum > 0.42f;
    }

    /// <summary>把一行切成若干帧：先按列找「有内容」的连续区间，再收紧竖直范围。</summary>
    private static List<Texture2D> SliceRow(Texture2D source, int y0, int rowHeight, int width)
    {
        const int labelWidth = 200;        // 左边那列中文标签，整段跳过
        const float contentDiff = 0.25f;   // 判「这是人物」的强差异（行底板有噪点，不能用小阈值）
        const float keyDiff = 0.30f;       // 泛洪「这是底板」的容差：底板有噪点，给小了会留斑
        const float minCoverage = 0.02f;   // 一列至少 2% 的像素是人物，才算这一列有内容

        Color32 background = RowBackground(source, y0, rowHeight, width);
        int minPixels = Mathf.Max(2, Mathf.RoundToInt(rowHeight * minCoverage));
        var columns = new List<bool>(width - labelWidth);

        for (int x = labelWidth; x < width; x++)
        {
            int hits = 0;
            for (int y = y0; y < y0 + rowHeight && hits < minPixels; y++)
            {
                Color32 c = source.GetPixel(x, y);
                float d = Mathf.Abs(c.r - background.r) + Mathf.Abs(c.g - background.g) + Mathf.Abs(c.b - background.b);
                // 两个条件都要满足：亮度像人物（黑描边/亮部），且颜色确实离底色够远。
                // 后者专治白底那两张（骷髅/持盾）：白底亮度 1.0，光看亮度会被整片当成人物。
                if (IsSprite(c) && d > 0.25f) hits++;
            }
            columns.Add(hits >= minPixels);
        }

        // 先只收「列的区间」，再决定要不要把过宽的那段劈开
        var segments = new List<Vector2Int>();   // x = 起列，y = 止列（含）
        int index = 0;
        while (index < columns.Count)
        {
            if (!columns[index]) { index++; continue; }

            int start = index;
            int last = index;
            int gap = 0;
            while (index < columns.Count)
            {
                if (columns[index]) { last = index; gap = 0; }
                else if (++gap > 8) break;
                index++;
            }

            if (last - start + 1 >= 10) segments.Add(new Vector2Int(start, last));
        }

        // ★ 蝙蝠那行有几帧的翅膀是贴着的，间隙为 0 → 两只会被连成「一条」。
        //   这里按中位宽度兜底：宽度超过中位数 1.7 倍的段，从中点劈成两帧。
        if (segments.Count >= 2)
        {
            var widths = new List<int>();
            foreach (Vector2Int seg in segments) widths.Add(seg.y - seg.x + 1);
            widths.Sort();
            int median = widths[widths.Count / 2];

            var split = new List<Vector2Int>();
            foreach (Vector2Int seg in segments)
            {
                int w = seg.y - seg.x + 1;
                if (w > median * 1.7f)
                {
                    int mid = seg.x + w / 2;
                    split.Add(new Vector2Int(seg.x, mid));
                    split.Add(new Vector2Int(mid + 1, seg.y));
                }
                else split.Add(seg);
            }
            segments = split;
        }

        var frames = new List<Texture2D>();
        foreach (Vector2Int seg in segments)
            frames.Add(CropFrame(source, labelWidth + seg.x, y0, seg.y - seg.x + 1, rowHeight,
                                 contentDiff, keyDiff, background));

        return frames;
    }

    private static Texture2D CropFrame(Texture2D source, int x0, int y0, int w, int h,
                                       float contentDiff, float keyDiff, Color32 background)
    {
        int top = -1, bottom = -1;
        for (int y = 0; y < h; y++)
        {
            bool any = false;
            for (int x = x0; x < x0 + w && !any; x++)
            {
                Color32 c = source.GetPixel(x, y0 + y);
                if (IsSprite(c)) any = true;
            }
            if (any) { if (top < 0) top = y; bottom = y; }
        }

        if (top < 0) { top = 0; bottom = h - 1; }

        int frameHeight = bottom - top + 1;
        var texture = new Texture2D(w, frameHeight, TextureFormat.RGBA32, false);

        int total = w * frameHeight;
        var src = new Color32[total];
        for (int y = 0; y < frameHeight; y++)
            for (int x = 0; x < w; x++)
                src[y * w + x] = source.GetPixel(x0 + x, y0 + top + y);

        // ★ 底板是一块「带噪点/渐变的绿色或白色矩形」，逐像素按颜色抠一定留脏点。
        //   这里改成「从四边泛洪」：只把与四边连通、且颜色接近底色的像素变透明，
        //   相当于画图软件里的魔棒 —— 人物被 2px 黑描边包住，泛洪进不去。
        var isPlate = new bool[total];
        var queue = new Queue<int>();

        bool NearPlate(Color32 c)
        {
            float d = Mathf.Abs(c.r - background.r) + Mathf.Abs(c.g - background.g) + Mathf.Abs(c.b - background.b);
            return d < keyDiff;
        }

        void Seed(int x, int y)
        {
            int i = y * w + x;
            if (isPlate[i] || !NearPlate(src[i])) return;
            isPlate[i] = true;
            queue.Enqueue(i);
        }

        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, frameHeight - 1); }
        for (int y = 0; y < frameHeight; y++) { Seed(0, y); Seed(w - 1, y); }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(x - 1, y);
            if (x < w - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < frameHeight - 1) Seed(x, y + 1);
        }

        Color32 clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < frameHeight; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                texture.SetPixel(x, y, isPlate[i] ? clear : src[i]);
            }

        texture.Apply();
        return texture;
    }

    /// <summary>把一帧贴进统一画布：水平居中、底部对齐（脚底在同一行，播动画才不会上下抖）。</summary>
    private static void SavePacked(Texture2D frame, int canvasW, int canvasH, string assetPath)
    {
        var canvas = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
        var clear = new Color32(0, 0, 0, 0);
        var pixels = new Color32[canvasW * canvasH];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
        canvas.SetPixels32(pixels);

        int offsetX = (canvasW - frame.width) / 2;
        canvas.SetPixels(offsetX, 0, frame.width, frame.height, frame.GetPixels());

        canvas.Apply();
        File.WriteAllBytes(assetPath, canvas.EncodeToPNG());
        Object.DestroyImmediate(canvas);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
    }

    private static void ConfigureImporter(string path, float ppu)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;

        // ★ 轴心只能走 TextureImporterSettings：
        //   TextureImporter 上并没有 spriteAlignment 这个属性（CS1061），
        //   ReadTextureSettings 取一份结构体、改完再 SetTextureSettings 写回去。
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, 0f);   // 轴心在脚底
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }

    private static void AssignConfig(Sheet sheet, Sprite[][] sprites)
    {
        var config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(ConfigDir + "/Enemy_" + sheet.Name + ".asset");
        if (config == null)
        {
            Debug.LogWarning("[EnemyArt] 没找到配置 " + sheet.Name + "，先跑「生成怪物资源」。");
            return;
        }

        config.idleFrames = sprites[0];
        config.moveFrames = sprites[1];
        config.attackFrames = sprites[2];
        config.hurtFrames = sprites[3];
        config.deathFrames = sprites[4];
        config.idleFps = sheet.IdleFps;
        config.moveFps = sheet.MoveFps;
        config.attackFps = sheet.AttackFps;
        config.hurtFps = sheet.HurtFps;
        config.deathFps = sheet.DeathFps;

        EditorUtility.SetDirty(config);
    }

    private static void AssignPrefab(Sheet sheet, Sprite[] idle)
    {
        string path = PrefabDir + "/" + sheet.Name + ".prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null) return;

        var renderer = contents.GetComponent<SpriteRenderer>();
        if (renderer != null && idle.Length > 0)
        {
            renderer.sprite = idle[0];
            renderer.color = Color.white;   // 之前是拿占位方图染色的，有真图就不染了
        }

        // 轴心挪到脚底了，碰撞盒要跟着抬高半个身位，否则半截陷进地里
        var capsule = contents.GetComponent<CapsuleCollider2D>();
        if (capsule != null) capsule.offset = new Vector2(0f, capsule.size.y * 0.5f);

        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
