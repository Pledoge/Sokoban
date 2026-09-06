# 02 · 技术设计文档（TDD）

> 本文档约定实现层的数据结构、算法、模块与接口。命名以本文档为准。
>
> **版本**：v3（2026-09-06）—— 编辑器双形态 / 混合求解器 / 平台适配 / UI 走 UI Builder。

## 1. 引擎与依赖

- Unity 2022.3 LTS（工程当前 `2022.3.62f3c1`）
- UGUI + TextMeshPro
- 脚本：纯 C#，不依赖第三方 package
- 序列化：`JsonUtility`（关卡 JSON）
- 资源加载：`Resources.Load`（关卡文件）+ `Addressables` 可选（美术贴图过多时再切）
- 不用热重载，不用 IL2CPP 限制（编辑器跑通即可）

**v3 新增**：

| 项 | 说明 |
|---|---|
| 目标平台 | PC（主）+ Android / iOS（次）。Unity 需装对应 Build Support |
| UI 产出 | 走 **UI Builder**（`Sokoban/UITools/UIBuilder/index.html` 网页排版 → `Assets/Editor/UIBuilder/UIBuilderImporter.cs` 导入 prefab）。详见 [03-美术资源文档 §4](./03-美术资源文档(ArtBible).md) |
| 中文字体 | 移动端 / WebGL 需放 `Assets/Resources/UI/UIBuilderFont.ttf`（UI Builder 导入器要求） |
| 移动端输入 | 不引第三方手势库，自己写 `SwipeInput`（约 60 行） |

## 2. 数据结构

### 2.1 单元格编码（关卡静态层）

```csharp
public enum CellType : int {
    Empty         = 0, // 空格（地板）
    Wall          = 1, // 外墙 / 内墙
    Obstacle      = 2, // 障碍物（不可推，只挡路）
    Goal          = 3, // 终点
    Box           = 4, // 木箱（占位美术）
    BoxOnGoal     = 5, // 木箱已就位（隐式运行态，不存盘）
    Player        = 6, // 玩家位置（隐式运行态，不存盘）
    PlayerOnGoal  = 7, // 玩家站在终点（隐式运行态，不存盘）
    Enemy         = 8, // 拓展模式敌人（隐式运行态，不存盘）
    EnemyOnGoal   = 9, // 拓展模式敌人站在终点（隐式运行态，不存盘）
}
```

> **关键设计**：玩家、敌人、「箱子在终点」都属于**运行时态**。关卡文件只存「地板 / 墙 / 障碍物 / 终点 / 箱子静态位置」与「玩家出生点 / 敌人出生点（可选）」。
>
> 运行态通过 `WorldState`（详见 §2.3）单独管理，避免 JSON 解析时把运行态写回文件。

### 2.2 `LevelData`（关卡静态数据）

```csharp
[Serializable]
public class LevelData {
    public string id;          // "ext_01"
    public string name;        // 中文显示名
    public Mode   mode;        // Classic / Extended
    public int    width;
    public int    height;
    public string author;      // 编辑器写入
    public int[][] cells;      // cells[y][x]，范围 [0, height) × [0, width)
    public Vec2Int playerSpawn;   // 经典模式可省略
    public Vec2Int enemySpawn;    // 拓展模式可省略
}

public enum Mode { Classic = 0, Extended = 1 }

[Serializable] public struct Vec2Int { public int x; public int y; }
```

### 2.3 `WorldState`（运行时世界状态）

> 把「关卡静态层」和「运行时动态对象」分离，是本题最重要的设计选择。

```csharp
public class WorldState {
    // 静态地形（不可变）
    public int[,] terrain;   // 仅含 Empty/Wall/Obstacle/Goal
    public int width, height;

    // 动态对象
    public Vec2Int player;
    public Vec2Int? enemy;     // null = 不存在（经典模式）
    public List<Vec2Int> boxes; // 全部箱子当前坐标

    // 派生（按需重算，不存盘）
    public bool AllBoxesOnGoal() { ... }
    public CellType At(Vec2Int p); // 取地形层 cell
}
```

### 2.4 `MoveSnapshot`（撤销栈元素）

```csharp
public class MoveSnapshot {
    public Vec2Int playerFrom, playerTo;     // v2: 玩家撞墙时 From == To
    public Vec2Int? enemyFrom, enemyTo;      // 拓展模式才有值；敌人没动时 From == To
    public Vec2Int? boxFrom, boxTo;          // 没推箱子则全为 null
    public Direction playerInput;            // 玩家原始输入，用于调试 / 回放

    // v2: 世界状态指纹，用于入栈判定与撤销后校验
    public long hashBefore;
}
```

