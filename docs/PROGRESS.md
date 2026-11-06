# PROGRESS.md — NewKOF 开发进度

> 最后更新：2026-11-07（阶段 02 补修完成）

---

## 当前开发阶段

**阶段 01：Godot 工程初始化 — ✅ 已完成**
**阶段 02：固定逻辑帧与模拟调度验收 — ✅ 已完成**
**阶段 02 补修：战斗状态与调度器帧同步 — ✅ 已完成**

---

## 阶段 02 补修成果（帧同步）

本轮只解决两个帧同步缺陷，未进入阶段 03。

### 问题 1：异常导致帧计数不同步

**缺陷**：`BattleSession.Advance()` 先调 `Scheduler.Advance()` 增加 `TotalSteps`，
**之后**才执行输入提供者与 `Step()`。中途抛异常 → 调度器已前进、状态未完成更新 → 半提交状态。

**修复（D17/D18）**：预算与记账分离
- `Scheduler.Advance()` 改为只**消耗时间预算**并返回可推进帧数，**不再增加 `TotalSteps`**
- 新增 `Scheduler.CommitStep(count)`：每真正提交一帧才记账
- 新增 `Scheduler.RecordDroppedSteps(count)`：异常后把未提交预算显式记为丢弃
- `BattleSession.Advance()` 用 `try/catch`：每帧 `Step()` 成功后才 `CommitStep()`；
  异常时记录丢弃预算并**原样 `throw`（绝不吞异常）**

**异常后的策略（明确）**：失败即终止本批次，**不回滚、不重试、不忽略**。
已提交帧保留，未提交帧不计入 `TotalSteps`，剩余预算记入 `DroppedStepCount`。
**不回滚累加器**——回滚会让同一段真实时间被重复计费，引入新漂移。

### 问题 2：StepExact 与调度器计数不一致

**缺陷**：`StepExact()` 只改 `CurrentState.Frame`，不改 `Scheduler.TotalSteps`。
与 `Advance` 混用时计数错位。

**修复（D19/D20）**：`StepExact` 改为走**同一记账路径**（`CommitStep`）。
两条路径可任意交替，帧号关系始终正确。
暂停语义明确：`Pause()` 阻止时间驱动推进，但**不阻止显式单步**（逐帧调试需要）。

### 额外发现并修复的 2 个数值缺陷

| # | 缺陷 | 实测 | 修复 |
|---|---|---|---|
| N1 | 单次巨大 delta 用「循环减步长」累积浮点残差 | `Advance(3600.0)` 得 **215999** 帧（少 1，残留 `acc=0.016666648439`） | 整除一次算整帧数 + 余数（O(1)），精确得 216000（D22） |
| N2 | 整帧数转 `long` 时 `(long)Math.Floor(∞)` 饱和为 **`long.MinValue`（负数）** | `DroppedStepCount` 停在 **0**（`if (pending > 0)` 为假，丢弃记账被整段跳过）；`_accumulator` 永久停在 **4.49e307**；之后每次 `Advance` 都返回上限帧数（永久满速追帧） | `MaxSafeWholeSteps = 1e15` 夹紧 + `SafeAddDropped` 饱和加法 + 末尾余量收敛（D23） |

> ⚠️ **更正记录（第二轮 Agent 实测）**：上一轮文档把 N2 写成「溢出为负 + `_accumulator` 变 NaN」。
> 在 .NET 8 上实测**不成立**：`(long)double.PositiveInfinity == long.MinValue`，因此
> `if (pending > 0)` 直接为假，代码走的是「跳过记录、累加器无限膨胀」路径，全程没有 NaN。
> 修复方案（夹紧 + 饱和 + 收敛）依然必要且正确，但**失效机理与观测值已按实测改写**。

### 语义澄清（D21）

**`DroppedStepCount` 记录的是被舍弃的模拟时间预算，不代表已经执行的逻辑帧。**
- 真实执行帧数只看 `TotalSteps`
- 满足 `TotalSteps + DroppedStepCount ≈ 累计投入时间 / FixedStepSeconds`

### ⚠️ 离线原型限定（重要）

**当前丢帧策略与帧同步保证只适用于当前离线原型。**
本项目尚无回滚联机、输入预测、跨平台确定性验证。`DroppedStepCount > 0` 表示该段时间
**没有被模拟**，在联机回滚场景会造成双方状态分叉，**不能**直接沿用为网络同步策略。
不得把「补帧上限 + 丢弃预算」承诺为未来回滚联机的确定性保证。

