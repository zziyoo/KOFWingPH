# AGENT_HANDOFF.md — Agent 交接说明

> **每一位 Agent 在修改代码前必须先读本文件，并核对文档与实际代码是否一致。**
> 最后更新：2026-11-07（阶段 05A：Q16.16 定点数 / 地面运动，D63–D65）

---

## 1. 当前项目绝对路径

```
C:\Users\32360\Desktop\NewKOF
```

**远程仓库**：https://github.com/zziyoo/KOFWingPH

| 工程 | 路径 |
|---|---|
| Godot 工程文件 | `C:\Users\32360\Desktop\NewKOF\project.godot` |
| 表现层工程 | `C:\Users\32360\Desktop\NewKOF\NewKOF.csproj` |
| 核心工程 | `C:\Users\32360\Desktop\NewKOF\core\BattleCore\BattleCore.csproj` |
| 测试工程 | `C:\Users\32360\Desktop\NewKOF\tests\BattleCore.Tests\BattleCore.Tests.csproj` |

无 `.sln` 文件，按工程分开构建。

---

## 2. Godot 和 .NET 版本

| 项 | 值 |
|---|---|
| Godot | `4.7.2.stable.mono.official.ed1daf0bf` |
| Godot 控制台版 | `C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe` |
| Godot 图形版 | `C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe` |
| .NET SDK | `10.0.403` |
| 目标框架 | `net8.0` |
| 渲染器 | Forward+ (Vulkan)，实测 `Vulkan 1.4.325` / `NVIDIA GeForce RTX 3060 Laptop GPU` |
| Git | `C:\Users\32360\AppData\Local\GitHubDesktop\app-3.6.10\resources\app\git\cmd\git.exe`（2.56.0，**不在 PATH**） |

> ⚠️ **必须使用 mono/.NET 版 Godot。** 本机桌面另有 `Godot_v4.7.2-stable_win64.exe\`
> （标准版 `4.7.2.stable.official`），**无法编译 C#**。
> 该名称带 `.exe` 后缀但它是**文件夹**，属历史遗留。

> ⚠️ **Git 不在系统 PATH。** 需使用上面的完整路径，或先
> `set PATH=%PATH%;C:\Users\32360\AppData\Local\GitHubDesktop\app-3.6.10\resources\app\git\cmd`

---

## 3. 工程启动及测试命令

### 3.1 编译（核心 + 表现层）

```bat
cd /d C:\Users\32360\Desktop\NewKOF
dotnet build
```
期望：`0 个警告 0 个错误`

### 3.2 运行单元测试（无 GUI 需求）

```bat
cd /d C:\Users\32360\Desktop\NewKOF\tests\BattleCore.Tests
dotnet test
```
期望：`失败: 0，通过: 418，已跳过: 0，总计: 418`

### 3.3 仅编译核心模块（验证核心独立可构建）

```bat
cd /d C:\Users\32360\Desktop\NewKOF\core\BattleCore
dotnet build
```

### 3.4 用 Godot 打开工程（编辑器）

```bat
"C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "C:\Users\32360\Desktop\NewKOF" --editor
```

### 3.5 导入资源（首次或资源变动后）

```bat
cd /d C:\Users\32360\Desktop\NewKOF
"C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path . --import
```

### 3.6 实际运行游戏（图形窗口）

```bat
cd /d C:\Users\32360\Desktop\NewKOF
"C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --path . --quit-after 400
```

### 3.7 无窗口冒烟运行

```bat
cd /d C:\Users\32360\Desktop\NewKOF
"C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path . --quit-after 900
```

### 3.8 自动截图验收

```bat
cd /d C:\Users\32360\Desktop\NewKOF
if not exist shots mkdir shots
"C:\Users\32360\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --path . -- --shot=C:/Users/32360/Desktop/NewKOF/shots/check.png --shotframes=150
```
输出 `[CaptureHelper] 截图已保存：...` 即成功。路径用正斜杠，参数须放在 `--` 之后。

### 3.9 Git 操作

```bat
set GIT=C:\Users\32360\AppData\Local\GitHubDesktop\app-3.6.10\resources\app\git\cmd\git.exe
cd /d C:\Users\32360\Desktop\NewKOF
%GIT% status
%GIT% log --oneline -5
```

---

## 4. 最近完成的修改

### 阶段 05A（2026-11-07）Q16.16 定点数与确定性地面运动（D63–D65）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Numerics/FixedPoint.cs` | **新增（D63）**：Q16.16 定点数（`long` 原始值，Scale = 65536）。加减 `checked`、乘除 `Int128` 中间结果、取整统一向零截断、溢出抛 `OverflowException`、除零抛 `DivideByZeroException`；`int` 隐式转换 = 世界单位；`ToIntTruncated` 只读整数视图；`ToDouble` 仅供显示 |
| `core/BattleCore/Combat/MovementDefinition.cs` | **新增（D64）**：角色运动配置（前/后步行速度大小，> 0 且 ≤ 32 单位/帧校验；默认 2.0/1.5） |
| `core/BattleCore/Combat/GroundMovement.cs` | **新增（D65）**：地面运动纯函数 `StepX`——仅站立待机移动，前后 → 世界 ±，收招同帧恢复步行 |
| `core/BattleCore/State/FighterState.cs` | **D63**：`PositionX/PositionY` 升级为 `FixedPoint` 世界坐标；新增 `PositionXUnits/PositionYUnits` 整数视图；旧命名参数调用点语义不变 |
| `core/BattleCore/Combat/FighterDefinition.cs` | **D64**：新增可选 `movement` 参数（null → 默认配置），`Movement` 恒非 null |
| `core/BattleCore/Combat/DemoRoster.cs` | Kai 前 2.0/后 1.5、Rei 前 1.75/后 1.25 单位/帧（双人数据刻意不同） |
| `core/BattleCore/Simulation/BattleSimulation.cs` | Step 内第 3 步接入地面运动（按 tick 后状态门槛），位置写入新快照 |
| `core/BattleCore/State/BattleState.cs` / `BattleCoreInfo.cs` | StateVersion / CoreVersion 2 → 3（格式边界标记） |
| `scripts/BattleScene.cs` | 显示转换改为定点 → 浮点 + 世界 Y 翻转（`groundY - worldY`）；HUD 新增 P1/P2 Pos 两行（只读权威坐标） |
| `scenes/Battle.tscn` | HUD 面板 264 → 300 高，初始文本 11 行 |
| `tests/BattleCore.Tests/FixedPointTests.cs` | **新增 44 项**：正负转换、分数精确表示、向零截断 pinned、checked 溢出、128 位乘除、舍入边界确定性、累计无漂移、哈希/相等 |
| `tests/BattleCore.Tests/GroundMovementTests.cs` | **新增 31 项**：前后 1/10/60 帧 pinned、镜像反对称、SOCD、蹲下/动作阻挡与恢复、暂停/丢弃预算无位移、Advance vs StepExact、中间快照续跑、3600 帧长跑、配置校验 |

**验收**：418/418（343 基线全部保留）；双构建 0 警告 0 错误；无头导入 `[ DONE ]`；
图形运行三张截图：前进 `(138, 0)`、后退 `(-313.5, 0)`（定点小数逐位命中）、
蹲下 `(-120, 0)` 冻结。**注意（K32）**：Godot 运行时直接加载 `.godot/mono/temp/bin/Debug`
的旧程序集——改代码后必须先 `dotnet build NewKOF.csproj`（Debug）再截图验收，
否则会看到旧行为。

