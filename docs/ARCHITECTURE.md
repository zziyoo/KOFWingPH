# ARCHITECTURE.md — NewKOF 架构说明

> 最后更新：2026-11-08（阶段12确定性AI；Debug/Release各1172项通过；StateVersion15/CoreVersion16保持）
> 本文描述**已确定**的架构与**未决定**的问题。修改代码前请先读本文件。

---

## 1. 项目结构与技术栈

### 技术栈（已确定）

| 项目 | 版本 | 说明 |
|---|---|---|
| Godot | `4.7.2.stable.mono.official` | 必须是 **mono/.NET 版**，标准版无法编译 C# |
| .NET SDK | 10.0.403 | |
| 目标框架 | `net8.0` | Godot 4.7 .NET 的官方目标框架 |
| 语言 | C# | 主要开发语言 |
| 测试框架 | xUnit 2.9.2 + Microsoft.NET.Test.Sdk 17.11.1 | 仅核心模块使用 |
| 渲染器 | Forward+ (Vulkan) | |

Godot 可执行文件路径（本机）：
```
C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
```

### 目录结构

```
NewKOF/
├── project.godot              # Godot 工程配置，主场景 = res://scenes/Battle.tscn
├── NewKOF.csproj              # 表现层程序集（Godot.NET.Sdk）
├── icon.svg
├── .gitignore
│
├── scenes/
│   └── Battle.tscn            # 基础战斗场景：背景 / 天空 / 地面 / 两个占位角色 / HUD
│
├── scripts/                   # 表现层 C# 源码（唯一被 Godot 编译的目录）
│   ├── BattleScene.cs         # 战斗场景根节点，驱动逻辑帧并同步画面
│   ├── KeyboardInputMap.cs    # ★ 阶段 03A：键盘事件 → InputBits（唯一接触 Godot 输入 API）
│   └── CaptureHelper.cs       # 开发期截图/按键注入工具（仅 --shot= 参数启用）
│
├── core/
│   └── BattleCore/            # ★ 权威战斗内核（纯 C#，零 Godot 依赖）
│       ├── BattleCore.csproj
│       ├── BattleCoreInfo.cs
│       ├── Numerics/
│       │   └── FixedPoint.cs           # ★ 阶段 05A：Q16.16 定点数（唯一权威数值表示）
│       ├── Input/FrameInput.cs
│       ├── Input/InputBuffer.cs        # ★ 阶段 03A：渲染采样 → 逻辑帧输入（边沿闩锁 + 历史）
│       ├── Input/DirectionResolver.cs  # ★ 阶段 03A：SOCD + 绝对方向 → 相对朝向
│       ├── Commands/                   # ★ 阶段 03B-1：指令识别（纯数据 + 识别器）
│       │   ├── CommandDirection.cs     # 相对朝向九宫格方向
│       │   ├── CommandDefinition.cs    # 指令数据（序列 / 按键 / 跨度 / 容差 / 优先级 / 时效）
│       │   ├── CommandMatch.cs         # 识别结果（完成帧 / 按键帧 / 诊断）
│       │   ├── CommandRecognizer.cs    # 方向变化压缩 + 匹配 + 冲突裁决 + 缓冲
│       │   ├── RecognitionState.cs     # ★ 阶段 04：不可变识别快照 + 纯函数 Advance（随权威状态携带）
│       │   └── CommandCatalog.cs       # 默认指令表（236A / 623A / 214B / 66 / 蓄力 / 双重）
│       ├── Combat/
│       │   ├── FighterLogicState.cs    # ★ 阶段 04/05B：角色逻辑状态枚举（含 PreJump/Airborne）+ MoveAllowedStates
│       │   ├── JumpKind.cs            # ★ 阶段 05B：跳跃类型枚举 + JumpLaunch 起跳参数
│       │   ├── FighterStateMachine.cs  # ★ 阶段 04/05B：集中纯函数状态机 Tick（含起跳/预备跳）
│       │   ├── MoveDefinition.cs       # ★ 阶段 04：招式定义（三阶段帧数 / 允许发动状态）
│       │   ├── FighterDefinition.cs    # ★ 阶段 04：角色专属配置（指令表 + 招式表 + 运动数据）
│       │   ├── FightRoster.cs          # ★ 阶段 04：双方角色配置
│       │   ├── DemoRoster.cs           # 演示配置（Kai / Rei，测试与 Godot 共用）
│       │   ├── MovementDefinition.cs   # ★ 阶段 05A/05B：运动配置（步行 + 跳跃参数）
│       │   ├── GroundMovement.cs       # ★ 阶段 05A：地面运动纯函数（站立步行）
│       │   └── AirborneMovement.cs     # ★ 阶段 05B：空中定点积分纯函数（重力/落地）
│       ├── State/FighterState.cs
│       ├── State/BattleState.cs
│       └── Simulation/
│           ├── BattleSimulation.cs
│           ├── BattleStepResult.cs
│           ├── FixedStepScheduler.cs   # ★ 阶段 02：固定步长调度器（可独立测试）
│           ├── BattleSession.cs        # ★ 阶段 02：调度 + 模拟 + 状态绑定
│           └── StageDefinition.cs      # ★ 阶段 05B：场地配置（边界/地面/最小间距）
│
├── tests/
│   └── BattleCore.Tests/      # 独立 xUnit 测试工程（173 个测试）
│       ├── BattleCore.Tests.csproj
│       ├── BattleSimulationTests.cs        # 阶段 01：8 个
│       ├── FixedStepSchedulerTests.cs      # 阶段 02：调度器测试
│       ├── DeterminismTests.cs             # 阶段 02：确定性与长跑
│       ├── GodotIntegrationContractTests.cs # 阶段 02：Godot 层契约
│       ├── FrameSyncRegressionTests.cs     # 阶段 02 补修：问题 1/2 回归
│       ├── SchedulerAccountingTests.cs     # 阶段 02 补修：丢弃语义、超大 delta、不变量、API 权限
│       ├── SchedulerNumericalRobustnessTests.cs # 阶段 02 补修：NaN/溢出/精度健壮性
│       ├── InputBufferTests.cs             # 阶段 03A：电平/边沿/双人独立/历史连续性
│       ├── DirectionResolverTests.cs       # 阶段 03A：SOCD 与相对朝向
│       ├── InputPipelineTests.cs           # 阶段 03A：输入与逻辑帧绑定、暂停、多帧批次
│       └── CommandRecognitionTests.cs      # 阶段 03B-1：指令识别、冲突、换边、帧率一致性
│
├── docs/                      # 交接文档（本目录）
└── shots/                     # 验收截图输出（git 忽略）
```

---

## 2. 模块职责与依赖方向

### 依赖关系（强制单向，不可逆）

```
tests/BattleCore.Tests  ──┐
                          ├──>  core/BattleCore  (纯 C#，无 Godot)
NewKOF (表现层)  ─────────┘
```

- **`core/BattleCore` 绝不引用 Godot。** 它的 `.csproj` 中没有 Godot SDK，也没有任何 `using Godot`。
- **`NewKOF`（表现层）引用 `BattleCore`**，方向单向。
- **`BattleCore.Tests` 只引用 `BattleCore`**，不引用表现层，因此测试可以在无 GUI 环境下运行。
- 如果未来需要表现层与核心共享数据结构，数据结构必须放在 `BattleCore` 内，由表现层引用，而不是反向。

### 各模块职责

| 模块 | 职责 | 明确不负责 |
|---|---|---|
| `BattleSimulation` | 唯一调度入口，`Step()` 推进一逻辑帧：**识别 → 消费 → 状态机推进**（D55/D58），按角色配置驱动 | 输入采样、渲染、读写文件、**时间累积** |
| `FixedStepScheduler` | **时间累积与补帧决策**：把渲染 delta 转成"该推进几帧" | 战斗规则、状态、Godot API |
| `BattleSession` | 把调度器输出与 `Step()` 绑定，保证输入帧号 == 状态帧号 | 渲染、输入采样 |
| `BattleState` | 权威状态快照（不可变） | 被外部修改 |
| `FighterState` | 单角色数据（身份 + 初始数据 + **动作状态与识别快照**，D57） | 被外部修改 |
| `FrameInput` | 一帧内双方输入（不可变值类型） | 输入归一化、SOCD、命令识别 |
| `InputBuffer` | **渲染采样 → 逻辑帧输入**：电平保持、按下边沿闩锁、两阶段提交、只读历史（默认 600 帧窗口） | 键盘/手柄 API、SOCD、指令识别、完整录像 |
| `DirectionResolver` | **SOCD 清理 + 绝对方向 → 相对朝向**（纯函数） | 输入录制、状态推进 |
| `CommandRecognizer` | **逐帧输入 → 指令 ID**：方向变化压缩、按键边沿匹配、冲突裁决、结果缓冲、**蓄力释放挂起（每指令一个固定槽位）** | 招式发动/取消/硬直、移动、任何 Godot API |
| `CommandDefinition` / `CommandCatalog` | 指令定义（纯数据）与默认指令表 | 识别流程、角色专属数值 |
| `FixedPoint` | **Q16.16 定点数（D63）**：权威位置/速度的唯一数值表示，整数算术、向零截断、checked 溢出 | 任何浮点权威计算、显示转换写回 |
| `MovementDefinition` / `GroundMovement` | **运动配置与地面运动（D64/D65）**：步行速度纯函数推进 | 识别、状态机、Godot 显示 |
| `BattleStepResult` | 单次 Step 的结果描述 | 权威判定 |
| `BattleScene` (Godot) | 把 `delta` 交给 `BattleSession`、把状态映射为画面 | 任何权威判定、**任何时间累积逻辑**、输入历史 |
| `KeyboardInputMap` (Godot) | 键盘事件 → `InputBits`，喂给 `InputBuffer` | SOCD、指令识别、权威判定 |
| `CaptureHelper` (Godot) | 开发期截图 / 合成按键注入 | 战斗逻辑（正常运行零副作用） |

**阶段 02 补修后的关键分工**：时间调度逻辑**已全部移出 Godot 层**，位于
`core/BattleCore/Simulation/FixedStepScheduler.cs`。`BattleScene._Process`
只调用 `_session.Advance(delta, inputSource: <输入缓冲>)`。

**记账职责链（阶段 02 补修 + 阶段 03A 补修）**：

```
BattleScene._Process(delta)
   └─> BattleSession.Advance(delta, inputSource: buffer)
         ├─> Scheduler.Advance(delta)              // 只消耗时间预算，返回可推进帧数
         ├─> 循环: buffer.Prepare(frame)           // 只计算输入，不改历史/不清边沿
         │         → BattleSimulation.Step(...)    // 算出候选状态，暂不赋值
         │         → buffer.Commit(input)          // 成功后才写历史、消费边沿
         │         → CurrentState = nextState      // 提交成功后才落权威状态
         │         → Scheduler.CommitStep()        // 记账
         │         → frameCommitted(input, state)  // 03B-1：只读派生（指令识别）
         └─> 异常时: Scheduler.RecordDroppedSteps(剩余预算) + throw
```

**指令识别接线（阶段 03B-1）**：`BattleSession` 的可选 `frameCommitted` 回调在每帧
**完整提交之后**调用一次；`BattleScene` 用它把 `(input, 当帧 state.Facing)` 喂给
`CommandRecognizer.Observe`。识别不在 `Step` 内执行，也不参与状态推进；
Godot 层只读取 `LastMatch` 显示到 HUD。

**提交顺序的异常语义（阶段 03A 最终补修）**：

- 候选状态在 `Commit` 成功之前**不赋值**，因此 `Step()` 失败或 `Commit()` 失败都
  不会让 `CurrentState` 或 `TotalSteps` 前进，`Frame - 初始帧号 == TotalSteps` 不被破坏。
- `Commit()` 失败时该帧不写输入历史、不清按下边沿，重试同一帧得到同一输入。
- `IInputSource.Commit()` 的契约是**「失败必须无副作用」**。`InputBuffer` 通过
  「先完成所有校验，再写历史/清边沿」满足它；第三方实现必须自行保证。
  `BattleSession` 只保证自己的状态与计数不被污染，**不假装**能回滚第三方输入源的内部改动。
- 普通 `Func<int,FrameInput>` 输入源无法回滚，契约要求「同一帧号返回同一输入（幂等）」。

**输入提交不变量**：`History` 为空（尚未提交）或 `History[^1].Frame == CurrentState.Frame - 1`，
用 `InputBuffer.IsAlignedWith(frame)` 断言。模拟或提交失败的那一帧**不会**出现在历史里。

**核心帧同步不变量**（可用 `BattleSession.IsFrameSyncIntact` 断言）：

```
CurrentState.Frame - 初始帧号  ==  Scheduler.TotalSteps
```

该等式在 `Advance`、`StepExact`、暂停、异常后**均成立**。

---

## 3. 核心模拟独立于 Godot 的原则

1. **状态所有权唯一**：`BattleState` 只能由 `BattleSimulation` 创建，表现层只持有引用并只读。
2. **纯函数推进**：`Step(state, input)` 永不修改入参，返回全新 `BattleState`。
3. **输入外部注入**：模拟内部绝不采样键盘/手柄。同一份帧输入序列必然得到同一结果。
4. **不使用 Godot 物理系统**：`PhysicsServer2D`、`RigidBody2D`、`Area2D` 碰撞信号**不得**参与权威结果。未来碰撞检测由 `BattleCore` 自行实现（整数/定点几何）。
5. **固定逻辑帧率**：`BattleCoreInfo.LogicFrameRate = 60`。渲染帧率可以浮动，逻辑帧数不受影响。
6. **无浮点权威字段**：位置/速度等权威数值一律使用 Q16.16 定点数（D63，`long` 原始值）。
   `double` 转换仅供 Godot 显示，结果不得写回权威状态。
7. **调度与模拟分离**：时间累积（谁决定推进几帧）由 `FixedStepScheduler` 负责，
   战斗规则推进（推进后状态如何变化）由 `BattleSimulation` 负责。二者互不知道对方细节。
8. **时间调度不得留在 Godot 层**：任何 `_Process` 中的累加器、补帧、取整都属于
   架构违规——它们无法被无 GUI 测试覆盖。Godot 层只允许 `_session.Advance(delta, ...)` 一行。
9. **输入边沿必须绑定逻辑帧**（阶段 03A）：Godot 层只把物理按键转成 `InputBits` 交给
   `InputBuffer`；「这次按下属于第几帧」由内核按帧号裁决。**禁止**把一次渲染帧采样
   直接当成一帧输入，也禁止把同一采样复制给整批逻辑帧（会重复消费边沿）。
10. **录制层保持原始语义**：`InputBits` 存**绝对方向 + 同时按下**，SOCD 与相对朝向
    只在 `DirectionResolver` 解析时应用。这样同一份输入日志换边后仍可重放。

---

## 4. 已经确定的技术决策

| # | 决策 | 理由 |
|---|---|---|
| D1 | **不可变状态模型** | 天然支持确定性重放、快照/回滚；杜绝别名修改导致的隐性 bug |
| D2 | `BattleState` 为 `sealed record`，角色数组在构造时 `ToArray()` + `AsReadOnly()` | 防止外部持有可变引用后修改内部状态 |
| D3 | `FrameInput` 携带 `Frame` 字段，`Step` 校验它必须等于 `state.Frame` | 强制输入与状态对齐，避免错帧重放 |
| D4 | 核心工程 `EnableDefaultCompileItems` 保持默认，表现层工程**关闭**默认收集并只编译 `scripts\**` | Godot SDK 会递归收集 `.godot/mono/temp/obj` 里的生成文件，导致 CS0579 特性重复 |
| D5 | 表现层角色节点用 `Node2D` 容器 + 子 `ColorRect` | `ColorRect` 是 Control，不响应 `Node2D.Position`；直接放在 Node2D 下无法移动 |
| D6 | ~~固定步长累加器推进逻辑帧（`_accumulatedMicros`）~~ → **阶段 02 已废弃** | 旧实现有实测缺陷 F1–F4，见下方「阶段 02 决策」 |
| D7 | 仅借鉴 Ikemen GO 的**设计思想**，不复制代码 | 任务硬性限制；且新项目无需 MUGEN 兼容负担 |
| D8 | 阶段 01 不引入任何第三方运行时依赖 | 只有测试工程用 xUnit |

### 阶段 02 决策

| # | 决策 | 理由 / 证据 |
|---|---|---|
| **D9** | **时间累积用 `double` 秒，不用整数微秒** | 1/60 秒 = 16666.666... µs 无法用整数精确表示。旧实现 `(int)(delta*1e6)` + `1000000/60` 的截断误差与间隔误差随渲染帧率叠加，实测 600 秒：240FPS **少 5 帧**、144FPS 少 1 帧、30FPS 多 1 帧。改为 double 秒累加后，30/60/144/240/59.94/165 FPS 全部**精确 36000 帧、零漂移**。 |
| **D10** | **调度器置于 `BattleCore`，不在 Godot 层** | 调度逻辑若留在 `_Process` 内则无法被无 GUI 测试覆盖，只能靠肉眼观察 HUD。抽到纯 C# 后可用无 GUI 测试逐项验证（当前 123 项）。 |
| **D11** | **补帧上限 `MaxStepsPerAdvance`，超限显式记录** | 防止卡顿后 `while` 无限补帧造成死亡螺旋。超限部分累加进 `DroppedStepCount`，**绝不静默丢弃**。`BattleScene` 设为 5，并在首次丢弃时告警一次。 |
| **D12** | **显式 `Pause()/Resume()`，恢复后不回补暂停时间** | 暂停期间 `Advance` 直接返回 0，且不累积 delta。恢复后不会突然补上暂停期间的数百帧。 |
| **D13** | **`Advance` 拒绝 NaN / ±Infinity / 负 delta** | 这些值会污染累加器（NaN 一旦进入永不恢复），必须立即失败。 |
| **D14** | **`BattleSession` 强制「输入帧号 == 状态帧号」** | 把配对逻辑收进内核，避免表现层出现"状态推进了但输入用旧帧号"的错帧。`inputProvider` 返回错误帧号时立即抛异常。 |
| **D15** | **`BattleScene` 不含任何时间逻辑** | `_Process` 只调用 `_session.Advance(delta, null)`，并根据返回值决定是否重绘。 |
| **D16** | **积压告警只输出一次** | 阶段 02 实现中曾用 `if (DroppedStepCount > 0)` 判断，导致每帧刷屏（实测一次运行输出数十条）。改为 `_backlogWarningIssued` 标志位。 |

### 阶段 02 补修决策（帧同步）

| # | 决策 | 理由 / 证据 |
|---|---|---|
| **D17** | **调度器只给「时间预算」，记账由 `CommitStep` 独立完成** | 旧实现 `Advance()` 先加 `TotalSteps` 再执行 `Step()`，中途抛异常会让计数跑在状态前面。现改为：`Advance()` 只消耗预算并返回可推进帧数，**不增加 `TotalSteps`**；每真实提交一帧才 `CommitStep()`。 |
| **D18** | **异常策略：失败即终止本批次，不回滚、不重试、不吞异常** | 已提交帧保留；未提交帧不计入 `TotalSteps`；剩余预算记入 `DroppedStepCount`；异常原样 `throw`。**不回滚累加器**，因为回滚会让同一段真实时间被重复计费、引入新漂移。 |
| **D19** | **`StepExact` 与 `Advance` 共用同一记账路径** | 旧 `StepExact` 只改 `Frame` 不改 `TotalSteps`，两种推进方式混用就错位。现在两条路径都调用 `CommitStep()`，可任意交替。 |
| **D20** | **暂停只阻止时间驱动推进，不阻止显式单步** | `Pause()` 后 `Advance()` 返回 0，但 `StepExact()` 仍生效——逐帧调试需要这个语义。 |
| **D21** | **`DroppedStepCount` = 被舍弃的时间预算，不是已执行帧** | 语义澄清。满足 `TotalSteps + DroppedStepCount ≈ 累计投入时间 / FixedStepSeconds`。真实执行帧数只看 `TotalSteps`。 |
| **D22** | **超大 delta 用整除一次算整帧数，禁止循环减步长** | 实测单次 `Advance(3600.0)` 用循环减步长得 **215999** 帧（少 1 帧，浮点残差累积）。改为整除后精确得 216000。复杂度也从 O(帧数) 降到 O(1)。 |
| **D23** | **整帧数转 `long` 必须饱和保护** | 实测（.NET 8）：`(long)Math.Floor(∞)` 饱和为 **`long.MinValue`（负数）**，于是旧代码的 `if (pending > 0)` 判定为假，**整个丢弃记账被跳过**——`DroppedStepCount` 停在 0，`_accumulator` 永久停在 `4.49e307` 且每次 `Advance` 都返回上限帧数（永久满速追帧、账目不可见）。旧文档曾写作「溢出为负 + NaN」，实测不成立，已更正。现加 `MaxSafeWholeSteps = 1e15` 夹紧 + `SafeAddDropped` 饱和加法 + 末尾余量收敛。 |
| **D24** | **新增 `IsFrameSyncIntact` 不变量断言属性** | `CurrentState.Frame - 初始帧号 == Scheduler.TotalSteps`。供测试与调试使用，可证明异常后不存在半提交状态。 |
| **D25** | **记账方法 `CommitStep()` / `RecordDroppedSteps()` 收为 `internal`** | 若保持 public，任何程序集都能 `session.Scheduler.CommitStep(999)`，让 `TotalSteps` 跑到权威状态前面，`IsFrameSyncIntact` 形同虚设（K9）。改为 `internal` + `InternalsVisibleTo("BattleCore.Tests")`：生产代码只有 `BattleSession` 能记账，测试仍可直接驱动调度器。新增反射测试 `AccountingMutators_AreNotPartOfPublicApi` 固定该 API 形状。 |

