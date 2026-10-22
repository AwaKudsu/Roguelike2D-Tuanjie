# Day 12 · Esc 菜单 / 大厅 / 设置（音量 + 改键）

> 一句话：按 `Esc` 能暂停，菜单里有继续、设置（音量）、按键调整、返回大厅、退出游戏；启动先看到大厅，点「开始游戏」才进关。

## 1. 做了什么

### 新增的 5 个脚本

| 文件 | 一句话职责 |
| --- | --- |
| `Assets/_Project/Scripts/Core/TimeFreeze.cs` | 时间冻结的仲裁中心：谁想冻结游戏就登记一个 owner，全部释放才还原 `Time.timeScale` |
| `Assets/_Project/Scripts/Core/GameSettings.cs` | 音量设置：主音量直接写 `AudioListener.volume`，三个值都存 `PlayerPrefs` |
| `Assets/_Project/Scripts/Core/InputBindingStore.cs` | 改键存档：`InputActionAsset` 的 binding overrides 序列化成 JSON 存 `PlayerPrefs` |
| `Assets/_Project/Scripts/UI/GameMenuUI.cs` | 菜单本体：运行时用代码搭出整块界面（主菜单 / 设置 / 按键调整三页） |
| `Assets/_Project/Scripts/Core/GameBootstrap.cs` | 启动器：`[RuntimeInitializeOnLoadMethod]` 自动把菜单挂进每个场景，并决定这次加载是「进大厅」还是「直接开打」 |

### 改动的 3 个地方

| 文件 | 改动 |
| --- | --- |
| `Assets/_Project/Scripts/Player/PlayerInputReader.cs` | `Awake()` 里 `new PlayerInputActions()` 之后调 `InputBindingStore.Apply(_actions.asset)`；新增 `public PlayerInputActions Actions => _actions;` 给设置界面改键用 |
| `Assets/_Project/Scripts/UI/RelicChoiceUI.cs` | 原来自己写 `Time.timeScale = 0` / 还原，改成 `TimeFreeze.Freeze(this)` / `TimeFreeze.Unfreeze(this)`；删掉 `_previousTimeScale` 字段 |
| `README.md` | 操作表加 `Esc` 一行，玩法里加「大厅与暂停菜单」一条 |

### 新增的自检

`Assets/_Project/_Tuanjie/Editor/MenuCheck.cs`，菜单 **团结 → 验证 Esc 菜单**，报告写 `_Tuanjie/menu-report.txt`。

## 2. 怎么用

1. 按 `Play` → 先出现**大厅**（画面冻着，时间不走），点「开始游戏」
2. 玩到一半按 `Esc` → **暂停菜单**，游戏时间冻结
3. 「设置」→ 三条音量，`-` / `+` 每次 0.1，改主音量或音效会响一声试听音
4. 「按键调整」→ 点某一行 → 提示「请按新的键」→ 按键生效并立刻存档；`Esc` 取消本次修改
5. 「返回大厅」→ 回到大厅（重新载入场景，血条蓝条词条全部重置）
6. 「退出游戏」→ 编辑器里停止 Play，打包后退出程序

菜单里所有按钮都能用鼠标点，也可以用键盘 `Esc` 一层层返回。

## 3. 为什么必须有 `TimeFreeze`

原来的问题是**两个地方抢 `Time.timeScale`**：

- `RelicChoiceUI`（词条三选一）在 `Show()` 里写 `Time.timeScale = 0f`，在 `Hide()` 里还原
- 暂停菜单也要冻结时间

如果各写各的，就会出现这种情况：词条面板开着的时候（`timeScale = 0`）玩家按 `Esc` 打开暂停菜单并把 `timeScale` 记成「原来是 0」，然后关掉暂停菜单时把它还原成 0 —— **词条面板还没关，游戏却……或者反过来，暂停菜单关了却把词条面板的冻结也一起解了**。谁后关谁说了算，另一个弹窗的状态就被踩坏了。

改成登记 owner 之后：

```csharp
TimeFreeze.Freeze(this);      // 第一个冻结的人才记录「原本的时间倍率」
TimeFreeze.Unfreeze(this);    // 只有登记表空了才还原
TimeFreeze.UnfreezeAll();     // 切场景时兜底，防止残留 owner 把新场景冻住
```

`GameMenuUI.Update()` 里还有一条保护：`_page == None` 且 `TimeFreeze.IsFrozen` 时**不响应 Esc** —— 也就是词条面板开着的时候按 Esc 不会叠出暂停菜单。

## 4. 改键是怎么存下来的

Input System 自带 binding overrides，存档只需要两行：

```csharp
string json = asset.SaveBindingOverridesAsJson();   // 存
asset.LoadBindingOverridesFromJson(json);           // 读
```

> ⚠️ 注意方法名：**读取是 `FromJson`，不是 `AsJson`**。Input System 1.7.0 里只有 `SaveBindingOverridesAsJson` 带 `As`，写成 `LoadBindingOverridesAsJson` 会报 `CS1061`（这次踩过）。

重绑流程（`InputBindingStore.StartRebind`）：

1. `action.Disable()` —— 不改这一步，重绑过程中按键还会被游戏吃进去
2. `action.PerformInteractiveRebinding()` —— 不传 binding 索引表示改第 0 条绑定
3. `.WithControlsExcluding("<Mouse>/position")` 等三条，避免鼠标移动被当成按键
4. `.WithCancelingThrough("<Keyboard>/escape")` —— Esc 取消
5. `.OnMatchWaitForAnother(0.15f)` —— 按下后等一下，防止一次按键被识别成多个
6. 完成或取消后 `operation.Dispose()`、`action.Enable()`、回调里写盘