### 阶段 04 最终补修（2026-11-07）快照不可变性 / 非法状态校验（D61/D62）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/RecognitionState.cs` | **D61**：五个公开集合（Directions/Presses/Buffer/ConsumedEndFrames/ChargePendings）在构造点包装为单实例 `ReadOnlyCollection<T>`——修复「`IReadOnlyList` 只是接口视图、底层 List/数组可被强转修改」的权威快照完整性缺陷；内部可变 List 构造流程保留，只在快照落定点包装一次；`Empty` 同样包装（共享安全） |
| `core/BattleCore/State/FighterState.cs` | **D62**：`Validate` 首项用 `Enum.IsDefined` 拒绝未定义 `FighterLogicState`（错误消息含具体非法值），无论其他字段是什么 |
| `core/BattleCore/Combat/FighterStateMachine.cs` | **D62**：`Tick` 入口独立校验未定义状态（不依赖构造代为校验，不被静默「修正」为待机） |
| `tests/BattleCore.Tests/SnapshotImmutabilityTests.cs` | **新增 6 项**：五集合强转/只读接口/内容保持断言、Empty 共享安全、WithResultConsumed 新旧互不污染、旧快照修改尝试后重放仍确定、1000 帧双人隔离、哈希一致且稳定 |
| `tests/BattleCore.Tests/FighterStateValidationTests.cs` | **新增 11 项**：`(999)/(-1)/(5)/(100)` 拒绝含具体值、带 MoveId 字段仍拒绝、BattleState 构造期拒绝、Tick 直接拒绝且不静默修正、合法五态校验与推进、负帧数保持 |

**修复前失败复现**：17 项新测试中 **15 项失败**（P1 五项：Directions 可强转 `List` 被
`Clear()` 等；P2 十项：`(999)` 静默通过构造与推进）。修复后 **343/343 通过**。

### 阶段 04（2026-11-07）角色状态机与招式系统 —— 结项

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/RecognitionState.cs` | **新增（D55）**：不可变的单玩家识别快照（方向历史/按键边沿/蓄力挂起/结果缓冲/消费标记）+ 纯函数 `Advance`——识别规则与 03B 完全一致（含 D54 下界），数据结构改为写时复制；结构化相等支撑跨实例确定性比较 |
| `core/BattleCore/Commands/CommandRecognizer.cs` | 重写为 `RecognitionState` 的双玩家门面（公共 API 不变，267 项既有测试不动地通过） |
| `core/BattleCore/Commands/CommandDefinition.cs` | **D56**：空方向序列 + 攻击键 = 裸按键指令（仅边沿当帧触发）；空 + 无按键仍拒绝 |
| `core/BattleCore/Combat/FighterLogicState.cs` | **新增（D58）**：Standing/Crouching/Startup/Active/Recovery 枚举 + `MoveAllowedStates` |
| `core/BattleCore/Combat/MoveDefinition.cs` | **新增（D59）**：招式定义（MoveId/CommandId/三阶段帧数/允许发动状态），阶段帧数 ≥ 1 校验 |
| `core/BattleCore/Combat/FighterDefinition.cs` | **新增（D59）**：角色专属指令表 + 招式表，构造期校验（重复 ID/未知指令）+ 防御复制 |
| `core/BattleCore/Combat/FightRoster.cs` | **新增（D59）**：双方角色配置（`CreateEmpty` 保持 01–03 默认行为） |
| `core/BattleCore/Combat/DemoRoster.cs` | **新增**：演示角色 Kai/Rei（同 236A 指令 → 不同招式不同帧数；Kai 有裸按键 5A） |
| `core/BattleCore/Combat/FighterStateMachine.cs` | **新增（D58/D60）**：集中纯函数 `Tick`：阶段推进 → 待机更新（B9）→ 消费（FIFO、每结果一次、失败保留） |
| `core/BattleCore/State/FighterState.cs` | **D57**：新增 LogicState/MoveId/PhaseRemainingFrames/Recognition；Validate 强制不变式 |
| `core/BattleCore/State/BattleState.cs` | `CreateDefault` 版本默认 2（D57） |
| `core/BattleCore/Simulation/BattleSimulation.cs` | **D55/D58**：Step 内完成「识别 → 消费 → 状态机推进」（零帧延迟）；接受 `FightRoster` |
| `core/BattleCore/Simulation/BattleSession.cs` | 新增可选 `simulation` 参数（观察者与 Faulted 机制不变） |
| `core/BattleCore/BattleCoreInfo.cs` | CoreVersion 1 → 2 |
| `scripts/BattleScene.cs` | 移除表现层识别器；HUD 九行只读权威状态（State/Move 行新增）；会话改传演示配置 |
| `scenes/Battle.tscn` | HUD 面板扩到 330×264 容纳九行 |
| `tests/BattleCore.Tests/…` | 新增 4 个测试文件 59 项（配置 19/状态机 13/消费 15/集成 12）；`BattleSimulationTests` 反修改断言按 D55 语义更新；`CommandRecognitionTests` 空序列校验按 D56 细化 |

**验收**：326/326；双工程 0 警告 0 错误；无头导入 `[ DONE ]`；图形运行六张截图
（Standing / Crouching+独立 / 5A Startup→Recovery→按住不重复回 Standing / 236A→FireballPrototype），
HUD 全部只读权威状态且与注入日志的推演时间线逐帧吻合。

### 03B 蓄力边界最终补修（2026-11-07）释放后旧方向借用（D54）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/CommandRecognizer.cs` | 蓄力匹配补下界约束 `endFrame < ReleasedAtFrame → continue`（D54）：尾段必须属于本次释放之后，释放前的旧方向条目不得被借用 |
| `tests/BattleCore.Tests/ChargeStaleTailTests.cs` | **新增 10 项**：最小复现（修复前失败）、不足不补、迟按不复活、同帧完成、窗口 1/2/3/4 边界（含旧条目历史）、P1/P2 独立、换边尾段归属 |

**复现证据**：修复前 10 项中 2 项失败（旧 Forward(0) 满足 `0−2=−2 ≤ 窗口` + 按键容差恰好
够到旧条目 → 幽灵完成 46A）；修复后 267/267 通过。

### 阶段 03B-2 最终补修（2026-11-07）长蓄力失效 / 释放窗口 / 双重超时边界 —— 结项

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/CommandRecognizer.cs` | **D51**：每条蓄力指令一个 `ChargePendingState`「释放挂起」槽位（离开蓄力方向时快照蓄力量与释放帧，与历史条目解耦）；蓄力匹配改读挂起状态（历史只需容纳尾段），`MaxSequenceFrames` 不再约束蓄力。**D52**：释放窗口检查（中立/尾段容忍、无关方向立即失效）。**D53**：产生过候选的蓄力指令挂起立即作废（防重复触发） |
| `core/BattleCore/Commands/CommandDefinition.cs` | 新增 `ChargeReleaseToleranceFrames`（默认 3，构造期校验 ≥ 0）；蓄力指令序列至少 2 元素校验；`MaxSequenceFrames` 文档改为「仅普通指令」 |
| `core/BattleCore/Commands/CommandCatalog.cs` | 新增 `DefaultChargeReleaseToleranceFrames = 3`；三条蓄力指令显式传入释放窗口 |
| `scripts/CaptureHelper.cs` | 新增 `--maxfps=` 调试参数（锁定渲染帧率，使注入时序可复现；正常启动零副作用） |
| `tests/BattleCore.Tests/ChargeCommandTests.cs` | +28 项：超长蓄力 29/30/45/60/61/120/600/3600、释放窗口 0/1/2/3/4/20 边界（46A+28B）、SOCD 中立释放、无关方向失效、不足不补、重蓄重置、防重复、独立重蓄、P1/P2、换边、按键容差不延长窗口；修正 1 项测试缺陷（`AfterIndependentRecharge` 忽略 8 帧缓冲有效期） |
| `tests/BattleCore.Tests/DoubleMotionCommandTests.cs` | +2 项：真正 40/41 帧跨度超时边界（方向完整连续、仅首尾超限） |

**修复前失败复现（实测）**：256 项中 13 项失败——长蓄力 61/120/600/3600 共 5 项
（> 60 帧被 `MaxSequenceFrames` 与 `Prune()` 双重杀死）、释放窗口 7 项（中立 1 帧即失效）、
1 项测试缺陷断言。**修复后 257/257 通过。**

**变异验证**：移除普通指令跨度判断 → 1 项失败（41 帧边界）；
移除释放窗口判断 → 5 项失败（4/20 帧边界、长释放、按键容差不得延长窗口）。均已恢复。

**Godot HUD 实测**（`--maxfps=60` 锁帧注入）：
长蓄力 80 帧 + 松开 2 帧 → `46A`（`shots/charge_long.png`）；
蓄力 40 帧 + 松开 2 帧 → `46A`（`shots/charge_release_ok.png`）；
松开 6 帧 → `-`（`shots/charge_release_expired.png`）；
`236236A` HUD 无退化（`shots/command_236236a.png`）。

**蓄力规则（最终语义，D49/D51/D52/D53）**：蓄力 = 蓄力方向（序列首元素，纯方向）
连续保持 ≥ `ChargeHoldFrames`；保持任意久不失效、内存不随时长增长、不被 `Prune()` 清除；
离开蓄力方向时进入释放挂起（快照蓄力量），窗口内容忍中立（含 SOCD 中立）与尾段方向，
无关方向立即失效；末方向须在 `释放帧 + ChargeReleaseToleranceFrames(3)` 内进入；
蓄力不足不能靠窗口补足；窗口内按回蓄力方向 → 重新计数；一次蓄力只能触发一次，
重新蓄满才能再次识别。普通/双重指令超时规则不变（首尾跨度 ≤ `MaxSequenceFrames`）。

### 阶段 03B-2（2026-11-07）蓄力与扩展指令

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/CommandDefinition.cs` | 新增 `ChargeHoldFrames`（序列第一个元素蓄力保持帧数，默认 0） |
| `core/BattleCore/Commands/CommandRecognizer.cs` | `DirectionEntry` 增加 `EndFrame`；蓄力保持检查；方向不变时实时更新持续结束帧 |
| `core/BattleCore/Commands/CommandCatalog.cs` | 新增默认指令：46A / 28B / 46C（蓄力）、236236A / 214214B / 623623A（双重） |
| `tests/BattleCore.Tests/ChargeCommandTests.cs` | 新增 16 项蓄力测试 |
| `tests/BattleCore.Tests/DoubleMotionCommandTests.cs` | 新增 9 项双重方向测试 |