### 阶段 03A 决策（基础输入链路）

| # | 决策 | 理由 |
|---|---|---|
| **D26** | **输入分两层：原始层（`InputBits`）+ 解析层（`DirectionResolver`）** | 原始层是被逐帧录制/重放的数据；解析层是可随规则演进的计算。混在一起会让「规则一改，旧录像全废」。 |
| **D27** | **SOCD：左右同时 → 水平中立；上下同时 → 上优先** | 左右对称抵消是当前数据格式下唯一无歧义的规则：`FrameInput` 只存电平位，没有事件顺序，「后输入优先」无法从位掩码还原。上下「上优先」符合 2D 格斗跳跃优先的惯例。要改「后输入优先」必须先扩展录制格式（后续阶段）。 |
| **D28** | **录制绝对方向，相对朝向只在解析时用 `Facing` 换算** | 朝向属于权威状态且会随换边变化；把它烘进输入会让同一份日志在换边后失效，也会让 P1/P2 需要两套录法。 |
| **D29** | **输入历史由 `BattleCore.Input.InputBuffer` 保存** | 放在表现层就无法无 GUI 测试，也无法保证「历史帧号 == 状态帧号」。`Prepare`/`Commit` 对非连续帧号直接抛异常，把错帧挡在源头（`Take` 已收为 internal）。 |
| **D30** | **按下边沿闩锁：`pending \|= (now & ~held)`，`Take` 时消费一次即清零** | 一个渲染帧可能推进 0/1/N 个逻辑帧。闩锁保证边沿必定落在紧随其后的那一个逻辑帧上：0 帧时不丢，N 帧时不重复消费。 |
| **D31** | **两次逻辑帧之间的多次按下合并为一帧，并累加 `CoalescedPressCount`** | 电平语义无法表达「同帧内按了两次」。选择显式计数而不是静默丢弃，使合并可观测；逐次事件流留给指令识别阶段。 |
| **D32** | **暂停与丢弃预算都不产生输入帧；已闩锁的边沿保留到恢复后第一帧** | 不为不存在的逻辑帧伪造输入。暂停期间按键不会丢，恢复后第一帧带上它；被丢弃的时间预算本来就没有模拟，自然没有输入。 |
| **D33** | **Godot 层按「按键事件」采样，而不是每个渲染帧轮询** | 轮询会漏掉「同一渲染帧内按下又松开」。事件驱动 + `HashSet<Key>` 推导按住状态，还能让一个输入位绑定多个键（小键盘 + 标点键都映射 P2 攻击）。窗口失焦时 `ReleaseAll()` 防止卡键。 |

### 阶段 03A 专项补修决策（输入一致性）

| # | 决策 | 理由 / 证据 |
|---|---|---|
| **D34** | **`History` 用 `ReadOnlyCollection<T>` 包装底层 `List`；默认只保留最近 600 帧（`IsHistoryTruncated` 标记淘汰）** | `IReadOnlyList<T>` 只是接口：实测外部可强转回 `List<FrameInput>` 并 `Add/Clear` 改写内部状态。改为构造时创建**单实例**只读包装（不产生每次访问的拷贝）：强转 `List` 返回 null，`IList<T>.Add` 抛 `NotSupportedException`。容量上限把「指令识别短期历史（10 秒窗口）」与「完整录像」分开，后者需要独立系统。对齐判断改用 `IsAlignedWith(frame)`（最后一帧 == frame-1），**不用** `History.Count == Frame`——有容量上限后该等式不再成立。 |
| **D35** | **输入两阶段提交：`IInputSource.Prepare` → `Step()` → `Commit()`** | 旧 `Take()` 立即写历史并清边沿，`Step()` 失败后留下幻影帧，重试同一帧还会因帧号不连续而抛异常（已用复现测试证实）。现在：准备阶段不写历史/不清边沿；模拟成功后才提交输入，再记账。失败帧不写入历史、不消费边沿，重试得到同一输入。`Take()` 收为 `internal`，防止表现层把它当作 `Func<int,FrameInput>` 传给会话。 |
| **D36** | **候选状态最后落盘：`nextState` 在 `Commit` 成功前不赋给 `CurrentState`** | 旧顺序「`CurrentState = Step(...)` → `Commit` → `CommitStep`」在 `Commit` 抛异常时会让状态前进而计数不动，直接破坏帧同步不变量（已用 `CommitFailSource` 复现：帧号 1 vs 期望 0）。现改为 `nextState = Step(...)`（局部变量）→ `Commit` → `CurrentState = nextState` → `CommitStep`。`Advance` 与 `StepExact` 共用该顺序。 |

### 阶段 03B-1 决策（基础指令识别）

| # | 决策 | 理由 |
|---|---|---|
| **D37** | **指令识别放在 `BattleCore.Commands`，完全数据驱动** | 新增指令只加 `CommandDefinition` 数据，识别器内没有 per-move 分支。识别器可在无 GUI 环境下用逐帧日志完整测试；Godot 只负责显示。 |
| **D38** | **方向历史按「变化」压缩，条目保存当帧朝向解析后的相对方向** | 按住 N 帧只算一个元素，才能区分「↓ 按住 10 帧再 →」与真正的 ↓↘→。每个条目在**它发生的那一帧**的朝向下经 `DirectionResolver` 换算；换边只影响之后的条目，**不得**用当前朝向重算历史（否则换边前的输入含义被改写）。 |
| **D39** | **末方向允许在按键容差内「稍早完成」** | 按键可能在末方向之后 1–3 帧才按下；若只允许「末方向 == 当前方向」，623（→↓↘）在继续滚到 → 后就永远无法成立，236 与 623 也无法在同一帧竞争。匹配从最近的方向条目向前尝试，末尾条目距当前帧 ≤ 容差即视为候选。 |
| **D40** | **冲突裁决：优先级降序 → 序列长度降序 → 指令 ID 序数升序** | 完全确定，与字典遍历、定义顺序、对象创建顺序无关（测试用「定义顺序反转」固定）。优先级满足「升龙压波动」的格斗惯例；长度作为次级键让更复杂的指令压过更简单的；ID 序数只作最终兜底。 |
| **D41** | **按键边沿一次性消费；识别结果按逻辑帧过期** | 一次 0→1 边沿最多完成一条指令，消费后同一边沿不会在后续帧重复触发。结果带 `ExpiresAtFrame = RecognizedFrame + BufferLifetimeFrames`，过期在下一帧 `Observe` 时移除；`LastMatch` 仅供显示，不参与消费语义。 |
| **D42** | **`BattleSession` 提供可选 `frameCommitted(input, state)` 观察者** | 指令识别需要「当帧输入 + 当帧朝向」，且必须发生在状态/输入/记账都提交之后。观察者在 `CommitStep` 与 `committed++` 之后调用，不改变调度与提交契约；契约要求实现不得抛异常（若抛，帧已提交且记账保持一致，异常照常传播）。 |

### 阶段 03B-1 补修决策（指令竞争 / 校验 / 连续性 / 朝向时序）

| # | 决策 | 理由 |
|---|---|---|
| **D43** | **手势消费：胜出指令的「方向序列末方向帧号」被标记为已消费** | 复现出的幽灵指令：同一方向手势（↓↘→）在帧 2 用 A 边沿让 `236A`（优先级 20）胜出后，落败的 `236B` 在帧 3 仍能用帧 1 残留的 B 边沿 + 帧 2 的末方向匹配成功。修复：胜出后把该手势的末方向帧号加入 `ConsumedEndFrames`，`TryMatch` 跳过已被消费的手势。末方向帧号与方向条目一一对应且唯一，因此「同一手势」被唯一锁定；两个独立手势的末方向帧号不同，互不影响——这区分了「冲突落败」与「两个独立手势先后完成」。 |
| **D44** | **`Observe` 帧号契约：首次允许非负初始帧，此后严格逐帧连续** | 快照恢复可能从中途帧开始，故首次不限起始帧；但识别器的方向/按键历史依赖「每帧恰好一次」，重复/回退/跳过都会破坏历史连续性。改为 `input.Frame == lastObservedFrame + 1`（首次为任意非负），非法帧在修改任何内部状态前抛出。与 `BattleSession` 的逐帧提交机制一致。 |
| **D45** | **`CommandDefinition` 构造期完整校验：相邻方向不得相同、枚举值必须合法** | 识别器按「方向变化」压缩历史，定义里相邻相同方向是冗余/错误，会让序列匹配逻辑产生歧义。`Enum.IsDefined` 拒绝非法枚举值；方向序列 `ToArray()` 复制，外部改动源数组不影响定义。 |
| **D46** | **`frameCommitted` 观察者传入「帧开始状态」，其 `Facing` 是当帧输入被消费时的朝向** | 输入在本帧开始被消费，其朝向语义必须取帧开始（`stateBefore`）而非帧结束（`nextState`）。未来引入帧内转身（自动转身、跳跃越身后），若用 `nextState.Facing` 会把「转身前按住的方向」按「转身后」重解释。实现上在 `Step` 前捕获 `CurrentState` 作为 `stateBefore` 传入观察者，`Advance` 与 `StepExact` 一致。 |

### 阶段 03B-1 最终补修决策（跨末方向帧幽灵指令 / 观察者失败封闭）

| # | 决策 | 理由 |
|---|---|---|
| **D47** | **同帧冲突裁决消费「所有合法候选」的按键边沿，而非仅胜者** | 之前的 `ConsumedEndFrames` 按「末方向帧号」锁定手势，只能抑制「共享同一末方向帧」的竞争。任务书复现出：623A（末方向 ↘）与 236B（末方向 →）末方向帧不同、却参与同一次裁决时，623A 胜出后 236B 仍能借残留 B 边沿 + 新的 → 末方向在下一帧幽灵触发。修复：同一帧收集全部合法候选，胜者与落败候选的按键边沿**一次性全部消费**。这既抑制幽灵指令，又不消费落败候选的方向手势（保留给独立手势），且与定义顺序无关。变异验证：只消费胜者边沿 → 4 项失败。 |
| **D48** | **观察者异常后会话进入 `IsFaulted` 故障封闭，`Recover()` 前拒绝继续模拟** | 战斗帧提交（状态/输入/记账落定）并不自动保证派生的指令识别状态同步——观察者（`frameCommitted`）抛异常时识别器可能未完成该帧的 `Observe`。若静默继续，下一帧会被误当成识别器的连续历史帧，造成派生状态漏帧。修复：`Advance`/`StepExact` 在观察者调用处单独 `try/catch`，捕获后 `MarkFaulted(input.Frame)` 并照常 `throw`（不吞异常）；两者开头 `ThrowIfFaulted` 拒绝未恢复继续。只有观察者异常触发 Faulted，`Step`/`Commit` 的失败不误标。调用方恢复派生状态一致性后调 `Recover()` 清障。 |

### 阶段 03B-2 决策（蓄力与扩展指令）

| # | 决策 | 理由 |
|---|---|---|
| **D49** | **蓄力用「方向序列第一个元素的持续帧数」表达，`DirectionEntry` 增加 `EndFrame` 记录持续结束帧** | 方向历史按「变化」压缩，按住 N 帧只产生一个条目，单靠 `Frame`（进入帧）无法知道保持时长。给条目增加 `EndFrame`（方向不变时实时更新为该帧），蓄力时长 = `EndFrame - Frame + 1`。`CommandDefinition` 新增 `ChargeHoldFrames`（默认 0 = 无蓄力）：序列第一个元素必须连续保持 ≥ 该帧数。**蓄力只认纯方向**——斜方向是独立条目，会打断纯方向的连续保持，因此斜方向不计入蓄力（明确规则，不依赖位运算）。 |
| **D50** | **双重/超必杀指令直接用更长的方向序列表达，复用同一冲突裁决与手势消费** | 236236A 的方向序列就是 `[↓,↘,→,↓,↘,→]`（6 元素），无需为每个超必杀写独立分支；`MaxSequenceFrames` 放宽到 40 帧容纳两段。长指令与短指令在同一帧竞争时按「优先级降序 → 长度降序」裁决（双重优先级 > 单段），且胜出指令的手势消费（D43/D47）保证长指令完成不立即借同一历史重复触发短指令。 |
| **D51** | **蓄力时长与方向历史解耦：每条蓄力指令一个「释放挂起」槽位，`MaxSequenceFrames` 不约束蓄力** | 复现出的缺陷：蓄力指令同时受「首尾跨度 ≤ `MaxSequenceFrames`」约束——`[4]6A` 保持 61 帧（> 60）就无法匹配，且 `Prune()` 会把长蓄力的起始条目裁掉，超长蓄力必然失效（修复前 61/120/600/3600 帧共 5 项测试失败）。修复：离开蓄力方向的瞬间从历史条目**快照**蓄力量与释放帧进 `ChargePendingState`（每指令一个固定槽位，3 个 int），此后匹配只依赖挂起状态 + 尾段历史。蓄力可保持任意久、内存不随时长增长、普通方向历史上界不变、有效蓄力不被 `Prune()` 提前清除；超时概念分离——`MaxSequenceFrames` 只管普通方向指令，蓄力由释放窗口与按键容差约束。蓄力计数只发生在 `Observe`（已提交帧）：暂停与被丢弃的逻辑时间不产生提交帧，不虚增蓄力。`Advance`/`StepExact` 走同一观察路径，结果一致。变异验证见 PROGRESS「最终补修」。 |
| **D52** | **蓄力释放窗口 `ChargeReleaseToleranceFrames`（默认 3 帧）：窗口内只容忍中立与尾段方向** | 复现出的缺陷：`[4]6A` 要求「后 → 前」在压缩历史中直接相邻，蓄力后松开 1 帧（`后→中立→前`）即失效；旧测试 `ChargeBackForward_ReleaseThenPress_WithinWindow_StillMatches` 实际没输入中立帧，没验证到真正的释放容差。修复：离开蓄力方向时进入「释放挂起」；窗口内出现**中立（含 SOCD 左右抵消）**容忍、**尾段方向**容忍、**任何无关方向立即失效**（释放瞬间直接进入无关方向同样立即失效）；序列末方向进入帧 − 释放帧 ≤ 窗口才可完成；按键容差不延长释放窗口（方向超窗即死）；蓄力不足不能靠窗口补足；窗口内重新按回蓄力方向 → 从 0 重新计数。普通与双重指令保持严格连续规则不变，不默认忽略中立。 |
| **D53** | **蓄力是一次性资源：产生过匹配候选的蓄力指令挂起立即作废** | 若不作废，同一蓄力可在释放窗口内借第二次按键边沿反复触发（蓄力 10 帧 → 前+A 识别 → 松开再按 → 又识别）。修复：`Observe` 的候选收集阶段记录所有产生候选的蓄力指令，裁决后统一清空其挂起。与 D41（按键边沿一次性消费）和 D43（手势消费）共同保证：按住 A 不重复、旧蓄力不无限触发、重新独立蓄满才可再次识别。 |
| **D54** | **蓄力尾段必须属于本次释放之后：`ReleasedAtFrame ≤ 末方向进入帧 ≤ ReleasedAtFrame + 释放窗口`** | 复现出的缺陷：释放窗口检查只有上界（`endFrame − ReleasedAtFrame > 窗口 → 拒绝`），没有下界。复现场景：前(0) → 后(1，蓄 1 帧) → 中立(2，释放) → 中立+A(3)——旧 Forward(0) 满足 `0 − 2 = −2 ≤ 窗口`，且按键(3) 与旧条目恰好相差 3 帧（按键容差内），于是借**释放前的旧条目**幽灵完成 46A，即使本次释放后从未重新输入前。修复：匹配时补一行下界约束 `endFrame < ReleasedAtFrame → continue`。尾段与按键容差的关系不变（方向必须先在窗口内重新进入，按键容差只约束按键相对末方向的时机，不能复活失效蓄力）。变异即修复前实测：`StaleForwardBeforeCharge_CannotBeBorrowedAfterRelease` 等 2 项失败 → 修复后 10 项全部通过，全量 267/267。 |

### 阶段 04 决策（角色状态机与招式系统）