> **设计要点**：快照只存「与初始态的差分」足够，但为了简化、避免引用歧义，**全量存对象坐标**。W、H ≤ 20 时一个快照 < 100B，万步撤销栈也就 1MB，可接受。
>
> **v2 补充**：`playerFrom == playerTo`（玩家撞墙）现在是**合法快照**，不要当成 bug。
> 撤销时按 `To → From` 原样回写即可，玩家没动就是没动，逻辑自洽。

### 2.5 `Direction`

```csharp
public enum Direction { Up = 0, Down = 1, Left = 2, Right = 3 }
public static class DirectionOp {
    public static readonly Direction[] Opposite = {
        Direction.Down, Direction.Up, Direction.Right, Direction.Left
    };
    public static readonly Vec2Int[] Delta = {
        new Vec2Int(0, -1), new Vec2Int(0, 1),  // Up, Down
        new Vec2Int(-1, 0), new Vec2Int(1, 0),  // Left, Right
    };
}
```

## 3. 核心算法

### 3.1 玩家单步移动

```text
TryMovePlayer(state, dir) -> bool:   // 返回"玩家是否真的动了"
    next = state.player + Delta[dir]

    // 1. 撞墙 / 撞障碍
    if At(next) ∈ {Wall, Obstacle} → return false

    // 2. 撞敌人 → 玩家不动（v2 补漏）
    //    原设计只写了"敌人不能撞玩家"，漏了反向。题目"无法和主角重合"是双向约束。
    if next == state.enemy → return false

    // 3. 前方是箱子
    if next 处有箱子:
        after = next + Delta[dir]
        if At(after) ∈ {Wall, Obstacle} → return false
        if after == state.enemy → return false   // 不允许把箱子推到敌人身上
        if after 处有其它箱子 → return false
        移动箱子：boxes[i] = after
        player: prev → next
        return true

    // 4. 玩家本体移动
    player: prev → next
    return true
```

> **v2 补漏说明**：原来的伪代码只检查墙 / 障碍 / 箱子，**漏了敌人**。
> 若不加第 2 步，玩家会直接走进敌人格子，破坏题目「无法和主角重合」的硬约束。
> 现在玩家与敌人的判定**完全对称**（互不可进入对方格子），规则自洽。

### 3.2 拓展模式一步

```text
Step(state, playerDir):
    before = hash(state)                          // 世界状态指纹

    playerMoved = TryMovePlayer(state, playerDir)          // 可能 false
    enemyMoved  = TryMoveEnemyAsOpposite(state, Opposite(playerDir))  // v2: 无条件调用

    if hash(state) != before:
        undoStack.Push(snapshot(state, before))   // 只有世界真变了才入栈
        return StepResult.Ok
    else:
        return StepResult.NoOp                    // 玩家+敌人都没动，视为无效输入
```

> **v2 关键修正**：`TryMoveEnemyAsOpposite` **无条件调用**，不再挂在 `if playerMoved` 下。
> 入栈判定从「玩家是否移动」改为「**世界状态哈希是否变化**」——
> 这样「玩家撞墙 + 敌人走了」会正确入栈，而「两个都没动」不会污染撤销栈。

**返回值**：

| playerMoved | enemyMoved | hash 变化 | 结果 |
|---|---|---|---|
| ✓ | ✓ | ✓ | `Ok`，入栈 |
| ✓ | ✗ | ✓ | `Ok`，入栈 |
| ✗ | ✓ | ✓ | `Ok`，入栈 ← v2 新增 |
| ✗ | ✗ | ✗ | `NoOp`，不入栈 |

### 3.3 邪恶推箱人同质移动

```text
TryMoveEnemyAsOpposite(state, dir) -> bool:   // 返回"敌人是否真的动了"
    if state.enemy == null → return false      // 经典模式，无敌人
    next = state.enemy + Delta[dir]

    // 1. 撞墙 / 撞障碍
    if At(next) ∈ {Wall, Obstacle} → return false

    // 2. 撞玩家 → 整个敌人操作取消
    //    v2: 玩家撞墙没走开时，敌人照样会走到这里，同样取消 → 保证永不重合
    if next == state.player → return false

    // 3. 前方是箱子
    if next 处有箱子:
        after = next + Delta[dir]
        if At(after) ∈ {Wall, Obstacle} → return false
        if after == state.player → return false   // 不允许把玩家挤走
        if after 处有其它箱子 → return false
        移动箱子：boxes[i] = after

    // 4. 敌人本体移动
    enemy: prev → next
    return true
```