**蓄力规则（D49）**：蓄力 = 方向序列第一个元素连续保持 ≥ `ChargeHoldFrames` 帧；
`DirectionEntry.EndFrame` 记录持续结束帧。**蓄力只认纯方向**，斜方向是独立条目会打断保持。
默认蓄力 30 帧（0.5 秒 @60Hz）。

**双重规则（D50）**：双重指令用更长方向序列表达（如 236236A = 6 元素），复用同一冲突裁决
（优先级/长度）与手势消费；`MaxSequenceFrames` 放宽到 40 帧。双重优先级（15/25）> 单段（10/20），
长指令完成不借同一历史重复触发短指令。

**验收**：227/227 测试通过；核心与表现层 0 警告 0 错误；Godot 无头导入 `[ DONE ]`；
图形运行 HUD 实测 `P1 Command: 46A`（蓄力）与 `P1 Command: 236236A`（双重）。
变异验证：移除蓄力检查 → `ChargeBackForward_HoldNMinusOneFrames` 失败。

### 阶段 03B-1 最终补修（2026-11-07）跨末方向帧幽灵指令 / 观察者失败封闭 —— 已提交 `33444f0` 并推送

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/CommandRecognizer.cs` | P1 同帧冲突裁决消费「所有合法候选」的按键边沿（D47） |
| `core/BattleCore/Simulation/BattleSession.cs` | P2 观察者异常后 `IsFaulted` 故障封闭 + `Recover()` + 拒绝未恢复继续（D48） |
| `tests/BattleCore.Tests/CommandRecognitionTests.cs` | 新增跨末方向帧幽灵指令复现/定义顺序/独立手势/不同按键 4 项 |
| `tests/BattleCore.Tests/InputIntegrityTests.cs` | 更新 1 项（Recover 语义）；新增观察者失败封闭 7 项 |

**P1 根因**：`ConsumedEndFrames` 只按「末方向帧号」锁定手势，无法抑制「末方向帧不同却
参与同一次裁决」的落败候选。623A（末方向 ↘）胜出后，236B（末方向 →）仍能借残留 B 边沿
在下一帧幽灵触发。修复：同帧收集全部合法候选，胜者与落败候选的按键边沿一次性全部消费
（D47）；不消费落败候选的方向手势，保留给独立手势。

**P2 失败封闭**：观察者（`frameCommitted`）抛异常时，战斗帧已提交但派生识别状态可能漏帧。
现改为 `Advance`/`StepExact` 在观察者调用处单独捕获，置 `IsFaulted` 后照常 `throw`（不吞异常）；
两者开头拒绝未恢复继续。只有观察者异常触发 Faulted，Step/Commit 失败不误标。
调用方恢复一致性后 `Recover()` 清障（D48）。

**验收**：202/202 测试通过；核心与表现层 0 警告 0 错误；Godot 无头导入 `[ DONE ]`。
变异验证：只消费胜者边沿 → 4 项失败。

### 阶段 03B-1 补修（2026-11-07）指令竞争 / 校验 / 连续性 / 朝向时序 —— 本地提交（未推送）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/CommandRecognizer.cs` | P1 手势消费（`ConsumedEndFrames` 抑制幽灵指令）；P2 帧号严格逐帧连续 |
| `core/BattleCore/Commands/CommandDefinition.cs` | P2 构造期校验：相邻方向不同、非法枚举值、源数组复制 |
| `core/BattleCore/Simulation/BattleSession.cs` | P4 `frameCommitted` 改传「帧开始状态」（`stateBefore`） |
| `tests/BattleCore.Tests/CommandRecognitionTests.cs` | 新增幽灵指令复现、独立手势、校验、连续性、朝向时序等 11 项（26 → 37） |
| `tests/BattleCore.Tests/InputIntegrityTests.cs` | 新增观察者异常安全 3 项（24 → 27） |

**P1 根因**：同一方向手势在 `236A` 胜出后，落败的 `236B` 仍能用残留按键边沿 +
末方向在下一帧幽灵触发。修复：胜出指令的「方向序列末方向帧号」标记已消费，
后续指令不得再借该手势匹配；独立手势末方向帧号不同，互不影响。

**P4 朝向时序**：输入按「当帧开始朝向」解析，`frameCommitted` 传入 `stateBefore`
而非 `nextState`（D46）。

**验收**：191/191 测试通过；核心与表现层 0 警告 0 错误；Godot 无头导入 `[ DONE ]`；
图形运行 HUD 显示 `P1 Command: 236A`（`shots/fix_236a.png`）。
变异验证：移除手势消费 → 幽灵指令 2 项失败。

### 阶段 03B-1（2026-11-07）拳皇式基础指令识别 —— 本地提交（未推送）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Commands/CommandDirection.cs` | 新增：相对朝向九宫格方向 |
| `core/BattleCore/Commands/CommandDefinition.cs` | 新增：指令数据（序列 / 按键 / 跨度 / 容差 / 优先级 / 时效） |
| `core/BattleCore/Commands/CommandMatch.cs` | 新增：识别结果（完成帧 / 按键帧 / 优先级 / 诊断） |
| `core/BattleCore/Commands/CommandRecognizer.cs` | 新增：方向变化压缩、边沿匹配、冲突裁决、结果缓冲 |
| `core/BattleCore/Commands/CommandCatalog.cs` | 新增：默认 236A / 623A / 214B / 66 |
| `core/BattleCore/Input/DirectionResolver.cs` | `AttackMask` → public `AttackButtons`（唯一事实来源） |
| `core/BattleCore/Simulation/BattleSession.cs` | 新增可选 `frameCommitted(input, state)` 提交后观察者 |
| `scripts/BattleScene.cs` | 接入识别器；HUD 增加 `P1/P2 Command` 两行 |
| `scenes/Battle.tscn` | HUD 面板高度 96 → 130（容纳五行） |
| `tests/BattleCore.Tests/CommandRecognitionTests.cs` | 新增 26 项 |

