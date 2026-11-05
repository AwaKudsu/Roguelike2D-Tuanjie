#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 把战士的普通攻击改成「三段连击」：劈砍 → 上挑 → 突刺。
    ///
    /// 起因：场景里 PlayerAttacker 的攻击数据一直是手填的法师弹道（Attack_MageBolt），
    /// 而 ClassData.basicAttack 这个字段根本没人读 —— 所以骑士挥剑其实在放法术弹。
    /// 这里一并做掉三件事：
    ///   1. 生成三段各自的 AttackData（判定框、倍率、击退都不同）
    ///   2. 把玩家的普通攻击改成职业模板（战士 = 近战剑），并把三段接上
    ///   3. 把「上挑」的姿势帧接给 WarriorVisual
    /// </summary>
    public static class CombatBuilder
    {
        private const string ScenePath   = "Assets/_Project/Scenes/Game.unity";
        private const string WeaponsDir  = "Assets/_Project/Data/Weapons";
        private const string ArtDir      = "Assets/_Project/Art/Characters/Warrior";
        private const string ClassPath   = "Assets/_Project/Data/classes/Class_Warrior.asset";
        private const string ReportPath  = "_Tuanjie/combo-report.txt";

        private const string Combo1Path = WeaponsDir + "/Attack_Combo1.asset";
        private const string Combo2Path = WeaponsDir + "/Attack_Combo2.asset";
        private const string Combo3Path = WeaponsDir + "/Attack_Combo3.asset";

        // 敌人都在第 7 层（Attack_Basic / Attack_Enemy 用的都是 128）
        private const int EnemyLayerBits = 128;

        [MenuItem("团结/生成三段连击")]
        public static void Build()
        {
            var sb = new StringBuilder();
            int fail = 0;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("[INFO] 已打开场景 | " + ScenePath);

            // ---------- 1. 三段 AttackData ----------
            AttackData combo1 = MakeAttack(Combo1Path, "劈砍", 1.0f, 6f, new Vector2(0.85f, 0.05f), new Vector2(1.4f, 1.0f), 0.16f, sb);
            AttackData combo2 = MakeAttack(Combo2Path, "上挑", 1.25f, 4f, new Vector2(0.80f, 0.35f), new Vector2(1.2f, 1.7f), 0.18f, sb);
            AttackData combo3 = MakeAttack(Combo3Path, "突刺", 1.7f, 10f, new Vector2(1.20f, 0.00f), new Vector2(2.2f, 0.9f), 0.34f, sb);

            if (combo1 == null || combo2 == null || combo3 == null) fail++;
            else sb.AppendLine("[PASS] 三段 AttackData | 劈砍 / 上挑 / 突刺");

            // ---------- 2. 找到玩家 ----------
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                sb.AppendLine("[FAIL] 找不到 Tag=Player 的物体");
                Write(sb, 1);
                return;
            }

            var attacker = player.GetComponent<PlayerAttacker>();
            var stats    = player.GetComponent<CharacterStats>();
            var visual   = player.GetComponent<WarriorVisual>();

            if (attacker == null || stats == null)
            {
                sb.AppendLine("[FAIL] 玩家身上缺 PlayerAttacker / CharacterStats");
                Write(sb, 1);
                return;
            }

            // ---------- 3. 职业模板 → 普通攻击 ----------
            var warriorClass = AssetDatabase.LoadAssetAtPath<ClassData>(ClassPath);
            if (warriorClass == null || warriorClass.basicAttack == null)
            {
                sb.AppendLine("[FAIL] 读不到 Class_Warrior 或它的 basicAttack");
                fail++;
            }
            else
            {
                var statsSo = new SerializedObject(stats);
                SerializedProperty classProp = statsSo.FindProperty("classData");
                string before = classProp.objectReferenceValue != null
                    ? classProp.objectReferenceValue.name
                    : "空";

                if (classProp.objectReferenceValue != warriorClass)
                {
                    classProp.objectReferenceValue = warriorClass;
                    statsSo.ApplyModifiedPropertiesWithoutUndo();
                }

                sb.AppendLine($"[INFO] 玩家职业 | {before} → {warriorClass.className}");
                sb.AppendLine($"[PASS] 职业普通攻击模板 | {warriorClass.basicAttack.name}（远程={warriorClass.basicAttack.isRanged}）");

                var atkSo = new SerializedObject(attacker);
                SerializedProperty atkProp = atkSo.FindProperty("attackData");
                string oldAtk = atkProp.objectReferenceValue != null ? atkProp.objectReferenceValue.name : "空";

                atkProp.objectReferenceValue = warriorClass.basicAttack;

                // ---------- 4. 三段接上 ----------
                SerializedProperty comboArr = atkSo.FindProperty("comboAttacks");
                comboArr.arraySize = 3;
                comboArr.GetArrayElementAtIndex(0).objectReferenceValue = combo1;
                comboArr.GetArrayElementAtIndex(1).objectReferenceValue = combo2;
                comboArr.GetArrayElementAtIndex(2).objectReferenceValue = combo3;

                SerializedProperty window = atkSo.FindProperty("comboWindow");
                if (window != null && window.floatValue <= 0f) window.floatValue = 0.55f;

                atkSo.ApplyModifiedPropertiesWithoutUndo();

                sb.AppendLine($"[PASS] 普通攻击已换成职业模板 | {oldAtk} → {warriorClass.basicAttack.name}");
                sb.AppendLine("[PASS] 三段连击已接 | 0=劈砍 1=上挑 2=突刺");
            }

            // ---------- 5. 上挑姿势帧 ----------
            if (visual != null)
            {
                var lift = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Warrior_AttackLift.png");
                if (lift == null)
                {
                    sb.AppendLine("[FAIL] 读不到 Warrior_AttackLift.png");
                    fail++;
                }
                else
                {
                    var visSo = new SerializedObject(visual);
                    SerializedProperty liftProp = visSo.FindProperty("attackLift");
                    if (liftProp == null)
                    {
                        sb.AppendLine("[FAIL] WarriorVisual 上没有 attackLift 字段");
                        fail++;
                    }
                    else
                    {
                        liftProp.objectReferenceValue = lift;
                        visSo.ApplyModifiedPropertiesWithoutUndo();
                        sb.AppendLine("[PASS] 上挑姿势已接 | Warrior_AttackLift");
                    }
                }
            }
            else
            {
                sb.AppendLine("[WARN] 玩家身上没有 WarriorVisual，跳过姿势接线");
            }

            // ---------- 6. 存盘 ----------
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            bool saved = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            sb.AppendLine(saved ? "[PASS] 场景已存盘" : "[FAIL] 存盘失败");
            if (!saved) fail++;

            Write(sb, fail);
        }

        private static AttackData MakeAttack(string path, string displayName, float multiplier,
                                             float knockback, Vector2 offset, Vector2 size,
                                             float cooldown, StringBuilder sb)
        {
            var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
            bool isNew = data == null;

            if (isNew)
            {
                data = ScriptableObject.CreateInstance<AttackData>();
                AssetDatabase.CreateAsset(data, path);
            }

            data.displayName      = displayName;
            data.damageMultiplier = multiplier;
            data.knockbackForce   = knockback;
            data.hitboxOffset     = offset;
            data.hitboxSize       = size;
            data.cooldown         = cooldown;
            data.targetLayers     = EnemyLayerBits;
            data.isRanged         = false;

            EditorUtility.SetDirty(data);

            sb.AppendLine($"[{(isNew ? "INFO" : "INFO")}] {(isNew ? "新建" : "更新")}攻击数据 | {path} " +
                          $"判定 {size.x}×{size.y} 偏移 {offset.x}/{offset.y} 倍率 {multiplier} 冷却 {cooldown}");

            return data;
        }

        private static void Write(StringBuilder sb, int fail)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            sb.AppendLine("[SUMMARY] 失败 | " + fail);

            System.IO.Directory.CreateDirectory("_Tuanjie");
            System.IO.File.WriteAllText(ReportPath, sb.ToString(), new System.Text.UTF8Encoding(false));
            Debug.Log("[CombatBuilder] 报告写入 " + ReportPath);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("三段连击",
                    fail == 0 ? "劈砍 / 上挑 / 突刺 已接好，普通攻击改成战士的剑了。" : $"有 {fail} 项失败，看 {ReportPath}",
                    "知道了");
            }
        }
    }
}
#endif
