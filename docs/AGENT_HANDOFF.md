# AGENT_HANDOFF.md — Agent 交接说明

> **每一位 Agent 在修改代码前必须先读本文件，并核对文档与实际代码是否一致。**
> 最后更新：2026-11-08（阶段 09C：选人与比赛流程 D110–D112，821 项测试）

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
期望：`失败: 0，通过: 1012，已跳过: 0，总计: 1012`

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

### 阶段 07B（2026-11-07）连段统计与招式取消（D90/D91）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/State/ComboState.cs` | **新增**：`Count` + `AttackerId` 的记录类型，`AfterHit` 纯函数（延续或重开连段），`Validate`（攻击者 ≠ 自己） |
| `core/BattleCore/Combat/CancelDefinition.cs` | **新增**：目标招式 / 窗口 `[First,Last]`（自发动帧起算） / 阶段过滤 / 命中·防御确认·空挥；构造期校验 |
| `core/BattleCore/Combat/MoveDefinition.cs` | 新增 `Cancels`（默认空 = 不可取消） |
| `core/BattleCore/Combat/FighterDefinition.cs` | 构造期校验：取消目标必须存在且不是自身 |
| `core/BattleCore/Combat/FighterStateMachine.cs` | **取消路径**：阶段推进前判定窗口+阶段+确认+缓冲，消费一次并进入目标 Startup（新实例）；新增只读 `IsInCancelWindow`（HUD 与内核共用窗口谓词） |
| `core/BattleCore/Combat/FighterLogicState.cs` | 新增 `IsHitReaction` / `IsInMove` 扩展（连段连续性判定） |
| `core/BattleCore/State/FighterState.cs` | 新增 `ComboRecord` / `HitConfirmFrame` / `BlockConfirmFrame` + 不变式（确认只在当前实例有效） |
| `core/BattleCore/Simulation/BattleSimulation.cs` | 结算写入攻击者确认帧；连段更新与帧末重置；`NormalizeConfirms` |
| `core/BattleCore/Combat/DemoRoster.cs` | Kai 5A 增加「命中确认 → 236A」取消规则（演示数据，窗口 3..10） |
| `core/BattleCore/BattleCoreInfo.cs` / `State/BattleState.cs` | StateVersion/CoreVersion 7 → 8 |
| `scripts/BattleScene.cs` / `scenes/Battle.tscn` | HUD 增加 `Combo:` 与两人 `Cancel:`（招式 @经过帧 WINDOW/closed hit@/blk@） |
| `tests/BattleCore.Tests/ComboCancelTests.cs` | **新增 22 项** |

**验收**：605/605；四构建 0/0；图形 `shots/p07b_cancel_window.png`（WINDOW + hit@）
与 `shots/p07b_cancel_combo.png`（取消成 236A + Combo 2 + HP 920）。
**注意**：取消确认必须来自<b>更早帧</b>（同帧确认不能同帧取消）；确认帧在换招/中断后自动清空。

### 阶段 07C（2026-11-07）击退、击飞、空中受击、倒地与起身（D92/D93）—— 阶段 07 结项

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/AttackDefinition.cs` | **D92/D93**：新增 `GroundKnockback` / `GroundKnockbackFriction` / `BlockKnockback` / `LaunchVelocityX` / `LaunchVelocityY` / `KnockdownFrames` / `WakeupFrames`；构造期校验（非负、击退必须有摩擦、击飞必须 ≥ 1 倒地帧） |
| `core/BattleCore/Combat/FighterLogicState.cs` | 新增 `AirHitstun(9)` / `Knockdown(10)` / `Wakeup(11)`；`IsHitReaction` 扩展到空中受击与倒地/起身 |
| `core/BattleCore/State/FighterState.cs` | 新增 `KnockbackFriction` / `LandingKnockdownFrames` / `LandingWakeupFrames`；`Validate` 重写空中/地面分支（击飞首帧 Y=0+vy>0 例外、倒地计划归属、击退速度与摩擦成对） |
| `core/BattleCore/Combat/FighterStateMachine.cs` | 空中受击原样保持；倒地/起身倒计时（倒地归零 → 起身或同帧回待机） |
| `core/BattleCore/Simulation/BattleSimulation.cs` | 运动阶段：地面击退逐帧位移+摩擦、空中受击积分与落地转倒地/起身/站立；结算：空中目标不再跳过、击飞/击退/倒地计划写入；`NormalizeLandingPlan` |
| `core/BattleCore/Combat/HitDetection.cs` | 倒地时受击框降为蹲下高度（贴地） |
| `core/BattleCore/Combat/DemoRoster.cs` | 演示数据：5A 击退 2/摩擦 0.5/防御击退 1.5；623A 升龙击飞（6/1.5，倒地 12/起身 8）；BackDraft 击退 3；214B 扫腿改为扫倒（2.5，倒地 12/起身 8） |
| `core/BattleCore/BattleCoreInfo.cs` / `State/BattleState.cs` | StateVersion/CoreVersion 8 → 9 |
| `scripts/BattleScene.cs` | HUD 状态行对 Knockdown/Wakeup 显示剩余帧 |
| `tests/BattleCore.Tests/KnockbackLaunchTests.cs` | **新增 16 项** |
| `tests/BattleCore.Tests/CombatSettlementTests.cs` | 空中被击用例按 D93 新契约更新（原「跳过」→「进入 AirHitstun」） |
| `tests/BattleCore.Tests/FighterStateValidationTests.cs` | 未定义枚举取样值 9 → 12（0..11 已合法） |

**验收**：621/621；四构建 0/0；图形 `shots/p07c_knockback.png` / `p07c_airhitstun.png` /
`p07c_knockdown.png` / `p07c_wakeup.png`（P1 被扫倒的完整链路）。
**注意**：击飞当帧位置仍为 0（速度已设置），下一帧才离地——这是 D93 的显式语义，别当成 bug；
623A 的三段方向无法用注入构造（K20 的严格连续规则），图形验收用 P2 的 214B。

## 4. 最近完成的修改

### 阶段 10D（2026-11-08）打击反馈（D126）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Presentation/CombatFeedback.cs` | **新增**：`FeedbackKind` / `FeedbackCue` / `FeedbackDirector`（映射 + 去重）/ `HitShake` |
| `scripts/CombatFeedbackLayer.cs` | **新增**：短命几何火花层（渲染帧衰减、QueueFree） |
| `scripts/FighterVisual.cs` | `FlashUntil(logicFrame)` 受击闪白（叠加在姿态上） |
| `scripts/BattleScene.cs` | 消费事件生成反馈、抖动偏移、F4 关闭特效、重开时清空去重 |
| `tests/BattleCore.Tests/FeedbackTests.cs` | **新增 26 项** |

**验收**：1004/1004（Debug）、表现层 0 警告 0 错误、`p10d_hit_18.png` / `p10d_doubleko_fx.png`。
**StateVersion 13 / CoreVersion 14 不变**。

### 阶段 10C（2026-11-08）状态驱动动画管线（D125）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Presentation/AnimationSet.cs` | **新增**：`PoseFrame` / `AnimationClip` / `AnimationSet` / `MoveAnimation` |
| `core/BattleCore/Presentation/AnimationResolver.cs` | **新增**：`FighterVisualClock` + `AnimationResolver`（状态→动画→姿态） |
| `core/BattleCore/Data/PresentationDataLoader.cs` | **新增**：表现配置严格加载器（schema v1，未知字段拒绝，错误带路径） |
| `data/presentation/Kai.json` / `Rei.json` | **新增**：各 15 段姿态动画（idle/walk/crouch/jump/三段攻击/受击/防御/击飞/倒地/起身） |
| `scripts/FighterVisual.cs` | **新增**：把 `ResolvedPose` 应用到 `ColorRect`（尺寸/偏移/旋转/颜色/镜像） |
| `scripts/BattleScene.cs` | 加载 `res://data/presentation/*.json`；选人/换人/每回合绑定动画集合并重置时钟 |
| `tests/BattleCore.Tests/AnimationTests.cs` | **新增 27 项** |
| `tests/BattleCore.Tests/BattleCore.Tests.csproj` | 复制 `data/presentation` 到测试输出 |

**验收**：978/978（Debug）、表现层 0 警告 0 错误、`p10c_attack.png` / `p10c_hurt.png` 显示姿态随状态变化。
**StateVersion 13 / CoreVersion 14 不变**（动画完全在快照之外）。

### 阶段 10B（2026-11-08）选队、接替流程与输入诊断（D123–D124）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Presentation/TeamSelectionModel.cs` | **新增**：纯 C# 选队模型（槽位游标/换人/确认/建队） |
| `core/BattleCore/Presentation/InputTrace.cs` | **新增**：`InputTrace` + `TracingInputSource`（只读探针） |
| `scripts/BattleScene.cs` | 重写为「模式选择 → 选人/选队 → 1v1/3v3 战斗」；队伍 HUD；`--inputdiag` |
| `scripts/KeyboardInputMap.cs` | 增加 `HeldMask` / `HeldKeys` 只读诊断视图（不改映射逻辑） |
| `tests/BattleCore.Tests/TeamSelectionTests.cs` | **新增 13 项** |
| `core/BattleCore/Team/TeamDefinition.cs` | 人数不再硬绑 3（只要求非空），算法一律读 `Count` |

**DoubleKO 诊断结论**：输入链路健康（`sample: P1=A P2=A` → 双方同帧 Startup → 同帧 0 HP）；
上一轮失败的真因是 **Rei 的 JSON 没有裸 5A**。**未修改** `KeyboardInputMap` / `InputBuffer`。

**验收**：951/951（Debug）、表现层 0 警告 0 错误、6 张 Godot 图形实证。**StateVersion 13 / CoreVersion 14 不变**。

### 阶段 10A（2026-11-08）3v3 队伍赛核心（D120–D122）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Team/TeamDefinition.cs` | **新增**：`TeamMemberDefinition` / `TeamDefinition`（有序三人、满血值）/ `TeamSlot` |
| `core/BattleCore/Team/TeamMatchRules.cs` | **新增**：交锋计时规则（默认 99 秒，可关闭） |
| `core/BattleCore/Team/TeamMatchState.cs` | **新增**：`TeamSideState`（独立淘汰判据 + 逐槽体力）与队伍赛权威状态 |
| `core/BattleCore/Team/TeamMatchEvent.cs` | **新增**：`(BoutNumber, EventId)` 组合身份 |
| `core/BattleCore/Team/TeamMatchSession.cs` | **新增**：每场交锋一个 `BattleSession`，接替/终局/重开 |
| `core/BattleCore/Match/FighterCatalog.cs` | `FighterCatalogEntry.MaxHealth` + `CreateTeam(teamId, ids)` |
| `tests/BattleCore.Tests/TeamMatchTests.cs` | **新增 30 项** |

**验收**：938/938（Debug）、表现层 0 警告 0 错误。**StateVersion 13 / CoreVersion 14 不变**。

### 阶段 09 最终专项补修（2026-11-08）批量推进 / 事件时序 / 结果原因一致性（D117–D119）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Simulation/BattleSession.cs` | `Advance` 新增可选 `stopAfterCommit`：整帧（状态/输入/记账/观察者）落定后判定是否停止本批次；未用整帧预算记 `RecordDroppedSteps` |
| `core/BattleCore/Match/MatchSession.cs` | `Advance` 传 `() => !State.IsRoundLive`；`OnFrameCommitted` 改为「计时投影 → 计分 → Validate → 发布事件」；新增 `_publishing` 重入拒绝 |
| `core/BattleCore/Match/MatchState.cs` | `RecordRoundResult` 与 `Validate` 共用 `RoundState.ValidateOutcomeReasonPair`（D119） |
| `core/BattleCore/State/RoundState.cs` | 抽出公开谓词 `IsValidOutcomeReasonPair` + `internal ValidateOutcomeReasonPair`（纯提取，行为不变） |
| `tests/BattleCore.Tests/Stage09EdgeCaseTests.cs` | **新增 52 项**：P1-A 12 / P1-B 8 / P2-A 22 / 跨系统场景 |