> **与 §3.1 玩家移动的对称性**：敌人判定是玩家的**镜像复制**，唯一多出的是第 2 步的「撞玩家取消」。
> 这样敌人既能推箱子、也能撞墙不动，且**永远不会与玩家重合**（题目硬约束）。

### 3.4 胜负判定

- 每步结束（含敌人推完箱子）调 `WorldState.AllBoxesOnGoal()`。
- 全部 `boxes[i]` 都在 `terrain` 的 `Goal` 格上即胜利。
- 拓展模式无失败条件，纯"全上 = 通关"。

## 4. 解判定（Solver）

> 实现细节多，建议放到独立 `SokobanSolver.cs` 单文件，方便阅读。

### 4.1 混合策略：正向 BFS + 反向 BFS（v3）

Sokoban 解判定是 PSPACE-complete。v3 采用**按箱子数自动切换**的混合策略。

**两种搜索方向**：

| | 正向 BFS | 反向 BFS |
|---|---|---|
| 起点 | 关卡初始状态（箱子在出生点） | **所有箱子都在 goal 上**的状态集合 |
| 动作 | **推**箱子（玩家顶着箱子往前走） | **拉**箱子（玩家背对箱子走，箱子跟过来） |
| 终点 | 所有箱子在 goal 上 | 箱子布局 == 关卡初始布局 |
| 状态 | `(玩家位置, 箱子位置集合)` | 同左 |
| 强项 | 箱子少；能直接给出**最短解步数** | 箱子多 |

**为什么按箱子数切换**：

正向搜索的状态空间上界 ≈ `C(可站立格数, 箱子数) × 玩家可达位置数`。箱子数 K 增大时 `C(N, K)` 会暴涨（峰值在 `K ≈ N/2`）。

反向搜索的**起点箱子配置是唯一的**（全部在 goal 上），搜索从「已归位」向「未归位」扩散，对多箱关卡更聚焦。

| 箱子数 | 策略 | 说明 |
|---|---|---|
| **1 ~ 8** | **正向 BFS** + 死锁剪枝 | 组合可控；且正向能直接给出**最短解步数**，是「提示下一步」功能的基础 |
| **9 ~ 15** | **反向 BFS** + 死锁剪枝 + 可达性剪枝 | 正向会爆炸；反向起点唯一，更聚焦 |
| **> 15** | 反向 BFS，超时放宽到 8s；仍超时则如实降级 | 超出时间盒能力 |

> 阈值 `8 / 15` 是经验起步值，D5 用内置关卡实测后校准。

**拓展模式：只用正向 BFS**。
> 反向 BFS 需要把「敌人的镜像行为」也反向推导（敌人的反向 = 玩家输入镜像的反向…），复杂度翻倍且极易写错。拓展关卡箱子通常 ≤ 6，正向完全够用。

**实现顺序（重要）**：
1. 先只做**正向 BFS + 死锁剪枝**，跑通全部内置关卡。
2. 再做反向 BFS，用多箱关卡验证。
3. **反向做不完就砍掉**，降级为「仅正向 + 超时提示」—— 不影响交付，别让它拖垮整体进度。

### 4.2 死锁剪枝

> **两个方向都要剪**，这是混合策略能跑得快的关键。

- **角落死锁**（corner deadlock）：一旦某箱子被推到"四周都无 Goal 的墙角"，判死。
- **墙边死锁**（wall deadlock）：沿墙的箱子若两侧无 Goal，也是死。
- **freeze**：箱子在墙边沿相同方向顶住，所有后续推都不可能再让它去 Goal。
- 双向 Box-Line Deadlock Detection 等高级算法**本题不上**，会拖时间。

**预计算（两种搜索共用）**：关卡加载后跑一次，把「所有不可能承载箱子的格子」标记为 **dead square**，之后正向 / 反向都直接查表，不用重复计算。

> ⚠️ 反向搜索里的死锁语义要**反过来理解**：正向是"箱子推到这里就完了"，反向是"箱子从这个位置拉不出来"。
> 实现上可以对 dead square 表做一次转置复用，但**必须写单元测试单独验证反向的剪枝不会误杀**。

