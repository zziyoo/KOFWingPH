# PROGRESS.md — NewKOF 开发进度

> 最后更新：2026-11-07（阶段 04 最终补修：快照不可变性 / 非法状态校验）

---

## 当前开发阶段

**阶段 01：Godot 工程初始化 — ✅ 已完成**
**阶段 02：固定逻辑帧与模拟调度验收 — ✅ 已完成**
**阶段 02 补修：战斗状态与调度器帧同步 — ✅ 已完成（已提交 `850c4db`）**
**阶段 03A：基础输入链路 — ✅ 已完成（已提交 `0be9919` 并推送）**
**阶段 03A 专项补修：输入一致性与调试工具 — ✅ 已完成（已提交 `f145275` 并推送）**
**阶段 03A 最终补修：输入与战斗状态原子提交 — ✅ 已完成（已提交 `0bd0c6d` 并推送）**
**阶段 03A — ✅ 正式结项**
**阶段 03B-1：拳皇式基础指令识别 — ✅ 已完成**
**阶段 03B-1 补修：指令竞争 / 校验 / 连续性 / 朝向时序 — ✅ 已完成并结项**
**阶段 03B-1 最终补修：跨末方向帧幽灵指令 / 观察者失败封闭 — ✅ 已完成（已提交 `33444f0`）**
**阶段 03B-2：蓄力与扩展指令 — ✅ 已完成**

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

**最近一次实际运行结果（2026-11-07，阶段 04 最终补修后实测）**
```
已通过! - 失败: 0，通过: 343，已跳过: 0，总计: 343，持续时间: 854 ms - BattleCore.Tests.dll (net8.0)
```

| 测试文件 | 用例数（`--list-tests` 实测） | 内容 |
|---|---|---|
| `BattleSimulationTests.cs` | 8 | 阶段 01 基础 Step 契约（反修改断言按 D55 语义更新：识别快照随权威状态推进） |
| `FixedStepSchedulerTests.cs` | 24 | 调度器：多帧率一致性（Theory 展开）、补帧、暂停、非法输入 |
| `DeterminismTests.cs` | 16 | 确定性、长跑、快照隔离、旧状态重放 |
| `GodotIntegrationContractTests.cs` | 6 | Godot 层依赖的内核契约 |
| `FrameSyncRegressionTests.cs` | 12 | **补修**：问题 1/2 回归 |
| `SchedulerAccountingTests.cs` | 13 | **补修**：丢弃语义、超大 delta、不变量、**记账 API 权限** |
| `SchedulerNumericalRobustnessTests.cs` | 14 | **补修**：NaN/溢出/精度健壮性 |
| `InputBufferTests.cs` | 13 | **03A**：电平/边沿/双人独立/历史连续性 |
| `DirectionResolverTests.cs` | 10 | **03A**：SOCD 与相对朝向 |
| `InputPipelineTests.cs` | 7 | **03A**：输入与逻辑帧绑定、暂停、多帧批次 |
| `InputIntegrityTests.cs` | 34 | **03A 补修 + 03B-1 最终补修**：只读历史、两阶段提交、失败恢复、容量、Commit 失败、观察者异常安全、**失败封闭（Faulted）** |
| `CommandRecognitionTests.cs` | 41 | **03B-1 + 最终补修**：四条基础指令、容差/超时、冲突、换边、SOCD、帧率一致性、缓冲、**跨末方向帧幽灵指令**（空序列校验按 D56 细化：空 + 无按键仍拒绝） |
| `ChargeCommandTests.cs` | 44 | **03B-2 + 最终补修**：蓄力成功/失败/超时、**超长蓄力 61/120/600/3600**、**释放窗口 0/1/2/3/4/20 帧边界**、斜方向、SOCD（不蓄力/视为释放）、换边、不同键、P1/P2 独立、**防重复消费/独立重蓄**、负帧校验、内存上界 |
| `ChargeStaleTailTests.cs` | 10 | **03B 边界补修（D54）**：释放前旧 Forward 不得借用（最小复现）、不足不补、迟按不复活、同帧完成、窗口 1/2/3/4 边界（含旧条目历史）、P1/P2 独立、换边新朝向尾段归属 |
| `FighterDefinitionTests.cs` | 19 | **04（D59/D56）**：招式/角色配置校验（重复 ID、未知指令、非法帧数、空 ID）、防御复制、同指令不同招式、裸按键校验（带键合法/无键拒绝/蓄力拒绝） |
| `FighterStateMachineTests.cs` | 13 | **04（D58）**：默认站立、蹲下切换（含 SOCD 上优先）、阶段推进与 1 帧边界、动作期间不产生非法跳转、损坏状态（无 MoveId/未知招式）抛出、待机消费进 Startup、动作期间不消费、收招同帧消费 |
| `MoveConsumptionTests.cs` | 15 | **04（D60/D56）**：一次消费/第二次失败、过期不可消费、保留到有效期、无映射跳过不阻断、FIFO 选择顺序、同指令不同招式（Kai/Rei）、朝左识别、蹲姿门控、裸按键（长待机触发/按住不重复/让位高优先级）、Last≠可发动、纯函数不改入参、Step 分批一致 |
| `Phase04IntegrationTests.cs` | 12 | **04（B6/B8）**：完整招式时间线逐帧断言（4/3/8）、独立二次发动、动作期间重复输入、蹲下不覆盖动作、P1/P2 独立角色表、确定性、旧快照重放、批次一致、Advance/StepExact + 暂停 + 帧同步、观察者故障封闭、快照含动作状态 |
| `SnapshotImmutabilityTests.cs` | 6 | **04 补修（D61）**：五集合不可被强转 List/数组/`IList` 修改（含只读接口抛错与内容保持）、Empty 共享安全、WithResultConsumed 新旧互不污染、旧快照修改尝试后重放仍确定、1000 帧双人隔离与裁剪有界、相等状态哈希一致且稳定 |
| `FighterStateValidationTests.cs` | 11 | **04 补修（D62）**：`(999)/(-1)/(5)/(100)` 拒绝并含具体值、非法枚举 + MoveId 字段仍拒绝、BattleState 构造期拒绝（第一道门）、Tick 直接拒绝未知状态且不被静默修正（第二道门）、合法五态通过校验并正常推进、负帧数保持拒绝 |
| `DoubleMotionCommandTests.cs` | 11 | **03B-2 + 最终补修**：双重指令成功/缺段/超时、**真正 40/41 帧跨度超时边界（连续方向序列）**、与单段竞争、新手势识别、无按键不触发 |
| **合计** | **343** | 与 `dotnet test` 的「总计: 343」一致 |

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

