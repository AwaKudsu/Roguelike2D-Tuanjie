using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 菜单「团结 → 导入怪物帧（桌面图）」。
///
/// 源目录：C:\Users\hp\Desktop\图片\图片\&lt;怪&gt;\&lt;状态&gt;\*.png
///   每张 PNG 就是该动作的一帧（不像之前的帧条需要切），按文件名排序 = 播放顺序。
///
/// 做四件事：
///   1. 复制到 Assets/_Project/Art/Enemies/&lt;Name&gt;/&lt;State&gt;_&lt;i&gt;.png；
///   2. 背景色抠成透明（取四角颜色，容差内全清）；
///   3. 导入设置：Sprite / 单张 / 轴心在脚底 / Point / 不压缩，PPU 按「这只怪多高」反算；
///   4. 写进对应 EnemyConfig 的五个数组。
/// </summary>
public static class EnemyFrameImporter
{
    private const string SourceRoot = @"C:\Users\hp\Desktop\图片\图片";
    private const string ArtRoot = "Assets/_Project/Art/Enemies";
    private const string ConfigDir = "Assets/_Project/Data/Enemies";

    private sealed class Enemy
    {
        public string Folder;      // 桌面上的文件夹名
        public string Name;        // 预制体 / 配置 / 资产名
        public float Height;       // 世界里的身高（单位）
        public float IdleFps, MoveFps, AttackFps, HurtFps, DeathFps;
    }

    private static readonly Enemy[] Enemies =
    {
        new Enemy { Folder = "哥布林",   Name = "Goblin",         Height = 1.5f, IdleFps = 6f,  MoveFps = 10f, AttackFps = 12f, HurtFps = 12f, DeathFps = 8f  },
        new Enemy { Folder = "骷髅射手", Name = "SkeletonArcher", Height = 1.7f, IdleFps = 5f,  MoveFps = 9f,  AttackFps = 10f, HurtFps = 12f, DeathFps = 8f  },
        new Enemy { Folder = "史莱姆",   Name = "Slime",          Height = 1.1f, IdleFps = 5f,  MoveFps = 8f,  AttackFps = 8f,  HurtFps = 12f, DeathFps = 8f  },
        new Enemy { Folder = "蝙蝠",     Name = "Bat",            Height = 1.1f, IdleFps = 10f, MoveFps = 14f, AttackFps = 14f, HurtFps = 12f, DeathFps = 10f },
        new Enemy { Folder = "火焰法师", Name = "FireMage",       Height = 1.6f, IdleFps = 5f,  MoveFps = 9f,  AttackFps = 10f, HurtFps = 12f, DeathFps = 8f  },
        new Enemy { Folder = "堕落骑士", Name = "ShieldKnight",   Height = 1.9f, IdleFps = 5f,  MoveFps = 8f,  AttackFps = 10f, HurtFps = 12f, DeathFps = 8f  },
    };

    // 桌面上的状态文件夹名 → 预制体里的数组字段名
    private static readonly string[] StateFolders = { "待机", "移动", "攻击", "受击", "死亡" };
    private static readonly string[] StateNames = { "Idle", "Move", "Attack", "Hurt", "Death" };

    [MenuItem("团结/导入怪物帧（桌面图）", false, 73)]
    public static void Import()
    {
        if (!Directory.Exists(SourceRoot))
        {
            Debug.LogError("[EnemyFrames] 找不到源目录：" + SourceRoot);
            return;
        }

        int done = 0, failed = 0;
        foreach (Enemy enemy in Enemies)
        {
            if (ImportEnemy(enemy)) done++; else failed++;
        }

        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Debug.Log("[EnemyFrames] 完成：成功 " + done + " 只，失败 " + failed + " 只。");
    }