**修复前实测**：5 帧预算下 KO 于第 2 帧 → `Advance` 返回 **5**（应 2）；`RoundEnded` 观察者读到
`RoundInProgress`、`P1Wins = 0`；观察者抛异常时计分完全丢失（`P1Wins = 0`）；
9 种非法结果/原因组合被 `RecordRoundResult` 接受。

**验收**：**908/908**（Debug/Release）、四构建 0 警告 0 错误、Godot 无头导入 `[ DONE ]`、
图形实测 `p09fix_round1_win.png` / `p09fix_match_win.png` / `p09fix_time_over.png`。
变异验证 **3/3 捕获**、`RESTORED=True`。**StateVersion 13 / CoreVersion 14 不变**。

**未完成（如实记录）**：图形层未构造出真 DoubleKO 截图（同渲染帧双按会被 `InputBuffer` 合并为一次采样），
该项由单元测试覆盖；导出模板仍未安装。

### 阶段 09D（2026-11-08）角色与招式 JSON 数据外置（D113–D116）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Data/FighterDataDtos.cs` | **新增**：Schema v1 DTO（fighter / movement / command / move / attack / cancel / throw / projectile / fraction） |
| `core/BattleCore/Data/FighterDataException.cs` | **新增**：带 `SourcePath` / `FieldPath` / `Reason` 的加载错误 |
| `core/BattleCore/Data/FighterDataLoader.cs` | **新增**：纯 C# 加载器（字符串/Stream → 严格校验 → `FighterDefinition` / `FighterCatalog`），不依赖 Godot |
| `data/fighters/Kai.json` / `Rei.json` | **新增**：与 `DemoRoster` 逐字段等价的招式数据（含取消/投技/飞行道具） |
| `scripts/BattleScene.cs` | 开赛前经 `FileAccess` 一次性加载 `res://data/fighters/*.json` 构建目录（失败即明确报错） |
| `export_presets.cfg` / `.gitignore` | 导出预设加入 `*.json` 过滤器；预设文件纳入版本管理 |
| `tests/BattleCore.Tests/FighterDataTests.cs` | **新增 27 项**：加载、等价性、战斗行为、错误处理 |
| `tests/BattleCore.Tests/Stage09AcceptanceTests.cs` | **新增 8 项**：阶段 09 场景 A–E、10000 帧确定性、30/60/144fps、StepExact/Advance |

**验收**：856/856 Debug/Release、四构建 0 错误、Godot 无头导入 `[ DONE ]`、图形运行
`p09d_json_loaded.png` / `p09d_json_fight.png`（P2 970/1000、Hitstun、Combo 1）。
**StateVersion 13 / CoreVersion 14 不变**。**导出模板未安装**，未做真实导出包实测（已配置 `include_filter=*.json`）。

### 阶段 09C（2026-11-08）选人、回合过渡与完整比赛流程（D110–D112）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Match/FighterCatalog.cs` | **新增**：数据驱动角色目录（ID 唯一、镜像合法、按 ID 生成 `FightRoster`）；09C 由 `DemoRoster` 构建 |
| `scripts/BattleScene.cs` | 重写：持有 `MatchSession` + `FighterCatalog`；选人界面 / 过渡 / 终局 / 重开 / 返回选人；比赛 HUD 全只读；界面按键拦截与每回合输入映射重建；`--hp` / `--match-time` / `--duel` 仅命令行调试参数 |
| `tests/BattleCore.Tests/FighterCatalogTests.cs` | **新增 14 项** |

**键位**：A/D = P1 切换、←/→ = P2 切换、J / `,` = 双方确认、Enter = 开赛、Space = 下一回合、
F5 = 重开、ESC = 返回选人、F1 暂停、F2 判定框（全部显示在画面上）。
**验收**：821/821 Debug/Release、四构建 0 错误；Godot 图形实测 7 张截图（选人 / Round1 / KO 过渡 /
P1 MATCH WIN / 镜像 Kai vs Kai / 重开改选 Rei vs Kai / Time Over DRAW）。
**StateVersion 13 / CoreVersion 14 不变**（09C 只新增独立类型与表现层）。

### 阶段 09B（2026-11-08）回合计时与 Time Over（D107–D109）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/State/BattleState.cs` | 新增 `int? RoundTimerRemainingFrames`（默认 null = 旧 API 兼容）+ 构造期范围校验；`CreateDefault` 版本 13 |
| `core/BattleCore/State/RoundState.cs` | `RoundOutcome.Draw = 4`；新增 `Reason` 与结果-原因自洽校验；`ForOutcome(outcome, reason)` / `Finish(outcome, reason)` 重载；旧单参数签名保留 |
| `core/BattleCore/Simulation/TimeOverResolver.cs` | **新增**：`checked long` 交叉相乘比例裁决 |
| `core/BattleCore/Simulation/BattleSimulation.cs` | `Step` 第 7b 阶段递减权威时钟并按比例裁决 Time Over（KO 优先）；终局冻结保留时钟；`CollectEvents` 发 `TimeOver` |
| `core/BattleCore/Simulation/BattleEvent.cs` | 新增 `TimeOver = 16` |
| `core/BattleCore/Match/MatchSession.cs` | 初始状态注入 `rules.RoundTimerFrames`；`OnFrameCommitted` 改为只读投影内核时钟，计分原因取 `RoundState.Reason` |
| `core/BattleCore/BattleCoreInfo.cs` | StateVersion 12 → 13、CoreVersion 13 → 14；`MaxRoundTimerFrames` |
| `tests/BattleCore.Tests/RoundTimerTests.cs` | **新增 24 项** |

**验收**：Debug/Release 807/807、四构建 0 错误；783 项既有测试零改动。
**注意**：`StateVersion` 已升到 13 / `CoreVersion` 14（格式边界，项目仍无序列化快照，无迁移负担）。

### 阶段 09A（2026-11-08）多回合比赛（D104–D106）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Match/MatchRules.cs` | **新增**：比赛规则（2 胜 / 5 回合 / 99 秒 / 超时开关）+ 构造期校验 |
| `core/BattleCore/Match/MatchState.cs` | **新增**：不可变比赛状态、四阶段、计分唯一入口、`Validate` 全部不变式 |
| `core/BattleCore/Match/MatchEvent.cs` | **新增**：`MatchEvent` / `MatchEventId` 组合身份 `(RoundNumber, EventId)` |
| `core/BattleCore/Match/MatchSession.cs` | **新增**：每回合独立 `BattleSession` + `InputBuffer` + 初始状态；开赛/下一回合/重开/返回选人/逐帧提交回调 |
| `core/BattleCore/State/RoundEndReason.cs` | **新增**：结束原因枚举（KO / DoubleKO / TimeOver / TimeOverDraw），09B 接入内核 |
| `tests/BattleCore.Tests/MatchTests.cs` | **新增 20 项** |

**方案选择**：方案一（多 BattleSession），**方案二被否决**（会破坏帧同步契约与 D89）。
**跨帧身份**：裸 `EventId` 只在单回合内唯一，跨回合用 `(RoundNumber, EventId)`。
**验收**：783/783；Godot 表现层构建 0/0；StateVersion 12 / CoreVersion 13 不变。
**边界**：09A 计时尚未接入内核时钟与 Time Over（09B），暂只能由 KO/DoubleKO 结束比赛。

### 阶段 08 最终专项补修（2026-11-08）跨系统边界（D100–D103）—— 阶段 08 最终结项

基线 `6396cf2b83328c720493fcd441c6ed101d8b84d0`（HEAD = origin/main，工作区干净，722 项）。
四项疑点**全部真实复现**：P1-A 三条中断路径均抛 `角色 0 处于非动作状态 Hitstun / AirHitstun /
Knockdown 但携带投掷物生成掩码 1`；P1-B 冻结方 0→−2 / 镜像 0→+2 / 墙角 580→578；
P2-A 首帧相杀 `AttackActive` 期望 2 实际 0；P2-B 防御 `HitstopRemainingFrames` 期望 3 实际 0、
Blockstun 提前 3 帧结束。修复前 38 项专项测试 **21 失败 / 17 通过**。

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Simulation/BattleSimulation.cs` | **D100** 新增 `NormalizeSpawnMask`（结算末端按攻击实例核账）；**D101** `ApplySeparation` 增加 `frozenThisFrame` 固定锚点分支；**D102** 两份结算前快照移到 `ApplyStrikeClash` 之前；**D103** 新增 `ApplyRemoteHitstop`，命中/防御分支共用 |
| `tests/BattleCore.Tests/Stage08EdgeCaseTests.cs` | **新增 16 项**：P1-A 三条中断 / 实例生命周期 / 实体独立存活 / 重放；P1-B 冻结与 D73 的 8 个场景 |
| `tests/BattleCore.Tests/Stage08EventTests.cs` | **新增 22 项**（含 0/1/3/8 帧 Theory）：P2-A 相杀事件、P2-B 防御停顿、场景 A–F |
| `tests/BattleCore.Tests/Stage08AcceptanceTests.cs` | **新增 3 项**：不同渲染帧率、`StepExact`/`Advance`、10000 帧确定性长跑 |
| `docs/ARCHITECTURE.md` / `PROGRESS.md` / `AGENT_HANDOFF.md` | 新增 D100–D103、K60–K63、本轮验收记录 |

**StateVersion 12 / CoreVersion 13 不变**（快照格式未变）。**变异验证 4/4 捕获**，恢复后逐字节
校验 `RESTORED=True`。**验收**：Debug/Release **763/763**、四构建 0/0、Godot 无头导入 `[ DONE ]`、
Godot 图形运行 `shots/p08fix_projectile_block.png` / `p08fix_block_hitstop.png`（启动期 1 条帧积压
警告，非错误）。原有 722 项零改动零跳过，无重大回归。**阶段 08 正式结项，阶段 09 可启动
（须主人另下指令）**。

### 阶段 08D（2026-11-08）飞行道具（D99）—— 阶段 08 结项

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/ProjectileDefinition.cs` | **新增**：类型 ID / 命中载荷（复用 `AttackDefinition`）/ 速度 / 寿命 / 同类型上限 / 出生帧 |
| `core/BattleCore/State/ProjectileState.cs` | **新增**：权威实体（Id/Owner/Type/SourceMove/位置/速度/寿命）+ `Validate` |
| `core/BattleCore/State/BattleState.cs` | 新增 `Projectiles`（只读复制）与 `NextProjectileId`（单调分配、越界拒绝） |
| `core/BattleCore/Combat/MoveDefinition.cs` | 新增 `Projectiles`（出生帧须在 Active 内、类型 ID 唯一） |
| `core/BattleCore/State/FighterState.cs` | 新增 `ProjectileSpawnMask`（每次发动只生成一次；非动作状态必须为 0） |
| `core/BattleCore/Simulation/BattleEvent.cs` | 新增 `ProjectileHit`(13) / `ProjectileBlocked`(14) / `ProjectileClash`(15) |
| `core/BattleCore/Simulation/BattleSimulation.cs` | `StepProjectiles`：运动/寿命/越界 → P×P 对撞 → P×F 命中/防御 → 生成；终局冻结保留实体 |
| `core/BattleCore/Combat/DemoRoster.cs` | 双方新增 `5C`（轻脚键 → Fireball：速度 6 / 寿命 90 / 伤害 40 / 停顿 4） |
| `core/BattleCore/BattleCoreInfo.cs` / `State/BattleState.cs` | StateVersion 11 → 12、CoreVersion 12 → 13 |
| `scripts/DebugBoxDrawer.cs` / `BattleScene.cs` | 绘制投掷物碰撞盒（橙色）+ HUD `Projectiles:` 行 |
| `tests/BattleCore.Tests/ProjectileTests.cs` | **新增 24 项** |