| # | 决策 | 理由 |
|---|---|---|
| **D55** | **指令识别纯函数化并进入权威状态：`RecognitionState`（不可变快照 + `Advance` 纯推进）随 `FighterState` 携带，`BattleSimulation.Step` 内完成「识别 → 缓冲 → 消费 → 状态机推进」（零帧延迟）** | 若识别仍留在「提交后观察者」里，其消费决策无法进入权威转换——重放/旧快照会失去识别历史（B6.3/B6.8 无法满足）。识别规则与 03B 完全一致（D39–D41、D43–D47、D49–D54），仅把可变列表换成不可变数组（每帧小数组写时复制）；`CommandRecognizer` 保留为双玩家门面供既有测试与独立使用。消费发生在 Step 内 = 同帧生效（明确为零帧延迟，有测试固定）；消费标记随快照携带 → 同结果不可能二次消费、跨渲染帧率不可能重复发动。会话的观察者机制与 Faulted 封闭保持不变（仅供显示等只读用途）。纯函数天然无半提交：Step 抛异常或输入提交失败时整个候选状态（含消费标记）被丢弃，重试得到同一结果。 |
| **D56** | **裸按键指令：空方向序列 + 攻击键，仅按键边沿当帧触发** | B3 要求「普通按键对应的基础动作」，而序列指令的末方向必须「新进入」（按键容差从末方向进入帧起算），长期中立/按住方向时无法触发——实测 `[Neutral]` 序列的 5A 在无输入 26 帧后按 A 不触发（中立条目从帧 0 就存在）。空序列 + 攻击键 = 裸按键指令：不依赖方向历史，只认当帧未消费边沿（旧边沿已被消费或按容差裁剪，不会复活）；不产生方向手势锁（按键边沿消费已防重复）；空序列 + 无按键仍拒绝（无法触发）。这是**增量**扩展——此前空序列一律非法，任何既有指令行为不变。 |
| **D57** | **`FighterState` 正式扩展动作状态（`LogicState`/`MoveId`/`PhaseRemainingFrames`）与识别快照；`StateVersion` 升到 2** | 阶段 04 确实需要角色动作状态（B7），保持 readonly record struct 与不可变契约：新字段全部为标量或不可变对象；`RecognitionState` 重写结构化相等，跨实例确定性比较仍成立。不变式：动作期间必须带 MoveId 且剩余 ≥ 1（阶段切换在归零同帧完成，无 0 帧中间态）、待机必须无 MoveId 且剩余 0，`Validate()` 构造期强制。`StateVersion` 1→2 标记格式边界：项目尚无序列化快照（K8），无迁移负担。仍不引入速度/重力/移动规则（阶段 05 决定）。 |
| **D58** | **状态机集中为纯函数 `FighterStateMachine.Tick`：阶段推进 → 待机更新 → 消费，转换边唯一** | 不把状态写成嵌套 if/else 散落各处（B2）：合法转换图唯一（待机↔蹲、待机→Startup→Active→Recovery→待机），由 Tick 单点产生；损坏状态（动作无 MoveId、未配置招式）立即抛出而非静默修正。每帧顺序固定：① 阶段剩余 −1，归零按招式数据切换（Recovery 完成同帧即可待机更新与消费）；② 仅待机时按解析输入切换站立/蹲下（动作不被蹲下覆盖，B9）；③ 消费（D60）。Airborne/Hitstun 等未来状态只做文档预留，不加空枚举值。 |
| **D59** | **角色专属配置：`FighterDefinition`（指令表 + 招式表）与 `FightRoster`（双方配置），构造期校验 + 防御复制** | B4：不同角色不同指令集合；两名角色可有相同 CommandId 映射不同 MoveId（演示配置 Kai/Rei 均有 236A → 不同招式不同帧数）。识别器不认识角色，只按配置给出的指令表匹配；指令→招式映射只存在于配置。校验：指令 ID 重复、招式 ID 重复、招式引用不存在指令、空 ID 全部构造期失败；招式阶段帧数 ≥ 1；传入集合防御性复制（外部改动不影响配置）。空配置 `FightRoster.CreateEmpty()` 保持阶段 01–03 默认行为。 |
| **D60** | **消费契约：`RecognitionState.WithResultConsumed`（显式失败）+ `Tick` 内确定选择顺序** | 识别、缓冲、消费、动作状态转换是四个概念（B5）：每条结果最多成功消费一次（消费标记在快照里）；过期不能消费（过期结果在 Advance 时移除；WithResultConsumed 对不存在/已消费结果抛出）；不属于当前角色的指令不会误消费（按角色配置查招式，无映射则保留）；失败不破坏其他结果（逐条尝试，跳过保留）；不能行动时保留到有效期结束、恢复可行动同帧可消费；选择顺序 = 缓冲顺序（识别帧升序，每帧至多一条胜者，确定性）；`LastMatch` 只是显示——绝不据其发动（有「Last 有值但缓冲空 → 不发动」测试）。 |
| **D61** | **识别快照集合所有权：五个公开集合一律只读包装（修复深层不可变性缺陷）** | 复现出的缺陷：`Advance` 把工作 `List<T>` 直接存进 `IReadOnlyList<T>` 属性——接口只是编译期视图，实测可强转回 `List<T>` 并 `Clear()` 修改权威快照；`WithResultConsumed` 暴露裸数组（可经 `IList<T>`/`T[]` 写入）；`Empty` 共享 `Array.Empty`。识别状态随 `FighterState` 进入 `BattleState`，这是权威快照完整性问题（15 项复现测试失败）。修复：五个公开集合（Directions/Presses/Buffer/ConsumedEndFrames/ChargePendings）在构造点一律包装为单实例 `ReadOnlyCollection<T>`（与 D34 `InputBuffer.History` 同一先例）——强转 `List<T>`/`T[]` 得 null，`IList{T}`/非泛型 `IList` 写操作抛 `NotSupportedException` 且不生效。内部构造保留可变 List 高效流程，只在快照落定点包装一次；每帧状态各持独立包装（不共享可写底层），只读包装之间共享安全（`Empty` 同样包装）。哈希与相等语义不变：相等状态哈希一致、内容比较不受包装影响。 |
| **D62** | **非法 `FighterLogicState` 两道门拒绝：`Validate` 构造期 + `Tick` 推进期，均用 `Enum.IsDefined` 集中校验** | 复现出的缺陷：`Validate` 的待机分支把一切非动作状态当待机——`(FighterLogicState)999` 携带空 MoveId 与 0 帧即可通过构造；`Tick` 的 `IsInMove(999)` 为 false，未知状态被静默「修正」成 Standing/Crouching。修复：两处入口都用 `Enum.IsDefined`（唯一事实来源 = 枚举定义本身，不会维护出两份不一致的状态清单）——Validate 无论其他字段是什么一律拒绝；Tick 推进前独立拒绝（不依赖构造代为校验）。合法状态（含收招回待机）行为不变；负剩余帧数拒绝保持。实测：损坏状态在 BattleState 构造期即被拒（第一道门），Tick 校验为第二道门。 |

### 阶段 05A 决策（定点数与地面运动）

| # | 决策 | 理由 |
|---|---|---|
| **D63** | **权威数值采用 Q16.16 定点数 `FixedPoint`（`long` 原始值，Scale = 65536）；`FighterState.PositionX/PositionY` 升级为定点世界坐标（Y 向上、地面为 0），旧整数坐标语义由「int → FixedPoint 隐式转换 = 世界单位」+ `ToIntTruncated`（向零截断只读视图）保留** | Q16.16 的 65536 分辨率足够亚像素运动精度，48 位整数部分（±2^47 单位）运动场景不可能触界；全整数算术天然无 NaN/Infinity，跨平台逐位一致。规则：加减 `checked`（溢出抛 `OverflowException`，绝不静默回绕）；乘除用 `Int128` 中间结果（不丢精度）后除以 Scale；**取整统一向零截断**（与 C# 整数除法一致，正负对称可重复）；除零显式抛出；`FromFraction` 表达 1/2、7/4 等配置值。**迁移**：`PositionX: -120` 经隐式转换表示 -120 世界单位（内部 ×65536），不是原始定点值——旧默认出生点（-120/120）与 Godot 显示语义不变；Godot 用 `ToDouble()` 显示转换（世界 Y 向上 → 屏幕 Y 向下翻转），浮点结果绝不写回。`StateVersion`/`CoreVersion` 2 → 3 标记格式边界。 |
| **D64** | **运动配置数据化：`MovementDefinition`（步行速度，纯数据）挂在 `FighterDefinition`（可选参数，null 用默认配置）** | A2：P1/P2 可携带不同运动数据（演示配置 Kai 前 2.0/后 1.5、Rei 前 1.75/后 1.25 单位/帧）；速度存**大小**（恒正），方向由朝向与输入在运动函数里决定，配置不携带符号；构造期校验 > 0 且 ≤ 每帧 32 单位（健全性上限，荒谬配置即失败）；状态机不散落速度常量，Godot 不计算权威速度。跳跃/重力字段留待 05B 按需扩展，不预留空配置。 |
| **D65** | **地面运动纯函数 `GroundMovement.StepX`：仅站立待机响应前后输入，按「本帧结束逻辑状态」门槛 + 帧开始朝向换算，步移 = 配置速度 × 朝向符号** | A3 表：Standing 允许、Crouching/Startup/Active/Recovery 不允许（动作期间不被移动输入打断；收招归零同帧回站立即可恢复步行——与 D58 同帧语义一致）。输入已由 `DirectionResolver` 按「当帧开始朝向」（D46）解析为相对方向，运动函数只做「前/后 → 世界 ±」换算——朝左按前，世界坐标减小，与方向名无关。SOCD 左右中立由既有解析保证，运动系统不另起炉灶。步行速度不进入快照：停止即静止，无残余动量、无衰减规则。跳跃/重力/边界/推挤属 05B。 |

### 阶段 05B 决策（跳跃、落地、场地边界、推挤、朝向）

| # | 决策 | 理由 |
|---|---|---|
| **D66** | **`MovementDefinition` 扩展跳跃参数：`PreJumpFrames`（1..30 整数帧）、小跳/大跳垂直初速、重力大小、水平跳速；构造期校验「大跳初速 > 小跳初速」与各项上限** | B2 要求小跳/大跳有明显高度与腾空差——由构造期校验保证（相等或反向配置直接失败），不靠数值巧合。重力存大小（恒正），积分时向 -Y 施加（D63 世界坐标 Y 向上）；速度存大小，方向由运动函数决定（与步行一致）。演示配置：Kai 预备 3 帧、小跳 4.0（腾空 14 积分帧、最高 14）、大跳 6.5（腾空 25 帧、最高 39）、重力 0.5、水平 3.0；Rei 全部不同。 |
| **D67** | **起跳请求 = 「上」按下边沿：`FighterState.UpHeldLastFrame` 保存上一逻辑帧解析后的「上」电平（随快照携带），边沿 = 本帧按住且上一帧未按住；边沿在站立/蹲下待机（含收招归零同帧）进入 `PreJump`（预备窗口 = `PreJumpFrames`）** | B5 防自动连跳的关键：落地后仍按住「上」→ `UpHeldLastFrame` 为 true → 无边沿 → 不再起跳；松开再按才重新武装。电平随快照携带保证重放确定性。PreJump 期间：不消费指令、不做待机更新（蹲下不覆盖）、不移动（不可取消——明确语义）；窗口归零同帧起跳。SOCD 覆盖：上+下 → 上优先（D27）→ 跳跃请求胜出（测试固定）。 |
| **D68** | **小跳/大跳与水平方向都在「起跳帧」（预备窗口末帧）按当帧输入一次性锁定：末帧「上」仍按住 → 大跳（`JumpKind.HighJump`），否则小跳；前/后按住 → 水平初速 ±`HorizontalJumpSpeed`（按朝向换算），否则 0；锁定结果经 `FighterActionTick.Launch`（`JumpLaunch`）交给运动阶段，`JumpKind` 随快照携带供只读显示** | 「不能在起跳第一帧根据未来按键时长决定跳跃类型」——分类只用已观察到的输入（预备窗口末帧电平），无预知、无回溯。空中不改变已锁定的速度（B3 推荐语义：起跳确定水平运动、无空中控制；测试固定「空中反向按住不改变 X 线性轨迹」）。空中重复按「上」不重新起跳（Airborne 分支忽略输入）。 |
| **D69** | **空中定点积分顺序固定（每帧、含起跳帧）：① vy' = vy − g → ② x' = x + vx、y' = y + vy' → ③ y' ≤ 0 即落地：y 归 0、速度归零、状态回 Standing；落地是唯一由运动阶段写入的状态边；落地帧不消费指令（状态机已按空中运行），落地后第一个地面帧恢复消费** | 任务书 B4 推荐序列（加速度 → 速度 → 坐标 → 触地 → 修正）原样固定，无起跳帧例外（首帧高度 = v0 − g，测试逐帧 pinned）。落地帧语义明确：贴地/零速/回站立同帧完成（不穿地、不弹跳、不悬空）；「落地帧不消费、下一帧消费」是显式选择并测试固定（区别于 Recovery 归零同帧可消费——落地发生在状态机之后，无法同帧回到消费路径）。Validate 新不变式：Airborne 必须 Y>0 且带 JumpKind；地面状态必须 Y=0、零速、无 JumpKind——新枚举值不能绕过 D62 校验。 |
| **D70** | **`StageDefinition`（模拟级配置，非角色配置）：MinX/MaxX/GroundY/MinimumFighterSeparation；双方候选 X 一律钳制到 [MinX, MaxX]（含空中、含边界值）——不穿墙、不反弹、不振荡** | 5.1：左/右墙、空中靠近边界、落地修正统一由同一钳制函数处理；`ClampX` 是纯函数。默认演示场地 X ∈ [-640, 640]、地面 0、间距 60（与 1280 宽视口对齐）。构造期校验：MinX < MaxX、0 ≤ 间距 ≤ 场地宽度。 |
| **D71** | **双人最小间距（仅双方都非空中时强制）：基于同一帧的两个候选位置对称分配（各推 overlap/2）；完全重叠平局规则固定——P1 向左、P2 向右；推挤后重新钳制边界（墙优先于间距：靠墙时最终间距可小于最小值，但绝不越界）；一方持续推进时整体沿间距线确定性蠕动** | 任务书 5.2：这是基础位置约束而非碰撞系统。对称分配 + 同帧候选 → 计算顺序无先手优势（镜像场景测试逐帧验证）。空中不推挤（允许越身）；落地帧落地者视为地面 → 落地位置冲突在落地帧按同一规则解决（测试固定）。overlap/2 向零截断（D63 统一规则）。 |
| **D72** | **自动朝向在帧末（推挤之后）更新：仅地面待机（站立/蹲下）转向对手最终位置；X 相等保持原朝向（不抖动）；PreJump/Airborne/动作三阶段朝向冻结；本帧输入始终按帧开始朝向解析（D46 不变），帧末朝向只影响后续帧** | 5.3：跳跃越身后落地互脸（测试固定）；等距平局不抖动（保持原朝向，测试固定）；动作中不转身（招式朝向锁定）。D46 契约完整保留：识别、运动、朝向更新各自使用规定时刻的朝向，禁止用新朝向重解释本帧输入。 |

### 阶段 05 最终专项补修决策（推挤求解 / GroundY / JumpKind）

| # | 决策 | 理由 |
|---|---|---|
| **D73** | **双人位置统一求解取代「平分后钳制」（D71 修订）：① 顺序约束——帧开始双方都在地面时以帧开始相对顺序为约束，禁止地面角色单帧交换左右（高速相向不得穿越）；涉及空中（合法越身/落地帧）以候选顺序为准，新地面顺序在落地帧建立；② 墙角残差再分配——对称分配并钳制后间距仍不足时，剩余量由仍有墙余量的一方承担，墙角不再残留重叠（贴墙 640 时对方收敛到 580，废除旧「墙优先于间距」允许的 582/640 残留）；③ 奇数原始单位的余数显式归属 P1（与平局规则同源，身份映射下镜像一致）；闭式求解，无迭代、无逐帧震荡** | 复现出的两个真实缺陷：旧算法只看运动后候选坐标——(a) 一方贴墙时残留 582/640（间距 58，且旧测试把它固定为预期）；(b) 双方 32 单位/帧相向冲刺时单帧交换左右被当作合法站位（P1=+30/P2=-30 的地面穿越）。修复保留 D71 的对称分配与平局规则，补上「帧开始顺序」与「残差再分配」两个约束；deficit/2 对偶数原始值精确、奇数原始值截断后余数显式归属 P1（1 raw 缺口精确消除，无永久残差）。空中越身不受影响（有测试固定越身合法与落地建序）。 |
| **D74** | **`StageDefinition` 构造期只接受 GroundY=0：任何非零（含正负与分数）GroundY 抛 `ArgumentException`** | 复现出的不一致：GroundY 字段可配置非零值，但落地判定（Y=0）、`FighterState.Validate`（地面必须 Y=0）、默认出生高度、Godot 显示基准全部写死 Y=0——非零配置会产生双重权威地面高度。当前只有水平场地，采用最小修复：拒绝非零并文档声明；将来需要平台/斜坡/地形时统一扩展出生位置、积分、落地与状态校验，不得只改一个判断。 |
| **D75** | **`FighterState.Validate` 用 `Enum.IsDefined(JumpKind)` 完整拒绝未定义跳跃类型（与 D62 同一模式）** | 复现出的漏洞：`(JumpKind)999` + Airborne + Y>0 能通过「JumpKind != None」检查进入权威快照（地面状态因「必须 None」反而早已拒绝，但理由不同）。修复后未定义值在任何状态下都拒绝且错误消息含具体非法值；`None` 仍只能用于地面。`JumpLaunch.Kind` 只由状态机产生（ShortHop/HighJump），无其他公开入口可注入非法值。 |

### 阶段 06A 决策（AABB 判定框与攻击数据）

| # | 决策 | 理由 |
|---|---|---|
| **D76** | **攻击判定采用 AABB（`FixedRect`，MinX/MaxX/MinY/MaxY 全定点）；三框职责严格划分：Pushbox（显示/查询，权威推挤仍由 D73 独占）、Hurtbox（姿态受击框：站立系 120 高、蹲下 70、空中跟随 PositionY）、Hitbox（只来自招式数据 `AttackDefinition`，仅 Active 配置帧范围生效）；相交 = 正面积重叠，边缘接触不算命中，无 epsilon** | 当前无旋转/骨骼，AABB 定点判定可逐位验证、镜像简单。`FixedRect` 构造期拒绝零/负宽高；`AttackDefinition`（前向/向上偏移 + 半宽高 + Active 内 0 起始帧范围 + AttackHeight）挂在 `MoveDefinition.Attacks`（可空 = 无攻击招式），范围超出 ActiveFrames 构造期失败（`int.MaxValue` 哨兵 = 至末帧）。**帧语义：Startup → Active 转换完成的那一逻辑帧即 Active 第 1 帧（elapsed = ActiveFrames − 剩余）**，该帧即产生攻击框；Recovery 首帧不再有。镜像只由权威 Facing 施加（前向偏移 ±），与 Godot 精灵翻转无关；攻击框跟随权威位置；不检测攻击者自己。检测输出为 `BattleStepResult.Contacts`（确定性诊断：先攻击者 0 后 1、按攻击序号），**06A 不结算**——伤害/防御/硬直属 06B，重复命中抑制属 06C。Godot 判定框调试（`DebugBoxDrawer` 顶层节点：Hitbox 红/Hurtbox 蓝/Pushbox 绿轮廓，F2 开关）只做权威几何的显示换算，绝不反向参与判定。 |

### 阶段 06B 决策（命中、伤害、防御与硬直）

| # | 决策 | 理由 |
|---|---|---|
| **D77** | **攻击高度（High/Mid/Low）是 `AttackDefinition` 的显式数据配置，不由 Hitbox 世界 Y 坐标推断；防御表：High 只能站防、Low 只能蹲防、Mid 皆可** | 高度是格斗语义（爆头/段位），几何 Y 会随姿态/体型变化，用几何推断会产生隐性耦合。`GuardCovers(Height, 防御姿态)` 是唯一事实来源（纯函数，测试固定整表）。 |
| **D78** | **新增 `Hitstun`/`Blockstun` 状态与 `FighterState.StunRemainingFrames`：命中 → Health = max(0, Health − Damage) + Hitstun；成功防御 → 不掉血 + Blockstun；硬直期间不能步行/起跳/发动招式/防御；剩余 −1 归零同帧回到待机更新与消费（与 Recovery 完成同语义）；动作中受击 → 招式中断（MoveId/阶段清空）；被命中者的结算覆盖其当帧 Tick 结果** | 转换边唯一：可受击状态 → Hitstun/Blockstun → 待机；不存在 Hitstun → Startup 等未定义边。结算在运动/推挤**之后**进行（基于最终候选位置），双方结果先并行解析再统一应用（D80）。Validate 不变式：Hitstun/Blockstun 必须 StunRemaining ≥ 1，其他状态必须为 0——新枚举值不能绕过 D62。体力边界由结算显式钳制 max(0, …)（Health − Damage 无溢出风险：Health ≥ 0、Damage ≤ int.MaxValue）。**空中被击显式跳过**（本阶段无空中受击/防御；几何接触仍可观测），测试固定。硬直中不能再防御 → 多帧攻击在 06B 会穿过 Blockstun 命中（**已知中期行为，06C 的攻击实例去重消除**）。 |
| **D79** | **防御 = 帧末状态为站立/蹲下待机 + 按住「后」（按帧开始朝向解析的相对输入，D46）；防御时机在命中结算前判定，帧末自动转身不回头解释本帧输入；动作中/PreJump/硬直中不能防御** | 用结算时（运动后）的候选状态判定资格——Standing→站防、Crouching→蹲防；「按住后」来自当帧解析输入（与步行/识别同一份 `RelativeInput`），不存在第二套防御输入解释。第一版成功防御不扣血、无削防、无精准防御。 |
| **D80** | **同帧双方互中：命中结算基于同一份结算前状态先并行解析双方结果、再统一写入——先处理的角色不会打断另一角色同帧成立的攻击；双方都命中则各自掉血并进入硬直（不实现攻击相杀）** | 消除「先扣 P1 血导致 P2 同帧攻击落空」的顺序偏差：结算函数对两名被击者各产出至多一个结果（来自确定性接触顺序的第一条），应用阶段才写状态。镜像场景测试固定（身份保持映射下逐位一致）。 |

### 阶段 06C 决策（攻击实例去重与确定性事件流）