    private static bool ImportEnemy(Enemy enemy)
    {
        string sourceDir = SourceRoot + "/" + enemy.Folder;
        if (!Directory.Exists(sourceDir))
        {
            Debug.LogError("[EnemyFrames] 没有这个文件夹：" + sourceDir);
            return false;
        }

        string targetDir = ArtRoot + "/" + enemy.Name;
        EnsureFolder(targetDir);

        var sprites = new Sprite[5][];
        float ppu = 0f;

        for (int s = 0; s < 5; s++)
        {
            string stateDir = sourceDir + "/" + StateFolders[s];
            var files = new List<string>();
            if (Directory.Exists(stateDir)) files.AddRange(Directory.GetFiles(stateDir, "*.png"));
            files.Sort(string.CompareOrdinal);          // 文件名里的时间戳就是帧序

            sprites[s] = new Sprite[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                string assetPath = targetDir + "/" + enemy.Name + "_" + StateNames[s] + "_" + i + ".png";
                CopyAndKey(files[i], assetPath);

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                if (ppu <= 0f) ppu = ComputePpu(assetPath, enemy.Height);   // 第一帧定 PPU，全组共用
                ConfigureImporter(assetPath, ppu);
                sprites[s][i] = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }
        }

        AssignConfig(enemy, sprites);

        string counts = "";
        for (int s = 0; s < 5; s++) counts += StateFolders[s] + " " + sprites[s].Length + " 帧  ";
        Debug.Log("[EnemyFrames] " + enemy.Folder + " → " + enemy.Name + "：" + counts + "（PPU " + ppu.ToString("0") + "）");
        return true;
    }

    /// <summary>复制并抠掉背景：取四角颜色当底色，整张图容差内的像素全变透明。</summary>
    private static void CopyAndKey(string sourcePath, string assetPath)
    {
        File.Copy(sourcePath, assetPath, true);

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(assetPath));

        int w = texture.width, h = texture.height;
        Color32[] pixels = texture.GetPixels32();

        Color32 bg = pixels[0];                                  // 左上角就是背景
        const int tolerance = 90;                                // 放宽：截图底色常有轻微渐变
        var cleared = new bool[pixels.Length];
        var queue = new Queue<int>();

        // 只从**四边**泛洪：人物内部和背景同色的像素不会被误伤（之前整张全清就有这个毛病）
        for (int x = 0; x < w; x++)
        {
            queue.Enqueue(x);                        // 下边
            queue.Enqueue((h - 1) * w + x);          // 上边
        }
        for (int y = 0; y < h; y++)
        {
            queue.Enqueue(y * w);                    // 左边
            queue.Enqueue(y * w + w - 1);            // 右边
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (i < 0 || i >= pixels.Length || cleared[i]) continue;

            Color32 c = pixels[i];
            if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > tolerance) continue;

            cleared[i] = true;
            pixels[i] = new Color32(0, 0, 0, 0);

            int x2 = i % w;
            if (x2 > 0) queue.Enqueue(i - 1);
            if (x2 < w - 1) queue.Enqueue(i + 1);
            if (i >= w) queue.Enqueue(i - w);
            if (i < pixels.Length - w) queue.Enqueue(i + w);
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(assetPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    private static float ComputePpu(string assetPath, float targetHeight)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (texture == null) return 100f;
        return Mathf.Max(1f, texture.height / Mathf.Max(0.1f, targetHeight));
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

        // 轴心必须走 TextureImporterSettings —— TextureImporter 上没有 spriteAlignment 属性
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, 0f);     // 脚底
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }

    private static void AssignConfig(Enemy enemy, Sprite[][] sprites)
    {
        var config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(ConfigDir + "/Enemy_" + enemy.Name + ".asset");
        if (config == null)
        {
            Debug.LogWarning("[EnemyFrames] 找不到配置 Enemy_" + enemy.Name + ".asset —— 先跑「团结 → 生成怪物资源」。");
            return;
        }

        config.idleFrames = sprites[0];
        config.moveFrames = sprites[1];
        config.attackFrames = sprites[2];
        config.hurtFrames = sprites[3];
        config.deathFrames = sprites[4];

        config.idleFps = enemy.IdleFps;
        config.moveFps = enemy.MoveFps;
        config.attackFps = enemy.AttackFps;
        config.hurtFps = enemy.HurtFps;
        config.deathFps = enemy.DeathFps;

        EditorUtility.SetDirty(config);
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