## 阶段 03B-1 成果（拳皇式基础指令识别，2026-11-07）

### 边界

只做「方向输入序列 → 指令 ID」的识别，**不做**招式发动、取消、硬直、伤害、移动/跳跃，
也不实现蓄力指令（留给 03B-2）。识别全部发生在 `BattleCore`，Godot 只显示结果。

### 新增模块

| 模块 | 位置 | 职责 | 明确不做 |
|---|---|---|---|
| `CommandDirection` | `core/BattleCore/Commands/CommandDirection.cs` | 相对朝向九宫格方向（中立/前后/上下/四个斜向） | 绝对方向录制 |
| `CommandDefinition` | `core/BattleCore/Commands/CommandDefinition.cs` | 指令数据：ID、方向序列、按键、跨度上限、按键容差、优先级、缓冲时效 | 识别流程 |
| `CommandMatch` | `core/BattleCore/Commands/CommandMatch.cs` | 识别结果：ID、完成帧、按键帧、优先级、逐元素方向帧 | 招式语义 |
| `CommandRecognizer` | `core/BattleCore/Commands/CommandRecognizer.cs` | 逐帧压缩方向变化、边沿匹配、冲突裁决、结果缓冲 | 招式发动/取消/硬直 |
| `CommandCatalog` | `core/BattleCore/Commands/CommandCatalog.cs` | 默认四条指令（236A / 623A / 214B / 66） | 角色专属招式表 |

### 识别规则（已确定）

1. **方向按变化压缩**：同一方向按住 N 帧只产生一个历史条目。
   「↓ 按住 10 帧再 →」不会匹配 ↓↘→——必须真的经过 ↘。
2. **每帧朝向随样本保存**：每个方向条目在**当帧朝向**下经 `DirectionResolver`
   换算为相对方向。换边只影响之后的条目，**不会**用当前朝向重算全部历史
   （换边用例：`SideSwitchDuringInput_KeepsHistoricalFacingPerFrame`）。
3. **序列连续**：方向序列必须在压缩历史中连续、顺序一致（不跳过中间方向）。
4. **末方向允许稍早完成**：末尾条目距当前帧 ≤ `ButtonToleranceFrames` 时，
   仍可被本帧按键补完。这是 236 与 623 能在同一帧竞争的原因。
5. **超时**：首尾跨度 > `MaxSequenceFrames`（默认 20 帧）即失败。
6. **按键边沿**：只认 0→1 边沿；按住不重复触发；一次边沿最多完成一条指令（消费后失效）。
7. **纯方向指令（66）**：只在末方向**进入的那一帧**触发，按住不会重复。
8. **冲突裁决（与遍历/定义顺序无关）**：优先级降序 → 序列长度降序 → 指令 ID 序数升序。
9. **结果缓冲**：`ExpiresAtFrame = RecognizedFrame + BufferLifetimeFrames`（默认 8 帧）；
   过期结果在下一帧 `Observe` 时移除，`LastMatch` 保留供 HUD 显示。

### 默认指令

| ID | 序列（相对朝向） | 按键 | 优先级 | 跨度上限 | 按键容差 |
|---|---|---|---|---|---|
| `236A` | ↓ → ↘ → → | A（轻拳） | 10 | 20 | 3 |
| `623A` | → → ↓ → ↘ | A（轻拳） | 20 | 20 | 3 |
| `214B` | ↓ → ↙ → ← | B（重拳） | 10 | 20 | 3 |
| `66` | → → 中立 → → | 无 | 0 | 18 | 0 |

### 测试与变异验证

`tests/BattleCore.Tests/CommandRecognitionTests.cs` —— **26 项**，覆盖任务书全部 13 类：
四条指令正确识别、错误顺序、超时、按键提前/延后（含 3 帧边界）、方向各保持 10 帧、
同一次按键不重复触发（容差窗口内）、236/623 竞争、同优先级稳定（含定义顺序反转）、
P1/P2 独立、面向左、输入中换边、SOCD 左右抵消/上优先、
30 vs 144 FPS 下同一逻辑输入日志结果一致、缓冲过期。

**变异验证**（证明断言不是摆设）：