**规则**：方向历史按变化压缩；每个条目用**当帧朝向**解析（换边不重算历史）；
序列必须连续；末方向可在按键容差内稍早完成；超时按首尾跨度；
按键只认边沿且一次边沿最多完成一条指令；冲突裁决 = 优先级降序 → 长度降序 → ID 序数升序；
结果按逻辑帧过期（默认 8 帧）。

**验收**：173/173 测试通过；核心与表现层 0 警告 0 错误；Godot 无头导入 `[ DONE ]`；
图形运行 HUD 显示 `P1 Command: 236A` / `P1 Command: 66`
（见 `shots/command_236a.png`、`shots/command_66.png`）。
变异验证：不消费边沿 / 长度优先 / 跨度边界 / 忽略朝向 四种变异分别触发 1、1、1、4 项失败。

**已知限制**：快速双击（松开与再按落在同一逻辑帧之间）会被 `InputBuffer` 合并，
66 需要至少一个逻辑帧的中立（K19）；跳过 ↘ 的快速 236 不匹配（严格连续，K20）。

### 阶段 03A 最终补修（2026-11-07）输入与战斗状态原子提交 —— 已提交 `0bd0c6d` 并推送

**修改文件**

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Simulation/BattleSession.cs` | `Advance` / `StepExact` 改为「候选状态 → 提交输入 → 落状态 → 记账」（D36）；类文档补充异常契约 |
| `tests/BattleCore.Tests/InputIntegrityTests.cs` | 新增 `CommitFailSource` + 6 项：Commit 失败不前进（Advance/StepExact）、批中失败、失败重试、`InputBuffer.Commit` 两类校验失败无副作用 |
| `docs/ARCHITECTURE.md` | 提交职责链加「候选状态」步；新增 D36；写明 `Commit()` 失败必须无副作用的契约与适用边界 |
| `docs/PROGRESS.md` | 最终补修与结项验收 |

**修复的缺陷（P1）**：旧顺序 `CurrentState = Step(...)` → `Commit()` → `CommitStep()`，
`Commit()` 抛异常时状态已前进而计数未动，破坏 `Frame - 初始帧号 == TotalSteps`。

**复现证据（修复前实测）**：4 项用例失败，帧号分别落后于期望 ——
`Advance` 得 1（期望 0）、`StepExact` 得 1（期望 0）、批中失败得 2（期望 1）、重试得 1（期望 0）。

**验收**：147/147 测试通过；阶段 02 调度器相关 79/79；Godot 图形运行
`shots/final_03a.png` → `Logic Frame: 45 [PAUSED · F1]` / `P1: R A` / `P2: L A`
（渲染帧 80→150 逻辑帧停在 45，暂停有效）。

---

### 阶段 03A 专项补修（2026-11-07）输入一致性与调试工具 —— 已提交 `f145275`

**修改文件**

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Input/IInputSource.cs` | **新增**：两阶段输入契约 `Prepare` / `Commit` |
| `core/BattleCore/Input/InputBuffer.cs` | `History` 改 `ReadOnlyCollection` 单实例包装；默认保留 600 帧（`Capacity` / `IsHistoryTruncated` / `IsAlignedWith`）；实现 `IInputSource`；`Take()` 收为 `internal` |
| `core/BattleCore/Simulation/BattleSession.cs` | `Advance` / `StepExact` 新增 `IInputSource? inputSource`（与 `inputProvider` 互斥）；顺序改为「准备 → 模拟 → 提交输入 → 记账」；普通委托幂等契约写入 XML |
| `scripts/BattleScene.cs` | 改用 `Advance(delta, inputSource: _inputBuffer)`；新增只读 `CurrentLogicFrame` 供调试工具读取 |
| `scripts/CaptureHelper.cs` | `--key=` 改名 `--renderkey=`（旧名兼容 + 告警）；渲染帧/逻辑帧变量与文案分离；注入日志打印当时逻辑帧 |
| `tests/BattleCore.Tests/InputIntegrityTests.cs` | **新增 18 项**：只读历史、两阶段提交、失败恢复、批中失败、暂停单步、容量 |
| `tests/BattleCore.Tests/InputPipelineTests.cs` | 全部改走 `inputSource: buffer`；对齐断言改为 `IsAlignedWith` + 最后一帧，不再用 `Count == Frame` |

**修复的三个缺陷**

1. `History` 伪只读 → 可强转 `List` 改写 / `IList.Add` 成功（实测）。
2. `Take()` 先提交输入、`Step()` 后失败 → 幻影帧 + 重试抛「帧号不连续」（实测）。
3. `--key=` 用渲染帧计数却像逻辑帧语义（实测渲染帧 20 = 逻辑帧 17）。

**验收证据**

- `dotnet test` → **141/141**；`dotnet build` 0 警告 0 错误。
- 变异测试：`History => _history` → 2 项失败；`Commit` 提前到 `Step` 前 → 3 项失败。
- Godot：`shots/fix_input.png`（`Logic Frame: 69` / `P1: R A` / `P2: L A`），
  日志 `渲染帧 20 注入 D 按下（当时逻辑帧 17）`、`截图已保存（渲染帧 150，逻辑帧 70）`。
- 复现命令（参数已改名）：
  ```bat
  Godot_v4.7.2-stable_mono_win64_console.exe --path . -- ^
    --shot=C:/Users/32360/Desktop/NewKOF/shots/x.png --shotframes=150 ^
    --renderkey=20:down:D --renderkey=20:down:J --renderkey=20:down:Left --renderkey=20:down:Kp1
  ```

---

### 阶段 03A（2026-11-07）基础输入链路 —— 已提交 `0be9919`

**新增文件**

| 文件 | 说明 |
|---|---|
| `core/BattleCore/Input/InputBuffer.cs` | 渲染采样 → 逻辑帧输入：电平保持、按下边沿闩锁、逐帧历史、帧号连续性校验 |
| `core/BattleCore/Input/DirectionResolver.cs` | 纯函数：SOCD（左右中立 / 上优先）+ 绝对方向 → 相对朝向 |
| `scripts/KeyboardInputMap.cs` | 键盘事件 → `InputBits`；P1/P2 独立键位；失焦 `ReleaseAll()` 防卡键 |
| `tests/BattleCore.Tests/InputBufferTests.cs` | 13 项：电平/边沿/双人独立/历史连续性 |
| `tests/BattleCore.Tests/DirectionResolverTests.cs` | 10 项：SOCD 与朝向 |
| `tests/BattleCore.Tests/InputPipelineTests.cs` | 7 项：输入与逻辑帧绑定、暂停、多帧批次、多帧率一致性 |

**修改文件**

| 文件 | 变更 |
|---|---|
| `scripts/BattleScene.cs` | `_Process` 传入 `_inputBuffer.Take`；`_Input` 处理 F1 暂停 + 转发按键；HUD 改三行（帧号 + 双人输入）；失焦清键 |
| `scripts/CaptureHelper.cs` | 新增 `--key=<frame>:<down\|up>:<KeyName>` 合成按键注入（`Input.ParseInputEvent`，与真实键盘同管线；仅调试参数启用） |
| `scenes/Battle.tscn` | `Hud` 尺寸 264×40 → 320×76，Label 初始三行文本 |
| `docs/ARCHITECTURE.md` | 新增 D26–D33、原则第 9/10 条、模块职责、目录结构；第 5 节问题 4 标记已决定 |
| `docs/PROGRESS.md` | 阶段 03A 成果、测试清单 123、验收记录 |

**验证（实测）**

- `dotnet test` → 123/123 通过（93 + 30）。
- 变异测试：`Take()` 丢弃 `pending` 后 5 项输入测试立即失败 → 断言有效，已还原。
- 真实引擎：`shots/input_p1p2.png`（`Logic Frame: 63` / `P1: R A` / `P2: L A`）、
  `shots/input_paused.png`（`Logic Frame: 21 [PAUSED · F1]` / `P1: D`）。
