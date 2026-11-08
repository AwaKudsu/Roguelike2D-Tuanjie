# -*- coding: utf-8 -*-
"""
生成射手的 ScriptableObject 资产 + 箭预制体 + 对应 .meta。

为什么要用脚本生成：这些 YAML 里全是 GUID 交叉引用（职业→技能→攻击→预制体→贴图），
手抄任何一个 guid 写错，编辑器里都不会报错，只会表现为「箭是透明的」「技能放不出来」。
所以统一在这里写死一次，写完再由 check 脚本反查一遍引用是否都能对上。
"""
import json
import os

ROOT = r"D:\zuoye\tuanjie\TuanjieRoguelike2D-2\Assets\_Project"

HEADER = "%YAML 1.1\n%TAG !u! tag:yousandi.cn,2023:\n"

# ---------------- GUID ----------------
# 脚本（沿用两个工程里同一路径脚本的既有 guid，绝对不能自己编）
GUID_CLASS_DATA = "7cec368ec76244144a4ce3189eb6d4c8"
GUID_ATTACK_DATA = "27c5b5c33a3c0214a94e22dbb0c29198"
GUID_SKILL_DATA = "83d985721c7f2114c89426c6a47067ad"
GUID_PROJECTILE = "81e2e127827c450489a5dcf510532f31"

# 资产（= 一号工程里同名资产的真实 guid，从 ClassCatalog / 技能资产交叉引用里读出来的）
GUID_CLASS_ARCHER = "e4a7e9184998aa04f811163fd03079a4"
GUID_ATTACK_ARROW = "dcaf8c8a2a1606e4abdd22acc5b82c8f"
GUID_ATTACK_MULTI = "46bb8e4962b567e40aaf5eaab6fcb6f2"
GUID_ATTACK_PIERCE = "dcfb8ccbc0026f94b8c568ae2c4e8154"
GUID_ATTACK_RAIN = "af5d6728741999d418292b6faa42b92c"
GUID_SKILL_MULTI = "79295a4b81da4254694f3cf8b9e7b21d"
GUID_SKILL_PIERCE = "645476992794c5345a34e012b5e8a84d"
GUID_SKILL_RAIN = "151444d4fcbca224cbaa9b05dd1e8cc7"
GUID_SKILL_FOCUS = "7a114ae563a68e94da8bfae533a3f798"
GUID_PREFAB_ARROW = "ea538a49d2cfc224895ad3ded7151526"
GUID_PREFAB_ARROW_PIERCE = "46acc0d185bc2f24c83d2f09c08e2263"

# 美术（从一号工程的序列化引用里取的 hex guid；贴图 .meta 是原样拷贝过来的，
# 所以同一个 hex 在二号工程里指向同一张图）
GUID_ICON_CLASS = "66232b0f027429043bc345873764178a"   # Icon_ClassArcher
GUID_ICON_MULTI = "6e9a8e68856f15040ac56243e9489ba4"   # Icon_MultiShot
GUID_ICON_BOW = "4b60450d2e80e8e44801fb48987a489d"     # Icon_Bow
GUID_ICON_QUIVER = "f1011658b29852642b5dab2cbcbbd21e"  # Icon_Quiver
GUID_ICON_FOCUS = "79957f6b535d0a34ba7cf33944aa4985"   # Icon_Focus
GUID_SPRITE_ARROW = "b0d3052f99af4b041908c7a8bf9ce7e4"  # Arrow_Projectile.png
GUID_SPRITE_RING = "1acd2637401963545b4ec9ab102cdf24"   # Effects/Ring.png
GUID_SPRITE_IDLE = "3cc98eb39d459394fa11541c6415e4ef"   # Archer_Idle.png

# 箭预制体里根 GameObject 的 fileID（我照着 Fireball.prefab 拷的结构，所以沿用它的 id）
PREFAB_ROOT_FILEID = 746189112167610233


def q(text):
    """YAML 双引号字符串：中文转成 \\uXXXX 转义（大写十六进制，和引擎写出来的一致）。"""
    import re
    quoted = json.dumps(text, ensure_ascii=True)
    return re.sub(r"\\u([0-9a-f]{4})", lambda m: "\\u" + m.group(1).upper(), quoted)


def ref(guid, file_id=11400000, type_id=2):
    return "{fileID: %d, guid: %s, type: %d}" % (file_id, guid, type_id)


