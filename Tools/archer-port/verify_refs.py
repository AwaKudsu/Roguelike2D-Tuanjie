# -*- coding: utf-8 -*-
"""静态校验：把引用查干净。

三个检查：
  1. 场景里 `&<id>` 不重复（fileID 撞车会让两个物体互相顶掉）；
  2. 场景里**本文件内**的 `{fileID: N}`（同一对花括号里没有 guid）都能找到 `&N`；
  3. 新写/改过的文件里出现的 hex guid，必须能对上「已知真实 guid 全集」——
     这个全集由三部分拼起来：二号工程里以 hex 形式存在的 .meta、二号工程原有场景/资产里的引用、
     以及一号工程里射手相关的引用（美术与图标的 hex guid 只在那边出现过）。
"""
import os
import re
import sys

P2 = r"D:\zuoye\tuanjie\TuanjieRoguelike2D-2"
P1 = r"D:\zuoye\tuanjie\TuanjieRoguelike2D-1"

# 打补丁**之前**的场景快照。用途只有一个：脚本的 hex guid 几乎只在场景里出现
# （.meta 里存的是加密形式），所以必须留一份原始场景当「已知 guid」的取样源。
BACKUP_SCENE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "baseline-scene.unity")

NEW_OR_CHANGED = [
    "Assets/_Project/Scenes/Game.unity",
    "Assets/_Project/Data/classes/Class_Archer.asset",
    "Assets/_Project/Data/Weapons/Attack_Arrow.asset",
    "Assets/_Project/Data/Weapons/Attack_Arrow_Multi.asset",
    "Assets/_Project/Data/Weapons/Attack_Arrow_Pierce.asset",
    "Assets/_Project/Data/Weapons/Attack_Arrow_Rain.asset",
    "Assets/_Project/Data/Skills/Skill_MultiShot.asset",
    "Assets/_Project/Data/Skills/Skill_PiercingArrow.asset",
    "Assets/_Project/Data/Skills/Skill_ArrowRain.asset",
    "Assets/_Project/Data/Skills/Skill_HunterFocus.asset",
    "Assets/_Project/Prefabs/Arrow.prefab",
    "Assets/_Project/Prefabs/ArrowPiercing.prefab",
]

BUILTIN = {"0000000000000000e000000000000000", "0000000000000000f000000000000000"}
RE_FILEID = re.compile(r"fileID: (\d+)")
RE_BRACE = re.compile(r"\{[^{}]*\}")
RE_HEXGUID = re.compile(r"guid: ([0-9a-f]{32})")


def read(path):
    with open(path, encoding="utf-8", errors="ignore") as f:
        return f.read()


def walk_files(root, exts):
    for dirpath, _, files in os.walk(root):
        for name in files:
            if name.endswith(exts):
                yield os.path.join(dirpath, name)


def real_guids():
    """已知真实 hex guid 全集"""
    known = set()

    # 1) 以 hex 形式存在的 .meta
    for path in walk_files(os.path.join(P2, "Assets"), ".meta"):
        for line in read(path).splitlines():
            if line.startswith("guid:"):
                g = line.split(":", 1)[1].strip()
                if re.fullmatch(r"[0-9a-f]{32}", g):
                    known.add(g)
                break

    # 2) 包里的 .meta（Packages / PackageCache）
    for root in (os.path.join(P2, "Packages"), os.path.join(P2, "Library", "PackageCache")):
        if not os.path.isdir(root):
            continue
        for path in walk_files(root, ".meta"):
            for line in read(path).splitlines():
                if line.startswith("guid:"):
                    g = line.split(":", 1)[1].strip()
                    if re.fullmatch(r"[0-9a-f]{32}", g):
                        known.add(g)
                    break

    # 3) 二号工程原有序列化文件里的引用（含脚本的 hex guid —— 它们只以引用形式出现）
    originals = []
    if os.path.exists(BACKUP_SCENE):
        originals.append(BACKUP_SCENE)
    for path in walk_files(os.path.join(P2, "Assets", "_Project"), (".asset", ".prefab")):
        rel = os.path.relpath(path, P2).replace("\\", "/")
        if rel in NEW_OR_CHANGED:
            continue
        originals.append(path)
    for path in originals:
        known.update(RE_HEXGUID.findall(read(path)))

    # 4) 一号工程：射手美术 / 图标 / 技能的 hex guid 只在那边出现过
    for path in list(walk_files(os.path.join(P1, "Assets", "_Project"), (".asset", ".prefab"))) + [
            os.path.join(P1, "Assets", "_Project", "Scenes", "Game.unity")]:
        if os.path.exists(path):
            known.update(RE_HEXGUID.findall(read(path)))

    return known


def check_scene():
    scene = os.path.join(P2, "Assets", "_Project", "Scenes", "Game.unity")
    text = read(scene)

    declared = re.findall(r"^--- !u!\d+ &(\d+)", text, re.M)
    dup = sorted({i for i in declared if declared.count(i) > 1})

    ids = set(declared)
    local_missing = {}
    external_missing = {}

    for brace in RE_BRACE.findall(text):
        m = RE_FILEID.search(brace)
        if not m:
            continue
        fid = m.group(1)
        if fid == "0" or fid in ids:
            continue
        if "guid:" in brace:
            external_missing[fid] = external_missing.get(fid, 0) + 1
        else:
            local_missing[fid] = local_missing.get(fid, 0) + 1

    print("scene docs=%d unique=%d dup=%s" % (len(declared), len(set(declared)), dup or "none"))
    print("scene local refs missing=%d %s" % (len(local_missing), sorted(local_missing)[:8]))
    print("scene external refs (guid 指向别的文件，属正常) =%d 种" % len(external_missing))
    return not dup and not local_missing


