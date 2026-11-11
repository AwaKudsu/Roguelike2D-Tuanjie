# -*- coding: utf-8 -*-
"""刷怪安全区的静态自检（不启动编辑器）。

为什么需要它：门锁着的时候是一面实心墙。怪只要落在门的外侧，近战就永远打不到，
房间清不完、门不开 —— 整局卡死。这个脚本把「用坐标算出来的结论」固化下来：

  1. 两个房间的 Room 组件都写了 doorClearance / keepEnemiesInside / insideCheckInterval；
  2. 每个刷怪点、以及「散开 ±scatterX」之后的最远位置，都在门的安全线以内；
  3. Room.cs 里刷怪位置过了 ClampInside，并且有「门锁着就把门后的怪拉回来」的兜底。

用法：
    python verify_spawn_zones.py            # 全部 PASS 退出码 0
    python verify_spawn_zones.py --verbose  # 连每个点的坐标一起打

房间预制体换了坐标、或者有人改了 Room 的刷怪逻辑，跑一次就知道有没有破。
"""
import argparse
import re
import sys

PROJ = r"D:\zuoye\tuanjie\project\TuanjieRoguelike2D-2\Assets"
ROOMS = PROJ + r"\_Project\Prefabs\Rooms"
ROOM_CS = PROJ + r"\_Project\Scripts\Roguelike\Room.cs"

fail = 0
ok = 0
verbose = False


def check(cond, label, detail=""):
    global fail, ok
    if cond:
        ok += 1
    else:
        fail += 1
    line = ("[PASS] " if cond else "[FAIL] ") + label
    if detail and (verbose or not cond):
        line += " | " + detail
    print(line)


def parse_room(path):
    text = open(path, encoding="utf-8").read()
    parts = re.split(r"^(--- !u!\d+ &-?\d+.*)$", text, flags=re.M)
    docs, i = [], 1
    while i < len(parts):
        docs.append((parts[i], parts[i + 1] if i + 1 < len(parts) else ""))
        i += 2

    go, tf = {}, {}
    for header, body in docs:
        m = re.match(r"^--- !u!(\d+) &(-?\d+)", header)
        cls, fid = int(m.group(1)), m.group(2)
        if cls == 1:
            nm = re.search(r"^  m_Name: (.*)$", body, flags=re.M)
            go[fid] = nm.group(1).strip() if nm else ""
        elif cls == 4:
            pos = re.search(r"m_LocalPosition: \{x: ([\d.-]+), y: ([\d.-]+)", body)
            goid = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            tf[goid.group(1) if goid else "?"] = (
                (float(pos.group(1)), float(pos.group(2))) if pos else (0, 0))
    return text, {name: tf.get(fid, (0, 0)) for fid, name in go.items()}


def main():
    global verbose
    ap = argparse.ArgumentParser()
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()
    verbose = args.verbose

    for room in ("Room_01.prefab", "Room_02.prefab"):
        path = ROOMS + "\\" + room
        text, positions = parse_room(path)

        for field in ("doorClearance:", "keepEnemiesInside:", "insideCheckInterval:"):
            check(field in text, f"{room} 有字段 {field[:-1]}")

        clearance = float(re.search(r"doorClearance: ([\d.]+)", text).group(1))
        scatter = float(re.search(r"scatterX: ([\d.]+)", text).group(1))

        markers = sorted(p for n, p in positions.items() if n.startswith("EnemySpawn_"))
        door = positions.get("Door")
        spawn = positions.get("SpawnPoint", (0, 0))

        check(door is not None, f"{room} 找得到门")
        check(len(markers) > 0, f"{room} 有刷怪点", f"{len(markers)} 个")
        if not door or not markers:
            continue

        side = 1 if door[0] > spawn[0] else -1
        limit = door[0] - side * clearance
        for i, m in enumerate(markers):
            inside = (m[0] <= limit) if side > 0 else (m[0] >= limit)
            check(inside, f"{room} 刷怪点 {i} 在安全线以内",
                  f"marker.x={m[0]:.2f}, 安全线={limit:.2f}, 门.x={door[0]:.2f}")

        worst = max((m[0] + scatter) if side > 0 else (m[0] - scatter) for m in markers)
        check((worst <= limit) if side > 0 else (worst >= limit),
              f"{room} 散开 ±{scatter} 之后仍在安全线以内",
              f"最远={worst:.2f}, 安全线={limit:.2f}")

    code = open(ROOM_CS, encoding="utf-8").read()
    check(code.count("ClampInside(") >= 3, "Room.cs 用 ClampInside 收刷怪位置")
    check("private int DoorSide" in code, "Room.cs 有 DoorSide 判定")
    check("door.IsUnlocked" in code and "behindDoor" in code, "Room.cs 有「门锁着才拉回」的兜底")
    check("body.position = rescued" in code, "拉回时同步刚体坐标（否则会被物理拉回）")
    check("insideCheckInterval" in code, "兜底检查有可调间隔")

    print()
    print(f"[SUMMARY] PASS {ok} / FAIL {fail}")
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