**验收**：722/722；四构建 0/0；图形 `shots/p08d_projectile_flight.png` / `p08d_projectile_hit.png` /
`p08d_projectile_clash.png`；10000 帧确定性长跑与中间快照重放均通过。

### 阶段 08C（2026-11-08）攻击相杀（D98）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/HitDetection.cs` | `FighterBoxes.HitboxesOverlap`：两侧攻击框逐对正面积重叠判定（D76） |
| `core/BattleCore/Simulation/BattleEvent.cs` | 新增 `StrikeClash`(12) |
| `core/BattleCore/Simulation/BattleSimulation.cs` | 第 6a 阶段相杀判定（重叠 + 双方都没命中）；`ApplyStrikeClash` 让双方进入本招式 Recovery 并施加停顿；相杀同时打断投技；事件发射 |
| `core/BattleCore/BattleCoreInfo.cs` | CoreVersion 11 → 12（**StateVersion 不变**：快照格式未变） |
| `tests/BattleCore.Tests/StrikeClashTests.cs` | **新增 15 项** |

**验收**：698/698；四构建 0/0；图形 `shots/p08c_strike_clash.png`。
**变异验证**：去掉「双方都没命中」条件 → 2 项失败（已恢复）。

### 阶段 08B（2026-11-08）地面投技（D97）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/ThrowDefinition.cs` | **新增**：投技数据（范围 / 生效窗口 / 伤害 / 倒地·起身 / 平移 / 停顿 / 可投类别）+ `AllowsTarget` 状态归类 |
| `core/BattleCore/Combat/MoveDefinition.cs` | 新增 `Throws`（窗口须在 Active 内、同一招式内 ThrowId 唯一） |
| `core/BattleCore/State/FighterState.cs` | 新增 `LastThrowStartFrame`（被投实例去重记录） |
| `core/BattleCore/Simulation/BattleEvent.cs` | 新增 `ThrowAttempt`(9) / `ThrowHit`(10) / `ThrowClash`(11) |
| `core/BattleCore/Simulation/BattleSimulation.cs` | 结算前采集投技尝试；第 6b 阶段仲裁（打击打断投技 / 互投冲突 / 成功）；成功后扣血 + Knockdown + 位移钳制 + 停顿；事件发射 |
| `core/BattleCore/Combat/DemoRoster.cs` | 双方新增 `5D`（重脚键 → StandingThrow，范围 70 / 伤害 60 / 倒地 12 / 起身 8 / 平移 20 / 停顿 6） |
| `core/BattleCore/BattleCoreInfo.cs` / `State/BattleState.cs` | CoreVersion/StateVersion 10 → 11 |
| `tests/BattleCore.Tests/ThrowTests.cs` | **新增 20 项** |

**验收**：683/683；四构建 0/0；图形 `shots/p08b_throw_hit.png` / `p08b_throw_clash.png`。
**变异验证**：互投改成「P1 固定胜出」→ `MutualThrows_SameFrame_ClashWithoutDamage` 失败（已恢复）。

### 阶段 08A（2026-11-08）命中停顿 Hitstop 与动作有效时间（D96）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/AttackDefinition.cs` | 新增 `HitstopFrames`（0..`MaxHitstopFrames`=30，构造期校验） |
| `core/BattleCore/State/FighterState.cs` | 新增 `MoveElapsedFrames`（动作有效经过帧数）与 `HitstopRemainingFrames`，含不变式 |
| `core/BattleCore/Combat/FighterStateMachine.cs` | `Tick`/`FighterActionTick` 携带并返回动作经过帧数；取消窗口改用它；`IsInCancelWindow` 去掉 frame 参数 |
| `core/BattleCore/Simulation/BattleSimulation.cs` | 冻结分支（只推进识别）；`frozenThisFrame` 让 `AttackActive` 快照跳过冻结帧；结算写入双方停顿（取最大值）；中断/换招清空动作时间 |
| `core/BattleCore/Combat/DemoRoster.cs` | 演示 5A 配置 `hitstopFrames: 4` |
| `core/BattleCore/BattleCoreInfo.cs` / `State/BattleState.cs` | CoreVersion/StateVersion 9 → 10 |
| `scripts/BattleScene.cs` | HUD 显示 `HITSTOP(n)`；取消行改用 `MoveElapsedFrames` |
| `tests/BattleCore.Tests/HitstopTests.cs` | **新增 17 项** |
| `tests/BattleCore.Tests/HitDetectionTests.cs`、`CombatEventTests.cs` | 4 条时序断言按新规则更新（详见 PROGRESS 08A 表格） |

**验收**：663/663；四构建 0/0；图形 `shots/p08a_hitstop_hit.png` / `p08a_hitstop_block.png`。
**变异验证**：把取消窗口改回 `frame − moveStartedFrame` → `CancelWindow_SurvivesHitstop` 失败（已恢复）。

### 阶段 07 最终专项补修（2026-11-07）空中受击推挤 / 防御击退摩擦（D94/D95）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/FighterLogicState.cs` | **新增 `IsAirborneLike()`**（= `Airborne \| AirHitstun`）：空中/地面资格的唯一判定；XML 文档明确「不能改用 PositionY 判断」 |
| `core/BattleCore/Simulation/BattleSimulation.cs` | `bothGroundAtFrameStart` 与 `ApplySeparation` 早退改用 `IsAirborneLike()`；`ResolveSettlement` 的 `defenderAirborne` 复用同一判定（消除重复状态列表）；`ApplySeparation` 文档补充 D94 |
| `core/BattleCore/Combat/AttackDefinition.cs` | 构造期校验改为「命中击退 **或** 防御击退 > 0 就必须有正摩擦」；`groundKnockbackFriction` / `GroundKnockbackFriction` 文档明确为**通用**摩擦（D95） |
| `tests/BattleCore.Tests/AirSeparationTests.cs` | **新增 12 项** |
| `tests/BattleCore.Tests/BlockKnockbackTests.cs` | **新增 13 项** |

**修复前实测**：AirHitstun 与地面角色重叠会被推成 −15/45（期望 0/30）；越身候选 15 被回投成 −22.5
（镜像 22.5）；落地建序 35/−25 变成 −25/35。`groundKnockback:0, blockKnockback:4, friction:0`
构造通过，真实 `Step` 抛 `角色 1 处于 Blockstun，击退速度 4 与摩擦 0 必须同时为零或同时非零`。

**验收**：646/646（Debug/Release）；四构建 0/0；无头导入 `[ DONE ]`；
图形 `shots/p07fix_air_cross.png` / `p07fix_air_landing.png` / `p07fix_block_knockback.png` /
`p07fix_block_decay_done.png` / `p07fix_wall_corner.png` / `p07fix_jump.png`。
**变异验证**：回退两处判断 → 7 项失败；回退构造校验 → 1 项失败（均已恢复）。
**无 StateVersion/CoreVersion 变更**（只是判定与构造校验，快照格式不变）。

### 阶段 07A（2026-11-07）KO 裁决与基础回合终局（D89）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/State/RoundState.cs` | **新增**：`RoundPhase` / `RoundOutcome` / `RoundState`（Phase+Outcome+Winner，`Validate` 三字段自洽、`FromFinalHealth` 唯一判定入口、`Finish` 只允许一次） |
| `core/BattleCore/State/BattleState.cs` | 新增 `Round`（构造参数可选，默认 InProgress）；`StateVersion` 默认 6 → 7 |
| `core/BattleCore/Simulation/BattleSimulation.cs` | **终局冻结分支**（帧号前进、角色/回合/事件不变）；结算后 `RoundState.FromFinalHealth`；事件增加 KO/RoundEnded；流水线文档更新 |
| `core/BattleCore/Simulation/BattleEvent.cs` | 新增 `Knockout`(6) / `DoubleKnockout`(7) / `RoundEnded`(8) 及语义文档 |
| `core/BattleCore/BattleCoreInfo.cs` | `CoreVersion` 6 → 7 |
| `scripts/BattleScene.cs` | 终局横幅 `Result`（P1 WIN / P2 WIN / DOUBLE KO，只读 `RoundState`）、HUD `Round:` 行、F5 重开（重建会话+输入缓冲）、`--hp=` 调试验收参数 |
| `scenes/Battle.tscn` | 新增 `Result` 横幅 Label；HUD 模板加 `Round:` 行 |
| `tests/BattleCore.Tests/RoundResolutionTests.cs` | **新增 17 项** |

**验收**：583/583；四构建 0/0；图形运行三张终局横幅（P1 WIN / P2 WIN / DOUBLE KO）。
**注意**：终局后 `Step` 仍推进帧号（冻结语义），不要据此认为回合可继续；重开必须新建 `BattleSession`。

### 阶段 06 最终收尾（2026-11-07）AttackActive 负载完整性 / 攻击框只读封装（D87–D88）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Simulation/BattleSimulation.cs` | **D87**：`CollectAttackActive` 返回 `AttackActiveSnapshot(bool IsActive, string? MoveId)[]`（结算前快照，含招式身份）；`CollectEvents` 改读快照而非 `fighters[i].MoveId`；`Add()` 增加不变式「AttackActive 必须有非空 MoveId」 |
| `core/BattleCore/Combat/HitDetection.cs` | **D88**：`GetActiveHitboxesWithIndex` 非空结果改 `boxes.AsReadOnly()`（此前直接返回 `List`，可强转后 `Clear()`）；空结果仍共享 `Array.Empty` |
| `tests/BattleCore.Tests/CombatEventTests.cs` | **新增 5 项**（双方不同招式互中保留各自 MoveId、单方生效被打断仍保留、未被打断仍保留、第 2 个 Active 帧不重复、重放逐位一致）+ 2 处既有测试补负载断言（AttackActive / Blocked / BlockstunStarted 的 MoveId） |
| `tests/BattleCore.Tests/HitDetectionTests.cs` | **新增 3 项**（非空结果只读：强转/泛型与非泛型写操作全部拒绝且内容逐位不变；空结果共享实例；多框顺序 + 原始 AttackIndex + 与几何视图一致） |
| `tests/BattleCore.Tests/MultiAttackContactTests.cs` | 错开窗口用例补 AttackActive MoveId 断言 |

**复现**：双方不同招式同帧互中时两个 AttackActive 的 MoveId 均为 **null**（修复前）；
`GetActiveHitboxesWithIndex` 非空结果可强转 `List<ActiveHitbox>` 并 `Clear()` 成功（修复前）。
**验收**：566/566（Debug/Release）；四构建 0/0；无头导入 `[ DONE ]`；图形截图
`shots/p06final_hit_verify.png` 与上一轮 **md5 逐位相同**（逻辑 119 / HP 970 / Hitstun(12) / id=1890）。
既有 558 项无一删除或削弱。

### 阶段 06 专项补修（2026-11-07）多攻击框序号 / 实扣伤害 / 事件契约（D84–D86）

