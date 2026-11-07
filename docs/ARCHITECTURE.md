# ARCHITECTURE.md — NewKOF 架构说明

> 最后更新：阶段 04 完成（2026-11-07，含 D54–D60）
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
│       ├── Input/FrameInput.cs
│       ├── Input/InputBuffer.cs        # ★ 阶段 03A：渲染采样 → 逻辑帧输入（边沿闩锁 + 历史）
│       ├── Input/DirectionResolver.cs  # ★ 阶段 03A：SOCD + 绝对方向 → 相对朝向
│       ├── Commands/                   # ★ 阶段 03B-1：指令识别（纯数据 + 识别器）
│       │   ├── CommandDirection.cs     # 相对朝向九宫格方向
│       │   ├── CommandDefinition.cs    # 指令数据（序列 / 按键 / 跨度 / 容差 / 优先级 / 时效）
│       │   ├── CommandMatch.cs         # 识别结果（完成帧 / 按键帧 / 诊断）
│       │   ├── CommandRecognizer.cs    # 方向变化压缩 + 匹配 + 冲突裁决 + 缓冲
│       │   └── CommandCatalog.cs       # 默认四条指令（236A / 623A / 214B / 66）
│       ├── State/FighterState.cs
│       ├── State/BattleState.cs
│       └── Simulation/
│           ├── BattleSimulation.cs
│           ├── BattleStepResult.cs
│           ├── FixedStepScheduler.cs   # ★ 阶段 02：固定步长调度器（可独立测试）
│           └── BattleSession.cs        # ★ 阶段 02：调度 + 模拟 + 状态绑定
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
6. **无浮点权威字段**：当前所有权威数值为 `int`。后续若引入速度/坐标小数，须使用定点数并写入规则版本。
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
2. **定点数格式**：位置/速度采用 Q16.16 还是 Q8.8？取整发生点在哪一步？（参考 22 号文档"数值约束"）
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