- 复现命令：
  ```bat
  Godot_v4.7.2-stable_mono_win64_console.exe --path . -- ^
    --shot=C:/Users/32360/Desktop/NewKOF/shots/x.png --shotframes=150 ^
    --key=20:down:D --key=20:down:J --key=20:down:Left --key=20:down:Kp1
  ```

---

### 阶段 02 补修（2026-11-07）战斗状态与调度器帧同步

**修改文件**

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Simulation/FixedStepScheduler.cs` | **`Advance()` 不再增加 `TotalSteps`**，改为只返回预算；新增 `CommitStep()` / `RecordDroppedSteps()`；超大 delta 改整除计算（O(1)）；新增 `MaxSafeWholeSteps` 夹紧 + `SafeAddDropped` 饱和加法防 NaN/溢出 |
| `core/BattleCore/Simulation/BattleSession.cs` | `Advance()` 加 `try/catch`：提交后才记账，异常时记录丢弃预算并原样抛出；`StepExact()` 改为共用 `CommitStep()` 记账路径；新增 `IsFrameSyncIntact` 不变量属性 |

**新增测试文件**

| 文件 | 测试数 | 内容 |
|---|---|---|
| `tests/BattleCore.Tests/FrameSyncRegressionTests.cs` | 12 | 问题 1/2 回归（修复前 10 个失败） |
| `tests/BattleCore.Tests/SchedulerAccountingTests.cs` | 12 | `DroppedStepCount` 语义、超大 delta、不变量 |
| `tests/BattleCore.Tests/SchedulerNumericalRobustnessTests.cs` | 15 | NaN/溢出/精度健壮性 |

**修改测试文件**

| 文件 | 变更 |
|---|---|
| `tests/BattleCore.Tests/FixedStepSchedulerTests.cs` | 新增 `Tick()` 辅助方法（取得预算后显式 `CommitStep`），替换 12 处直接调用——因 `Advance()` 契约变更，非功能退化 |
| `tests/BattleCore.Tests/DeterminismTests.cs` | 修正过时注释（`StepExact` 现在会同步 `TotalSteps`） |

**文档**：`ARCHITECTURE.md`（新增 D17–D24、记账职责链、核心不变量、离线原型限定）、`PROGRESS.md`、`AGENT_HANDOFF.md`

**复核追加（2026-11-07 第二轮 Agent，独立重测后）**

| 项 | 变更 |
|---|---|
| `FixedStepScheduler.cs` | `CommitStep()` / `RecordDroppedSteps()` 由 **public 收为 internal**（D25）；新增 `InternalsVisibleTo("BattleCore.Tests")` |
| `SchedulerAccountingTests.cs` | 新增反射测试 `AccountingMutators_AreNotPartOfPublicApi`（+1 → 13） |
| `SchedulerNumericalRobustnessTests.cs` | N2 失效机理注释按实测更正；`Advance_DroppedCountNeverOverflowsToNegative` 改名并加强为正数 + 饱和断言 |
| `ARCHITECTURE.md` / `PROGRESS.md` | 更正 D23 与 N2 的实测机理（`(long)∞` 饱和到 `long.MinValue`，不是 `long.MaxValue`；全程无 NaN） |

**未修改**：`C:\Users\32360\Desktop\Ikemen-GO\` 下任何文件。

---

### 阶段 02（2026-11-07）固定逻辑帧与模拟调度验收

**新增文件**

| 文件 | 说明 |
|---|---|
| `core/BattleCore/Simulation/FixedStepScheduler.cs` | 纯 C# 固定步长调度器。double 秒累加、补帧上限、显式丢弃计数、Pause/Resume、拒绝 NaN/Inf |
| `core/BattleCore/Simulation/BattleSession.cs` | 调度器 + 模拟 + 状态绑定，强制「输入帧号 == 状态帧号」 |
| `tests/BattleCore.Tests/FixedStepSchedulerTests.cs` | 调度器测试（多帧率一致性、补帧、暂停、非法输入） |
| `tests/BattleCore.Tests/DeterminismTests.cs` | 确定性、长跑、快照隔离、旧状态重放 |
| `tests/BattleCore.Tests/GodotIntegrationContractTests.cs` | Godot 层依赖的内核契约（含边界声明） |

**修改文件**

| 文件 | 变更 |
|---|---|
| `scripts/BattleScene.cs` | **移除全部时间累积逻辑**，改为 `_session.Advance(delta, null)`；积压告警只输出一次 |
| `docs/ARCHITECTURE.md` | 新增决策 D9–D16、调度器职责、原则第 7/8 条、Ikemen GO 版本记录 |
| `docs/PROGRESS.md` | 阶段 02 成果、缺陷修复记录、54 项测试清单、验收记录 |
| `docs/AGENT_HANDOFF.md` | 本文件 |

**删除文件**：无（`shots/diag/` 临时诊断工程已在阶段 02 中清理）

**未修改**：`C:\Users\32360\Desktop\Ikemen-GO\` 下任何文件（只读参考）。

---

## 5. 当前已知问题

| # | 问题 | 影响 | 建议 |
|---|---|---|---|
| K1 | 本机桌面另有**标准版** Godot（`Godot_v4.7.2-stable_win64.exe\`），不支持 C# | 误用会报编译 C# 失败 | 始终使用 mono 版路径（见第 2 节） |
| K2 | **Git 不在系统 PATH** | 直接 `git` 命令失败 | 使用第 3.9 节的完整路径 |
| K2b | **GitHub 直连被重置**，需本地代理 `127.0.0.1:7897` | `git push/fetch` 报 `Connection was reset` | push 前 `set HTTPS_PROXY=http://127.0.0.1:7897`（见第 6 节） |
| K3 | `CaptureHelper.cs` 位于 `scripts/`，会打进游戏程序集 | 正常运行零副作用 | 见 `ARCHITECTURE.md` 第 5 节问题 9 |
| K4 | `--headless --import` 末尾打印 `ERROR: Parameter "singleton" is null` | Godot 4.7 headless 已知无害告警，导入成功（`[ DONE ]`） | 忽略，勿误判 |
| K5 | 启动首帧有 3 帧被丢弃（`DroppedStepCount = 3`） | 资源加载尖峰导致，900 帧稳态不再增加 | 属正常现象；若稳态持续增长才是性能问题 |
| K6 | ~~输入固定为 `InputBits.None`~~ **已解决（阶段 03A）** | 角色仍不移动（本阶段范围外），但输入已逐逻辑帧进入内核 | HUD 三行可实时核对；下一阶段用 `DirectionResolver` 接运动 |
| K7 | 无 `.sln` 文件 | `dotnet build` 需在具体工程目录执行 | 可选加 `NewKOF.sln`（不必须） |
| K8 | `BattleState.StateVersion` 恒为 `1` | 仅作快照格式标记，无升级机制 | 见 `ARCHITECTURE.md` 第 5 节问题 1 |
| K9 | ~~`CommitStep()` / `RecordDroppedSteps()` 为 public~~ **已解决（2026-11-07）** | 原 public 可让外部把 `TotalSteps` 推到状态前面 | 已改为 `internal` + `InternalsVisibleTo("BattleCore.Tests")`，并加反射测试 `AccountingMutators_AreNotPartOfPublicApi` 固定形状（D25）。生产代码只有 `BattleSession` 能记账 |
| K10 | 单次超大 delta（数百秒级）存在浮点边界行为 | 已用整除 + 饱和保护消除 NaN 与丢帧，但极端值下余量会被归零（记为丢弃） | 正常渲染（16ms 级 delta）完全不受影响；超大 delta 仅出现在挂起/调试场景 |
| K11 | **丢帧策略仅适用于离线原型** | 未来联机回滚不能沿用 `DroppedStepCount` 丢弃策略（会造成状态分叉） | 联机阶段必须重新设计时间预算策略并单独验收 |
| K12 | ~~`InputBuffer.History` **无上限增长**~~ **已解决（阶段 03A）** | 原约 16 B/帧 ≈ 3.4 MB/小时；现默认只保留最近 600 帧（`Capacity`），超出淘汰最旧帧并置 `IsHistoryTruncated` | 完整录像仍需独立模块（K16） |
| K13 | **键位硬编码**，无重绑定 | 只有 `KeyboardInputMap.TryMap` 一处映射，改键需改代码 | 键位重绑定 UI 属后续阶段；手柄映射未做 |
| K14 | 两次逻辑帧之间多次按下会**合并成一帧** | 快速双击在 `InputBits` 里只体现为一帧按下 | 按 D31 属已知限制，`CoalescedPressCount` 可观测；逐次事件流仍未实现（K19） |
| K15 | **普通 `Func<int,FrameInput>` 输入源无法回滚** | 模拟失败后会话会以同一帧号再次调用它；若委托有副作用（如 `buffer.Take`），会重复提交 | 契约要求「同一帧号返回同一输入（幂等）」；生产路径必须传 `IInputSource`（`InputBuffer`），`Take()` 已收为 internal 防误用 |
| K16 | **输入历史只保留最近 600 帧** | 更早的帧被淘汰，`IsHistoryTruncated == true`，不能再用于回放/长窗口识别 | 完整输入录像需独立模块（未实现）；若做重放，先设计录像格式，不要拉长这个缓冲 |
| K17 | `BattleSession.Advance` 允许 `inputSource` 与 `inputProvider` 二选一 | 同时传入会抛 `ArgumentException` | 保持显式；不要为了省参做成隐式优先级 |
| K18 | **`Commit()` 失败「无副作用」靠输入源自己实现** | 会话能保证自己的状态/计数不前进，但无法回滚第三方源内部已发生的写入 | `InputBuffer` 把校验放在写入前；自定义 `IInputSource` 必须遵守该契约，否则只有它自己脏，帧同步仍不被破坏 |
| K19 | **快速双击（松开 + 再按落在同一逻辑帧之间）被合并** | 逻辑输入日志中没有中立帧，`66` 不会触发（Godot 实测复现） | 属电平格式的已知合并语义（K14/D31）；真实手感需要逐次事件流，属后续输入格式扩展 |
| K20 | **指令方向序列要求严格连续** | 跳过 ↘ 的快速 236（↓ 直接到 →）不会被 `236A` 匹配 | 当前刻意严格，避免误判；若需宽松匹配，应在 `CommandDefinition` 增加显式配置，而不是放宽识别器默认行为 |
| K21 | ~~**`frameCommitted` 观察者不得抛异常**~~ **已解决（03B-1 最终补修）** | 观察者抛异常时该帧已记账、状态已推进、历史对齐；现在会话进入 `IsFaulted` 故障封闭，`Recover()` 前拒绝继续模拟（防止派生识别状态漏帧），异常照常传播、不吞 | 安全；调用方恢复派生状态一致性后 `Recover()` 再继续（已补 7 项回归测试） |
| K22 | 识别结果缓冲按逻辑帧过期，暂停时不流逝 | 暂停期间 `ExpiresAtFrame` 不会推进 | 设计如此：缓冲是逻辑帧语义，不是真实时间语义 |
| K23 | **手势消费用「末方向帧号」锁定** | 同一方向手势被某指令消费后，其他指令不能再借它匹配；帧号随方向条目淘汰时清理 | 属 D43 刻意行为，避免幽灵指令；最终补修（D47）补上「落败候选按键边沿」也消费，覆盖末方向帧不同的竞争 |
| K24 | **蓄力匹配不检查 `MaxSequenceFrames`**（D51） | 蓄力指令的超时语义 = 释放窗口（默认 3 帧）+ 按键容差；定义里的 60 帧跨度值对蓄力无效 | 设计如此：保持时长与序列跨度是两个概念；若要限制蓄力指令整体时长，应另加显式配置而不是复用跨度 |
| K25 | **释放窗口只容忍中立与尾段方向**（D52） | 蓄力释放后输入任何无关方向（含斜方向）立即失效，不能在窗口内「绕路」 | 设计如此（明确规则）；若要放宽需在 `CommandDefinition` 加显式配置 |
| K26 | **`CaptureHelper --maxfps=` 只影响调试验收运行** | 锁定渲染帧率使注入时序可复现；正常启动不传参则零改动 | 仅与 `--shot=` 同用时生效（`_Ready` 里解析，无 `--shot=` 时节点直接禁用） |
| K27 | **序列指令要求「末方向新进入」** | 按住某方向较久后按攻击键，不会触发以该方向结尾的序列指令（按键容差从末方向**进入帧**起算）——如长按 ↓ 再按 A 不触发「↓+A」型指令 | D39 的既有语义在长按场景的自然结果；「无方向要求」的基础动作用裸按键指令（D56）表达。若要「按住方向 + 按键」，应扩展容差锚点（显式设计，勿放宽默认） |
| K28 | **识别状态与动作状态随快照推进** | `FighterState` 每帧都产生新实例（识别历史合法前进）；跨实例比较依赖 `RecognitionState` 的结构化相等 | D55 设计如此；不要把识别状态移出权威快照（会破坏重放契约 B6.3/6.8） |
| K29 | **`StateVersion` 已升到 2 / CoreVersion 2** | 阶段 04 改变了 FighterState 格式（动作状态 + 识别快照） | 项目无序列化快照，无迁移负担；未来引入序列化时以版本号分支（K8 仍有效） |
| K30 | **识别快照的五个公开集合是 `ReadOnlyCollection` 包装** | 强转 `List<T>`/`T[]` 得 null；`IList` 写操作抛 `NotSupportedException` | D61 设计如此（D34 同一先例）；需要改动集合请走 `Advance`/`WithResultConsumed` 纯函数，不要试图绕过包装 |
| K31 | **非法 `FighterLogicState` 在两道门被拒** | `FighterState.Validate`（BattleState 构造期）与 `FighterStateMachine.Tick` 入口都用 `Enum.IsDefined` 拒绝未定义枚举 | D62 设计如此；新增状态枚举值时两处自动跟随枚举定义，无需维护状态清单 |
| K32 | **Godot 运行时直接加载 `.godot/mono/temp/bin/Debug` 旧程序集** | 改代码后直接跑 `--shot` 验收会看到旧行为（实测 05A 踩坑：截图显示旧 HUD 且角色不移动） | 截图验收前先 `dotnet build NewKOF.csproj`（默认 Debug 配置即写入该目录；`-c Release` 只更新 Release 目录） |
| K33 | **`FighterState.PositionX/PositionY` 已是 `FixedPoint`** | 直接当 int 用会编译失败；`Assert.Equal(-120, f.PositionX)` 因泛型推断失败也编译不过 | 读整数用 `PositionXUnits`（向零截断）或 `PositionX.ToIntTruncated()`；整数字面量赋值/传参可隐式转换（= 世界单位）；比较用 `FixedPoint.FromInt(...)` 或 `== -120`（运算符可隐式转换）；Godot 显示用 `ToDouble()`，**浮点绝不写回权威状态** |