**这一轮是「独立交接复核」**：新 Agent 不采信 06A–06C 报告，先跑基线（540/540 属实），
再逐文件审查并在**先写失败复现**后修复。缺陷与复现见 `PROGRESS.md` 同名章节。

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/HitDetection.cs` | **D84**：新增 `ActiveHitbox(FixedRect Box, int AttackIndex)` + `FighterBoxes.GetActiveHitboxesWithIndex`；`GetActiveHitboxes` 退化为几何视图（调试绘制 API 不变）；`HitDetection.Collect` 改用<b>原始序号</b>（此前用过滤后下标） |
| `core/BattleCore/Simulation/BattleSimulation.cs` | **D85**：结算用 `Health前 − Health后` 覆盖 `CombatSettlement.Damage`，`Hit.Amount` 直接取该值。**D86**：新增 `CollectAttackActive`，在 `ApplyCombatSettlement` **之前**取结算前视图（该函数原地改写数组）；`CollectEvents` 改为消费布尔标记；`Add` 增加帧号上限检查 |
| `core/BattleCore/Simulation/BattleEvent.cs` | **D86**：新增 `MaxFrameForEventIds = int.MaxValue / MaxEventsPerFrame`（134,217,727）；AttackActive 文档改为「本帧曾进入生效状态、每次发动一次」 |
| `tests/BattleCore.Tests/MultiAttackContactTests.cs` | **新增 8 项**（原始序号、两帧各自结算、镜像、Low/High 防御表、同帧双框优先级、错开窗口只发一次 AttackActive） |
| `tests/BattleCore.Tests/CombatEventTests.cs` | **新增 10 项**（实扣伤害 5 组 Theory、防御无 Hit、互中各自实扣、同帧互中两个 AttackActive、Step 失败零事件、EventId 越界显式失败） |

**验收**：558/558（Debug/Release）；四构建 0/0；无头导入 `[ DONE ]`；图形运行
`shots/p06fix_hit_verify.png`（`HP 970` + `Hitstun(12)` + `Event ... (id=1890)` = 118×16+2）。
**未改**：EventId 仍 `int`（25.9 天余量，升级 long 需整体评估）；D83 提交时序本来就对，
只补测试固定。既有 540 项无一删除或削弱。

### 阶段 06C（2026-11-07）攻击实例去重、确定性事件流（D81–D83）—— 阶段 06 结项

| 文件 | 变更 |
|---|---|
| `core/BattleCore/State/FighterState.cs` | **D81**：新增 `MoveStartedFrame`（动作实例身份，消费同帧赋值）+ `LastHitAttackStartFrame`（被击者已结算记录）；Validate：动作中 ≥ 0、非动作 −1 |
| `core/BattleCore/Combat/FighterStateMachine.cs` | 消费同帧赋发动帧；动作阶段间携带；`FighterActionTick.MoveStartedFrame` |
| `core/BattleCore/Simulation/BattleSimulation.cs` | **D81 去重**：结算前比对「攻击者发动帧 vs 被击者记录」，同实例跳过（被防御也算已结算）；命中/防御写入记录、中断清空发动帧。**D82 事件**：`CollectEvents`（MoveStarted → AttackActive → 逐被击者 Hit/Blocked + 硬直开始，EventId = Frame×16+序号，上限 16 显式失败） |
| `core/BattleCore/Simulation/BattleEvent.cs` | **新增**：`BattleEventType` + `BattleEvent`（不可变值类型） |
| `core/BattleCore/Simulation/BattleStepResult.cs` | 新增 `Events`（只读包装） |
| `core/BattleCore/Simulation/BattleSession.cs` | **D83**：新增 `eventsCommitted` 观察者（frameCommitted 后、同一 try/catch，Faulted 语义一致）；Advance/StepExact 捕获 Step 结果投递事件 |
| `core/BattleCore/Combat/HitDetection.cs` | Contacts 只读包装（D61 先例） |
| `core/BattleCore/State/BattleState.cs` / `BattleCoreInfo.cs` | StateVersion/CoreVersion 5 → 6 |
| `scripts/BattleScene.cs` + `Battle.tscn` | HUD 事件行（最近一帧事件：类型/双方/招式/数值/EventId） |
| `tests/BattleCore.Tests/CombatEventTests.cs` | **新增 12 项**（去重 4、事件 4、会话 3、长跑 1） |

**验收**：540/540；四构建 0/0；无头导入 `[ DONE ]`；图形运行命中帧事件行
（`Event: HitstunStarted P1→P2 StandLightPunch 12 (id=1730)`，EventId 公式一致）、
单次命中（970）、防御不掉血（1000 + Blockstun）。
**注意**：多帧攻击的重复结算由 D81 去重消除——06B 时代的「Blockstun 穿透」与「多帧双扣」已不复存在。

### 阶段 06B（2026-11-07）命中、伤害、防御与硬直（D77–D80）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/FighterLogicState.cs` | 新增 `Hitstun=7`/`Blockstun=8` + 转换图 |
| `core/BattleCore/State/FighterState.cs` | 新增 `StunRemainingFrames`；Validate：硬直状态必须 ≥ 1、其他状态必须 0 |
| `core/BattleCore/Combat/AttackDefinition.cs` | 新增 `Damage`/`HitstunFrames`/`BlockstunFrames`（构造期校验） |
| `core/BattleCore/Combat/FighterStateMachine.cs` | 硬直倒计时分支（归零同帧回待机更新与消费；剩余 < 1 拒绝）；`FighterActionTick.StunRemainingFrames` |
| `core/BattleCore/Simulation/BattleSimulation.cs` | **D78/D79/D80 结算**：候选 → 结算前视图 → 接触检测 → 双方并行解析（防御表 + max(0, HP−Damage)）→ 统一应用 → 朝向（硬直冻结）→ 快照；`StateVersion`/`CoreVersion` 4→5 |
| `core/BattleCore/Combat/DemoRoster.cs` | 演示招式接入伤害（5A 30/12/8 Mid、236A 50/16/10、623A 60/18/12 High、BackDraft 45/15/10、Sweep 40/14/10 Low） |
| `scripts/BattleScene.cs` + `Battle.tscn` | HUD 增加 `HP: x/y` 行与硬直剩余帧显示（`Hitstun(n)`），面板 400 高 |
| `tests/BattleCore.Tests/CombatSettlementTests.cs` | **新增 19 项**（命中/防御/高度表/互中/中断/边界/完整链路） |

**验收**：528/528；四构建 0/0；图形运行命中（940 + Hitstun）与防御（1000 + Blockstun(8)）截图命中。
**注意**：Blockstun 中不能再防御（既有设计）→ 多帧攻击在 06B 会穿透防御/重复扣血——**06C 攻击实例去重消除**。

### 阶段 06A（2026-11-07）确定性 AABB 攻击判定（D76）

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/FixedRect.cs` | **新增**：AABB（MinX/MaxX/MinY/MaxY 全定点），构造期拒绝零/负宽高；`Overlaps` = 正面积重叠（边接触不算，无 epsilon） |
| `core/BattleCore/Combat/AttackHeight.cs` | **新增**：High/Mid/Low（06B 防御表；数据显式配置，不由 Hitbox Y 推断） |
| `core/BattleCore/Combat/AttackDefinition.cs` | **新增**：攻击框几何（前向/向上偏移 + 半宽高）+ Active 内生效帧范围（`int.MaxValue` 哨兵 = 至末帧）+ 高度 |
| `core/BattleCore/Combat/MoveDefinition.cs` | 新增 `Attacks` 列表（可空 = 无攻击招式）；范围超出 ActiveFrames 构造期失败 |
| `core/BattleCore/Combat/HitDetection.cs` | **新增**：`FighterBoxes`（Hurtbox 姿态查询 / Pushbox 查询 / Active 攻击框查询）+ `HitDetection.Detect`（确定性接触候选；不结算） |
| `core/BattleCore/Simulation/BattleStepResult.cs` | 新增 `Contacts`（只读接触候选） |
| `core/BattleCore/Simulation/BattleSimulation.cs` | Step 末尾基于最终候选位置做接触检测 |
| `core/BattleCore/Combat/DemoRoster.cs` | 演示招式接入攻击框（5A/236A/623A/BackDraft/Sweep，Mid/High/Low 齐备） |
| `scripts/DebugBoxDrawer.cs` | **新增**：顶层判定框调试节点（Hitbox 红/Hurtbox 蓝/Pushbox 绿轮廓；几何全部来自权威查询） |
| `scripts/BattleScene.cs` | F2 调试开关；刷新转发给 DebugBoxDrawer |
| `tests/BattleCore.Tests/FixedRectTests.cs` 等 | **新增 24 项**（几何 6 + 判定框 18） |

**验收**：509/509；四构建 0/0；无头导入 `[ DONE ]`；图形运行攻击框 Active 可见、
Recovery 无框（`shots/p06a_hitbox_active.png` / `shots/p06a_recovery_nobox.png`）。
**注意**：BattleScene 自身 `_Draw` 会被不透明 Background 子节点遮挡——调试绘制必须在
顶层子节点（`DebugBoxDrawer`）进行。

### 阶段 05 最终专项补修（2026-11-07）推挤统一求解 / 防穿越 / GroundY / JumpKind（D73–D75）—— 阶段 05 结项

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Simulation/BattleSimulation.cs` | **D73**：`ApplySeparation` 重写为统一求解——① 帧开始双方都在地面 → 以帧开始顺序为约束（**禁止地面角色单帧交换左右**；高速相向不再穿越）；涉及空中（越身/落地帧）以候选顺序为准（越身合法，新地面顺序落地帧建立）；② 对称分配缺口 + **墙角残差再分配**（触墙方无法承担的份额由仍有墙余量的一方承担：贴墙 640 时对方收敛到 **580**，废除旧 582/640 残留）；③ 奇数原始单位余数显式归 P1；闭式求解无迭代无震荡 |
| `core/BattleCore/Simulation/StageDefinition.cs` | **D74**：构造期拒绝任何非零 `GroundY`（当前场地模型只支持 Y=0 平地；正/负/分数全部 `ArgumentException`） |
| `core/BattleCore/State/FighterState.cs` | **D75**：`Validate` 增加 `Enum.IsDefined(JumpKind)`——未定义跳跃类型（如 999/-1）任何状态拒绝，消息含具体非法值 |
| `tests/BattleCore.Tests/GroundCrossingTests.cs` | **新增 9 项**：32/32 相向不穿越（回投到 ∓30）、32 冲静止者、32v1 精确对半、镜像、百帧无震荡、动作中被冲不穿越、越身合法且落地建序保持、贴墙高速单帧求解、双跑一致 |
| `tests/BattleCore.Tests/StageBoundaryTests.cs` | **新增 9 项 / 更正 1 项**：右墙 580/640（旧断言 582/640 是把求解缺陷固定为预期，已更正）、左墙镜像、双方同推右/左墙、精确间距不动、1 raw 缺口精确消除、贴墙落地残差归落地者、GroundY 拒绝（10/−10/0.5）与默认合法 |
| `tests/BattleCore.Tests/JumpTests.cs` | **新增 7 项**：JumpKind 未定义值（Airborne+999/−1、地面三态+999）Validate 拒绝、BattleState 构造门拒绝 |

**复现证据（修复前实测 22 项失败）**：墙角 582/640（7 项）、高速穿越（6 项）、GroundY 接受非零（3 项）、
JumpKind 999 入快照（5 项）+ 1 项测试自身预期修正。
**变异验证（已还原）**：禁用墙角残差再分配 → 4/5 墙角测试失败；取消帧开始顺序约束 → 5/9 穿越测试失败。
**验收**：485/485；四构建 0 警告 0 错误；无头导入 `[ DONE ]`；图形运行六张截图
（右墙 580/640、左墙 −640/−580 ×2、相向不穿越、大跳空中 37.5、落地不连跳）全部命中。

### 阶段 05B（2026-11-07）跳跃、落地、场地边界、推挤与自动朝向（D66–D72）—— 阶段 05 结项

