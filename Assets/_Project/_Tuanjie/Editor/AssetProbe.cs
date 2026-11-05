#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tuanjie.PortCheck
{
    /// <summary>
    /// 一次性排查工具：print 出 sprite 的 guid 与预制体里的引用到底解不解析得出来。
    /// 只读，不写盘。
    /// </summary>
    public static class AssetProbe
    {
        const string MageDir = "Assets/_Project/Art/Characters/Mage";
        const string WarriorDir = "Assets/_Project/Art/Characters/Warrior";

        [MenuItem("团结/诊断资源引用")]
        public static void Run()
        {
            var sb = new StringBuilder();

            sb.AppendLine("===== 贴图的真实 GUID =====");
            foreach (string path in new[]
                     {
                         MageDir + "/Mage_Fireball.png",
                         MageDir + "/Mage_Idle.png",
                         WarriorDir + "/Warrior_Idle.png",
                         "Assets/_Project/Art/Placeholder/Square.png",
                         "Assets/_Project/Art/Placeholder/Circle.png",
                     })
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                sb.AppendLine($"{path}\n  guid={guid}  load={(sprite != null ? sprite.name : "★null")}");
            }

            sb.AppendLine("\n===== 预制体里的 sprite 引用 =====");
            foreach (string path in new[]
                     {
                         "Assets/_Project/Prefabs/Fireball.prefab",
                         "Assets/_Project/Prefabs/MageBolt.prefab",
                     })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var sr = go != null ? go.GetComponentInChildren<SpriteRenderer>() : null;
                sb.AppendLine($"{path}\n  sprite={(sr != null && sr.sprite != null ? sr.sprite.name : "★null")}  enabled={(sr != null && sr.enabled)}");
            }

            sb.AppendLine("\n===== 场景里玩家当前的贴图 =====");
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var sr = player.GetComponent<SpriteRenderer>();
                sb.AppendLine($"player sprite={(sr != null && sr.sprite != null ? sr.sprite.name : "★null")}  flipX={(sr != null && sr.flipX)}");
            }

            string text = sb.ToString();
            Debug.Log("[AssetProbe]\n" + text);
            System.IO.File.WriteAllText("_Tuanjie/asset-probe.txt", text, new UTF8Encoding(false));
        }
    }
}
#endif
