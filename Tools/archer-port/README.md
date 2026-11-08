# 射手迁移工具（一次性）

这四份脚本是「把一号工程的射手并进本工程」时用的迁移工具，不是游戏的一部分。
正常开发请直接在编辑器里改；只有在**要重建一遍射手**（比如美术重切、资产被删）时才需要跑它们。

| 脚本 | 干什么 | 注意 |
| --- | --- | --- |
| `gen_assets.py` | 生成 4 个 `AttackData`、4 个 `SkillData`、`Class_Archer`，以及 `Arrow` / `ArrowPiercing` 两个预制体（连 `.meta` 的 hex guid 一起写） | guid 是写死的，和一号工程里同名资产**必须**保持一致，否则场景/资产里的引用会全断 |
| `patch_scene.py` | 给 `Game.unity` 的 Player 挂 `ArcherVisual`、把射手接进 `PlayerVisualSwitcher`（组件 + 职业表）、复制出第三尊大厅立牌、把三座台摆成 4.6 / 10.0 / 15.4 | 幂等：检测到场景里已有 `ArcherVisual` 会直接退出，不会重复挂 |
| `verify_refs.py` | 静态校验：fileID 不重、本文件内引用不悬空、hex guid 都能对上、职业→技能→攻击→预制体链接完整 | 不依赖编辑器，纯文本检查 |
| `compile_check.ps1` | 用团结自带的 Roslyn（`Editor/Data/MonoBleedingEdge/.../csc.exe`，跑在 `mono.exe` 上）编译 `Assembly-CSharp` 与 `Assembly-CSharp-Editor` | 不启动编辑器也能查语法/类型错误；`-Csproj` 选工程，编辑器程序集要多传 `-SkipRefName Assembly-CSharp -ExtraRef <刚编出来的 dll>` |

用法（在本目录下）：

```powershell
$py = "C:\Users\Lenovo\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe"

& $py gen_assets.py      # 只在资产缺失时跑，会覆盖同名资产
& $py patch_scene.py     # 只在场景没打过补丁时跑
& $py verify_refs.py     # 每次改完资产/场景都可以跑

powershell -ExecutionPolicy Bypass -File compile_check.ps1
```

脚本里的路径是**这台机器上的绝对路径**（工程目录、团结安装目录），换机器要改开头的常量。
