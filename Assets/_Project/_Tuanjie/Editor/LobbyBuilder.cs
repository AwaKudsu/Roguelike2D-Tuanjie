#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 给两个职业补上大厅需要的展示信息（头像 + 一句话定位）。
    ///
    /// 大厅本身是运行时用代码搭的，场景里不需要摆任何东西 ——
    /// 这个菜单只负责把「职业资产上的空白字段」填上，让卡片有图有话说。
    /// </summary>
    public static class LobbyBuilder
    {
        private const string WarriorPath = "Assets/_Project/Data/classes/Class_Warrior.asset";
        private const string MagePath    = "Assets/_Project/Data/classes/Class_Mage.asset";
        private const string WarriorIcon = "Assets/_Project/Art/Characters/Warrior/Warrior_Idle.png";
        private const string MageIcon    = "Assets/_Project/Art/Characters/Mage/Mage_Idle.png";
        private const string ReportPath  = "_Tuanjie/lobby-report.txt";

        [MenuItem("团结/生成等待大厅")]
        public static void Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[INFO] 填充职业展示信息");

            bool ok = true;
            ok &= Fill(WarriorPath, WarriorIcon,
                "近身缠斗：三段连击（劈砍 → 上挑 → 突刺），旋风斩持续扫地，战吼把敌人吓跑。血厚能抗，适合正面推。",
                sb);

            ok &= Fill(MagePath, MageIcon,
                "远程法术：火球可以按住蓄力，越大越快越疼；按 L 展开回复法阵，站在里面持续回血。血薄，靠距离活命。",
                sb);

            sb.AppendLine(ok ? "[SUMMARY] 失败 | 0" : "[SUMMARY] 失败 | 1");

            System.IO.Directory.CreateDirectory("_Tuanjie");
            System.IO.File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[LobbyBuilder]\n" + sb);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("等待大厅", ok ? "职业展示信息已填好" : "有职业资产没找到，看报告", "好");
            }
        }

        private static bool Fill(string classPath, string iconPath, string description, StringBuilder sb)
        {
            var data = AssetDatabase.LoadAssetAtPath<ClassData>(classPath);
            if (data == null)
            {
                sb.AppendLine($"[FAIL] 找不到职业资产 | {classPath}");
                return false;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (sprite == null)
            {
                sb.AppendLine($"[FAIL] 找不到头像 | {iconPath}");
                return false;
            }

            var so = new SerializedObject(data);
            so.FindProperty("icon").objectReferenceValue = sprite;
            so.FindProperty("description").stringValue = description;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            sb.AppendLine($"[PASS] {data.className} | 头像 {sprite.name} / 说明 {description.Length} 字");
            return true;
        }
    }
}
#endif
