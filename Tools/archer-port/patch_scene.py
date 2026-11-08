# -*- coding: utf-8 -*-
"""给二号工程的 Game.unity 打「射手」补丁。

做三件事：
  1. 给 Player 挂一个 ArcherVisual 组件（帧引用按一号工程里同一份组件的 guid 接好）；
  2. 让 PlayerVisualSwitcher 认识射手（archer 字段 + 职业表追加射手）；
  3. 把大厅的 Pedestal_Mage 复制成 Pedestal_Archer，并把三座台重新摆位。

为什么用脚本而不是手改：场景是 6900 行 YAML，所有引用都是 fileID 数字。
复制一棵子树要同时改 GameObject / Transform / 组件 / 父子链，漏一个就是「Missing」。
"""
import re
import sys

SCENE = r"D:\zuoye\tuanjie\TuanjieRoguelike2D-2\Assets\_Project\Scenes\Game.unity"

GUID_ARCHER_VISUAL = "6e8b5482df753b84587f141635f88f23"
GUID_SWITCHER = "e81e0aea9d46ac94db0c8f7a2278787d"
GUID_WARRIOR_VISUAL = "4cfa8105e18e45b4c956acd65fdaa7e4"
GUID_CLASS_ARCHER = "e4a7e9184998aa04f811163fd03079a4"
GUID_CLASS_MAGE = "b5fc4f381e18e3d4a9f3535e0119b2be"
GUID_SPRITE_MAGE_FRONT = "af8ec8bfe8f746c41a6d9338e547afdb"

A_IDLE = "3cc98eb39d459394fa11541c6415e4ef"
A_IDLE2 = "a12abccfcab0e0442967bcd6dd010e05"
A_IDLE3 = "15fde27ebd60c644fb234e6874725568"
A_WALK = ["f9780690957a06b4397416ccb608e8fa", "b7e5d9e1b782c8c40806db6d120d2bae",
          "6c89e8bfc3c7f954f9faa576d4abc559", "82287c6586587db479c86a8a669a1a17"]
A_SHOOT = ["aa792af16a42fdf4db8eb9ba6fddc921", "c3cabd83be262ba4dadb0ee75ba7e36e",
           "cdd4b91c3c954144095c5d10e6c2569f"]
A_JUMP = "97d40e4aa179e544684898e71b8f7a88"
A_FALL = "ad3a9d6f8065bba49a7cb7148131413d"
A_DASH = "1786d815c215b9443a12c06c58105097"
A_HURT = "58071d0b76c0ad84c8fa629a1a876cec"
A_DEATH = "b96c2b8d2d50d5d4a99655ae5c3c7d32"
A_VICTORY = "3643d4118bf44dc40971f6d204d67ed4"

NEW_ARCHER_VISUAL_ID = 1728500190
PEDESTAL_OFFSET = 3000000

PEDESTAL_X = {"Pedestal_Warrior": 4.6, "Pedestal_Archer": 10.0, "Pedestal_Mage": 15.4}
PEDESTAL_Y = 6.3


# ---------------------------------------------------------------- 基础工具
def sprite_ref(guid):
    return "{fileID: 21300000, guid: %s, type: 3}" % guid


def doc_header_id(header):
    m = re.match(r"--- !u!(\d+) &(\d+)", header)
    if not m:
        raise SystemExit("无法解析文档头：" + header)
    return m.group(1), m.group(2)