---

## 阶段 02 核心成果

### 发现并修复的 4 个真实缺陷

阶段 01 的 `BattleScene._Process` 用 `(int)(delta * 1e6)` 累加 + `1000000/60 = 16666us`
整数间隔推进逻辑帧。**实测证明该实现随时间漂移，且漂移量随渲染帧率变化**：

| FPS | 期望帧数(600s) | 旧实现 | 旧漂移 | 新实现 | 新漂移 |
|---|---|---|---|---|---|
| 30 | 36000 | 36001 | **+1** | 36000 | 0 |
| 60 | 36000 | 36000 | 0 | 36000 | 0 |
| 144 | 36000 | 35999 | **−1** | 36000 | 0 |
| 240 | 36000 | 35995 | **−5** | 36000 | 0 |
| 59.94 | 36000 | 36000 | 0 | 36000 | 0 |
| 165 | 36000 | 35997 | **−3** | 36000 | 0 |

根因：1/60 秒 = 16666.666... µs，**无法用整数微秒精确表示**。
截断误差（每渲染帧丢 0.67µs）+ 间隔误差（每逻辑帧少 0.67µs）在不同帧率下
彼此抵消或叠加，导致帧数随帧率漂移。

| # | 缺陷 | 修复 |
|---|---|---|
| F1 | 整数微秒累加导致帧率相关漂移 | 改为 **double 秒**累加 + 精确步长判定（D9） |
| F2 | `while` 补帧无上限，卡顿后死亡螺旋 | `MaxStepsPerAdvance` 上限 + `DroppedStepCount` 显式记录（D11） |
| F3 | 无暂停概念，`_Process` 无条件推进 | `Pause()/Resume()`，暂停期间不累积 delta（D12） |
| F4 | `(int)` 强转溢出风险 | 改用 double，并对 NaN/±Infinity/负值立即抛异常（D13） |
| F5 | 积压告警每帧刷屏（阶段 02 实现中自引入） | `_backlogWarningIssued` 标志位，只告警一次（D16） |

### 架构调整

**时间调度逻辑已全部移出 Godot 层**：

- 新增 `core/BattleCore/Simulation/FixedStepScheduler.cs` — 纯 C#，零 Godot 依赖
- 新增 `core/BattleCore/Simulation/BattleSession.cs` — 绑定调度器与 `Step()`，
  强制「输入帧号 == 状态帧号」不变量
- `scripts/BattleScene.cs` 改写 — `_Process` 只剩一行
  `_session.Advance(delta, inputProvider: null)`，**不含任何累加器/补帧/取整代码**

---

## 已实现功能

### 1. Godot 工程（表现层）

- `project.godot`：Godot 4.7 工程，主场景 `res://scenes/Battle.tscn`，1280×720，Forward+ 渲染器
- `NewKOF.csproj`：Godot.NET.Sdk 4.7.2，`net8.0`，引用 `BattleCore`
- `scenes/Battle.tscn`：
  - `Background` / `Sky` / `Ground` / `GroundLine` — 背景与地面
  - `Fighter1` — Node2D 容器，内含 60×120 红色矩形
  - `Fighter2` — Node2D 容器，内含 60×120 蓝色矩形
  - `Hud` — 左上角显示 `Logic Frame: N`
- `scripts/BattleScene.cs`（阶段 02 改写）：
  - 把 `delta` 交给 `BattleSession.Advance()`
  - 按返回值决定是否重绘
  - 积压超过上限时告警一次
- `scripts/CaptureHelper.cs`：开发期截图工具（`--shot=` 参数启用，正常运行时零副作用）

### 2. 战斗内核（`core/BattleCore`，纯 C#）

| 类型 | 状态 | 说明 |
|---|---|---|
| `BattleSimulation` | ✅ | `Step(BattleState, FrameInput) → BattleStepResult` |
| `FixedStepScheduler` | ✅ 阶段 02 | 时间累积与补帧决策，可独立测试 |
| `BattleSession` | ✅ 阶段 02 | 调度 + 模拟 + 状态绑定 |
| `BattleState` | ✅ | `sealed record`，不可变；`Frame` + `Fighters`(只读2人) + `StateVersion` |
| `FighterState` | ✅ | `readonly record struct` |
| `FrameInput` | ✅ | `readonly record struct`：Frame + P1/P2 `InputBits` |
| `BattleStepResult` | ✅ | 帧前/帧后帧号、输入、新状态 |
| `InputBits` | ✅ | `[Flags]`：4 方向 + 4 攻击键 |
| `BattleCoreInfo` | ✅ | `CoreVersion=1`、`LogicFrameRate=60` |