| # | 决策 | 理由 |
|---|---|---|
| **D81** | **攻击实例身份 = 「攻击者 ID + 发动帧」（`FighterState.MoveStartedFrame`，消费同帧赋值、随快照携带）；被击者携带 `LastHitAttackStartFrame`（命中与被防御都写入）——同一实例对同一目标只结算一次，新发动（新帧号）可再次命中** | 多帧 Active 的 Hitbox 连续相交会逐帧重复扣血/重复防御结算（修复前实测：5A 两帧扣 60）。去重数据全部在权威快照内 → 可重放、可确定性重建。规则：命中**或**被防御都写入记录（接触即结算）；新实例（发动帧不同）不受影响；被中断的攻击实例随 MoveId/发动帧清空而消失，不会复活；被击记录单调不清空（1v1 下攻击发动帧恒递增）。`Validate`：动作中发动帧 ≥ 0、非动作必须 −1（被击者记录无不变式）。**不通过缩短 ActiveFrames 规避**。StateVersion/CoreVersion 5 → 6。 |
| **D82** | **确定性战斗事件（`BattleEvent`）：EventId = Frame × 16 + 帧内序号（每帧上限 16，超出显式失败）；类型 MoveStarted / AttackActive（每次发动仅首个生效帧）/ Hit（Amount = 实扣伤害，DamageApplied 并入）/ Blocked / HitstunStarted / BlockstunStarted；帧内顺序按权威阶段固定（MoveStarted → AttackActive → 逐被击者 Hit/Blocked + 硬直开始）** | 同初态同输入必得相同 EventId 与顺序（禁用 Guid/时钟/随机数/渲染帧号/对象地址）；每帧上限显式失败防静默碰撞。事件由 `BattleSimulation.Step` 的权威结算产生、随 `BattleStepResult.Events`（只读包装，D61 先例）返回——**事件不参与权威判定**，Godot 只消费。HitstunStarted/BlockstunStarted 与 Hit/Blocked 分列（标记状态转换），DamageApplied 并入 Hit.Amount（避免过度细碎）。 |
| **D83** | **事件与状态提交一致：`BattleSession` 新增可选 `eventsCommitted` 观察者，在 `frameCommitted` 之后、同一 try/catch 内调用——帧完整提交（状态/输入/记账落定）后才投递该帧事件；Step 失败或输入提交失败该帧事件整体不存在；观察者异常进入既有 Faulted/Recover 机制；Recover 不重发已提交事件，消费者按 EventId 去重（进程内语义，不承诺跨进程 exactly-once）** | 复用 03B 的故障封闭模式（观察者异常 → MarkFaulted → 照常抛出）；两阶段输入提交的原子性自然覆盖事件（Step 没跑就没有事件）。本阶段不做可靠消息队列/持久化事件日志/联机同步。 |



### 阶段 06 专项补修决策（交接复核：多攻击框序号 / 实扣伤害 / 事件契约）

| # | 决策 | 理由 |
|---|---|---|
| **D84** | **攻击框查询分两个入口：`FighterBoxes.GetActiveHitboxes` 只给几何（调试绘制用）；结算必须走 `GetActiveHitboxesWithIndex`（返回 `ActiveHitbox(FixedRect Box, int AttackIndex)`）。`AttackContact.AttackIndex` 是攻击定义在 `MoveDefinition.Attacks` 中的<b>原始序号</b>，不是过滤后列表的下标；同一帧多块攻击框全部产生接触（原始序号升序），结算取该被击者的第一条 = 最小序号** | 复现出的真实缺陷：`GetActiveHitboxes` 先按生效帧过滤再返回 Rect 列表，`HitDetection.Collect` 却拿过滤后下标当序号。招式有多个攻击定义且生效窗口错开时（Attack[0] 只在 Active 第 2 帧、Attack[1] 只在第 1 帧），第 1 帧的接触被记成序号 0 → 结算取到错误定义的伤害/高度/硬直（复现：期望 970 实测 990；蹲防 Low 生效但被当成 High 而破防）。修复保留多攻击定义、不同生效窗口、左右镜像、AABB 正面积重叠与 D81 去重；**不通过禁止多攻击定义规避**。同帧多框的选择规则显式固定（最小原始序号）并用测试钉住。 |
| **D85** | **`CombatSettlement.Damage` 表示本次<b>实扣</b>伤害：先 `healthAfter = max(0, Health − Damage)`，再取 `applied = Health前 − Health后` 写回结算结果；`Hit.Amount` 直接取该值。配置伤害只作为钳制前的上限，事件层绝不重算** | 复现出的真实缺陷：体力 10 受 30 伤害时 `Hit.Amount` 报 30（实扣 10），体力 1 受 `int.MaxValue` 伤害时报 21 亿级（实扣 1）。事件是权威结算的产物，必须与「前 − 后」逐位一致（测试断言 `Amount == HealthBefore − HealthAfter`）。0 伤害仍是有效命中（进入 Hitstun，Amount = 0）——伤害与硬直是两条独立规则，本轮不合并。 |
| **D86** | **AttackActive 语义定为「本帧曾进入生效状态」（结算<b>前</b>视图）：`elapsed == min(FirstActiveFrame)`，每个攻击实例恰好发一次。事件收集前先算标记再结算，因为 `ApplyCombatSettlement` 会原地改写 `nextFighters`。EventId 保持 `int`，新增帧号上限 `MaxFrameForEventIds = int.MaxValue / MaxEventsPerFrame`（134,217,727），超出在事件组装处显式抛异常** | 复现出的两个问题：① 事件收集在结算之后，双方同帧在 Active 首帧互中、同时被打断时两个 AttackActive 全部丢失（实测 0 个，虽然该帧攻击确实生效并造成伤害）；② 原实现对多攻击定义错开窗口会重复发 AttackActive，与枚举文档「每次发动只发一次」矛盾——统一为只发一次并写入 XML 文档。EventId 用 int 在 60Hz 下可覆盖约 <b>25.9 天</b>连续对局（远超单场），但溢出会回绕成与历史帧相同的 ID，故显式失败而非静默；升级 `long` 需整体评估公开接口与全部测试，本轮不做（只记录结论）。 |



### 阶段 06 最终收尾决策（AttackActive 负载完整性 / 攻击框只读封装）

| # | 决策 | 理由 |
|---|---|---|
| **D87** | **`AttackActive` 的招式身份来自<b>结算前快照</b>：`CollectAttackActive` 返回 `AttackActiveSnapshot(bool IsActive, string? MoveId)` 数组（纯值、不共享可变状态），事件组装只读该快照；`CollectEvents` 对 `AttackActive` 增加不变式——<b>生效事件必须携带非空 MoveId，否则显式抛异常</b>** | 复现出的真实缺陷：上一轮把「是否生效」提前到结算前，但事件负载仍读结算后的 `fighters[i].MoveId`。同帧互中时双方都在结算中被打断（`MoveId = null`），两个 AttackActive 变成 `MoveId = null`——事件无法追溯到招式，与「该帧实际进入生效的招式」语义矛盾（自带不同招式名复现：两事件均 null）。规格：**结算前快照描述事实，结算后状态描述结果**，两者不得混用；不通过延迟 Hitstun 或保留已中断 MoveId 掩盖。帧数、每实例一次、EventId 公式、同帧并行结算规则全部不变。 |
| **D88** | **`FighterBoxes.GetActiveHitboxesWithIndex` 对非空结果返回 `List<T>.AsReadOnly()`（空结果仍共享 `Array.Empty`）——不可强转 List/数组，`IList<T>` 与非泛型 `IList` 写操作抛 `NotSupportedException`** | 复现出的真实缺陷：返回类型是 `IReadOnlyList`，但非空时底层仍是 `List<ActiveHitbox>`，`result is List<ActiveHitbox> list → list.Clear()` 可清空调用方拿到的「只读」视图（实测强转成功）。违反 D61 已确立的公开只读集合约定（同 `RecognitionState`/`Events`/`Contacts` 先例）。只处理集合所有权：`ActiveHitbox` 仍是 readonly record struct，索引语义/顺序/AABB 几何/旧 `GetActiveHitboxes` 几何视图均不变；只读包装一次构造，不在索引访问时复制。 |



### 阶段 07A 决策（KO 裁决与回合终局）

| # | 决策 | 理由 |
|---|---|---|
| **D89** | **`RoundState`（`Phase` + `Outcome` + `WinnerFighterId`）进入权威 `BattleState`；KO 只依据<b>全部结算完成之后</b>的双方最终体力判定（`FromFinalHealth`），双方同帧归零 = `DoubleKO`（无先手）；终局后 `Step` <b>仍可推进帧号但完全冻结</b>（角色/回合/事件逐位不变）；事件 `Knockout`(胜者→败者) / `DoubleKnockout`(0→1) / `RoundEnded`(Amount = (int)RoundOutcome，Source = 胜者或 -1) 各恰好一次；重开 = 新的 `BattleState` + 新的 `BattleSession`（不复用旧快照）** | 体力归零原先只是数值，没有对局语义。判定放在结算之后是唯一能保证「不因先处理 P1 而跳过 P2 同帧有效命中」的位置（D80 延伸）：双方各自解析、统一应用，再看最终体力。**选择「允许 Step 但冻结」而不是「禁止 Step」**：`BattleSession` 的 `Frame − 初始帧 == TotalSteps` 不变量与 Godot 的 `Advance` 调用都不需要特判，终局不会让游戏进入 `IsFaulted`，也不会重复产生 KO 事件；代价是终局后帧号仍在前进（明示语义）。`RoundState` 的三字段互相约束（InProgress ⇔ None ⇔ 无胜者；P1Win/P2Win ⇔ 胜者 0/1；DoubleKO ⇔ 无胜者），构造期 `Validate` 拒绝非法枚举与不自洽组合，`Finish` 只允许一次。`StateVersion`/`CoreVersion` 6 → 7，仅作格式边界标记（尚无序列化）。**EventId 的唯一性只在同一对局时间线内成立**——重开新对局会从帧 0 重新计数，不承诺跨场次全局唯一。 |



### 阶段 07B 决策（连段统计与招式取消）

| # | 决策 | 理由 |
|---|---|---|
| **D90** | **`ComboState`（Count + AttackerId）作为<b>被击方</b>的记录进入 `FighterState`；一次有效命中后：若目标在<b>结算前</b>处于受击延续状态（`IsHitReaction`：Hitstun / AirHitstun / Knockdown / Wakeup）且连段攻击者相同 → Count+1，否则重新计 1；帧末仍在受击延续中则保留，<b>恢复自由行动的那一帧末清空</b>；成功防御不计数并立即清空；同一攻击实例由 D81 去重，不会重复计数** | 「连段」必须由权威状态定义，不能由 HUD 数事件。用<b>结算前</b>状态判定连续性，与 D80 同时性一致；用<b>帧末</b>状态判「是否还在挨打」，因此「硬直归零转待机」的那一帧明确<b>不再是连段</b>（该帧再被命中按 1 Hit 计，测试固定两种边界）。被击方各自持有一条记录（双方同时受击互不干扰），攻击者 ID 必须 ≠ 自己（`Validate`）。不用 List 保存历史：连段只需要「当前次数 + 谁打的」，历史由事件流负责。 |
| **D91** | **取消规则是纯数据 `CancelDefinition`，挂在<b>来源招式</b>的 `MoveDefinition.Cancels` 上（来源 = 拥有者，不另存 SourceMoveId）；窗口 = 自发动帧起算的经过帧数 `[First, Last]` + 阶段过滤（Active/Recovery）；确认条件三选一以上：命中确认 / 防御确认 / 空挥；确认帧由结算写入攻击者（`HitConfirmFrame`/`BlockConfirmFrame`），且必须<b>来自更早帧的同一实例</b>`（frame > confirmFrame`）；满足条件的第一个「规则 × 缓冲结果」组合胜出并被消费，每帧至多取消一次，目标作为新的独立攻击实例发动（`MoveStartedFrame` = 当帧）** | 取消必须数据驱动——`FighterStateMachine` 里不得出现招式名字面量。窗口用<b>固定逻辑帧</b>而不是渲染时间，`[First,Last]` 与阶段过滤同时满足才算命中窗口（语义可逐帧测试）。「确认下一帧起可用」杜绝了「本帧命中→本帧取消→新招同帧再判定」的循环，且不依赖函数调用顺序。选择顺序沿用 D60 的缓冲顺序 + 规则声明顺序，不建立第二套优先级；未胜出的缓冲结果保持原样（不消费）。取消不检查 `AllowedFrom`（动作中没有待机状态可判定），但只可能发生在招式三阶段内，空中招式限制不受影响。`FighterDefinition` 构造期校验目标存在且非自身；无任何确认条件的规则无法触发，构造期拒绝。确认帧只在当前实例内有效，换招/中断/回待机一律清空（`Validate` 强制）。StateVersion/CoreVersion 7 → 8。 |

### 阶段 07C 决策（击退、击飞、空中受击、倒地与起身）

| # | 决策 | 理由 |
|---|---|---|
| **D92** | **地面击退由攻击配置给出「初速 + 每帧摩擦」，方向 = 命中时双方权威相对位置的「远离攻击者」（完全重合时退化为攻击者朝向）；位移在<b>运动阶段</b>逐帧施加（`x += vx`，`vx` 向 0 衰减且不过零），因此仍然经过场地钳制（D70）与双人最小间距（D73）；速度与摩擦只允许存在于受击/防御硬直，且必须同时为零或同时非零（`Validate`）；防御击退独立配置（`BlockKnockback`，默认 0）** | 击退若一次性改坐标就绕过了 D70/D73；放进运动阶段后，墙角收敛、防穿越、双人推挤全部自动生效（有逐帧测试钉住 4/3/2/1/0 的位移序列）。方向用位置差而不是朝向：越身后仍然「推离」，朝向只作为位置重合时的确定性平局规则。摩擦归零时速度必然已为 0，两者同步清空，避免「有速度但不衰减」的非法快照。 |
| **D93** | **新增 `AirHitstun` / `Knockdown` / `Wakeup` 三状态；击飞 = 攻击配置了垂直初速（`LaunchVelocityY > 0`，构造期强制 `KnockdownFrames ≥ 1`）。时间线：`击飞/空中被击 → AirHitstun（空中积分，持续到落地）→ Knockdown(knockdownFrames) → Wakeup(wakeupFrames) → Standing`；空中受击<b>不携带 JumpKind</b>、不可防御、不可行动；落地计划由 `LandingKnockdownFrames`/`LandingWakeupFrames` 随快照携带并在落地/起身时逐段消费；被非击飞攻击命中的空中目标保持原轨迹、落地直接站起（无倒地计划）** | 「空中被击显式跳过」的 06B 限制在本阶段解除：空中目标同样结算伤害，但必须进入一个能携带非零 Y/速度的合法状态（D62 的校验不能破）。击飞当帧位置仍为 0、垂直速度为正——`Validate` 显式允许这个唯一例外，位置由下一帧的空中积分推进（与起跳同构）。空中受击不设独立计时：本阶段没有任何空中行动，计时没有行为意义（不做为了字段而字段的设计）。落地必倒地由<b>配置</b>保证（击飞必须有 `knockdownFrames ≥ 1`），而不是靠状态不变式强行限制「无倒地计划」——后者会挡住「空中被普通攻击打中」的合法情形。倒地期间无起身无敌（受击资格不受限制），Hurtbox 用蹲下高度（贴地）。StateVersion/CoreVersion 8 → 9。 |

### 阶段 07 最终补修决策（空中推挤资格与防御击退摩擦）

| # | 决策 | 理由 |
|---|---|---|
| **D94** | **「是否参与地面双人最小间距推挤」由唯一的空中判定 `FighterLogicState.IsAirborneLike()`（= `Airborne | AirHitstun`）决定；`Step` 的两处判断——`bothGroundAtFrameStart`（顺序约束来源）与 `ApplySeparation` 的早退——共用它，任何调用点都不得再复制一份状态列表；判定只看<b>逻辑状态</b>，不看 `PositionY`** | 07C 新增 `AirHitstun` 时只改了结算，推挤的两处仍只排除 `Airborne`，飞行中的角色被当地面角色处理：① 与地面角色重叠时被求解器额外推动（实测间隙 30 → 位置 0 被推成 −15、对手 30 → 45）；② 空中越身被「帧开始顺序约束」强行拉回原侧（候选 15 → 回投 −22.5，镜像 22.5）；③ 落地帧无法按候选顺序建立新顺序（应为 35 / −25，实际 −25 / 35）。改用 `PositionY > 0` 同样不行——击飞当帧允许 Y=0 且垂直速度为正（D93）。规则边界保持不变：D70 场地边界仍然钳制空中角色；落地帧的候选状态已是地面状态，因此按<b>候选顺序</b>建立新地面顺序，此后地面帧继续按新顺序防穿越。 |
| **D95** | **`GroundKnockbackFriction` 是地面击退的<b>通用</b>摩擦（命中击退与防御击退共用）；只要 `GroundKnockback > 0` <b>或</b> `BlockKnockback > 0`，构造期就要求它 `> 0`，否则抛 `ArgumentException`；不新增第二个摩擦字段，也不放宽 `FighterState.Validate`** | 修复前只校验命中击退：`groundKnockback: 0, blockKnockback: 4, groundKnockbackFriction: 0` 能通过构造，真实 `Step` 在防御结算后生成 `Blockstun + VelocityX=4 + KnockbackFriction=0`，触发「速度与摩擦必须同时为零或同时非零」而<b>整帧提交失败</b>（实测异常：`角色 1 处于 Blockstun，击退速度 4 与摩擦 0 必须同时为零或同时非零`）。不自洽的配置必须在<b>数据构造期</b>被拒绝，而不是等到对局中才爆发；分别配置命中/防御摩擦属于另一项设计决策，本阶段不做。 |

### 阶段 08A 决策（命中停顿与动作有效时间）

| # | 决策 | 理由 |
|---|---|---|
| **D96** | **命中停顿由攻击数据配置（`AttackDefinition.HitstopFrames`，0..30）。时间线：命中/成功防御<b>当帧照常完成</b>伤害与事件结算，停顿从<b>下一逻辑帧</b>起冻结后续 N 帧，第 N+1 帧恢复；攻击方与被击方冻结<b>同一数值</b>，同帧多来源取最大值。冻结内容包括动作阶段倒计时、硬直计时、击退衰减、空中积分、落地推进、取消窗口与新招式消费；<b>只有输入识别继续推进</b>（缓冲不丢失、不消费）。取消窗口与 HUD 的「第几帧」改用 `FighterState.MoveElapsedFrames`（动作有效经过帧数，发动当帧 0，只在未冻结的帧 +1）而不是 `Frame − MoveStartedFrame`；无停顿时两者恒等，旧时间线逐位不变。KO 仍在命中当帧判定，D89 的终局冻结优先于停顿。** | 停顿必须由内核状态表达，不能靠暂停引擎（D1 的战斗结算独立于 Godot）也不能改固定调度器；`MoveElapsedFrames` 是唯一能同时满足「动作冻结」与「取消窗口不提前过期」的时间基准——若继续用全局帧差，8 帧停顿会把 5A 的 3..10 帧窗口整个吃掉（有专项测试与变异验证钉住）。输入识别留在冻结之外，是为了让「停顿期间按的键」仍然进入缓冲（可以被消费一次，但不会被重复消费）；代价是识别缓冲按逻辑帧计时，超长停顿可能让过早的输入过期——这是显式取舍。`AttackActive` 取结算前快照（D87），冻结帧会重复满足「首个生效帧」，因此快照收集显式跳过冻结角色。命中停顿不写入事件流（只体现在状态与 HUD），避免挤占每帧 16 条的事件预算。StateVersion/CoreVersion 9 → 10。 |

### 阶段 08B 决策（地面投技）