**无阻塞性问题。** 编译 0 警告 0 错误，阶段 04 最终补修验收时 343/343 测试通过，可正常启动运行。

---

## 6. 提交历史与工作区状态

- **Git**：https://github.com/zziyoo/KOFWingPH，分支 `main`（跟踪 `origin/main`）
  - `9937b3f` — 阶段 01–02 Godot 工程初始化与固定逻辑帧调度
  - `c003dc2` — 记录远程仓库与代理要求
  - `850c4db` — 阶段 02 补修（调度器记账与帧同步）
  - `0be9919` — 阶段 03A 基础输入链路
  - **`f145275` — 阶段 03A 专项补修（只读历史、两阶段提交、容量、调试帧语义），已推送**
  - `1538395` / `083abd5` — 仅文档
  - `bc048c8` — 仅文档（同步 `f145275` 状态）
  - **`0bd0c6d` — 阶段 03A 最终补修（输入与战斗状态原子提交顺序），已推送**
  - **上一轮 03B-1 补修：`fix(input): finalize phase 03B-1 command recognition`（`3bd1388`），已推送。**
  - **03B-1 最终补修：`fix(input): resolve command conflicts and observer faults`（`33444f0`），已推送。**
  - **03B-2：`feat(input): implement charge and advanced commands`（`c098f40`），已推送。**
  - **当前 HEAD 请用 `git rev-parse HEAD` 读取**：本节记录的是写文档时的状态，
    不要当作实时 HEAD。
  - **本轮 03B-2 最终补修：`fix(input): finalize charge timing and command timeout`（`0843dd6`），已推送。**
  - **03B 边界补修：`fix(input): prevent stale direction reuse after charge release`（`5434c5b`），已推送。**
  - **阶段 04：`feat(combat): implement phase 04 fighter state machine`（`b622d0f`），已推送。**
  - **阶段 04 最终补修：`fix(combat): enforce immutable snapshots and valid fighter states`（`cc8a510`），已推送。**
  - **本轮阶段 05A：`feat(movement): implement deterministic fixed-point ground movement`**
    （创建后用 `git rev-parse HEAD` 读取 SHA）。提交内容：
    ```
    新增: core/BattleCore/Numerics/FixedPoint.cs
          core/BattleCore/Combat/{MovementDefinition,GroundMovement}.cs
          tests/BattleCore.Tests/{FixedPointTests,GroundMovementTests}.cs
    修改: core/BattleCore/State/{FighterState,BattleState}.cs
          core/BattleCore/Combat/{FighterDefinition,DemoRoster}.cs
          core/BattleCore/Simulation/BattleSimulation.cs
          core/BattleCore/BattleCoreInfo.cs
          scripts/BattleScene.cs  scenes/Battle.tscn
          tests/BattleCore.Tests/{BattleSimulationTests,Phase04IntegrationTests}.cs（2 处断言改整数视图）
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **工作区：干净**（阶段 04 最终补修提交并推送后）。
    `git status --porcelain` 应无输出；有输出说明有人动过工作区。
  - `f145275` 提交内容（12 文件）：
    ```
    新增: core/BattleCore/Input/IInputSource.cs（+ .uid）
          tests/BattleCore.Tests/InputIntegrityTests.cs（+ .uid）
    修改: core/BattleCore/Input/InputBuffer.cs
          core/BattleCore/Simulation/BattleSession.cs
          scripts/BattleScene.cs  scripts/CaptureHelper.cs
          tests/BattleCore.Tests/InputPipelineTests.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - `0be9919` 提交内容（19 文件）：
    ```
    新增: core/BattleCore/Input/InputBuffer.cs
          core/BattleCore/Input/DirectionResolver.cs
          scripts/KeyboardInputMap.cs
          tests/BattleCore.Tests/InputBufferTests.cs
          tests/BattleCore.Tests/DirectionResolverTests.cs
          tests/BattleCore.Tests/InputPipelineTests.cs
    修改: scripts/BattleScene.cs  scripts/CaptureHelper.cs
          scenes/Battle.tscn  .gitignore
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **网络注意**：本机直连 github.com:443 不通，需本地代理 `http://127.0.0.1:7897`。
    代理**偶发 502**，重试一次即可；最近一次成功命令：
    ```bat
    git -c http.proxy=http://127.0.0.1:7897 -c http.version=HTTP/1.1 push origin main
    ```
    （环境里另有沙箱代理 `127.0.0.1:51323`，对 github 返回 502，**不要**用它）
