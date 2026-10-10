# Day 6 配置步骤：HUD + 房间切换

> 今天的目标：**让一局游戏第一次有头有尾。**
> 具体是两件事：① 屏幕上终于有血条蓝条技能栏了；② 打完一间房，门会开，走进去就换下一间。
>
> 为什么先做这两件而不是技能树？
> 因为排期表里写着第 1 周的验收标准是「室友拿到 `.exe` 能自己玩 30 秒不问你怎么操作」——
> 现在连血量都看不见，他不知道自己还剩多少血。而且「一局完整的开始→结束循环」
> 是排期里那四条**永远不要砍**的东西之一，目前还是空的。

---

## 目录

- [第 0 节 动手之前](#第-0-节-动手之前)
- [第 1 节 把现在的场景改造成「一间房」（阶段 1）](#第-1-节-把现在的场景改造成一间房阶段-1)
- [第 2 节 挂 RoomManager（阶段 1 收尾）](#第-2-节-挂-roommanager阶段-1-收尾)
- [第 3 节 做第 2 间房（阶段 2）](#第-3-节-做第-2-间房阶段-2)
- [第 4 节 搭 HUD：血条与蓝条（阶段 3）](#第-4-节-搭-hud血条与蓝条阶段-3)
- [第 5 节 技能栏（阶段 4）](#第-5-节-技能栏阶段-4)
- [第 6 节 黑幕（换房间的过渡）](#第-6-节-黑幕换房间的过渡)
- [第 7 节 总验收清单](#第-7-节-总验收清单)
- [第 8 节 排错表](#第-8-节-排错表)
- [第 9 节 提交](#第-9-节-提交)
- [第 10 节 答辩时怎么说](#第-10-节-答辩时怎么说)
- [第 11 节 Day 7 预告](#第-11-节-day-7-预告)

---

# 第 0 节 动手之前

## 0.1 确认编译通过

切回 Unity，等它转完圈。看 **Console**（`Window → General → Console`）：

- **一个红色的 `error CS` 都不能有。**
- 黄色的 `[StatBar] ...` 警告现在可能有，那是**故意留的**，等会儿配好就没了。先不用管。

今天新增了 7 个脚本，它们在：

```
Assets/_Project/Scripts/UI/StatBar.cs
Assets/_Project/Scripts/UI/HUDController.cs
Assets/_Project/Scripts/UI/SkillBarUI.cs
Assets/_Project/Scripts/UI/ScreenFader.cs
Assets/_Project/Scripts/Roguelike/Room.cs
Assets/_Project/Scripts/Roguelike/Door.cs
Assets/_Project/Scripts/Roguelike/RoomManager.cs
```

在 Project 面板里逐个点一下，确认 Inspector 里能看到内容（不是「No MonoBehaviour scripts」）。

## 0.2 今天要拆场景，先把状态存一下

今天会把现在的场景**拆开重组**：地形和敌人要从场景里搬进一个「房间预制体」。
万一拆坏了想退回去，靠的是 git。所以先提交一次当前状态：

```powershell
cd D:\Roguelike2D
git add -A
git commit -m "chore: Day 5 收尾，提交前的存档点"
```

> ⚠️ 这时候 Unity 可能正在写 `.asset` 文件。如果 `git commit` 报
> `fatal: Unable to create '.../index.lock'`，等两秒再试一次就行。
> 更稳妥的做法是先在 Unity 里 `Ctrl+S` 存场景。

## 0.3 今天的四个阶段

**不要一口气全做完再测试。** 分成四段，每段做完都能跑、都能验收：

| 阶段 | 做什么 | 验收标准 |
|---|---|---|
| 1 | 房间预制体 + RoomManager（先只用 1 间房） | 按 Play，房间自动出现；把敌人打死，门从灰色变成青色 |
| 2 | 再做 1 间房 | 走进门 → 屏幕黑一下 → 出现在另一间房 |
| 3 | HUD 血条蓝条 | 血条会跟着掉血、蓝条会自己回 |
| 4 | 技能栏 + 黑幕 | 按 K 放技能，格子变灰然后慢慢亮回来 |

**每做完一段就 `Ctrl+S` + `git commit`。** 后面出问题才好退。

---

# 第 1 节 把现在的场景改造成「一间房」（阶段 1）

## 1.1 为什么要拆

现在场景里的东西是「一整个关卡」：3 块地面、1 个敌人、1 个地刺、1 个靶子，全都平铺在场景里。

但肉鸽需要**很多间房**，而且每次玩顺序还不一样。所以必须把「一间房长什么样」打包成一个**可以反复复制的东西** —— 也就是预制体（Prefab）。

打包之后，一个房间 = 一个预制体文件。做第 5 间房就是复制第 4 间改一改，不用重新摆一遍。

## 1.2 建房间的根物体

1. Hierarchy 面板空白处**右键** → `Create Empty`
2. 它会出现在最下面，**立刻按 `F2` 改名**成 `Room_01`
3. ★ 选中它，在 Inspector 的 **Transform** 里把三个数字都设成：

   ```
   Position   X 0    Y 0    Z 0
   ```

> ⚠️ **这一步不能省。**
> 预制体记录的是「子物体相对根物体的坐标」。根物体必须待在原点，
> 相对坐标才等于世界坐标，将来实例化到哪儿都不会错位。
> 这个坑要到第 2 间房放歪了才会暴露，那时候你已经想不起来是哪一步的问题了。

## 1.3 把地形和敌人拖进去

在 Hierarchy 里**按住 Ctrl 一个个点选**（不要框选，容易漏）：

```
Ground
Ground (1)
Ground (2)
Enemy
Spike
Dummy
```

然后**把这 6 个东西一起拖到 `Room_01` 上面**（拖到它身上松手）。

拖完之后，这 6 个应该变成 `Room_01` 的**子物体**，在 Hierarchy 里缩进一格。

> **怎么确认拖对了**：点一下 `Ground`，Inspector 里它的 Transform 的 **Position 应该还是 `(0, -3, 0)`** —— 数值没变。
> 因为根物体在原点，父子关系不改变世界坐标。
> 如果 Position 变成了别的数字，说明 `Room_01` 不在原点，回 1.2 重来。

**留在场景里不动的**（这三个不进房间）：

```
Player          ← 玩家会被 RoomManager 传来传去，不属于任何一间房
Main Camera     ← 它是 Player 的子物体，跟着走
RunManager      ← 一局游戏的管理者，跨房间存在
```

## 1.4 加一个出生点

玩家进新房间时要被放到一个固定位置。这个位置得写在房间里。

1. **右键点 `Room_01`** → `Create Empty`
2. 改名 `SpawnPoint`
3. Inspector → Transform → Position 设成 `X 0  Y 0  Z 0`

> 为什么出生点不直接写在 `Room` 脚本里、而是放个空物体？
> 因为「房间里站哪儿最舒服」是**摆出来的**，不是算出来的。
> 放个空物体，你就可以在 Scene 视图里拖着它调位置，所见即所得。

## 1.5 加一扇门

### 1.5.1 建门的显示

1. **右键点 `Room_01`** → `2D Object` → `Sprites` → `Square`
2. 改名 `Door`
3. Inspector → Transform：

   ```
   Position   X 14    Y 0.5    Z 0
   Scale      X 1     Y 4      Z 1
   ```

   > 为什么是 `Y 0.5` 和 `Scale Y 4`？
   > 门要盖住「地面顶面」到「跳不过去的高度」这一整段。
   > x = 14 那个位置的地面是 `Ground (1)`（中心 y = -2，高 1），所以地面顶面在 **y = -1.5**。
   > 门高 4，中心在 y = 0.5，于是它从 **y = -1.5 一直到 y = 2.5** —— 下沿正好贴地，上沿比玩家跳跃高度高。
   > 玩家跳跃高度约 2.4 格，所以跳不过去。

4. Inspector → **Sprite Renderer** → `Color` 随便设一个明显的颜色（比如橙红色），等会儿运行时会自动被脚本改成灰/青。
5. Inspector → **Layer** 下拉 → 选 `Ground`

   > 选 `Ground` 有两个作用：玩家的地面检测会把它当墙（其实无所谓），
   > 更重要的是**火球会撞在门上**（`Projectile` 的 `Obstacle Layers` 勾的就是 `Ground`），
   > 不然法师能隔着锁着的门把里面的敌人打死。

6. 应该已经自动带上一个 **`Box Collider 2D`**（`Square` 精灵默认自带）。如果没有，`Add Component` → 搜 `Box Collider 2D`。

   > 不用手动调碰撞体大小。**`Scale` 会自动放大 `Collider2D`**，
   > 所以 `Scale (1, 4, 1)` 加上默认 1×1 的碰撞体 = 1×4 的墙。

### 1.5.2 挂上门脚本

选中 `Door` → Inspector 最下面 `Add Component` → 搜 `Door` → 回车。

应该出现 **`Door`** 组件，里面这些字段：

| 字段 | 填什么 |
|---|---|
| **Room** | **留空**（脚本会自动往上找父物体上的 `Room`） |
| **Visual** | **留空**（脚本会自动找自己的 `SpriteRenderer`） |
| **Locked Color** | 保持默认（深灰蓝） |
| **Unlocked Color** | 保持默认（青色） |
| **Advance On Enter** | ✅ 勾上 |

> ⚠️ **`Box Collider 2D` 的 `Is Trigger` 现在是什么状态都无所谓**。
> `Door.Awake()` 会在游戏一开始把它强行设成「实心」，
> 因为门出生时一定是锁着的。你不用管这个勾。

## 1.6 给房间挂 Room 组件

选中 **`Room_01`**（根物体，不是 Door）→ `Add Component` → 搜 `Room`。

| 字段 | 填什么 |
|---|---|
| **Spawn Point** | 把 Hierarchy 里的 **`SpawnPoint`** 拖进来 |
| **Door** | 把 Hierarchy 里的 **`Door`** 拖进来 |
| **Require Clear** | ✅ 勾上 |

> 门的引用能不能留空让它自己找？
> 能（`Room.Awake` 会 `GetComponentInChildren<Door>`），但**建议手动拖**。
> 因为手动拖的话，将来房间里有两扇门（左门右门）时你知道哪扇是哪扇；
> 自动找只会拿到第一个，而且不报错。

## 1.7 存成预制体

1. 在 Project 面板里确认有这么个文件夹：`Assets/_Project/Prefabs/Rooms/`
   没有就右键 `Prefabs` → `Create` → `Folder`，改名 `Rooms`
2. 把 Hierarchy 里的 **`Room_01`** 拖到 Project 面板的 `Rooms` 文件夹里
3. 松手后会弹一个框问你要 `Original Prefab` 还是 `Prefab Variant` → 选 **`Original Prefab`**
4. Project 面板里出现 `Room_01`（蓝色方块图标 = 预制体）
5. ★ **把 Hierarchy 里的 `Room_01` 删掉**（选中它按 `Delete`）

> **最后这步很多人不敢做，但必须做。**
> 因为 `RoomManager` 会在游戏开始时**自己生成**房间。
> 如果场景里还留着一个手摆的 `Room_01`，你会看到两个重叠的房间，
> 而且 `RoomManager` 找不到「自己生成的那个」，玩家会出生在错误的位置。
>
> 删掉之后，Hierarchy 里应该只剩下：
> ```
> Player
>   └ Main Camera
> RunManager
> ```
> 空荡荡的，这是**对的**。房间马上就会由脚本生成出来。

## 1.8 阶段 1 检查

现在按 `Play`，因为还没挂 `RoomManager`，**应该什么都看不到，只有一个玩家在往下掉**。

这是正常的。下一步就把它接上。

---

# 第 2 节 挂 RoomManager（阶段 1 收尾）

## 2.1 建物体

Hierarchy 空白处右键 → `Create Empty` → 改名 `RoomManager` → Position 设 `(0, 0, 0)`。

## 2.2 挂脚本

选中它 → `Add Component` → 搜 `RoomManager`。

| 字段 | 填什么 |
|---|---|
| **Room Prefabs** | 点 `+` 加一格 → 把 Project 里的 `Room_01` 预制体拖进来 |
| **Room Anchor** | **留空**（留空就用 RoomManager 自己的位置，也就是原点） |
| **Fader** | **先留空**（第 6 节做好黑幕再回来填） |
| **Use Bag Random** | ✅ 勾上 |
| **Build First Room On Start** | ✅ 勾上 |
| **Cleanup Projectiles** | ✅ 勾上 |

## 2.3 阶段 1 验收

按 `Play`。应该发生：

1. **`Room_01` 自动出现在 Hierarchy 里**，名字就叫 `Room_01`（没有 `(Clone)` 后缀 —— 脚本把后缀去掉了）
2. 玩家掉到地面上，敌人从右边冲过来打你
3. **门是深灰蓝色的**，撞上去过不去
4. **把敌人打死** → 门「唰」地变成**青色**
5. 走进青色的门 → 因为还没配黑幕，会看到场景「啪」地重置一下，然后又回到同一个房间（房间池里只有 1 间，抽来抽去都是它）

第 5 步是**对的**，不是 bug。

### 阶段 1 提交

```powershell
cd D:\Roguelike2D
git add -A
git commit -m "feat(room): 房间预制体与房间流水线，清场开门换房"
```

---

# 第 3 节 做第 2 间房（阶段 2）

## 3.1 复制

1. Project 面板 → `Assets/_Project/Prefabs/Rooms/`
2. 点一下 `Room_01`，按 **`Ctrl + D`**（Duplicate）
3. 出现 `Room_01 1`，**`F2`** 改名成 `Room_02`

## 3.2 进预制体编辑模式改它

**双击** `Room_02`。Unity 会切进一个蓝色的「预制体隔离视图」，Hierarchy 顶上显示 `Room_02`。

现在改两处，让玩家一眼能看出这是「另一间房」：

### 改动 A：把门挪远一点

选中 `Door`，Transform → Position 改成：

```
X 22    Y 0.5    Z 0
```

> x = 22 那个位置的地面还是 `Ground (1)`（覆盖 x 从 10 到 30），顶面仍是 y = -1.5，
> 所以门的 `Y 0.5`、`Scale Y 4` 不用改。

### 改动 B：多放一个敌人

1. 在 Hierarchy 里点选 **`Enemy`**，按 **`Ctrl + D`** 复制一份
2. 改名 `Enemy (2)`
3. Inspector → Transform → Position 改成：

   ```
   X 18    Y -1    Z 0
   ```

> **不是必须叫 `Enemy (2)`**，名字随便。`Room` 脚本是按「有没有 `EnemyController` 组件」来找敌人的，
> 不看名字。所以复制出来的敌人**自动算数**，你不用做任何额外配置。这是刻意设计的。

### 改动 C（可选，但很值）

把两块地面的 `Sprite Renderer → Color` 调成不同的色调。
将来换成真正的美术素材时这一步就自动没了，但现在阶段「**看得出这是另一间房**」是刚需，
否则验收时你自己都分不清有没有换成功。

改完按 Hierarchy 左上角的 **`<`** 箭头退出预制体模式（或者直接双击场景文件）。

## 3.3 加进房间池

选中 Hierarchy 里的 `RoomManager` → 把 Project 里的 **`Room_02` 预制体拖到 `Room Prefabs` 列表的 `+` 上**，
或者点 `+` 加一格再拖。

列表里现在应该有 2 项。

## 3.4 阶段 2 验收

按 `Play`，打死敌人，进门。这次应该：

1. 场景「啪」一下重置
2. **新房间里门的位置不一样了、敌人多了一个** → 说明换房成功了
3. 再打一次，又回到第 1 间

> **袋子随机（bag randomization）是怎么回事**：
> 它不是「每次从房间里随机抽一个」——那样会连着两次抽到同一间，玩家会觉得游戏坏了。
> 它是「把 2 个房间放进一个袋子，打乱，一个一个摸出来，摸完了重新装袋」。
> 所以**两轮之内每个房间必然各出现一次**，而且脚本还额外做了「队首不能和上一轮队尾相同」的处理。
> 这套东西以后加第 3、第 4 间房都不用改代码。

### 阶段 2 提交

```powershell
git add -A
git commit -m "feat(room): 第二间房与袋子随机抽取"
```

---

# 第 4 节 搭 HUD：血条与蓝条（阶段 3）

## 4.1 建 Canvas

Hierarchy 空白处右键 → **`UI (Canvas)`** → 子菜单**最下面、分隔线之后**的那个 **`画布`**（英文界面里是 `UI` → `Canvas`）。

> ⚠️ **别点上面那一串 `图像` / `文本 - TextMeshPro` / `按钮`。**
> 它们是「快速建 UI 元素」的快捷方式 —— 点了 Unity 会**顺手帮你建一个 Canvas**。
> 东西是能用，但 Canvas 的参数是它替你写的默认值，回头还得改一遍。
> 我们现在只要一个空 Canvas，所以点最下面的 **`画布`**。

Unity 会一次性生成两个东西：

```
Canvas          ← 所有 UI 的容器
EventSystem     ← 处理点击事件的，现在用不上但留着不碍事
```

选中 `Canvas`，在 Inspector 里找到 **`Canvas Scaler`** 组件（不是 `Canvas` 组件），设成：

| 字段 | 值 | 为什么 |
|---|---|---|
| **UI Scale Mode** | `Scale With Screen Size` | 屏幕变大变小，UI 按比例缩放 |
| **Reference Resolution** | `X 1920` `Y 1080` | 以 1080p 为基准 |
| **Screen Match Mode** | `Match Width Or Height` | |
| **Match** | `0.5` | 宽高各占一半权重 |

> ⚠️ **不设 `Canvas Scaler` 的话，UI 会按像素写死。**
> 在 1080p 上看着正好，室友用 4K 显示器一开就变成左上角一小坨。
> 这个设置是「一次性配对，一辈子受益」。

## 4.2 血条

### 4.2.1 背景

1. **右键点 `Canvas`** → `UI` → `Image`
2. 改名 `HealthBar`
3. Inspector → **Rect Transform**，点**最左边那个画着小锚点符号的暗色方框**（它横跨「位置」和
   「宽度/高度」两行）—— 那就是 **Anchor Presets（锚点预设）** 按钮，点了会弹出九宫格。

   **按住 `Shift` 和 `Alt` 一起点九宫格的左上角那格**（这样锚点、轴心、位置一次设好）。

   > ⚠️ **别点右边那个 `R`。** 那是 Raw edit mode（直接编辑锚点数值），和预设没关系。
   > Unity 6 把 Rect Transform 面板改版过了，预设按钮从右上角挪到了**左边**，长得像个预览小方块。

   然后填数字：

   | 字段 | 值 |
   |---|---|
   | **Pos X** | `40` |
   | **Pos Y** | `-40` |
   | **Width** | `420` |
   | **Height** | `26` |

   > **Anchor / Pivot / Pos 这三个东西的关系**，一句话讲清：
   > **Anchor（锚点）**决定「你跟着父物体的哪个角走」——锚在左上角，父物体变形时你就跟着左上角。
   > **Pivot（轴心）**决定「Pos X/Y 是从你身上的哪个点量出去的」。
   > 两个都设在左上角时，`Pos X = 40, Pos Y = -40` 就等于「离屏幕左上角往右 40、往下 40」。
   >
   > **不想点那个九宫格也行**，直接在 Inspector 里手填：
   > `Anchor Min = (0, 1)`、`Anchor Max = (0, 1)`、`Pivot = (0, 1)`，然后填 Pos 和 Width/Height。
   > 给数字最不会出错。

4. Inspector → **Image** 组件 → `Color` 点开，设成深灰：`R 38` `G 38` `B 46` `A 230`

### 4.2.2 填充条

1. **右键点 `HealthBar`**（刚才那个）→ `UI` → `Image`
2. 改名 `HealthFill`
3. Rect Transform 设成**铺满父物体**：

   | 字段 | 值 |
   |---|---|
   | **Anchor Min** | `X 0` `Y 0` |
   | **Anchor Max** | `X 1` `Y 1` |
   | **Left / Top / Right / Bottom** | 全部 `0` |

   > 想快点的话：点**左边那个锚点预设方框**，**按住 `Alt`** 点九宫格最右下角那个「拉伸」图标，
   > 然后四个边距填 0。

4. ★★ Inspector → **Image** 组件，设成：

   | 字段 | 值 |
   |---|---|
   | **Source Image** | `UISprite`（保持默认） |
   | **Color** | 红色：`R 214` `G 63` `B 63` `A 255` |
   | **Image Type** | **`Filled`** ← 改成这个！ |
   | **Fill Method** | `Horizontal` |
   | **Fill Origin** | `Left` |
   | **Fill Amount** | `1` |

   > ★★★ **`Image Type` 必须是 `Filled`，这是今天最容易翻车的一步。**
   > 它是 `Simple` 的时候，`fillAmount` 这个数字**完全不起作用** ——
   > 不报错、不警告，血条永远满格。你会以为是自己代码写错了，然后翻半天代码。
   > 所以 `StatBar.cs` 里专门写了 `OnValidate` 检查它，配错了 Console 会喊你。

### 4.2.3 挂 StatBar

1. 选中 **`HealthBar`**（背景那个）→ `Add Component` → 搜 `StatBar`
2. 把 **`HealthFill`** 从 Hierarchy 拖到 **`Fill`** 槽里
3. `Smooth` ✅ 勾着
4. `Catch Up Speed` 填 `4`

> 为什么组件挂在背景上、图却是子物体？
> 因为这样背景框永远在，只有里面的填充条会缩短 —— 这是血条的标准做法。
> 如果你把组件挂在 `HealthFill` 上也能跑，但那样代码就没法区分「框」和「条」了。

## 4.3 蓝条

一模一样再来一遍，改 4 个地方：

| 步骤 | 和血条不同的地方 |
|---|---|
| 建背景 → 改名 | `ManaBar` |
| 位置 | **Pos X `40`、Pos Y `-74`**（在血条下面） |
| 尺寸 | **Width `320`、Height `18`**（比血条短一点、细一点，视觉上分主次） |
| 填充色 | 蓝色：`R 66` `G 133` `B 244` `A 255` |

填充条（`ManaFill`）的做法、`Image Type = Filled` 的要求、挂 `StatBar`，全部和血条一样。

## 4.4 挂 HUDController

1. 选中 **`Canvas`** → `Add Component` → 搜 `HUDController`
2. 填：

   | 字段 | 填什么 |
   |---|---|
   | **Player Health** | **留空**（脚本自动找 Tag 为 `Player` 的对象） |
   | **Player Mana** | **留空** |
   | **Health Bar** | 把 `HealthBar` 拖进来 |
   | **Mana Bar** | 把 `ManaBar` 拖进来 |
   | **Hide Bars On Death** | ✅ 勾上 |

> 为什么 `Player Health` 建议留空？
> 因为玩家是场景里的对象，而 Canvas 也是场景里的对象，手动拖一次是没问题。
> 但**留空 + 自动查找**的好处是：将来做「玩家在房间里重生」时，
> 如果 `Player` 被重新生成了一份，手动拖的那个引用就指向已销毁的旧对象了（会变成 `None`，血条从此不动）。
> 自动查找每次都能找到当前这个。
>
> `HUDController` 的查找写在 `Awake`，并且每帧检查一次「引用还在不在」，不在就重找。

## 4.5 阶段 3 验收

按 `Play`：

1. **左上角出现一条红色血条和一条蓝色蓝条**
2. 让敌人打你一下 → **血条缩短**（不是「啪」地一下，而是快速地滑过去）
3. **蓝条会自己慢慢涨**（`ManaPool` 每帧回蓝）
4. 用法师按 `K` 放火球 → **蓝条掉一截**，然后慢慢回
5. 被打死 → **血条蓝条一起消失**，1.5 秒后场景重开、又出现

如果血条永远是满的、不跟着掉 → 直接跳到[第 8 节 排错表](#第-8-节-排错表)看第一条。

### 阶段 3 提交

```powershell
git add -A
git commit -m "feat(ui): 血条蓝条 HUD"
```

---

# 第 5 节 技能栏（阶段 4）

## 5.1 建容器

1. 右键点 `Canvas` → `UI` → `Image`
2. 改名 `SkillBar`
3. Rect Transform：

   | 字段 | 值 |
   |---|---|
   | **Anchor Min / Max** | `X 0` `Y 0`（左下角） |
   | **Pivot** | `X 0` `Y 0` |
   | **Pos X** | `40` |
   | **Pos Y** | `40` |
   | **Width** | `312` |
   | **Height** | `72` |

4. Inspector → **Image** 组件：`Color` 的 **`A` 改成 `0`**（完全透明），并且**取消勾选 `Raycast Target`**

   > 它只是个「把 4 个格子装在一起」的容器，不需要看得见。
   > 关掉 `Raycast Target` 是因为透明的 UI 图**照样会挡住鼠标射线**，
   > 将来做背包 / 暂停菜单时会莫名其妙点不动 —— 现在顺手关掉。

## 5.2 建 4 个格子

每个格子是「一张图标 + 盖在上面的一层冷却遮罩」。

### 格子 1

1. 右键点 `SkillBar` → `UI` → `Image` → 改名 `SkillSlot_1`
2. Rect Transform：

   | 字段 | 值 |
   |---|---|
   | **Anchor Min / Max** | `X 0` `Y 0` |
   | **Pivot** | `X 0` `Y 0` |
   | **Pos X** | `0` |
   | **Pos Y** | `0` |
   | **Width / Height** | `72` / `72` |

3. Image 组件：`Color` 白色，**取消勾选 `Raycast Target`**

### 格子 1 的冷却遮罩

1. 右键点 `SkillSlot_1` → `UI` → `Image` → 改名 `Cooldown`
2. Rect Transform 铺满父物体：`Anchor Min (0,0)`、`Anchor Max (1,1)`、四个边距全 `0`
3. Image 组件：

   | 字段 | 值 |
   |---|---|
   | **Color** | 黑色，`A` 改成 `170`（半透明，能看见下面的图标） |
   | **Image Type** | **`Filled`** ← 又是这个！ |
   | **Fill Method** | **`Radial 360`** |
   | **Fill Origin** | **`Top`** |
   | **Clockwise** | ✅ 勾上 |
   | **Fill Amount** | `0` |

   > 用 `Radial 360`（扇形）而不是横向，是因为技能的冷却读起来像「转一圈」，
   > 比一条横线更符合直觉 —— 这是绝大多数游戏的做法。
   > `Fill Amount = 0` 表示「完全没被遮住」= 冷却好了。

### 格子 2 / 3 / 4

选中 `SkillSlot_1`，按 **`Ctrl + D`** 复制三次，然后只改 **`Pos X`**：

| 物体 | Pos X |
|---|---|
| `SkillSlot_1` | `0` |
| `SkillSlot_2` | `80` |
| `SkillSlot_3` | `160` |
| `SkillSlot_4` | `240` |

> 复制的时候子物体（`Cooldown`）会跟着一起复制，不用重新搭。
> `80 = 72(格子宽) + 8(间距)`，`240 + 72 = 312` 正好等于容器的宽度。

## 5.3 挂 SkillBarUI

1. 选中 **`Canvas`** → `Add Component` → 搜 `SkillBarUI`
2. 填：

   | 字段 | 填什么 |
   |---|---|
   | **Caster** | **留空**（自动找 Player 上的 `SkillCaster`） |
   | **Slots** | 点 `+` **四次**，做成 4 行 |

3. 逐行填 `Slots`（**顺序不能错，第 1 行对应 K 键**）：

   | 行 | Icon | Cooldown Mask |
   |---|---|---|
   | **Element 0** | `SkillSlot_1`（它自己的 Image） | `SkillSlot_1` 下面的 `Cooldown` |
   | **Element 1** | `SkillSlot_2` | `SkillSlot_2` 下面的 `Cooldown` |
   | **Element 2** | `SkillSlot_3` | `SkillSlot_3` 下面的 `Cooldown` |
   | **Element 3** | `SkillSlot_4` | `SkillSlot_4` 下面的 `Cooldown` |

   > 拖 `Icon` 的时候注意：**要拖 `SkillSlot_1` 这个物体本身**（它的 Image 就是图标）。
   > 拖 `Cooldown Mask` 的时候要拖它**下面那一层** `Cooldown`。
   > 拖错的话最明显的表现是「冷却遮罩不动」，因为脚本在改一个不该改的东西的 `fillAmount`。

## 5.4 给技能配图标（可选但强烈建议）

现在技能还没有图标，格子会显示成白色方块。

1. Project 面板 → `Assets/_Project/Art/UI/`（没有就建一个）
2. 找两张对比强烈的图。**现在完全不用画**，随便截两张纯色图或者用 Unity 自带的都行。
3. 选中图片 → Inspector → `Texture Type` 改成 **`Sprite (2D and UI)`** → `Apply`
4. Project 面板 → `Assets/_Project/Data/Skills/` → 点 `Skill_Fireball`
5. Inspector → **`Icon`** 槽 → 把那张图拖进去
6. `Skill_Whirlwind` 同样操作

> **图标不是必需项**。不配的话格子是白方块，功能完全正常。
> 现在这个阶段「能一眼看出冷却在转」比「图标好不好看」重要得多。

## 5.5 阶段 4 验收

按 `Play`，用**战士**（`Class_Warrior`）：

1. **左下角出现 4 个格子，第 1 个里有图标，后 3 个不见了** —— 对，战士只有 1 个技能（旋风斩）
2. 按 **`K`** → 放旋风斩，同时第 1 格**被扇形遮罩盖满**，然后**逆时针慢慢退掉**
3. 冷却没走完的时候图标是**灰的**；走完了变**亮**
4. 蓝不够的时候**也是灰的**（`IsReady` 同时检查冷却和法力）

再切**法师**（把 `Class_Mage` 拖到 Player 的 `Character Stats → Class Data`）：

5. 应该只看到**第 1 格有图标**，按 `K` 发火球

### 阶段 4 提交

```powershell
git add -A
git commit -m "feat(ui): 技能栏与冷却显示"
```

---

# 第 6 节 黑幕（换房间的过渡）

现在换房间是「啪」地一下，很廉价。加个黑幕遮盖，观感立刻不一样。

## 6.1 建黑幕图

1. 右键点 `Canvas` → `UI` → `Image`
2. 改名 `ScreenFade`
3. Rect Transform 铺满整个屏幕：

   | 字段 | 值 |
   |---|---|
   | **Anchor Min** | `X 0` `Y 0` |
   | **Anchor Max** | `X 1` `Y 1` |
   | **Left / Top / Right / Bottom** | 全 `0` |

4. Image 组件：`Color` = 纯黑（`R 0` `G 0` `B 0` `A 255`），**取消勾选 `Raycast Target`**
5. ★ **把它拖到 Hierarchy 里 `Canvas` 的最后一个子物体位置**

   > UI 的绘制顺序 = Hierarchy 里的顺序，**越靠下越晚画 = 盖在越上面**。
   > 黑幕必须盖住血条和技能栏，所以它得排在最后。
   > 将来做暂停菜单，也是拖到最后。

## 6.2 挂 ScreenFader

★ **选中 `Canvas`**（不是 `ScreenFade`！）→ `Add Component` → 搜 `ScreenFader`。

| 字段 | 填什么 |
|---|---|
| **Overlay** | 把 Hierarchy 里的 **`ScreenFade`** 拖进来 |
| **Fade Out Duration** | `0.22` |
| **Fade In Duration** | `0.28` |
| **Hold Duration** | `0.05` |

> ⚠️⚠️ **这个组件必须挂在 `Canvas` 上，不能挂在 `ScreenFade` 自己身上。**
>
> 原因是 `ScreenFader` 为了不让透明的图挡住点击，**会在变透明之后把那张图关掉**
> （`SetActive(false)`）。如果组件就挂在那一张图上，它等于把自己也关掉了 ——
> **正在跑的协程会被一起停掉**，表现是「黑幕淡出到一半卡住，游戏再也点不动」。
>
> 代码里已经加了一层保护（检测到「组件和目标图是同一个物体」就不关它），
> 但正确做法还是挂在 `Canvas` 上。

## 6.3 把 Fader 接给 RoomManager

选中 Hierarchy 里的 **`RoomManager`** → 把 `Canvas` 上的 **`ScreenFader`** 组件拖到 **`Fader`** 槽里。

> 拖的时候注意拖的是**组件**，不是物体。
> 从 Canvas 的 Inspector 里拖 `ScreenFader` 标题那几个字。

## 6.4 最终验收

按 `Play`，完整走一遍：

1. 房间自动生成，玩家站在出生点
2. 打死敌人 → 门变青
3. 走进门 → **屏幕黑一下**（大约 0.2 秒）→ 亮起来时已经在另一间房
4. 血条蓝条在这期间**一直显示在最上面**（没被黑幕盖住？被盖住也对，反正是全黑的）

---

# 第 7 节 总验收清单

逐条打勾。**任何一条不过，就不要往下做 Day 7。**

- [ ] Console 里没有红色 `error CS`
- [ ] Console 里没有黄色的 `[StatBar]` 或 `[SkillBarUI]` 警告
- [ ] 按 Play，`Room_01` 自动出现在 Hierarchy 里
- [ ] 玩家掉到地上，敌人会从右边冲过来
- [ ] 门一开始是深灰蓝色的，撞上去过不去
- [ ] 打死敌人后门变成青色
- [ ] 走进门 → 黑屏 → 出现在另一间房（门的位置和敌人数量明显不同）
- [ ] 左上角有血条（红）和蓝条（蓝）
- [ ] 被打一下，血条平滑地缩短（不是瞬间跳）
- [ ] 放技能消耗法力，蓝条掉一截然后慢慢回
- [ ] 左下角有 4 个技能格，有技能的那格显示图标
- [ ] 放完技能，格子上出现扇形遮罩并慢慢转掉
- [ ] 冷却中 / 蓝不够时图标是灰的
- [ ] 被打死后血条蓝条消失，1.5 秒后场景重开

---

# 第 8 节 排错表

| 现象 | 真正的原因 | 怎么修 |
|---|---|---|
| **血条永远是满的，掉血也不动** | `HealthFill` 的 `Image Type` 不是 `Filled`。它在 `Simple` 模式下 `fillAmount` 完全无效，而且不报错 | 选 `HealthFill` → Image → `Image Type = Filled`、`Fill Method = Horizontal`、`Fill Origin = Left` |
| **Console 一直刷 `[StatBar] ... 不是 Filled`** | 同上，这是脚本故意在喊你 | 同上 |
| **技能格子里的遮罩不转** | `Cooldown` 的 `Image Type` 不是 `Filled` | `Image Type = Filled`、`Fill Method = Radial 360`、`Fill Origin = Top` |
| **Console 刷 `[SkillBarUI] 第 N 格...`** | 同上 | 同上 |
| **UI 在 Game 视图里完全看不见** | ① `Canvas` 被禁用了 ② 血条被摆到了屏幕外面 ③ `Rect Transform` 的 `Scale` 是 0 | 选中 `HealthBar`，在 Scene 视图里按 `F` 聚焦看它在哪；或者直接把 `Pos X/Y` 改成 `40 / -40` |
| **血条位置对了但技能栏不见了** | `SkillBar` 的 `Image → Color → A` 是 0，本来就看不见 —— 要看的是它**下面的 4 个格子** | 检查 `SkillSlot_1` 到 `_4` 是不是 `SkillBar` 的子物体 |
| **4 个格子全都不显示** | `SkillBarUI` 的 `Slots` 没填，或者 `Caster` 找不到 | ① 检查 Canvas 上的 `SkillBarUI` 里 `Slots` 有没有 4 行 ② 确认 `Player` 上有 `Skill Caster` 组件 ③ 确认 Player 的 Tag 是 `Player` |
| **放技能了但格子没反应** | `Slots` 里的 `Icon` 拖成了 `Cooldown`，或者顺序错位 | 重拖一遍，注意 `Element 0` 对应 `SkillSlot_1` |
| **按 Play 后场景里什么都没有** | `RoomManager` 没挂，或者 `Room Prefabs` 是空的 | 选中 `RoomManager` → `Room Prefabs` 至少要有 1 项 |
| **场景里出现了两个重叠的房间** | 你把 `Room_01` 存成预制体之后**没有从 Hierarchy 里删掉** | 删掉 Hierarchy 里的那个 `Room_01` |
| **玩家出生在奇怪的位置** | `Room_01` 的根物体不在 `(0,0,0)`，导致子物体相对坐标错位 | 双击 `Room_01.prefab` 进预制体模式 → 看根物体的 Transform 是不是 `(0,0,0)` |
| **门永远是灰的，敌人死光了也不开** | ① 敌人没有被登记（不是 `Room` 的子物体）② `Health.Died` 没触发，或者那个「敌人」其实是你复制出来但没挂 `Health` 的靶子 | 在 Play 模式下选中 `Room_01`，看 Console 打印的 `[Room] Room_01：敌人 N 个`，N 是不是等于房间里敌人的实际数量 |
| **反过来：敌人还活着，门却能碰，一碰就传回出生点**（2026/10/8 已修） | `Room.Awake()` 里就数了敌人，可 `Health.Current` 要到 `Health.Awake()` 才被赋值。`Room` 在房间根物体上、`Enemy` 是子物体，**根物体的 `Awake` 先跑** → 敌人全被判成「已死」→ `AliveEnemies = 0` → `Start()` 当场开门 | 用修好的 `Room.cs`（`RecountAlive()` 已从 `Awake` 挪进 `Start`）。判据：Console 里 `[Room] Room_01：敌人 1 个，门锁着` |
| **门是青色的，但走进去没反应** | `Door` 的 `Box Collider 2D` 的 `Is Trigger` 没被脚本接管，或者 Player 的 Tag 不是 `Player` | 确认 `Player` 的 Tag 是 `Player`；`Door` 上的 `Advance On Enter` 是勾的 |
| **换房间时黑幕卡在半黑不动了** | `ScreenFader` 组件挂在了 `ScreenFade` 那张图上（见 6.2 的警告） | 把 `ScreenFader` 组件从 `ScreenFade` 上删掉，改挂到 `Canvas` 上 |
| **换房间后上一间的火球跟着飞过来** | `RoomManager` 的 `Cleanup Projectiles` 没勾 | 勾上 |
| **换房间后被弹飞出去** | 玩家的速度没清零 | 确认用的是本次提供的 `RoomManager.cs`（`MovePlayerToSpawn` 里有 `linearVelocity = Vector2.zero`） |
| **`Door` 组件上 `Room` 槽是 `None`，Console 报「没有 RoomManager」** | `Door` 所在物体的父级链里没有 `Room` 组件 | 确认 `Door` 是 `Room_01` 的子物体，且 `Room_01` 上挂了 `Room` |

---

# 第 9 节 提交

## 9.1 先看改了什么

```powershell
cd D:\Roguelike2D
git status --short
```

应该看到（大致）：

```
 M Assets/_Project/Scenes/Game.unity
 M Assets/_Project/Scripts/...（如果有手工微调）
?? Assets/_Project/Scripts/UI/            ← 4 个新脚本
?? Assets/_Project/Scripts/Roguelike/     ← 3 个新脚本
?? Assets/_Project/Prefabs/Rooms/         ← 房间预制体
```

## 9.2 检查有没有漏掉 `.meta`

★ **每次新增文件之后都要检查一遍。** `.meta` 记录 GUID，漏提交的话别人克隆下来引用全断。

```powershell
git status --short | Select-String "\.meta"
```

应该能看到每个新文件旁边都有对应的 `.meta`。

## 9.3 检查有没有违规的大文件

```powershell
git diff --cached --stat | Select-Object -Last 5
git status --short | Select-String "Library|Temp|obj"
```

第二条**应该什么都不输出**。有输出说明 `.gitignore` 失效了。

## 9.4 提交

```powershell
git add -A
git commit -m "feat: HUD 血条蓝条技能栏 + 房间流水线与黑幕过渡"
git push
```

> 顺便：`fa0d09e`（那 15 份脚本详解文档）**还没 push**。这次 `git push` 会一起带上。
> 推完去 GitHub 上看一眼，确认仓库里有 `docs/06-脚本详解/` 这一整个文件夹。

---

# 第 10 节 答辩时怎么说

今天做的东西在答辩里能撑起**两页 PPT**，而且都是「有设计含量」的那种，不是「我做了个血条」。

## 10.1 「你是怎么让 UI 更新的？」

> **逻辑用事件，显示用轮询。**
> 伤害结算、死亡判定这些必须精确发生一次的东西走 C# 事件；
> 血条蓝条这种「只是把当前状态画出来」的东西，每帧直接去问 `Health.Normalized`。
> 反过来的代价更大：漏订阅一个事件，血条就永远不动，而且不报错 —— 这种 bug 很难查。
> 每帧读两个 `float` 的成本可以忽略。

**为什么这是个好回答**：它显示你想过「什么该用事件、什么该用轮询」，而不是无脑全套事件。

## 10.2 「房间是怎么随机出来的？」

> **袋子随机（bag randomization）。**
> 不是每次独立随机抽一个 —— 那样有 1/N 的概率连着抽到同一间，玩家的体感是「游戏卡住了」。
> 我把所有房间放进一个袋子、洗牌、一个一个摸出来，摸完再重新装袋，
> 并且额外保证「新袋子的第一个不能是上一轮的最后一个」。
> 这样 N 轮之内每个房间必然各出现一次，玩家的体感是「内容很丰富」。

**为什么这是个好回答**：这是一个**具体的、有名有姓的算法**，而不是「我用了 `Random.Range`」。
面试官会立刻知道你做过游戏。

## 10.3 「为什么门不用脚本拦住玩家？」

> 门在锁着的时候，它的 `Collider2D` 是**实心**的 —— 它物理上就是一面墙。
> 清场之后脚本把 `isTrigger` 改成 `true`，它才变成能穿过去的触发器。
> 用实体碰撞而不是「脚本判断玩家想不想过去」，是因为**物理事实没法被绕过**，
> 也不需要每帧去检查玩家是不是在偷跑。少一个每帧检查，也少一类 bug。

## 10.4 「你这个架构以后怎么扩展？」

按这个顺序讲：

1. **加房间**：做一个新预制体，拖进 `Room Prefabs` 列表。**一行代码都不用写。**
2. **加技能**：右键新建一个 `SkillData` + 一个 `AttackData`，也都是资产。**一行代码都不用写。**
3. **加职业**：右键新建一个 `ClassData`。
4. **加新的施放方式**（比如「朝天上召唤一道雷」）：在 `SkillCastType` 里加一项，
   在 `SkillCaster.Cast()` 里加一个 `case`。**改两处，而且编译器会告诉你哪里没改全。**

> 这四句话是今天最值钱的答辩素材。它证明的不是「我会写代码」，
> 而是「**我知道什么东西该做成数据、什么东西才该做成代码**」。

---

# 第 11 节 Day 7 预告

今天的成果让项目**第一次有了完整的骨架**：

```
一局游戏 = 开始 → 打房间 1 → 打房间 2 → 打房间 3 → ... → 死 → 重开
```

但中间少了一环：**打完一间的奖励**。

Day 7 要做的是**通关三选一强化** —— 打完敌人之后弹出三个词条，选一个，立刻生效。
这是肉鸽的灵魂，也是排期表里「第 2 周 = 肉鸽骨架」的核心。

好消息是：今天做的 `RoomManager.RoomChanged` 事件就是它的挂载点 ——
「换房间之前先弹强化面板」只需要订阅这个事件。

另外今天还欠着两笔账，**不急但别忘**：

1. `docs/06-脚本详解/` 里 Day 5 的 7 个新脚本还没有详解文档
 （`ManaPool` / `SkillData` / `SkillCaster` / `Projectile` / `DamageCalculator` / `ProjectileLauncher` / `CharacterVisual`），
  而且 `09-MeleeAttacker.md` 要改名成 `09-PlayerAttacker.md`
2. `Data/classes/` 这个文件夹名是小写的，和 `Data/Weapons/` 风格不一致，抽空改成 `Classes`