| # | 决策 | 理由 |
|---|---|---|
| **D97** | **投技是招式数据（`MoveDefinition.Throws` → `ThrowDefinition`），由现有指令识别器与招式时间线驱动，不另建按键系统。判定<b>不是</b> Hitbox×Hurtbox，而是「投技者处于 Active + 当前经过帧在投技窗口内 + 目标状态可投 + 目标在投技者面前 + 权威水平距离 ≤ Range」。不可投状态（PreJump / Airborne / AirHitstun / Knockdown / Wakeup）由 `AllowsTarget` <b>按规则</b>拒绝，与「有没有 Hurtbox」无关；防御不提供任何投技免疫。冲突规则（唯一表）：① 投技者本帧被有效打击命中 → 投技被打断；② 目标本帧被有效打击命中 → 抓不住（避免同帧双重结算）；③ 双方同帧都有有效投技 → <b>两边都不成立</b>并发一条 `ThrowClash`；④ 否则成立。成功后：扣血 → 目标 `Knockdown(knockdownFrames)` → `Wakeup(wakeupFrames)` → 待机，并沿「远离投技者」平移 `PushDistance`（随后按 D70 边界与 D73 最小间距显式钳制）。同一实例对同一目标至多结算一次（`LastThrowStartFrame`，与 D81 同构）。** | 投技必须能被「距离/状态」这类数据完全描述，否则会退化成硬编码特判；用面对面 + 距离判定而不是新增一类 Hurtbox，是为了让「投技免疫」成为显式规则（倒地/起身/空中不可投），而不是几何巧合。互投必须对称——固定 P1 胜会让身份映射下的镜像测试立刻失败（有变异验证）。打击优先于投技，是因为打击已经结算完才轮到投技；这样「同帧结果」只依赖双方共同的结算前状态，不依赖遍历顺序。投技位移是<b>显式放置</b>而非物理积分，因此必须在同一处同时做完 D70 与 D73 钳制——这是 D92「位移只在运动阶段」的唯一例外，写在这里以免被误当成遗漏。事件：`ThrowAttempt`（每实例首个生效帧一次，兼作「挥空」证据）、`ThrowHit`（Amount = 实扣伤害）、`ThrowClash`（每帧至多一次）。基础规则下「投技造成 DoubleKO」<b>不可达</b>：双方互投必定冲突（无伤害），而单方投技被对方的有效打击打断，因此投技不可能与另一次致命伤害同帧同时成立。StateVersion/CoreVersion 10 → 11。 |

### 阶段 08C 决策（攻击相杀）

| # | 决策 | 理由 |
|---|---|---|
| **D98** | **相杀 = 两个攻击实例在同一帧都有生效攻击框、<b>正面积重叠</b>（D76，仅边缘接触不算），<b>且双方都没有用该实例打中对方受击框</b>（攻击在空中对撞）。任一方真正打中受击框的帧按 D80 正常结算（互中或单方命中），<b>不判相杀</b>——同一帧不能既相杀又命中。裁决粒度是<b>攻击实例</b>：只要有任意一对攻击框重叠就整实例相杀；只要有任意一框真正命中就按命中结算。相杀效果：双方攻击中断，直接进入本招式的 `Recovery`（`PhaseRemainingFrames = RecoveryFrames`），确认帧清空，无伤害；停顿取双方招式 `HitstopFrames` 的较大值（D96 同一套字段）。事件 `StrikeClash`（0→1，MoveId = null）每帧至多一次。** | 相杀与互中必须是互斥结果，否则同一帧会出现「既取消攻击又结算伤害」的双重裁决。把「有没有人真正打中」作为唯一分界，能<b>逐位保留 D80</b>（683 项既有测试零改动），相杀只出现在此前不存在的「攻击对撞但谁都没打到身体」这一新情形——例如两人在中距离（本演示 95..130 世界单位）同时出招。选 `Recovery` 而不是新状态，是因为它已经是唯一「合法携带 MoveId/MoveStartedFrame、不产生攻击框、走完即可重新行动」的状态：进入后 `GetActiveHitboxes` 立刻为空，同一实例不会在下一次 Active 帧重复相杀，也不必新增 D62 校验分支。相杀同样打断投技（真实中断，不只是视觉效果）。本阶段不做武器相杀等级、相杀专用招式或相杀后的特殊硬直。StateVersion 不变（快照格式未变），CoreVersion 11 → 12。 |

### 阶段 08D 决策（飞行道具）

| # | 决策 | 理由 |
|---|---|---|
| **D99** | **飞行道具是 <see cref="ProjectileState"/> 形式的权威实体，住在 `BattleState.Projectiles` 里；ID 由快照内的 `NextProjectileId` 单调分配（溢出显式失败）。数据在 `MoveDefinition.Projectiles` → `ProjectileDefinition`：<b>命中载荷直接复用 `AttackDefinition`</b>（`OffsetForward/Up` 解释为出生偏移、`HalfWidth/Height` 解释为碰撞盒尺寸、`Height` 走 D77 防御表、伤害/硬直/击退/击飞/停顿与近战同源；生效帧字段不适用）。<b>时间线</b>：招式 Active 阶段经过帧 == 出生帧的那一帧<b>只建立实体</b>（不运动、不判定），下一逻辑帧才开始移动与命中。阶段顺序：近战结算 → 投技 → <b>飞行道具</b> → KO。每帧顺序：已有实体运动（寿命 −1、越界删除）→ P×P 对撞（不同发射者、正面积重叠 → 双方消失）→ P×F 命中/防御（命中即消失；每名目标每帧至多一枚，按 ID 升序）→ 新实体入列。| 实体必须能与发射者解耦：发射者进入收招、被击中、被投中都不影响已在场的弹——它们只读自己的位置/速度/寿命/来源身份（`SourceMoveId` + `TypeId`），因此无需「发射者还活着」这类隐式前提，也不会因为发射者动作状态变化而被误删。生成当帧不判定，是为了让「生成 → 下一帧生效」成为可逐帧预测的固定语义（而不是依赖同一帧内的处理顺序）。复用 `AttackDefinition` 作为命中载荷，避免第二套伤害/防御/击退规则（D77/D92/D93 全部自动一致）；`ProjectileSpawnMask` 把「这次发动已经发过」变成显式事实，否则命中停顿冻结动作时间时同一帧会反复生成。每帧每个目标至多被一枚弹命中（ID 升序），保证多来源同帧结算稳定可复现。**本阶段不支持**：飞行道具被普通攻击打掉（P×Hitbox 暂不结算，已显式记录为扩展边界）、追踪/反弹/耐久/等级。事件：`ProjectileHit`（Amount = 实扣伤害）、`ProjectileBlocked`（Amount = 防御硬直）、`ProjectileClash`（每对一次）；生成不单独发事件（实体本身在快照里）。StateVersion 11 → 12，CoreVersion 12 → 13。 |

### 阶段 08 最终专项补修决策（跨系统边界，2026-11-08）

四项疑点经真实失败复现**全部确认成立**，均为最小修复；**StateVersion 12 / CoreVersion 13 不变**
（快照格式未变，修正的是结算与事件顺序）。D46/D55/D60/D61/D63/D69/D70/D73/D76/D78–D83/D87/D89–D99
全部保留，722 项既有测试零改动。

| # | 决策 | 理由 |
|---|---|---|
| **D100** | **`ProjectileSpawnMask` 属于「角色 + 发动帧」这一次攻击实例，不是角色本身。全部结算（近战 / 投技 / 相杀 / 投掷物）完成后，由唯一入口 `NormalizeSpawnMask(fighter, 帧开始 MoveStartedFrame)` 核账：掩码非零且（帧末不在招式中 **或** 发动帧已不同于帧开始）→ 清零；否则保留。同一实例 Active → Recovery 是正常阶段转换，不清零；取消到新实例由候选计算先行置 0，新实例可再次发射。已生成的 `ProjectileState`、`NextProjectileId` 与实体身份（Owner / SourceMoveId / ID）一概不动；`FighterState.Validate` 不放宽。** | 命中结算（Hitstun / AirHitstun / Blockstun）与投技结算（Knockdown）只清 `MoveId` / `MoveStartedFrame` / `MoveElapsedFrames`，掩码残留使 `BattleState` 构造期的 `Validate` 抛异常、整帧失败（实测三条中断路径分别报 `Hitstun` / `AirHitstun` / `Knockdown` 携带掩码 1）。在每个结算分支补一句 `ProjectileSpawnMask = 0` 会继续漏新路径；每帧无条件清零则会破坏 D99「同实例只生成一次」与「取消到新实例后重新发射」。故放在流水线末端按实例身份一次性核账。 |
| **D101** | **Hitstop 冻结资格优先于 D73 地面最小间距的对称分摊。`ApplySeparation` 显式接收 `frozenThisFrame`：双方都不冻结 → 逐位沿用旧 D73 求解器；只有一方冻结 → 冻结方坐标固定（冻结分支的候选 X 即帧开始 X），缺口全部由未冻结方承担，仍受 D70 边界钳制；双方冻结 → 保持二者已有的合法位置、不推挤。空中状态继续按 D94 `IsAirborneLike()` 完全不参与本求解。** | 冻结分支跳过运动后，旧求解器不知道该角色被冻结，仍按缺口对称分摊：实测 P1 0 → −2、镜像 P2 0 → +2、墙角 580 → 578、冻结末帧 0 → −2。采用「求解器内固定锚点重解」而不是「事后把冻结方坐标恢复」——后者会留下非法的另一方位置。冻结方贴墙且已无空间的人为初态属于约束不可同时满足，只钳到边界、不编造额外位移；正常可达状态（上一帧已满足 D70/D73）不会出现。冻结结束后计数归零，立即恢复既有对称分摊。 |
| **D102** | **结算前事件快照必须早于任何状态改写：`strikeClash` 仍用结算前状态计算 → 先采集 `AttackActiveSnapshot[]` 与 `ThrowAttempt[]` → 再执行 `ApplyStrikeClash` → 其余结算。事件组装只读快照，绝不回读改写后的状态。** | `ApplyStrikeClash` 会把双方从 `Active` 直接改成 `Recovery`，而 `CollectAttackActive` 只识别 `Active`；首个 Active 帧相杀时两条 `AttackActive` 全部丢失（实测事件集合只剩 `MoveStarted` ×2 + `StrikeClash`，AttackActive 期望 2 实际 0）。不改 D98 相杀资格、D80 互中语义、相杀后进 Recovery 的规则、EventId 公式与 MoveStarted 帧语义；同一实例仍只发一次 AttackActive，冻结帧仍跳过（D96）。 |
| **D103** | **飞行道具的命中停顿与成功防御停顿共用唯一入口 `ApplyRemoteHitstop(fighters, target, frames)`：只作用于被命中/成功防御的目标，不自动冻结远处发射者（D99 远程语义）；`frames = 0` 不停顿，已有更长停顿用 `Math.Max` 保留，N 帧精确冻结 N 个后续逻辑帧、第 N+1 帧恢复 Blockstun/硬直倒计时。** | 防御分支在写完 `ProjectileBlocked` 事件后提前 `return`，跳过了命中分支末尾才写入的 Hitstop（实测 `HitstopRemainingFrames` 期望 3 实际 0；Blockstun 恢复帧数期望 11 实际 8，提前 3 帧）。把「向目标应用远程停顿」提成私有方法让两个分支共用，避免再次分叉。 |

### 阶段 09A 决策（多回合比赛，2026-11-08）

| # | 决策 | 理由 |
|---|---|---|
| **D104** | **采用「方案一：比赛级 `MatchSession` 管理多个 `BattleSession`」。每回合拥有独立的 `BattleSession` / `InputBuffer` / `BattleState`，Frame 从 0 开始、`Scheduler.TotalSteps` 也从 0 开始；回合结束后由比赛层读取内核 `RoundState` 做一次计分，下一回合重建会话。<br>跨回合事件身份 = `(RoundNumber, EventId)`（`MatchEventId`）；<b>裸 `EventId = Frame × 16 + seq` 只在单回合内唯一</b>，不同回合会重复，绝不宣称全比赛唯一。 | 方案二（单一连续 BattleSession + 权威回合重置）需要同时改动帧同步契约（`Frame − 初始帧 == TotalSteps`）、D89 终局冻结与 `BattleSession` 的输入提交顺序，风险最大。方案一让 `BattleSimulation.Step()` / `BattleSession` / `BattleState` / `FixedStepScheduler` <b>一行都不用改</b>，且天然隔离跨回合输入边沿、攻击实例与飞行道具。代价（EventId 重号）由比赛级包装层显式承担，而不是悄悄忽略。 |
| **D105** | **`MatchRules`（RoundsToWin / MaxRounds / RoundTimeSeconds / EnableTimeout，构造期校验）与不可变 `MatchState`（Phase / RoundNumber / RoundsPlayed / P1Wins / P2Wins / 当前回合结果与结束原因 / MatchOutcome / RemainingTimeFrames / 双方选人 ID）是比赛级唯一权威。计分只在 `RecordRoundResult` 一处发生：P1Win → P1 +1、P2Win → P2 +1、DoubleKO 与平局都不加分但计入 `RoundsPlayed`；先到 RoundsToWin 立即 MatchFinished，达到 MaxRounds 按累计胜场比较（相同 → MatchDraw）。`Validate` 固定所有不变式（枚举合法、胜场非负且不超过已结束回合数、双方胜场和 ≤ 已结束回合数、MatchFinished ⇔ Outcome ≠ None、终局禁止再开回合）。 | 胜负不能出现第二套裁决：内核 `RoundState` 只回答「这一回合谁赢」，比赛层只做「一次计分 + 比赛终局」，Godot 只读显示。把计分收进一个纯函数入口，可杜绝「终局冻结帧被重复计分」「DoubleKO 凭空加分」；`RoundsPlayed` 与胜场和的约束让平局不会制造不存在的胜场。 |
| **D106** | **新回合初始化 = 重建会话与初始 `BattleState`，不是重建整场比赛。重置：体力 / 位置 / 朝向 / 逻辑状态 / 阶段计数 / 硬直 / Hitstop / 连段 / 命中确认 / `ProjectileSpawnMask` / 飞行道具列表 / `NextProjectileId` / 输入缓冲与方向历史 / 旧回合事件显示 / 计时器。保留：规则、累计胜场、已结束回合数、选人身份、当前回合序号。`MatchFinished` 明确拒绝 `StartNextRound`。** | 直接复用 `BattleState.CreateDefault()` 这一既有干净初态，比逐字段「清理」更可靠；回合切换时新建 `InputBuffer` 保证旧按住状态不泄漏。`RoundNumber = RoundsPlayed + 1`（进行中）这一不变式让「比分清零」类错误在 `Validate` 阶段即暴露。 |

> **09A 的计时边界（为 09B 预留）**：`MatchState.RemainingTimeFrames` 在 09A 由比赛层按「成功提交的逻辑帧」逐帧记账（初值 5940）。09B 将权威时钟下放为 `BattleState.RoundTimerRemainingFrames`，由 `BattleSimulation.Step` 在同一权威转换内递减并完成 Time Over 裁决，比赛层改为只读投影——届时**只有一套**权威计时。09A 不改内核，因此 StateVersion 12 / CoreVersion 13 不变。

### 阶段 09B 决策（回合计时与 Time Over，2026-11-08）

**StateVersion 12 → 13**（`BattleState` 新增权威计时字段）；**CoreVersion 13 → 14**。
旧单回合 API 逐位兼容：计时字段为 `null` 时 `Step` 完全不做计时/超时逻辑，783 项既有测试零改动通过。

| # | 决策 | 理由 |
|---|---|---|
| **D107** | **权威计时器是 `BattleState.RoundTimerRemainingFrames`（int?），由 `BattleSimulation.Step` 在成功推进一个进行中战斗逻辑帧后递减 1；`null` = 未启用（旧 API 默认）。默认 99 秒 = 5940 帧（99 × `LogicFrameRate`）。Hitstop 冻结帧仍是一个成功提交的逻辑帧，照常扣时；Godot 暂停、`Step` 抛异常（未提交）、帧积压丢弃的时间预算都不产生提交帧，因此不扣时；已结束回合走 D89 终局冻结、过渡阶段由比赛层停止推进，也不扣时。计时器随不可变快照携带，终局冻结原样保留。比赛层 `MatchState.RemainingTimeFrames` 只是它的只读投影，不再另起一套记账。** | 计时若放在 Godot（Timer / `DateTime.Now` / `Stopwatch` / 渲染 delta）就会随帧率与机器漂移，违背 D1/D63。放在 `BattleSimulation.Step` 的同一权威转换内，使「一个成功提交的帧 = 计时 −1」成为不证自明的事实，也让 09B 的「同帧先结算后裁决」可以在同一处完成。09A 的比赛层占位记账在 09B 移除，全程只有一套时钟。每秒变化由 UI 直接读权威字段，不新增逐帧事件。 |
| **D108** | **计时从 1 归零的那一帧：先完成本帧全部攻击/伤害/防御/投技/飞行道具/KO 结算，再裁决。若本帧已产生 KO/DoubleKO（`RoundState.FromFinalHealth` 已把回合改为 Finished），以 KO/DoubleKO 为准，不发 TimeOver；只有双方仍存活才按<b>剩余体力比例</b>裁决：安全整数交叉相乘 `P1.Health × P2.MaxHealth` vs `P2.Health × P1.MaxHealth`（`checked long`，无浮点、无 NaN、无文化区域差异），大者胜，完全相等 → `RoundOutcome.Draw`（TimeOverDraw）。超时裁决不改动任何体力，不产生虚假实扣伤害。** | 「计时器先归零就取消同帧合法攻击」会让最后一帧的胜负依赖处理顺序，破坏 D80 的同时性。比例比较必须用交叉相乘而非绝对 HP：P1 500/1000 (50%) 应胜过 P2 600/1500 (40%)，只看绝对值会判反。`long` 乘积最大约 2^62 < long.MaxValue，`checked` 兜底显式失败而非静默回绕。 |
| **D109** | **`RoundOutcome` 扩展 `Draw = 4`（旧值 0..3 原义不变），`RoundState` 新增 `Reason`（`RoundEndReason`：None / Knockout / DoubleKO / TimeOver / TimeOverDraw）并与 Outcome 做自洽校验（KO 胜利 ↔ Knockout 或 TimeOver；DoubleKO ↔ DoubleKO；Draw ↔ TimeOverDraw）。新增事件 `TimeOver = 16`，只在超时裁决帧发<b>一条</b>，顺序：KO/DoubleKO 或 TimeOver（互斥）→ `RoundEnded`（恰好一次）。Time Over 胜利绝不伪装成 `Knockout` 事件；`RoundEnded.Amount` 仍是 `(int)RoundOutcome`。`ForOutcome(outcome)` / `Finish(outcome)` 旧单参数签名保留、默认推断为 KO。** | 结果（谁赢）与结束原因（为什么）正交，才能在「不改写 D89 KO/DoubleKO 语义」的前提下表达 Time Over 胜利与平局；同时让比赛层能辨识回合胜者而不用解析事件文本。把原因放进 `RoundState` 而不是事件流，是因为它属于权威状态、必须随快照重放一致。事件只在归零帧发一条，避免每帧事件压力。 |

### 阶段 09C 决策（选人、回合过渡与完整比赛流程，2026-11-08）

**StateVersion 13 / CoreVersion 14 不变**（09C 只新增独立目录类型与表现层，未改快照格式）。

| # | 决策 | 理由 |
|---|---|---|
| **D110** | **`FighterCatalog` 是数据驱动的角色目录：条目含 `FighterId` / `DisplayName` / `Source` / 不可变 `FighterDefinition`；ID 唯一、未知选择立即拒绝；按双方 ID 生成 `FightRoster`。允许镜像角色（P1/P2 同 ID），允许 `Kai vs Rei` / `Rei vs Kai` / `Kai vs Kai` / `Rei vs Rei` 四种组合。`BattleSimulation` 完全不知道角色名称与目录的存在；09C 目录来自 `DemoRoster`（保留为测试夹具与回退路径），09D 起由 JSON 加载器构建同构目录。** | 配置对象不可变且不含可变战斗状态，镜像对战共用同一 `FighterDefinition` 实例不会共享受击/取消/飞行道具状态——那些都在每回合独立的 `BattleState` 里。把目录做成纯 C# 索引，新增角色只需加数据，不需要在状态机或模拟里加分支。 |
| **D111** | **完整比赛流程由 `MatchSession` 的四个阶段驱动：`NotStarted`（选人）→ `RoundInProgress`（战斗）→ `BetweenRounds`（按键确认后进入下一回合）→ `MatchFinished`（F5 重开 / ESC 返回选人）。过渡与终局阶段比赛层不推进任何战斗逻辑帧；不扣时间、不重复投递 `RoundEnded`、不重复计分、不保留旧攻击实例或飞行道具。输入隔离：选人/过渡/终局的界面按键在表现层 `_Input` 直接拦截，绝不传给 `KeyboardInputMap`；每回合由 `MatchSession` 新建 `InputBuffer`，表现层同步重建键盘映射与判定框绑定。** | 选人确认键（J / `,`）同时也是战斗轻拳键，若不在选人阶段拦截就会泄漏进开局首帧的输入缓冲；F5 重开时旧按住状态、暂停标志、事件身份都可能跨回合污染。用「每回合全新 InputBuffer + 全新 KeyboardInputMap + 全新 Scheduler」一次性消除整类问题，而不是逐项打补丁。第一版用按键确认进入下一回合（而非自动倒计时），便于调试且保持确定性。 |
| **D112** | **比赛 HUD 全部只读权威状态：当前回合 / 双方角色名 / HP / 胜场 / 剩余时间（由 `RemainingTimeFrames` 换算）/ 比赛阶段 / 回合结果与结束原因 / 最终比赛结果；原有判定框、事件与输入调试显示保留。跨回合事件用 `(RoundNumber, EventId)` 组合身份展示（如 `R2:164`）。Godot 不自行统计胜场、不自行倒计时、不判定结果。仅命令行的调试验收参数 `--hp=<n>` / `--match-time=<s>` / `--duel=<d>` 只在传入时生效，正常启动零副作用。** | 表现层一旦自己算胜场或倒计时就会出现「画面与权威快照不一致」的第二套结果。调试参数收在命令行并写进文档，避免把测试用短比赛/近站位混成正式默认规则（默认仍是满血 1000、99 秒、±120 站位）。 |