### 3. 状态与快照契约（任务 3 结论）

- **所有权明确**：`BattleState` 只能由 `BattleSimulation.Step()` 创建；
  `BattleSession.CurrentState` 为 `{ get; private set; }`，无 public setter（有测试守护）。
- **无可变引用泄漏**：构造时 `fighters.ToArray()` + `Array.AsReadOnly()`；
  测试 `BattleState_ExposesNoMutableReference` 验证底层不是可写数组。
- **帧间互不污染**：`Step()` 从不修改入参，每次返回新实例；
  测试 `IntermediateSnapshot_IsNotAffectedByLaterSteps` 验证。
- **旧状态可重放**：同一旧状态 + 同一输入可重复得到相同结果；
  测试 `OldState_CanReExecuteSameInput_AndReproduceResult` 验证。
- **`StateVersion` 用途**：目前是常量 `1`，**仅作快照格式兼容标记**，
  尚无升级/迁移机制（见 `ARCHITECTURE.md` 第 5 节问题 1）。

---

## 测试状态

**执行命令**
```bat
cd /d C:\Users\32360\Desktop\NewKOF\tests\BattleCore.Tests
dotnet test
```

**最近一次实际运行结果（2026-11-07，第二轮 Agent 复核实测）**
```
已通过! - 失败: 0，通过: 93，已跳过: 0，总计: 93，持续时间: 253 ms - BattleCore.Tests.dll (net8.0)
```

| 测试文件 | 数量 | 内容 |
|---|---|---|
| `BattleSimulationTests.cs` | 8 | 阶段 01 基础 Step 契约 |
| `FixedStepSchedulerTests.cs` | 19 | 调度器：多帧率一致性、补帧、暂停、非法输入 |
| `DeterminismTests.cs` | 15 | 确定性、长跑、快照隔离、旧状态重放 |
| `GodotIntegrationContractTests.cs` | 7 | Godot 层依赖的内核契约 |
| `FrameSyncRegressionTests.cs` | 12 | **补修**：问题 1/2 回归 |
| `SchedulerAccountingTests.cs` | 13 | **补修**：丢弃语义、超大 delta、不变量、**记账 API 权限**（本轮 +1） |
| `SchedulerNumericalRobustnessTests.cs` | 15 | **补修**：NaN/溢出/精度健壮性 |

### 补修回归测试的可信度

**修复前基线（已实测）**：`FrameSyncRegressionTests` 12 个测试中 **10 个失败**，
两个缺陷均被真实复现，不是推测。
**修复后**：全部通过。新增的 `SchedulerNumericalRobustnessTests` 又暴露出
2 个数值缺陷（N1/N2），同样遵循「先失败 → 再修复 → 转绿」的流程。

### 第二轮 Agent 独立复核（2026-11-07）

不照搬上一轮汇报，全部重新执行：

| 复核项 | 方法 | 实测结果 |
|---|---|---|
| 测试基线 | `dotnet test`（`tests/BattleCore.Tests`） | **93/93 通过**（原 92 + 本轮新增 1） |
| 编译 | 根目录 `dotnet build` | 0 警告 0 错误 |
| 「修复前 10/12 失败」 | `git archive HEAD` 还原修复前代码树，只放入 `FrameSyncRegressionTests.cs` 运行 | **失败 10 / 通过 2 / 总计 12**，与汇报一致 |
| N1 | 修复前代码树跑 `Advance(3600.0)`（上限 300000） | steps=**215999**、`acc=0.016666648439`，实测复现 |
| N2 | 修复前代码树跑 `Advance(double.MaxValue/4)` | steps=8、**dropped=0**、`acc=4.49e307`，每次都返回 8 → 与旧文档「NaN / 负数」**不符**，已更正 |
| 权限风险 K9 | 反射检查 `FixedStepScheduler` 公开 API | 原为 public，可绕过 `BattleSession` 让 `TotalSteps` 跑在状态前面 → **改为 internal**，新增反射测试 |
| Godot 启动 | `--quit-after 400` 图形模式 | 正常启动（Vulkan 1.4.325 / RTX 3060），首帧丢弃 4 帧、告警仅一次 |

