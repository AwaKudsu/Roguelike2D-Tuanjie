// 把「素材大师」生成的 UI 图搬进工程：抠掉纯色底、按内容裁边、写导入参数。
//
// 用法：菜单「团结 → 导入 UI 素材」。跑一次就行，重跑会覆盖同名资源。
// 这些图本来在 D:\Deepseek_Hardness\dsh-home\game-material-master\image-jobs\<作业id>\out\img-00.png，
// 不在 Assets 里；这里直接用 File 读、用 Texture2D 改、再写回 Assets，绕开 Unity 的资源管线。
//
// 为什么要自己抠底：素材大师的 keyImageJob 依赖 ffmpeg，这台机器上没有，报「找不到 ffprobe」。
// 而这些图标本来就是「纯色平底 + 深黑描边」，用「到底色的人为距离」直接抠就很干净。
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class UiArtImporter
{
    private const string ArtDir = "Assets/_Project/Art/UI";
    private const string ReportPath = "_Tuanjie/ui-art-report.txt";

    // 素材大师的作业目录。换一批图时改这里。
    private const string JobRoot =
        @"D:\Deepseek_Hardness\dsh-home\game-material-master\image-jobs";

    private struct Job
    {
        public string Id;         // 作业 id，产物固定是 <JobRoot>/<Id>/out/img-00.png
        public string AssetName;  // 落盘名（不含扩展名）
        public float Ppu;         // 每单位像素：九宫格边框的粗细靠它，边框UI像素 = Border / Ppu * 100
        public int Border;        // 九宫格边（源图像素），0 = 普通图
        public bool Key;          // 是否抠掉底色
        public Color KeyColor;    // 底色
        public int TargetSize;    // 缩放到的边长（正方形），0 = 原尺寸
        public int LongEdge;      // 非正方形用：把长边缩到这个值、保持比例，0 = 不动
        public string Folder;     // 落盘目录，空 = ArtDir（UI 图）；大厅陈设要写到 Art/Lobby
    }

    private static readonly Job[] Jobs =
    {
        // 面板：双层金铜框 + 四角铆钉，中间是平的深蓝黑，正好当九宫格拉伸
        // PPU 730 = 边框在 600x174 的 HUD 板上约 24px（175 / 730 * 100）
        new Job { Id = "imw6ox6p497615b", AssetName = "UI_Panel", Ppu = 730f, Border = 175 },
        // 格子：外圈亮金 + 内凹槽，给技能格、装备格、立绘框、条底共用
        // PPU 640 = 边框在 62x62 的格子上约 8px
        new Job { Id = "imw6oxx6221290c", AssetName = "UI_Slot", Ppu = 640f, Border = 50 },

        // 技能图标：绿幕/洋红幕，抠完按内容裁边；缩到 128 是因为格子里只有 54 像素，
        // 1024 缩下去用点采样会糊成噪点
        new Job { Id = "imw6oyyh5df4914", AssetName = "Icon_Fireball",  Ppu = 100f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 128 },
        new Job { Id = "imw6ozfv2c5dd2d", AssetName = "Icon_Whirlwind", Ppu = 100f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 128 },
        new Job { Id = "imw6ozuo91c3d97", AssetName = "Icon_Heal",      Ppu = 100f, Key = true, KeyColor = new Color(1f, 0f, 1f), TargetSize = 128 },
        new Job { Id = "imw6p0afe38265a", AssetName = "Icon_Warcry",    Ppu = 100f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 128 },

        // 战斗特效：都是绿幕的「一次性烟花」，最后缩到 256（PPU 256 → 1 个世界单位直径）
        // 打击火花在命中点崩一下；漩涡绕身转；冲击波从脚下往外扩
        new Job { Id = "imw6pd6g67b89f1", AssetName = "FX_HitSpark",   Ppu = 256f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 256 },
        new Job { Id = "imw6pdmp15d7f16", AssetName = "FX_SpinAura",   Ppu = 256f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 256 },
        new Job { Id = "imw6pe30w2da825", AssetName = "FX_WarcryRing", Ppu = 256f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 256 },

        // 刀光弧：模型给的是 1776×592 的横躺月牙，不能按正方形缩（会被压扁），
        // 所以缩「长边」到 512，PPU 512 → 贴图宽度正好 1 个世界单位，代码里再按挥砍半径放大
        new Job { Id = "imw6pr5d61b65e1", AssetName = "FX_SlashArc", Ppu = 512f, Key = true, KeyColor = new Color(0f, 1f, 0f), LongEdge = 512 },

        // 法师专用：奥术球（弹体）、奥术爆裂（普攻命中）、火焰爆裂（火球命中）
        // 战士的命中是「刀砍出来的白热星芒」，法师不能也是它 —— 一个是四角星、一个是符文碎片
        new Job { Id = "imw6q083b534185", AssetName = "FX_MageBolt",    Ppu = 256f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 256 },
        new Job { Id = "imw6q20isf93364", AssetName = "FX_ArcaneBurst", Ppu = 256f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 256 },
        new Job { Id = "imw6q2hozc62262", AssetName = "FX_FireBurst",   Ppu = 256f, Key = true, KeyColor = new Color(0f, 1f, 0f), TargetSize = 256 },

        // 贴图版刀光：一张「扇形弧带」，弧的圆心在画布正中、弧在上半部分。
        // 代码把它挂在玩家身上绕圆心旋转 → 就是一次挥砍（SlashArcFx）。
        // 宽高都按 1024 保留（弧的半径精度直接决定旋转时的抖动），不抠底（模型给的就是透明背景）
        new Job { Id = "imw6sz4hf7919c2", AssetName = "FX_SlashSweep", Ppu = 1024f, TargetSize = 1024 },

        // 大厅陈设：书架、告示板（技能树交互点）。
        // 贴图进 Art/Lobby，PPU 与 LobbyRoomBuilder.ArtFiles 里写的一致，摆进去就是想要的尺寸
        new Job { Id = "imw6vowte23eaaa", AssetName = "LobbyBookshelf",   Ppu = 480f, Key = true, KeyColor = new Color(0f, 1f, 0f), Folder = "Assets/_Project/Art/Lobby" },
        new Job { Id = "imw6vpf5o3d00fa", AssetName = "LobbyNoticeBoard", Ppu = 470f, Key = true, KeyColor = new Color(0f, 1f, 0f), Folder = "Assets/_Project/Art/Lobby" },

        // 法师的正 / 背视图（大厅上下走要用）。
        // 画布缩到 320×480 与关卡帧一致；PPU 235 是反算出来的：
        // 新图里人物约占画面高度 51%（246px），关卡帧里人物约 170px = 1.06 单位，
        // 所以 246 / 1.06 ≈ 232，取 235 —— 这样正脸和侧脸看起来一样高，脚底位置也只差 0.04 单位。
        new Job { Id = "imw6x637n2d238a", AssetName = "Mage_Front", Ppu = 235f, Key = true, KeyColor = new Color(0f, 1f, 0f), LongEdge = 480, Folder = "Assets/_Project/Art/Characters/Mage" },
        new Job { Id = "imw6x6rug699336", AssetName = "Mage_Back",  Ppu = 235f, Key = true, KeyColor = new Color(0f, 1f, 0f), LongEdge = 480, Folder = "Assets/_Project/Art/Characters/Mage" },
    };

    [MenuItem("团结/导入 UI 素材", false, 61)]
    public static void Import()
    {
        var log = new StringBuilder();
        log.AppendLine("===== 导入 UI 素材 =====");

        if (!Directory.Exists(ArtDir)) Directory.CreateDirectory(ArtDir);

        int ok = 0, fail = 0;
        foreach (Job job in Jobs)
        {
            string src = Path.Combine(JobRoot, job.Id, "out", "img-00.png");
            if (!File.Exists(src))
            {
                log.AppendLine("[FAIL] " + job.AssetName + "：找不到源图 " + src);
                fail++;
                continue;
            }

            Texture2D tex = Load(src, log, job.AssetName);
            if (tex == null) { fail++; continue; }

            if (job.Key) tex = KeyOut(tex, job.KeyColor, log, job.AssetName);
            if (job.Key) tex = CropToContent(tex, 2);
            if (job.TargetSize > 0) tex = Downscale(tex, job.TargetSize);
            if (job.LongEdge > 0) tex = DownscaleLongEdge(tex, job.LongEdge);

            string assetPath = (string.IsNullOrEmpty(job.Folder) ? ArtDir : job.Folder) + "/" + job.AssetName + ".png";
            string assetDir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(assetDir) && !Directory.Exists(assetDir)) Directory.CreateDirectory(assetDir);

            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ApplyImportSettings(assetPath, job);
            ok++;
            log.AppendLine("[PASS] " + job.AssetName + "：PPU " + job.Ppu +
                           (job.Border > 0 ? "，九宫格边 " + job.Border + "px" : "") +
                           (job.TargetSize > 0 ? "，缩到 " + job.TargetSize : "") +
                           " → " + assetPath);
        }

        AssetDatabase.Refresh();
        log.AppendLine("[SUMMARY] 成功 " + ok + " 张，失败 " + fail + " 张。");
        log.AppendLine("下一步：菜单「团结 → 修复 HUD 与技能栏」把新图接上去。");

        string text = log.ToString();
        Debug.Log(text);
        File.WriteAllText(ReportPath, text, new UTF8Encoding(false));
        EditorUtility.DisplayDialog("导入完成", text, "好");
    }

    // ------------------------------------------------------------------ 接特效

    // 图搬进来之后还得「谁用哪张」，这一步做接线：
    //   · Skill_WarCry.ringSprite   ← FX_WarcryRing（冲击波两圈共用这一张）
    //   · Skill_Whirlwind.auraSprite ← FX_SpinAura（绕身漩涡）
    //   · 玩家身上的 PlayerAttacker.hitSparkSprite ← FX_HitSpark（命中火花）
    //   · 玩家身上的 PlayerAttacker.slashSweepSprite ← FX_SlashSweep（贴图版刀光）
    // 菜单「团结 → 接特效资源」。
    [MenuItem("团结/接特效资源", false, 62)]
    public static void WireFx()
    {
        var log = new StringBuilder();
        log.AppendLine("===== 接特效资源 =====");

        int ok = 0;

        if (AssignSkillSprite("Assets/_Project/Data/Skills/Skill_WarCry.asset", "ringSprite", "FX_WarcryRing.png", log)) ok++;
        if (AssignSkillSprite("Assets/_Project/Data/Skills/Skill_Whirlwind.asset", "auraSprite", "FX_SpinAura.png", log)) ok++;

        var player = GameObject.FindGameObjectWithTag("Player");
        var attacker = player != null ? player.GetComponent<PlayerAttacker>() : null;

        if (attacker == null)
        {
            log.AppendLine("[FAIL] 场景里找不到挂着 PlayerAttacker 的 Player");
        }
        else
        {
            var so = new SerializedObject(attacker);

            if (AssignSprite(so, "hitSparkSprite", "FX_HitSpark.png", log)) ok++;
            if (AssignSprite(so, "slashArcSprite", "FX_SlashArc.png", log)) ok++;
            if (AssignSprite(so, "slashSweepSprite", "FX_SlashSweep.png", log)) ok++;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(attacker);

            // 投射物（法师法杖弹 / 火球）的命中特效：预制体上各接一次
            ok += AssignMageBolt("Assets/_Project/Prefabs/MageBolt.prefab", log);
            ok += AssignProjectileImpact("Assets/_Project/Prefabs/Fireball.prefab", "Fireball",
                "FX_FireBurst.png", new Color(1f, 0.86f, 0.6f, 1f), 0f, log);
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        log.AppendLine("[SUMMARY] 接上 " + ok + " 处。");
        log.AppendLine("下一步：菜单「团结 → 修复 HUD 与技能栏」（技能图标），然后进 Play 试试挥砍、旋风斩、战吼。");

        string text = log.ToString();
        Debug.Log(text);
        File.WriteAllText(ReportPath, text, new UTF8Encoding(false));
        EditorUtility.DisplayDialog("接线完成", text, "好");
    }

    /// <summary>
    /// 把九宫格 UI 图接到背包界面。背包是纯代码搭的 UI，图片只能在运行时由脚本持有，
    /// 所以这里直接把两张 Sprite 写进场景里那个 InventoryUI 组件的字段。
    /// </summary>
    [MenuItem("团结/接 UI 美术", false, 62)]
    public static void WireUi()
    {
        var log = new StringBuilder();
        log.AppendLine("===== 接 UI 美术 =====");

        var ui = UnityEngine.Object.FindObjectOfType<InventoryUI>(true);
        if (ui == null)
        {
            log.AppendLine("[FAIL] 场景里找不到 InventoryUI（先打开 Game.unity）");
        }
        else
        {
            var so = new SerializedObject(ui);
            int ok = 0;
            if (AssignSprite(so, "panelSprite", "UI_Panel.png", log)) ok++;
            if (AssignSprite(so, "slotSprite", "UI_Slot.png", log)) ok++;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ui);
            log.AppendLine(ok == 2 ? "[PASS] 背包界面已换成九宫格美术" : "[WARN] 有图没接上，检查「导入 UI 素材」跑过没有");
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string text = log.ToString();
        Debug.Log(text);
        File.WriteAllText(ReportPath, text, new UTF8Encoding(false));
        EditorUtility.DisplayDialog("接 UI 美术", text, "好");
    }

    private static bool AssignSprite(SerializedObject so, string fieldName, string fileName, StringBuilder log)    {
        Sprite sprite = LoadSprite(ArtDir + "/" + fileName);
        if (sprite == null)
        {
            log.AppendLine("[SKIP] " + fieldName + "：" + fileName + " 还没导入");
            return false;
        }

        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            log.AppendLine("[FAIL] 没有字段 " + fieldName);
            return false;
        }

        prop.objectReferenceValue = sprite;
        log.AppendLine("[PASS] PlayerAttacker." + fieldName + " ← " + fileName);
        return true;
    }

    /// <summary>
    /// 给投射物预制体接命中特效图 + 染色 + 自转。
    /// 每颗子弹用自己那张图 —— 刀砍的白热星芒不该出现在奥术球上。
    /// </summary>
    private static int AssignProjectileImpact(string prefabPath, string label, string fileName,
        Color tint, float spin, StringBuilder log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            log.AppendLine("[SKIP] " + label + "：找不到 " + prefabPath);
            return 0;
        }

        var projectile = prefab.GetComponent<Projectile>();
        if (projectile == null)
        {
            log.AppendLine("[FAIL] " + label + " 上没有 Projectile");
            return 0;
        }

        Sprite impact = LoadSprite(ArtDir + "/" + fileName);
        if (impact == null)
        {
            log.AppendLine("[SKIP] " + label + "：" + fileName + " 还没导入");
            return 0;
        }

        var so = new SerializedObject(projectile);
        var spriteProp = so.FindProperty("impactSprite");
        if (spriteProp != null) spriteProp.objectReferenceValue = impact;

        var colorProp = so.FindProperty("impactColor");
        if (colorProp != null) colorProp.colorValue = tint;

        var spinProp = so.FindProperty("spinSpeed");
        if (spinProp != null) spinProp.floatValue = spin;

        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SavePrefabAsset(prefab);
        EditorUtility.SetDirty(projectile);
        log.AppendLine("[PASS] " + label + ".impactSprite ← " + fileName);
        return 1;
    }

    /// <summary>法师普攻：换弹体贴图（奥术球）+ 命中图（符文爆裂）+ 让它自转。</summary>
    private static int AssignMageBolt(string prefabPath, StringBuilder log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            log.AppendLine("[SKIP] MageBolt：找不到 " + prefabPath);
            return 0;
        }

        Sprite bolt = LoadSprite(ArtDir + "/FX_MageBolt.png");
        if (bolt == null)
        {
            log.AppendLine("[SKIP] MageBolt：FX_MageBolt 还没导入");
            return 0;
        }

        var body = prefab.GetComponentInChildren<SpriteRenderer>(true);
        if (body == null)
        {
            log.AppendLine("[FAIL] MageBolt 上没有 SpriteRenderer");
            return 0;
        }

        body.sprite = bolt;
        EditorUtility.SetDirty(body);
        PrefabUtility.SavePrefabAsset(prefab);
        log.AppendLine("[PASS] MageBolt 弹体 ← FX_MageBolt");

        // 奥术球自转：一圈符文在转，「东西是活的」就出来了
        return AssignProjectileImpact(prefabPath, "MageBolt", "FX_ArcaneBurst.png",
            new Color(0.82f, 0.94f, 1f, 1f), 200f, log);
    }

    private static bool AssignSkillSprite(string assetPath, string fieldName, string fileName, StringBuilder log)
    {
        var skill = AssetDatabase.LoadAssetAtPath<SkillData>(assetPath);
        if (skill == null)
        {
            log.AppendLine("[FAIL] 找不到 " + assetPath);
            return false;
        }

        Sprite sprite = LoadSprite(ArtDir + "/" + fileName);
        if (sprite == null)
        {
            log.AppendLine("[SKIP] " + skill.skillId + "：" + fileName + " 还没导入");
            return false;
        }

        var so = new SerializedObject(skill);
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            log.AppendLine("[FAIL] " + assetPath + " 上没有字段 " + fieldName);
            return false;
        }

        prop.objectReferenceValue = sprite;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skill);
        log.AppendLine("[PASS] " + skill.skillId + "." + fieldName + " ← " + fileName);
        return true;
    }

    private static Sprite LoadSprite(string path)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ------------------------------------------------------------------ 读图

    private static Texture2D Load(string path, StringBuilder log, string name)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            log.AppendLine("[FAIL] " + name + "：这张图读不出来 " + path);
            UnityEngine.Object.DestroyImmediate(tex);
            return null;
        }
        return tex;
    }

    // ------------------------------------------------------------------ 抠底

    // 用「到底色的切比雪夫距离」抠：距离很小 → 全透明；中间 → 半透明（吃掉描边外的那圈混色）；
    // 同时把偏绿/偏洋红的那点余色压掉，不然放大看边缘会有一圈荧光。
    private static Texture2D KeyOut(Texture2D tex, Color key, StringBuilder log, string name)
    {
        const float inner = 0.12f;   // 小于它 = 纯底色
        const float outer = 0.42f;   // 大于它 = 完全是内容

        Color[] px = tex.GetPixels();
        int cleared = 0;
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            float d = Mathf.Max(Mathf.Abs(c.r - key.r), Mathf.Max(Mathf.Abs(c.g - key.g), Mathf.Abs(c.b - key.b)));
            if (d <= inner)
            {
                c.a = 0f;
                cleared++;
            }
            else if (d < outer)
            {
                c.a = Mathf.Min(c.a, (d - inner) / (outer - inner));
                Despill(ref c, key);
            }
            else
            {
                Despill(ref c, key);
            }
            px[i] = c;
        }

        tex.SetPixels(px);
        tex.Apply();

        float ratio = cleared * 100f / Mathf.Max(1, px.Length);
        log.AppendLine("[INFO] " + name + "：抠掉 " + ratio.ToString("0.0") + "% 的像素");
        if (ratio < 5f)
            log.AppendLine("[WARN] " + name + "：底色只占 " + ratio.ToString("0.0") +
                           "%，可能这张图根本没画纯色底，抠完会留边");
        return tex;
    }

    private static void Despill(ref Color c, Color key)
    {
        if (key.g > 0.5f && key.r < 0.5f && key.b < 0.5f)
        {
            float cap = Mathf.Max(c.r, c.b) + 0.06f;
            if (c.g > cap) c.g = cap;
        }
        else if (key.r > 0.5f && key.b > 0.5f && key.g < 0.5f)
        {
            float cap = Mathf.Max(c.g, c.b) + 0.06f;
            if (c.r > cap) c.r = cap;
            cap = Mathf.Max(c.r, c.g) + 0.06f;
            if (c.b > cap) c.b = cap;
        }
    }

    // ------------------------------------------------------------------ 裁边

    // 抠完四周全是透明，留着当九宫格/居中都会偏，按 alpha 的实际边界裁掉，只留 pad 的空隙。
    private static Texture2D CropToContent(Texture2D tex, int pad)
    {
        Color[] px = tex.GetPixels();
        int w = tex.width, h = tex.height;
        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].a <= 0.02f) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < minX || maxY < minY) return tex;   // 全透明，别裁了

        minX = Mathf.Max(0, minX - pad);
        minY = Mathf.Max(0, minY - pad);
        maxX = Mathf.Min(w - 1, maxX + pad);
        maxY = Mathf.Min(h - 1, maxY + pad);

        int nw = maxX - minX + 1, nh = maxY - minY + 1;
        var outTex = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
        var dst = new Color[nw * nh];
        for (int y = 0; y < nh; y++)
        {
            Array.Copy(px, (minY + y) * w + minX, dst, y * nw, nw);
        }
        outTex.SetPixels(dst);
        outTex.Apply();
        UnityEngine.Object.DestroyImmediate(tex);
        return outTex;
    }

    // ------------------------------------------------------------------ 缩放

    // 最近邻：像素画缩小只能点采样，双线性会把 2px 描边糊成灰边。
    private static Texture2D Downscale(Texture2D tex, int size)
    {
        if (tex.width == size && tex.height == size) return tex;

        Color[] src = tex.GetPixels();
        int sw = tex.width, sh = tex.height;
        var dst = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            int sy = Mathf.Clamp((int)((y + 0.5f) * sh / size), 0, sh - 1);
            for (int x = 0; x < size; x++)
            {
                int sx = Mathf.Clamp((int)((x + 0.5f) * sw / size), 0, sw - 1);
                dst[y * size + x] = src[sy * sw + sx];
            }
        }

        var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        outTex.SetPixels(dst);
        outTex.Apply();
        UnityEngine.Object.DestroyImmediate(tex);
        return outTex;
    }

    // 非正方形的图（比如刀光弧 3:1）：只缩长边、保持比例。
    // 硬缩成正方形会把月牙压成一条扁片。
    private static Texture2D DownscaleLongEdge(Texture2D tex, int longEdge)
    {
        int w = tex.width, h = tex.height;
        if (Mathf.Max(w, h) <= longEdge) return tex;

        float k = longEdge / (float)Mathf.Max(w, h);
        int nw = Mathf.Max(1, Mathf.RoundToInt(w * k));
        int nh = Mathf.Max(1, Mathf.RoundToInt(h * k));

        Color[] src = tex.GetPixels();
        var dst = new Color[nw * nh];
        for (int y = 0; y < nh; y++)
        {
            int sy = Mathf.Clamp((int)((y + 0.5f) * h / nh), 0, h - 1);
            for (int x = 0; x < nw; x++)
            {
                int sx = Mathf.Clamp((int)((x + 0.5f) * w / nw), 0, w - 1);
                dst[y * nw + x] = src[sy * w + sx];
            }
        }

        var outTex = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
        outTex.SetPixels(dst);
        outTex.Apply();
        UnityEngine.Object.DestroyImmediate(tex);
        return outTex;
    }

    // ------------------------------------------------------------------ 导入参数

    private static void ApplyImportSettings(string path, Job job)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = job.Ppu;
        importer.spriteBorder = job.Border > 0
            ? new Vector4(job.Border, job.Border, job.Border, job.Border)
            : Vector4.zero;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }
}
