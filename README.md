# Roguelike2D · 横板动作肉鸽

> **一句话简介**：一款 2D 横板动作 Roguelike。每次进入地牢，房间布局、敌人配置与掉落词条都会重新生成；死亡即失去本局所有收获，但可以用积累的资源永久解锁新武器与词条，形成「越玩越强、越强越想玩」的循环。

<!--
  📌 简历门面就靠这一屏。填写要点：
  1. 第一张图必须是最帅的实机截图（不是 Logo）
  2. 「下载试玩」这一行要在首屏可见 —— 面试官不会往下翻
  3. 上线前把下面这些占位符全部替换掉，删掉本段注释
-->

<p align="center">
  <img src="docs/images/hero.png" alt="游戏截图" width="800">
</p>

<p align="center">
  <b><a href="../../releases/latest">⬇️ 下载试玩（Windows / WebGL）</a></b> ·
  <a href="docs/02-游戏设计文档.md">设计文档</a> ·
  <a href="docs/00-行动清单.md">开发清单</a>
</p>

![演示](docs/images/demo.gif)

---

## 玩法

**已经能玩的**（当前可运行版本）

- **横板动作战斗**：近战挥砍带判定框与命中闪白、击退硬直；法师职业发射投射物
- **房间流水线**：清场 → 开门 → 换房，房间池用「袋子随机」抽，抽完一轮才重复
- **三名职业 + 技能栏**：战士（旋风斩）/ 法师（火球、魔弹），技能走 `SkillData` 数据驱动
- **资源条**：血量 / 蓝量 HUD，技能进入冷却时技能栏实时反馈

**设计目标**（还没实现，见 [docs/00-行动清单.md](docs/00-行动清单.md)）

- **程序生成关卡**：每局房间布局与敌人配置随机，且支持固定种子复现
- **词条构筑**：局内获取遗物/词条，词条之间存在乘算联动，构筑决定强度上限
- **永久成长**：局外解锁新武器与词条，死亡不等于白玩
- **Boss 战**：多阶段 Boss，血量过半后变招

## 操作

绑定表就是 `Assets/_Project/Settings/PlayerInputActions.inputactions` 里的 8 个 Action（1 个 `Player` ActionMap / 17 条 binding），改键直接改这个文件。

| 按键 | 动作 | Action |
| --- | --- | --- |
| `A` / `D`（或 `←` / `→`） | 左右移动 | `Move` |
| `W` / `S`（或 `↑` / `↓`） | 上下（跳跃/下蹲预留） | `Move` |
| `Space` | 跳跃（含土狼时间与跳跃缓冲） | `Jump` |
| `LeftShift` | 冲刺 | `Dash` |
| `J` | 普通攻击 | `Attack` |
| `K` / `L` / `U` / `I` | 技能 1 / 2 / 3 / 4 | `Skill1` ~ `Skill4` |

## 技术栈

