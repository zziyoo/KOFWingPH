# PROGRESS.md — NewKOF 开发进度

> 最后更新：2026-11-08（阶段 08D：飞行道具——D99——**722** 项测试——阶段 08 结项）

---

## 当前开发阶段

**阶段 07A：KO 裁决与基础回合终局 — ✅ 已完成（已提交并推送）**
**阶段 07B：连段统计与招式取消 — ✅ 已完成（已提交并推送）**
**阶段 07C：击退、击飞、空中受击与起身 — ✅ 已完成（已提交并推送）**
**阶段 07 最终专项补修：空中受击推挤与防御击退配置 — ✅ 已完成（已提交并推送，阶段 07 正式结项）**

**阶段 08A：命中停顿 Hitstop — ✅ 已完成（已提交并推送）**
**阶段 08B：地面投技 — ✅ 已完成（已提交并推送）**
**阶段 08C：攻击相杀 — ✅ 已完成（已提交并推送）**
**阶段 08D：飞行道具 — ✅ 已完成（已提交并推送，阶段 08 正式结项）**

历史阶段（全部 ✅）：阶段 01–02（工程与调度）、02 补修（帧同步）、03A 及其三轮补修（输入链路）、
03B-1 及其补修（指令识别）、03B-2 及其最终补修（蓄力/双重）、03B 边界补修（D54）、
阶段 04（角色状态机 D55–D60）与最终补修（D61/D62）、阶段 05A（D63–D65）、05B（D66–D72）、
阶段 06A/06B/06C（判定/结算/事件流，D76–D83）与两轮专项补修（D84–D88）。
阶段 06 结项基线：**566** 项测试。

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

## 阶段 05A 成果（Q16.16 定点数与确定性地面运动，2026-11-07）

### 边界

解决「角色沿地面前进/后退且每帧结果完全可复现」。
**不做**：跳跃/重力、场地边界、双人推挤、碰撞判定（按任务书留给 05B 与阶段 06）。

### 核心架构（D63–D65）

1. **定点数（D63）**：`Numerics.FixedPoint` = Q16.16（`long` 原始值，Scale = 65536）。
   加减 `checked`、乘除 `Int128` 中间结果、取整统一**向零截断**、除零显式抛出、
   溢出抛 `OverflowException` 绝不回绕、无 NaN/Infinity。
   `FighterState.PositionX/PositionY` 升级为定点世界坐标（Y 向上、地面 0）；
   迁移方案：int 隐式转换 = 世界单位（旧 `PositionX: -120` 语义不变），
   `PositionXUnits/PositionYUnits` 为向零截断只读视图；Godot 显示用 `ToDouble()`
   并做 Y 翻转（`screenY = groundY - worldY`），浮点不写回。
   `StateVersion` / `CoreVersion` 2 → 3。
2. **运动配置（D64）**：`MovementDefinition`（步行速度大小，构造期校验 > 0 且 ≤ 32 单位/帧）
   挂在 `FighterDefinition` 可选参数上（null → 默认配置）。演示配置：Kai 前 2.0 / 后 1.5，
   Rei 前 1.75 / 后 1.25（单位/帧）——双人数据刻意不同。
3. **地面运动（D65）**：`GroundMovement.StepX` 纯函数——仅站立待机移动；
   输入按 D46 帧开始朝向解析后做「前/后 → 世界 ±」换算（朝左按前 → X 减小）；
   SOCD 左右中立复用既有解析；蹲下与 Startup/Active/Recovery 一律原地，
   收招归零同帧回站立即恢复步行（与 D58 同帧语义一致）。
   步行速度不进快照：停止即静止，无残余动量。
4. **集成顺序**：`Step` = 输入校验 → 识别（D55）→ 状态机（D58/D60）→ 地面运动 → 全新快照。

### 测试与构建（05A 实测）

新增 **75 项**（343 → **418**）：定点数数值 44 项（正负转换、0.5/0.25/分数精确表示、
向零截断正负用例、加减精确与结合、乘除 128 位中间结果与舍入边界 pinned、
checked 溢出、除零、整数标量乘、相等/排序/哈希、1000/3600 帧累计无漂移、显示转换）；
地面运动 31 项（前/后 1/10/60 帧 pinned、朝左按前 X 减小、等速镜像反对称、
停止无残余、方向反转净位移、SOCD 中立、蹲下阻挡与恢复、动作 11 帧冻结 + 收招同帧恢复、
双人独立、差异化配置、暂停无位移、丢弃预算无位移、Advance vs StepExact 一致、
中间快照续跑一致、同输入两次一致、3600 帧长跑无漂移、配置校验、StepX 直测）。
既有 343 项全部不动地通过；仅 2 处旧断言改为 `ToIntTruncated()` 视图（语义不变）。

| 项 | 结果 |
|---|---|
| `dotnet test` | **418/418 通过** |
| `dotnet build core/BattleCore`（Release） | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj`（Debug/Release） | 0 警告 / 0 错误 |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | P1 持住 D：`P1 Pos: (138, 0)` 且红块越过 P2（`shots/p05a_walk_forward.png`，
  = −120 + 2×129 与注入帧 24→截图帧 154 时序吻合）；P1 持住 A：`P1 Pos: (-313.5, 0)`
  （= −120 − 1.5×129，定点小数逐位命中，`shots/p05a_walk_backward.png`）；
  P1 持住 S+D：`Crouching` 且 `Pos (-120, 0)` 冻结（`shots/p05a_crouch_block.png`） |

HUD 增加两行只读权威坐标（P1 Pos / P2 Pos，世界坐标 Y 向上），面板 264 → 300 高。

### 遗留限制（非阻塞，05B/后续处理）

- 无场地边界：长跑测试明确验证 -120 + 2×3600 = 7080 不会被钳制（05B 引入边界）。
- 无跳跃/重力/速度字段：`PositionY` 恒为 0（05B 引入）。
- 无双人推挤：P1 可视觉越过 P2（05B 引入最小中心间距）。
- 朝向固定不变（自动朝向属 05B 第 5.3 节）。

---

## 阶段 05B 成果（跳跃、落地、场地边界、推挤与自动朝向，2026-11-07）—— 阶段 05 结项

### 边界

解决「角色真正能小跳/大跳/前后跳跃、受重力落地、不穿墙、不与对方不合理重叠、
越身后正确转身」。**不做**：攻击判定框、伤害结算、空中攻击/受击/防御、投技、
完整碰撞引擎（按任务书留给阶段 06）。

### 核心架构（D66–D72）

1. **跳跃参数配置化（D66）**：`MovementDefinition` 扩展预备窗口/小跳初速/大跳初速/
   重力/水平跳速，构造期校验「大跳初速 > 小跳初速」等（B2 可分辨性由校验保证）。
2. **起跳边沿（D67）**：`FighterState.UpHeldLastFrame`（上一帧解析后的「上」电平，
   随快照携带）→ 边沿请求起跳；站立/蹲下皆可；落地后持住不起跳（B5）；
   SOCD 上+下 → 跳跃胜出。`PreJump`（3 帧）不可取消、不消费、不移动、不被蹲下覆盖。
3. **起跳帧锁定（D68）**：预备窗口末帧按当帧输入一次锁定跳跃类型（按住 → 大跳，
   否则小跳）与水平初速（前/后/中立）；`JumpKind`/`JumpLaunch` 随快照携带；
   空中无控制（速度恒定，反向按住不改变轨迹——测试固定）。
4. **空中积分与落地（D69）**：每帧「vy' = vy − g → 坐标积分 → y ≤ 0 落地修正」
   （含起跳帧，无例外）；落地同帧贴地/零速/回站立（唯一由运动阶段写入的状态边）；
   落地帧不消费指令，落地后第一个地面帧恢复消费（显式语义，测试固定）。
5. **场地边界（D70）**：`StageDefinition`（默认 X ∈ [-640, 640]、地面 0、间距 60），
   双方候选 X 统一钳制（含空中）——不穿墙、不反弹、不振荡。
6. **双人推挤（D71）**：双方都非空中时按同帧候选对称分配；完全重叠平局 = P1 左 / P2 右；
   墙优先于间距（靠墙允许间距 < 60，绝不越界）；持续推进产生确定性蠕动。
7. **自动朝向（D72）**：帧末（推挤后）仅地面待机转向对手；等距保持原朝向；
   PreJump/Airborne/动作朝向冻结；D46（帧开始朝向解析输入）完整保留。
8. **`FighterState` 扩展**：`VelocityX/VelocityY/JumpKind/UpHeldLastFrame`；
   `StateVersion`/`CoreVersion` 3 → 4。Validate 新不变式：空中必须 Y>0 且带跳跃类型、
   地面必须贴地零速无跳跃类型——新枚举值（PreJump=5/Airborne=6）不能绕过 D62 校验。

### 测试与构建（05B 实测）

新增 **42 项**（418 → **460**）：跳跃 23 项（PreJump 窗口逐帧、小跳/大跳解析轨迹逐值
pinned、分类边界、四向跳水平锁定、空中无控制、防自动连跳、再起跳、动作期间不起跳、
空中不消费/落地次帧消费、蹲下起跳、SOCD、Validate 新不变式、配置校验）；
边界与双人 12 项（左右墙、空中钳制、相向停止、推挤蠕动、靠墙收敛 582/640、
同位置平局、落地冲突、越身与朝向、镜像对称）；确定性 8 项（双跑一致、中间快照续跑、
Advance vs StepExact、三种渲染帧率一致、暂停冻结、10000 帧长跑、顺序公平性）。
既有测试更新 5 处（全部为 05B 语义变化的**合理**结果，非削弱）：长跑测试改用宽场地
（05A 版本依赖无边界前提）、`(5)` 从非法枚举清单移至合法值（PreJump=5）、
Up+Down 待机测试改为「边沿起跳 + 持住站立」双断言、镜像相遇断言 ±30（推挤截停）、
MovementDefinition 构造补跳跃参数。

| 项 | 结果 |
|---|---|
| `dotnet test` | **460/460 通过** |
| `dotnet build core/BattleCore`（Debug/Release） | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj`（Debug/Release） | 0 警告 / 0 错误 |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | 小跳：`P1 Pos: (-120, 13.5)` / `Vel (0, 1)` / `Jump: ShortHop` / `Airborne`（`shots/p05b_shorthop.png`）；
  大跳：`(-120, 37.5)` / `Vel (0, 1.5)` / `Jump: HighJump`（`shots/p05b_highjump.png`，明显高于小跳）；
  落地防连跳：持住「上」至逻辑帧 132 → `Standing (−120, 0)` / `Vel (0, 0)`（`shots/p05b_landed_no_rejump.png`）；
  推挤到墙：`P1 (582, 0)` / `P2 (640, 0)`（`shots/p05b_wall_push.png`，与单元测试解析值逐位一致） |

HUD 增加双人 `Vel (vx, vy) Jump: <类型>` 行（只读权威速度与跳跃类型），面板加高至 350。

### 已知限制（非阻塞，后续阶段处理）

- 空中攻击/受击/防御不存在（Airborne 期间缓冲保留到有效期，落地后消费）。
- 推挤是基础位置约束：无判定框、无身体 AABB、无投技抓取。
- 朝向仅在帧末按位置更新，无手动转身指令。
- `StateVersion` 4 仍无序列化快照（K8）。

---

## 阶段 05 最终专项补修（2026-11-07）—— 推挤、高速穿越与状态安全（D73–D75）—— 阶段 05 结项

### 复现（修复前实测 22 项新测试失败，四项缺陷全部实证）

1. **P1-A 墙角残留重叠**：P2 贴墙 640、P1 持续推进 → 旧算法长期稳定在 **582/640（间距 58）**，
   旧测试 `PushAgainstWall` 把它固定为预期（错误预期，已更正）；1 raw 缺口场景
   （P1 raw=1 / P2=60）永久残留 60−1 raw 间距；贴墙落地重叠（间距 40）只修一半（间距 50）。
2. **P1-B 高速地面穿越**：双方 32 单位/帧从 ∓30 相向冲刺，候选 ∓2 已交换位置，
   旧算法按候选顺序推挤 → **P1=+30 / P2=−30 的地面穿越**被当作合法站位；
   32 冲入静止者、动作中被穿越、镜像场景同样复现。
3. **P2-A GroundY 不一致**：`StageDefinition` 接受 GroundY=10/−10/0.5，但落地判定、
   状态校验、出生高度、Godot 显示全部以 Y=0 为基准——非零配置产生双重权威地面高度。