class Doc:
    def __init__(self, header, body):
        self.header = header
        self.body = body          # list[str]

    @property
    def cls(self):
        return doc_header_id(self.header)[0]

    @property
    def id(self):
        return doc_header_id(self.header)[1]

    def field(self, name, default=None):
        for line in self.body:
            s = line.strip()
            if s.startswith(name + ":"):
                return s[len(name) + 1:].strip()
        return default

    def replace_line(self, name, new_line):
        for i, line in enumerate(self.body):
            if line.strip().startswith(name + ":"):
                self.body[i] = new_line
                return True
        return False

    def insert_after(self, name, new_line):
        for i, line in enumerate(self.body):
            if line.strip().startswith(name + ":"):
                self.body.insert(i + 1, new_line)
                return True
        return False

    def list_field(self, name):
        """读 `name:` 下面那串 `- component: {fileID: N}` / `- {fileID: N}`"""
        out = []
        active = False
        for line in self.body:
            s = line.strip()
            if s.startswith(name + ":"):
                active = True
                continue
            if not active:
                continue
            if s.startswith("- "):
                m = re.search(r"fileID: (\d+)", s)
                if m:
                    out.append(m.group(1))
                continue
            break
        return out

    def text(self):
        return "\n".join([self.header] + self.body)


def load_scene(path):
    with open(path, encoding="utf-8") as f:
        lines = f.read().split("\n")

    first = next(i for i, l in enumerate(lines) if l.startswith("--- "))
    prefix = lines[:first]

    docs = []
    cur = None
    for line in lines[first:]:
        if line.startswith("--- "):
            if cur is not None:
                docs.append(cur)
            cur = Doc(line, [])
        else:
            cur.body.append(line)
    docs.append(cur)
    return prefix, docs


def save_scene(path, prefix, docs):
    chunks = list(prefix)
    for d in docs:
        chunks.extend(d.text().split("\n"))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(chunks))


# ---------------------------------------------------------------- 场景查询
def index_by_id(docs):
    return {d.id: d for d in docs}


def go_by_name(docs, name):
    for d in docs:
        if d.cls == "1" and d.field("m_Name", "") == name:
            return d
    return None


def script_guid(doc):
    raw = doc.field("m_Script", "")
    m = re.search(r"guid: ([0-9a-zA-Z+/=]{20,})", raw)
    return m.group(1) if m else None


def component_ids(go_doc):
    return go_doc.list_field("m_Component")


def child_transform_ids(transform_doc):
    return transform_doc.list_field("m_Children")


def collect_subtree(by_id, root_go_id):
    """返回子树里所有文档 id：GameObject + 它挂的所有组件 + 子物体的递归。

    注意：`m_Children` 里存的是**子物体的 Transform**，不能当 GameObject 查；
    必须顺着 Transform 的 `m_GameObject` 绕回它自己的 GO，再展开那层组件。
    """
    seen = set()
    stack = [("go", str(root_go_id))]

    while stack:
        kind, node = stack.pop()
        if node in seen:
            continue
        seen.add(node)

        doc = by_id.get(node)
        if doc is None:
            raise SystemExit("缺文档 " + node)

        if kind == "go":
            for cid in component_ids(doc):
                stack.append(("comp", cid))
            continue

        # 组件：Transform 才有 m_Children
        for child_transform in child_transform_ids(doc):
            stack.append(("comp", child_transform))

            child = by_id.get(child_transform)
            if child is None:
                raise SystemExit("缺子物体 Transform 文档 " + child_transform)

            owner = child.field("m_GameObject", "")
            m = re.search(r"fileID: (\d+)", owner)
            if not m:
                raise SystemExit("子物体 Transform 上没有 m_GameObject：" + child_transform)
            stack.append(("go", m.group(1)))

    return seen