### 4.3 正向 BFS

**经典模式**：

```text
ForwardBfs(level):
    dead = PrecomputeDeadSquares(level)        // 一次预计算
    start = WorldState(level)
    queue = [start]; visited = {hash(start)}
    while queue 非空:
        if 超时: return Timeout
        s = queue.Dequeue()
        if s.AllBoxesOnGoal(): return Solvable(steps = s.depth)
        foreach dir in {Up, Down, Left, Right}:
            n = Clone(s); Step(n, dir)          // ⚠️ 复用 §3.2，绝不另写转移
            if hash(n) == hash(s): continue     // 自环（都没动）
            if n 有箱子落在 dead square: continue
            if visited.Contains(hash(n)): continue
            visited.Add(hash(n)); queue.Enqueue(n)
    return Unsolvable
```

**拓展模式**（只走正向）：

- 状态哈希要把敌人算进去：`HashCode.Combine(player, enemy, boxes...)`。
- 枚举玩家输入（4 种），敌人动作由 `Step()` 内部决定 —— 仍然是**复用同一个 `Step()`**。
- "敌人在终点上"算 0 代价（不影响胜负，只影响可达性）。

> **v2 关键影响**：规则改成「敌人与玩家解耦」后，**"玩家撞墙"的输入现在是合法状态转移**（只要敌人能动）。
>
> - 不能因为"玩家没动"就剪掉这条分支 —— 否则会漏解，**把有解关卡误判为无解**。
> - 若某输入导致玩家、敌人**都没动**（世界哈希不变），该边产生自环，**不入队**（避免死循环）。
>
> ⚠️ **求解器必须复用 `Step()`，绝不能另写一份转移逻辑** —— 否则规则和求解会悄悄漂移，这是这类题最容易翻车的地方。

### 4.4 反向 BFS

> **适用**：经典模式且箱子数 > 8。拓展模式不走这条路径。

**反向移动的精确定义**：

玩家站在箱子**相邻格**，朝**远离箱子**的方向走一格，同时把箱子拉向玩家原来的位置。

```text
玩家在 (1,1)，箱子在 (2,1)
玩家往 Left 走到 (0,1)  →  箱子被拉到 (1,1)

   拉之前:  [P][B][.]
   (1,1)(2,1)(3,1)

   拉之后:  [B][P][.]
   (0,1)(1,1)(2,1)
```

**算法骨架**：

```text
BackwardBfs(level):
    dead = PrecomputeDeadSquares(level)
    // 起点：箱子全在 goal 上，玩家在任意可达空位（多个起点）
    goals = 所有 goal 格
    starts = []
    foreach p in 玩家可达空位(箱子固定在 goals 上):
        starts.Add(WorldState(player=p, boxes=goals))
    queue = starts; visited = {hash(s) for s in starts}
    while queue 非空:
        if 超时: return Timeout
        s = queue.Dequeue()
        if s.boxes == level.初始箱子布局: return Solvable
        foreach (拉的方向 dir):
            n = Clone(s); PullStep(n, dir)       // 反向版本
            if hash(n) == hash(s): continue
            if n 有箱子落在 dead square: continue
            if !PlayerReachable(n.玩家位置, n.箱子布局): continue   // 可达性剪枝
            if visited.Contains(hash(n)): continue
            visited.Add(hash(n)); queue.Enqueue(n)
    return Unsolvable
```

**两个必须做对的点**：

1. **可达性剪枝 `PlayerReachable`**（不做就是错的）：反向搜索里，玩家必须能**真的走到**那个拉箱位。
   实现：以当前箱子布局为障碍，从玩家位置跑一次 BFS，算出可达格子集合。这个开销不小，可以缓存（按箱子布局的哈希做 key）。
2. **终止时的玩家位置校验**：找到"箱子布局 == 初始布局"时，还要确认玩家能走到关卡初始的玩家出生点，否则是假阳性。

**风险**：反向 BFS 的实现量约为正向的 2 倍（反向移动 + 可达性剪枝 + 缓存）。
**一周时间盒内的取舍**：正向跑通是 P0，反向是 P1。反向做不完就砍，不影响交付。

### 4.5 接口

