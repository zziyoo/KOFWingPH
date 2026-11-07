# PROGRESS.md — NewKOF 开发进度

> 最后更新：2026-11-07（阶段 03A 结项：最终补修完成，已提交并推送）

---

## 当前开发阶段

**阶段 01：Godot 工程初始化 — ✅ 已完成**
**阶段 02：固定逻辑帧与模拟调度验收 — ✅ 已完成**
**阶段 02 补修：战斗状态与调度器帧同步 — ✅ 已完成（已提交 `850c4db`）**
**阶段 03A：基础输入链路 — ✅ 已完成（已提交 `0be9919` 并推送）**
**阶段 03A 专项补修：输入一致性与调试工具 — ✅ 已完成（已提交 `f145275` 并推送）**
**阶段 03A 最终补修：输入与战斗状态原子提交 — ✅ 已完成（已提交 `0bd0c6d` 并推送）**
**阶段 03A — ✅ 正式结项**

---

## 阶段 03A 成果（基础输入链路）

### 边界

只做「输入进入正确的逻辑帧」，**不做**：↓↘→ / →↓↘ 等指令识别、蓄力、必杀优先级、
输入缓冲取消、移动/跳跃/攻击/碰撞/伤害、投技、连击、回滚联机。角色位置仍不变。

### 新增模块

| 模块 | 位置 | 职责 | 明确不做 |
|---|---|---|---|
| `InputBuffer` | `core/BattleCore/Input/InputBuffer.cs` | 渲染采样 → 逻辑帧输入：电平保持、按下边沿闩锁、逐帧历史 | 键盘 API、SOCD、指令识别 |
| `DirectionResolver` | `core/BattleCore/Input/DirectionResolver.cs` | 纯函数：SOCD 清理 + 绝对方向 → 相对朝向 | 录制、状态推进 |
| `KeyboardInputMap` | `scripts/KeyboardInputMap.cs` | 键盘事件 → `InputBits`，唯一接触 Godot 输入 API 的类 | SOCD、权威判定 |
| `CaptureHelper` 扩展 | `scripts/CaptureHelper.cs` | 新增 `--key=<frame>:<down\|up>:<KeyName>` 合成按键注入（仅调试参数启用） | 正常运行时零副作用 |

### 规则（实现细节见 `ARCHITECTURE.md` D26–D33）

| 项 | 规则 |
|---|---|
| SOCD-H | 左右同时按下或都不按 → **水平中立**（相反方向抵消）。不采用「后输入优先」：`FrameInput` 只存电平位，无事件顺序，无法唯一还原 |
| SOCD-V | 上下同时按下 → **上优先**（跳跃优先） |
| 绝对 / 相对 | `InputBits` 存**绝对方向**（物理键）；相对朝向只在 `DirectionResolver` 用 `Facing` 换算 |
| 历史归属 | `InputBuffer.History`（内核层）保存每逻辑帧一条 `FrameInput`，帧号必须连续，否则抛异常 |
| 边沿传递 | 采样时 `pending \|= (now & ~held)`；`Commit` 时消费一次即清零（`Take` 是「准备+提交」的内部便捷方法）。0 帧不丢、N 帧不重复 |
| 多次按下 | 两次逻辑帧之间按多次 → 合并为一帧，并累加 `CoalescedPressCount`（可观测，非静默） |
| 暂停 | 不产生输入帧；已闩锁边沿保留到恢复后第一帧 |
| 丢弃预算 | 被丢弃的时间不产生逻辑帧，也不产生输入帧 |
| 双人独立 | 每人独立 `held`/`pending`，互不影响 |
| 采样方式 | Godot 按**按键事件**采样（非每渲染帧轮询），可捕获同帧内按下又松开；失焦时 `ReleaseAll()` 防卡键 |

### 键位

| 玩家 | 方向 | A / B / C / D |
|---|---|---|
| P1 | W / A / S / D | J / K / U / I |
| P2 | ↑ / ↓ / ← / → | 小键盘 1 / 2 / 4 / 5，或 `,` `.` `/` `;`（无小键盘的笔记本） |

F1 切换暂停（只阻止时间驱动推进，不影响显式单步）。

### 新增测试（30 项，总数 93 → 123）

| 文件 | 数量 | 覆盖 |
|---|---|---|
| `InputBufferTests.cs` | 13 | 单方向、按住/松开、点击边沿只出现一帧、多帧批次不重复消费、双人独立、多键组合、历史连续性、非法帧号 |
| `DirectionResolverTests.cs` | 10 | 单方向、双方向 SOCD（左右中立 / 上优先）、朝向翻转、攻击位透传、双轴不串扰 |
| `InputPipelineTests.cs` | 7 | 输入与逻辑帧绑定、0/1/N 帧批次、暂停保留边沿、丢弃预算不产帧、抖动 delta 下历史与状态帧号一致、固定输入日志在 30/60/144/240fps 下结果一致 |