# ---------------------------------------------------------------- 补丁 1：ArcherVisual
def archer_visual_body(player_go_id, sprite, controller, health, attacker, body, caster="0"):
    frames = lambda guids: ["  - " + sprite_ref(g) for g in guids]

    out = [
        "MonoBehaviour:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_GameObject: {fileID: %s}" % player_go_id,
        "  m_Enabled: 0",
        "  m_EditorHideFlags: 0",
        "  m_Script: {fileID: 11500000, guid: %s, type: 3}" % GUID_ARCHER_VISUAL,
        "  m_Name: ",
        "  m_EditorClassIdentifier: ",
        "  sprite: {fileID: %s}" % sprite,
        "  controller: {fileID: %s}" % controller,
        "  health: {fileID: %s}" % health,
        "  attacker: {fileID: %s}" % attacker,
        "  caster: {fileID: %s}" % caster,
        "  body: {fileID: %s}" % body,
        "  idle: " + sprite_ref(A_IDLE),
        "  jump: " + sprite_ref(A_JUMP),
        "  fall: " + sprite_ref(A_FALL),
        "  dash: " + sprite_ref(A_DASH),
        "  hurt: " + sprite_ref(A_HURT),
        "  death: " + sprite_ref(A_DEATH),
        "  victory: " + sprite_ref(A_VICTORY),
        "  focus: " + sprite_ref(A_VICTORY),
        "  idleFrames:",
    ]
    out += frames([A_IDLE, A_IDLE2, A_IDLE3])
    out += ["  idleFrameInterval: 0.72", "  walkFrames:"]
    out += frames(A_WALK)
    out += ["  walkFpsMin: 9", "  walkFpsMax: 17", "  shootFrames:"]
    out += frames(A_SHOOT)
    out += [
        "  shootHold: 0.2",
        "  hurtHold: 0.22",
        "  victoryHold: 1.4",
        "  focusHold: 0.55",
        "  runSpeedThreshold: 0.6",
    ]
    return out


def patch_player(docs, by_id):
    player = go_by_name(docs, "Player")
    if player is None:
        raise SystemExit("找不到 Player")

    player_id = player.id
    comps = component_ids(player)

    if str(NEW_ARCHER_VISUAL_ID) in comps:
        raise SystemExit("Player 上已经有 %d 号组件了，先确认场景是否已打过补丁" % NEW_ARCHER_VISUAL_ID)

    comp_by_script = {}
    for cid in comps:
        d = by_id.get(cid)
        if d is None:
            continue
        g = script_guid(d)
        if g:
            comp_by_script[g] = d

    switcher = comp_by_script.get(GUID_SWITCHER)
    if switcher is None:
        raise SystemExit("找不到 PlayerVisualSwitcher")

    # 引用从 WarriorVisual 上抄：它和 ArcherVisual 的依赖字段一模一样（sprite / controller /
    # health / attacker / body），而 PlayerVisualSwitcher 只有 stats / sprite，抄不到这些。
    sibling = comp_by_script.get(GUID_WARRIOR_VISUAL)
    if sibling is None:
        raise SystemExit("找不到 WarriorVisual，没法抄组件引用")

    def num(doc, field):
        raw = doc.field(field, "")
        m = re.search(r"fileID: (\d+)", raw)
        return m.group(1) if m and m.group(1) != "0" else None

    sprite, controller = num(sibling, "sprite"), num(sibling, "controller")
    health, attacker, body = num(sibling, "health"), num(sibling, "attacker"), num(sibling, "body")
    for name, value in [("sprite", sprite), ("controller", controller), ("health", health),
                        ("attacker", attacker), ("body", body)]:
        if not value:
            raise SystemExit("WarriorVisual.%s 引用读不到" % name)

    # SkillCaster 没有对外引用字段，靠它自己的字段组合认人（找不到也不要紧，运行时 GetComponent 会兜底）
    caster_id = "0"
    for cid in comps:
        d = by_id.get(cid)
        if d is None:
            continue
        fields = {l.strip().split(":")[0] for l in d.body if ":" in l}
        if {"startingSkills", "mana", "hitReaction", "input"} <= fields:
            caster_id = cid
            break

    # --- 新组件文档：插在 PlayerVisualSwitcher 后面 ---
    new_doc = Doc("--- !u!114 &%d" % NEW_ARCHER_VISUAL_ID,
                  archer_visual_body(player_id, sprite, controller, health, attacker, body,
                                     caster_id))
    insert_at = max(i for i, d in enumerate(docs) if d.id == switcher.id)
    docs.insert(insert_at + 1, new_doc)
    by_id[new_doc.id] = new_doc

    # --- Player 的组件列表追加 ---
    idx = None
    for i, line in enumerate(player.body):
        if line.strip().startswith("m_Component:"):
            idx = i
            break
    if idx is None:
        raise SystemExit("Player 上没有 m_Component")
    last = idx + 1
    while last < len(player.body) and player.body[last].strip().startswith("- "):
        last += 1
    player.body.insert(last, "  - component: {fileID: %d}" % NEW_ARCHER_VISUAL_ID)

    # --- PlayerVisualSwitcher：archer 字段 + 职业表追加射手 ---
    if not switcher.insert_after("mage", "  archer: {fileID: %d}" % NEW_ARCHER_VISUAL_ID):
        raise SystemExit("PlayerVisualSwitcher 上没有 mage 字段")

    # 职业表：在最后一个 `- {fileID: ...}` 后面补一项
    start = None
    for i, line in enumerate(switcher.body):
        if line.strip().startswith("classes:"):
            start = i
            break
    if start is None:
        raise SystemExit("PlayerVisualSwitcher 上没有 classes 字段")
    end = start + 1
    while end < len(switcher.body) and switcher.body[end].strip().startswith("- "):
        end += 1
    switcher.body.insert(end, "  - {fileID: 11400000, guid: %s, type: 2}" % GUID_CLASS_ARCHER)

    return switcher


