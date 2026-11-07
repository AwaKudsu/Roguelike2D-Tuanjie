using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 菜单「团结 → 同步大厅朝向图」。
///
/// ★ 它解决的是「图有了，但要一张张拖进 Inspector」这件事：
///   按命名约定扫 <c>Art/Characters/&lt;职业&gt;/&lt;职业&gt;_Front.png</c>（以及 _Back / _Side），
///   把 Sprite 写进对应 ClassData 的 lobbyFront / lobbyBack / lobbySide。
///
/// ★ 文件不存在就跳过 —— 不会把已经手填好的图清空，所以可以边画边同步。
///   一张都没有时，大厅里的表现和以前完全一样（只按侧视翻转）。
/// </summary>
public static class LobbyFacingBuilder
{
    private const string ClassDir = "Assets/_Project/Data/classes";
    private const string ArtRoot = "Assets/_Project/Art/Characters";

    [MenuItem("团结/同步大厅朝向图")]
    public static void SyncAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:ClassData", new[] { ClassDir });
        if (guids.Length == 0)
        {
            Debug.LogWarning("[LobbyFacing] " + ClassDir + " 下没找到 ClassData。");
            return;
        }

        int touchedClasses = 0;
        int touchedFields = 0;

        foreach (string guid in guids)
        {
            string classPath = AssetDatabase.GUIDToAssetPath(guid);
            ClassData data = AssetDatabase.LoadAssetAtPath<ClassData>(classPath);
            if (data == null) continue;

            // Class_Warrior ⟶ Warrior，图和资产用同一个词，省得维护第二张对照表
            string key = Path.GetFileNameWithoutExtension(classPath);
            if (key.StartsWith("Class_")) key = key.Substring("Class_".Length);

            Sprite front = LoadFacing(key, "Front");
            Sprite back = LoadFacing(key, "Back");
            Sprite side = LoadFacing(key, "Side");

            if (front == null && back == null && side == null)
            {
                Debug.Log("[LobbyFacing] " + key + "：还没有 _Front / _Back / _Side 图，跳过（大厅里继续用侧视）。");
                continue;
            }

            SerializedObject so = new SerializedObject(data);
            int changed = 0;
            changed += Assign(so, "lobbyFront", front);
            changed += Assign(so, "lobbyBack", back);
            changed += Assign(so, "lobbySide", side);

            if (changed > 0)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                touchedClasses++;
                touchedFields += changed;
            }

            Debug.Log("[LobbyFacing] " + key + "：正面 " + Describe(front) + "、背面 " + Describe(back)
                      + "、侧视 " + Describe(side) + (changed > 0 ? "（已写入）" : "（本来就是这些，没改）"));
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[LobbyFacing] 完成：" + touchedClasses + " 个职业、共 " + touchedFields + " 处更新。");
    }

    private static Sprite LoadFacing(string classKey, string suffix)
    {
        string path = ArtRoot + "/" + classKey + "/" + classKey + "_" + suffix + ".png";
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null && File.Exists(path) == false) return null;

        if (sprite == null)
            Debug.LogWarning("[LobbyFacing] " + path + " 存在但没导入成 Sprite（Texture Type 要设成 Sprite (2D and UI)）。");

        return sprite;
    }

    private static int Assign(SerializedObject so, string fieldName, Sprite sprite)
    {
        if (sprite == null) return 0;

        SerializedProperty property = so.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogWarning("[LobbyFacing] ClassData 上没有 " + fieldName + " 字段，脚本是不是没编译完？");
            return 0;
        }

        if (property.objectReferenceValue == sprite) return 0;

        property.objectReferenceValue = sprite;
        return 1;
    }

    private static string Describe(Sprite sprite) => sprite != null ? sprite.name : "（空，回退侧视）";
}