```csharp
public interface ISokobanSolver {
    SolveResult Solve(LevelData level, int timeoutMs = 5000);
}

public class SolveResult {
    public SolveStatus status;      // Solvable / Unsolvable / Timeout / Error
    public SolveStrategy strategy;  // v3: 实际使用的策略，便于调试与日志
    public int     steps;           // 最短步数（无解 / 超时则为 -1）
    public long    elapsedMs;
    public int     visitedStates;   // 搜索过的状态数，用于校准 §4.1 的阈值
    public List<Direction> path;    // 最短解路径（可选，供"提示下一步"用）
}

public enum SolveStatus { Solvable, Unsolvable, Timeout, Error }
public enum SolveStrategy { ForwardBfs, BackwardBfs }
```

**策略选择入口**：

```csharp
public static SolveStrategy PickStrategy(LevelData level) {
    if (level.mode == Mode.Extended) return SolveStrategy.ForwardBfs;  // 拓展只走正向
    int boxes = 统计 level.cells 中 Box 的数量;
    return boxes <= 8 ? SolveStrategy.ForwardBfs : SolveStrategy.BackwardBfs;
}
```

## 5. 模块与接口

```
Assets/
├── Scripts/
│   ├── Core/
│   │   ├── LevelData.cs           // CellType / LevelData / Vec2Int
│   │   ├── WorldState.cs          // WorldState
│   │   ├── Direction.cs           // Direction + DirectionOp
│   │   └── MoveSnapshot.cs        // 撤销快照
│   ├── Game/
│   │   ├── IGameMode.cs           // 经典 / 拓展 接口
│   │   ├── ClassicMode.cs
│   │   ├── ExtendedMode.cs
│   │   ├── GameController.cs      // 主循环、输入分发、撤销、计步
│   │   └── WinChecker.cs
│   ├── EditorCore/                // ★ v3: 编辑器核心（双形态共用）
│   │   ├── LevelEditorCore.cs     // 纯逻辑：网格数据 / 工具 / 历史栈
│   │   ├── LevelValidator.cs      // 实时校验（外墙闭合、对象不重合）
│   │   ├── LevelIO.cs             // JSON 序列化（不碰文件系统 API）
│   │   └── EditCommand.cs         // 编辑操作，供撤销/重做
│   ├── Solver/
│   │   ├── ISokobanSolver.cs
│   │   ├── ForwardBfsSolver.cs    // v3: 正向 BFS + 死锁剪枝
│   │   ├── BackwardBfsSolver.cs   // v3: 反向 BFS（含可达性剪枝）
│   │   ├── SolverRouter.cs        // v3: 按箱子数/模式选择策略
│   │   └── Deadlock.cs            // dead square 预计算（两方向共用）
│   ├── Platform/                  // ★ v3: 平台适配
│   │   ├── IInputProvider.cs      // 键盘 / 手柄 / 滑动手势
│   │   ├── SafeAreaApplier.cs     // 刘海屏适配
│   │   └── OrientationSwitch.cs   // 横竖屏布局切换
│   └── UI/
│       ├── HudView.cs
│       ├── WinPanelView.cs
│       ├── LevelSelectView.cs
│       └── RuntimeEditorView.cs   // ★ v3: 游戏内编辑器（壳）
├── Editor/                        // Unity 要求：编辑器脚本必须在 Editor 文件夹下
│   ├── UIBuilder/                 // UI Builder 导入器（外部工具，勿改）
│   │   └── UIBuilderImporter.cs
│   └── Sokoban/
│       ├── LevelEditorWindow.cs   // ★ v3: Editor 插件形态（壳）
│       └── LevelEditorMenu.cs     // 菜单入口 Window / 推箱子 / ...
└── Art/
    ├── Sprites/
    └── _Placeholder/
```

### 5.0 编辑器双形态架构（v3 核心）

> **原则：逻辑一份，壳两份。** 两边共用 `EditorCore/`，只换「绘制 + 输入 + 文件 IO」。

```text
                 ┌──────────────────────────┐
                 │   LevelEditorCore (纯C#)  │
                 │  网格 / 工具 / 校验 / 历史 │
                 └────────────┬─────────────┘
                              │
              ┌───────────────┴───────────────┐
              │                               │
   ┌──────────▼──────────┐        ┌───────────▼─────────┐
   │  A. EditorWindow 壳  │        │  B. 运行时 uGUI 壳   │
   │  Editor/Sokoban/     │        │  Scripts/UI/        │
   │  - IMGUI 绘制        │        │  - UI Builder prefab│
   │  - Unity Undo 集成   │        │  - 自建编辑操作栈    │
   │  - File API 直写工程 │        │  - persistentDataPath│
   └─────────────────────┘        └─────────────────────┘
```