### 阶段 01 测试（8 个，全部保留通过）

| # | 测试方法 | 验证内容 |
|---|---|---|
| 1 | `Step_AdvancesExactlyOneLogicFrame` | 每次 Step 恰好推进 1 帧 |
| 2 | `Step_ManyTimes_ReachesExactFrameCount` | 600 次 Step 后帧号精确为 600 |
| 3 | `Step_DoesNotMutateInputState` | 原状态不变、返回新实例 |
| 4 | `Step_SameInitialStateAndInputs_ProduceIdenticalResults` | 确定性 |
| 5 | `Step_RejectsMismatchedInputFrame` | 帧号不匹配抛异常 |
| 6 | `Step_RecordsInputInResult` | 结果正确记录输入与前/后帧号 |
| 7 | `BattleState_RejectsWrongFighterCount` | 非 2 名角色被拒绝 |
| 8 | `BattleState_IsReadOnlyAfterConstruction` | 修改源数组不影响已构造状态 |

### 阶段 02 新增测试（46 个）

**`FixedStepSchedulerTests.cs`（调度器）**

| 测试 | 验证内容 |
|---|---|
| `Advance_600Seconds_ProducesExactly36000LogicFrames_RegardlessOfRenderRate` (×8 帧率: 25/30/59.94/60/144/165/240/360) | 600 秒恒为 36000 帧，与渲染帧率无关 |
| `Advance_10Seconds_ProducesExactly600LogicFrames` (×4 帧率) | 10 秒恒为 600 帧 |
| `Advance_SingleCallEqualToStep_ProducesOneFrame` | 单步 = 1 帧 |
| `Advance_AccumulatesSubStepDeltas_WithoutLosingOrDuplicatingFrames` | 100×10ms 累加 = 60 帧，不丢不重 |
| `Advance_LargeDelta_IsCappedAndDroppedFramesAreAccountedFor` | 补帧上限生效且欠帧显式记账 |
| `Advance_BacklogIsCapped_NoUnboundedCatchUp` | 卡顿后不会无限追帧 |
| `Advance_WhenPaused_DoesNotAdvance` | 暂停不推进 |
| `Advance_AfterResume_DoesNotBackfillPausedTime` | 恢复后不回补暂停时间 |
| `Advance_RejectsNegativeDelta` | 拒绝负 delta |
| `Advance_RejectsNaNAndInfinity` | 拒绝 NaN/±Infinity |
| `Constructor_RejectsNonPositiveMaxSteps` | 非法上限被拒绝 |
| `Advance_AccumulatorStaysBelowOneStep` | 累加器恒在 [0, 1步长) |
| `Advance_ZeroDelta_ProducesNoSteps` | 零 delta 不推进 |
| `Advance_TotalStepsIsMonotonic` | 总步数单调不减 |

**`DeterminismTests.cs`（确定性与长跑）**

| 测试 | 验证内容 |
|---|---|
| `StepExact_ReachesExactFrameNumber` (×3: 60/600/36000) | 帧编号精确 |
| `LongRun_36000Frames_HasNoAccumulatedDrift` | 36000 帧（600 秒）零漂移，两条路径均验证 |
| `DifferentRenderRates_ProduceSameLogicSequenceForSameDuration` (×4 帧率) | 同模拟时段 → 同逻辑结果 |
| `SameInitialStateAndInputs_ProduceByteIdenticalResults` | 同初态同输入 → 同结果 |
| `DifferentSimulationInstances_ProduceSameResult` | 不同实例 1000 帧结果一致 |
| `IntermediateSnapshot_IsNotAffectedByLaterSteps` | 中间快照不受后续 Step 影响 |
| `OldState_CanReExecuteSameInput_AndReproduceResult` | 旧状态可重放 |
| `PausedSession_DoesNotAdvance` | 暂停不推进 |
| `InputProvider_ReceivesExactlyTheFrameBeingSimulated` | 输入帧号严格连续对应 |
| `InputProvider_ReturningWrongFrame_IsRejected` | 错帧输入被拒绝 |
| `BattleState_ExposesNoMutableReference` | 无可变引用泄漏 |

**`GodotIntegrationContractTests.cs`（Godot 层契约）**