| 项目 | 选择 |
| --- | --- |
| 引擎 | **团结引擎 2022.3.62t16**（Tuanjie Editor 1.10.4，版本锁定，不要随意升级） |
| 宿主工程 | Unity **6000.3.22f1** → 已于 2026-10-10 迁移到团结（见 [docs/09](docs/09-迁移到团结引擎.md)） |
| 语言 | C# |
| 渲染管线 | Built-in Render Pipeline（2D） |
| 输入 | Input System 1.7.0（`PlayerInputActions` 包装类随包版本自动重生成） |
| 测试 | Unity Test Framework（`Assets/_Project/Tests/`，尚未编写用例） |
| 版本管理 | Git（`main` @ [AwaKudsu/Roguelike2D-Tuanjie](https://github.com/AwaKudsu/Roguelike2D-Tuanjie)，场景/预制体走团结 YAML 合并驱动） |

## 如何运行

### 方式一：直接下载（推荐给面试官 / 老师）

前往 [Releases](../../releases/latest) 下载 `Roguelike2D-v1.0-win64.zip`，解压后运行 `Roguelike2D.exe`。
无需安装团结引擎（打包还没做，链接先占位）。

### 方式二：从源码运行

```bash
# 本机 github.com 走代理，SSH 22 端口会被 reset，所以用 GitHub 的 443 端点
git clone ssh://git@ssh.github.com:443/AwaKudsu/Roguelike2D-Tuanjie.git
cd Roguelike2D-Tuanjie
```

然后用 **团结 Hub（或团结 Cowork）→ 添加工程 → 选择该文件夹**，使用 `2022.3.62t16` 打开。首次打开需要几分钟导入资源并重建 `Library/`。

> 仓库不含 `Library/`、`Logs/`、`.codely-cli/`（团结 Cowork 的本地代码索引），这些都是本机可重建的缓存。

## 目录结构

```
Roguelike2D/
├── Assets/
│   ├── _Project/              # 所有自己产出的资源（下划线开头，排在资源浏览器最前）
│   │   ├── Art/               # 美术：角色 / 敌人 / 场景 / UI / 特效
│   │   ├── Audio/             # 音频：BGM / 音效
│   │   ├── Data/              # ScriptableObject 数据：武器 / 词条 / 敌人 / 房间
│   │   ├── Prefabs/           # 预制体
│   │   ├── Scenes/            # 场景
│   │   ├── Scripts/           # 代码，按职责分层
│   │   │   ├── Core/          # 游戏循环、状态机、事件总线、存档
│   │   │   ├── Player/        # 输入、移动、战斗
│   │   │   ├── Enemies/       # 敌人 AI 与感知
│   │   │   ├── Combat/        # 伤害、命中、状态效果
│   │   │   ├── Roguelike/     # 关卡生成、掉落、词条、局外成长
│   │   │   ├── UI/
│   │   │   └── Utils/
│   │   ├── Settings/          # 渲染 / 输入等配置资源
│   │   ├── Tests/             # 单元测试
│   │   └── UI/
│   └── ThirdParty/            # 第三方免费素材，每个包必须带 SOURCE.md 标注授权
├── docs/                      # 设计文档、开发清单、Git 手册、迁移记录
├── Tools/                     # 开发辅助脚本
├── Packages/                  # 包依赖清单
├── ProjectSettings/           # 工程设置（必须提交）
└── _Tuanjie/                  # 迁移自检报告与设置备份（54 MB 的安装清单不入库）
```

`Assets/_Project/_Tuanjie/` 下有一个编辑器专用目录，装的是迁移期的自检工具 `Editor/MigrationVerifier.cs`（菜单 `团结/`）：跑一遍会输出脚本编译、预制体、场景引用、内置资源等检查报告到 `_Tuanjie/`，用来在迁移后证明「没有断引用、没有 Missing Script」。它不是游戏逻辑，可以随时删掉。

**为什么这么分**：`Assets` 根目录下的东西越少越好。所有自己的东西塞进 `_Project/`，第三方素材塞进 `ThirdParty/`，以后要删素材包、要迁移、要给别人看代码，都不会误伤。

## 开发进度

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| 0 | 工程与版本管理地基 | ✅ |
| 1 | 垂直切片：移动 / 战斗 / 一个敌人 | ✅ |
| 2 | 肉鸽骨架：关卡生成 / 掉落 / Boss | ⬜ |
| 3 | 构筑深度 + 局外成长 | ⬜ |
| 4 | 打磨：美术 / 音效 / UI | ⬜ |
| 5 | 交付：打包 / 文档 / 答辩 | ⬜ |

详细任务见 [docs/00-行动清单.md](docs/00-行动清单.md)。

## 我做了什么

<!-- 这段是给面试官看的，务必用「我」做主语，不要写「我们」 -->

- 设计并实现了基于**房间模板池 + 方向匹配**的程序化关卡生成，支持固定种子复现（可写单元测试验证）
- 实现了数据驱动的词条系统（`ScriptableObject` + 多态效果），新增词条无需修改战斗代码
- 实现了角色动作手感细节：土狼时间、跳跃缓冲、可变跳跃高度、命中顿帧
- 用 Git 管理项目，配置 UnityYAMLMerge 作为场景/预制体的合并驱动，处理 `.unity` 与 `.prefab` 的多人协同时的冲突
- 把工程从 Unity 6000.3 迁移到团结引擎 2022.3（含 22 处 API 降级与内置资源引用重映射），全程保持原 Unity 工程零改动，见 [docs/09-迁移到团结引擎.md](docs/09-迁移到团结引擎.md)

## 素材来源

见 [Assets/ThirdParty/](Assets/ThirdParty/) 下各素材包的 `SOURCE.md`。

当前版本**还没有接入任何美术素材**，画面全部由 `Assets/_Project/Art/Placeholder/` 下的两张程序生成的占位图承担：

| 占位图 | 尺寸 | 用途 |
| --- | --- | --- |
| `Square.png` | 32×32，32 PPU | 玩家、地面、门、敌人、尖刺（靠 `SpriteRenderer.m_Color` 染色区分） |
| `Circle.png` | 32×32，32 PPU | 火球、魔弹两个投射物 |

迁移前这些位置引用的是 Unity 的内置图形（`Square` / `Knob`），团结引擎把内置资源包重新打包成了 `tuanjie_builtin_extra`，原 GUID 全部失效 —— 详见迁移记录。

## License

代码采用 MIT 协议。第三方美术与音频素材遵循各自原始授权（详见各 `SOURCE.md`）。