**`LevelEditorCore` 对外接口**（两个壳都只调这些）：

```csharp
public class LevelEditorCore {
    public LevelData Data { get; }
    public EditorTool CurrentTool { get; set; }

    public bool TryPaint(Vec2Int cell);          // 落笔（内部走校验）
    public void ClearCell(Vec2Int cell);          // 擦除（保留外墙）
    public bool TrySetEnemy(Vec2Int cell);        // 仅拓展模式
    public ValidationResult Validate();           // 整关校验，返回问题列表
    public bool CanUndoEdit();
    public void UndoEdit();
    public void RedoEdit();
    public string ToJson();                       // 只序列化，不写盘
    public void LoadFromJson(string json);
}

public enum EditorTool { Wall, Player, Enemy, Box, Goal, Obstacle, Erase }
```

> **为什么核心层不碰 `UnityEditor` / `UnityEngine.UI`**：
> 一旦引用，`EditorCore` 就只能跑在编辑器里，运行时形态直接报废。
> 文件读写同理 —— 核心层只产出/消费 JSON **字符串**，由壳决定写到哪。

### 5.1 `IGameMode`

```csharp
public interface IGameMode {
    WorldState State { get; }

    // v2: 语义从"玩家是否移动"改为"世界是否变化"
    //     NoOp = 玩家与敌人都没动 → 无效输入，不入撤销栈、不计步数
    StepResult Step(Direction playerDir, out StepDetail detail);

    void Reset();
    void Undo();
    void Redo();

    // v3: 计步
    int StepCount { get; }      // 只计玩家有效步，敌人不计
    int UndoCount { get; }      // 撤销次数，单独统计
}
```

**计步实现要点（v3）**：

```text
Step(dir):
    result = 内部执行(dir)
    if result == Ok:
        stepCount++            // 世界变了才计步
    return result

Undo():
    回退整个世界状态（玩家 + 敌人 + 箱子）
    undoCount++
    // ⚠️ stepCount 不减 —— 见 GDD §2.3，否则可刷步数

Reset():
    stepCount = 0; undoCount = 0; 清空两个栈
```

> **敌人步数不单独统计**：按 GDD §7 第 4 条，拓展模式只展示玩家步数。
> 敌人的移动已经隐含在玩家步数里（玩家的每一步都会触发敌人），单独统计反而是噪音。

> **为什么要细分 `StepDetail`**：v2 之后「玩家没动但敌人动了」是合法且常见的一步，
> 动画层需要知道**该播谁的动画** —— 玩家撞墙要播"顶一下"的反馈，敌人要播位移。

### 5.2 `GameController`

```csharp
public class GameController : MonoBehaviour {
    public IGameMode Mode { get; private set; }
    public bool IsPaused { get; private set; }
    public event Action<int> OnStepTaken;   // v3: 带步数
    public event Action OnWin;

    public void BindInput(IInputProvider input);
    public void LoadLevel(LevelData level);
    public void Pause(bool paused);
}
```

### 5.3 平台与屏幕适配（v3）

**Canvas 配置**：

| 项 | 值 | 说明 |
|---|---|---|
| CanvasScaler | `Scale With Screen Size` | |
| Reference Resolution | 竖屏 `720×1280` / 横屏 `1280×720` | 与 UI Builder 的比例预设**保持一致**，避免二次换算 |
| Screen Match Mode | `Match Width Or Height` | |
| Match | 竖屏 `0`（match Width）、横屏 `1`（match Height） | 运行时按 `Screen.width > Screen.height` 切换 |

**棋盘适配（与 UI 分开处理）**：

> 棋盘**不能**用 CanvasScaler 拉伸 —— 否则各种比例下格子会变形。

```text
棋盘适配算法：
    cellSize = min(可用宽 / 棋盘宽, 可用高 / 棋盘高)   // 取小，保证完整可见
   棋盘整体缩放 = cellSize / 设计格子尺寸
    棋盘容器锚点 = 居中（竖屏时上移，给下方操作区留位）
```

**SafeArea（移动端刘海 / 挖孔 / Home 条）**：

```csharp
public class SafeAreaApplier : MonoBehaviour {
    void Awake() {
        var rect = Screen.safeArea;                 // 拿到安全区
        把本 RectTransform 的 anchorMin/anchorMax 按 rect 换算
    }
}
```
> 只对**根节点 HUD 容器**应用一次，子节点用锚点跟随即可，不要每个元素都算。