**测试有效性验证（变异测试）**：把 `Take()` 改成只用 `held`、丢弃 `pending` 后，
`InputBufferTests` + `InputPipelineTests` 中 **5 项立即失败**（点击边沿、多帧批次、
双人独立边沿等），证明断言不是空转；还原后 123/123 通过。

### 真实 Godot 运行证据

| 场景 | 截图 | 实测 HUD |
|---|---|---|
| 双人按键（P1 按住 D+J，P2 按住 ←+小键盘1） | `shots/input_p1p2.png` | `Logic Frame: 63` / `P1: R A` / `P2: L A` |
| 按住 S 后按 F1 暂停 | `shots/input_paused.png` | `Logic Frame: 21 [PAUSED · F1]` / `P1: D` / `P2: -` |

- 按键通过 `Input.ParseInputEvent`（Godot 自身输入管线）注入，与真实键盘走同一条
  `_Input → KeyboardInputMap → InputBuffer` 路径；OS 级按键注入被安全策略拦截，未使用。
- 暂停截图在 200 个渲染帧后仍停在逻辑帧 21，证明暂停确实阻止了时间驱动推进。
- 启动首帧丢弃 1~4 帧（渲染尖峰），告警只输出一次。

---

## 阶段 03A 最终补修（2026-11-07）——输入与战斗状态原子提交

### 缺陷（P1）

`BattleSession.Advance()` / `StepExact()` 的顺序是
`CurrentState = Step(...)` → `Commit()` → `CommitStep()`。
若 `Commit()` 抛异常，**战斗状态已推进、调度器未记账**，`Frame - 初始帧号 == TotalSteps` 被破坏。

### 复现（修复前实测）

新增 `CommitFailSource`（`Step` 成功、`Commit` 按帧号抛异常，自身无写入）：

| 用例 | 修复前实测 | 期望 |
|---|---|---|
| `Advance_CommitThrows_DoesNotAdvanceStateOrAccount` | 帧号 **1** | 0 |
| `StepExact_CommitThrows_DoesNotAdvanceStateOrAccount` | 帧号 **1** | 0 |
| `MultiFrameBatch_CommitFailureAtSecondFrame_KeepsOnlyCommittedFrames` | 帧号 **2** | 1 |
| `CommitFailure_ThenRetry_CommitsEdgeExactlyOnce` | 帧号 **1** | 0 |

（4 项在旧实现上失败，修复后全部通过。）

### 最终提交顺序（D36）

```
nextState = BattleSimulation.Step(CurrentState, input).State   // 候选状态，不赋值
inputSource?.Commit(input)                                     // 成功后才写历史/清边沿
CurrentState = nextState                                       // 落权威状态
Scheduler.CommitStep()                                         // 最后记账
```

`Advance` 与 `StepExact` **共用同一顺序**。

- `Step()` 失败 → 状态、计数、历史、边沿全部不变；预算记为丢弃。
- `Commit()` 失败 → 同上；`nextState` 被丢弃，重试同一帧得到同一输入。
- 批中失败 → 前序帧完整保留（状态 + 计数 + 历史三者一致）。

### `InputBuffer.Commit()` 的原子性

校验（帧号连续性、提交输入 == 当前采样）**全部在写入之前**完成，
失败时 `History`、`pending`、`CoalescedPressCount` 均不变。
第三方 `IInputSource` 必须自行满足「失败无副作用」；`BattleSession` 只保证自身状态与计数
不被污染，不为第三方内部状态背书（已写入 `ARCHITECTURE.md` 与 `IInputSource` 文档）。

### 验收（实际执行）

| 项 | 结果 |
|---|---|
| BattleCore 独立构建 | 0 错误 |
| 根工程编译 | 0 警告 0 错误 |
| 全部单元测试 | **147/147 通过**（141 + 新增 6） |
| 阶段 02 调度器相关回归 | 79/79 通过（调度器 / 帧同步 / 记账 / 数值 / 确定性） |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | 双人输入 + F1 暂停均正常；见下 |

**图形运行证据** `shots/final_03a.png`：

```
[CaptureHelper] 渲染帧 20 注入 D/J/Left/Kp1 按下（当时逻辑帧 19）
[CaptureHelper] 渲染帧 80 注入 F1 按下（当时逻辑帧 45）
[CaptureHelper] 截图已保存（渲染帧 150，逻辑帧 45）
HUD：Logic Frame: 45 [PAUSED · F1] / P1: R A / P2: L A
```

渲染帧从 80 推到 150，逻辑帧仍停在 45 → 暂停有效、无幽灵帧；两名玩家输入同时被记录。

**阶段 03A 正式结项。**

---

## 阶段 03A 专项补修（2026-11-07）