| 文件 | 变更 |
|---|---|
| `core/BattleCore/Combat/JumpKind.cs` | **新增（D68）**：`JumpKind`（None/ShortHop/HighJump）+ `JumpLaunch`（起跳参数） |
| `core/BattleCore/Combat/AirborneMovement.cs` | **新增（D69）**：空中定点积分纯函数 `Integrate`（固定顺序：vy−g → 坐标 → 触地修正），落地返回 `Landed` |
| `core/BattleCore/Simulation/StageDefinition.cs` | **新增（D70）**：场地配置（MinX/MaxX/GroundY/最小间距 + `ClampX`），构造期校验 |
| `core/BattleCore/Combat/FighterLogicState.cs` | 新增 `PreJump`/`Airborne` 枚举值 + 转换图文档 |
| `core/BattleCore/Combat/MovementDefinition.cs` | **D66**：扩展预备窗口/小跳/大跳初速/重力/水平跳速；校验「大跳 > 小跳」与上限 |
| `core/BattleCore/Combat/FighterStateMachine.cs` | **D67/D68**：起跳边沿（`upHeldLastFrame` 参数）→ PreJump；窗口归零按当帧输入锁定类型/水平方向 → Airborne + `Launch`；Airborne 原样保持；`FighterActionTick.Launch` 字段 |
| `core/BattleCore/State/FighterState.cs` | **D68/D69**：新增 `VelocityX/VelocityY/JumpKind/UpHeldLastFrame`；Validate 新不变式（空中 Y>0 + 跳跃类型；地面贴地零速无类型；PreJump 剩余 ≥ 1） |
| `core/BattleCore/Simulation/BattleSimulation.cs` | 权威流水线重构：逐角色候选（识别→状态机→运动）→ 边界钳制 → 双人推挤（D71：对称分配/平局 P1 左 P2 右/墙优先）→ 帧末自动朝向（D72）→ 全新快照；接受 `StageDefinition` |
| `core/BattleCore/State/BattleState.cs` / `BattleCoreInfo.cs` | StateVersion / CoreVersion 3 → 4 |
| `core/BattleCore/Combat/DemoRoster.cs` | Kai/Rei 各自跳跃参数（预备 3/4 帧，初速 4.0/3.5 与 6.5/6.0 等） |
| `scripts/BattleScene.cs` | HUD 增加双人 `Vel (vx, vy) Jump: <类型>` 行（只读权威数据） |
| `scenes/Battle.tscn` | HUD 面板 300 → 350 高，初始文本 13 行 |
| `tests/BattleCore.Tests/JumpTests.cs` | **新增 23 项**：PreJump 逐帧、小跳/大跳轨迹逐值（14/39 最高点、17/27 落地帧）、分类边界、四向跳、空中无控制、防连跳、再起跳、动作期间不起跳、空中不消费/次帧消费、Validate 不变量、配置校验 |
| `tests/BattleCore.Tests/StageBoundaryTests.cs` | **新增 12 项**：左右墙、空中钳制、相向推挤蠕动、靠墙收敛（582/640）、同位平局、落地冲突、越身朝向、等距不抖动、镜像对称 |
| `tests/BattleCore.Tests/MovementDeterminismTests.cs` | **新增 8 项**：双跑一致、中间快照续跑（含速度/跳跃字段）、Advance vs StepExact、60/144/240 FPS 一致、暂停冻结、10000 帧长跑、推挤顺序公平性 |
| 既有测试更新（5 处，均为 05B 语义变化的合理结果） | `GroundMovementTests`：长跑改宽场地+背向行走（原前提「无边界」已不成立）、镜像相遇 ±30（推挤截停）、MovementDefinition 构造补参；`FighterStateValidationTests`：`(5)`→`(7)`（PreJump=5 已合法）；`FighterStateMachineTests`：Up+Down → 「边沿起跳 / 持住站立」双断言 |

**验收**：460/460（418 基线全部保留）；四构建 0 警告 0 错误；无头导入 `[ DONE ]`；
图形运行四张截图全部命中解析值（小跳 13.5 / 大跳 37.5 / 落地防连跳 / 推挤 582+640）。

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
| K34 | **跳跃/落地/推挤/朝向的帧语义全部有测试 pinned** | 改动积分顺序（D69）、落地消费时机、推挤分配、朝向更新时刻中的任何一项都会破坏逐值断言 | 修改前先读 `JumpTests`/`StageBoundaryTests`/`MovementDeterminismTests` 的注释（含解析推导）；「落地帧不消费」「墙优先于间距」「平局 P1 左 P2 右」是显式选择，不是偶然行为 |
| K35 | **双人推挤由统一求解器完成（D73）**：墙角残差再分配——贴墙方不动、剩余位移全部由另一侧承担（贴墙 640 时对方收敛到 **580**，间距恰 60）；帧开始双方都在地面时以帧开始顺序为约束（高速相向不穿越）；奇数原始单位余数归 P1 | 旧算法「平分后钳制」会残留 582/640 且放任高速地面穿越（修复前实测）；旧测试把 582 固定为预期已更正 | 不要改回「平分后即钳制」的单遍算法（变异验证会使 4 项墙角测试失败）；不要取消帧开始顺序约束（5 项穿越测试失败）；空中越身不受约束（落地帧以候选顺序建立新地面顺序） |
| K36 | **`StageDefinition` 只接受 GroundY=0（D74）** | 落地判定/状态校验/出生高度/Godot 显示全部以 Y=0 为基准，非零配置会产生双重权威地面高度 | 需要平台/斜坡时必须统一扩展全部关联模块与测试，不能只改一个判断 |
| K37 | **`JumpKind` 在 `Validate` 中完整枚举校验（D75）** | `(JumpKind)999` 此前能借 Airborne+Y>0 进入权威快照 | 与 D62 同一模式；`JumpLaunch.Kind` 只由状态机产生，无需额外入口校验 |

| K38 | **`AttackContact.AttackIndex` 是「原始攻击定义序号」，不是过滤后列表下标（D84）** | 曾导致多攻击定义错开生效窗口时用错伤害/高度/硬直（复现：970 期望变 990、蹲防 Low 破防）。几何消费者请用 `GetActiveHitboxes`（只有 Rect），结算请用 `GetActiveHitboxesWithIndex` | 不要用 `move.Attacks[i]` 之外的任何位置推导序号；**同帧多框**的取舍已定为「接触全部产生、结算取最小原始序号」（测试 `SameFrame_TwoActiveHitboxes_*` 固定），改规则要先改测试与文档 |
| K39 | **`Hit.Amount` / `CombatSettlement.Damage` = 实扣伤害（D85）** | 曾报配置伤害（体力 10 受 30 报 30、受 `int.MaxValue` 报 21 亿） | 事件值只能来自结算结果 `Health前 − Health后`；不要为了显示「原始伤害」另开计算路径——需要时新增字段并同步文档/测试。0 伤害仍是有效命中（进 Hitstun，Amount 0） |
| K40 | **`AttackActive` 取结算前视图，且每个攻击实例只发一次（D86）** | 事件收集若放在 `ApplyCombatSettlement` 之后，同帧互中被打断者的事件会全部丢失（复现 0 个）；`ApplyCombatSettlement` 会**原地改写**传入的 `FighterState[]` | 新增任何「结算前事实」类事件都要在结算前采样；`MaxFrameForEventIds = 134217727`（60Hz ≈ 25.9 天），超出显式抛异常不回绕 |

| K41 | **`AttackActive` 的 MoveId 来自结算前快照（D87）** | 上一轮只把「是否生效」提前，负载仍读结算后状态；同帧互中双方被打断 → 两个事件 `MoveId = null`（实测） | 任何「本帧发生过」的事件负载都要在结算前采样（`AttackActiveSnapshot`）；`CollectEvents.Add` 已加不变式：AttackActive 的 MoveId 为空直接抛异常。不要用「延迟 Hitstun」或「保留已中断 MoveId」绕过 |
| K42 | **`GetActiveHitboxesWithIndex` 返回真正的只读集合（D88）** | 此前非空分支直接返回 `List<ActiveHitbox>`，`result is List<ActiveHitbox> l → l.Clear()` 能清空「只读」视图 | 公开集合一律 `AsReadOnly()`/`Array.AsReadOnly`（D61 先例）；空结果共享 `Array.Empty`。新增集合型返回值时先写「强转 + `IList` 写操作」测试 |

| K43 | **终局后 `Step` 仍推进帧号但完全冻结（D89）** | 若误以为「终局可以继续打」，会看到角色永远不动；若改成禁止 Step，`Frame − 初始帧 == TotalSteps` 与 Godot `Advance` 都需要特判 | 冻结是显式选择：零事件、零接触、角色/回合逐位不变。重开必须新建 `BattleState.CreateDefault()` + 新 `BattleSession`（Godot F5 已实现），**不要**复用旧快照或手工清字段 |
| K44 | **`RoundState` 三字段互相约束** | InProgress ⇔ None ⇔ 无胜者；P1Win/P2Win ⇔ 胜者 0/1；DoubleKO ⇔ 无胜者。构造期 `Validate` 拒绝不自洽组合 | 不要为「加一个字段」绕过 `Validate`；胜负只能由 `FromFinalHealth`（最终体力）判定，不要在事件层二次推断 |

| K45 | **连段连续性用结算前状态、重置用帧末状态（D90）** | 硬直归零转待机的那一帧<b>不算</b>连段延续（该帧再被命中按 1 Hit）；误把帧开始状态当依据会让连段多算一帧 | 修改前先读 `ComboCancelTests.HitOnLastHitstunFrame_*` / `HitOnRecoveryFrame_*` 两条边界测试；连段只存在于被击方记录，攻击者自己不累加 |
| K46 | **取消确认必须来自更早帧，且只属于当前攻击实例（D91）** | 同帧命中→同帧取消会让新招式在同一帧继续判定（循环风险）；确认帧若跨实例残留会解锁不该有的取消 | `HitConfirmFrame`/`BlockConfirmFrame` 在换招、中断、回待机时一律清空（`Validate` 强制）；窗口是「自发动帧起算的经过帧数 + 阶段」，不要改成渲染时间或 Active 内相对帧 |
| K47 | **每帧至多取消一次** | 取消后 Tick 立即返回，不存在同帧连锁；这是刻意设计而非遗漏 | 需要「取消链」时应扩展为显式的一帧内多级规则并单独设计阶段顺序，不要放宽 `TryCancel` 的返回时机 |