4. **P2-B 非法 JumpKind**：`(JumpKind)999` + Airborne + Y>0 通过「≠ None」检查进入权威快照
   （地面状态早已拒绝但理由不同——无统一「未定义」校验）。

### 修复（D73–D75，最小改动，不重构内核）

- **D73 双人位置统一求解**（`ApplySeparation` 重写）：① 帧开始双方都在地面 → 以帧开始
  相对顺序为约束（禁止地面单帧交换左右）；涉及空中（越身/落地帧）→ 候选顺序为准，
  新地面顺序落地帧建立（空中越身不被禁止，测试固定）；② 对称分配缺口后若因触墙间距
  仍不足 → 剩余量由仍有墙余量的一方承担（580/640，废除 582/640）；③ 奇数原始单位
  余数显式归 P1（镜像一致）。闭式求解、同帧双方统一计算、无迭代无震荡、全定点。
- **D74**：`StageDefinition` 构造期拒绝任何非零 GroundY（正/负/分数全部 `ArgumentException`）。
- **D75**：`FighterState.Validate` 增加 `Enum.IsDefined(JumpKind)`——未定义值任何状态拒绝，
  消息含具体非法值；BattleState 构造期第一道门同步生效。

### 测试与构建（专项补修实测）

新增 **25 项**（460 → **485**）：地面防穿越 9 项（32/32 相向、32 冲静止者、32v1 精确对半、
镜像、百帧无震荡无穿越、动作中被冲、越身合法且落地建序、贴墙高速单帧求解、双跑一致）；
墙角与精度 9 项（右墙 580/640、左墙镜像、双方同推右/左墙、精确间距不动、1 raw 缺口、
贴墙落地、GroundY 三类拒绝与默认合法）；JumpKind 校验 7 项（Airborne+999/−1、
地面三态+999、BattleState 构造门、合法值回归）。
**旧测试预期更正 1 处**：`PushAgainstWall` 582/640 → 580/640（旧断言把「求解器没把剩余
位移分配给未触墙方」的残留重叠固定为预期——按 D71 的本意，场地宽度足够时必须满足
最小间距，580/640 才是正确行为）。其余 459 项基线全部不动地通过。