### 修复的三个缺陷

| # | 缺陷 | 修复前实测证据 | 修复 |
|---|---|---|---|
| I1 | `History` 只读是假的：返回的仍是 `List<FrameInput>` | `History as List<FrameInput>` 非 null；`(IList<FrameInput>)History` 的 `IsReadOnly == false`，`Add/Clear` 直接改写内部历史 | 构造时创建**单实例** `ReadOnlyCollection<FrameInput>` 包装（不额外拷贝）；强转 `List` 返回 null，`IList.Add` 抛 `NotSupportedException`（D34） |
| I2 | 输入先提交、模拟后失败 → 幻影帧 + 重试错帧 | `Step()` 溢出失败后 `History.Count == 1`（战斗帧没提交）；重试同一帧抛「输入帧号必须严格连续」 | 两阶段 `IInputSource.Prepare` → `Step` → `Commit`；失败帧不写历史、不消费边沿；`Take()` 收为 `internal`（D35） |
| I3 | 调试参数 `--key=` 用渲染帧计数，文案却像逻辑帧 | 实测渲染帧 20 时逻辑帧为 17，渲染帧 150 时逻辑帧 70 | 改名 `--renderkey=`（旧名保留 + 告警一次），注入日志同时打印渲染帧与当时逻辑帧 |

### P2 历史容量：改了，依据如下

- 默认保留 **600 帧（10 秒 @60Hz）**，超限淘汰最旧帧并置 `IsHistoryTruncated`。
- 依据：当前唯一潜在消费者（指令识别）只需秒级窗口；完整录像/回滚属独立系统，
  不应塞进缓冲器（会同时破坏内存上界与回放语义）。
- **影响**：被淘汰的帧不能再用于回放或长窗口识别；需要完整录像时必须另建录像模块（未实现）。
- 对齐判断改用 `IsAlignedWith(frame)`（最后一帧 == frame-1）。旧测试
  `InputHistoryFrames_AlwaysMatchStateFrames` 断言 `History.Count == Frame`，容量生效后必然失效，
  已按验收不变量改写（不是放宽断言，而是换用稳定可比对的条件）。

### 测试（123 → 141）

新增 `InputIntegrityTests` 18 项：历史只读 / Prepare 不提前提交 / Commit 只写一次 /
模拟失败不留幻影帧 / 批中失败只保留已提交帧 / 边沿跨失败恢复且只消费一次 /
`StepExact` 与暂停单步 / 双输入源互斥 / 非零初始帧 / 容量与构造参数 / 普通委托幂等。

**变异测试**（证明新增断言确实能发现故障）：

| 变异 | 失败用例 |
|---|---|
| `History => _history`（退回裸列表） | 2 项（`History_IsNotBackedByMutableList`、`History_CannotBeMutatedThroughIListInterface`） |
| 把 `Commit` 提前到 `Step` 之前 | 3 项（幻影帧、重试、批中失败） |

### 真实 Godot 运行验证

```
[CaptureHelper] 将在第 150 个渲染帧截图（渲染帧计数，不是逻辑帧）
[CaptureHelper] 渲染帧 20 注入 D 按下（当时逻辑帧 17）
[CaptureHelper] 截图已保存（渲染帧 150，逻辑帧 70）
```

截图 `shots/fix_input.png`：`Logic Frame: 69` / `P1: R A` / `P2: L A`。
旧参数 `--key=` 仍可用，并输出「已改名为 --renderkey=」告警。

### 未完成 / 边界

- 未实现「按逻辑帧注入」：那需要重放系统把逐帧输入日志直接喂给 `IInputSource`，本轮不做。
- 未做完整录像、回滚、网络同步。
- 普通 `Func<int, FrameInput>` 输入源无法回滚：契约要求「同一帧号返回同一输入（幂等）」，
  已写入 `BattleSession.Advance` 的 XML 文档与 K15。
- 已提交 `f145275` 并推送（`083abd5..f145275`，快进、无强推）。

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
  - `Hud` — 左上角三行：`Logic Frame: N`、`P1:`、`P2:`（显示最近一个已消费逻辑帧的输入）
- `scripts/BattleScene.cs`（阶段 02 改写 / 阶段 03A 接入输入）：
  - 把 `delta` 与 `_inputBuffer.Take` 交给 `BattleSession.Advance()`
  - 按返回值决定是否重绘
  - 积压超过上限时告警一次
  - `_Input` 只做两件事：F1 切暂停 + 转发按键事件给 `KeyboardInputMap`
- `scripts/KeyboardInputMap.cs`（阶段 03A）：键盘事件 → `InputBits`，`ReleaseAll()` 防失焦卡键
- `scripts/CaptureHelper.cs`：开发期截图 + 合成按键注入工具（`--shot=` / `--key=` 参数启用，正常运行时零副作用）