| K48 | **击退位移发生在运动阶段（D92）** | 若在结算里直接改坐标，会绕过场地钳制（D70）与双人最小间距（D73），墙角/越身行为立即退化 | 位置只能由运动阶段写入；击退速度存 `VelocityX`、衰减用 `KnockbackFriction`，两者必须同时为零或同时非零（速度归零即清摩擦） |
| K49 | **击飞当帧 `AirHitstun` 的 Y 仍为 0（D93）** | `Validate` 允许这个唯一例外（要求垂直速度为正）；下一帧由空中积分抬升。写成「Y 必须 > 0」会让击飞首帧无法提交 | 碰撞/受击框查询直接用 `PositionY`，不要假设空中状态一定离地；落地转 `Knockdown` 由 `LandingKnockdownFrames` 决定 |
| K50 | **空中被击不再跳过（D93），倒地计划由配置保证** | 「击飞必倒地」靠 `AttackDefinition` 构造期强制 `KnockdownFrames ≥ 1`，而不是状态不变式；非击飞攻击打中空中目标落地直接站起 | 改这条规则会影响 `AirborneDefender_*` 与 `KnockdownAndWakeup_*` 两组测试，先读注释再动 |
| K51 | **「是不是空中状态」只有 `IsAirborneLike()` 一个答案（D94）** | 07C 新增 `AirHitstun` 时只改了结算，推挤的两处仍写死 `Airborne`，导致飞行中被地面求解器推动、越身被拉回、落地无法建新序 | 新增任何空中/地面状态时**只改这一个函数**；绝不能改回 `PositionY > 0`（击飞当帧 Y=0，D93）。`bothGroundAtFrameStart` 与 `ApplySeparation` 必须同时改，只改一处仍会错 |
| K52 | **击退摩擦是「地面击退通用」配置（D95）** | 只校验 `GroundKnockback` 会放过「仅防御击退 + 零摩擦」，构造期合法但真实防御结算时整帧失败 | 任何新增「会写 VelocityX 的配置」都要同步加进 `AttackDefinition` 的摩擦不变式；**不要**靠放宽 `FighterState.Validate` 来「修」这类问题 |
| K53 | **冻结角色必须整条流水线一起跳过（D96）** | 只冻动作不冻输入会让缓冲错乱；只冻动作不跳 `AttackActive` 快照会让首个生效帧重复发事件 | 冻结分支只做「推进识别 + 复制其余字段 + 计数 −1」，任何新的逐帧逻辑都要先判断该角色本帧是否冻结 |
| K54 | **取消窗口/UI 的「第几帧」一律用 `MoveElapsedFrames`** | 用 `Frame − MoveStartedFrame` 会在停顿期间继续走，窗口提前过期（有变异测试钉住） | 新增任何「按动作帧计算」的规则（如未来的取消链、连段递减）都从 `MoveElapsedFrames` 取时间 |
| K55 | **投技尝试必须在结算前采集（D97）** | 结算把人打进 Hitstun 之后，「目标可投」的答案就变了，投技会凭空消失 | 采集一次、仲裁一次：不要在 `ApplyThrows` 里重新读状态判断资格；新增投技来源（飞行道具/派生）也走同一张冲突表 |
| K56 | **投技位移是唯一「不在运动阶段」的位置写入（D97）** | 它是显式放置，因此**必须**在同一处做完 D70 边界与 D73 最小间距钳制，否则会贴墙越界或与投技者重叠 | 任何新的「一次性放置」都要照这个模板；不要用 `PositionX = …` 绕过钳制 |
| K57 | **相杀与互中互斥（D98）** | 如果「有重叠就相杀」，D80 的互中会全部消失（既有互中测试大面积失败），而且同一帧会出现「既取消攻击又结算伤害」 | 唯一分界是「有没有人真正打中受击框」：命中优先于相杀。改这条规则前先读 `StrikeClashTests.CloseRange_BothHit_IsTradeNotClash` |
| K58 | **投掷物是快照实体，不是附着在角色上的判定（D99）** | 跟着发射者走会导致「发射者收招/被击中就消失」，也无法表示双弹对撞 | 只用 `ProjectileState` 自己的位置/速度/寿命/来源身份判定；新增投掷物行为请改 `StepProjectiles` 的顺序表，不要在角色身上加特例 |
| K59 | **生成当帧只建立实体（D99）** | 生成帧立刻判定会依赖同一帧内的处理顺序，且「出生点贴着对手」时会凭空多打一下 | `ProjectileSpawnMask` 保证每次发动只生成一次（命中停顿冻结动作时间也不会重复生成） |
| K60 | **掩码按「攻击实例」核账，不按角色（D100）** | 命中/投技结算清了 `MoveId`/`MoveStartedFrame` 却留下 `ProjectileSpawnMask`，`Validate` 整帧失败 | 只在流水线末端 `NormalizeSpawnMask` 一处核账；新增任何会中断招式的路径都自动覆盖，**不要**在每个分支补一句清零，更不要每帧无条件清零 |
| K61 | **冻结资格要传给 D73 求解器（D101）** | 冻结分支跳过了运动，但旧 `ApplySeparation` 仍把缺口对称分摊，冻结角色会滑动 | 新增「本帧冻结」信息时同步传给求解器；不要用「事后恢复坐标」绕过（会留下非法的另一方位置） |
| K62 | **结算前快照必须早于所有状态改写（D102）** | `ApplyStrikeClash` 把 Active 改成 Recovery 后再采集，首个生效帧的两条 `AttackActive` 全丢 | 任何新增的「会改状态」阶段都排在 `CollectAttackActive` / `CollectThrowAttempts` 之后；事件负载只读快照 |
| K63 | **命中/防御停顿共用一个入口（D103）** | 防御分支提前 `return`，跳过了命中分支末尾才写的 Hitstop | 新增「防御派生」分支时统一调用 `ApplyRemoteHitstop`；远程停顿只给目标，不要冻结发射者 |
| K64 | **每回合一个 BattleSession，别去重置 Frame（D104）** | 方案二会让 `Frame − 初始帧 == TotalSteps` 与 D89 终局冻结同时失效；还会让不同回合的 EventId 重号 | 新回合必须重建会话 + 全新 InputBuffer；跨回合去重一律用 `MatchEventId`，不要假设裸 EventId 全比赛唯一 |
| K65 | **计分只有一个入口（D105）** | 终局冻结帧仍会调用提交观察者，若在观察者里无条件加分就会重复计分；DoubleKO 也不能凭空加分 | 只在 `State.IsRoundLive` 且 `Round.IsFinished` 时调用 `RecordRoundResult` 一次；平局只增 `RoundsPlayed` |
| K66 | **新回合要「重建」而不是「清理」（D106）** | 逐字段重置容易漏掉 ProjectileSpawnMask / 命中确认 / 输入边沿 / NextProjectileId | 直接用 `BattleState.CreateDefault()` 等价初态 + `new InputBuffer()`；保留规则/胜场/回合号/选人 |
| K67 | **计时器属于 BattleState，不属于比赛层（D107）** | 09A 的比赛层占位记账在 09B 已移除；两套计时会让「暂停/未提交帧」出现分叉 | 只由 `BattleSimulation.Step` 递减；`null` = 旧 API 兼容；比赛层只读投影，不要自己 −1 |
| K68 | **KO 优先于 Time Over（D108）** | 计时归零就取消同帧合法攻击会破坏 D80 同时性，让最后一帧胜负依赖处理顺序 | 先 `FromFinalHealth`，只有 `!IsFinished` 且计时归零才裁决；比例比较用 `checked long` 交叉相乘，绝不用浮点 |
| K69 | **结果与结束原因正交（D109）** | 把 Time Over 胜利伪装成 Knockout 事件会破坏 D89 语义；平局借用 DoubleKO 会丢失「时间到」这一事实 | 用 `RoundState.Reason` 区分；`RoundOutcome.Draw` 的 `WinnerFighterId` 为 null，事件代码不要对它取 `.Value` |
| K70 | **角色选择走目录，不要在模拟里写死名字（D110）** | 在 `BattleSimulation` 里按角色名分支会让新增角色必须改内核；镜像共用不可变配置是安全的 | 只改 `FighterCatalog` 条目；战斗状态永远在每回合独立的 `BattleState` 里 |
| K71 | **界面按键必须拦截（D111）** | J / `,` 同时是选人确认键与战斗轻拳键；不拦截会变成开局首帧攻击 | 选人/过渡/终局阶段 `_Input` 直接 return；每回合新建 `InputBuffer` 并重建 `KeyboardInputMap` |
| K72 | **过渡与终局不推进战斗帧（D111）** | 继续推进会重复投递 `RoundEnded`、重复扣分、让旧弹进入新回合 | `MatchSession.Advance/StepExact` 在 `!IsRoundLive` 时返回 0；HUD 只读 `MatchState` |
| K73 | **JSON 只是数据，不能变成第二套战斗算法（D113）** | 让模拟层读文件或按配置分支，会把「文件内容」变成隐式输入，破坏确定性 | 只有 `FighterDataLoader` 认识 JSON；`BattleSimulation` 永远只吃 `FightRoster`；开赛前一次性加载 |
| K74 | **定点数别用 double 承载（D114）** | `1.5` 经 double 解析在不同平台/文化下可能末位不同，且会把非法值静默变成 0 | JSON 一律 `{numerator, denominator}` + `checked` 转 Q16.16；分母必须为正；越界即失败 |
| K75 | **迁移必须证明「数值没变」（D115）** | 只验证「能加载」会漏掉悄悄改掉的伤害/帧数，直接污染旧基线 | 摊平全部战斗相关字段逐项比较 `DemoRoster`；保留 C# 演示配置作夹具与回退 |
| K76 | **导出包不会自动带 `.json`（D116）** | Godot 只打包被识别的资源，`.json` 不在默认过滤器中，导出后角色会消失 | `export_presets.cfg` 写 `include_filter="*.json"`；用 `FileAccess` 从 `res://` 读，不写绝对路径 |
| K77 | **批量 Advance 的终止点在「提交之后」，不在「预算之前」（D117）** | 只在批次入口检查一次比赛阶段，会让 KO 之后继续提交终局冻结帧：返回帧数虚高、`StepExact` 与 `Advance` 不一致、`LastEvents` 被空事件覆盖 | 用 `stopAfterCommit` 谓词在整帧落定后判定；剩余整帧预算记 `RecordDroppedSteps`，**不要**回滚累加器（会重复计费），也**不要**循环调 `StepExact` 绕开预算 |
| K78 | **先落定权威 `MatchState`，再发布比赛事件（D118）** | 先发事件后计分会让 `RoundEnded` 观察者读到 `RoundInProgress`/旧胜场；观察者抛异常还会把唯一一次计分吞掉 | `OnFrameCommitted` 顺序固定为「计时投影 → `RecordRoundResult` → `Validate` → 发布」；观察者异常不回滚、不重发、原样上抛；回调里禁止重入修改比赛会话（`_publishing` 显式拒绝） |
| K79 | **结果/原因组合只允许一份实现（D119）** | 比赛层自己写 switch 必然与 `RoundState` 分叉，`DoubleKO + Knockout`、`Draw + Knockout` 会被接受 | 一律调用 `RoundState.IsValidOutcomeReasonPair` / `ValidateOutcomeReasonPair`；新增结果或原因时**只改这一处**，并同步 `MatchState.Validate` |
| K80 | **3v3 不要塞进 BattleState（D120）** | 六人同场会让推挤/相杀/投技/飞行道具与 D89 终局语义全部失效，且所有旧测试要重写 | 队伍赛是比赛层组织多个 1v1 交锋；`BattleSimulation` 一行不改；新回合重建 BattleSession |
| K81 | **淘汰不是 `Health == 0`（D121）** | Time Over 败者可能仍有正体力，用 HP 判存活会让它重新登场、剩余人数算错 | 一律读 `TeamSideState.Eliminated[]`；体力只用于继承与显示；新上场队员用自身 `MaxHealth` |
| K82 | **跨交锋事件必须带交锋号（D122）** | 每场 Frame 从 0 开始，裸 EventId 必然重号，特效去重/回放会把旧事件当新事件 | 用 `TeamMatchEventId(BoutNumber, EventId)`；不要假设裸 EventId 全场唯一 |
| K83 | **选队规则放纯 C#，别写进 Godot 节点（D123）** | 槽位游标、换人回绕、改阵容取消确认这些确定性逻辑写成节点脚本后只能手点验证 | 用 `TeamSelectionModel`；BattleScene 只把按键转成方法调用、把结果画出来 |
| K84 | **双人同帧先看采样掩码，再怀疑输入链路（D124）** | 直接改 `KeyboardInputMap`/`InputBuffer` 会把健康链路改坏，还掩盖真正的配置缺口 | 开 `--inputdiag` 看 `sample: P1=… P2=…` 与 `state: fN …`；本轮真因是 **Rei 无裸 5A**，不是输入缺陷 |
| K85 | **动画只能读权威状态，绝不能反过来写（D125）** | 用 AnimationPlayer 回调扣血/结束硬直/开判定框，会让掉帧与缺素材改变胜负 | 单向流 `FighterState → AnimationResolver → Pose`；招式帧用 `MoveElapsedFrames`，不用动画时钟 |
| K86 | **表现配置独立于战斗 JSON（D125）** | fighter schema 已声明未知字段拒绝，往里加动画字段会直接破坏已发布格式 | 放 `data/presentation/*.json`；缺素材允许降级（`CreateFallback`），格式非法必须带路径报错 |
| K87 | **特效只能由权威事件触发并按身份去重（D126）** | 自己判断「靠得近就是命中」会在相杀/被防御/投技冲突时出现假特效；不去重会每渲染帧重放 | 只用 `FeedbackDirector.TryConsume(scope, event)`；重开/返回选人要 `Reset()`（回合号会重用） |
| K88 | **抖动用固定序列，别用随机数（D126）** | `Random` 会让同一段回放的截图不可复现，也难写回归测试 | 固定偏移表 + 线性衰减 + 取最大值不叠加；只叠加在角色节点，不写回权威坐标 |