| 测试 | 验证内容 |
|---|---|
| `Session_UnderIrregularDeltas_NeverSkipsOrRegressesFrames` | 5000 次抖动 delta（4–40ms）无跳号/倒退 |
| `AdvanceReturnValue_EqualsFrameDelta` | 返回值 == 帧号增量（`BattleScene` 重绘依据） |
| `CurrentState_CanOnlyChangeThroughAdvance` | `CurrentState` 无 public setter |
| `TenMinuteRun_HasZeroDrift` (×3 帧率) | 10 分钟零漂移、零丢弃 |

> ⚠️ **边界声明**：纯 C# 测试**无法实例化 Godot 节点**（`BattleScene : Node2D` 需要
> Godot 运行时）。`GodotIntegrationContractTests` 验证的是 `BattleScene` **唯一依赖的
> 内核契约**，不是"Godot 调度已被测试覆盖"。Godot 层的真实运行证据见下方验收记录。

**测试不依赖 Godot 图形窗口**：测试工程只引用 `BattleCore.csproj`，`dotnet test` 全程无 GUI。

---

## 验收记录

| 验收项 | 结果 | 证据 |
|---|---|---|
| Godot 工程能够正常启动 | ✅ | `Vulkan 1.4.325 - Forward+ - Device: NVIDIA RTX 3060 Laptop GPU`，`--quit-after 900` 退出码 0 |
| C# 核心和表现层能够编译 | ✅ | `dotnet build` → 0 警告 0 错误 |
| 原有 8 项测试继续通过 | ✅ | 全部保留且通过 |
| 新增调度与确定性测试全部实际运行 | ✅ | 54/54 通过（新增 46） |
| 长时间模拟无未经解释的帧漂移 | ✅ | 36000 帧（600 秒）精确无漂移；10 分钟多帧率零漂移 |
| 同初态同输入的权威结果可重复 | ✅ | `SameInitialStateAndInputs_ProduceByteIdenticalResults` 等 |
| 交接文档与代码一致 | ✅ | 本次已同步更新三份文档 |
| 场景画面正确 | ✅ | 截图 `shots/p02.png` 像素统计：红 `(229,64,76)` 7200px、蓝 `(64,140,242)` 7200px |

**运行期观察（真实，未修饰）**：
- 启动首帧因资源加载尖峰，`DroppedStepCount = 3`，触发一次积压告警。
- 之后 900 帧稳态运行**不再增加**丢弃数。
- 该告警在修复前会每帧刷屏（数十条），已通过 `_backlogWarningIssued` 修复为只输出一次。

---

## 尚未实现功能（范围外，非缺陷）

- 角色移动 / 跳跃 / 攻击 / 受击
- 状态机、招式数据、帧数据
- 碰撞检测（hitbox / hurtbox / pushbox）
- 伤害、硬直、combo、取消规则、Hitstop、KO
- 动画、精灵图、音效、特效
- 回合 / 计时
- 键盘手柄输入映射（当前输入固定为 `InputBits.None`）
- 指令识别与输入缓冲
- 快照序列化 / 回滚 / 重放
- 联机、AI、训练模式

---

## 下一阶段计划（阶段 03 建议）

**进入前必须先读** `ARCHITECTURE.md` 第 5 节「尚未决定的架构问题」。

阶段 02 已解决"何时推进逻辑帧"，阶段 03 应当解决"推进时发生什么"：

1. **决策落定**：定点数格式、SOCD 规则、碰撞几何、P0–P9 阶段划分（至少 P0/P4/P8）。
   写入 `ARCHITECTURE.md` 第 4 节。
2. **输入链路**：实现 `InputSystem.Normalize`（SOCD + 相对朝向）；
   在 `BattleScene` 接入键盘映射，产出真实 `FrameInput`。
   > 注意：`BattleSession.Advance` 的 `inputProvider` 参数已就绪，接入时直接使用。
3. **运动系统**：`FighterState` 增加速度字段，实现 `MovementSystem.Integrate`
   （走/跳/重力 + 地面边界），保持整数/定点。
4. **碰撞与推挤**：AABB pushbox 推挤，先只做身位不重叠。
5. **测试扩展**：每项新规则补确定性测试；建立"黄金帧序列"回归测试。
6. **不要**在阶段 03 引入攻击/伤害（那是后续阶段）。

**每完成一个子任务，立即更新本文件与 `AGENT_HANDOFF.md`。**