| 变异 | 实际失败项 |
|---|---|
| 不消费按键边沿 | 1 项（`SameButtonPress_DoesNotRetriggerWhileHeldInsideToleranceWindow`） |
| 冲突改为「长度优先于优先级」 | 1 项（`HigherPriorityShortCommand_BeatsLongerLowerPriorityCommand`） |
| 跨度上限 `>` 改成 `>=` | 1 项（`EachDirectionHeldTenFrames_StillMatchesWithinMaxSpan`） |
| 忽略传入朝向、恒按朝右解析 | 4 项（换边、面向左、双人独立、帧率一致性） |

### 构建与运行

| 项 | 结果 |
|---|---|
| `dotnet build core/BattleCore` | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj` | 0 警告 / 0 错误 |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | HUD 显示 `P1 Command: 236A` / `P1 Command: 66`；见 `shots/command_236a.png`、`shots/command_66.png` |

**Godot 实测日志（236A）**：

```
[CaptureHelper] 渲染帧 18 注入 S 按下（当时逻辑帧 17）
[CaptureHelper] 渲染帧 22 注入 D 按下（当时逻辑帧 19）
[CaptureHelper] 渲染帧 26 注入 S 松开（当时逻辑帧 20）
[CaptureHelper] 渲染帧 27 注入 J 按下（当时逻辑帧 21）
HUD：P1 Command: 236A / P2 Command: -
```

**66 实测注意**：第一次注入时「松开 + 再按」落在同一逻辑帧之间，被 `InputBuffer`
合并成一个电平帧（D31），逻辑输入日志中没有中立帧，因此未触发；
把两次点击拉宽到 8 渲染帧后正常识别 `66`。这不是识别缺陷，是电平输入格式的已知合并语义。

### 与既有契约的关系

- `BattleSession` 新增**可选** `frameCommitted(input, state)` 回调：每帧完整提交后调用一次，
  不改变调度、输入提交顺序与记账；只供只读派生层（指令识别）使用，不得抛异常。
- Godot 层只把识别结果映射为 HUD 文本，不做任何判定。

---

## 阶段 03B-1 补修（2026-11-07）—— 指令竞争 / 校验 / 连续性 / 朝向时序

### P1：幽灵指令（根因与修复）

**复现**：自定义 `236A`（优先级 20）与 `236B`（优先级 10）共享同一方向序列 ↓↘→。
输入 `↓`、`↘+B`、`→+A`、`→`：帧 2 上两者同时成立（B 在帧 1 仍处于 3 帧容差内），
`236A` 胜出并消费 A 边沿；但 B 边沿未消费，帧 3 方向仍是 `→`（末方向帧号不变），
`236B` 借「帧 1 的 B + 帧 2 的末方向」再次匹配 → **幽灵触发**（复现测试 2 项失败）。

**修复（D43）**：胜出指令把其方向序列的<b>末方向帧号</b>加入 `ConsumedEndFrames`；
`TryMatch` 跳过已被消费的手势。末方向帧号与方向条目一一对应且唯一，因此同一手势被
唯一锁定；两个独立手势的末方向帧号不同，互不影响。既不是「清空按键历史」，也不吞掉
后续独立输入。

**区分规则**：同一手势（方向序列末方向帧号相同）的冲突落败 → 抑制；两个独立手势
（重新走一遍方向、末方向帧号不同）→ 各自正常识别。

### P2：校验与帧连续性

- `CommandDefinition` 构造期新增：相邻方向不得相同（`ArgumentException`）、
  元素必须是合法 `CommandDirection`（`ArgumentOutOfRangeException`）；
  方向序列 `ToArray()` 复制，外部改源数组不影响定义（D45）。
- `Observe` 帧号契约改为严格逐帧连续（D44）：首次允许任意非负初始帧（快照恢复），
  此后 `input.Frame == lastObservedFrame + 1`，重复/回退/跳过/负帧都在修改内部状态前拒绝。

### P4：换边朝向时序

- `frameCommitted` 观察者改为传入<b>帧开始状态</b>（`stateBefore`），其 `Facing` 是
  当帧输入被消费时的朝向（D46）。`Advance` 与 `StepExact` 一致。
- 当前 `BattleSimulation` 不改朝向，故帧开始 == 帧结束；但契约已显式固定，
  未来引入帧内转身时不会用 `nextState.Facing` 误解析本帧输入。

### P5：观察者异常安全（核查结论：安全，已补回归测试）

- 观察者抛异常时：该帧**已经**记账（`CommitStep` + `committed++` 在观察者之前），
  状态已推进，输入历史与状态对齐；异常照常向上传播。
- `Advance` 路径：`catch` 把本批次未提交剩余预算记入 `DroppedStepCount`，不变量
  `Frame - 初始帧 == TotalSteps` 保持。
- 恢复后继续推进：下一帧从正确的连续帧号开始，识别器不漏帧、不跳变。

### 补修测试与变异验证

新增 18 项（累计 173 → **191**）：幽灵指令复现 + 独立手势、定义顺序反转、
236/623 竞争不退化、相邻方向校验、非法枚举值、源数组复制、空序列、负帧、
重复/跳过/回退/非零初始帧、帧开始朝向对齐、跨帧换边、观察者异常三连。

| 变异 | 实际失败项 |
|---|---|
| 移除 `ConsumedEndFrames.Add` | 2 项（`LosingCandidateEdge_DoesNotGhostTriggerOnNextFrame`、`LosingCandidateSuppression_IsIndependentOfDefinitionOrder`） |

### 补修后构建与运行

| 项 | 结果 |
|---|---|
| `dotnet build core/BattleCore` | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj` | 0 警告 / 0 错误 |
| 全量测试 | **191/191 通过** |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | HUD `P1 Command: 236A`（`shots/fix_236a.png`，逻辑帧 26） |