- **`.gitignore` 覆盖**：`.godot/`、`bin/`、`obj/`、`shots/`、`.vs/`、`*.user` 等。
- **构建产物**（被忽略，不影响源码）：
  - `bin/`、`obj/`（根目录、`core/BattleCore/`、`tests/BattleCore.Tests/`）
  - `.godot/`
  - `shots/`（验收截图：`input_p1p2.png` 双人输入、`input_paused.png` 暂停、
    `command_236a.png` 指令 236A、`command_66.png` 双击前；
    历史：`battle_01.png`、`battle_02.png`、`final.png`、`p02.png`）
- **安全备份**：`C:\Users\32360\Desktop\NewKOF_backups\NewKOF_pre_phase03a_20261107.tar.gz`
  （阶段 03A 开始前的完整工作区，含当时未提交的补修代码）
- **清理建议**：`bin/obj/.godot` 可安全删除，重新 `dotnet build` + `--import` 即可重建。
- **工作区状态判读**：以 `git status --porcelain` 的**实际输出**为准，不要凭文档猜。
  提交后应无输出；有新改动时它会如实列出，以它为准而不是以本节文字为准。
  若出现 `bin/`、`obj/`、`.godot/`、`shots/`、`.workbuddy/`，说明 `.gitignore` 被改坏。

---

## 7. 下一位 Agent 应优先阅读的文件

**按顺序阅读：**

1. `docs/AGENT_HANDOFF.md`（本文件）— 环境、命令、已知问题
2. `docs/ARCHITECTURE.md` — 第 4 节（D1–D60 决策）与第 5 节（未决定问题）
3. `docs/PROGRESS.md` — 已完成/未完成边界、191 项测试、下一阶段计划
4. `core/BattleCore/Simulation/FixedStepScheduler.cs` — **阶段 02 核心**，理解时间调度
5. `core/BattleCore/Simulation/BattleSession.cs` — 输入帧号与状态帧号的绑定规则
6. `core/BattleCore/Input/InputBuffer.cs` — **阶段 03A 核心**：边沿闩锁、两阶段提交、只读历史
7. `core/BattleCore/Input/IInputSource.cs` — 输入提交契约（Prepare/Commit）
8. `core/BattleCore/Input/DirectionResolver.cs` — SOCD 与相对朝向规则
9. `core/BattleCore/Commands/CommandRecognizer.cs` — **阶段 03B-1 核心**：方向压缩、匹配、冲突、缓冲
10. `core/BattleCore/Commands/CommandDefinition.cs` / `CommandCatalog.cs` — 指令数据与默认表
11. `core/BattleCore/Simulation/BattleSimulation.cs` — 内核入口 `Step()` 契约
12. `core/BattleCore/State/BattleState.cs` — 不可变状态与所有权
13. `tests/BattleCore.Tests/` — 十二个测试文件构成当前回归基线
14. `scripts/BattleScene.cs` — 表现层如何只读消费状态（**应保持极薄**）
15. `scripts/KeyboardInputMap.cs` — 键位映射，唯一接触 Godot 输入 API 的地方

**参考资料（只读，禁止修改）：**