**输入抽象 `IInputProvider`**：

| 实现 | 平台 | 行为 |
|---|---|---|
| `KeyboardInput` | PC | 方向键 / WASD → `Direction`；`Z` 撤销、`R` 重置 |
| `GamepadInput` | PC / 主机 | 十字键 / 左摇杆四方向 |
| `SwipeInput` | 移动端 | 单次滑动 → 一个 `Direction`；**一次滑动 = 一步**，不做长按连走 |

> 三者都只产出 `Direction`，`GameController` 不关心来源 —— 换平台只换 Provider。

**横竖屏切换**：

- 监听 `Screen.orientation` / `Screen.width` 变化。
- 两个预设布局（竖屏 / 横屏）用**同一套 prefab 的不同根节点**切换显隐，不做运行时重排（重排极易出 bug）。
- 编辑器插件形态不受影响（EditorWindow 固定布局）。

**测试清单**：

- [ ] 9:16 / 9:19.5 / 9:21 / 16:9 / 21:9 五种比例下棋盘完整可见、不变形
- [ ] 刘海机型（iPhone 14 Pro 类）HUD 不被遮挡
- [ ] 横竖屏来回切换布局不残留
- [ ] 滑动手势一次只走一步，无误触连走

## 6. 美术管线

**两类资产，两条产出路径（v3）**：

| 资产类型 | 产出方式 | 落盘位置 |
|---|---|---|
| **游戏内素材**（角色 / 箱子 / 地砖 / 背景） | AI 生图 `ImageGen`，按 [03-ArtBible](./03-美术资源文档(ArtBible).md) 清单 | `Assets/Art/Sprites/` |
| **UI 界面**（菜单 / HUD / 编辑器面板 / 结算） | **UI Builder** 网页排版 → 导出 JSON → Unity 导入器生成 prefab | `Assets/UIBuilder/` |

**UI Builder 工作流**：

```text
1. 打开 Sokoban/UITools/UIBuilder/index.html
2. 顶部「屏幕比例」选 9:16（竖屏主设计比例）
3. 策划模式排版 → 「✨ AI 排版」一句话出初版 → 手动微调
4. 切「美术」模式上色 / 换背景素材
5. 「📤 导出 → 完整包」得到 .json
6. Unity 菜单 UI Builder / Import from JSON → 生成 Assets/UIBuilder/<名字>.prefab
7. 程序在 Unity 里微调 + 绑 onClick（重导入不会丢）
```

> **为什么 UI 走工具而不是手写**：多分辨率适配（9:16 / 9:19.5 / 9:21 / 16:9）在工具里是**切换下拉就能预览**的，手写 uGUI 要反复进 Unity 改锚点试。
> 这也是本题「工具提效」叙事里最硬的一条 —— 见 [04-流程文档 §2](./04-制作流程与工具提效记录.md)。

**临时占位**：色块占位跑通逻辑，落 `Assets/Art/_Placeholder/`，后续替换。
游戏素材统一 64×64 / pivot center。

## 7. 性能与极限

- 棋盘 ≤ 30×30；箱子数 ≤ 12；步数 ≤ 9999。
- 撤销栈直接 `List<MoveSnapshot>`，不优化（够用）。
- BFS 求解时维护 `HashSet<long>` 状态哈希，预估可达 100w+ 状态 / 秒（C# 默认够用）。
- GC：撤销栈里都是 struct，期望无 GC。

**v3 补充（移动端 / 求解器）**：

| 项 | 约束 / 对策 |
|---|---|
| 移动端内存 | 状态哈希 `HashSet<long>` 在 100w 状态约 30~50MB，**求解只在编辑器与 PC 跑**，移动端不出「检测是否有解」按钮（关卡编辑器移动端也只保留基础编辑） |
| 反向 BFS 可达性缓存 | 按「箱子布局哈希」缓存玩家可达集合，否则每步重算 BFS 会拖慢 10 倍以上 |
| 求解线程 | 5s 内同步跑可能卡 UI → 用 `async/await` + 进度条，或放 `Thread`（求解器只读 `WorldState` 副本，无共享写） |
| 低端机 | 棋盘 ≤ 20×20 时目标 60fps；格子用 SpriteRenderer + 静态批处理 |