---

## 阶段 03B-1 最终补修（2026-11-07）—— 跨末方向帧幽灵指令 / 观察者失败封闭

### P1：跨末方向帧幽灵指令

**复现（修复前实测）**：`ConsumedEndFrames` 按「末方向帧号」锁定手势，只能抑制「共享同一
末方向帧」的竞争（236A vs 236B）。任务书指出：当两条指令**末方向帧不同、却参与同一次裁决**
时，末方向帧号无法覆盖落败候选。

自定义 `623A`（→↓↘，末方向 ↘，优先级 20）与 `236B`（↓↘→，末方向 →，优先级 10）。
输入 `→(0-1) ↓(2-3) ↘(4) ↘+B(5) →+A(6) →(7)`：

- 帧 6：623A（末方向 ↘@4）与 236B（末方向 →@6）同时成立，623A 按优先级胜出。
- 帧 7：方向仍是 →，236B 借「帧 6 末方向 → + 帧 5 残留 B 边沿」**幽灵触发**。

实测 4 项复现测试失败，集合含 2 项（623A + 幽灵 236B，`DirectionFrames=[2,4,6]`）。

**修复（D47）**：同帧冲突裁决**收集全部合法候选**，胜者与落败候选的**按键边沿一次性全部消费**
（此前只消费胜者边沿）。这抑制了幽灵指令，又不消费落败候选的**方向手势**（保留给独立手势），
且与定义顺序无关。变异验证：只消费胜者边沿 → 4 项失败。

### P2：观察者异常后的失败封闭（Faulted）

战斗帧提交（状态/输入/记账落定）并不自动保证派生的指令识别状态同步——观察者
（`frameCommitted`）抛异常时，识别器可能未完成该帧的 `Observe`。

**修复（D48）**：
- `BattleSession` 新增 `IsFaulted` / `FaultedFrame`；观察者调用处单独 `try/catch`，
  捕获后 `MarkFaulted(input.Frame)` 并照常 `throw`（不吞异常）。
- `Advance` / `StepExact` 开头 `ThrowIfFaulted`：未恢复前拒绝继续模拟。
- 只有观察者异常触发 Faulted；`Step`/`Commit` 的失败不误标。
- 调用方恢复派生状态一致性后 `Recover()` 清障，恢复后从正确的下一帧继续。

### 测试与变异验证

新增 11 项（累计 191 → **202**）：跨末方向帧幽灵指令复现 + 定义顺序反转 + 独立手势 +
不同按键（4 项）；观察者首次/中途抛异常、状态记账同步、未恢复拒绝（Advance/StepExact 两路）、
多帧预算丢弃、Recover 后不漏帧、Step/Commit 失败不误标 Faulted、正常观察者不故障（7 项）。
更新 1 项（`...ThenRecoverContinuesWithCorrectFrame` 反映新 Recover 契约）。

| 变异 | 实际失败项 |
|---|---|
| 只消费胜者边沿（不消费落败候选边沿） | 4 项（跨末方向帧幽灵指令四连） |

### 最终补修后构建与运行