**变异验证**（完成后已还原正确源码）：禁用墙角残差再分配 → 4/5 墙角测试失败；
取消帧开始顺序约束 → 5/9 穿越测试失败。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug 与 Release） | **485/485 通过** |
| `dotnet build core/BattleCore`（Debug/Release） | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj`（Debug/Release） | 0 警告 / 0 错误 |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | 右墙：P1 持住 D、P2 持住 → 至逻辑帧 1303：`P1 (580, 0)` / `P2 (640, 0)`（`shots/p05fix_right_wall.png`，间距恰 60）；
  左墙镜像：`P1 (-640, 0)` / `P2 (-580, 0)`（`shots/p05fix_left_wall2.png`，显式释放按键后仍稳定保持）；
  双人相向：`P1 (-18.88, 0)` / `P2 (41.13, 0)`（HUD 显示为 0.## 舍入，实际间距恰 60，色块相接不穿越，
  `shots/p05fix_headon.png`）；大跳空中 `(-120, 37.5)` / `Jump: HighJump`（`shots/p05fix_highjump_air.png`）
  与落地后 `(-120, 0)` / `Standing`（`shots/p05fix_jump_landed.png`）——跳跃链路零回归。
  空中越身与落地建序由单元测试覆盖（真实键位无法在图形运行中构造 32 单位/帧穿越场景，
  已用测试专用运动配置 + 合成输入在 GroundCrossingTests 验证，未宣称图形运行覆盖该场景）。 |

---

## 阶段 06A 成果（确定性 AABB 攻击判定，2026-11-07）

### 边界

只做判定框几何、攻击数据时间线与确定性接触检测。**不做**伤害/防御/硬直（06B）、
重复命中抑制与事件流（06C）。

### 核心架构（D76）

1. **`FixedRect`**：AABB（MinX/MaxX/MinY/MaxY 全定点），构造期拒绝零/负宽高；
   相交 = 正面积重叠，**边缘接触不算命中**，无 epsilon（专项测试固定）。
2. **三框职责**：Pushbox = 显示/调试查询（宽 60 与 D73 间距一致，权威推挤仍由 D73 独占）；
   Hurtbox = 姿态受击框（站立系 120 高、蹲下 70、空中跟随权威 PositionY）；
   Hitbox = 只来自 `AttackDefinition`（挂在 `MoveDefinition.Attacks`，可空），
   仅 Active 配置帧范围生效。
3. **帧语义**：Startup → Active 转换完成的那一逻辑帧 = Active 第 1 帧
   （elapsed = ActiveFrames − 剩余），该帧产生攻击框；Recovery 首帧不再有。
   `int.MaxValue` 哨兵 = 「至 Active 末帧」。
4. **镜像**：前向偏移按权威 Facing 施加（±），与 Godot 精灵翻转无关；
   攻击框跟随权威位置；不检测攻击者自己。
5. **接触输出**：`BattleStepResult.Contacts`（只读；顺序 = 攻击者 0→1、攻击序号升序）。
   基于推挤/边界处理后的最终候选位置检测。
6. **Godot**：`DebugBoxDrawer` 顶层调试节点（Hitbox 红/Hurtbox 蓝/Pushbox 绿轮廓，
   F2 开关），几何全部来自 `FighterBoxes` 权威查询，只做显示换算。

### 测试与构建（06A 实测）

新增 **24 项**（485 → **509**）：AABB 几何 6 项（完全/部分重叠、边接触不算、1 raw 边界、
负坐标、非法矩形拒绝）；判定框 18 项（三姿态 Hurtbox、Pushbox 对齐 D73、
Startup/Active 首末帧/Recovery 时间线、无招式无框、镜像、跟随位置、非法几何与范围拒绝、
Step 时间线接触恰在 Active 帧、镜像配置镜像接触、无自命中、墙角推挤不退化）。
既有 485 项全部不动地通过（期间发现并修复 `GetActiveHitboxes` 缺「必须 Active 状态」
门的原型缺陷——修复前 Startup/Recovery 也会产出攻击框，被专项测试捕获）。

| 项 | 结果 |
|---|---|
| `dotnet test` | **509/509 通过** |
| `dotnet build core/BattleCore`（Debug/Release） | 0 警告 / 0 错误 |
| `dotnet build NewKOF.csproj`（Debug/Release） | 0 警告 / 0 错误 |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行 | `shots/p06a_hitbox_active.png`（逻辑帧 29 = 5A Active 首帧：红色攻击框出现在
  P1 面前，蓝色 Hurtbox/绿色 Pushbox 双方可见）、`shots/p06a_recovery_nobox.png`（Recovery 无攻击框） |

---

## 阶段 06B 成果（命中、伤害、防御与硬直，2026-11-07）

### 边界

地面基础战斗结算：命中扣血、站/蹲防御、High/Mid/Low 高度表、Hitstun/Blockstun、
动作中断、双方同帧互中。**不做**：空中受击/防御（显式跳过，测试固定）、攻击相杀、
连段/取消、击飞、KO。

### 核心架构（D77–D80）

1. **伤害数据（D77）**：`AttackDefinition` 扩展 `Damage`（≥ 0）/`HitstunFrames`（≥ 1）/
   `BlockstunFrames`（≥ 1）；`AttackHeight`（High/Mid/Low）为显式配置（不由几何推断），
   防御表 `GuardCovers` 为唯一事实来源。
2. **硬直（D78）**：`FighterLogicState` 新增 `Hitstun=7`/`Blockstun=8` + `FighterState.StunRemainingFrames`；
   Tick 倒计时归零同帧回待机更新与消费；期间不能步行/起跳/发动/防御；动作中受击 → 招式中断。
   Validate：硬直状态必须剩余 ≥ 1、其他状态必须为 0（D62 延伸）。
3. **防御（D79）**：站立/蹲下待机 + 按住「后」（帧开始朝向解析）；高度表裁决；
   成功防御不掉血进入 Blockstun；动作中/PreJump/硬直中不能防御。
4. **同帧互中（D80）**：结算基于同一份结算前状态并行解析双方结果再统一应用——
   无先手优势；双方都命中则各自掉血进入硬直（无相杀）。
5. **集成**：结算发生在运动/推挤之后（基于最终候选位置）、朝向更新之前（硬直朝向冻结）；
   `StateVersion`/`CoreVersion` 4 → 5。
6. **已知中期行为**：Blockstun 中不能再防御 → 多帧攻击在 06B 会穿过防御命中
   （06C 攻击实例去重消除）；多帧 Active 也会重复扣血（同上）。

### 测试与构建（06B 实测）

新增 **19 项**（509 → **528**）：命中伤害/中断/体力钳制/无接触不动/空中跳过 5 项；
防御与高度表 6 项（站防 Mid、蹲防 Low/Mid、蹲防 High 失败、站防 Low 失败、需按住后、
动作中不可防御）；同帧互中 3 项（双方掉血硬直、镜像一致、一方命中一方防御）；
硬直行为 4 项（倒计时封禁动作、归零同帧回待机/蹲下、Blockstun 恢复、Validate 不变量）+
完整识别链路逐帧 1 项。既有 509 项中 1 处数据更新（`(7)` 从非法枚举清单移至合法值——
Hitstun=7）。期间状态机测试还捕获了硬直归零同帧消费缓冲的 D60 语义（正确行为，测试对齐）。

| 项 | 结果 |
|---|---|
| `dotnet test` | **528/528 通过** |
| 四构建 | 均 0 警告 / 0 错误 |
| Godot 图形运行 | `shots/p06b_hit.png`：P1 走近后 5A 命中——`P2 HP: 940/1000`（两 Active 帧 × 30，
  06C 去重前行为）+ `Hitstun(6)`；`shots/p06b_guard.png`：P2 按住后站立防御——
  `P2 HP: 1000/1000` + `Blockstun(8)`，红色攻击框与 P2 Hurtbox 相交但不掉血 |

---

## 阶段 06C 成果（攻击实例去重、确定性事件流与最终集成，2026-11-07）—— 阶段 06 结项

### 核心架构（D81–D83）

1. **攻击实例身份（D81）**：`FighterState.MoveStartedFrame`（消费同帧赋值，随快照携带）+
   `LastHitAttackStartFrame`（被击者已结算记录，命中/被防御都写入）——
   同一实例对同一目标只结算一次；新发动可再命中；被中断实例不复活。
   Validate：动作中发动帧 ≥ 0、非动作 −1。`StateVersion`/`CoreVersion` 5 → 6。
2. **事件流（D82）**：`BattleEvent`（Frame/EventId/Type/Source/Target/MoveId/Amount）；
   EventId = Frame×16+帧内序号（上限 16 显式失败）；类型 MoveStarted/AttackActive（仅首生效帧）/
   Hit（含伤害）/Blocked/HitstunStarted/BlockstunStarted；帧内顺序按阶段固定；
   `BattleStepResult.Events` 只读包装（D61 先例）。
3. **提交后发布（D83）**：`BattleSession.eventsCommitted` 观察者在 frameCommitted 之后、
   同一 try/catch 内投递——帧完整提交才有事件；失败帧零事件；观察者异常进入 Faulted/Recover；
   Recover 不重发（消费者按 EventId 去重，进程内语义）。
4. **Godot**：HUD 增加事件行（最近一帧事件：类型/双方/招式/数值/EventId）。

### 测试与构建（06C 实测）

新增 **12 项**（528 → **540**）：去重 4 项（多帧 Active 单次命中、被防御已结算不重置、
新实例再命中、被中断不复活）；事件 4 项（帧/类型/EventId 精确、只读、重放一致、
EventId 全局唯一）；会话 3 项（提交后发布 + 状态一致、提交失败零事件泄漏、观察者异常
Faulted/Recover）；10000 帧混合战斗长跑确定性 + EventId 唯一 1 项。
既有测试 4 处构造更新（直接构造 Active 需携带发动帧——新不变式）；期间 Validate 捕获
并修复「命中结算未清空被中断者的发动帧」真实缺陷。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug 与 Release） | **540/540 通过** |
| 四构建 | 均 0 警告 / 0 错误 |
| Godot 无头导入 | `[ DONE ]` |
| Godot 图形运行（七场景） | ① 普通攻击命中：`shots/p06c_hit_events.png`（逻辑 109 = Active 首帧：红框相交、
  `HP 970` 单次 30、`Hitstun(12)`、`Event: HitstunStarted P1→P2 StandLightPunch 12 (id=1730)`，
  EventId = 108×16+2 与公式一致）；② 站立防御：`shots/p06c_guard_events.png`（`HP 1000` +
  `Blockstun(7)`，防御后倒计时不再被第二帧打断）；③ 蹲防/高度表、④ 双方同帧互中、
  ⑤ 多帧 Active 单次命中、⑥ 命中后再发动、⑦ 旧快照重放——由单元测试逐项覆盖
  （`CombatSettlementTests`/`CombatEventTests`；图形键位无法确定性构造部分场景，未宣称图形覆盖） |

**阶段 06 正式结项。**

---

## 阶段 06 专项补修（独立交接复核，2026-11-07）—— 多攻击框序号 / 实扣伤害 / 事件契约（D84–D86）

**背景**：新 Agent 独立复核阶段 06，不采信上一轮交接报告。基线 `5788b7f`、工作区干净、
`dotnet test` 实测 **540/540** 通过——报告中的数字属实。但代码审查发现两处真实缺陷、
两处契约不一致，全部先写失败复现再修。

### 复现（修复前实测，编号即失败断言）

| # | 复现 | 修复前实测 | 期望 |
|---|---|---|---|
| P1-a | 招式两个攻击定义（Attack[0] 仅 Active 第 2 帧 High/10、Attack[1] 仅第 1 帧 Low/30），第 1 帧的 `AttackContact.AttackIndex` | **0** | 1 |
| P1-b | 同上第 1 帧结算伤害（满血 1000） | **990**（取到 Attack[0]） | 970 |
| P1-c | 同上第 1 帧，P2 蹲防（Low 可防） | **990 + Hitstun(10)**（被当成 High 破防） | 1000 + Blockstun(6) |
| P1-d | 镜像（P1 朝左）第 1 帧的 `AttackIndex` | **0** | 1 |
| P2-a | 体力 10 受 30 伤害的 `Hit.Amount` | **30** | 10 |
| P2-b | 体力 1 受 `int.MaxValue` 伤害的 `Hit.Amount` | **2147483647** | 1 |
| P2-c | 同帧互中：P1 体力 10 / P2 满血，各自 `Hit.Amount` | **30 / 30** | 10 / 30 |
| P2-d | 双方同帧在 Active 首帧互中并被同时打断的 `AttackActive` 事件数 | **0** | 2 |

根因：① `FighterBoxes.GetActiveHitboxes` 先按生效帧过滤，`HitDetection.Collect` 用过滤后下标
当攻击定义序号；② 结算把 `max(0, …)` 的结果写进状态，事件却仍读配置伤害；
③ `CollectEvents` 在 `ApplyCombatSettlement` 之后读状态（该函数原地改写数组），被打断者已不在 Active。

### 修复（D84–D86，最小改动，不动既有架构）

1. **D84**：新增 `ActiveHitbox(FixedRect Box, int AttackIndex)` 与
   `FighterBoxes.GetActiveHitboxesWithIndex`；`GetActiveHitboxes` 保留为几何视图（调试绘制不变）。
   `HitDetection.Collect` 改用原始序号。同帧多框 → 全部产生接触（序号升序），结算取第一条（最小序号），
   测试固定。
2. **D85**：`ApplyCombatSettlement` 用 `Health前 − Health后` 覆盖 `CombatSettlement.Damage`，
   事件层直接使用——不再有第二条计算路径。
3. **D86**：新增 `CollectAttackActive`（结算前调用）产出布尔标记，`CollectEvents` 只消费标记；
   语义统一为「本帧进入生效窗口（`elapsed == min(FirstActiveFrame)`）、每次发动一次」。
   `BattleEvent.MaxFrameForEventIds = int.MaxValue / 16`，超出显式抛异常（不回绕）。
4. 契约审查**未改**：EventId 仍为 `int`（60Hz ≈ 25.9 天余量，升级 long 需整体评估接口与测试）；
   D83 提交时序本就正确（Step 失败/输入提交失败零事件、提交后才投递、Faulted/Recover 不重发、
   Events/Contacts 只读）——本轮只补测试固定。

### 测试与构建（专项补修实测）

新增 **18 项**（540 → **558**）：`MultiAttackContactTests` 8 项（原始序号、两帧各自结算、
镜像、Low/High 防御表、同帧双框优先级、错开窗口 AttackActive 只发一次）、
`CombatEventTests` 10 项（实扣伤害 5 组 Theory、防御无 Hit 事件、互中各自实扣、
同帧互中两个 AttackActive、Step 失败零事件、EventId 越界显式失败）。
既有 540 项无一删除或削弱。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug / Release） | **558/558 通过**（0 失败 0 跳过） |
| 四构建（BattleCore / Godot × Debug / Release） | 均 **0 警告 / 0 错误** |
| Godot 无头导入 | `[ DONE ]`，无错误 |
| Godot 图形运行 | `shots/p06fix_hit_verify.png`（逻辑 119 = Active 首帧：红框与 P2 Hurtbox 相交、`P2 HP: 970/1000`、`Hitstun(12)`、`Event: HitstunStarted P1→P2 StandLightPunch 12 (id=1890)` = 118×16+2，与公式一致；`shots/` 被 `.gitignore` 忽略，仅本机证据） |
| 旧快照重放 / 同帧互中 / 多窗口 / 事件一致 / 阶段 05 推挤回归 | 由 `MidSnapshotReplay_ProducesIdenticalEvents`、`MutualHits_*`、`StaggeredWindows_*`、`HitEvent_Amount_*`、`Step_WallCornerPush_DoesNotRegress` 等测试覆盖 |

**阶段 06 独立交接验收通过。**

### 阶段 07 推荐实施顺序（未开工，仅建议）

1. **KO / 体力归零裁决**：`Health == 0` 目前只是数值，无回合结束语义。先定「KO 检测在 Step 的哪一阶段、
   归零当帧是否还结算对方攻击（D80 同时性）」，再动 `RoundSystem`。
2. **连段与取消**：需要先定 hitstun/blockstun 期间的可取消窗口与「取消目标动作」数据形状，
   不要在 `FighterStateMachine` 里加特例判断。
3. **击飞 / 空中受击**：当前空中被击显式跳过（D78）。落地-受击-起身是完整状态簇，需一次设计
   （含 `Airborne` + `Hitstun` 的组合状态，或新增 `AirHitstun` 枚举值并同步 `Validate`）。
4. **招式数据外置**（ARCHITECTURE 第 5 节问题 7）：与上面三项解耦，可并行；先定格式与加载期校验。
5. **回滚/录像**（第 5 节问题 8/10）：`StateVersion` 已到 6，序列化格式未定——排最后。

---

## 阶段 06 最终收尾（2026-11-07）—— AttackActive 负载完整性 / 攻击框只读封装（D87–D88）

**背景**：阶段 06 逐文件复查又发现两处局部问题（均由上一轮补修引入/遗留），
本轮只修这两项，不新增阶段 07 功能。

### 复现（修复前实测）

| # | 复现 | 修复前实测 | 期望 |
|---|---|---|---|
| P1-a | 双方**不同招式**（PokeA / PokeB）同帧 Active 首帧互中，两个 `AttackActive` 的 `MoveId` | **null / null** | PokeA / PokeB |
| P1-b | 只有 P1 产生 AttackActive、但 P1 同帧被 P2 的攻击打断（P2 处于 elapsed 1） | **null** | PokeA |
| P2-a | `GetActiveHitboxesWithIndex` 非空结果的类型 | **可强转为 `List<ActiveHitbox>`**（`Clear()` 成功） | 强转失败 + 写操作抛 `NotSupportedException` |

P1 根因：上一轮把「是否生效」提到结算前，但**事件负载仍读结算后的 `fighters[i].MoveId`**；
同帧互中双方都在结算中被清空 `MoveId`，于是生效事件带着 null 招式。
P2 根因：非空分支 `return boxes`（`List<ActiveHitbox>`）只靠返回类型「看起来只读」。

### 修复（D87–D88）

1. **D87**：`CollectAttackActive` 返回 `AttackActiveSnapshot(bool IsActive, string? MoveId)[]`，
   招式身份与生效标记**一起**在结算前取得；`CollectEvents` 只读快照。
   `Add()` 增加不变式：`AttackActive` 的 MoveId 为空 → 显式抛异常（不允许用 null 表示生效事件）。
   不依赖结算后状态、不修改原快照、不共享可变状态；帧数、每实例一次、EventId 公式、
   同帧并行结算规则均不变。
2. **D88**：非空结果改为 `boxes.AsReadOnly()`；空结果仍共享 `Array.Empty<ActiveHitbox>()`。
   `ActiveHitbox` 结构、索引语义、顺序、AABB 几何、`GetActiveHitboxes` 几何视图全部不动。

### 测试与构建（本轮实测）

新增 **8 项**（558 → **566**），既有 558 项全部保留：

- `CombatEventTests` 5 项：双方不同招式互中 → 两个 AttackActive 各带自己的 MoveId（并校验
  Hit / HitstunStarted 负载、EventId = 帧×16+序号、帧内顺序、被中断实例下一帧不复活）；
  只有一个角色生效且被打断时仍带 MoveId；未被中断时仍带 MoveId 且招式继续；
  第 2 个 Active 帧不重复产生 AttackActive；同快照同输入重放事件逐位一致。
- `HitDetectionTests` 3 项：非空结果不可强转 List/数组、`IList<T>` 与非泛型 `IList` 写操作
  全部抛 `NotSupportedException`、尝试前后 Count 与元素逐位一致；空结果共享同一实例；
  多框顺序与原始 `AttackIndex` 保持（并与 `GetActiveHitboxes` 几何视图逐位一致）。
- 另补 3 处既有测试的**负载断言**：`Events_FramesTypesAndIds_AreExact` 的 AttackActive MoveId、
  `BlockedAttack_ProducesNoHitEvent` 的 Blocked/BlockstunStarted MoveId、
  `StaggeredWindows_*` 的 AttackActive MoveId。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug / Release） | **566/566 通过**（0 失败 0 跳过） |
| 四构建（BattleCore / Godot × Debug / Release） | 均 **0 警告 / 0 错误** |
| Godot 无头导入 | `[ DONE ]`，无错误 |
| Godot 图形运行 | `shots/p06final_hit_verify.png`（逻辑 119：红框与 P2 Hurtbox 相交、`HP 970/1000`、`Hitstun(12)`、`Event ... (id=1890)`）——**与上一轮截图 md5 逐位相同**，说明渲染与判定未因只读包装改变；`shots/` 被 `.gitignore` 忽略 |
| 阶段 05/06 回归 | 全量 566 项含墙角 580/640、地面防穿越、空中越身、起跳/落地帧语义、5A 时序、多攻击框索引、Hit.Amount 实扣语义——全部通过 |

**阶段 06 最终交接完成（D76–D88）。**

---

## 阶段 07A 成果（KO 裁决与基础回合终局，2026-11-07）—— D89

### 边界

只做「单回合终局」：不做三局两胜、回合计时、选人、KO 动画/音效。
KO 判定不改变既有命中/防御/去重规则，只在其后增加一次基于最终体力的裁决。

### 核心架构（D89）

1. **`RoundState`**：`Phase`（InProgress/Finished）+ `Outcome`（None/P1Win/P2Win/DoubleKO）+
   `WinnerFighterId`（0/1/null）。不可变 record，`Validate` 强制三字段自洽并拒绝未定义枚举；
   `Finish` 只允许 InProgress → Finished 一次；`FromFinalHealth` 是唯一判定入口。
2. **权威归属**：`BattleState.Round` 随快照携带（构造参数可选，默认 InProgress，旧构造路径语义不变）。
   表现层只读，不判定胜负。`StateVersion`/`CoreVersion` 6 → 7（仅格式边界标记）。
3. **判定时机**：双方命中/防御结算**统一应用之后**，读最终 `Health`——
   P1>0&P2=0 → P1Win；P1=0&P2>0 → P2Win；双方 0 → DoubleKO。不存在先手偏差。
4. **终局冻结**：`Step` 在已结束回合上仍推进帧号，但角色/回合/事件逐位不变（零事件、零接触）。
   选择冻结而非抛异常，`BattleSession` 的帧同步不变量与 Godot `Advance` 都无需特判。
5. **事件**（帧内顺序 `MoveStarted → AttackActive → Hit/Blocked + 硬直 → KO → RoundEnded`）：
   `Knockout`(胜者→败者, Amount 0) / `DoubleKnockout`(0→1, Amount 0) / `RoundEnded`
   (Amount = (int)RoundOutcome，Source = 胜者或 -1，Target = -1)，每个回合各恰好一次。
   每帧事件上限 16 不变（最坏 11 条）。
6. **重开**：`BattleState.CreateDefault()` + 新 `BattleSession` + 新 `InputBuffer`
   （Godot 侧 F5）。旧输入/硬直/攻击实例/事件显示全部丢弃；不复用旧快照。

### 测试与构建（07A 实测）

新增 **17 项**（566 → **583**）：P1/P2/DoubleKO、恰好归零、超额伤害钳制、0 伤害不 KO、
防御不 KO、KO 不跳过对方同帧命中、镜像对称、终局冻结（帧号前进/输入无效/零事件）、
KO 事件只出现一次且 EventId 合法、中间快照重放一致、RoundState 构造与不变式。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **583/583 通过** |
| 四构建 | 均 0 警告 / 0 错误 |
| Godot 图形运行 | `shots/p07a_p1win.png`（P1 WIN / HP 0 / Round Finished/P1Win）、
  `shots/p07a_p2win.png`（P2 WIN，P2 用 236A 击倒 P1）、`shots/p07a_doubleko.png`
  （DOUBLE KO，双方同帧用 3 帧启动技互中）——三张横幅均来自权威 `RoundState` |
| 既有 566 项 | 全部通过（无删除/跳过/削弱） |

**已知限制**：KO 后角色完全冻结（含空中位置，07C 落地未完成时尤为明显）；无 KO 演出；
`EventId` 重置语义见 D89（仅同场次唯一）。

---

## 阶段 07B 成果（连段统计与招式取消，2026-11-07）—— D90/D91

### 边界

只做「连续命中的计数」与「数据驱动的招式取消」：不做连段伤害衰减、不做取消树的 UI、
不做空中取消（本阶段空中无可取消的动作）、不做投技取消。

### 核心架构

1. **`ComboState`（D90）**：`Count` + `AttackerId`，作为被击方记录进入 `FighterState`。
   连续性用<b>结算前</b>状态判定（`Hitstun` 等受击延续状态 + 同一攻击者）；
   「恢复自由行动」用<b>帧末</b>状态判定，因此硬直归零转待机的那一帧<b>结束旧连段</b>
   （该帧再被命中按 1 Hit 计）。成功防御不计数并清空。同一实例由 D81 去重，不会重复计数。
2. **`CancelDefinition`（D91）**：挂在来源招式的 `MoveDefinition.Cancels`；
   窗口 = 自发动帧起算的经过帧数 `[First,Last]` + 阶段过滤（Active/Recovery）；
   确认条件 = 命中确认 / 防御确认 / 空挥（至少一个）；目标必须存在于同角色配置且非自身。
3. **确认与消费**：结算把 `HitConfirmFrame` / `BlockConfirmFrame` 写进攻击者（仅当仍是同一实例）；
   取消要求确认帧<b>更早</b>（`frame > confirmFrame`），从下一逻辑帧起才可用。
   取消在 `Tick` 的<b>阶段推进之前</b>判定，用的是帧开始状态与固定逻辑帧窗口。
   选择顺序 = 规则声明顺序 × D60 缓冲顺序，第一个满足者胜出并被消费；每帧至多一次；
   目标招式以新的 `MoveStartedFrame` 独立发动，不继承旧实例的确认与去重标记。
4. **不变式**：确认帧只在招式期间存在且 ≥ 当前 `MoveStartedFrame`；非动作状态必须为 -1
   （换招/中断/回待机一律清空）。连段攻击者必须 ≠ 记录所属角色。

### 测试与构建（07B 实测）

新增 **22 项**（583 → **605**）：连段 9 项（首击、第二独立实例、同实例不重复计数、
恢复自由行动重置、硬直末帧仍算连段、恢复帧算新连段、防御不计数并打断、双方各自独立、
快照重放一致）、取消 11 项（命中确认取消、取消后形成 2 Hit、空挥不可取消、防御确认取消、
窗口过早/过晚、阶段过滤、无规则角色不能取消、缓冲只消费一次、终局后不能取消、中间快照重放）、
配置合法性 2 项（`CancelDefinition` / `FighterDefinition` 构造期拒绝非法规则）。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **605/605 通过** |
| 四构建 | 均 0 警告 / 0 错误 |
| Godot 图形运行 | `shots/p07b_cancel_window.png`（`P1 Cancel: StandLightPunch @5 WINDOW hit@120` +
  `Combo: P2 1 by P1`）、`shots/p07b_cancel_combo.png`（5A 取消成 236A：
  `P1 Move: FireballPrototype` + `Combo: P2 2 by P1` + `HP 920`） |
| 既有 583 项 | 全部通过（无删除/跳过/削弱） |

**演示数据**：Kai 的 5A 新增「命中确认后取消到 236A」规则（窗口 3..10 帧），
用于图形验收与后续手感调参；Rei 无取消规则（角色独立）。

**已知限制**：取消不做「同帧连锁」（每帧一次即上限，属刻意设计）；无取消窗口 HUD 图形化高亮
（只有文本）；空挥取消目前没有演示数据使用。

---

## 阶段 07C 成果（击退、击飞、空中受击、倒地与起身，2026-11-07）—— D92/D93 —— 阶段 07 结项

### 边界

只做「最小的物理反馈链路」：地面击退、击飞、空中被击、倒地、起身。
**不做**：空中防御、空中受击身/受身、浮空连段限制、击退撞墙反弹、起身无敌、Hitstop、
击飞轨迹预测 UI、投技/飞行道具。

### 核心架构

1. **地面击退（D92）**：`AttackDefinition` 增加 `GroundKnockback` / `GroundKnockbackFriction` /
   `BlockKnockback`；方向 = 命中时双方权威相对位置的「远离攻击者」（重合时用攻击者朝向）。
   位移在<b>运动阶段</b>逐帧施加（`x += vx`，`vx` 按摩擦衰减且不过零），因此自动经过
   场地钳制（D70）与双人最小间距（D73）。速度/摩擦只允许存在于 Hitstun/Blockstun，
   且必须同时为零或同时非零。
2. **击飞与空中受击（D93）**：`LaunchVelocityX/Y` + `KnockdownFrames` + `WakeupFrames`；
   垂直初速 > 0 即「会击飞」，构造期强制 `KnockdownFrames ≥ 1`（落地必倒地）。
   新增 `AirHitstun`（空中积分，持续到落地）、`Knockdown`（贴地倒计时）、`Wakeup`（起身倒计时）。
   落地计划（倒地/起身帧数）随快照携带，落地时转成状态计时，起身结束时清空。
3. **空中被击**：`Airborne` / `AirHitstun` 目标不再被跳过——伤害照常结算并进入 `AirHitstun`；
   击飞攻击覆盖轨迹并给倒地计划，非击飞攻击保持原轨迹、落地直接站起。空中不可防御，
   `JumpKind` 一律清空（击飞不是跳跃）。
4. **新增状态合法性（D62 延伸）**：`Validate` 覆盖 `AirHitstun` 的 Y/速度例外
   （Y=0 时垂直速度必须为正 = 击飞首帧）、`Knockdown/Wakeup` 的倒计时、落地计划字段归属、
   Hurtbox 姿态（倒地用蹲下高度）。
5. **兼容**：KO 仍在结算后按最终体力判定（击飞致死的当帧即终局，冻结时保留空中位置）；
   连段把 `AirHitstun/Knockdown/Wakeup` 视为受击延续（D90 扩展）；D81 去重在空中同样生效。

### 测试与构建（07C 实测）

新增 **16 项**（605 → **621**）：击退逐帧位移序列（4/3/2/1/0）、无击退招不位移、
防御击退默认 0、墙角钳制、双方互相击退对称、击飞当帧状态与 12 帧逐帧轨迹（顶点 33）、
落地→倒地→起身→待机时间线、跳跃中被击保持轨迹并直接站起、空中同实例不重复 / 新实例可追击、
倒地与起身期间不能行动、起身归零帧可行动、连段与击飞兼容、击飞致死当帧 KO、非法状态组合拒绝、
全流程结束后无脏数据、1200 帧确定性长跑。

受 07C 影响而<b>按新契约更新</b>的既有测试 2 项（非削弱）：
`CombatSettlementTests.AirborneDefender_SettlementSkipped` → `..._EntersAirHitstun_...`
（06B 的「空中被击跳过」已被 07C 取代，改为断言伤害结算 + 保持轨迹）；
`FighterStateValidationTests` 的未定义枚举取样值 9 → 12（0..11 现在都是合法枚举）。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **621/621 通过** |
| 四构建 | 均 0 警告 / 0 错误 |
| Godot 图形运行 | `shots/p07c_knockback.png`（5A 命中后 P2 被推离到 125）、
  `shots/p07c_airhitstun.png`（P1 `AirHitstun`，Y=5 离地）、
  `shots/p07c_knockdown.png`（P1 `Knockdown(10)`，受击框降到贴地高度）、
  `shots/p07c_wakeup.png`（P1 `Wakeup(4)`）；`Combo: P1 1 by P2` 同时可见 |
| 既有 605 项 | 除上述 2 项按新契约更新外全部通过（无删除/跳过） |

**演示数据**：Kai 5A 击退 2/摩擦 0.5、防御击退 1.5；Kai 623A 升龙击飞（垂直 6、水平 1.5、
倒地 12/起身 8）；Rei BackDraft 击退 3；Rei 214B 扫腿改为「扫倒」（垂直 2.5、倒地 12/起身 8）。
其中 623A 的三段方向（F→D→DF）无法用注入方式确定性构造（识别器要求严格连续，K20），
图形验收因此使用 P2 的 214B 展示完整击飞→倒地→起身链路；623A 的击飞由单元测试覆盖。

---

## 阶段 07 最终专项补修（空中受击推挤与防御击退配置，2026-11-07）—— D94/D95

### 修复前的真实缺陷（先复现再改）

| # | 缺陷 | 复现实测 |
|---|---|---|
| P1-A | `ApplySeparation` 的空中早退与 `bothGroundAtFrameStart` 都只判断 `Airborne`，07C 新增的 `AirHitstun` 被当成地面角色 | 重叠不推挤场景：P1（AirHitstun, x=0）被推成 **−15**（期望 0）、P2 由 30 → **45**；越身场景：候选 15 被回投成 **−22.5**（期望 15），镜像 **22.5**（期望 −15）；落地建序：期望 35 / −25，实际 **−25 / 35** |
| P1-B | `AttackDefinition` 只校验 `GroundKnockback` 与摩擦的搭配，`BlockKnockback` 也产生速度却不受约束 | `groundKnockback:0, blockKnockback:4, friction:0` 构造通过；真实 `Step` 在防御结算抛 `InvalidOperationException：角色 1 处于 Blockstun，击退速度 4 与摩擦 0 必须同时为零或同时非零。` |

### 修复（最小改动）

1. **D94**：`FighterLogicStateExtensions.IsAirborneLike()` = `Airborne | AirHitstun`，作为唯一空中判定；
   `Step` 的 `bothGroundAtFrameStart` 与 `ApplySeparation` 早退共用它（`ResolveSettlement` 的
   `defenderAirborne` 也改用它，消除重复列表）。判定只看逻辑状态，**不看 `PositionY`**
   （击飞当帧 Y=0 且 vy>0，D93）。D70 场地边界照旧作用于空中角色。
2. **D95**：`AttackDefinition` 构造期改为
   `(GroundKnockback > 0 || BlockKnockback > 0) && Friction <= 0 → ArgumentException`；
   `GroundKnockbackFriction` 明确为「地面击退通用摩擦」，命中与防御击退共用。
   不放宽 `FighterState.Validate`，不新增第二个摩擦字段。

### 测试与验收

新增 **25 项**（621 → **646**）：
`AirSeparationTests`（12）——空中受击 vs 站立/硬直/双方空中均不推挤、左→右与右→左越身、
落地转 Knockdown、落地建立新顺序（35 / −25，间距恰 60）、落地后不能穿回、普通 `Airborne` 越身不变、
地面击退仍禁穿越（−22.5 / 37.5）、空中仍受场地边界钳制（640）、同输入逐位一致；
`BlockKnockbackTests`（13）——构造期接受/拒绝矩阵（0/4、4/0、4/4、0/0）、
真实 `Step` 防御不扣血 + 进 Blockstun + 速度 4/摩擦 1、逐帧 4/3/2/1/0（累计 10）、
镜像方向（−4）、贴墙钳制且不穿越、硬直结束无残留、StepExact 与 Advance 一致、中间快照续跑一致。

**变异验证**：临时把两处判断改回「只排除 Airborne」→ `AirSeparationTests` **7 项失败**；
临时把构造校验改回只查 `GroundKnockback` → `BlockKnockbackTests` **1 项失败**。均已恢复源码。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug / Release） | **646/646 通过**（0 失败 0 跳过） |
| 四构建 | BattleCore / Godot × Debug / Release 均 0 警告 0 错误 |
| Godot 无头导入 | `[ DONE ]`，无错误 |
| Godot 图形运行 | `shots/p07fix_air_cross.png`（逻辑 117：P1 `AirHitstun` (120, 16.5)，已在 P2 (118.25) 右侧）、
`shots/p07fix_air_landing.png`（逻辑 120：落地后 P1 154.13 / P2 92.63，新顺序建立）、
`shots/p07fix_block_knockback.png`（逻辑 118：`Blockstun(6)`、P2 Vel 0.5、HP 1000/1000）、
`shots/p07fix_block_decay_done.png`（逻辑 121：`Blockstun(4)`、Vel 0）、
`shots/p07fix_wall_corner.png`（持续顶墙 680 帧后 **580 / 640**）、
`shots/p07fix_jump.png`（普通跳跃 `Airborne` (−69, 34) Jump HighJump 不变） |
| 既有 621 项 | 全部通过，未修改、未删除、未削弱 |

**残留风险**：空中受击与地面角色重叠时不再推挤，因此视觉上可以完全重合（本阶段无 Pushbox 碰撞，
符合「空中允许越身」的既有语义）；击退摩擦仍只有一套配置，命中与防御需要不同摩擦时必须另立决策。

---

## 阶段 07 总结（2026-11-07）—— 正式结项

| 子阶段 | 结果 | 测试 | 提交 |
|---|---|---|---|
| 07A | KO 与回合终局（D89） | 566 → 583 | `9848744` |
| 07B | 连段与招式取消（D90/D91） | 583 → 605 | `d3e4ddc` |
| 07C | 击退/击飞/空中受击/倒地起身（D92/D93） | 605 → 621 | `d567f53` |
| 07 补修 | 空中受击推挤资格 + 防御击退摩擦（D94/D95） | 621 → 646 | 见本轮推送 |

**未完成 / 明确留到后续**：三局两胜与回合计时、选人界面、KO 演出与音效、空中防御、
受身、浮空连段限制、击退撞墙反弹、起身无敌、Hitstop、投技、飞行道具、AI、联机回滚、
招式数据外置（JSON）。

**阶段 08 安全入口（按依赖顺序建议）**：① 比赛规则（多回合 / 计时 / 选人）——建立在 D89 的
`RoundState` 之上，只需外层循环，不动内核；② Hitstop / 打击感（受击双方短暂冻结帧数，
需明确冻结范围与事件顺序）；③ 投技与抓取（新命令 + 近距离判定 + 强制位移，需复用 D92/D93 的状态簇）；
④ 飞行道具（独立实体集合进入快照，事件流扩展）；⑤ 招式数据外置 JSON（与规则解耦，可并行）；
⑥ 联机/回滚（最后，需先做序列化与状态校验）。

---

## 下一阶段计划（阶段 06 已结项；本节的入口列表保留作历史参考）

> **本节的「阶段 06 入口」已于 2026-11-07 全部完成（D76–D83）。** 阶段 07 推荐顺序见上一节末尾。

**进入前必须先读** `ARCHITECTURE.md` 第 4 节（D1–D72 全部决策）与第 5 节。

阶段 05 已正式结项（Q16.16 定点数 D63、运动配置 D64/D66、地面运动 D65、起跳/分类 D67/D68、
空中积分与落地 D69、场地边界 D70、双人推挤 D71、自动朝向 D72）。后续阶段的自然入口：

1. **攻击判定**：Startup/Active/Recovery 时间轴已就绪（阶段 04）；判定框几何
   （AABB vs OBB，ARCHITECTURE 第 5 节问题 5 仍未决定）+ 命中/伤害结算 +
   事件流（`(frame, eventId)` 契约，第 5 节问题 6）是阶段 06 的核心入口。
   推挤模型（D71）可扩展为身体判定框（pushbox）。
2. **招式数据外置**：`MoveDefinition`/`MovementDefinition` 目前是 C# 配置；
   角色专属数据量大后可外置 JSON（识别器与状态机不感知数据来源，第 5 节问题 7）。
3. **序列化快照**：`StateVersion` 已到 4，若做录像/回滚需先设计序列化格式（K8）。

**明确不需要**：
- 不需要修改识别器核心（`RecognitionState` 已稳定）；
- 不需要提前设计连段/取消规则（依赖判定与结算先行）。

**注意**：若要「后输入优先」SOCD，必须先扩展 `FrameInput` 记录事件顺序（D27 仍有效）。

> 阶段 05 已连同 05B 结项。接手后先看 `AGENT_HANDOFF.md` 第 6 节，运行 `dotnet test`
> 确认 **722** 项通过再继续。

## 阶段 08A 成果（命中停顿 Hitstop，2026-11-08）—— D96

### 核心规则

1. **数据**：`AttackDefinition.HitstopFrames`（0..30，构造期校验；超上限抛
   `ArgumentOutOfRangeException`）。命中与成功防御<b>共用</b>同一数值，攻击方与被击方同值。
2. **精确时间线**：命中/防御当帧照常结算；停顿从<b>下一逻辑帧</b>起冻结后续 N 帧，
   第 N+1 帧恢复。N 帧 = N 个冻结帧（1/3/8 帧都有逐帧测试）。
3. **冻结范围**：动作阶段倒计时、硬直/防御硬直计时、击退速度与摩擦、空中积分（重力）、
   落地推进、取消窗口、新招式消费；<b>不冻结</b>全局帧号、BattleSession 记账、输入识别
   （缓冲继续记录但不消费）、渲染与事件投递。
4. **动作有效时间**：`FighterState.MoveElapsedFrames`（发动/取消当帧 0，未冻结帧 +1）。
   取消窗口（D91）与 HUD 都用它；无停顿时恒等于 `Frame − MoveStartedFrame`（旧契约逐位不变）。
5. **互中**：同帧双方各自命中时两人都取<b>最大值</b>冻结，不存在 P1 先手。
6. **KO**：仍在命中当帧判定（D89），终局冻结优先于停顿；KO 后不再扣血、不重复事件。
7. **事件**：停顿不产生新事件；`AttackActive` 快照显式跳过冻结角色（否则首个生效帧会重复发事件）。

### 既有测试的契约更新（4 项，均为「1 帧 = 1 动作帧」假设的必然变化）

演示配置 `DemoRoster` 的 5A 现在配置 `hitstopFrames: 4`，因此以下 4 条的时序断言整体后移 4 帧，
并补上「冻结期间硬直不消耗」的断言（不是简单改数字）：

| 测试 | 原断言 | 新断言 |
|---|---|---|
| `HitDetectionTests.Step_5A_Timeline_ContactsExactlyOnActiveFrames` | Active 接触在帧 3–4、Recovery 从帧 5 | 接触在帧 3–8（帧 4–7 为冻结帧，仍处 Active），Recovery 从帧 9 |
| `CombatEventTests.MultiFrameActive_HitsOnlyOnce` | 帧 4 是第二 Active 帧、硬直 11 | 帧 8 是第二 Active 帧、硬直 11；帧 4–7 冻结且硬直恒为 12 |
| `CombatEventTests.BlockedAttack_AlsoConsumed_NoDoubleGuardSettlement` | 同上（防御硬直 7） | 同上（帧 8，Blockstun 7；帧 4–7 恒为 8） |
| `CombatEventTests.NewInstance_CanHitAgain_AfterPreviousResolved` | 第一次 5A 帧 11 收招、第二次帧 15 命中 | 帧 15 收招、帧 19 命中 |

### 测试与验收

新增 **17 项**（646 → **663**）：`HitstopTests` —— 0 帧不改变行为、1/3/8 帧精确冻结、
冻结期间动作/硬直/位置/击退速度全不动、冻结期间按键不发动招式、互中取最大值、
防御也冻结、空中受击不受重力、不重复扣血、KO 当帧成立、取消窗口跨停顿仍有效
（含变异验证：改回全局帧差 → 该测试失败）、无停顿时动作时间等于帧差、冻结帧不重复
`AttackActive`、中间快照重放、StepExact/Advance 一致、构造期与状态校验。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **663/663 通过** |
| Godot 图形 | `shots/p08a_hitstop_hit.png`（逻辑 119：P2 HP 970、`Hitstun(12) HITSTOP(2)`、
P2 速度 2 但位置冻结在 138）、`shots/p08a_hitstop_block.png`（`Blockstun(8) HITSTOP(3)`、防御击退 1.5 未推进） |
| 版本 | StateVersion/CoreVersion 9 → 10（仅格式边界，未实现序列化迁移） |

**已知限制**：停顿只由「打中/被防御」触发（投技/相杀/飞行道具在后续子阶段接入）；
识别缓冲仍按逻辑帧计时，超长停顿可能让过早按下的指令过期。

## 阶段 08B 成果（地面投技，2026-11-08）—— D97

### 核心规则

1. **数据**：`ThrowDefinition`（`ThrowId` / `Range` / 生效帧窗口 / 伤害 / 倒地帧 / 起身帧 /
   平移距离 / 停顿帧 / 可投状态类别），挂在 `MoveDefinition.Throws`。构造期校验范围上限、
   帧窗口（须在 Active 内）、同招式内 ThrowId 唯一。
2. **判定**：投技者 Active + 窗口内 + 目标状态可投 + 目标在<b>面前</b> + 水平距离 ≤ 范围。
   **不是** Hitbox×Hurtbox，也<b>不看防御</b>（站防/蹲防都挡不住投技）。
3. **不可投**：PreJump / Airborne / AirHitstun / Knockdown / Wakeup —— 由 `AllowsTarget`
   按规则拒绝，与「有没有 Hurtbox」无关。
4. **冲突唯一表**：投技者被有效打击命中 → 打断；目标被有效打击命中 → 抓不住；
   双方同帧互相抓 → **都不成立**（`ThrowClash`，无伤害）；否则成立。
5. **结果**：扣血 → `Knockdown(12)` → `Wakeup(8)` → 待机；沿「远离投技者」平移 20，
   并显式按 D70 边界与 D73 最小间距钳制；成功后双方按 `HitstopFrames` 冻结（D96）。
6. **去重**：`LastThrowStartFrame`（与 D81 同构），同一实例对同一目标只结算一次。
7. **事件**：`ThrowAttempt`（每实例首个生效帧一次，兼作挥空证据）、`ThrowHit`
   （Amount = 实扣伤害）、`ThrowClash`（每帧至多一次）。
8. **DoubleKO 不可达**：互投必定冲突、单方投技会被对方有效打击打断，因此投技不可能与
   另一次致命伤害同帧同时成立（已写入 D97）。

### 演示数据

Kai 与 Rei 各新增 `5D`（裸重脚键 → `StandingThrow`）：Startup 2 / Active 2 / Recovery 8、
范围 70、伤害 60、倒地 12 / 起身 8、平移 20、停顿 6。**不占用任何已有指令**，
既有识别测试全绿。

### 测试与验收

新增 **20 项**（663 → **683**）：`ThrowTests` —— P1/P2 双向抓取、镜像方向、
范围内外与临界（70 成功 / 71 失败）、背后目标不可抓、站防/蹲防被投、空中与倒地不可投、
同实例去重、双方同帧互投冲突（无伤害 + 一条 ThrowClash）、打击与投技同帧（打击胜）、
贴墙平移钳制、投技 KO（Amount = 实扣 30）、投技停顿冻结双方、中间快照重放、
`ThrowAttempt` 每实例一次、ThrowDefinition/MoveDefinition 构造期校验。
**变异验证**：把互投改成「P1 固定胜出」→ `MutualThrows_SameFrame_ClashWithoutDamage` 失败（已恢复）。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **683/683 通过**（既有 663 项零改动） |
| Godot 图形 | `shots/p08b_throw_hit.png`（逻辑 115：P2 HP **940**、`Knockdown(12) HITSTOP(6)`、
P2 位置 137→**157**（+20 平移）、`Event: ThrowHit P1→P2 StandingThrow 60`）、
`shots/p08b_throw_clash.png`（双方 `StandingThrow / Active 1`、HP 均 1000、`Event: ThrowClash P1→P2`） |
| 版本 | StateVersion/CoreVersion 10 → 11（仅格式边界） |

**已知限制**：只做地面普通投（无空投、无拆投、无连续投、无投技等级/霸体）；
投技位移是一次性放置（不做抛投轨迹）；投技不产生 `Hit` 事件（用 `ThrowHit` 区分来源）。

## 阶段 08C 成果（攻击相杀，2026-11-08）—— D98

### 核心规则

1. **几何**：`FighterBoxes.HitboxesOverlap` —— 两侧都在 Active、攻击框用 `FixedRect.Overlaps`
   （D76 正面积）判定，逐个配对检查，**仅边缘接触不算**。
2. **触发条件**：攻击框重叠 **且双方都没有用该实例打中对方受击框**。任一方真正命中 → 按 D80
   正常结算（互中/单方命中），**不判相杀**。同一帧不可能既相杀又命中。
3. **粒度**：攻击实例级。任一框对重叠 → 整实例相杀；任一框真正命中 → 按命中结算（不相杀）。
4. **效果**：双方中断进入本招式的 `Recovery`（`PhaseRemainingFrames = RecoveryFrames`）、
   确认帧清空、无伤害；停顿取双方 `HitstopFrames` 最大值。进入 Recovery 后不再产生攻击框，
   同一实例不会重复相杀；走完回待机可再次行动/再次相杀。
5. **事件**：`StrikeClash`（Source=0 / Target=1 / MoveId=null）每帧至多一次。
6. **兼容**：相杀同时打断投技尝试；相杀不产生伤害因此不会触发 KO；
   D80 的互中语义在「真正命中」时逐位保持不变。

### 测试与验收

新增 **15 项**（683 → **698**）：`StrikeClashTests` —— 中距离相杀（无伤害、双方 Recovery）、
近距离互中不算相杀（D80 保持）、边缘相接不算（d=130）、重叠 1 单位算（d=129）、
单方出招不相杀、不同生效帧不相杀、同实例不重复相杀、相杀后可再次行动、新实例可再次相杀、
相杀停顿、相杀不触发 KO、多攻击框（一框相交一框命中 → 判命中；两框都够不到 → 相杀）、
镜像对称、中间快照重放。
**变异验证**：去掉「双方都没命中」条件（有重叠就相杀）→ 2 项测试失败（已恢复）。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **698/698 通过**（既有 683 项零改动） |
| Godot 图形 | `shots/p08c_strike_clash.png`（逻辑 114：双方均为 `Recovery HITSTOP(4)`、
HP 均 1000/1000、`Event: StrikeClash P1→P2 (id=1808)`；P1 `StandLightPunch / Recovery 6`、
P2 `BackDraft / Recovery 7`） |
| 版本 | CoreVersion 11 → 12（规则变化）；**StateVersion 保持 11**（快照格式未变） |

**已知限制**：不做武器相杀等级 / 耐久 / 相杀专用招式；相杀只发生在「双方都没打中身体」，
双方都命中时仍是 D80 互中（这是显式规则，不是遗漏）。

## 阶段 08D 成果（飞行道具，2026-11-08）—— D99 —— 阶段 08 结项

### 核心规则

1. **实体**：`ProjectileState`（Id / Owner / TypeId / SourceMoveId / 位置 / 速度 / 剩余寿命）住在
   `BattleState.Projectiles`，构造期复制 + 只读；`NextProjectileId` 单调分配（溢出显式抛异常）。
2. **数据**：`ProjectileDefinition` 复用 `AttackDefinition` 作命中载荷——
   `OffsetForward/Up` = 出生偏移、`HalfWidth/Height` = 碰撞盒尺寸、`Height` 走 D77 防御表，
   伤害/硬直/击退/击飞/停顿与近战同源；另有速度 / 寿命 / 同类型上限 / 出生帧。
3. **时间线**：Active 阶段经过帧 == 出生帧的那一帧**只建立实体**（不运动、不判定），
   下一逻辑帧开始移动与命中。
4. **阶段顺序**：近战结算 → 投技 → 飞行道具 → KO。每帧：已有实体运动/寿命/越界 →
   P×P 对撞（不同发射者、正面积 → 双方消失）→ P×F 命中/防御（命中即消失，每目标每帧至多一枚）
   → 新实体入列。
5. **独立生命周期**：实体只读自身快照，发射者收招/被击中/被投中都不影响它。
6. **兼容**：命中走同一套防御/击退/击飞/实扣伤害/KO；停顿只作用于被击者（不冻结发射者）；
   飞行道具与投技/相杀/近战同帧时按上述固定顺序仲裁。

### 测试与验收

新增 **24 项**（698 → **722**）：`ProjectileTests` —— 生成实体字段/出生点/朝向镜像、
生成当帧不运动不判定、逐帧匀速、寿命精确到期、出界删除、发射者回待机后仍在飞、数量上限（1/2）、
同帧双方发射各自分配 ID、命中站立/空中/击飞、被防御、不命中发射者、投掷物 KO、
双弹对撞（双方消失、无伤害、一条事件）、同发射者不互撞、快照只读（不可强转 List/数组）、
BattleState 拒绝重复 ID 与非法分配器、定义与招式的构造期校验、**10000 帧确定性长跑**、中间快照重放。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **722/722 通过**（既有 698 项零改动） |
| Godot 图形 | `shots/p08d_projectile_flight.png`（`Projectiles: 1 [Fireball@P1 (33, 60) life=72]` + 橙色碰撞盒）、
`shots/p08d_projectile_hit.png`（P2 HP **960**、`Hitstun(14) HITSTOP(1)`、`Projectiles: -`）、
`shots/p08d_projectile_clash.png`（双方 Recovery、HP 均 1000、`Event: ProjectileClash P1→P2 (id=400)`） |
| 版本 | StateVersion 11 → 12、CoreVersion 12 → 13 |

**已知限制**：不做飞行道具被普通攻击打掉（P×Hitbox 显式不结算）、追踪/反弹/耐久/等级、
多段弹幕与大型特效；生成不单独发事件（实体在快照里，HUD 直接读）。

---

## 阶段 08 最终专项补修成果（2026-11-08）—— D100–D103 —— 阶段 08 最终结项

### 开始前检查

- `git status --porcelain` 空、分支 `main`、本地 HEAD = `origin/main` = `6396cf2b83328c720493fcd441c6ed101d8b84d0`。
- `dotnet test -c Debug` 基线 **722/722**；`git fetch origin` 后远程无新提交，**四项问题远程均未修复**，不存在重复修改或其他开发者的并行改动。
- 已按要求阅读 `ARCHITECTURE.md` / `PROGRESS.md` / `AGENT_HANDOFF.md` 与全部指定源码、六个相关测试文件。

### 四项疑点：全部真实复现（无「审查疑点但不成立」项）

| 编号 | 复现结论 | 修复前实测 |
|---|---|---|
| P1-A | **成立**。普通命中、击飞、被投三条中断路径都残留掩码 | `InvalidOperationException: 角色 0 处于非动作状态 Hitstun / AirHitstun / Knockdown 但携带投掷物生成掩码 1（必须为 0）`，抛出于 `FighterState.Validate` ← `BattleState..ctor` ← `BattleSimulation.Step` |
| P1-B | **成立**。冻结角色被 D73 对称分摊推动 | 单方冻结 `P1: 0 → −2`；镜像 `P2: 0 → +2`；墙角 `P1: 580 → 578`；击退场景 `0 → −2`；冻结末帧 `0 → −2` |
| P2-A | **成立**。首个 Active 帧相杀时两条 AttackActive 全丢 | `AttackActive` 期望 2 实际 **0**；实际事件集合 = `[MoveStarted(PokeA), MoveStarted(PokeB), StrikeClash]` |
| P2-B | **成立**。防御分支跳过停顿写入 | `HitstopRemainingFrames` 期望 3 实际 **0**（n=1/3/8 全为 0）；Blockstun 恢复帧数期望 11 实际 **8**（提前 3 帧）；场景 E 期望 5 实际 0 |

**失败复现**：先写 38 项专项测试并运行，修复前 **21 失败 / 17 通过**（`repro_before.txt`），逐项记录实测值后才改源码。

### 采用的修复（最小改动，均在 `BattleSimulation.cs`）

1. **D100**：新增 `NormalizeSpawnMask(fighter, 帧开始 MoveStartedFrame)`，在 `StepProjectiles` 之后、连段收尾之前统一核账；只改 `FighterState` 的掩码，不碰实体 / ID / 分配器，不放宽 `Validate`。
2. **D101**：`ApplySeparation` 增加 `frozenThisFrame` 参数与「固定锚点」分支；都不冻结时原求解器逐位不变。
3. **D102**：把 `CollectAttackActive` / `CollectThrowAttempts` 移到 `ApplyStrikeClash` **之前**，事件组装仍只读快照。
4. **D103**：新增 `ApplyRemoteHitstop` 私有入口，命中与成功防御两个分支共用；`Math.Max` 保留更长停顿。

**版本**：StateVersion 12 / CoreVersion 13 **不变**（快照格式未变，只修正结算与事件顺序）。

### 测试与验收

新增 **41 项**（722 → **763**），原有 **722 项全部保留、零改动、零跳过**：

| 文件 | 项数 | 覆盖 |
|---|---|---|
| `Stage08EdgeCaseTests.cs` | 16 | P1-A 三条中断路径 / Active→Recovery / 回待机 / 取消到新实例 / 新实例重发 / 旧实例不重发 / 实体独立存活 / 中间快照重放 / Validate 未放宽；P1-B 单方冻结 / 镜像 / 双方冻结 / 墙角 / 空中冻结 / 击退 / 冻结结束恢复 / 无冻结回归 |
| `Stage08EventTests.cs` | 22（含 `BlockedHitstop_ExactlyNFrames` 的 0/1/3/8 四个 case） | P2-A 首帧相杀 / 第二 Active 帧 / 不同 MoveId / 相杀后停顿 / Recovery / 正常互中 / 错开窗口 / 镜像 / 重放 / 事件顺序与 EventId；P2-B 命中与防御停顿 / 0/1/3/8 帧 / 发射者不冻结 / 已有更长停顿 / 输入缓冲 / Blockstun 结束 / 实扣伤害 / 重放；场景 E / F（近战 DoubleKO、飞行道具 DoubleKO、终局冻结、事件提交后投递） |
| `Stage08AcceptanceTests.cs` | 3 | 不同渲染帧率（60fps ×10 vs 30fps ×5）一致性、`StepExact`/`Advance` 一致性、含相杀+Hitstop 的 **10000 帧确定性长跑**（EventId 全局唯一） |

**变异验证**（临时回退 → 对应专项测试必须失败 → 恢复并逐字节校验）：

| 变异 | 结果 |
|---|---|
| M1 冻结锚点改回对称分摊 | `P1B_OneFrozenOneMoving_FrozenKeepsX` 失败 ✅ |
| M2 相杀改回快照之前 | `P2A_FirstActiveFrameClash_EmitsBothAttackActive` 失败 ✅ |
| M3 `NormalizeSpawnMask` 变成空操作 | `P1A_EmitThenStruck_MaskCleared_EntityStillFlies` 失败 ✅ |
| M4 防御分支去掉 `ApplyRemoteHitstop` | `P2B_ProjectileBlocked_AppliesHitstop_ThreeFrameTimeline` 失败 ✅ |

`RESTORED=True`、`ALL_MUTATIONS_CAUGHT=True`，源码已完整恢复。

| 验收项 | 实际结果 |
|---|---|
| `dotnet test -c Debug` / `-c Release` | **763/763** / **763/763**，0 失败 0 跳过 |
| 四构建（BattleCore Debug/Release、NewKOF Debug/Release） | 全部 **0 警告 0 错误** |
| Godot 无头导入 | `--headless --import` → `[ DONE ] first_scan_filesystem` / `update_scripts_classes` / `loading_editor_layout` |
| Godot 实际图形运行 | Vulkan Forward+ / RTX 3060；`--maxfps=60` + 键位注入 P1 `5C` 发弹、P2 按住防御 → 截图 `shots/p08fix_projectile_block.png`（逻辑帧 121，P2 HP 仍 1000 = 防御成功、弹已消失、Round InProgress）、`shots/p08fix_block_hitstop.png`（逻辑帧 80）。启动期有 1 条 `帧积压超过上限 5，已丢弃 4 个逻辑帧` 警告（调度器既有保护，非本轮改动、非错误） |
| 10000 帧确定性 / 中间快照 / EventId 唯一性 | 全通过；两次独立运行状态与事件序列逐位一致，EventId 无重复无回绕 |
| 不同渲染帧率 / `StepExact` vs `Advance` | 全通过（冻结 + 推挤组合下逐位一致） |
| 旧阶段 05–08 关键回归 | 722 项既有测试全部通过；D73 高速防穿越、墙角、1 raw 残差、AirHitstun 越身、D94 资格、Hitstop 精确 N 帧、击退摩擦冻结、起跳落地冻结、近战/投掷物 DoubleKO 均未回归 |
| **重大回归** | **无** |

**仍存在的限制（如实记录，不虚报）**：

1. D101 的固定锚点只作用于 `ApplySeparation`（D73 地面最小间距）。投技位移（D97，显式放置）等其他「结算阶段写入 X」的路径不受该锚点约束——冻结角色被投时仍会位移；本轮按任务书范围只处理四项问题，未扩大改动。
2. 人为注入「冻结方贴墙且已无空间」的非法初态属于 D70/D73 约束不可同时满足，D101 只把未冻结方钳到边界、不编造额外位移；正常可达状态不会出现。
3. Godot 截图抓取的是防御发生后的画面，未能定格 HITSTOP 数字本身；逐帧时间线由 `P2B_*` 单元测试钉住，不以截图冒充。
4. 阶段 08 原有的功能边界保持不变（不做拆投 / 空中防御 / 相杀等级 / 飞行道具被打掉 / 招式数据外置 / AI / 联机回滚）。

**结论**：四项问题全部关闭；**阶段 08 正式结项**，阶段 09 可以启动（仍须主人另下指令）。

## 阶段 08 总结（2026-11-08）—— 正式结项

| 子阶段 | 结果 | 测试 | 提交 |
|---|---|---|---|
| 08A | 命中停顿 Hitstop（D96） | 646 → 663 | `d3cd8cd` |
| 08B | 地面投技（D97） | 663 → 683 | `24b4ec0` |
| 08C | 攻击相杀（D98） | 683 → 698 | `38e06bb` |
| 08D | 飞行道具（D99） | 698 → 722 | 见本轮推送 |
| 08 最终补修 | 跨系统边界（D100–D103） | 722 → 763 | 见本轮推送 |

**仍未实现**：投技的拆投/空投/连续投/等级与霸体；相杀等级与武器相杀；飞行道具被攻击打掉、
追踪/反弹/耐久；Hitstop 的逐招式分级与视觉演出；比赛规则（多回合/计时/选人）、KO 演出、
空中防御/受身、AI、联机回滚、招式数据外置 JSON。

**阶段 09 安全入口（按依赖顺序建议）**：① 比赛规则（多回合 / 计时 / 选人）——建在 `RoundState`
与 `BattleSession` 之上，不动内核；② 招式数据外置 JSON（`MoveDefinition` 已包含攻击/取消/投技/
投掷物四类数据，是外置的最佳时机）；③ 防御与受身的扩展（空中防御、拆投）；④ AI（读权威快照即可）；
⑤ 联机/回滚（必须先做序列化与状态校验，`StateVersion` 12 只是格式边界）。

**每完成一个子任务，立即更新本文件与 `AGENT_HANDOFF.md`。**

## 阶段 09A 成果（多回合比赛，2026-11-08）—— D104–D106

### 核心规则

1. **方案比较与选择**：采用**方案一**（`MatchSession` 管理多个 `BattleSession`），每回合独立会话、Frame 与 `Scheduler.TotalSteps` 都从 0 开始；方案二（单一连续会话 + 权威回合重置）会同时触碰帧同步契约、D89 终局冻结与输入提交顺序，破坏面最大，故不采用。内核 `BattleSimulation` / `BattleSession` / `BattleState` / `FixedStepScheduler` **零改动**。
2. **跨回合事件身份**：`MatchEvent` / `MatchEventId` 提供 `(RoundNumber, EventId)` 组合键；明确记录「裸 EventId 只在单回合内唯一、跨回合会重复」，并有专项测试实测重复存在且组合键全比赛唯一。
3. **`MatchRules`**：默认先胜 2 回合 / 最多 5 回合 / 每回合 99 秒（5940 逻辑帧）/ 启用超时；构造期校验 RoundsToWin ≥ 1、MaxRounds ≥ RoundsToWin 且 ≤ 15、计时秒数 1..999。
4. **`MatchState`**：不可变比赛级快照，含阶段（NotStarted / RoundInProgress / BetweenRounds / MatchFinished）、回合序号、双方胜场、已结束回合数、当前回合结果与结束原因、比赛结果、剩余时间、双方选人 ID、`Validate` 完整不变式。
5. **计分**：`RecordRoundResult` 是唯一入口；P1Win/P2Win 各 +1，DoubleKO / 平局不加分但计入已结束回合数；先到 RoundsToWin 立即终局，达 MaxRounds 按胜场比较、相同为 MatchDraw；终局后拒绝新回合、拒绝重复计分。
6. **新回合**：重建 `BattleSession` + `BattleState.CreateDefault()` 等价初态 + 全新 `InputBuffer`；保留规则/胜场/回合序号/选人。过渡与终局阶段不推进任何战斗逻辑帧。

### 测试与验收

新增 **20 项**（763 → **783**）：`MatchTests` —— 默认规则与非法配置、MatchState 状态转换与不变式、
P1/P2 连胜、DoubleKO 不加分、终局不重复加分、5 回合封顶平局、胜场不同时的封顶裁决、
决胜回合、新回合重置角色/飞行道具/输入边沿/命中去重身份、跨回合帧同步、比赛级事件身份、
中间快照重放、重开比赛、返回选人。

| 项 | 结果 |
|---|---|
| `dotnet test`（Debug） | **783/783 通过**（既有 763 项零改动） |
| `dotnet build NewKOF.csproj`（Debug） | **0 警告 0 错误** |
| 版本 | **StateVersion 12 / CoreVersion 13 不变**（09A 纯比赛层，内核快照格式未变） |

**已知限制（09A 当时，09B 已解除计时部分）**：09A 的 `RemainingTimeFrames` 由比赛层按已提交帧数逐帧记账，当时尚未接入内核权威时钟与 Time Over 裁决（09B 已交付并改为只读投影）；09A 的比赛只能由 KO / DoubleKO 结束。

---

## 阶段 09B 成果（回合计时与 Time Over，2026-11-08）—— D107–D109

### 核心规则

1. **权威时钟**：`BattleState.RoundTimerRemainingFrames`（`int?`），由 `BattleSimulation.Step` 每成功提交一个进行中战斗逻辑帧 −1；默认 99 秒 = **5940 帧**。`null` = 旧单回合 API 兼容路径（阶段 01–08 全部 783 项测试零改动通过）。
2. **时间线**：Hitstop 冻结帧照常扣时；Godot 暂停 / 未提交 Step / 帧积压丢弃预算 / 已结束回合 / 过渡阶段都不扣时。比赛层 `MatchState.RemainingTimeFrames` 改为内核字段的只读投影，09A 的比赛层占位记账已移除，**全程只有一套计时**。
3. **KO 优先**：计时归零帧先完成全部结算，若本帧已 KO/DoubleKO 则以 KO 为准、不发 `TimeOver`；双方仍存活才按体力比例裁决。超时不产生任何虚假伤害。
4. **比例裁决**：`P1.Health × P2.MaxHealth` vs `P2.Health × P1.MaxHealth`（`checked long` 交叉相乘），相等 → `TimeOverDraw`。
5. **结果与原因正交**：`RoundOutcome.Draw = 4`、`RoundState.Reason`（KO / DoubleKO / TimeOver / TimeOverDraw）自洽校验；新增 `TimeOver = 16` 事件只在归零帧发一条，事件顺序 `TimeOver|KO → RoundEnded`，`RoundEnded` 恰好一次。

### 测试与验收

新增 **24 项**（783 → **807**）：`RoundTimerTests` —— 5940 帧精确倒计时、逐帧递减、1 帧边界、
null 时钟旧行为兼容、非法时钟值、Hitstop 冻结帧扣时、暂停不扣时、未提交 Step 不扣时、
丢弃预算不多扣、Time Over P1/P2 胜与平局、绝对 HP 不同但比例相同 / 绝对 HP 相同但比例不同、
`TimeOverResolver` 非法输入、KO 与 Time Over 同帧、DoubleKO 与 Time Over 同帧、
超时后不再产生伤害或事件、事件顺序与唯一性、`RoundState` KO/TimeOver 区分、
比赛层新回合计时重置与计分、StepExact/Advance 一致与中间快照重放。

| 项 | 实际结果 |
|---|---|
| `dotnet test`（Debug / Release） | **807/807** / **807/807**，0 失败 0 跳过 |
| 四构建（BattleCore + NewKOF，Debug/Release） | 全部 **0 错误** |
| 版本 | **StateVersion 12 → 13、CoreVersion 13 → 14** |

**尚未支持**：选人界面与完整比赛流程（09C）、JSON 招式数据外置（09D）、正式 KOF 的 3v3 队伍赛、
超时后的「判定胜负动画」与回合演出。计时规则第一版明确采用「按成功提交的逻辑帧递减」，
未实现「Hitstop 期间暂停计时」的另一套时间线（如需改用需另行决策并固定完整时间线）。

---

## 阶段 09C 成果（选人、回合过渡与完整比赛流程，2026-11-08）—— D110–D112

### 核心规则

1. **`FighterCatalog`**：数据驱动角色目录（ID / 显示名 / 配置来源 / 不可变 `FighterDefinition`），ID 唯一、非法选择拒绝、按双方 ID 生成 `FightRoster`、允许镜像角色。09C 由 `DemoRoster` 构建，09D 起接 JSON 加载器。
2. **表现层改造**：`BattleScene` 改为持有 `MatchSession` + `FighterCatalog`，实现选人界面（A/D 与 ←/→ 切换、J 与 `,` 确认、Enter 开赛）、回合过渡（Space 进入下一回合）、比赛终局（F5 重开 / ESC 返回选人）、F1 暂停 / F2 判定框保留。
3. **比赛 HUD**：当前回合、双方角色名、HP、胜场、剩余时间、比赛阶段、回合结果与结束原因、最终比赛结果、跨回合事件身份（`R2:164`）；原有判定框/事件/输入调试显示保留。表现层不计算任何权威结果。
4. **输入隔离**：选人/过渡/终局界面按键在 `_Input` 拦截，不进入战斗输入缓冲；每回合全新 `InputBuffer` + `KeyboardInputMap` + `Scheduler` + `DebugBoxDrawer` 绑定；暂停状态不跨过渡泄漏。
5. **仅命令行调试参数**：`--hp=<n>`（初始血量）、`--match-time=<s>`（回合计时秒数）、`--duel=<d>`（初始半距离，下限 30）。默认满血 1000 / 99 秒 / ±120，正常启动零副作用。

### 测试与验收

新增 **14 项**（807 → **821**）：`FighterCatalogTests` —— 目录唯一性与非法选择、四种选人组合、
镜像角色状态独立、镜像双弹对撞、镜像比赛计分、完整流程（选人→KO→过渡→下一回合→MatchFinished→
F5→ESC→改选 Rei vs Kai）、暂停不跨过渡、跨回合事件身份、确认键不泄漏、Time Over 平局后的过渡。

| 项 | 实际结果 |
|---|---|
| `dotnet test`（Debug / Release） | **821/821** / **821/821** |
| 四构建（BattleCore + NewKOF，Debug/Release） | 全部 **0 错误** |
| Godot 图形实测 | 选人 `p09c_select.png`；Round 1 `p09c_round1.png`；KO 过渡 `p09c_round1_win.png`（`Wins P1 1 : P2 0`、`P1 WIN (KO)`）；比赛终局 `p09c_match_win.png`（`P1 MATCH WIN`、`R2:164`）；镜像 `p09c_mirror.png`（`P1 [Kai] / P2 [Kai]`）；重开+改选 `p09c_rei_vs_kai.png`（`P1 [Rei] / P2 [Kai]`、Wins 0:0）；Time Over `p09c_time_over.png`（`Time: 0s`、`DRAW (TIME OVER DRAW)`、HP 1000/1000 不变、`RoundEnded` 一次） |
| 版本 | **StateVersion 13 / CoreVersion 14 不变** |

**已知限制**：仍是 1v1（非 3v3 队伍赛）；选人界面为调试版（无立绘/动画）；回合过渡需按键确认；
角色配置仍来自 C# `DemoRoster`（09D 外置 JSON 后正常游戏改用外部配置）。

---

## 阶段 09D 成果（角色与招式 JSON 数据外置，2026-11-08）—— D113–D116

### 核心规则

1. **Schema v1**：`schemaVersion` / `fighterId` / `displayName` / `movement` / `commands` / `moves`；
   每条 Move 覆盖 `attacks` / `cancels` / `throws` / `projectiles` 四类能力，不是只导出普通攻击。
   未知字段拒绝、缺失必填拒绝、`schemaVersion != 1` 拒绝。
2. **定点数表示**：所有战斗数值使用 `{ "numerator": n, "denominator": d }` 整数分数，加载时
   `checked` 转 Q16.16；分母必须为正，越界/非法立即失败，不做静默 0 填充。
3. **加载器**：`FighterDataLoader` 纯 C#，接受字符串或 `Stream`，不读文件、不依赖 Godot；
   校验在构造 `FighterDefinition` 之前逐字段给出 `文件:字段路径:原因`，并保留全部原有构造器不变式。
4. **数据迁移**：Kai / Rei 迁到 `data/fighters/Kai.json` / `Rei.json`，与 `DemoRoster` 全覆盖逐字段
   等价（指令、招式帧数、攻击框、伤害、硬直、Hitstop、击退/击飞/倒地、取消、投技、飞行道具、运动）。
   `DemoRoster` 保留为测试夹具与回退路径。
5. **Godot 集成**：`BattleScene` 开赛前经 `FileAccess` 一次性读取 `res://data/fighters/*.json` 构建
   `FighterCatalog`；`export_presets.cfg` 的 `include_filter` 加入 `*.json`，保证导出包内可用。
   运行中的比赛不再读文件。