## 8. 单元 / 集成测试策略

> 笔试时间紧，但下面 3 类测试最少要落 1 类让评审有"硬证据"。

1. **`WorldState` 单元测试**（EditMode）：覆盖「推 1 箱子」「撞墙」「撞死锁」「敌人撞玩家取消」。
   **v2 必测新增用例**：
   - `PlayerBlocked_EnemyStillMoves` —— 玩家撞墙 + 敌人方向通畅 → 断言**只有敌人坐标变了**，且**入了撤销栈**
   - `PlayerBlocked_EnemyBlocked_NoUndoPush` —— 两者都撞墙 → 断言世界哈希不变、**撤销栈不增长**
   - `EnemyCannotOverlapPlayer` —— 玩家不动时敌人走向玩家格 → 断言敌人**原地不动**
   - `UndoRestoresEnemyOnlyStep` —— 撤销一步"仅敌人移动"，断言敌人回退、玩家不动
2. **求解器单元测试**：拿经典 Sokoban 已知小关 + 本工程 `tut_01 / cls_01` 至少 3 个，断言 `Solvable`。
3. **关卡编辑器冒烟**：加载关卡 JSON → 启动运行时 → 推完 → 出 `OnWin`。

**v3 必测新增用例**：

| 用例 | 断言 |
|---|---|
| `Undo_DoesNotDecreaseStepCount` | 走 3 步 → 撤销 1 次 → 步数仍是 3、撤销数是 1 |
| `StepCount_IgnoresNoOpInput` | 撞墙无效输入 5 次 → 步数仍为 0 |
| `ExtendedMode_EnemyNotCountedAsStep` | 拓展模式走 1 步（玩家+敌人都动）→ 步数 +1 而非 +2 |
| `EditorCore_SameResultInBothShells` | 同一串编辑操作序列，Editor 壳与运行时壳产出的 JSON **逐字节相同** |
| `Solver_ForwardAndBackwardAgree` | 对同一关卡（箱子数 ≤ 8）分别强制走正向 / 反向，两者 `Solvable` 结论必须一致 |
| `Solver_RouterPicksRightStrategy` | 5 箱 → `ForwardBfs`；12 箱 → `BackwardBfs`；拓展模式任意箱数 → `ForwardBfs` |

> 最后两条尤其重要：**策略路由和双壳一致性**是最容易悄悄坏掉、又最难靠手玩发现的地方。

## 9. 风险与不做的事

### 9.1 不做的事（时间盒内主动砍掉）

- ❌ 不做联机 / 异步回放
- ❌ 不做自定义皮肤系统
- ❌ 不做 boss 关、剧情章节编辑器（题目要求上限即可）
- ❌ 移动端不做关卡编辑器（只保留游玩；编辑是 PC 场景，移动端也跑不动 5s 求解）
- ❌ 不做账号 / 云存档 / 内购

> ~~不做移动端 / WebGL 适配~~ → **v3 已推翻**，改为 PC 主 + 移动端次。

### 9.2 风险登记

| # | 风险 | 影响 | 应对 |
|---|---|---|---|
| R1 | **反向 BFS 实现量超预期** | 拖垮 D5，连带影响美术 | 正向是 P0，反向是 P1；做不完就砍，降级为「仅正向 + 超时提示」 |
| R2 | 求解超时频繁 | 编辑器「检测有解」体验差 | 反向放宽到 8s；仍超时如实告知，引导缩小关卡 |
| R3 | **双形态编辑器行为不一致** | 同一关卡在 Editor 和游戏里表现不同 | 逻辑只写一份（`EditorCore`）；用 `EditorCore_SameResultInBothShells` 用例守住 |
| R4 | 反向 BFS 死锁剪枝误杀 | 把有解关卡判成无解 | 反向剪枝单独写测试；先用正向结果做交叉验证 |
| R5 | UI Builder 导入的 prefab 与手写代码冲突 | 重导入丢绑定 | 按工具规范：绑定只写在**独立脚本**里，不直接挂 prefab；重导入前先 `Export to JSON` 回写 |
| R6 | 移动端适配吃掉太多时间 | 拖累主线 | 适配只做「棋盘等比 + HUD 贴边 + SafeArea + 滑动输入」四件事，不做重排动画 |
| R7 | AI 生图奶蛙风跑偏 | 美术返工 | 用 ArtBible §1.1 前缀；每类准备 2 套 prompt；来不及就色块占位 |