def write(path, text):
    full = os.path.join(ROOT, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return path


def meta(native, guid, main_id=11400000):
    return (
        "fileFormatVersion: 2\n"
        "guid: %s\n"
        "%s:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: %d\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n" % (guid, native, main_id)
    )


def prefab_meta(guid):
    return (
        "fileFormatVersion: 2\n"
        "guid: %s\n"
        "PrefabImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n" % guid
    )


def asset_body(script_guid, class_id, name, fields):
    head = (
        HEADER
        + "--- !u!114 &11400000\n"
        + "MonoBehaviour:\n"
        + "  m_ObjectHideFlags: 0\n"
        + "  m_CorrespondingSourceObject: {fileID: 0}\n"
        + "  m_PrefabInstance: {fileID: 0}\n"
        + "  m_PrefabAsset: {fileID: 0}\n"
        + "  m_GameObject: {fileID: 0}\n"
        + "  m_Enabled: 1\n"
        + "  m_EditorHideFlags: 0\n"
        + "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n" % script_guid
        + "  m_Name: %s\n" % name
        + "  m_EditorClassIdentifier: Assembly-CSharp::%s\n" % class_id
    )
    return head + fields


# =====================================================================
# AttackData
# =====================================================================
def attack_fields(display, icon, mult, knock, hitbox, cooldown, ranged, prefab_guid=None,
                  element="None", bits=128, offset=(0.0, 0.0)):
    lines = []
    lines.append("  displayName: %s\n" % q(display))
    lines.append("  icon: %s\n" % (ref(icon, 21300000, 3) if icon else "{fileID: 0}"))
    lines.append("  damageMultiplier: %s\n" % mult)
    lines.append("  knockbackForce: %s\n" % knock)
    lines.append("  bonusCritChance: 0\n")
    lines.append("  element: %d\n" % element_id(element))
    lines.append("  hitboxOffset: {x: %s, y: %s}\n" % offset)
    lines.append("  hitboxSize: {x: %s, y: %s}\n" % hitbox)
    lines.append("  targetLayers:\n    serializedVersion: 2\n    m_Bits: %d\n" % bits)
    lines.append("  isRanged: %d\n" % (1 if ranged else 0))
    if prefab_guid:
        lines.append("  projectilePrefab: %s\n" % ref(prefab_guid, PREFAB_ROOT_FILEID, 3))
    else:
        lines.append("  projectilePrefab: {fileID: 0}\n")
    lines.append("  cooldown: %s\n" % cooldown)
    return "".join(lines)


def element_id(name):
    """DamageElement 的序号：None=0, Poison=1, Fire=2, Ice=3（见 DamageInfo.cs）"""
    return {"None": 0, "Poison": 1, "Fire": 2, "Ice": 3}.get(name, 0)


# =====================================================================
# SkillData
# =====================================================================
CAST_MELEE, CAST_PROJECTILE, CAST_WARCRY, CAST_HEAL, CAST_ARROWRAIN, CAST_BUFF = 0, 1, 2, 3, 4, 5

SKILL_DEFAULTS = """  duration: 0
  tickInterval: 0.22
  fearDuration: 3.5
  fearRadius: 12
  ringDuration: 0.45
  ringSprite: {fileID: 0}
  swordSprite: {fileID: 0}
  auraSprite: {fileID: 0}
  rainDistance: 5
  rainRadius: 4
  rainCount: 9
  rainDuration: 1.2
  rainHeight: 8
  rainFallSpeed: 17
  buffDuration: 6
  buffAttackSpeed: 0.35
  buffCritChance: 0.15
  buffMoveSpeed: 0
  projectilePrefab: {fileID: 0}
  projectileCount: 1
  spreadAngle: 10
  chargeable: 0
  chargeTime: 1.1
  chargeMinScale: 0.7
  chargeMaxScale: 2.2
  chargeMaxSpeedBonus: 1.9
  chargeMaxDamageBonus: 2
  healAmount: 6
  healDuration: 2.4
  healTickInterval: 0.3
  circleSprite: {fileID: 0}
  circleDiameter: 3.2
  healColor: {r: 0.45, g: 1, b: 0.55, a: 1}
"""


def skill_asset(name, skill_id, display, desc, mana, cooldown, cast_type, attack_guid,
                icon_guid, overrides):
    """按 SkillData 的字段顺序拼一份资产；overrides 用 (field, yaml_text) 覆盖默认行。"""
    lines = [
        "  skillId: %s\n" % skill_id,
        "  displayName: %s\n" % q(display),
        "  icon: %s\n" % ref(icon_guid, 21300000, 3),
        "  description: %s\n" % q(desc),
        "  manaCost: %s\n" % mana,
        "  cooldown: %s\n" % cooldown,
        "  castType: %d\n" % cast_type,
        "  attack: %s\n" % (ref(attack_guid) if attack_guid else "{fileID: 0}"),
    ]

    body = "".join(lines) + SKILL_DEFAULTS

    for key, yaml in overrides:
        body = replace_field(body, key, yaml)

    return asset_body(GUID_SKILL_DATA, "SkillData", name, body)


def replace_field(body, key, yaml):
    """替换一行 `key: ...`（保留缩进）。找不到就报错 —— 宁可炸，也不要静默写错资产。"""
    out = []
    done = False
    for line in body.splitlines(True):
        stripped = line.lstrip()
        if not done and stripped.startswith(key + ":"):
            indent = line[: len(line) - len(stripped)]
            out.append(indent + key + ": " + yaml + "\n")
            done = True
        else:
            out.append(line)
    if not done:
        raise SystemExit("字段不存在：%s" % key)
    return "".join(out)


# =====================================================================
# 生成
# =====================================================================
def main():
    made = []

    # ---------- AttackData ----------
    attacks = [
        (
            "Data/Weapons/Attack_Arrow.asset", GUID_ATTACK_ARROW, "Attack_Arrow",
            attack_fields("射击", GUID_ICON_MULTI, 1, 3.5, ("0.4", "0.4"), 0.34, True, GUID_PREFAB_ARROW),
        ),
        (
            "Data/Weapons/Attack_Arrow_Multi.asset", GUID_ATTACK_MULTI, "Attack_Arrow_Multi",
            attack_fields("多重箭", GUID_ICON_MULTI, 0.8, 2.5, ("0.4", "0.4"), 0, True, GUID_PREFAB_ARROW),
        ),
        (
            "Data/Weapons/Attack_Arrow_Pierce.asset", GUID_ATTACK_PIERCE, "Attack_Arrow_Pierce",
            attack_fields("穿透箭", GUID_ICON_BOW, 2.2, 8, ("0.5", "0.5"), 0, True, GUID_PREFAB_ARROW_PIERCE),
        ),
        (
            "Data/Weapons/Attack_Arrow_Rain.asset", GUID_ATTACK_RAIN, "Attack_Arrow_Rain",
            attack_fields("落箭", GUID_ICON_QUIVER, 0.75, 1.5, ("0.4", "0.4"), 0, True, GUID_PREFAB_ARROW),
        ),
    ]

    for path, guid, name, fields in attacks:
        body = asset_body(GUID_ATTACK_DATA, "AttackData", name, fields)
        made.append(write(path, body))
        made.append(write(path + ".meta", meta("NativeFormatImporter", guid)))

    # ---------- SkillData ----------
    arrow_prefab_ref = "  projectilePrefab: %s" % ref(GUID_PREFAB_ARROW, PREFAB_ROOT_FILEID, 3)
    pierce_prefab_ref = "  projectilePrefab: %s" % ref(GUID_PREFAB_ARROW_PIERCE, PREFAB_ROOT_FILEID, 3)
    ring_ref = "  ringSprite: %s" % ref(GUID_SPRITE_RING, 21300000, 3)

    skills = [
        (
            "Data/Skills/Skill_MultiShot.asset", GUID_SKILL_MULTI,
            skill_asset(
                "Skill_MultiShot", "archer_multi_shot", "多重射击",
                "一次射出 3 支箭，扇形覆盖；每支 80% 攻击力",
                10, 4.5, CAST_PROJECTILE, GUID_ATTACK_MULTI, GUID_ICON_MULTI,
                [("projectilePrefab", ref(GUID_PREFAB_ARROW, PREFAB_ROOT_FILEID, 3)),
                 ("projectileCount", "3"),
                 ("spreadAngle", "10")],
            ),
        ),
        (
            "Data/Skills/Skill_PiercingArrow.asset", GUID_SKILL_PIERCE,
            skill_asset(
                "Skill_PiercingArrow", "archer_piercing_arrow", "穿透箭",
                "蓄力射出一支贯穿箭，最多穿透 4 个敌人，造成 220% 攻击力伤害",
                18, 8, CAST_PROJECTILE, GUID_ATTACK_PIERCE, GUID_ICON_BOW,
                [("projectilePrefab", ref(GUID_PREFAB_ARROW_PIERCE, PREFAB_ROOT_FILEID, 3)),
                 ("projectileCount", "1"),
                 ("spreadAngle", "0")],
            ),
        ),
        (
            "Data/Skills/Skill_ArrowRain.asset", GUID_SKILL_RAIN,
            skill_asset(
                "Skill_ArrowRain", "archer_arrow_rain", "箭雨",
                "在身前区域召唤 9 支箭依次落下，持续 1.2 秒；适合封走位、清小怪",
                25, 12, CAST_ARROWRAIN, GUID_ATTACK_RAIN, GUID_ICON_QUIVER,
                [("projectilePrefab", ref(GUID_PREFAB_ARROW, PREFAB_ROOT_FILEID, 3)),
                 ("ringSprite", ref(GUID_SPRITE_RING, 21300000, 3)),
                 ("rainDistance", "5"),
                 ("rainRadius", "4"),
                 ("rainCount", "9"),
                 ("rainDuration", "1.2"),
                 ("rainHeight", "8"),
                 ("rainFallSpeed", "17")],
            ),
        ),
        (
            "Data/Skills/Skill_HunterFocus.asset", GUID_SKILL_FOCUS,
            skill_asset(
                "Skill_HunterFocus", "archer_hunter_focus", "猎人专注",
                "6 秒内攻速 +35%、暴击率 +15%；按下去就要准备好把输出打满",
                20, 14, CAST_BUFF, None, GUID_ICON_FOCUS,
                [("ringSprite", ref(GUID_SPRITE_RING, 21300000, 3)),
                 ("buffDuration", "6"),
                 ("buffAttackSpeed", "0.35"),
                 ("buffCritChance", "0.15"),
                 ("buffMoveSpeed", "0")],
            ),
        ),
    ]

    for path, guid, body in skills:
        made.append(write(path, body))
        made.append(write(path + ".meta", meta("NativeFormatImporter", guid)))

    # ---------- ClassData ----------
    class_fields = []
    class_fields.append("  className: %s\n" % q("射手"))
    class_fields.append("  icon: %s\n" % ref(GUID_ICON_CLASS, 21300000, 3))
    class_fields.append("  description: %s\n" % q(
        "远程输出，靠走位和箭雨控场。血薄、怕贴脸，但能把整条走廊变成自己的射程。"))
    # 大厅朝向：射手只画了侧视姿势表，所以「往下走」也用这张（和战士一样），背面留空退回侧视
    class_fields.append("  lobbyFront: %s\n" % ref(GUID_SPRITE_IDLE, 21300000, 3))
    class_fields.append("  lobbyBack: {fileID: 0}\n")
    class_fields.append("  lobbySide: {fileID: 0}\n")
    class_fields.append("  maxHealth: 95\n")
    class_fields.append("  attack: 11\n")
    class_fields.append("  moveSpeed: 8.5\n")
    class_fields.append("  attackSpeed: 1.15\n")
    class_fields.append("  critChance: 0.1\n")
    class_fields.append("  critMultiplier: 1.6\n")
    class_fields.append("  defense: 0.03\n")
    class_fields.append("  cooldownRate: 0\n")
    class_fields.append("  maxMana: 80\n")
    class_fields.append("  manaRegen: 7\n")
    class_fields.append("  basicAttack: %s\n" % ref(GUID_ATTACK_ARROW))
    class_fields.append("  startingSkills:\n")
    for g in (GUID_SKILL_MULTI, GUID_SKILL_PIERCE, GUID_SKILL_RAIN, GUID_SKILL_FOCUS):
        class_fields.append("  - %s\n" % ref(g))

    made.append(write("Data/classes/Class_Archer.asset",
                      asset_body(GUID_CLASS_DATA, "ClassData", "Class_Archer", "".join(class_fields))))
    made.append(write("Data/classes/Class_Archer.asset.meta",
                      meta("NativeFormatImporter", GUID_CLASS_ARCHER)))

    # ---------- 箭预制体 ----------
    def arrow_prefab(name, sprite_guid, color, radius, scale, speed, life, pierce, max_hits, sort_order):
        return (
            HEADER
            + """--- !u!1 &%(root)d
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 7
  m_Component:
  - component: {fileID: 4093876723906147635}
  - component: {fileID: 8598890984689286182}
  - component: {fileID: 1631815303486743381}
  - component: {fileID: 3708960863625297284}
  - component: {fileID: 1645432594281700542}
  m_Layer: 0
  m_HasEditorInfo: 1
  m_Name: %(name)s
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &4093876723906147635
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: %(root)d}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: %(scale)s, y: %(scale)s, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!212 &8598890984689286182
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: %(root)d}
  m_Enabled: 1
  m_CastShadows: 0
  m_ReceiveShadows: 0
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 0
  m_RayTraceProcedural: 0
  m_virtualGeometry: 0
  m_ShadingRate: 0
  m_ForceDisableGRD: 0
  m_ForceDisableSmallMeshCull: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {fileID: 10754, guid: 0000000000000000f000000000000000, type: 0}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {fileID: 0}
  m_ProbeAnchor: {fileID: 0}
  m_LightProbeVolumeOverride: {fileID: 0}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 0
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {fileID: 0}
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: %(sort_order)d
  m_Sprite: {fileID: 21300000, guid: %(sprite)s, type: 3}
  m_Color: %(color)s
  m_FlipX: 0
  m_FlipY: 0
  m_DrawMode: 0
  m_Size: {x: 1, y: 1}
  m_AdaptiveModeThreshold: 0.5
  m_SpriteTileMode: 0
  m_WasSpriteAssigned: 1
  m_MaskInteraction: 0
  m_SpriteSortPoint: 0
--- !u!50 &1631815303486743381
Rigidbody2D:
  serializedVersion: 4
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: %(root)d}
  m_BodyType: 1
  m_Simulated: 1
  m_UseFullKinematicContacts: 0
  m_UseAutoMass: 0
  m_Mass: 1
  m_LinearDrag: 0
  m_AngularDrag: 0.05
  m_GravityScale: 0
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_Interpolate: 1
  m_SleepingMode: 1
  m_CollisionDetection: 1
  m_Constraints: 4
--- !u!58 &3708960863625297284
CircleCollider2D:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: %(root)d}
  m_Enabled: 1
  serializedVersion: 3
  m_Density: 1
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_ForceSendLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_ForceReceiveLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_ContactCaptureLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_CallbackLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_IsTrigger: 1
  m_UsedByEffector: 0
  m_CompositeOperation: 0
  m_CompositeLayer: 0
  m_Offset: {x: 0, y: 0}
  m_Radius: %(radius)s
--- !u!114 &1645432594281700542
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: %(root)d}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: %(projectile)s, type: 3}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::Projectile
  speed: %(speed)s
  lifeTime: %(life)s
  targetLayers:
    serializedVersion: 2
    m_Bits: 0
  obstacleLayers:
    serializedVersion: 2
    m_Bits: 256
  pierce: %(pierce)d
  maxHits: %(max_hits)d
  rotateToDirection: 1
  mirrorToDirection: 0
  body: {fileID: 0}
  destroyDelay: 0
  impactSprite: {fileID: 0}
  impactColor: {r: 1, g: 0.9, b: 0.7, a: 1}
  leaveTrail: 1
  trailSpacing: 0.3
  spinSpeed: 0
  obstacleGrace: 0.08
  maxColliderScale: 1.6
"""
            % dict(root=PREFAB_ROOT_FILEID, name=name, sprite=sprite_guid, color=color,
                   radius=radius, scale=scale, speed=speed, life=life, pierce=pierce,
                   max_hits=max_hits, sort_order=sort_order, projectile=GUID_PROJECTILE)
        )

    made.append(write("Prefabs/Arrow.prefab",
                      arrow_prefab("Arrow", GUID_SPRITE_ARROW, "{r: 1, g: 1, b: 1, a: 1}",
                                   0.12, 1, 22, 1.1, 0, 0, 2)))
    made.append(write("Prefabs/Arrow.prefab.meta", prefab_meta(GUID_PREFAB_ARROW)))

    made.append(write("Prefabs/ArrowPiercing.prefab",
                      arrow_prefab("ArrowPiercing", GUID_SPRITE_ARROW,
                                   "{r: 1, g: 0.88, b: 0.6, a: 1}", 0.16, 1.3, 26, 1.4, 1, 4, 3)))
    made.append(write("Prefabs/ArrowPiercing.prefab.meta", prefab_meta(GUID_PREFAB_ARROW_PIERCE)))

    print("generated %d files:" % len(made))
    for m in made:
        print("  " + m)


if __name__ == "__main__":
    main()