### 测试与验收

新增 **27 项** `FighterDataTests`（821 → **848**）：Kai/Rei 加载与逐字段等价、JSON 下 5A/236A/623A/
214B/取消/投技/飞行道具/Hitstop/相杀、修改伤害只影响该招式、同 JSON 两次加载一致、同输入逐位一致、
非法 JSON / 版本 / 重复 ID / 缺失引用 / 非法枚举 / 负伤害 / 越界 Hitstop / 非法定点数 / 攻击窗口 /
空必填字段 / 未知字段全部报错并带路径、Stream 与字符串等价、加载器不引用 Godot、目录重复 ID 拒绝。

另新增 **8 项** `Stage09AcceptanceTests`（阶段 09 统一集成）：
场景 A 完整比赛（两回合 → MATCH WIN → 重开保留选人）、场景 B Time Over 比例裁决与单次计分、
场景 C 连续 DoubleKO 不悬置、场景 D 镜像 Kai vs Kai 双弹对撞、场景 E 改 JSON 伤害即改变比赛结果、
10000 帧比赛级确定性（组合事件身份全唯一）、30/60/144fps 相同逻辑帧一致、`StepExact`/`Advance` 整回合等价。

| 项 | 实际结果 |
|---|---|
| `dotnet test`（Debug / Release） | **856/856** / **856/856** |
| 四构建（BattleCore + NewKOF，Debug/Release） | 全部 **0 错误** |
| Godot 无头导入 | `[ DONE ]`（4 个阶段） |
| Godot 图形运行 | `p09d_json_loaded.png`（`Catalog: Kai, Rei`）、`p09d_json_fight.png`（P2 `970/1000`、`Hitstun(5)`、`Combo 1`，与 JSON 的 30 点伤害一致） |
| 版本 | **StateVersion 13 / CoreVersion 14 不变**（只新增数据加载层，不改快照格式） |