### 2. 战斗内核（`core/BattleCore`，纯 C#）

| 类型 | 状态 | 说明 |
|---|---|---|
| `BattleSimulation` | ✅ | `Step(BattleState, FrameInput) → BattleStepResult` |
| `FixedStepScheduler` | ✅ 阶段 02 | 时间累积与补帧决策，可独立测试 |
| `BattleSession` | ✅ 阶段 02 | 调度 + 模拟 + 状态绑定 |
| `InputBuffer` | ✅ 阶段 03A | 渲染采样 → 逻辑帧输入：边沿闩锁、逐帧历史、帧号连续性校验 |
| `DirectionResolver` | ✅ 阶段 03A | 纯函数：SOCD（左右中立/上优先）+ 绝对方向 → 相对朝向 |
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

**最近一次实际运行结果（2026-11-07，阶段 03A 最终补修后实测）**
```
已通过! - 失败: 0，通过: 147，已跳过: 0，总计: 147，持续时间: 215 ms - BattleCore.Tests.dll (net8.0)
```

| 测试文件 | 用例数（`--list-tests` 实测） | 内容 |
|---|---|---|
| `BattleSimulationTests.cs` | 8 | 阶段 01 基础 Step 契约 |
| `FixedStepSchedulerTests.cs` | 24 | 调度器：多帧率一致性（Theory 展开）、补帧、暂停、非法输入 |
| `DeterminismTests.cs` | 16 | 确定性、长跑、快照隔离、旧状态重放 |
| `GodotIntegrationContractTests.cs` | 6 | Godot 层依赖的内核契约 |
| `FrameSyncRegressionTests.cs` | 12 | **补修**：问题 1/2 回归 |
| `SchedulerAccountingTests.cs` | 13 | **补修**：丢弃语义、超大 delta、不变量、**记账 API 权限** |
| `SchedulerNumericalRobustnessTests.cs` | 14 | **补修**：NaN/溢出/精度健壮性 |
| `InputBufferTests.cs` | 13 | **03A**：电平/边沿/双人独立/历史连续性 |
| `DirectionResolverTests.cs` | 10 | **03A**：SOCD 与相对朝向 |
| `InputPipelineTests.cs` | 7 | **03A**：输入与逻辑帧绑定、暂停、多帧批次 |
| `InputIntegrityTests.cs` | 24 | **03A 补修**：只读历史、两阶段提交、失败恢复、容量、**Commit 失败不前进状态** |
| **合计** | **147** | 与 `dotnet test` 的「总计: 147」一致 |

> 说明：上表是 **xUnit 用例数**（`Theory` 的每个 `InlineData` 计一条），不是方法数。
> 早先版本按方法数填写，与「总计」对不上，已按 `--list-tests` 实测校正。

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
- 手柄输入映射（键盘基础输入已在阶段 03A 接入；手柄、键位重绑定 UI 未做）
- 指令识别（↓↘→ / →↓↘）、蓄力、输入缓冲取消
- 快照序列化 / 回滚 / 重放
- 联机、AI、训练模式

---

## 下一阶段计划：阶段 03B —— 拳皇式指令识别

**进入前必须先读** `ARCHITECTURE.md` 第 4 节（D26–D36 输入规则）与第 5 节。

阶段 03A 已把「输入逐帧绑定到逻辑帧」做扎实。阶段 03B **只做指令识别**，
仍然只处理输入与整数逻辑帧：

1. **方向指令**：↓↘→、→↓↘、←↙↓↘→ 等的窗口与序列匹配（基于 `InputBuffer.History` 的逐帧电平）。
2. **蓄力指令**：←（保持 N 帧）→ 的计数窗口。
3. **输入缓冲**：预输入窗口、取消窗口（只在输入层排队，不涉及招式/状态机）。
4. **换边识别**：`Facing` 变化时如何重置方向序列（依赖 `DirectionResolver` 的绝对/相对分层）。
5. **指令优先级**：同一帧命中多条指令时的裁定顺序。
6. **测试**：指令识别必须是纯函数 + 逐帧日志输入驱动，保持确定性可重放。

**明确不需要（本轮任务已确认）**：
- 不需要提前开发运动系统；
- 不需要为 03B 修改 `FighterState`；
- 不需要先确定运动定点数格式（那是运动阶段的前置，不是指令识别的前置）。
- 不实现攻击、碰撞、伤害、状态机。

**注意**：若 03B 需要「后输入优先」SOCD，必须先扩展 `FrameInput` 记录事件顺序。

> 阶段 03A 已结项。接手后先看 `AGENT_HANDOFF.md` 第 6 节，运行 `dotnet test`
> 确认 **147** 项通过再继续。

**每完成一个子任务，立即更新本文件与 `AGENT_HANDOFF.md`。**
