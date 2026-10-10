using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 房间流水线：一间的敌人清光了、玩家进了门，就淡出 → 换下一间 → 淡入。
///
/// 它只管「现在该是第几间、下一间长什么样」，不管房间里有什么。
/// 将来关卡的难度曲线（第 5 间开始出精英怪、每 5 间一个 Boss）也挂在这里。
///
/// 换房间的完整顺序（这个顺序不能乱）：
///   1. 淡出到全黑        ← 玩家此时什么都看不见
///   2. 销毁旧房间
///   3. 实例化新房间
///   4. 把玩家瞬移到新房间的出生点，并清零速度
///   5. 淡入
/// 第 4 步的清速度很容易忘：不清的话玩家会带着「进门那一下的冲刺惯性」
/// 直接飞出出生点，看起来像被弹射出去。
/// </summary>
public class RoomManager : MonoBehaviour
{
    [Header("房间池")]
    [Tooltip("从这些预制体里抽。至少放 1 个，每个预制体都要有 Room 组件")]
    [SerializeField] private List<GameObject> roomPrefabs = new();

    [Tooltip("房间生成的位置。留空则用本物体的位置")]
    [SerializeField] private Transform roomAnchor;

    [Header("过渡")]
    [Tooltip("黑幕。留空则换房间时不会有淡入淡出（会很突兀）")]
    [SerializeField] private ScreenFader fader;

    [Header("规则")]
    [Tooltip("勾上则用「袋子抽取」：一轮之内每个房间各出现一次，抽完再重新装袋。" +
             "比纯随机体感好得多 —— 纯随机经常连着抽到同一间，玩家会以为游戏坏了")]
    [SerializeField] private bool useBagRandom = true;

    [Tooltip("开局自动生成第 1 间。关掉的话场景里得先摆好一间带 Room 组件的房间")]
    [SerializeField] private bool buildFirstRoomOnStart = true;

    [Tooltip("换房间时清掉场上飞着的投射物。不清的话上一间的火球会飞进新房间")]
    [SerializeField] private bool cleanupProjectiles = true;

    /// <summary>已经进入过的房间数（第 1 间生出来时就是 1）</summary>
    public int RoomsCleared { get; private set; }

    /// <summary>每换一间触发一次，参数是新的 RoomsCleared。将来的难度曲线、HUD 计数都订阅它</summary>
    public event Action<int> RoomChanged;

    /// <summary>当前房间。还没生成时为 null</summary>
    public Room CurrentRoom => _currentRoom;

    /// <summary>正在换房间的过渡中。此时不应该再触发一次</summary>
    public bool IsSwitching => _switching;

    private GameObject _currentInstance;
    private Room _currentRoom;
    private Transform _player;
    private Rigidbody2D _playerBody;
    private bool _switching;

    // 袋子随机的状态：_bag 是「还没抽到的房间下标」，抽一个少一个，空了重新装袋
    private readonly List<int> _bag = new();
    private int _lastIndex = -1;

    private Vector3 AnchorPosition => roomAnchor != null ? roomAnchor.position : transform.position;

    private void Start()
    {
        ResolvePlayer();

        if (buildFirstRoomOnStart)
        {
            if (roomPrefabs.Count == 0)
            {
                Debug.LogWarning(
                    "[RoomManager] 房间池是空的，一间都生不出来。" +
                    "请至少把一个房间预制体拖进 Room Prefabs。", this);
                return;
            }

            BuildRoom(PickIndex());
        }
        else if (_currentRoom == null)
        {
            // 场景里已经手摆了一间，把它认领过来，这样它也能触发换房间
            _currentRoom = FindObjectOfType<Room>();
            if (_currentRoom != null) _currentRoom.Initialize(this);
        }
    }