| 项 | 结果 |
|---|---|
| `dotnet build core/BattleCore` | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj` | 0 警告 / 0 错误 |
| 全量测试 | **202/202 通过** |
| Godot 无头导入 | `[ DONE ]` |

---

## 尚未实现功能（范围外，非缺陷）

- 角色移动 / 跳跃 / 攻击 / 受击
- 状态机、招式数据、帧数据
- 碰撞检测（hitbox / hurtbox / pushbox）
- 伤害、硬直、combo、取消规则、Hitstop、KO
- 动画、精灵图、音效、特效
- 回合 / 计时
- 手柄输入映射（键盘基础输入已在阶段 03A 接入；手柄、键位重绑定 UI 未做）
- ~~蓄力指令、超必杀组合~~（**已在 03B-2 接入识别**）；指令触发的招式发动 / 取消 / 硬直（仍未做）
- 快照序列化 / 回滚 / 重放
- 联机、AI、训练模式

---

## 阶段 03B-2 成果（蓄力与扩展指令，2026-11-07）

### 边界

只做「方向/蓄力/多段输入序列 → 指令 ID」的识别，**不做**招式发动、取消、硬直、移动、动画。

### 新增 / 修改

| 模块 | 变更 |
|---|---|
| `CommandDefinition` | 新增 `ChargeHoldFrames`（序列第一个元素蓄力保持帧数，默认 0 = 普通指令） |
| `CommandRecognizer` | `DirectionEntry` 增加 `EndFrame`（持续结束帧）；蓄力保持检查（D49） |
| `CommandCatalog` | 新增 6 条默认指令：蓄力 46A/28B/46C、双重 236236A/214214B/623623A |

### 蓄力规则（D49）

- 蓄力 = 方向序列第一个元素连续保持 ≥ `ChargeHoldFrames` 帧（默认 30 帧 = 0.5s @60Hz）。
- `DirectionEntry.EndFrame` 记录该方向持续到的最后一帧，蓄力时长 = `EndFrame - Frame + 1`。
- **蓄力只认纯方向**：斜方向是独立条目，会打断「纯方向」的连续保持，因此不计入蓄力。
- 换边按「当帧朝向」解析（D46 已定）：换边前后相对方向一致则累计，不一致则失效。
- SOCD 影响：左右同时 → 水平中立（不产生「后」），上下同时 → 上优先（不产生「下」）。
- 按住攻击键不重复发动（边沿一次性消费）。

### 双重 / 超必杀规则（D50）

- 双重指令用更长方向序列表达（236236A = 6 元素），无 per-move 识别分支。
- `MaxSequenceFrames` 放宽到 40 帧容纳两段；第二段超时（首尾跨度超限）即失败。
- 与单段竞争：优先级（双重 15/25 > 单段 10/20）+ 序列长度双键裁决，复用 D47 冲突规则。
- 手势消费（D43/D47）保证长指令完成不借同一历史重复触发短指令。

### 默认指令扩展

| ID | 序列 | 按键 | 蓄力 | 优先级 | 跨度 |
|---|---|---|---|---|---|
| `46A` | 后 → 前 | A | 30 帧 | 30 | 60 |
| `28B` | 下 → 上 | B | 30 帧 | 30 | 60 |
| `46C` | 后 → 前 | C | 30 帧 | 30 | 60 |
| `236236A` | ↓↘→↓↘→ | A | — | 15 | 40 |
| `214214B` | ↓↙←↓↙← | B | — | 15 | 40 |
| `623623A` | →↓↘→↓↘ | A | — | 25 | 40 |

### 测试与变异验证

新增 25 项（累计 202 → **227**）：蓄力 16 项（成功/失败/超过/超时/斜方向/SOCD/换边/不同键/
P1P2 独立/按住不重复/负帧校验/内存上界）、双重 9 项（三种成功/缺段/顺序错误/超时/无按键/
双重 vs 单段竞争/新手势识别）。

| 变异 | 实际失败项 |
|---|---|
| 移除蓄力保持检查 | 1 项（`ChargeBackForward_HoldNMinusOneFrames_DoesNotMatch`） |

### 构建与运行（03B-2 实测）

| 项 | 结果 |
|---|---|
| `dotnet build core/BattleCore` | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj` | 0 警告 / 0 错误 |
| 全量测试 | **227/227 通过** |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行（蓄力） | HUD `P1 Command: 46A`（`shots/charge_46a.png`） |
| Godot 图形运行（双重） | HUD `P1 Command: 236236A`（`shots/double_236236a.png`） |

### 遗留限制（非阻塞）

- 蓄力「斜方向计入」默认关闭（严格纯方向）；若需放宽需加显式配置。
- ~~蓄力转向窗口复用 `ButtonToleranceFrames`，未做独立的「转向窗口」配置~~
  （**已在最终补修解决**：独立的释放窗口 `ChargeReleaseToleranceFrames`，D52）。
- 指令表仍是全局默认表，角色专属拆分待后续阶段。

---

## 阶段 03B-2 最终补修（2026-11-07）—— 长蓄力失效 / 释放窗口 / 双重超时边界 —— 结项

### P1：蓄力保持过久反而失效（根因与修复）

**根因**：旧实现用「方向序列首尾跨度 ≤ `MaxSequenceFrames`」判断超时，蓄力指令
（`[4]6A` 保持 30 帧、跨度上限 60）保持超过 60 帧后 `endFrame - startFrame > 60`
直接跳过匹配；同时 `Prune()` 按 `frame - maxSeq - maxTol` 裁剪方向条目，
600 帧蓄力时起始的「后」条目早已被裁掉，历史里只剩 `前`，连匹配的前提都不存在。

**修复前失败复现（实测 256 项中 13 项失败）**：

| 复现组 | 失败项 |
|---|---|
| 长蓄力（P1） | `HoldBoundaryAndLongHold` holdFrames 61/120/600/3600、`ChargeDownUp_LongHold_StillMatches`（600）——共 5 项 |
| 释放窗口（P2） | 46A 中立 1/2/3 帧、28B 中立 1/3 帧、SOCD 中立释放、P1/P2 独立（带中立）——共 7 项 |
| 测试缺陷 | `AfterIndependentRecharge_MatchesAgain` 期望缓冲里 2 条结果，忽略 8 帧缓冲有效期（第一条在帧 19 过期出队）——1 项，属测试错误，已按缓冲语义修正断言 |

**修复（D51）**：蓄力与方向历史**解耦**。识别器为每条蓄力指令维护一个固定的
「释放挂起」槽位（`ChargePendingState`）：离开蓄力方向的瞬间从历史条目快照
蓄力量与释放帧，此后匹配只依赖挂起状态 + 尾段历史——

- 蓄力可保持任意久（时长只受 `int` 帧号范围限制，3600 帧实测通过）；
- 内存不随蓄力时长增长（每指令一个槽位，3 个 int）；
- 普通方向历史容量上界不变（`Prune` 规则原样保留）；
- 有效蓄力不会被 `Prune()` 提前清除（信息已在快照里）；
- `MaxSequenceFrames` 对蓄力指令不再生效（超时语义分离，见 D51），普通指令不变；
- `Advance()` / `StepExact()` 走同一 `Observe` 路径，结果一致；
- 暂停/被丢弃的逻辑时间不产生提交帧 → 不会调 `Observe` → 不虚增蓄力帧数。

### P2：蓄力释放窗口（D52）