**已知限制**：导出模板未安装，未做真实导出包实测（`include_filter` 已配置并记录）；
JSON 只覆盖角色与招式数据，不含动画/音效/资源引用；尚无跨版本迁移（schema v1 only）。

---

## 阶段 09 最终专项补修（2026-11-08）—— D117–D119

审查基线 `76c1b203`（856 项，HEAD = origin/main，工作区干净）。三项审查疑点
**全部真实复现**：先写 52 项失败复现测试，修复前 **28 失败 / 24 通过**，再逐项最小修复。

### P1-A 批量推进越过回合终局（D117）

修复前实测（5 帧预算）：第 2 帧 KO → `Advance` 返回 **5**（期望 2）；第 1 帧 KO → **5**（期望 1）；
第 2 帧 DoubleKO → **5**（期望 2）；第 3 帧 Time Over → **5**（期望 3）。多出来的帧是 D89 的终局冻结帧，
它们把 `LastEvents` 覆盖成空列表，并让 `StepExact` 与 `Advance` 的终局帧号不一致。

修复：`BattleSession.Advance` 增加提交后终止谓词；`MatchSession.Advance` 传
`() => !State.IsRoundLive`。提交 2 帧后立即停止，第 3–5 帧既不 `Prepare` 也不 `Commit`，
`DroppedStepCount = 3`、`TotalSteps = 2`、`IsFrameSyncIntact = true`；
直接驱动单回合 `BattleSession.Advance(5 帧)` 仍提交 5 帧（D89 不变）。