    private void ResolvePlayer()
    {
        if (_player != null) return;

        var go = GameObject.FindGameObjectWithTag("Player");
        if (go == null)
        {
            Debug.LogWarning("[RoomManager] 找不到 Tag 为 Player 的对象，换房间时没法传送玩家。", this);
            return;
        }

        _player = go.transform;
        _playerBody = go.GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// 换下一间。由 Door 调用。
    /// 过渡途中重复调用会被忽略 —— 否则玩家站在门口不动就会连开好几个协程。
    /// </summary>
    public void Advance()
    {
        if (_switching) return;

        if (roomPrefabs.Count == 0)
        {
            Debug.LogWarning("[RoomManager] 房间池是空的，Advance 被忽略了。", this);
            return;
        }

        StartCoroutine(AdvanceRoutine());
    }

    private IEnumerator AdvanceRoutine()
    {
        _switching = true;

        if (fader != null) yield return fader.FadeOut();

        BuildRoom(PickIndex());

        if (fader != null) yield return fader.FadeIn();

        _switching = false;
    }

    private void BuildRoom(int index)
    {
        if (index < 0 || index >= roomPrefabs.Count) return;

        if (cleanupProjectiles) CleanupProjectiles();

        // 先销毁旧的：地刺、残留的敌人、掉在地上的东西一起清掉。
        // 玩家不是房间的子物体，所以不会被一起销毁 —— 这是刻意的
        if (_currentInstance != null) Destroy(_currentInstance);

        GameObject prefab = roomPrefabs[index];
        _currentInstance = Instantiate(prefab, AnchorPosition, Quaternion.identity);
        _currentInstance.name = prefab.name;   // 去掉名字后面的 (Clone)，Hierarchy 里好认

        _currentRoom = _currentInstance.GetComponent<Room>();
        if (_currentRoom == null)
        {
            Debug.LogWarning(
                $"[RoomManager] 预制体「{prefab.name}」上没有 Room 组件，" +
                "出生点和门都不会生效，玩家会被留在原地。", this);
        }
        else
        {
            _currentRoom.Initialize(this);
        }

        MovePlayerToSpawn();

        RoomsCleared++;
        RoomChanged?.Invoke(RoomsCleared);
    }

    private void MovePlayerToSpawn()
    {
        ResolvePlayer();
        if (_player == null) return;

        Vector3 target = _currentRoom != null ? _currentRoom.SpawnPoint.position : AnchorPosition;

        // 有刚体时必须以刚体的坐标为准：只改 transform.position 的话，
        // 物理系统下一帧会把物体拉回它自己记录的位置，表现为「传送失败」
        if (_playerBody != null)
        {
            _playerBody.position = target;
            _playerBody.velocity = Vector2.zero;
        }

        _player.position = target;
    }

    /// <summary>
    /// 投射物不是房间的子物体，销毁房间带不走它们。
    /// 不清的话会出现「上一间的火球飞进新房间把刚出生的玩家打死」这种荒唐事。
    /// </summary>
    private void CleanupProjectiles()
    {
        var flying = FindObjectsOfType<Projectile>();
        for (int i = 0; i < flying.Length; i++)
        {
            if (flying[i] != null) Destroy(flying[i].gameObject);
        }
    }

    /// <summary>抽一间。袋子随机保证一轮之内不重复</summary>
    private int PickIndex()
    {
        int count = roomPrefabs.Count;
        if (count == 0) return -1;

        // 只有一间房，或者关掉了袋子随机 —— 那就退化成纯随机
        if (!useBagRandom || count == 1) return UnityEngine.Random.Range(0, count);

        if (_bag.Count == 0) RefillBag();

        int pick = _bag[0];
        _bag.RemoveAt(0);
        _lastIndex = pick;
        return pick;
    }

    private void RefillBag()
    {
        int count = roomPrefabs.Count;

        _bag.Clear();
        for (int i = 0; i < count; i++) _bag.Add(i);

        // Fisher-Yates 洗牌。用它而不是「每次随机挑一个」是因为这样每个房间
        // 在一轮里出现且仅出现一次，玩家的体验是「内容很丰富」而不是「怎么又是这间」
        for (int i = _bag.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
        }

        // 洗完之后如果队首恰好是上一轮的最后一间，就和队尾换一下。
        // 不换的话会出现「连着两轮抽到同一间」——袋子随机唯一的破绽
        if (count > 1 && _bag[0] == _lastIndex)
        {
            (_bag[0], _bag[count - 1]) = (_bag[count - 1], _bag[0]);
        }
    }

    private void OnValidate()
    {
        if (roomPrefabs == null) return;

        for (int i = 0; i < roomPrefabs.Count; i++)
        {
            if (roomPrefabs[i] == null)
            {
                Debug.LogWarning($"[RoomManager] 房间池第 {i + 1} 格是空的，抽到它会什么都不生成。", this);
            }
        }
    }
}