`CommandDefinition` 新增 `ChargeReleaseToleranceFrames`（默认
`CommandCatalog.DefaultChargeReleaseToleranceFrames = 3` 帧 = 50ms @60Hz）。
离开蓄力方向后：

- **容忍**：中立（含 SOCD 左右抵消形成的中立）与序列尾部方向；
- **立即失效**：任何无关方向（释放瞬间直接进入无关方向同样立即失效）；
- **完成条件**：序列末方向进入帧 − 释放开始帧 ≤ 释放窗口，且按键边沿在
  `ButtonToleranceFrames` 内（按键容差不延长释放窗口——方向超出窗口即死）；
- **不足不补**：蓄力量 < `ChargeHoldFrames` 时窗口再宽也不匹配；
- **重新计数**：窗口内重新按回蓄力方向 → 挂起作废、从 0 重新计数；
- **兼容**：普通 236A/623A/214B/66 与双重指令保持严格连续规则，不忽略中立；
  旧蓄力配置不传新参数时获得默认 3 帧窗口。

边界实测（Theory）：中立 0/1/2/3 帧成功、4/20 帧失败（46A 与 28B 均验证）。

### 额外核查（任务书第五节）

1. **防重复触发**：蓄力指令产生过候选后挂起立即作废（D53）——同一蓄力不能借
   第二次按键边沿在释放窗口内反复触发；重新独立蓄满后才可再次识别（各有测试）。
2. **与普通指令竞争**：同帧多候选仍走 D40/D47 裁决（候选边沿一次性全消费，
   落败者无幽灵、不吞后续独立输入）；蓄力尾段外的无关方向会先取消挂起，
   天然避免蓄力与普通指令的跨手势误拼。
3. **长时间运行**：`ChargeInput_LongRun_DoesNotGrowUnbounded`（5000 帧抖动 +
   重蓄识别）+ 3600 帧长蓄力 + 历史裁剪/消费标记裁剪规则未变，无增长、无复活。

### 测试与变异验证

新增 30 项（227 → **257**）：蓄力 +28（长蓄力 8 档边界、释放窗口 9 档边界、
SOCD 释放、重蓄重置、防重复、P1/P2 独立、换边、按键容差不延长窗口等）、
双重 +2（真正的 40/41 帧跨度边界——方向完整连续、仅首尾超限）。
另修正 1 项测试缺陷（缓冲有效期语义）。

| 变异 | 实际失败项 |
|---|---|
| 移除普通指令跨度超时判断 | 1 项（`DoubleQuarterCircle_OneFrameOverMaxSpan_DoesNotMatch`，41 帧边界） |
| 移除蓄力释放窗口判断 | 5 项（`ReleaseLongThenPress`、中立 4/20 帧 ×2、`ButtonToleranceDoesNotExtendReleaseWindow`、20 帧边界） |

两组变异均已恢复正确实现并复测全绿。旧测试
`DoubleQuarterCircle_SecondSegmentTimeout_DoesNotMatch` 因在两段间插入大量中立
（破坏连续性）无法证明超时机制，仅作连续性语义保留；真正的超时边界由新测试承担。

### 构建与运行（最终补修实测）