### P1-B 比赛事件早于权威状态（D118）

修复前实测：`RoundEnded` 观察者读到 `Phase = RoundInProgress`、`P1Wins = 0`、`RoundsPlayed = 0`；
Time Over 的观察者读到 `Reason = None`；观察者抛异常时 `P1Wins` 停在 **0**（计分被事件投递挡住）。

修复：`OnFrameCommitted` 改为「计时投影 → 唯一一次计分 → `Validate` → 包装并投递」。
现在观察者读到 `BetweenRounds/MatchFinished`、胜场与回合数已加、剩余时间与已提交 `BattleState` 一致；
异常时计分保留、异常原样上抛、`BattleSession` 故障封闭、不二次计分、不重发事件。
`_publishing` 期间对 `StartNextRound` / `RestartMatch` / `ReturnToSelection` 等入口显式抛
`InvalidOperationException`（回调重入拒绝）。

### P2-A 结果/原因组合不自洽（D119）

修复前实测：`RecordRoundResult(DoubleKO, Knockout)`、`(Draw, Knockout)`、`(P1Win, DoubleKO)`、
`(P2Win, TimeOverDraw)`、`(DoubleKO, TimeOver)` 等 **9 种非法组合全部被接受**（「未抛出异常」）；
`MatchState.Validate()` 对反射构造的非法状态同样不报错。

