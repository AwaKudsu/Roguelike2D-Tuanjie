using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 菜单「团结 → 生成怪物资源」：一次性把六只怪的配置资产和预制体建出来。
///
/// ★ 为什么不手搓：
///   六只怪 = 六个 EnemyConfig + 六个预制体（Rigidbody2D / 碰撞体 / Health /
///   CharacterStats / HitReaction / EnemyController 六件套，外加私有字段靠手拖）。
///   手搓一遍容易漏字段，而且以后第七只怪还得再来一次。
///
/// ★ 数值都是「先能跑」的占位值，不是平衡数值 —— 想调直接改 Data/enemies 下的资产。
///   攻击力 / 血量 / 移速写进 CharacterStats（唯一数值出口），
///   行为参数（距离、节奏、跳跃高度）写进 EnemyConfig。
/// </summary>
public static class EnemyBuilder
{
    private const string ConfigDir = "Assets/_Project/Data/enemies";
    private const string PrefabDir = "Assets/_Project/Prefabs/Enemies";
    private const string PlaceholderPath = "Assets/_Project/Art/Placeholder/Square.png";
    private const string FireballPath = "Assets/_Project/Prefabs/Fireball.prefab";
    private const string EnemyAttackPath = "Assets/_Project/Data/Weapons/Attack_Enemy.asset";

    [MenuItem("团结/生成怪物资源", false, 70)]
    public static void BuildAll()
    {
        EnsureFolder(ConfigDir);
        EnsureFolder(PrefabDir);

        Sprite placeholder = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPath);
        if (placeholder == null)
            Debug.LogWarning("[EnemyBuilder] 没找到 " + PlaceholderPath + "，预制体先不带图。");