**无阻塞性问题。** 编译 0 警告 0 错误，阶段 10 结项验收时 **1012/1012** 测试通过，可正常启动运行。

阶段 10 新增验收测试 `Stage10AcceptanceTests`（8 项）：一人连挑三人、交替淘汰、跨交锋事件身份、
同输入逐位一致、3v3 10000 帧确定性、30/60/144fps 一致、StepExact/Advance 等价、
动画与反馈不改变比赛结果。

**注意**：`Rei` 的 JSON 只有 236A / 214B / 5D / 5C，**没有裸 5A**——写测试或做图形验收时
让 P2 按轻拳不会出招（这是配置事实，不是输入缺陷，见 K84）。3v3 图形验收请用 Kai 镜像。

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
  - **阶段 05A：`feat(movement): implement deterministic fixed-point ground movement`（`9d2e089`），已推送。**
    提交内容：
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
  - **阶段 05B：`feat(movement): implement jumps landing and stage boundaries`（`ba2c426`），已推送。**
    提交内容：
    ```
    新增: core/BattleCore/Combat/{JumpKind,AirborneMovement}.cs
          core/BattleCore/Simulation/StageDefinition.cs
          tests/BattleCore.Tests/{JumpTests,StageBoundaryTests,MovementDeterminismTests}.cs（含 .uid）
    修改: core/BattleCore/Combat/{FighterLogicState,FighterStateMachine,MovementDefinition,DemoRoster}.cs
          core/BattleCore/State/{FighterState,BattleState}.cs
          core/BattleCore/Simulation/BattleSimulation.cs
          core/BattleCore/BattleCoreInfo.cs
          scripts/BattleScene.cs  scenes/Battle.tscn
          tests/BattleCore.Tests/{GroundMovementTests,FighterStateValidationTests,FighterStateMachineTests}.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **阶段 05 专项补修：`fix(movement): resolve corner push and ground crossing edge cases`（`252758c`），已推送。**
  - **阶段 06A：`feat(combat): implement deterministic AABB hit detection`（`4ab34fc`），已推送。**
  - **阶段 06B：`feat(combat): implement hit damage guard and stun`（`90cf0c9`），已推送。**
  - **本轮阶段 06C：`feat(combat): add deterministic combat events and hit deduplication`**
    （创建后用 `git rev-parse HEAD` 读取 SHA）。提交内容：
    ```
    新增: core/BattleCore/Simulation/BattleEvent.cs
          tests/BattleCore.Tests/CombatEventTests.cs（含 .uid）
    修改: core/BattleCore/State/{FighterState,BattleState}.cs
          core/BattleCore/Combat/{FighterStateMachine,HitDetection}.cs
          core/BattleCore/Simulation/{BattleSimulation,BattleStepResult,BattleSession}.cs
          core/BattleCore/BattleCoreInfo.cs
          scripts/BattleScene.cs  scenes/Battle.tscn
          tests/BattleCore.Tests/{CombatSettlementTests,HitDetectionTests,FighterStateValidationTests}.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 08D：`feat(combat): implement deterministic projectiles`**
    （文档与代码同一提交，SHA 由 `git rev-parse HEAD` 读取，不自引用）。提交内容：
    ```
    新增: core/BattleCore/Combat/ProjectileDefinition.cs
          core/BattleCore/State/ProjectileState.cs
          tests/BattleCore.Tests/ProjectileTests.cs（含 .uid）
    修改: core/BattleCore/Combat/{MoveDefinition,DemoRoster}.cs
          core/BattleCore/State/{BattleState,FighterState}.cs
          core/BattleCore/Simulation/{BattleSimulation,BattleEvent}.cs
          core/BattleCore/BattleCoreInfo.cs
          scripts/{DebugBoxDrawer,BattleScene}.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 08C：`feat(combat): add deterministic strike clashes`**
    （文档与代码同一提交，SHA 由 `git rev-parse HEAD` 读取，不自引用）。提交内容：
    ```
    新增: tests/BattleCore.Tests/StrikeClashTests.cs（含 .uid）
    修改: core/BattleCore/Combat/HitDetection.cs
          core/BattleCore/Simulation/{BattleSimulation,BattleEvent}.cs
          core/BattleCore/BattleCoreInfo.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 08B：`feat(combat): implement deterministic ground throws`**
    （文档与代码同一提交，SHA 由 `git rev-parse HEAD` 读取，不自引用）。提交内容：
    ```
    新增: core/BattleCore/Combat/ThrowDefinition.cs
          tests/BattleCore.Tests/ThrowTests.cs（含 .uid）
    修改: core/BattleCore/Combat/{MoveDefinition,DemoRoster}.cs
          core/BattleCore/State/{FighterState,BattleState}.cs
          core/BattleCore/Simulation/{BattleSimulation,BattleEvent}.cs
          core/BattleCore/BattleCoreInfo.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 08A：`feat(combat): add deterministic hitstop`**
    （文档与代码同一提交，SHA 由 `git rev-parse HEAD` 读取，不自引用）。提交内容：
    ```
    新增: tests/BattleCore.Tests/HitstopTests.cs（含 .uid）
    修改: core/BattleCore/Combat/{AttackDefinition,FighterStateMachine,DemoRoster}.cs
          core/BattleCore/State/{FighterState,BattleState}.cs
          core/BattleCore/Simulation/BattleSimulation.cs
          core/BattleCore/BattleCoreInfo.cs
          scripts/BattleScene.cs
          tests/BattleCore.Tests/{HitDetectionTests,CombatEventTests}.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 07 最终补修：`fix(combat): correct air hitstun push and block knockback validation`**
    （文档与代码同一提交，SHA 由 `git rev-parse HEAD` 读取，不自引用）。提交内容：
    ```
    新增: tests/BattleCore.Tests/AirSeparationTests.cs（含 .uid）
          tests/BattleCore.Tests/BlockKnockbackTests.cs（含 .uid）
    修改: core/BattleCore/Combat/FighterLogicState.cs
          core/BattleCore/Combat/AttackDefinition.cs
          core/BattleCore/Simulation/BattleSimulation.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 07C：`feat(combat): add knockback launch and airborne hit reactions`**
    （文档与代码同一提交，SHA 由 `git rev-parse HEAD` 读取，不自引用）。提交内容：
    ```
    新增: tests/BattleCore.Tests/KnockbackLaunchTests.cs（含 .uid）
    修改: core/BattleCore/Combat/{AttackDefinition,FighterLogicState,FighterStateMachine,
            HitDetection,DemoRoster}.cs
          core/BattleCore/State/{FighterState,BattleState}.cs
          core/BattleCore/Simulation/BattleSimulation.cs
          core/BattleCore/BattleCoreInfo.cs
          scripts/BattleScene.cs
          tests/BattleCore.Tests/{CombatSettlementTests,FighterStateValidationTests}.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 07B：`feat(combat): implement combo tracking and move cancels`**
    （`d3e4ddc`，已推送）。提交内容：
    ```
    新增: core/BattleCore/State/ComboState.cs
          core/BattleCore/Combat/CancelDefinition.cs
          tests/BattleCore.Tests/ComboCancelTests.cs（含 .uid）
    修改: core/BattleCore/Combat/{MoveDefinition,FighterDefinition,FighterStateMachine,
            FighterLogicState,DemoRoster}.cs
          core/BattleCore/State/FighterState.cs
          core/BattleCore/Simulation/BattleSimulation.cs
          core/BattleCore/BattleCoreInfo.cs  core/BattleCore/State/BattleState.cs
          scripts/BattleScene.cs  scenes/Battle.tscn
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 07A：`feat(combat): add deterministic KO and round resolution`**
    （`9848744`，已推送）。提交内容：
    ```
    新增: core/BattleCore/State/RoundState.cs
          tests/BattleCore.Tests/RoundResolutionTests.cs（含 .uid）
    修改: core/BattleCore/State/BattleState.cs
          core/BattleCore/Simulation/{BattleSimulation,BattleEvent}.cs
          core/BattleCore/BattleCoreInfo.cs
          scripts/BattleScene.cs  scenes/Battle.tscn
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 06 最终收尾：`fix(combat): preserve active move identity and readonly hitboxes`**
    （`f3f12e2`，已推送）。提交内容：
    ```
    修改: core/BattleCore/Simulation/BattleSimulation.cs
          core/BattleCore/Combat/HitDetection.cs
          tests/BattleCore.Tests/{CombatEventTests,HitDetectionTests,MultiAttackContactTests}.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **本轮阶段 06 专项补修：`fix(combat): correct attack contact mapping and event damage`**
    （`2955918`，已推送）。提交内容：
    ```
    新增: tests/BattleCore.Tests/MultiAttackContactTests.cs（含 .uid）
    修改: core/BattleCore/Combat/HitDetection.cs
          core/BattleCore/Simulation/{BattleSimulation,BattleEvent}.cs
          tests/BattleCore.Tests/CombatEventTests.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
  - **阶段 09 最终专项补修：`fix(match): enforce terminal batch boundary and committed match events`**
    （本轮提交，SHA 见 `git rev-parse HEAD`）。修改：`BattleSession.Advance` 增加提交后终止谓词、
    `MatchSession` 调整事件发布顺序与重入拒绝、`MatchState` / `RoundState` 共享结果原因校验；
    新增 `tests/BattleCore.Tests/Stage09EdgeCaseTests.cs`（52 项）。测试 856 → **908**。
  - **工作区：干净**（阶段 09 最终专项补修提交并推送后）。
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
2. `docs/ARCHITECTURE.md` — 第 4 节（D1–D88 决策）与第 5 节（未决定问题）
3. `docs/PROGRESS.md` — 已完成/未完成边界、722 项测试、阶段 09 安全入口
4. `core/BattleCore/Simulation/FixedStepScheduler.cs` — **阶段 02 核心**，理解时间调度
5. `core/BattleCore/Simulation/BattleSession.cs` — 输入帧号与状态帧号的绑定规则
6. `core/BattleCore/Combat/HitDetection.cs` — 三框几何 + **原始攻击序号**（K38）
7. `core/BattleCore/Simulation/BattleEvent.cs` — 事件契约与 EventId 边界（K40）
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
4e. **阶段 08 已结项**（08A–08D + 最终补修 D100–D103）；**阶段 09A–09D 已全部交付并结项**
   （D104–D106 / D107–D109 / D110–D112 / D113–D116），阶段 09 最终补修（D117–D119，批量推进边界 /
   事件时序 / 结果原因一致性）已推送，**908 项测试**。
   阶段 10 安全入口（按依赖顺序）：① 正式比赛流程补全（3v3 队伍赛 / 选人立绘 / BGM 与演出）；
   ② 防御扩展（空中防御、拆投、防御反击）；③ AI（先做确定性脚本 AI，再做搜索式）；
   ④ 资源与动画管线（精灵/特效/音效，仍需保持 BattleCore 无 Godot 依赖）；
   ⑤ 联机回滚（必须先做 `BattleState` 序列化与版本迁移，当前**完全没有**）。
   **不要**在没有新决策的情况下改动 D96 的冻结语义、D97 的冲突表、D98 的相杀分界、D99 的实体顺序、
   D100–D103 的实例核账 / 冻结锚点 / 快照顺序 / 远程停顿入口。
   阶段 08 安全入口（按依赖顺序，详见 `PROGRESS.md` 阶段 07 总结）：① 比赛规则（多回合/计时/选人，
   建在 `RoundState` 之上）；② Hitstop；③ 投技；④ 飞行道具；⑤ 招式数据外置；⑥ 联机回滚。
   **不要**在没有新决策的情况下改动 D92/D93 的状态簇或 D89 的终局语义。