# ---------------------------------------------------------------- 补丁 2：第三座立牌
def clone_pedestal(docs, by_id, source_name, new_name, class_guid):
    src_go = go_by_name(docs, source_name)
    if src_go is None:
        raise SystemExit("找不到 " + source_name)

    ids = collect_subtree(by_id, src_go.id)

    # 统一偏移，保证新 id 不和场景里任何既有 id 撞车
    mapping = {old: str(int(old) + PEDESTAL_OFFSET) for old in ids}
    for old, new in mapping.items():
        if new in by_id:
            raise SystemExit("撞 id 了：%s → %s" % (old, new))

    clones = []
    for old in sorted(ids, key=lambda x: int(x)):
        src = by_id[old]
        header = src.header.replace("&" + old, "&" + mapping[old])
        body = [re.sub(r"fileID: (\d+)",
                       lambda m: "fileID: " + mapping.get(m.group(1), m.group(1)), line)
                for line in src.body]
        clones.append(Doc(header, body))

    # 改名 + 换职业
    root = next(c for c in clones if c.cls == "1" and c.id == mapping[src_go.id])
    root.replace_line("m_Name", "  m_Name: " + new_name)

    for c in clones:
        for i, line in enumerate(c.body):
            if "guid:" in line and GUID_CLASS_MAGE in line:
                c.body[i] = line.replace(GUID_CLASS_MAGE, class_guid)

    docs.extend(clones)
    for c in clones:
        by_id[c.id] = c

    return root, clones


def set_portrait(by_id, clones, sprite_guid):
    """把复制出来的立牌立绘换成射手的待机帧（人物帧是 320x480 @160PPU，和法师同一套画布）"""
    comp = None
    for c in clones:
        if c.cls == "114" and c.field("portrait") is not None:
            comp = c
            break
    if comp is None:
        raise SystemExit("复制出来的立牌上没有 LobbyPedestal 组件")

    m = re.search(r"fileID: (\d+)", comp.field("portrait", ""))
    if not m:
        raise SystemExit("立牌的 portrait 引用读不到")

    portrait = by_id.get(m.group(1))
    if portrait is None:
        raise SystemExit("立牌的 portrait 渲染器找不到")

    if not portrait.replace_line("m_Sprite", "  m_Sprite: " + sprite_ref(sprite_guid)):
        raise SystemExit("立绘渲染器上没有 m_Sprite")


def set_pedestal_position(root_doc, by_id, x, y):
    """立牌根物体是 GameObject，位置在它的 Transform 上"""
    for cid in component_ids(root_doc):
        comp = by_id.get(cid)
        if comp is not None and comp.cls == "4":
            comp.replace_line("m_LocalPosition", "  m_LocalPosition: {x: %s, y: %s, z: 0}"
                              % (fmt(x), fmt(y)))
            return comp
    raise SystemExit("立牌没有 Transform")