| **D113** | **JSON 配置是单向数据管线：`JSON → DTO → 严格校验 → FighterDefinition → FighterCatalog`。`FighterDataLoader` 只接受字符串或 `Stream`，不读文件、不依赖 Godot；`BattleSimulation` 完全不知道 JSON 的存在，战斗中不重复读文件。`schemaVersion` 必填且当前只支持 1，未知版本立即失败；反序列化禁止未知字段（`UnmappedMemberHandling.Disallow`），必填字段缺失/为 null 一律报错，绝不静默取默认值。 | 一旦让模拟层感知配置文件，就会引入「运行时读到什么就打什么」的非确定性。把加载器做成纯函数式入口（同样的文本 → 同样的不可变配置），Godot 只负责「开赛前一次性读文件」这一步，导出/编辑器两种路径都不影响战斗结果。禁止未知字段是为了让拼写错误立刻暴露，而不是变成一个永远不生效的字段。 |
| **D114** | **权威数值在 JSON 中用精确整数分数 `{ "numerator": n, "denominator": d }` 表示**（`d > 0`），加载时用 `checked` 转成 Q16.16 `FixedPoint`；分母为 0/负、超出可表示范围、NaN/Infinity、非整数或文化相关的数字格式一律失败。**加载器只做更严格的校验，不放宽任何构造器不变式**：伤害非负、帧数 ≥ 1、`hitstopFrames ≤ 30`、攻击窗口不越 `activeFrames`、取消目标必须存在、投技/飞行道具自洽、D95 的击退与摩擦成对。 | 直接解析 double 会让 1.5 这类数值在不同平台/文化下产生末位差异，破坏 D63 与逐位回放。整数分数让「1.5」与 `FixedPoint.FromFraction(3, 2)` 一一对应，测试可直接比较 `RawValue`。校验放在构造器之前逐字段给出路径，既保留原有全部不变式，又避免「解析失败的伤害悄悄变成 0」。 |
| **D115** | **Kai / Rei 的招式数据迁移到 `data/fighters/*.json`，并与 `DemoRoster` 做逐字段等价性测试**（指令 ID/优先级/缓冲、MoveId、Startup/Active/Recovery、攻击框尺寸偏移与生效窗口、伤害、防御高度、Hitstun/Blockstun、Hitstop、击退与摩擦、击飞与倒地计划、取消、投技、飞行道具、运动配置）；`DemoRoster` 保留为测试夹具与显式回退路径。目录仍由同一个 `FighterCatalog` 承载，`fighterId` 重复立即拒绝。所有错误消息包含 `文件路径 + 字段路径 + 原因`。 | 迁移最大的风险不是「读不出来」，而是「读出来但数值悄悄变了」——那会直接改变平衡与旧测试基线。逐字段摊平比较能在一次测试里证明迁移没有偷改任何战斗相关数值；保留 C# 演示配置让旧测试与回退路径不受影响，也证明 JSON 只是数据来源，不是第二套战斗算法。 |
| **D116** | **Godot 在开赛前一次性加载 `res://data/fighters/*.json`**（`FileAccess`，导出包内同样有效），全部文档交给 `FighterDataLoader.LoadCatalog` 构建目录；`export_presets.cfg` 的 `include_filter` 显式包含 `*.json`。正常游戏使用外部配置，测试可继续使用纯内存 `FighterDefinition`。运行中的比赛不会再读文件，因此外部修改只影响下一次启动。**这不等于已支持 `BattleState` 序列化/回滚迁移——两者无关。** | 把「读文件」限制在表现层的一次性入口，内核保持纯数据输入，路径变化就不可能改变战斗确定性。显式声明导出过滤器是因为 Godot 默认不会把 `.json` 当资源打包；不写清楚就会出现「编辑器里能跑、导出后角色消失」。 |
| **D117** | **比赛层批量推进必须在「回合终局帧完整提交之后」立即停止**：`BattleSession.Advance` 新增可选<b>提交后终止谓词</b> `stopAfterCommit`（默认 null = 旧行为逐位不变），在「权威状态 / 输入提交 / 调度器记账 / 提交观察者」<b>全部落定之后</b>才询问调用方；`MatchSession.Advance` 传入 `() => !State.IsRoundLive`。提前终止时未使用的整帧预算记为<b>丢弃预算</b>（`RecordDroppedSteps`），不计入 `TotalSteps`、不留给下一次调用；下一回合是全新调度器，天然不继承旧预算。`CurrentState.Frame − 初始帧 == TotalSteps` 仍然成立，D89 的「直接 `Step` 可推进终局冻结帧」语义不变。 | 旧实现只在批次开始前检查一次比赛阶段，底层一次 `Advance` 可提交多帧：KO 之后仍会推进 2–4 个终局冻结帧，导致返回帧数虚高、`StepExact` 与 `Advance` 不一致、终局事件被后续空事件覆盖。把终止点定义在「提交之后」而不是「预算之前」，才能既保证本帧的计分与事件完整，又不多推一帧。剩余预算必须记丢弃而不是回滚累加器——回滚会让同一段时间被重复计费（与阶段 02 的异常策略同一理由）。 |
| **D118** | **比赛事件必须在权威 `MatchState` 更新完成之后才对外发布**：`OnFrameCommitted` 的固定顺序是 ① 计时投影（读已提交的 `BattleState.RoundTimerRemainingFrames`）→ ② 唯一一次 `RecordRoundResult` → ③ `State.Validate()` → ④ 包装并投递 `MatchEvent`。观察者抛异常时：本帧战斗状态与比赛状态<b>均已落定</b>、不回滚、不二次计分、不重发已投递事件，异常经 `BattleSession` 原样上抛并进入既有故障封闭；`_publishing` 期间禁止重入修改比赛会话（`StartMatch` / `StartNextRound` / `RestartMatch` / `ReturnToSelection` / `SelectFighters` / `Advance` / `StepExact` 显式抛 `InvalidOperationException`）。 | 先发事件后计分会让消费者在收到 `RoundEnded` 时读到 `RoundInProgress`、胜场未加、`RoundsPlayed` 未增——UI/录像/第三方观察者会各自得到一份不一致的比赛快照；观察者抛异常还会把权威计分直接吞掉（实测 P1Wins 期望 1 实际 0）。把「权威状态先落定」作为硬顺序，异常语义就退化成「事件没送到，但结果已经算完」，这正是可恢复的方向。重入必须显式拒绝而不是依赖调用顺序：回调里启动下一回合会让旧回合的后续帧推进到新会话、或把旧回合事件挂到新回合身份上。 |
| **D119** | **「回合结果 + 结束原因」的合法性只有一个权威实现**：`RoundState.IsValidOutcomeReasonPair(outcome, reason)`（公开只读谓词）给出六种合法组合 P1Win/P2Win × Knockout/TimeOver、DoubleKO + DoubleKO、Draw + TimeOverDraw；`RoundState.ValidateOutcomeReasonPair` 基于同一谓词抛错。单回合层（`ForOutcome` / `Validate`）与比赛层（`MatchState.RecordRoundResult` / `MatchState.Validate`）全部调用同一份规则，比赛层不再自己写 switch。 | 比赛层原先只校验「枚举已定义且不是 None」，因此 `DoubleKO + Knockout`、`Draw + Knockout`、`P1Win + DoubleKO` 这类组合会被接受并写进 `MatchState`，与 `RoundState` 的不变式直接矛盾——同一条规则两处实现必然分叉。抽出共享谓词后，入口与状态自检用的是同一份判断，`Validate` 也能拒绝「本不应存在」的历史/外部构造状态。不改变 `RoundOutcome` 枚举数值，也不改变胜场规则，因此无需升级 StateVersion。 |
| **D120** | **3v3 队伍赛是「比赛级组织多个连续的 1v1 交锋」，不是在 `BattleState` 里放六名角色**：新增 `TeamDefinition`（有序三人 + 每人的 `MaxHealth`）、`TeamMatchRules`、`TeamMatchState`、`TeamMatchSession`，与 1v1 的 `MatchSession` 并列而不互相改写。场上永远两人，`BattleSimulation` 一行未改。**队员身份是 `(TeamId, SlotIndex)`**：允许 Kai / Rei / Kai 这类同名角色占据不同槽位，每个槽位拥有独立的出战历史、剩余体力与淘汰记录；`FighterCatalog.CreateTeam` 负责把 ID 解析成带满血值的队员。 | 把六人塞进 `BattleState.Fighters` 会同时破坏 1v1 的推挤、相杀、投技、飞行道具与 D89 终局语义，且让所有既有测试失效。队伍赛本质是「谁在场上 / 谁被淘汰 / 下一场谁上」，属于比赛层职责。槽位身份是必须的：只用 FighterId 会让同名队员被误判成同一对象，淘汰与体力继承立刻出错。 |
| **D121** | **淘汰是独立权威状态，不是 `Health == 0`**：`TeamSideState.Eliminated[]` 是唯一存活判据，`Health[]` 只记录「该槽位最后一战的实际体力」。淘汰规则：P1Win/P2Win（KO 或 Time Over）→ 败方当前队员淘汰、胜方保留；DoubleKO 与 TimeOverDraw → 双方当前队员同时淘汰。任一方耗尽即判定队伍胜负，双方同时耗尽为 `TeamDraw`。**体力继承**：胜方当前队员保留本场结束时的实际 HP（不回复），接替登场的新队员使用自己的 `MaxHealth` 满血开战；每场交锋重建 `BattleState`/`InputBuffer`/`FixedStepScheduler`，不继承任何招式、硬直、击飞、投技、飞行道具或输入历史。 | Time Over 的败者可能仍有正体力，用 HP 判存活会让它错误地重新登场；把淘汰写成显式布尔数组后，「HP 大于 0 但已出局」是合法且可表达的状态。体力继承让「一人连挑三人」成为真实策略，而每场重建战斗状态保证不会把上一场未结算的动作带进下一场。 |
| **D122** | **跨交锋事件身份是 `(BoutNumber, EventId)`**：`TeamMatchEvent` 在比赛层包装，`TeamMatchEventId.ToString()` 形如 `B2:164`。裸 `EventId` 只在单场交锋内唯一（每场 Frame 从 0 开始），文档明确它**不是**全场唯一键。计时与事件顺序沿用 D107/D117/D118：`TeamMatchSession.Advance` 传 `stopAfterCommit: () => !State.IsBoutLive`，先完成计时投影与唯一一次 `RecordBoutResult` + `Validate`，再发布事件；观察者异常不回滚、不重发，重入修改会话被显式拒绝。 | 若不把交锋序号并入身份，第二场交锋的 `EventId` 会与第一场重号，表现层的特效去重与录像回放都会把旧事件当成新事件。沿用 D117/D118 的同一套批次边界与发布顺序，避免 3v3 出现第二套语义。 |
| **D123** | **表现层用「两个会话 + 一个纯 C# 选队模型」支持双模式**：`BattleScene` 只按 `Stage`（模式选择 / 1v1 选人 / 3v3 选队 / 1v1 战斗 / 3v3 战斗）分派按键与渲染；`MatchSession` 与 `TeamMatchSession` 互不干扰，场上永远是 1v1。队伍选择逻辑抽到 `TeamSelectionModel`（纯 C#、无 Godot）：槽位游标、角色回绕、改阵容即取消确认、按目录满血值建队，全部可单元测试。**输入隔离**：非战斗阶段的按键在 `_Input` 直接拦截，绝不进入 `KeyboardInputMap`；每场交锋由会话新建 `InputBuffer`，表现层同步重建键盘映射；选队确认键（J / `,`）不会变成开局第一下攻击。 | 选队规则一旦写进 Godot 节点就只能靠手点验证；抽成纯 C# 后「游标回绕、改阵容取消确认、同名角色占不同槽位」都有测试守住。两套会话并列而非继承，避免 3v3 反向污染 1v1 的 `MatchState` 语义；`Stage` 只决定「按键去哪、画面画谁」，不参与任何权威判定。 |
| **D124** | **双人同帧输入用「只读探针」定位，不靠猜**：`InputTrace` + `TracingInputSource` 按顺序记录「键盘事件 → 采样掩码 → Prepare → Commit → 帧末权威状态」，只在 `--inputdiag` 打开时写入并打印，HUD 附带最近 4 条。**实测结论**：输入链路本身没有缺陷——同一渲染帧按下 J 与 `,` 时日志出现 `sample: P1=A P2=A`，随后 `state: f9 P1=Startup/StandLightPunch P2=Startup/StandLightPunch`，下一帧双方同时 `Hitstun/0`，得到真实 DoubleKO。上一轮「双人同帧只有一方生效」的真实原因是**角色数据**：Rei 的 JSON 只有 236A / 214B / 5D / 5C，没有裸 5A，P2 按轻拳自然没有指令可匹配。 | 如果不动日志就改 `KeyboardInputMap` 或 `InputBuffer`，会把一个健康的输入链路改坏，并掩盖真正的配置缺口。探针只观察、不改变输入与时序，因此可以长期保留在验收工具里；用「采样掩码」与「帧末状态」两段证据区分「输入没进来」和「输入进来了但没识别出招式」，这是两类完全不同的故障。 |
| **D125** | **动画是权威状态的单向投影**：`FighterState → AnimationResolver → ResolvedPose → Godot ColorRect`。表现配置放在<b>独立的</b> `data/presentation/*.json`（schemaVersion 1，与战斗 JSON 完全分离，不污染已冻结的 fighter schema），描述「动画名 → 姿态帧序列」与「状态/招式 → 动画名」；`FighterVisualClock` 把逻辑帧换算成动画时间并在 <b>Hitstop 期间停止推进</b>（同一逻辑帧重复渲染不推进，状态或招式切换时归零）。解析优先级：招式三段（按 <c>MoveId</c> + <c>MoveElapsedFrames</c>）→ <c>Walking</c> → 逻辑状态名 → 兜底 <c>idle</c>；招式缺配置回落到「该阶段的通用动画」，整份配置缺失回落 <see cref="AnimationSet.CreateFallback"/>。**动画绝不写回战斗状态**：不改命中、硬直、取消窗口与位置速度。 | 一旦让 AnimationPlayer 的回调决定扣血或硬直结束，就会出现「掉帧改变胜负」；把动画限制成只读投影后，渲染帧率、缺素材、换皮都不可能影响权威结果。表现配置独立成文件是因为 fighter JSON 已经声明「未知字段一律拒绝」，往里面加动画字段会破坏已发布的 schema v1。用 `MoveElapsedFrames` 而不是「动画播放了几帧」来取招式帧，Hitstop 天然冻结，不需要额外的暂停分支。 |
| **D126** | **打击反馈只由权威事件驱动，并按 `(scope, EventId)` 去重**：`FeedbackDirector` 维护唯一映射表（Hit→命中火花、Blocked→冷色火花、ThrowHit→重冲击、ThrowClash/StrikeClash→中点火花、ProjectileHit/Blocked/Clash→远程火花、Knockout/DoubleKnockout→红色闪光强抖动、TimeOver→仅横幅），同一条事件被重复读取（每个渲染帧都会读 `LastEvents`）只产生一次特效；重开/返回选人时 `Reset()`（回合号会重新从 1 开始）。火花位置用<b>已提交的 <c>BattleState</c></b> 解析（相杀/对撞取双方中点，其余取被击者胸口），绝不按「两人距离近」推断。**抖动是节点偏移**：`HitShake` 用固定偏移序列（非随机）随幅度线性衰减、取最大值不叠加、夹紧在 0..8，只叠加在角色节点位置上，不写回权威坐标、不影响 Hitbox/Hurtbox。受击闪白用 `FlashUntil(logicFrame)`，按逻辑帧计时。F4 可整体关闭特效。 | 若让表现层自己判断「打中了没」，就会出现与权威结果不一致的特效（尤其是相杀、被防御、投技冲突这三类）；把「事件 → 特效」做成单一映射表，判定权仍只在内核。抖动如果用 `Random`，同一段回放的截图就不可复现；固定序列既保留打击感又可回归测试。不做全屏相机震动是因为 HUD 与世界同在根节点下，移动相机会把调试文字一起晃走，反而破坏可读性——用角色节点偏移得到同样的打击反馈而不牺牲可读性。 |
| **D127** | **逐逻辑帧表现快照是表现层的唯一事件来源**：`MatchSession` / `TeamMatchSession` 新增可选观察者 `framePresented`，在<b>每个已提交帧</b>（含无事件帧）之后投递 `PresentationFrame(Scope, LogicFrame, StateBefore, StateAfter, Events)`；`PresentationFrameBuffer` 按提交顺序缓存，表现层每个渲染帧整批 `Drain()` 并清空（容量 240 仅作防御，超出丢最旧帧，绝不无限积累）。事件身份仍是 `(RoundNumber/BoutNumber, EventId)`，`FeedbackDirector` 不改；位置改由纯函数 `FeedbackGeometry.Anchor(cue, frame.StateAfter)` 解析——<b>事件发生那帧提交后的权威状态</b>，不是渲染时的最终状态（命中帧被击者仍在接触位置，击退从后续帧才开始；投技位移已应用，与画面一致）。`LastEvents` 继续保留「最近一帧」的兼容语义，但<b>不再</b>是特效数据源。单向流：`权威状态 → 逐帧快照 → 动画与特效`，快照只读、绝不写回。 | 实测：`Advance` 5 帧、命中在第 3 帧时，旧路径 `LastEvents` 为空而 P2 已掉血 30——伤害结算了但火花丢失；批次末状态里 P2 已被击退到 x=33.5，用它取坐标会把火花画偏。只保留「最后一帧」在低帧率下必然丢事件；逐个事件回调又无法携带「事件发生时」的状态快照。用「一帧一个快照」既保住顺序与坐标，又让动画时钟能观察到无事件的冻结帧。队列显式 Drain 使生命周期可断言（不残留、不泄漏），容量上限只影响表现、不影响权威状态。 |
| **D128** | **Hitstop 的冻结判据是「帧开始状态的 `HitstopRemainingFrames > 0`」，并且必须逐帧观察**：`FighterVisualClock.Observe(logicFrame, state, moveId, frozenThisFrame)` 按提交顺序每帧调用一次；`frozenThisFrame` 由 `FighterVisual.ObserveFrame` 取 `StateBefore` 的计数器得出。真实执行顺序：命中帧 N 本身不冻结（正常推进并在结算末尾写计数器），N+1…N+4 四帧冻结（开始值 4→3→2→1，提交时递减），N+5 恢复。计数器从 1 递减到 0 的那一帧<b>仍是冻结帧</b>，但它的结束值已是 0——所以不能用帧结束值判冻结。状态/招式切换仍然归零；招式动画仍取权威 `MoveElapsedFrames`。旧接口 `Sync(…, hitstopRemaining)` 保留为单帧便捷入口，参数语义明确为「该帧生效中的停顿」（帧开始值）。 | 旧实现只在一次 `Advance` 结束后用末状态同步：实测命中后 4 个冻结帧 + 1 个恢复帧被算成 +5（正确是 +1），低帧率下动画会明显抢跑；而且「最后一帧冻结」在末状态里已经归零，条件判断天然无法表达。把冻结标志与「帧结束状态」分开后，同一逻辑帧重复渲染、一次跨多帧的批次、跨 FPS 播放都能得到同一条动画时间线。不改 `FighterState` 字段、不改 Hitstop 时长与战斗规则，只改表现层如何读它。 |