修复：`RoundState.IsValidOutcomeReasonPair` 作为唯一权威谓词，`RoundState` 与 `MatchState`
的入口与自检共用。合法六种组合（P1Win/P2Win × KO/TimeOver、DoubleKO + DoubleKO、
Draw + TimeOverDraw）保持可用；未定义枚举仍抛 `ArgumentOutOfRangeException`。

### 测试与验收

新增 **52 项** `Stage09EdgeCaseTests`（12 项 P1-A、8 项 P1-B、22 项 P2-A、其余为跨系统场景）。
既有 **856 项零改动零跳过**。变异验证 **3/3 捕获**：移除终止谓词 → 5 项失败；
恢复「先发事件再计分」→ 10 项失败；移除共享组合校验 → 13 项失败；三次均 `RESTORED=True`。

| 项 | 实际结果 |
|---|---|
| `dotnet test`（Debug / Release） | **908/908** / **908/908** |
| 四构建（BattleCore + NewKOF，Debug/Release） | 全部 **0 警告 0 错误** |
| Godot 无头导入 | `[ DONE ]`（无 ERROR / SCRIPT ERROR） |
| Godot 图形运行 | `p09fix_round1_win.png`（`Phase: BetweenRounds`、`Wins 1:0`、`ROUND 1 WINNER`、`Logic Frame 13`）、`p09fix_match_win.png`（`Round 2/5`、`Wins 2:0`、`P1 MATCH WIN`、`Logic Frame 11` 证明下一回合重新从 0 计数）、`p09fix_time_over.png`（`Time 0s`、`DRAW (TIME OVER DRAW)`、双方 1000/1000） |
| 版本 | **StateVersion 13 / CoreVersion 14 不变**（不改快照格式） |