def check_guids(known):
    bad = 0
    for rel in NEW_OR_CHANGED:
        path = os.path.join(P2, rel)
        if not os.path.exists(path):
            print("  MISSING FILE %s" % rel)
            bad += 1
            continue
        for g in set(RE_HEXGUID.findall(read(path))):
            if g in known or g in BUILTIN:
                continue
            print("  %s -> 未知 guid %s" % (rel, g))
            bad += 1
    print("new/changed files: 未知 guid=%d" % bad)
    return bad == 0


def check_internal_consistency():
    """新资产之间的引用必须是「我刚才写进去的那几个 guid」"""
    expect = {
        "Assets/_Project/Data/classes/Class_Archer.asset": [
            "e4a7e9184998aa04f811163fd03079a4",  # 自己
        ],
    }
    ok = True

    class_text = read(os.path.join(P2, "Assets/_Project/Data/classes/Class_Archer.asset"))
    meta = read(os.path.join(P2, "Assets/_Project/Data/classes/Class_Archer.asset.meta"))
    class_guid = re.search(r"guid: ([0-9a-f]{32})", meta).group(1)
    if class_guid != "e4a7e9184998aa04f811163fd03079a4":
        print("  Class_Archer 的 meta guid 不对：%s" % class_guid)
        ok = False

    # 职业 → 攻击 / 技能：必须能在对应资产的 meta 里找到同一个 guid
    want = {
        "dcaf8c8a2a1606e4abdd22acc5b82c8f": "Data/Weapons/Attack_Arrow.asset.meta",
        "79295a4b81da4254694f3cf8b9e7b21d": "Data/Skills/Skill_MultiShot.asset.meta",
        "645476992794c5345a34e012b5e8a84d": "Data/Skills/Skill_PiercingArrow.asset.meta",
        "151444d4fcbca224cbaa9b05dd1e8cc7": "Data/Skills/Skill_ArrowRain.asset.meta",
        "7a114ae563a68e94da8bfae533a3f798": "Data/Skills/Skill_HunterFocus.asset.meta",
    }
    for guid, rel in want.items():
        path = os.path.join(P2, "Assets", "_Project", rel)
        text = read(path)
        if guid not in text:
            print("  %s 的 meta 里没有 %s" % (rel, guid))
            ok = False
        if guid not in class_text:
            print("  Class_Archer 没有引用 %s" % guid)
            ok = False

    # 技能 → 攻击 & 预制体
    links = [
        ("Data/Skills/Skill_MultiShot.asset", ["46bb8e4962b567e40aaf5eaab6fcb6f2", "ea538a49d2cfc224895ad3ded7151526"]),
        ("Data/Skills/Skill_PiercingArrow.asset", ["dcfb8ccbc0026f94b8c568ae2c4e8154", "46acc0d185bc2f24c83d2f09c08e2263"]),
        ("Data/Skills/Skill_ArrowRain.asset", ["af5d6728741999d418292b6faa42b92c", "ea538a49d2cfc224895ad3ded7151526"]),
    ]
    for rel, guids in links:
        text = read(os.path.join(P2, "Assets", "_Project", rel))
        for g in guids:
            if g not in text:
                print("  %s 缺引用 %s" % (rel, g))
                ok = False

    # 攻击 → 箭预制体
    for rel, g in [("Data/Weapons/Attack_Arrow.asset", "ea538a49d2cfc224895ad3ded7151526"),
                   ("Data/Weapons/Attack_Arrow_Pierce.asset", "46acc0d185bc2f24c83d2f09c08e2263"),
                   ("Data/Weapons/Attack_Arrow_Rain.asset", "ea538a49d2cfc224895ad3ded7151526")]:
        if g not in read(os.path.join(P2, "Assets", "_Project", rel)):
            print("  %s 没有指向箭预制体 %s" % (rel, g))
            ok = False

    # 预制体的 meta guid 要和攻击资产引用的对得上
    for rel, g in [("Prefabs/Arrow.prefab.meta", "ea538a49d2cfc224895ad3ded7151526"),
                   ("Prefabs/ArrowPiercing.prefab.meta", "46acc0d185bc2f24c83d2f09c08e2263")]:
        if g not in read(os.path.join(P2, "Assets", "_Project", rel)):
            print("  %s 的 guid 不是 %s" % (rel, g))
            ok = False

    print("internal consistency=%s" % ok)
    return ok


def main():
    known = real_guids()
    print("已知真实 guid 总数=%d" % len(known))
    a = check_scene()
    b = check_guids(known)
    c = check_internal_consistency()
    print("RESULT scene=%s guids=%s links=%s" % (a, b, c))
    return 0 if (a and b and c) else 1


if __name__ == "__main__":
    sys.exit(main())