### 离线原型限定（重要）

**当前丢帧策略与帧同步保证只适用于当前离线原型。**

- 本项目**尚无**回滚联机、输入预测、跨平台确定性验证。
- `DroppedStepCount > 0` 意味着该段时间**没有**被模拟。在未来的联机回滚场景中，
  这会造成双方状态分叉，**不能**直接沿用为网络同步策略。
- 因此：**不得**把当前「补帧上限 + 丢弃预算」的设计承诺为未来回滚联机的确定性保证。
  联机阶段必须重新设计时间预算策略（如暂停等待、强制重同步），并单独验收。

### 与 Ikemen GO 研究资料的对应

**外部研究资料的来源与版本（可复现，不依赖本机绝对路径）**

| 项 | 值 |
|---|---|
| 仓库 | `https://github.com/ikemen-engine/Ikemen-GO.git` |
| 分支 | `develop` |
| 本地 HEAD 提交 | `fb269c975745d3effc447af4c6d74dd2988fc9ba` |
| 研究文档基线提交 | `4d007d6e1b72262115b120c4b25886b3ba4f9a57`（= `origin/develop`，见 23 号文档首行） |
| 第二轮源码复核提交 | `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`（见 23 号文档首行） |
| 相关标签 | `v1.0.0` = `81c6da71d689625e20db79586815b695da00dd6d` |
| 文档路径 | `docs/combat-research/`（相对仓库根） |
| 本机镜像 | `C:\Users\32360\Desktop\Ikemen-GO`（**只读参考，禁止修改**） |

> 说明：`4d007d6e…` 与 `5dac5caf…` 是研究文档自身声明的基线；本地 `develop` 已前进到
> `fb269c97…`。**阅读源码时以文档声明的提交为准**，不要假设本地 HEAD 与文档一致。

对应关系：

- 逻辑/渲染分离 → 对应 `REUSE_CONCEPT`（见 `docs/combat-research/23_REUSE_DECISIONS.md` 第 1 行）
- 输入作为外部注入的帧数据 → 对应 `REUSE_CONCEPT`（多源输入归一）
- 不可变快照 → 对应 `REUSE_CONCEPT`（快照与重算）
- 固定逻辑帧 + 绘制节奏分离 → 对应 `REUSE_CONCEPT`（23 号文档「第二轮决策登记」首行）
- **尚未**引入其 P0–P9 阶段划分、模块拆分（15 个模块）——留待后续阶段按需引入。阶段 02 只做到
  「P0 帧入口 + 固定帧推进」的调度部分，**未**实现 P1–P9 的任何规则阶段。

---

## 5. 尚未决定的架构问题

以下问题**必须在进入阶段 02 之前明确**，不得由实现者临时拍板：

1. **后续状态模型细化**：`BattleState` 内如何组织角色数组（固定 `[2]` 还是可变列表）？引入招式/实体后是否仍保持全量不可变复制，还是改为结构共享（如不可变持久化集合）？
2. ~~**定点数格式**~~ **阶段 05A 已决定（D63）**：Q16.16（`long` 原始值，Scale = 65536），
   取整统一向零截断，乘除 `Int128` 中间结果，溢出显式抛出；1 世界单位 = 2^16 原始值，
   Y 向上、地面为 0。**仍待决定**：快照序列化格式（定点原始值如何进出，属 K8 序列化问题）。
3. **帧内阶段划分**：是否完整采用 22 号文档的 P0–P9？阶段 02 至少需要 P0（帧入口）、P4（运动）、P8（快照）。
4. ~~**输入归一化策略**~~ **阶段 03A 已决定（D26–D28）**：SOCD 左右同时 → 中立、上下同时 → 上优先；录制层存绝对方向，相对朝向只在解析层应用。**仍待决定**：是否改为「后输入优先」——那需要先扩展 `FrameInput` 记录事件顺序。
5. **碰撞几何**：采用 AABB 还是 OBB？推挤算法（对称分配 vs 墙角余量）尚未定。
6. **事件流设计**：表现层如何消费 `(frame, eventId)` 事件？阶段 01 尚无事件流，直接轮询状态。
7. **规则数据格式**：指令定义已采用 **C# 数据**（D37，03B-1）；**招式/帧数据**的格式
   （JSON / 自定义文本 / C# 代码）与加载期校验策略仍待定。
8. **回合与比赛规则**：`RoundSystem` 何时引入？KO 判定放在 Step 的哪个阶段？
9. **`CaptureHelper` 的归属**：目前放在 `scripts/`。是否移入独立 `tools/` 目录或改为 Godot EditorPlugin？
10. **状态版本号语义**：`BattleState.StateVersion` 目前为常量 1，尚未定义升级/迁移规则。

---

## 6. 阶段 01 边界声明

本阶段**刻意未实现**（不是遗漏）：

- 角色移动、跳跃、攻击、受击
- 状态机、招式数据、帧数据
- 碰撞检测（hitbox / hurtbox / pushbox）
- 伤害、硬直、连击
- 动画、精灵、音效
- 回合、计时、KO
- 输入采样（键盘/手柄映射）
- 快照序列化、回滚、重放


## 阶段 11A：回调与对撞定位（D129/D130）
- D129：1v1/3v3 的 framePresented 与 eventsCommitted 共用发布守卫。比赛计分、校验与战斗帧提交先完成；回调不能重开、返回选择或开启下一回合/交锋。异常原样传播并保留 BattleSession 故障封闭。
- D130：飞行道具对撞在运动后、实体移除前采集两枚实体中心的定点中点，以 EventId 关联只读 EventAnchor，经 BattleStepResult、已提交会话和 PresentationFrame 传到 FeedbackGeometry。多次对撞各自定位，不改变事件内容、顺序、判定和 BattleState。
- 旧构造路径的可选锚点参数默认空；StateVersion=13 / CoreVersion=14 保持，因为权威状态与规则未变化。阶段 11B/11C/11D 尚待完成。


## 阶段 11B：空中防御（D131–D133）
- D131：正常 Airborne + 帧开始朝向解析的后输入可防 High/Mid，Low 不可防；AirHitstun、PreJump、动作、倒地、起身不授予防御。近战与飞行道具共用 CanGuard。
- D132：AirBlockstun=12，保留跳跃类型与空中速度、重力积分；计时归零恢复 Airborne。未归零落地转 Blockstun，剩余计时保留，落地速度清零。Hitstop 分支冻结运动与计时；不属于受击连段延续。
- D133：沿用 Blocked/BlockstunStarted/ProjectileBlocked；远程防御只冻结目标。Kai/Rei 增加 air_guard 与 air_guard_hurt 姿态，AirBlockstun 映射后者，缺配置显式回退 hurt。
- StateVersion=14（新增权威逻辑状态），CoreVersion=15（空防规则）；未实现跨进程快照迁移。专项与关联 Debug 回归 377 项通过；1v1/3v3 真实图形集成留在 11D。11C 尚未完成。


## 阶段 11C：三帧拆投窗口（D134–D138）
- D134：LP+LK 同时按住且组合产生新的按下边沿才拆投；上一帧按钮来自 FighterState.RecognitionState.HeldAttacks。长按不自动触发。
- D135：普通投技默认 Techable=true，旧 JSON 缺字段同样默认 true，可显式 techable=false 保留不可拆投规则。Schema v1 新增可选布尔字段，类型错误由 JSON 加载器拒绝。旧 D97 即时结算专项夹具明确使用不可拆投配置，断言保持；真实 JSON 投技测试增加窗口期不扣血断言后再验证完整伤害。
- D136：BattleState.PendingThrow 是不可变待决抓取，包含双方、攻击实例发动帧、MoveId、抓取帧和不可变 ThrowDefinition（含 ThrowId）。剩余窗口由抓取帧与当前帧计算。T、T+1、T+2 有效，T+2 末尾未拆则扣血、倒地、Hitstop；T+3 不可逆转。
- D137：待决期间双方动作/运动锁定，输入历史继续推进，窗口按已提交逻辑帧计数（Hitstop 不延长）。独立飞行道具继续运动/碰撞/命中；任一方被弹命中取消未完成抓取，同帧已结算的投技不回滚。Time Over 仍在攻击/投技/飞行道具之后，终局清除待决状态。新回合/交锋使用全新状态。
- D138：ThrowTech=17，与 ThrowClash 区分，源为拆投方、目标为原投技者，携带 MoveId、ThrowId、帧和唯一 EventId。成功双方回地面，向外各分离8单位且受墙角钳制，清空动作、生成掩码、连段与倒地计划；青色火花。失败沿用 D97 伤害/倒地/命中确认/停顿路径。
- StateVersion=15（PendingThrow），CoreVersion=16；没有跨进程快照序列化与迁移能力。Debug/Release 全量各1072项通过、无跳过；核心与宿主 Debug/Release 构建通过。
- 图形运行已实测 Kai vs Kai：shots/p11_airguard.png 中 P2 AirBlockstun、双方1000HP；shots/p11_throwtech.png 中 ThrowTech P2→P1、双方满血、分离至±38。完整11D对局与全部图形场景尚未验收，不宣称阶段11正式完成。

## 阶段 11D 集成验收记录（D139）
- Git：11A `29a46e8`，11B `6b0adaf`，11C `38d4baf`，均已独立推送。11D 追加了空中按后姿态解析、抓取提示、实际墙角及旧 JSON 配置验证。
- 最终测试：Debug **1078/1078**，Release **1078/1078**，均失败0/跳过0。原1031项测试全部保留，新增47项。随合法状态增加，将未定义状态用例从12改为13；D97即时投技夹具显式使用不可拆投配置；JSON投技用例增加待决不扣血断言，原伤害/倒地/事件断言保留。
- 四构建：BattleCore Debug/Release、NewKOF Debug/Release 全部成功，0警告0错误。测试构建仍有原有 CS8600 / xUnit2029 两条告警。
- Godot mono 4.7.2 无头导入成功；真实 Vulkan / RTX 3060 图形运行截图逐张检查：`shots/p11_airguard.png`、`p11_landed.png`、`p11_groundguard.png`、`p11_pending.png`、`p11_throwtech.png`、`p11_doubleko.png`、`p11_duel_win.png`、`p11_team_airguard.png`、`p11_team_tech.png`、`p11_team_win.png`、`p11_team_draw.png`。
- 图形实测：Kai vs Kai 空防满血、Hitstop、落地回Standing；地面防御；抓取待决提示；拆投青色火花和确定性分离；窗口结束投技KO；下一回合；MATCH WIN。队伍赛首名空防与拆投、逐交锋换人、投技KO后TEAM WIN、同帧互中最终TEAM DRAW。快速终局只用既有 `--hp=30` 调试参数，正式招式数值未改。
- 调度：30/60/144fps 的10000帧拆投测试与StepExact结果一致；空防调度/中间快照、待决抓取快照、D80/D89/D97/D117/D127/D128与其余旧专项均包含在全量回归中。图形一次启动出现过1帧积压丢弃告警（现有宿主5步预算），不将渲染帧当作权威帧验收。
- 阶段12前置：AI只能输出权威FrameInput；需理解AirBlockstun、三帧PendingThrow窗口、输入边沿与Hitstop，不可根据动画结束控制规则。阶段12未开始。

- 结算边界补修：T+1弹丸命中取消未完成抓取；T+2先结算投技再处理弹丸，保留既有优先级。新增失败复现发现倒地转地面受击后的旧起身计划残留，现于全部结算后统一归一化清理。PendingThrow构造还校验双方地面状态与抓取实例一致。

### 阶段 11 最终交付状态
11A/11B/11C 已实现并分别提交推送；11D完成本节所列最终回归与真实图形验收。以上1078项为最终基线，覆盖旧1031项，新增47项。独立代码复核未发现尚待处理的正确性问题；投技/弹丸同帧非法落地计划已由失败复现驱动修复并通过全量回归。未发现重大回归。此前“11D尚未验收”等段落为分阶段历史记录，以本节最终记录为准。阶段12未启动。


## 阶段12A：观察与输入事务（D140–D142）
- D140：AI只接收已提交BattleState和缓存FightRoster的只读AiObservation，未提交真人输入仅由组合器持有，不传给策略。状态与规则不变（StateVersion15/CoreVersion16）。
- D141：AiPolicyState独立格式v1携带帧、种子、动作计划/索引、冷却、威胁时刻与最多121帧观察历史；集合复制只读，ContentEquals可逐项比较。内存恢复必须同时还原BattleState、两席策略快照与相同角色/策略配置；不声称跨进程录像或回滚已实现。
- D142：AiInputSource拥有一份完整FrameInput事务；Prepare缓存双方候选且不提交策略/输入历史/随机数。Commit先检查完整输入和原始InputBuffer一致，再提交具体InputBuffer及预验证策略状态。仅组合具体InputBuffer，不接任意可能抛异常的子输入源，避免真人先提交、AI后失败。输出历史只保留600帧；完整录像由外部逐提交帧收集。


## 阶段12B：合法决策与指令（D143–D145）
- D143：AiCommandPlanner读取角色真实CommandDefinition，生成不可变相对方向脚本；每帧按当前Facing转InputBits。前后释放按钮、支持普通/单方向/序列/双重/多按钮/蓄力；最多4096脚本帧，无法满足时限或识别历史时返回空计划，择招跳过该候选。
- D144：AiController实现输入型策略，稳定优先处理终局、待决抓取、受限状态、防御、稳定脚本、攻击机会和空间控制。读取延迟观察与反应计时，不读取当前真人输入。硬直/Hitstop不新发招；空防和高低防御均输出真实方向；投技/拆投均经原有指令与战斗规则。
- D145：实际角色招式按固定序数遍历，距离/前摇/收招/伤害/远程/投技评分只用于输入选择；不改战斗状态。未支持的计划跳过，无合法招式时接近/观察；角色与指令数据只在载入时解析。专项：指令16项、实际战斗策略20项通过。StateVersion15/CoreVersion16不变。


## 阶段12C：难度与战术（D146–D148）
- D146：四档Profile集中管理观察/反应/决策间隔、防御/攻击/投技/拆投/失误/反击概率（千分数）、冷却与偏好距离。Easy/Normal/Hard/Expert普通反应18/12/7/4帧，观察4/3/2/1帧，决策8/6/4/2帧。拆投是公开待决事件的独立快速反应通路，延迟4/3/2/1帧、概率100/300/550/800；因此简单/普通通常赶不上3帧窗口，困难/专家也非必拆。
- D147：固定整数xorshift32；种子按玩家/回合或交锋/槽位稳定派生，独立席位不共享随机状态。Prepare仅计算候选种子，Commit才落定。无系统时间、线程调度、Guid或Random.Shared。
- D148：进攻型提高攻投与贴身倾向；防守型更重防御、远程与距离；均衡型在落后或时间紧时加压。所有调整仅选择输入，不改HP/伤害/硬直/判定。固定300帧实测三种战术输入日志两两不同，Easy与Expert不同，同配置同种子重复一致。当前配置/控制器/指令关联专项66项通过。
- Git网络状态：12A `bd74407`、12B `bcbb500` 已本地提交；GitHub HTTPS多次连接超时/重置，推送未成功，不把缓存origin/main当作已同步证据。后续仍需核实远程。


## 阶段12D：宿主接入与验收（D149–D152）
- D149：比赛规则仍为1v1/3v3，控制方式独立配置。模式/选人/选队页F6切P1、F7切P2 Human/AI，F8循环难度，F9循环战术，F10切固定种子队伍。角色与阵容可手选；任意手动修改队员会覆盖种子阵容。种子阵容从实际Catalog ID排序选择3槽，允许重复，界面显示预览。CLI `--p1=ai --p2=ai --ai-difficulty=Expert --ai-tactic=Offensive --ai-seed=12345 --ai-seeded-teams` 可指定演示配置；`--ai-auto-next`仅演示时自动进入下个回合/交锋。
- D150：BindDuelRound/BindTeamBout按当前角色、回合/交锋和槽位新建输入组合器与策略。重开清空脚本和观察历史，配置保持且种子稳定重建；返回选择清空旧源。暂停由原Scheduler控制，不调用Prepare/Commit。HUD读取组合后的LastInput、控制类型、难度、角色、战术、Action/TargetMove。原键盘InputBuffer的电平/边沿与历史语义未改。
- D151：观察历史最多121帧，配置延迟超过121构造即拒绝。快照带Player/Scope，恢复及候选都检查席位/回合与角色/帧；拒绝错位。外部非法修改真人采样导致Commit拒绝后可显式DiscardPrepared再重备，不消费策略或随机数。含上方向且当前地面规则会优先起跳的招式计划不被控制器选择，回退至其他招式或接近；不修改起跳/招式规则。远距离加入有限接近决策，避免双AI无限飞行道具相消。
- D152：10000帧高HP持续战斗中，每条执行路径重新运行AI决策，比较单帧/7帧StepExact及30/60/144fps Advance逐帧输入、策略内容、角色和事件；独立恢复策略快照、离线不运行AI重放输入。完整1v1/3v3另逐提交帧比较比赛状态、BattleState全部权威内容与事件/策略/最终结果。没有拿预录输入替代实时AI跨帧率验证。
- Godot真实Vulkan/RTX3060截图：`shots/p12_ai_duel.png` Expert/Offensive AI双方，真实攻击KO至P2 MATCH WIN；`p12_ai_team.png` Hard/Balanced双方种子阵容，5场交锋后P2 TEAM WIN（最后队员900HP）；`p12_human_ai.png` P1真人输入vsEasy P2，`p12_ai_human.png` Normal P1vs真人P2，均真实KO并MatchFinished。全部满血1000开始，未用改伤害或强制KO；验收时钟用既有`--match-time=15`。
- 真实图形首次暴露双远程AI满血相消至TimeOver，已修复主动接近并以原种子回归；正常TimeOver仍保留。独立复核发现并修复观察延迟超历史永久等待、事务失败恢复、候选身份与上方向地面招式循环问题。
- 权威StateVersion15/CoreVersion16保持，AI策略格式v1；不宣称正式跨进程序列化/录像/联机回滚。未来回滚必须同时恢复BattleState、两席AiPolicyState、相同Profile/Tactic和角色定义，以及外部输入记录游标。阶段13未开始。

### 阶段12最终验收记录
- Debug **1172/1172**、Release **1172/1172**，失败0/跳过0；原1078项测试文件与断言均保留，新增94项。核心Debug/Release、宿主Debug/Release四构建通过，0警告0错误；测试编译仍有原两条CS8600/xUnit2029告警。
- Godot无头导入成功；真实图形除上列完整对局外，还验证 `shots/p12_human_team_ai.png` 真人vs电脑三人队伍至TEAM WIN；`p12_restart_return.png` 暂停60个渲染帧逻辑帧保持91，恢复后继续；F5重置至帧1，ESC返回选人帧0，重新确认后AI继续新比赛。
- `shots/p12_team_reconfirm.png` 验证双方READY后F10改变种子阵容，双方确认被清除。F6/F7在种子阵容下切控制、手动编辑覆盖种子阵容也会清除受影响的确认，避免未经重新确认开赛。
- 最终完整实时AI确定性：每帧输入、两席策略内容、BattleState、事件、MatchState/TeamMatchState、终局结果均在单帧/7帧StepExact与30/60/144fps Advance之间比较；各路径重新决策。10000帧持续战斗、种子复现、策略中途恢复及不运行AI的输入重放分别验收。
- 单帧成本有界：最多121帧历史、最多4096帧脚本、仅对当前角色已缓存配置进行稳定顺序评分；没有未来战斗分支模拟、每帧JSON解析、线程或ML。未给出脱离环境噪声的微基准数字。
- 本阶段未修改FighterState/BattleState/BattleSimulation/伤害与比赛计分规则，StateVersion15/CoreVersion16不变；AI策略v1。后续恢复仍要求相同不可变角色定义和Profile/Tactic，比较策略内容使用ContentEquals。
- GitHub中途网络超时曾阻塞推送，随后网络恢复，12A `bd744078f4c193624fbfe1e3d86e32ca3b33367c`、12B `bcbb500f3bf7a64a379e00eec390d4fa3bc1649f`、12C `9eea512e132ea1b5e2438294d65927818870736f` 已成功推送。12D最终提交用git log核实，交付时再次检查本地/远程HEAD。
- 阶段12开发与本地验收完成；没有已知重大回归。阶段13未启动，已有统一输入接口与独立策略快照可供后续需求使用；不提前假设其范围。