5. **为现有测试增加用例** — 只要不改动 `BattleCore` 公共契约。
6. **把 `CaptureHelper.cs` 移到 `tools/`** — 需同步改 `scenes/Battle.tscn` 的 `ext_resource`
   与 `NewKOF.csproj` 的 `Compile Include`。
7. **添加 `.sln`** — 纯工程整理。

### 需要先决策 ⚠️

8. **修改 `BattleState` 结构**（增加速度/状态字段）— 先读 `ARCHITECTURE.md` 第 5 节问题 1。
9. ~~**引入碰撞/判定框**~~ **已完成（D76/D84）**：AABB `FixedRect` + 三框职责 +
   原始攻击定义序号映射。改动前先读 K38。
10. ~~**引入事件流**~~ **已完成（D82/D83/D86）**：`(frame, eventId)` 契约、提交后投递、
   AttackActive 结算前视图。改动前先读 K39/K40。
10b. **SOCD 改「后输入优先」** — 必须先扩展 `FrameInput` 记录事件顺序，否则无法重放。

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
- [ ] `dotnet test` 在 `tests\BattleCore.Tests` 返回「通过: 908」
      （若数量变化，说明有 Agent 改过测试，需查 `PROGRESS.md` 是否同步）
- [ ] `MatchSession.Advance` 仍传 `stopAfterCommit: () => !State.IsRoundLive`（D117）：
      终局帧之后不得再提交冻结帧；`TotalSteps` 只随 `CommitStep` 增加
- [ ] `MatchSession.OnFrameCommitted` 的计分与 `Validate` 仍在 `_eventsCommitted` 之前（D118）：
      调换顺序会让观察者读到旧 `MatchState`，并让观察者异常吞掉计分
- [ ] `MatchState.RecordRoundResult` / `Validate` 仍调用 `RoundState.ValidateOutcomeReasonPair`（D119）：
      不要在比赛层另写一份结果/原因 switch
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
- [ ] 运动配置（D64/D66）在 `MovementDefinition`（构造期校验），不在状态机散落速度常量、不在 Godot 计算
- [ ] 跳跃链路（D67/D68/D69）：起跳 = 「上」边沿（`UpHeldLastFrame` 随快照携带，防自动连跳）；
      PreJump 不可取消不消费；类型/水平方向在起跳帧按当帧输入锁定；积分顺序固定
      （vy−g → 坐标 → 触地）；落地帧不消费、次帧消费——帧语义由测试逐值 pinned
- [ ] 场地与双人（D70/D71/D73）：候选 X 统一钳制（含空中）；推挤仅双方非空中、
      对称分配、余数归 P1、**墙角残差再分配（贴墙 640 → 对方 580，不残留 582）**；
      **帧开始双方都在地面时禁止单帧交换左右（防高速穿越）**，越身/落地帧以候选顺序为准；
      朝向帧末更新、仅地面待机、等距保持；输入始终按帧开始朝向解析（D46 不变）
- [ ] `StageDefinition` 只接受 GroundY=0（D74）；`JumpKind` 在 Validate 完整枚举校验（D75）
- [ ] 攻击判定（D76）：`FixedRect` 正面积重叠（边接触不算）；Hitbox 仅 Active 配置帧范围生效；
      镜像只按权威 Facing；不检测自己；Godot 调试框只在顶层节点绘制（父节点 _Draw 会被背景遮挡）
- [ ] 结算（D77–D80）：高度表 GuardCovers 唯一事实来源；防御 = 站/蹲待机 + 按住后（帧开始朝向）；
      命中 max(0, HP−Damage) + Hitstun；双方结果并行解析统一应用（无先手）；空中被击跳过
- [ ] 去重与事件（D81–D83）：同实例（攻击者+发动帧）对同一目标只结算一次（被防御也算）；
      EventId = Frame×16+帧内序号；事件由 Step 产生、Session 提交后才投递（失败帧零事件）；
      eventsCommitted 异常进入 Faulted；Recover 不重发（消费者按 EventId 去重）
- [ ] 补修契约（D84–D86）：`AttackContact.AttackIndex` = **原始**攻击定义序号（结算必须走
      `GetActiveHitboxesWithIndex`，同帧多框取最小序号）；`Hit.Amount` = 实扣伤害
      （`Health前 − Health后`，不是配置伤害）；`AttackActive` 取**结算前**视图且每实例一次；
      `MaxFrameForEventIds` 越界显式失败
- [ ] 收尾契约（D87–D88）：`AttackActive` 的 MoveId 来自结算前快照（同帧互中被打断仍非空；
      `Add` 对空 MoveId 抛异常）；`GetActiveHitboxesWithIndex` 非空结果不可强转 List/数组，
      `IList<T>` 与非泛型 `IList` 写操作抛 `NotSupportedException`
- [ ] 回合契约（D89）：`BattleState.Round` 三字段自洽（`Validate`）；KO 只依据**结算后**最终体力，
      双方同帧归零 = DoubleKO；终局 `Step` 冻结（帧号+1、角色/回合/事件不变）；KO 事件每回合各一次
- [ ] 连段契约（D90）：连续性看**结算前**受击延续状态、重置看**帧末**状态；硬直归零帧不算延续；
      防御不计数并清空；同一实例不重复计数（D81）
- [ ] 取消契约（D91）：规则来自 `MoveDefinition.Cancels`（不在状态机硬编码招式名）；
      窗口 = 经过帧数 + 阶段；确认必须更早帧且同实例；每帧至多取消一次；新实例 `MoveStartedFrame` = 当帧
- [ ] 击退与击飞（D92/D93）：击退位移只在运动阶段逐帧施加（经过 D70/D73）；方向 = 远离攻击者；
      击飞当帧 Y=0 且 vy>0（唯一例外）；空中受击不携带 JumpKind、不可防御；
      落地 → Knockdown(knockdownFrames) → Wakeup(wakeupFrames) → Standing；倒地 Hurtbox 降为蹲下高度
- [ ] 推挤资格（D94）：空中判定只有 `IsAirborneLike()`（Airborne/AirHitstun）；空中不参与地面最小间距，
      但 D70 边界照旧；落地帧按候选顺序建立新地面顺序，此后地面帧防穿越
- [ ] 击退摩擦（D95）：`GroundKnockback` 或 `BlockKnockback` 任一 > 0 时摩擦必须 > 0（构造期拒绝）；
      不放宽 `FighterState.Validate`
- [ ] 命中停顿（D96）：命中/防御当帧结算，后续 N 帧冻结（动作/硬直/击退/空中积分/取消窗口），
      第 N+1 帧恢复；双方同值、同帧多来源取最大；冻结帧只推进输入识别；
      `AttackActive` 快照跳过冻结帧；取消窗口用 `MoveElapsedFrames`
- [ ] 投技（D97）：数据在 `MoveDefinition.Throws`；判定 = Active + 窗口 + 目标可投 + 面前 + 距离；
      不看防御；空中/倒地/起身/PreJump 一律不可投；同一实例只结算一次；
      互投冲突双方都不成立；打击优先于投技；位移经 D70/D73 钳制
- [ ] 相杀（D98）：攻击框正面积重叠 **且双方都没命中受击框** → 双方进 Recovery、无伤害、
      一条 `StrikeClash`；有人真正命中时按 D80 结算（互中/单方命中），不判相杀
- [ ] 飞行道具（D99）：实体在 `BattleState.Projectiles`（只读、ID 单调）；生成当帧不运动不判定；
      阶段顺序 近战 → 投技 → 投掷物 → KO；P×P 同级抵消；P×F 命中即消失、每目标每帧至多一枚；
      实体与发射者状态解耦
- [ ] 冲突裁决顺序为「优先级降序 → 序列长度降序 → ID 序数升序」，与定义顺序无关
- [ ] 胜出指令标记「末方向帧号」已消费（`ConsumedEndFrames`），落败指令不得幽灵触发
- [ ] `frameCommitted` 传入的是帧开始状态（`stateBefore`），其 `Facing` 是当帧输入朝向
- [ ] `Observe` 帧号严格逐帧连续（首次任意非负），非法帧先抛后不改内部状态
- [ ] 攻击实例掩码（D100）：离开招式或换实例就清零；同一实例 Active→Recovery 保留；`Validate` 不放宽
- [ ] 冻结与推挤（D101）：单方冻结由未冻结方承担缺口；双方冻结不动；冻结结束恢复对称分摊；D94 空中资格不变
- [ ] 相杀事件（D102）：两份结算前快照先于 `ApplyStrikeClash`；首帧相杀仍有 2 条 `AttackActive`，顺序在 `StrikeClash` 之前
- [ ] 远程停顿（D103）：`ProjectileBlocked` 与 `ProjectileHit` 共用 `ApplyRemoteHitstop`；只冻目标、取 max、N 帧精确
- [ ] 选人与比赛流程（D110–D112）：`FighterCatalog` 数据驱动、四种选人组合含镜像、界面按键不泄漏进
      战斗缓冲、每回合全新 InputBuffer、过渡/终局不推进战斗帧、HUD 只读权威状态
- [ ] 回合计时（D107–D109）：`BattleState.RoundTimerRemainingFrames` 权威时钟、5940 帧精确递减、
      Hitstop 帧扣时 / 暂停与未提交帧不扣时、KO 优先于 Time Over、比例用 checked long 交叉相乘、
      `TimeOver` 只在归零帧一条、`RoundEnded` 恰好一次、`RoundState.Reason` 区分 KO 与超时
- [ ] 多回合比赛（D104–D106）：每回合独立 BattleSession、跨回合事件身份 `(RoundNumber, EventId)`、
      计分只在 `RecordRoundResult` 一次发生、DoubleKO 不加分、终局禁止再开回合、新回合全新 InputBuffer
- [ ] JSON 加载（D113–D116）：Schema v1、整数分数、未知字段/缺失必填/重复 ID/非法窗口全部报错并带路径、
      `FighterDataLoader` 不引用 Godot、Kai/Rei 与 `DemoRoster` 逐字段等价、导出预设含 `*.json`
- [ ] 阶段 09 集成（场景 A–E）：完整比赛→重开、Time Over 比例裁决、连续 DoubleKO、镜像双弹对撞、
      改 JSON 伤害改变结果、10000 帧确定性、30/60/144fps 一致、`StepExact`/`Advance` 等价
- [ ] `docs/ARCHITECTURE.md` 第 4 节决策（D1–D116）与代码实现一致
- [ ] `docs/PROGRESS.md` 已完成项与实际代码一致

**工作结束时必须做的事：**

- [ ] 更新 `docs/PROGRESS.md`（已完成/测试状态/下一阶段）
- [ ] 更新 `docs/AGENT_HANDOFF.md` 第 4/5/6 节
- [ ] 若做了架构决策，写入 `docs/ARCHITECTURE.md` 第 4 节，并从第 5 节移除
- [ ] 实际运行测试并在文档中记录真实结果（**不得编造**）