**已知限制**：图形层未能构造出真 DoubleKO 截图——调试注入器在同一渲染帧同时按下 J 与 `,` 时，
`InputBuffer` 只保留后一次采样，P2 的攻击不会成立；DoubleKO 由单元测试
（`BatchAdvance_DoubleKoStopsBatch`、`DoubleKoObserver_SeesCorrectScoring`、
`MatchTests.DoubleKO_AddsNoWins_ButCountsAsPlayedRound`）覆盖，未用截图冒充。
导出模板仍未安装，正式导出包未实测。

---

## 阶段 10A 成果（3v3 队伍赛核心，2026-11-08）—— D120–D122

### 核心规则

- **结构**：`TeamDefinition`（有序三人 + 每人 `MaxHealth`）+ `TeamMatchRules` + `TeamMatchState`
  + `TeamMatchSession`；场上始终 1v1，`BattleSimulation` / `BattleState` / `BattleSession` 一行未改。
- **队员身份**=`(TeamId, SlotIndex)`：允许 Kai / Rei / Kai 同名占位；`FighterCatalog.CreateTeam`
  逐 ID 解析并带出满血值（不假定 1000）。
- **淘汰**：`Eliminated[]` 是唯一存活判据（Time Over 败者 HP > 0 也算淘汰）；KO/TimeOver 淘汰败方，
  DoubleKO/TimeOverDraw 双方各淘汰一名；一方耗尽即定胜负，双方同时耗尽 `TeamDraw`。
- **体力继承**：胜方保留本场实际 HP，新登场队员满血；每场交锋重建 BattleState / InputBuffer /
  Scheduler，招式、硬直、击飞、投技、飞行道具、输入历史一律不继承。
- **事件身份**：`(BoutNumber, EventId)`；`Advance` 沿用 D117 的提交后终止谓词，D118 的
  「先计分 Validate 再发布」顺序与重入拒绝。
- **1v1 兼容**：`MatchSession` / `MatchState` / `MatchRules` 与阶段 09 语义完全不变。

### 测试与验收

新增 **30 项** `TeamMatchTests`：建队与非法配置、槽位身份、KO 接替、体力继承、新队员满血、
一人连挑三人、交替淘汰、DoubleKO、最后一名 DoubleKO → TeamDraw、Time Over 淘汰正 HP 败者、
计时重置、TimeOverDraw 消耗双方、飞行道具不跨场、输入历史不跨场、事件组合身份唯一、
批次边界、观察者看到已计分状态、状态不可变、终局只判一次、镜像对称、同输入逐位一致、
镜像同角色独立状态、重开/返回选队、帧同步不变。

| 项 | 实际结果 |
|---|---|
| `dotnet test`（Debug） | **938/938**（908 基线零改动 + 30 新增） |
| 表现层构建 | 0 警告 0 错误 |
| 版本 | StateVersion 13 / CoreVersion 14 不变（未改任何战斗快照字段） |

**已知限制**：本阶段只有核心与测试，Godot 侧仍是 1v1；队伍选择、接替演出与模式切换在 10B。
导出模板仍未安装，未做导出包实测。

---

## 阶段 09 总结（2026-11-08）—— 正式结项

| 子阶段 | 内容 | 决策 | 测试 | Commit |
|---|---|---|---|---|
| 09A | 多回合比赛与胜场统计 | D104–D106 | 763 → 783 | `f2a756f` |
| 09B | 逻辑帧计时与 Time Over | D107–D109 | 783 → 807 | `b1398da` |
| 09C | 选人、回合过渡与完整比赛流程 | D110–D112 | 807 → 821 | `b0b0b42` |
| 09D | 招式与角色 JSON 数据外置 | D113–D116 | 821 → 848 | `20d668e` |
| 集成 | 场景 A–E + 长跑/帧率/路径一致性 | — | 848 → **856** | 同上 |
| 补修 | 批量推进边界 / 事件时序 / 结果原因一致性 | D117–D119 | 856 → **908** | 本轮 |

**分层结论**：`BattleState` 仍是唯一权威战斗快照；`RoundState` 回答「这一回合谁赢、为什么」；
`MatchState` 回答「打到第几回合、各赢几场、整场谁赢」；Godot 只显示。计时器在 `BattleState` 内，
JSON 只提供不可变配置。旧单回合 API（`BattleSimulation.Step` / `BattleSession`）语义未变。

**仍不支持**：正式 KOF 3v3 队伍赛、空中防御/拆投、AI、联机回滚与 `BattleState` 序列化。