| 项 | 结果 |
|---|---|
| `dotnet build core/BattleCore` | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj` | 0 警告 / 0 错误 |
| 全量测试 | **257/257 通过** |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行（长蓄力） | 后保持 80 帧（> 旧上限 60）+ 松开 2 帧 → HUD `P1 Command: 46A`（`shots/charge_long.png`） |
| Godot 图形运行（释放窗口内） | 蓄力 40 帧 + 松开 2 帧 → HUD `P1 Command: 46A`（`shots/charge_release_ok.png`） |
| Godot 图形运行（释放窗口过期） | 蓄力 40 帧 + 松开 6 帧 → HUD `P1 Command: -`（`shots/charge_release_expired.png`） |
| Godot 图形运行（双重回归） | HUD `P1 Command: 236236A`（`shots/command_236236a.png`） |

> 验收脚本使用 `CaptureHelper` 新增的 `--maxfps=` 参数锁定 60 FPS，
> 使渲染帧 ≈ 逻辑帧（固定启动偏移），注入时序可复现（正常启动零副作用）。

**结论：阶段 03B-2 正式结项。**

---

## 03B 蓄力边界最终补修（2026-11-07）—— 释放后旧方向条目借用（D54）

### 缺陷与复现

释放窗口检查只有上界（`endFrame − ReleasedAtFrame > 窗口 → 拒绝`），没有下界。
复现场景（蓄力 1 帧 / 窗口 3 / 按键容差 3）：前(0) → 后(1) → 中立(2，释放) → 中立+A(3)
——旧 Forward(0) 满足 `0 − 2 = −2 ≤ 窗口`，按键(3) 与旧条目恰差 3 帧（容差内），
借**释放前的旧条目**幽灵完成 46A（即使本次释放后从未重新输入前）。

**修复前实测**：新增 10 项中 **2 项失败**（`StaleForwardBeforeCharge_CannotBeBorrowedAfterRelease`
最小复现 + `StaleForward_P1Rejected_P2CleanChargeStillMatches`）；其余 8 项
（不足不补、迟按、同帧完成、窗口 1/2/3/4 边界、换边归属）修复前即通过。

### 修复（D54）

`TryMatch` 蓄力分支补一行下界约束：`endFrame < pending.ReleasedAtFrame → continue`——
尾段（序列末方向）必须属于**本次释放之后**的新手势；按键容差只约束按键相对末方向的
时机，不能复活失效蓄力。普通/双重指令不受影响，3600 帧长蓄力与释放窗口边界全部保持。

**修复后**：267/267 通过（257 + 10，新增 `tests/BattleCore.Tests/ChargeStaleTailTests.cs`）。

---

## 阶段 04 成果（角色状态机与招式系统，2026-11-07）

### 边界

解决「角色当前处于什么状态、状态能否切换、招式指令如何请求执行、动作如何按逻辑帧推进」。
**不做**：移动/跳跃轨迹、碰撞推挤、攻击判定、伤害受击、动画、取消技、回合胜负
（Airborne/Hitstun 等状态只在文档预留，不以空实现冒充）。

### 核心架构（D55–D60）

1. **识别进入权威状态（D55）**：识别逻辑提取为不可变的 `RecognitionState`
   （方向历史 / 按键边沿 / 蓄力挂起 / 结果缓冲 / 消费标记，全部随快照携带），
   `Advance` 为纯函数——同初态同输入必得同结果，旧快照 + 后续输入可完整重放。
   `BattleSimulation.Step` 内按角色推进识别 → 消费 → 状态机（**零帧延迟**，同帧生效）；
   `CommandRecognizer` 保留为双玩家门面（267 项既有测试全部不动地通过）。
   会话观察者与 Faulted 封闭不变（仅供显示）。
2. **动作状态机（D58）**：`FighterLogicState` = Standing / Crouching / Startup / Active / Recovery；
   集中纯函数 `FighterStateMachine.Tick`：阶段剩余 −1 → 归零按招式数据切换
   （Recovery 完成同帧可待机更新与消费）→ 仅待机时切换站立/蹲下（B9，动作不被蹲下覆盖）
   → 消费（D60）。转换边唯一，损坏状态立即抛出。
3. **数据驱动招式（D59）**：`MoveDefinition`（MoveId / CommandId / 三阶段帧数 /
   允许发动状态）+ `FighterDefinition`（角色专属指令表与招式表）+ `FightRoster`；
   构造期校验（重复 ID、未知指令、非法帧数）+ 防御复制。识别器不认识角色与招式。
4. **消费契约（D60）**：每条识别结果最多消费一次（`WithResultConsumed` 显式失败）；
   过期不能消费；不属于当前角色的指令不误消费；失败不破坏其他结果；
   不能行动时保留到有效期结束、恢复可行动同帧可消费；选择顺序 = 缓冲顺序（FIFO）；
   `LastMatch` 仅显示，绝不据其发动。
5. **裸按键指令（D56）**：空方向序列 + 攻击键——实测发现 `[Neutral]` 序列无法表达
   「站立不动按 A」（中立条目进入帧太旧），补充裸按键形态：仅当帧未消费边沿触发，
   不依赖方向历史。空序列 + 无按键仍拒绝。

### `FighterState` 扩展（D57）

新增 `LogicState` / `MoveId` / `PhaseRemainingFrames` / `Recognition`（识别快照）。
不变式：动作期间必须带 MoveId 且剩余 ≥ 1；待机必须无 MoveId 且剩余 0。
`StateVersion` 1 → 2（格式边界标记；项目无序列化快照，无迁移负担）。
仍不引入速度/重力/移动规则。

### 演示配置（测试与 Godot 共用）

| 角色 | 指令 → 招式 | 阶段帧数 |
|---|---|---|
| Kai | 236A → FireballPrototype | 4 / 3 / 8 |
| Kai | 623A → DragonPunchPrototype | 3 / 5 / 10 |
| Kai | 5A（裸按键）→ StandLightPunch | 3 / 2 / 6（仅站立） |
| Rei | 236A → BackDraft（**同指令不同招式**） | 5 / 2 / 7 |
| Rei | 214B → SweepPrototype | 3 / 4 / 9 |

### 测试与构建（阶段 04 实测）

新增 59 项（267 → **326**）：配置校验 19、状态机 13、消费契约 15、集成 12。
既有 267 项全部保持通过；唯一断言更新是
`BattleSimulationTests.Step_DoesNotMutateInputState` 的结果侧角色相等——
按 D55 语义识别快照随权威状态合法推进，反修改契约改为断言输入状态本体不可变。

| 项 | 结果 |
|---|---|
| `dotnet build core/BattleCore` | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj` | 0 警告 / 0 错误 |
| 全量测试 | **326/326 通过** |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | 双方默认 `Standing`（`shots/p04_standing.png`）；P1 按住下 → `Crouching` 且 P2 独立 `Standing`（`shots/p04_crouch.png`）；5A → `StandLightPunch / Startup 2`（`shots/p04_5a_startup.png`）→ `Recovery 6`（`shots/p04_5a_recovery.png`）→ 按住 A 全程只发动一次、回到 `Standing`（`shots/p04_5a_done.png`）；236A → `FireballPrototype / Startup 3` 且 `P1 Command: 236A`（`shots/p04_236a_startup.png`） |

HUD 九行只读权威状态（Logic Frame / 双人输入 / 双人 Command / 双人 State / 双人 Move），
表现层不再持有识别器、不做任何状态机或计时。

### 遗留限制（非阻塞）

- 序列指令的「方向新进入」约束仍然生效：按住方向较久后按攻击键，不会触发以该方向
  结尾的序列指令（如长按 ↓ 再按 A 不触发「↓+A」型指令）——裸按键指令（D56）不受影响；
  若需「按住方向 + 按键」语义，应扩展容差锚点（记录为后续可选项）。
