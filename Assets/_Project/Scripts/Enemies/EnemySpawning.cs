using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪物池：把 Resources/Enemies 下所有「怪物预制体」收集起来，交给房间随机刷。
///
/// ★ 为什么要走 Resources 而不是在预制体上拖引用：
///   房间预制体是「谁被抽到就摆哪一间」的模板，如果每间房都把六只怪的引用拖一遍，
///   加第七只怪就要回去改所有房间；而且团结引擎给新资产发的 GUID 是加密形态，
///   手写引用很容易写错。Resources.LoadAll 按文件夹收，扔进一只新怪就自动进池子。
///
/// ★ 为什么不用 UnityEngine.Random：
///   房间顺序是用 RoomManager 的种子算出来的（同一局能复现），
///   怪物如果走全局 Random，同一个种子跑两次出来的怪不一样，那个种子就白记了。
///   所以抽怪也要走外面传进来的 System.Random。
/// </summary>
public static class EnemySpawning
{
    /// <summary>Resources 下的怪物目录。Assets/…/Resources/Enemies 里的预制体都会被收进来</summary>
    public const string ResourceFolder = "Enemies";

    private static GameObject[] _pool;

    /// <summary>池子里的怪物预制体（按名字排序，保证同一个种子抽出来的顺序一致）</summary>
    public static GameObject[] Pool
    {
        get
        {
            if (_pool == null) Load();
            return _pool;
        }
    }

    /// <summary>池子里有几只怪</summary>
    public static int Count => Pool.Length;

    private static void Load()
    {
        GameObject[] loaded = Resources.LoadAll<GameObject>(ResourceFolder);
        var list = new List<GameObject>(loaded != null ? loaded.Length : 0);

        if (loaded != null)
        {
            foreach (GameObject go in loaded)
            {
                if (go == null) continue;
                // 只收「真怪物」：没有 EnemyController 的（箭、火球、装饰）不算
                if (go.GetComponent<EnemyController>() == null) continue;
                list.Add(go);
            }
        }

        // 排序：Resources.LoadAll 的顺序没有保证，按名字排一下才能复现
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        _pool = list.ToArray();

        if (_pool.Length == 0)
        {
            Debug.LogWarning(
                "[EnemySpawning] Resources/" + ResourceFolder + " 里没有找到怪物预制体 —— " +
                "房间里的怪会退回占位方块。检查预制体是不是放在 Assets/_Project/Resources/" + ResourceFolder + " 下。");
        }
    }

    /// <summary>打乱一份下标数组（Fisher-Yates）。同一间房里优先把六只怪轮一遍再重复</summary>
    public static int[] ShuffledOrder(System.Random rng)
    {
        int count = Count;
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        if (rng == null) return order;

        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    /// <summary>在指定位置生成一只怪，并挂到 parent 底下（这样换房间时会被一起销毁）</summary>
    public static GameObject Spawn(GameObject prefab, Vector3 position, Transform parent)
    {
        if (prefab == null)
        {
            // 池子是在第一次用到时缓存下来的，工程里删了预制体的话这里会拿到 null
            Debug.LogWarning("[EnemySpawning] 池子里有空的怪物预制体，这一只跳过。");
            return null;
        }

        GameObject go = Object.Instantiate(prefab, position, Quaternion.identity, parent);
        go.name = prefab.name;      // 去掉 (Clone)，Hierarchy 里一眼看出刷了谁
        return go;
    }
}