        Projectile arrow = BuildArrowPrefab();
        Projectile fireball = AssetDatabase.LoadAssetAtPath<Projectile>(FireballPath);
        AttackData enemySwing = AssetDatabase.LoadAssetAtPath<AttackData>(EnemyAttackPath);
        if (enemySwing == null)
            Debug.LogWarning("[EnemyBuilder] 没找到 " + EnemyAttackPath + "，近战怪会打不出伤害。");

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0)
        {
            Debug.LogWarning("[EnemyBuilder] 没有 Enemy 层 —— 玩家的攻击判定（targetLayers=Enemy）会打不到它们。");
            enemyLayer = 0;
        }

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer < 0)
        {
            Debug.LogWarning("[EnemyBuilder] 没有 Player 层，敌人的索敌会打不到人。");
            playerLayer = 0;
        }

        int ok = 0;

        // ---- 哥布林 ----
        EnemyConfig goblin = NewConfig("Enemy_Goblin", c =>
        {
            c.displayName = "哥布林";
            c.behavior = EnemyBehavior.Melee;
            c.detectRange = 9f; c.attackRange = 1.15f; c.loseTargetRange = 14f;
            c.meleeCooldown = 1.4f; c.meleeWindup = 0.28f;
            c.meleeAttack = enemySwing;
        });
        ok += MakePrefab("Goblin", goblin, placeholder, new Color(0.45f, 0.72f, 0.35f), 28f, 6f, 3.4f, 0f, false, playerLayer, enemyLayer) ? 1 : 0;

        // ---- 骷髅弓箭手 ----
        EnemyConfig archer = NewConfig("Enemy_SkeletonArcher", c =>
        {
            c.displayName = "骷髅弓箭手";
            c.behavior = EnemyBehavior.Archer;
            c.detectRange = 11f; c.attackRange = 2.5f; c.loseTargetRange = 16f;
            c.rangedRange = 8f; c.rangedCooldown = 2.2f; c.retreatDistance = 3f; c.chargeTime = 0.5f;
            c.projectilePrefab = arrow;
        });
        ok += MakePrefab("SkeletonArcher", archer, placeholder, new Color(0.85f, 0.84f, 0.78f), 22f, 5f, 2.8f, 0f, false, playerLayer, enemyLayer) ? 1 : 0;

        // ---- 史莱姆 ----
        EnemyConfig slime = NewConfig("Enemy_Slime", c =>
        {
            c.displayName = "史莱姆";
            c.behavior = EnemyBehavior.Jumper;
            c.element = DamageElement.Poison;
            c.detectRange = 8f; c.attackRange = 1.2f; c.loseTargetRange = 13f;
            c.jumpDistance = 3.6f; c.jumpDuration = 0.55f; c.jumpHeight = 1.5f; c.landRest = 0.9f;
            c.landRadius = 1.2f; c.landDamage = 8f;
        });
        ok += MakePrefab("Slime", slime, placeholder, new Color(0.42f, 0.68f, 0.30f), 30f, 4f, 2.2f, 0f, false, playerLayer, enemyLayer) ? 1 : 0;

        // ---- 蝙蝠 ----
        EnemyConfig bat = NewConfig("Enemy_Bat", c =>
        {
            c.displayName = "蝙蝠";
            c.behavior = EnemyBehavior.Flyer;
            c.detectRange = 10f; c.attackRange = 1.2f; c.loseTargetRange = 15f;
            c.hoverDistance = 2.8f; c.hoverHeight = 1.4f;
            c.diveSpeed = 10f; c.diveDuration = 0.45f; c.diveCooldown = 1.8f; c.diveDamage = 7f;
        });
        ok += MakePrefab("Bat", bat, placeholder, new Color(0.55f, 0.42f, 0.68f), 18f, 4f, 3.2f, 0f, true, playerLayer, enemyLayer) ? 1 : 0;

        // ---- 火焰法师 ----
        EnemyConfig caster = NewConfig("Enemy_FireMage", c =>
        {
            c.displayName = "火焰法师";
            c.behavior = EnemyBehavior.Caster;
            c.element = DamageElement.Fire;
            c.detectRange = 11f; c.attackRange = 3f; c.loseTargetRange = 16f;
            c.rangedRange = 8f; c.rangedCooldown = 2.6f; c.retreatDistance = 3.5f; c.chargeTime = 0.9f;
            c.projectilePrefab = fireball;
        });
        ok += MakePrefab("FireMage", caster, placeholder, new Color(0.85f, 0.35f, 0.25f), 24f, 8f, 2.6f, 0f, false, playerLayer, enemyLayer) ? 1 : 0;

        // ---- 持盾战士 ----
        EnemyConfig shielder = NewConfig("Enemy_ShieldKnight", c =>
        {
            c.displayName = "持盾战士";
            c.behavior = EnemyBehavior.Shielder;
            c.detectRange = 9f; c.attackRange = 1.3f; c.loseTargetRange = 14f;
            c.meleeCooldown = 1.6f; c.meleeWindup = 0.3f;
            c.meleeAttack = enemySwing;
            c.shieldReduction = 0.6f; c.shieldArc = 100f;
            c.chargeTriggerRange = 5f; c.chargeWindup = 0.55f;
            c.chargeSpeed = 9f; c.chargeDuration = 0.6f; c.chargeDamage = 12f; c.chargeCooldown = 3f;
        });
        ok += MakePrefab("ShieldKnight", shielder, placeholder, new Color(0.62f, 0.66f, 0.72f), 60f, 8f, 2.4f, 0.15f, false, playerLayer, enemyLayer) ? 1 : 0;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[EnemyBuilder] 完成：" + ok + "/6 个预制体，配置在 " + ConfigDir + "，预制体在 " + PrefabDir);
    }

    /// <summary>
    /// 往房间预制体里塞怪：Room_01 放 哥布林×2 + 弓箭手，Room_02 放 史莱姆 + 蝙蝠。
    /// 位置是固定的几个点，房间地板大约 20×10，y=1.1 是站在地面上的高度。
    /// </summary>
    [MenuItem("团结/往房间加怪物", false, 71)]
    public static void FillRooms()
    {
        FillRoom("Assets/_Project/Prefabs/Rooms/Room_01.prefab", new[]
        {
            ("Goblin", new Vector3(6f, 0.15f)),
            ("Goblin", new Vector3(9f, 0.15f)),
            ("SkeletonArcher", new Vector3(14f, 0.15f)),
        });

        FillRoom("Assets/_Project/Prefabs/Rooms/Room_02.prefab", new[]
        {
            // 脚底贴地：轴心在脚底，y=1.1 会让小个子（史莱姆只有 1.1 高）出生就在半空
            ("Slime", new Vector3(7f, 0.15f)),
            ("Bat", new Vector3(12f, 3.2f)),
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void FillRoom(string roomPath, (string prefab, Vector3 position)[] enemies)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(roomPath);
        if (prefab == null)
        {
            Debug.LogWarning("[EnemyBuilder] 找不到房间预制体 " + roomPath);
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(roomPath);
        int added = 0;

        foreach (var (prefabName, position) in enemies)
        {
            string path = PrefabDir + "/" + prefabName + ".prefab";
            GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (enemyPrefab == null)
            {
                Debug.LogWarning("[EnemyBuilder] 缺少 " + path + "，先跑一次「生成怪物资源」。");
                continue;
            }

            // 已经有这个品种就不重复塞，方便反复点
            bool already = false;
            foreach (Transform child in root.transform)
            {
                if (child.name.StartsWith(prefabName + "_")) { already = true; break; }
            }
            if (already) continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, root.transform);
            instance.transform.localPosition = position;
            instance.name = prefabName + "_" + added;
            added++;
        }

        PrefabUtility.SaveAsPrefabAsset(root, roomPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log("[EnemyBuilder] " + System.IO.Path.GetFileName(roomPath) + " => " + added + " enemies");
    }

    // ---------------- 造物 ----------------

    private static Projectile BuildArrowPrefab()
    {
        const string path = PrefabDir + "/Arrow.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<Projectile>(path);
        if (existing != null) return existing;

        var go = new GameObject("Arrow");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPath);
        sr.color = new Color(0.9f, 0.86f, 0.72f);
        sr.sortingOrder = 12;
        go.transform.localScale = new Vector3(0.5f, 0.18f, 1f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var circle = go.AddComponent<CircleCollider2D>();
        circle.isTrigger = true;
        circle.radius = 0.3f;

        go.AddComponent<Projectile>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        var so = new SerializedObject(prefab.GetComponent<Projectile>());
        so.FindProperty("speed").floatValue = 16f;
        so.FindProperty("lifeTime").floatValue = 2.4f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(prefab);
        AssetDatabase.SaveAssets();

        return prefab.GetComponent<Projectile>();
    }

    private static EnemyConfig NewConfig(string assetName, System.Action<EnemyConfig> configure)
    {
        string path = ConfigDir + "/" + assetName + ".asset";
        var config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<EnemyConfig>();
            AssetDatabase.CreateAsset(config, path);
        }

        configure(config);
        EditorUtility.SetDirty(config);
        return config;
    }

    private static bool MakePrefab(string prefabName, EnemyConfig config, Sprite sprite, Color tint,
                                   float maxHealth, float attack, float moveSpeed, float defense,
                                   bool noGravity, int playerLayer, int enemyLayer)
    {
        string path = PrefabDir + "/" + prefabName + ".prefab";

        // 已经生成过就「打补丁」而不是跳过 —— 改了层、改了数值要能一键同步过去
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            ApplyEnemySettings(contents, config, sprite, tint, maxHealth, attack, moveSpeed,
                               defense, noGravity, playerLayer, enemyLayer);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("[EnemyBuilder] " + prefabName + " updated (layer/data)");
            return true;
        }

        var go = new GameObject(prefabName);
        ApplyEnemySettings(go, config, sprite, tint, maxHealth, attack, moveSpeed,
                           defense, noGravity, playerLayer, enemyLayer);
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return true;
    }

    /// <summary>
    /// 建/改一只怪的预制体内容。新建和打补丁走同一段代码，避免两套逻辑慢慢长歪。
    /// ★ 层一定要设成 Enemy：玩家的攻击判定是按 targetLayers=Enemy 找碰撞体的，
    ///   留在 Default 上就是「打不着、也不打你」。
    /// </summary>
    private static void ApplyEnemySettings(GameObject go, EnemyConfig config, Sprite sprite, Color tint,
                                           float maxHealth, float attack, float moveSpeed, float defense,
                                           bool noGravity, int playerLayer, int enemyLayer)
    {
        go.layer = enemyLayer;
        foreach (Transform child in go.transform) child.gameObject.layer = enemyLayer;

        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = tint;
        sr.sortingOrder = 5;

        Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
        if (rb == null) rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = noGravity ? 0f : 3f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        CapsuleCollider2D capsule = go.GetComponent<CapsuleCollider2D>();
        if (capsule == null) capsule = go.AddComponent<CapsuleCollider2D>();
        capsule.size = new Vector2(0.85f, 1.1f);
        // 轴心在脚底，盒子必须整体抬到脚底以上；否则一半陷进地里，物理把人往上顶半盒高 = 浮空
        capsule.offset = new Vector2(0f, capsule.size.y * 0.5f);

        if (go.GetComponent<Health>() == null) go.AddComponent<Health>();
        if (go.GetComponent<CharacterStats>() == null) go.AddComponent<CharacterStats>();
        if (go.GetComponent<HitReaction>() == null) go.AddComponent<HitReaction>();
        if (go.GetComponent<EnemyController>() == null) go.AddComponent<EnemyController>();

        // 私有字段只能走 SerializedObject
        var healthSo = new SerializedObject(go.GetComponent<Health>());
        healthSo.FindProperty("maxHealth").floatValue = maxHealth;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        var statsSo = new SerializedObject(go.GetComponent<CharacterStats>());
        statsSo.FindProperty("baseMaxHealth").floatValue = maxHealth;
        statsSo.FindProperty("baseAttack").floatValue = attack;
        statsSo.FindProperty("baseMoveSpeed").floatValue = moveSpeed;
        statsSo.FindProperty("baseDefense").floatValue = defense;
        statsSo.ApplyModifiedPropertiesWithoutUndo();

        var enemySo = new SerializedObject(go.GetComponent<EnemyController>());
        enemySo.FindProperty("config").objectReferenceValue = config;
        enemySo.FindProperty("playerLayer").intValue = 1 << playerLayer;
        enemySo.ApplyModifiedPropertiesWithoutUndo();
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