### 阶段13A 视觉导入
- `tools/MugenImporter` 将 SFF v1/PCX/ACT 与 AIR 转为有界分页图集和确定性 Manifest；详细规则见 `docs/MUGEN_IMPORT.md`。Clsn 仅作为参考文本，战斗状态和规则没有改变。
- `SpriteTimeline` 接受逻辑帧时钟；`FighterVisual` 可选加载绑定与 Sprite2D，资源缺失回退原几何姿态。独立预览场景不引入可操作 Terry。StateVersion 15 / CoreVersion 16 不变。

### 阶段13B 决策 D153–D157
- D153：战斗 FighterId 与可选 VisualId 分离；每席可覆盖或限制到某个战斗角色。1v1/3v3 换人重新解析，默认不改变既有选择。
- D154：绑定 schema v2 支持动作、片段、循环、起始 tick、move/phase 时钟和按招式阶段映射；兼容 13A 整数绑定。缺失或非法资源退回几何图，合法空白帧保持透明。
- D155：res 图集经 ResourceLoader 缓存，注入式加载接口为后续外部资源保留边界；轴点 = AIR 偏移与 SFF 原点按 localcoord、全局偏移、比例及翻转组合，权威状态只读。
- D156：角色状态、动作名称切换驱动视觉时钟；招式可用 MoveElapsedFrames 跨逻辑阶段连续播放。Hitstop 沿用提交帧的帧前状态；已停止的回合只让闪白余时收尾，不推进角色动画。
- D157：Hit、ProjectileHit、ThrowHit 使用保留 alpha 的白色 shader，回合及换人复位。Godot 像素夹具验证白色、透明和恢复；原始第三方素材及本地绑定不纳入 Git。

### 阶段 13C-B 视觉投影

- 地面走路由同一提交帧的实际位移和已识别方向投影，原有状态与速度契约保持。向后步行、起跳预备和方向跳跃可选用素材变体，旧绑定兼容原 clip。
- 成功防御事件锁存当帧姿态，下一次合法防御可更新。硬直结束、换人或重开复位。站防和蹲防仍共用权威 Blockstun。
- `frameSchedule` 在绑定中提供显式 move tick 到源帧的映射。Kai/Rei 的招式时序、HitStop 和取消由权威状态驱动。具体绑定与 Vulkan 验收见 `docs/STAGE13C_B_ACCEPTANCE.md`。

### 阶段 13C-C 多对象表现

- 同一交锋的两席共用 `ThrowVisual`，抓取姿态取 `PendingThrow`，释放姿态只属于 `ThrowHit` 的施投招式实例。拆投、打断、退出招式及交锋重置清除对应姿态。施投、受投和后续倒地均使用权威坐标。
- 飞行道具按当前交锋 `ProjectileId` 与提交快照同步节点，显示年龄取寿命差，镜像取速度。拥有者换动作、被打断或进入 HitStop 不决定道具的存续。结束交锋清除显示对象。绑定缺失用原创几何图，绑定可给剪影颜色和镜像偏移。
- 事件特效以 `(scope, EventId)` 去重，坐标取同一提交帧。火花按提交帧计龄，闪白保留 alpha，结束后短时收尾。交锋重置清空所有瞬时表现。详见 `docs/STAGE13C_C_ACCEPTANCE.md`。

### 阶段 14 规则数据入口

沿用 D7、D115、D125 和 IKEMEN GO 研究 23 的有类型数据路线。原始角色脚本在离线审查和转换时使用，运行时由现有固定步长内核执行校验过的动作数据。首个实验配置选取 Terry State 200，MoveId 与视觉 Action 的关联单独校验。实际范围与验收矩阵见 [阶段 14 计划](superpowers/plans/2026-11-13-stage14-state200.md)。

- 角色 JSON v1 可选独立的命中及防御停顿。D96 继续消费各自的计数，同帧每个参与者取适用的最大值。未使用扩展的攻击和相杀读取原有对称值，飞行道具使用原有远程停顿路径。
- 可选地面待机受击框是局部正面积矩形列表。内核按权威朝向转换坐标，逐矩形测试近身及飞行道具接触。显式配置的近身攻击可在边缘相接时结算。启用蹲伏过渡的角色在第一 tick 可用站姿框，防御资格仍由当 tick 输入和逻辑状态确定。
- 可选地面受击运动按比例保留速度，从既有硬直及输入快照取得末次位移和蹲防摩擦，缺省沿用攻击定义的线性衰减。快照格式和帧调度保持。
- 实验席位只在 1v1 由显式 JSON 路径选择。Action 200 绑定使用 `MoveElapsedFrames` 驱动三个阶段，调试取证只读取已提交数据。实测和范围见 [阶段 14 验收](STAGE14_ACCEPTANCE.md)。

### 阶段 15 多招式实验

- 实验角色预设同时注册轻拳 `5A` 和重拳 `5B`。重拳使用同一个 `TerryStandHeavyPunch210`。开赛时选择预设，分别绑定 Action 210 或 211 及对应的 5/8/17 或 5/7/18 时序。内核只读选定的配置，快照结构保持。
- 可选的 `minimumStartDistance` 在帧开始快照上检查。可选的 `hurtboxTimeline` 使用与招式判定相同的 `MoveElapsedFrames`，每个连续时段可含多个局部矩形，须覆盖整招。回待机和被打断沿用既有状态清理。`counterDamage` 只在配置近身反击且目标处于启动或生效时选用，普通伤害提交顺序保持。
- 可选精灵绑定使用 `states` 映射状态，使用 `hitReactions` 按已提交的命中来源选择受击片段。受击时钟按各席位已提交的 HitStop 推进。第三方资源与时间线的对应关系仍由离线转换核对。
- `--capture-input-only` 与 `--shot` 同时指定时，取证器向既有对局输入处理提交计划按键，屏蔽桌面键盘和失焦清键对该次采集的影响。正常启动沿用 Godot 输入路径。

### 阶段 16 近远身选招与接触组

- 角色 JSON v1 可选 `groundBodyWidths` 和 `airBodyWidths`。拥有身体宽度的招式可以用 `maximumStartBodyDistance` 约束进入。内核从帧开始快照读取两席坐标、朝向和身体边缘，以己方前宽、对方朝向相关的前宽或后宽计算带符号距离。按配置顺序选择第一个同时满足姿态和距离的候选。入招后只按已选择的 MoveId 执行。
- 双席都配置地面身体宽度时，默认场地按两侧相邻的宽度推挤。其他对局继续使用原有场地间距。实验初始距离和调试框使用相同的角色配置。Stage 14/15 的实验文件仍使用既有中心距契约。
- 可选 `neutralAirHurtboxes` 只在垂直跳跃的 Airborne 状态投影局部受击框。Terry 中立大跳和小跳的原始 Action 41/49 使用相同矩形。位移、跳跃高度和其他空中状态保留既有规则。
- 可选 `contactGroup` 把同一次招式的多个命中窗分为独立接触。目标在快照中保存最后一次攻击实例和已结算组集合。已结算组在后续 tick 中去重，新实例重新建立集合。显式配置 `damageAfterPriorContact` 可读取先前命中或格挡。已有格挡硬直可接续格挡同一实例的另一段中段攻击。独立 HitPause 和原子伤害提交沿用原结算。
- Stage 16 两套配置共存 200、205、210、215，近身素材为 205/215，远身重拳仍按席位预设使用 210/211。双席的命令优先级、状态时钟、反应及视觉绑定分别读取自身配置。原版随机／反击控制器和脚本取消仍列为独立规则。详见 [原引擎基准](STAGE16_REFERENCE.md)。

### 阶段 17 动态伤害

- 可选 `damageRules` 定义角色级基础换算、有效攻击／防御倍率、补正范围和反击延续。可选 `dynamicDamage` 定义单一判定的普通与反击基数及接触分母开关。加载时拒绝混用固定伤害、非法倍率及缺少角色规则的动态判定。阶段 14–16 配置沿用旧结算。
- `FighterState.DamageScript` 保存席位独立 RNG、最近一次补正、反击年龄、自停顿及接触分母状态。使用 Park–Miller 步进和原版区间映射，初始化从显式种子派生。重放从快照继续，视觉和输入采样频率不决定随机消耗。原版还共享非伤害用途的随机流，两者的样本序列可不同。
- 同一结算前快照提供双方的伤害条件。反击读取攻击阶段与当前起手，允许 2 个非攻击控制器 tick 延续。自身命中停顿暂停控制器，单纯受击冻结仍可推进。招式中断清除局部停顿及分母，回合结束清理反击状态。旧状态机、接触优先级、KO 和原子伤害提交顺序保持。

### 阶段 18 站轻拳重进

- D91 的取消规则可用 `allowSameMove` 显式允许同招目标。`standingInputOnly` 在消费时读取当前下方向。如果站立规则的其他条件已满足、当前持下且无其他取消胜出，则使该输入失效，避免松下后触发另一招。`maximumInputAge` 用物理按键边沿计算年龄。招式选择按规则顺序和帧开始的身体距离进行，一帧最多发动一次取消。旧配置缺省继续拒绝同招目标。
- 新实例仍通过已有 `MoveStartedFrame` 区分。重进从 Startup tick 0 开始，清除旧命中和格挡确认。接触去重和招式局部伤害状态按新实例初始化。视觉通过已有招式时钟从新实例首帧播放。快照结构保持。
- `TerryReentry` 与 `TerryReentryMode2` 以阶段 17 配置为基线，仅为 200／205 添加收招 tick 7–11 的显式重进。两 tick 的站轻拳缓冲来自原引擎根指令和 Helper 的实际窗口。详见[原引擎取证](STAGE18_REFERENCE.md)和[验收](STAGE18_ACCEPTANCE.md)。

### 阶段 19 Power Wave

- `TerryPowerWave` 是独立 1v1 实验预设。`useCommandPriority` 按有效命令的优先级尝试符合状态条件的候选。同一物理边沿共用消费记录。可选 `bufferPolicy` 将根命令的 HitPause 保留与 Helper 99999 的按住／松开衰减分开。未启用的配置继续使用原有 FIFO。
- State 1000 的 52 个本地 tick 驱动 Action 1000，tick 15 创建带独立 ID 和出生朝向的权威实体。可选 `ProjectileTimeline` 定义首帧位移、停止移动、年龄判定段、一次接触资格及独立存续。Helper 1020 命中或格挡只冻结目标。伤害读取接触前的发射者补正和目标反应状态。旧飞行道具继续使用原有行为。
- 可选击飞反应把震停、上升、回落、触地、反弹、倒地和起身存入 `FighterState` 快照。物理位移和可见反应片段读取同一反应阶段。起身的打击保护延续到恢复控制的提交 tick。地面快照在穿越地平面的 tick 保持 Y=0，由独立标志延迟到下一 tick 进入触地阶段。
- 可选 `forwardGuardDistance` 使用上一提交帧的招式和实体年龄，判断前方的接近防御威胁。持后角色停步，防御结算仍按实际输入及既有姿态处理。根角色和 Helper 的威胁区间分别来自各自的 `MoveType` 控制器。
- `ScriptedPauseState` 保存双席计数和下一个全局暂停 tick。该 tick 继续提交帧号和输入历史，角色动作、局部 HitPause、实体年龄、计时器和伤害 RNG 保持快照。视觉节点只投影已提交状态。取证与已知兼容边界见[阶段 19 验收](STAGE19_ACCEPTANCE.md)。

### 阶段 20 多来源脚本取消

- 实验预设 `TerryPowerWaveRoutes` 从冻结的阶段 19 配置派生，为 205 增加一条既有结构的取消规则。转换读取 205 的独立 AIR 时长与 State −2 的元素比较，计算该来源的有效动作窗口。200 和 205 均通过 `TryPriorityCancel` 提交到同一个 State 1000，继承各自的正常重进顺序及实例隔离。
- 模式 2 对照保留原有普通技和重进。其原版特殊技目标为 1050，独立于默认模式的 1000 和 1020。生成器对冻结文件和原版轨迹校验哈希后产生两套配置。详见[阶段 20 验收](STAGE20_ACCEPTANCE.md)。

### 阶段 21 多段招式取消

- `TerryPowerWaveHeavyRoutes` 在阶段 20 冻结预设的 215 上添加一条 `CancelDefinition`。215 的两个接触组依旧由攻击实例起始帧去重。取消在检测前提交，已有权威事件保留，待发生的第二段随旧实例结束。默认、200／205 取消和模式 2 分支的职责见[阶段 21 验收](STAGE21_ACCEPTANCE.md)。
- 原版受击退出保护、末段受击框、防御续接和进入守势前的残余位移通过可选数据配置。冻结快照保存残余位移和防御恢复时钟，碰撞只读取权威状态。既有历史预设未启用这些选项。

### 阶段 22 蹲姿普通技

- 同一物理边沿可映射到不同姿态的招式。距离回落顺序按各允许姿态独立校验。状态提交前，取消执行器按当前下方向许可蹲姿目标，并沿用优先级、命令缓冲和单次消费。State 400 以自己的 `CancelDefinition` 配置旧 tick 4，复用 State 1000、Helper 1020 和暂停执行器。
- `returnsToCrouch` 描述自然退出后的 State 11。`groundHurtboxes.crouchRise` 描述松下后 Action 12 的可控地面过渡。权威快照保存起身进度，Godot 读取该进度绘制。出招、受击或 KO 清除过渡。
- `lightGroundReaction` 和接收方 `groundHurtboxes.lightHitstun` 启用源素材的轻击反应框。时钟随快照保存，HitPause 冻结，后续反应替换它。原子结算、暂停和伤害公式沿用既有实现。

### 阶段 23 蹲姿重击

- `reserveGroundInput` 表示已确认的原版地面命令占用输入。它按当帧方向和新按键边沿识别，在可行动时与普通指令一起排序并消费同帧候选。阶段 23 的两套实验预设启用 CMD 优先级，使前下＋重拳优先于 400／410。默认模式保留弱 Wave 的更高优先级。占位没有对应招式或动画实例。
- `groundHitReaction` 为近战命中选择接收方数据。原有 `lightGroundReaction` 仍能读取，`Heavy` 使用 `heavyHitstun`，目标命中时为蹲姿且配置了 `crouchingHeavyHitstun` 时选择蹲姿分支。分支和受击时钟锁存在权威快照，独立 HitPause 和全局 Pause 冻结。新命中替换反应，退出硬直或 KO 时清除。
- 蹲姿重击反应使用接收方的 `crouchHitVelocityRetention` 及现有末步比例，并经可控蹲伏恢复。该受击姿态由接触决定，松下可影响恢复后的动作。Godot 通过同一权威时钟选择站姿或蹲姿的初始和滑动受击动画。
- 阶段 23 用同一受击时钟处理 205 打断蹲姿时的轻击和 Helper 晚段落地重击。仅晚段不击飞的 Wave HitDef 选择 `HeavyLow`。可选的站／蹲守势框按格挡时锁存的姿态查询，续防期间的可控守势按当前姿态查询。全局 Pause 与 HitPause 保持权威快照。

### 阶段 24 蹲重脚与扫倒反应

- 近战 `launchGravity`／`launchReaction` 沿用实体击飞的反应快照。`startInFall` 在震停结束后的第一个有效 tick 直接进入 Fall 并积分。接收方可配置 `groundHurtboxes.tripFall`，由同一权威时钟选择 Action 5070 的受击框及画面。撞地、反弹、倒地与起身继续由 `ScriptedLaunchLanding` 推进。可选 GroundTech 读取物理按键边沿，在接地时消费有效缓冲并进入独立受身阶段，受身持续期间保护接触并关闭推挤。HitPause 冻结各自角色时钟，全局 Pause 同时冻结世界和实体。反应阶段、本地时间、受身输入年龄和施放席位由不可变状态保存。
- 发射者的扫倒标记和实体的 `respectsAirFollowupRestriction` 同时启用时，实体从帧开始快照读取同一席位的追击限制。落地转换当帧仍读取空中受击，下一 tick 解锁。限制只作用于接触，实体出生、动画、移动和到期照常。旧预设继续使用各自的碰撞和反应定义。
- `rejectsLyingTargets` 让配置了该规则的攻击跳过倒地、反弹和起身的 L 类型目标，攻击框仍可用于绘制和观察。State 440 与 Stage 24 的弱 Wave 从原版 HitDef 读取此限制。两套预设均绑定完整的扫倒反应 Action，模式 2 的倒地画面使用同样的权威阶段。
- `reserveGroundInput` 可表达多个相对方向。即时占位沿用当帧边沿，带缓冲的占位在消费时读取方向，并抑制同帧落败的输入边沿。阶段 24 用它保留蹲轻脚和站重脚的 CMD 优先级，前下＋重拳的 310 保留项仍参与同一排序。State 440 的显式取消使用原版旧 tick 11–17，和前五个来源共用 `CancelDefinition`、优先级缓冲、State 1000 以及 Helper 1020。 [阶段 24 原版与门禁](STAGE24_ACCEPTANCE.md)

### 阶段 25 蹲轻脚与普通技衔接

- State 430 使用真实 `2C` 指令。指令按 a 的新边沿识别，动作与衔接在消费时检查当前蹲姿。旧边沿胜出时仅消费该边沿的候选。当帧新按的 b 可在原有短缓冲中自然到期。阶段 24 的占位与其冻结配置保持独立。
- 原版普通技衔接复用 `CancelDefinition`、`MoveElapsedFrames` 和 CMD 候选顺序。无接触确认要求的衔接仍新建攻击实例。可选 `reservedCancels` 允许已确认的未接入目标在指定源窗口内占用对应指令。占用后原动作继续运行，不产生新招式或攻击实例。动作可执行性由源窗口、有效方向和缓冲共同决定。
- `retainBufferedMotionHelper` 允许特定普通技衔接在消费物理按键时保留同边沿的独立手势 Helper 时钟。根手势与普通候选依原消费规则清除。后继招式仍按自己的许可窗口检查 Helper。430 回落至站轻拳后，持 x 可让原手势存活至该拳自己的 Wave 窗口，松 x 的样本先过期。
- `reserveGroundInput` 可搭配 `matchHeldMotion` 保留模式 2 的未接入特殊技。方向由手势识别器检查，候选在可控地面或自然恢复时参与选招。430 没有直接取消至弱 Wave 的窗口，既有六来源的窗口保持。模式 2 的该候选不创建 State 1000 或 Helper 1020。
- `matchCompletedMotion` 允许具有独立按键步骤的命令，在总体时限内读取最近一次完整的方向序列。重复单按末方向不会替换已完成的序列。换方向后的新按键边沿可完成手势，已使用的序列不能重复提交。阶段 25 的默认弱 Wave 与模式 2 的预留手势都启用此规则。普通技是否可回落仍由当前消费方向及来源窗口决定。
- `walkOnCrouchRecovery` 允许配置为回蹲的动作在自然恢复当帧读取前后方向，直接使用既有步行规则。430 使用该选项匹配原版 State −1 的 Walk 候选。同帧若已选中招式，仍优先提交招式。
- `LightLow` 根据接触时的目标姿态选择站姿或蹲姿的轻击反应，受击框和画面读取同一权威时钟。`rejectsFallingTargets` 根据已有受击倒地计划排除原版缺少 F 许可的目标，与 `rejectsLyingTargets` 独立。普通起跳仍可发生几何接触。通用空战和 430 原版空中反应参数属于后续范围。 [原版依据](STAGE25_REFERENCE.md)
- `retainsGroundGuardOnFollowup` 在目标仍处于地面格挡硬直时保留防御资格，并按新接触时的下方向选择高度与反应姿态。430 和 400 在阶段 25 预设中启用。后继独立攻击仍重新提交接触、停顿和硬直。松后不解除当前守势，松下可使后续下段命中。