def fmt(v):
    text = ("%.3f" % v).rstrip("0").rstrip(".")
    return text if text else "0"


# ---------------------------------------------------------------- 补丁 3：大厅接线
def patch_lobby(docs, by_id, archer_root):
    lobby_go = go_by_name(docs, "LobbyRoom")
    if lobby_go is None:
        raise SystemExit("找不到 LobbyRoom")

    room_comp = None
    room_transform = None
    for cid in component_ids(lobby_go):
        d = by_id.get(cid)
        if d is None:
            continue
        if d.cls == "4":
            room_transform = d
        elif "pedestals" in "\n".join(d.body):
            room_comp = d

    if room_comp is None or room_transform is None:
        raise SystemExit("LobbyRoom 的组件/Transform 找不到")

    # 立牌组件（LobbyPedestal）：找它的 classData 引用
    pedestal_comp = None
    for cid in component_ids(archer_root):
        d = by_id.get(cid)
        if d is not None and d.cls == "114" and d.field("classData") is not None:
            pedestal_comp = d
    if pedestal_comp is None:
        raise SystemExit("复制出来的立牌上没有 LobbyPedestal 组件")

    # pedestals 数组：warrior / archer / mage（和职业表顺序一致）
    start = None
    for i, line in enumerate(room_comp.body):
        if line.strip().startswith("pedestals:"):
            start = i
            break
    if start is None:
        raise SystemExit("LobbyRoom 上没有 pedestals 字段")

    end = start + 1
    while end < len(room_comp.body) and room_comp.body[end].strip().startswith("- "):
        end += 1

    existing = [l for l in room_comp.body[start + 1:end]]
    if len(existing) != 2:
        raise SystemExit("预期两座立牌，实际 %d 座" % len(existing))

    room_comp.body[start + 1:end] = [
        existing[0],
        "  - {fileID: %s}" % pedestal_comp.id,
        existing[1],
    ]

    # Transform 的 m_Children 追加立牌
    idx = None
    for i, line in enumerate(room_transform.body):
        if line.strip().startswith("m_Children:"):
            idx = i
            break
    if idx is None:
        raise SystemExit("LobbyRoom.Transform 上没有 m_Children")
    last = idx + 1
    while last < len(room_transform.body) and room_transform.body[last].strip().startswith("- "):
        last += 1
    # 立牌的 Transform（不是 GameObject）
    pedestal_transform = None
    for cid in component_ids(archer_root):
        d = by_id.get(cid)
        if d is not None and d.cls == "4":
            pedestal_transform = d
    if pedestal_transform is None:
        raise SystemExit("立牌 Transform 找不到")
    room_transform.body.insert(last, "  - {fileID: %s}" % pedestal_transform.id)

    return room_comp


def main():
    prefix, docs = load_scene(SCENE)
    by_id = index_by_id(docs)

    switcher = patch_player(docs, by_id)

    archer_root, archer_clones = clone_pedestal(docs, by_id, "Pedestal_Mage", "Pedestal_Archer",
                                                GUID_CLASS_ARCHER)
    set_portrait(by_id, archer_clones, A_IDLE)

    patch_lobby(docs, by_id, archer_root)

    # 三座台重新摆位
    for name, x in PEDESTAL_X.items():
        go = go_by_name(docs, name)
        if go is None:
            raise SystemExit("找不到立牌 " + name)
        set_pedestal_position(go, by_id, x, PEDESTAL_Y)

    save_scene(SCENE, prefix, docs)

    print("switcher     = %s" % switcher.id)
    print("archerVisual = %d" % NEW_ARCHER_VISUAL_ID)
    print("archerPedestal GO = %s" % archer_root.id)
    print("pedestal x   = %s" % PEDESTAL_X)


if __name__ == "__main__":
    main()