- 仓库：`https://github.com/ikemen-engine/Ikemen-GO.git`，分支 `develop`
- 本地镜像：`C:\Users\32360\Desktop\Ikemen-GO\docs\combat-research\`
- **文档基线提交**：`4d007d6e1b72262115b120c4b25886b3ba4f9a57`
- **第二轮复核提交**：`5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`
- 重点文件：`22_NEW_KOF_ARCHITECTURE.md`（P0–P9 阶段）、`23_REUSE_DECISIONS.md`（决策表）

> 本地 `develop` HEAD 已前进到 `fb269c97…`，**与文档基线不同**。
> 阅读源码时以文档声明的提交为准。

---

## 8. 下一步可以安全执行的任务

### 可安全执行 ✅

1. ~~实现阶段 03 输入链路~~ **已完成（03A）**。后续若要改键位/加手柄，只动
   `KeyboardInputMap`（或新增 `GamepadInputMap`），**不要**把映射写进 `BattleScene` 或内核。
2. ~~提交阶段 03A 各轮补修~~ **已推送 `0be9919`、`f145275`、`bc048c8`、`0bd0c6d`**。
3. ~~阶段 03B-1 基础指令识别~~ **已完成并创建本地提交**：236A / 623A / 214B / 66、
   相对朝向与换边、冲突优先级、按键边沿消费、结果缓冲。
3b. ~~阶段 03B-1 补修~~ **已完成并创建本地提交**：手势消费抑制幽灵指令（D43）、
   帧连续性（D44）、构造期校验（D45）、朝向时序（D46）、观察者异常安全。
3c. ~~阶段 03B-1 最终补修~~ **已完成并推送 `33444f0`**：跨末方向帧幽灵指令（D47）、
   观察者失败封闭 `IsFaulted`/`Recover()`（D48）。
4. ~~阶段 03B-2：蓄力与扩展指令~~ **已完成并推送 `c098f40`**：蓄力指令（46A/28B/46C，D49）、
   双重/超必杀指令（236236A/214214B/623623A，D50）。**只处理输入与整数逻辑帧**。
4b. ~~阶段 03B-2 最终补修~~ **已完成并推送**（长蓄力解耦 D51、释放窗口 D52、
   一次性消费 D53、双重超时边界测试）。**03B-2 正式结项**。
4c. ~~阶段 04 入口：按角色指令表 / 消费接口~~ **已完成并推送**：识别进内核（D55）、
   裸按键指令（D56）、FighterState 扩展（D57）、状态机（D58）、角色配置（D59）、
   消费契约（D60）；**最终补修已推送**（快照只读包装 D61、非法状态两道门 D62）。
   **阶段 04 正式结项**。仍未做：序列指令的「按住方向 + 按键」容差锚点（K27）、
   招式判定/伤害/动画、运动系统。
4d. **阶段 05 入口**：先定定点数格式（ARCHITECTURE 第 5 节问题 2），再接移动/跳跃
   （`Airborne` 状态转换）与 `PositionX` 真实运动；判定框与事件流是阶段 06 入口。
   **不得**在 `BattleScene._Process` 重新引入时间累积；运动数值进 `FighterState` 时
   必须保持不可变快照契约（D57）。
5. **为现有测试增加用例** — 只要不改动 `BattleCore` 公共契约。
6. **把 `CaptureHelper.cs` 移到 `tools/`** — 需同步改 `scenes/Battle.tscn` 的 `ext_resource`
   与 `NewKOF.csproj` 的 `Compile Include`。
7. **添加 `.sln`** — 纯工程整理。

### 需要先决策 ⚠️

8. **修改 `BattleState` 结构**（增加速度/状态字段）— 先读 `ARCHITECTURE.md` 第 5 节问题 1。
9. **引入碰撞/判定框** — 先决定几何类型（AABB/OBB）与推挤算法。
10. **引入事件流** — 先设计 `(frame, eventId)` 事件契约。
10. **SOCD 改「后输入优先」** — 必须先扩展 `FrameInput` 记录事件顺序，否则无法重放。

### 禁止 ❌

11. **不得**修改 `C:\Users\32360\Desktop\Ikemen-GO\` 仓库。
12. **不得**复制或逐函数翻译 Ikemen GO 源码。
13. **不得**使用 Godot 物理系统决定权威战斗结果。
14. **不得**在 `BattleScene._Process` 中重新引入时间累积/补帧/取整逻辑（见原则第 8 条）。
15. **不得**在 Godot 层保存输入历史或做 SOCD/指令识别（见原则第 9、10 条）。
16. **不得**擅自执行 `git commit` / `git push`（除非用户明确要求）。
17. **不得**用空类或未实际运行的测试冒充完成。

---

## 9. 交接自检清单

新 Agent 开工前请逐项核对，任何不一致都要先修正文档再写代码：

- [ ] `C:\Users\32360\Desktop\NewKOF` 存在，`project.godot` 可读
- [ ] `dotnet build` 在项目根目录返回 0 错误
- [ ] `dotnet test` 在 `tests\BattleCore.Tests` 返回「通过: 418」
      （若数量变化，说明有 Agent 改过测试，需查 `PROGRESS.md` 是否同步）
- [ ] `core/BattleCore/BattleCore.csproj` 中**没有**任何 Godot 引用
- [ ] `scripts/BattleScene.cs` 中**没有**累加器/补帧/取整代码（只有 `_session.Advance`）
- [ ] `scripts/` 下**没有**任何权威判定逻辑、**没有**输入历史/SOCD 实现
- [ ] `FrameInput` 的帧号恒等于 `BattleState.Frame`（`BattleSession` 会抛异常，测试守护）
- [ ] `InputBuffer.History` 强转 `List<FrameInput>` 必须为 null（只读包装未被改回裸列表）
- [ ] `history[^1].Frame == CurrentState.Frame - 1`（`IsAlignedWith`），不用 `Count == Frame`
- [ ] `BattleSession` 中 `CurrentState` 只在 `Commit` 成功后才赋值（`nextState` 是局部变量）
- [ ] 输入只有两条合法路径：`IInputSource`（两阶段）或幂等委托；`Take()` 不得被表现层当委托用
- [ ] 指令识别只在 `core/BattleCore/Commands/`；`scripts/` 只读取 `LastMatch` 显示，不做判定
- [ ] 方向历史按「变化」压缩，每个条目用**当帧朝向**解析；换边不得重算历史条目
- [ ] 蓄力匹配读「释放挂起」槽位而不是历史首条目（D51）：长蓄力条目被裁剪仍能识别，
      `MaxSequenceFrames` 不约束蓄力；挂起槽位 = 每指令一个，内存恒定
- [ ] 释放窗口语义（D52/D54）：中立/尾段容忍、无关方向立即失效、不足不补、按回蓄力方向重新计数、
      尾段必须属于本次释放之后（下界）
- [ ] 产生过候选的蓄力指令挂起立即作废（D53）：同一蓄力不得二次触发
- [ ] 识别状态为不可变 `RecognitionState` 并随 `FighterState` 进快照（D55）：
      `Advance` 纯函数、结构化相等、Step 内零帧延迟完成识别→消费→推进
- [ ] 识别快照的五个公开集合是只读包装（D61）：强转 List/数组得 null、
      `IList` 写抛 `NotSupportedException`、旧快照不被污染、新旧状态不共享可写底层
- [ ] 非法 `FighterLogicState` 两道门拒绝（D62）：`Validate` 与 `Tick` 都用 `Enum.IsDefined`
- [ ] 权威位置/速度一律 `FixedPoint` Q16.16（D63）：加减 checked、乘除 Int128、取整向零截断、
      溢出抛异常；`PositionX: -120` 经隐式转换 = -120 世界单位（不是原始值）；
      浮点转换只出现在 Godot 显示层且不写回
- [ ] 地面运动（D65）：仅站立待机移动；蹲下/动作三阶段原地；朝左按前 X 减小；
      SOCD 复用 `DirectionResolver`，运动系统不实现第二套左右消解
- [ ] 运动配置（D64）在 `MovementDefinition`（构造期校验），不在状态机散落速度常量、不在 Godot 计算
- [ ] 冲突裁决顺序为「优先级降序 → 序列长度降序 → ID 序数升序」，与定义顺序无关
- [ ] 胜出指令标记「末方向帧号」已消费（`ConsumedEndFrames`），落败指令不得幽灵触发
- [ ] `frameCommitted` 传入的是帧开始状态（`stateBefore`），其 `Facing` 是当帧输入朝向
- [ ] `Observe` 帧号严格逐帧连续（首次任意非负），非法帧先抛后不改内部状态
- [ ] `docs/ARCHITECTURE.md` 第 4 节决策（D1–D62）与代码实现一致
- [ ] `docs/PROGRESS.md` 已完成项与实际代码一致

**工作结束时必须做的事：**

- [ ] 更新 `docs/PROGRESS.md`（已完成/测试状态/下一阶段）
- [ ] 更新 `docs/AGENT_HANDOFF.md` 第 4/5/6 节
- [ ] 若做了架构决策，写入 `docs/ARCHITECTURE.md` 第 4 节，并从第 5 节移除
- [ ] 实际运行测试并在文档中记录真实结果（**不得编造**）