- 动作只有阶段时间轴：无攻击判定、无伤害、无动画、无取消（按边界留给后续阶段）。
- 快照/重放为内存语义；序列化格式与 `StateVersion` 升级机制仍未设计（K8）。

---

## 阶段 04 最终补修（2026-11-07）—— 快照深层不可变性 / 非法状态校验（D61/D62）

### P1：识别快照集合可被外部修改（D61）

**根因**：`RecognitionState.Advance` 把工作 `List<T>` 直接存进 `IReadOnlyList<T>` 属性
（接口仅限制编译期视图）；`WithResultConsumed` 暴露裸 `BufferedResult[]`；
`Empty` 共享 `Array.Empty`。识别状态随 `FighterState` 进入 `BattleState`，
外部强转即可篡改权威快照。

**修复前失败复现（实测 17 项新测试中 5 项失败）**：
`PublicCollections_AreNotExternallyMutable`（Directions 可强转 `List<DirectionEntry>`
并被 `Clear()`）、`EmptyState_IsSharedSafely`、`WithResultConsumed_OldAndNewStates_BothImmutable`、
`OldSnapshot_MutationAttempts_DoNotAffectReplay`、`LongRun_1000Frames_PlayersIsolated_NoPollution`。

**修复（D61）**：五个公开集合（Directions/Presses/Buffer/ConsumedEndFrames/ChargePendings）
在构造点一律包装为单实例 `ReadOnlyCollection<T>`（D34 先例）——强转 `List<T>`/`T[]`
得 null；`IList{T}`/非泛型 `IList` 写操作抛 `NotSupportedException` 且不生效。
内部构造保留可变 List 高效流程，只在快照落定点包装一次；每帧状态各持独立包装，
只读包装之间共享安全。哈希/相等语义不变。

### P2：非法 FighterLogicState 静默通过（D62）

**根因**：`Validate` 的待机分支把一切非动作状态当待机——`(FighterLogicState)999`
携带空 MoveId 与 0 帧即可通过构造；`Tick` 把未知状态静默「修正」为 Standing/Crouching。

**修复前失败复现**：P2 相关 10 项新测试全部失败（Validate 不抛、Tick 不抛、
BattleState 构造接受）。

**修复（D62）**：两道门都用 `Enum.IsDefined` 集中校验（唯一事实来源 = 枚举定义）——
`Validate` 无论其他字段一律拒绝未定义状态（错误消息含具体非法值）；
`Tick` 推进前独立拒绝（不依赖构造代为校验）。损坏状态在 BattleState 构造期即被拒
（第一道门），Tick 为第二道门。合法状态（含收招回待机）与负帧数拒绝行为不变。

### 验收（补修实测）

| 项 | 结果 |
|---|---|
| 全量测试 | **343/343 通过**（326 + 17：P1 复现+回归 6 项、P2 复现+回归 11 项） |
| `dotnet build core/BattleCore` | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj` | 0 警告 / 0 错误 |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | 5A 链路完好：`P1 Command: 5A` / `StandLightPunch / Startup 2`（`shots/p04_fix_5a.png`，与注入日志推演逐帧吻合），HUD 无退化 |

快照重放、输入提交异常封闭、帧同步、蓄力/双重指令、一次消费、双人独立均由
既有回归 + 新增 D61 测试双重覆盖（`OldSnapshot_MutationAttempts_DoNotAffectReplay`、
`LongRun_1000Frames_PlayersIsolated_NoPollution`、`EqualStates_HaveStableAndConsistentHashes`）。

---

## 下一阶段计划：阶段 05（运动系统 / 判定与结算前置）

**进入前必须先读** `ARCHITECTURE.md` 第 4 节（D37–D60：输入、指令、状态机与配置规则）与第 5 节。

阶段 04 已正式结项（识别进内核 D55、裸按键 D56、动作状态字段 D57、状态机 D58、
角色配置 D59、消费契约 D60）。后续阶段的自然入口：

1. **定点数与运动**（阶段 05 的前置决策）：位置/速度格式（Q16.16 vs Q8.8）、
   取整发生点——必须先定（ARCHITECTURE 第 5 节问题 2），再接 `FighterState.PositionX` 的
   真实移动与 `LogicState` 的移动/跳跃扩展（Airborne 状态转换）。
2. **攻击判定前置**：Startup/Active/Recovery 时间轴已就绪；判定框（AABB vs OBB）、
   命中/伤害结算与事件流（`(frame, eventId)` 契约）是阶段 06 的入口。
3. **招式数据外置**：`MoveDefinition` 目前是 C# 配置；角色专属数据量大后可外置 JSON
   （识别器与状态机不感知数据来源）。

**明确不需要**：
- 不需要为阶段 05 修改识别器核心（`RecognitionState` 已稳定）；
- 不需要提前设计连段/取消规则（依赖判定与结算先行）。

**注意**：若要「后输入优先」SOCD，必须先扩展 `FrameInput` 记录事件顺序（D27 仍有效）。

> 阶段 04 已连同最终补修结项。接手后先看 `AGENT_HANDOFF.md` 第 6 节，运行 `dotnet test`
> 确认 **343** 项通过再继续。

**每完成一个子任务，立即更新本文件与 `AGENT_HANDOFF.md`。**