存下来之后，`PlayerInputReader.Awake()` 会调 `InputBindingStore.Apply(...)` —— **放在 `Awake` 而不是 `OnEnable`**，因为暂停菜单会反复开关 `PlayerInputReader` 组件，放 `OnEnable` 会重复读取。

改键页只覆盖 7 个**按钮型**动作：跳跃、冲刺、攻击、技能 1~4。移动（`Move`）是 2DVector composite（WASD + 方向键共 8 条 binding），要重绑得单独处理上/下/左/右四个 part，暂时不做。

## 5. 音量：现在还没有音频素材

工程里 `Assets/_Project/Audio/{BGM,SFX}` 至今是空的（只有 `.gitkeep`），所以：

- **主音量**是真的生效的：直接写 `AudioListener.volume`，将来加音乐音效立刻受控
- **音乐 / 音效**两个值只是存下来了，暂时没有声音受它影响
- 为了让你能听出「音量确实在变」，改主音量或音效时会**现场合成**一段 0.2 秒的 880Hz 正弦试听音（`AudioClip.Create` + 前 10ms 淡入 / 后 50ms 淡出防爆音），不依赖任何音频文件

## 6. 自检结果

`D:\TuanjieRoguelike2D\_Tuanjie\menu-report.txt`（**失败 0 / 警告 0**）：

```
[PASS]|新增类型齐全|TimeFreeze / GameSettings / InputBindingStore / GameMenuUI / GameBootstrap
[PASS]|GameBootstrap 自动启动|[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] 已挂上
[PASS]|PlayerInputReader.Actions|可用于设置界面改键
[PASS]|TimeFreeze 仲裁|两人冻结 → 只解一个仍冻结 → 全解才还原 1.0
[PASS]|主音量已交给 AudioListener|AudioListener.volume = 0.80
[PASS]|音量写盘|settings.audio.master = 0.35
[PASS]|音量已还原|0.80
[PASS]|七条动作都读得出当前按键|跳跃=Space  冲刺=Left Shift  攻击=J  技能1=K  技能2=L  技能3=U  技能4=I
[PASS]|改键存档往返|保存 → 读回 → 全部恢复默认，三步都不报错
[PASS]|场景可点按钮|EventSystem + InputSystemUIInputModule 都在
[PASS]|RoomManager 房间池仍在|RoomPool_Default
[SUMMARY] 失败|0   [SUMMARY] 警告|0
```

自检会临时改音量、临时写一条改键存档，跑完把你本机原来的值还回去。

### 怎么重跑

**菜单方式**：编辑器开着 → 菜单 **团结 → 验证 Esc 菜单**（结果看 Console 与 `_Tuanjie/menu-report.txt`）

**命令行方式**（必须先关掉编辑器，否则 `Library` 被锁）：

```
"D:\Tuanjie\Editor\2022.3.62t16\Editor\Tuanjie.exe" -batchmode -quit ^
  -projectPath D:\TuanjieRoguelike2D ^
  -executeMethod Tuanjie.PortCheck.MenuCheck.Run ^
  -logFile D:\TuanjieRoguelike2D\_Tuanjie\menu-check.log
```

跑完查两件事：`_Tuanjie/menu-report.txt` 是新的、日志里 `error CS` 是 0 条。

## 7. 排错表

| 症状 | 原因 | 怎么办 |
| --- | --- | --- |
| 按 `Esc` 没反应 | 菜单物体没被创建（`GameBootstrap` 没跑起来） | 菜单 **团结 → 验证 Esc 菜单**，看第一条是不是 PASS |
| 菜单出现了但游戏还在动 | 有另一处代码把 `timeScale` 改回去了 | 搜全工程 `Time.timeScale`，除了 `TimeFreeze.cs` 不该有别的写入 |
| 点了按钮没反应 | 场景里缺 `EventSystem`，或 `InputSystemUIInputModule` 没挂 | 自检第 10 条会报；也可以在 Hierarchy 里点 `EventSystem` 看 Inspector |
| 改完键没生效 | 改完没退出暂停菜单（改键后要 `Close()` 才重新 `OnEnable`） | 关掉菜单就生效；实在不行「全部恢复默认」 |
| 每次 Play 都停在原地不动 | 这是大厅（正常） | 点「开始游戏」；想跳过大厅改 `GameBootstrap.ShowLobbyOnStart` 的初值 |
| 改键存档想清掉 | 存在 `PlayerPrefs` 里 | 游戏里点「全部恢复默认」；或注册表删 `HKCU\Software\<公司名>\<产品名>` |

## 8. 已知限制

1. **没有独立的 Lobby 场景**：大厅是在 `Game.unity` 上盖的一层运行时界面。将来要做「大厅里选职业/看统计」这类需要场景配合的东西，得真的建一个 `Lobby.unity` 并加进 Build Settings。
2. **改键只覆盖 7 个按钮动作**，`Move` 的 WASD/方向键没做（2DVector composite 要拆成 4 个 part 分别重绑）。
3. **没有音频素材**，音乐/音效音量目前只存不用；主音量是真的接通了。
4. **暂停时游戏时间真的停了**：所有 `WaitForSeconds`、协程、动画都会停，这是有意设计的（`ScreenFader` 用的是 `WaitForSecondsRealtime`，所以暂停不影响淡入淡出）。
5. **存档在注册表（`PlayerPrefs`）**：换机器、清注册表就恢复默认绑定，不做云同步。
