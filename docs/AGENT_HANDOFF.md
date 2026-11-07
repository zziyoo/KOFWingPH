# AGENT_HANDOFF.md — Agent 交接说明

> **每一位 Agent 在修改代码前必须先读本文件，并核对文档与实际代码是否一致。**
> 最后更新：2026-11-07（阶段 03A 专项补修完成，本地未提交）

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
期望：`失败: 0，通过: 141，已跳过: 0，总计: 141`

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

### 阶段 03A 专项补修（2026-11-07）输入一致性与调试工具 —— 本地未提交

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
| K12 | `InputBuffer.History` **无上限增长** | 约 16 B/帧 ≈ 3.4 MB/小时；长时间挂机会吃内存 | 原型够用。若做长跑/录制，加环形缓冲或按需 `Clear()`（尚未提供，避免过早设计） |
| K13 | **键位硬编码**，无重绑定 | 只有 `KeyboardInputMap.TryMap` 一处映射，改键需改代码 | 键位重绑定 UI 属后续阶段；手柄映射未做 |
| K14 | 两次逻辑帧之间多次按下会**合并成一帧** | 快速双击在 `InputBits` 里只体现为一帧按下 | 按 D31 属已知限制，`CoalescedPressCount` 可观测；逐次事件流留给指令识别阶段 |
| K15 | **普通 `Func<int,FrameInput>` 输入源无法回滚** | 模拟失败后会话会以同一帧号再次调用它；若委托有副作用（如 `buffer.Take`），会重复提交 | 契约要求「同一帧号返回同一输入（幂等）」；生产路径必须传 `IInputSource`（`InputBuffer`），`Take()` 已收为 internal 防误用 |
| K16 | **输入历史只保留最近 600 帧** | 更早的帧被淘汰，`IsHistoryTruncated == true`，不能再用于回放/长窗口识别 | 完整输入录像需独立模块（未实现）；若做重放，先设计录像格式，不要拉长这个缓冲 |
| K17 | `BattleSession.Advance` 允许 `inputSource` 与 `inputProvider` 二选一 | 同时传入会抛 `ArgumentException` | 保持显式；不要为了省参做成隐式优先级 |

**无阻塞性问题。** 编译 0 警告 0 错误，阶段 02 补修验收时 93/93 测试通过（当前总数 141），可正常启动运行。

---

## 6. 未提交文件与工作区状态

- **Git**：https://github.com/zziyoo/KOFWingPH，分支 `main`（跟踪 `origin/main`）
  - `9937b3f` — 阶段 01–02 Godot 工程初始化与固定逻辑帧调度
  - `c003dc2` — 记录远程仓库与代理要求
  - `850c4db` — 阶段 02 补修（调度器记账与帧同步）
  - `0be9919` — 阶段 03A 基础输入链路（**最近一次已推送的功能基线**）
  - `1538395` / `083abd5` — 仅文档
  - **当前 HEAD 请用 `git rev-parse HEAD` 读取**：本轮补修**尚未提交**，
    远端仍停在 `083abd5`。本节记录的是写文档时的状态，不要当作实时 HEAD。
  - **工作区：有未提交改动**（阶段 03A 专项补修）：
    ```
    新增: core/BattleCore/Input/IInputSource.cs（+ .uid）
          tests/BattleCore.Tests/InputIntegrityTests.cs（+ .uid）
    修改: core/BattleCore/Input/InputBuffer.cs
          core/BattleCore/Simulation/BattleSession.cs
          scripts/BattleScene.cs  scripts/CaptureHelper.cs
          tests/BattleCore.Tests/InputPipelineTests.cs
          docs/ARCHITECTURE.md  docs/PROGRESS.md  docs/AGENT_HANDOFF.md
    ```
    `git status --porcelain` 应恰好输出上面这组文件。
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
  - `shots/`（验收截图：`input_p1p2.png` 双人输入、`input_paused.png` 暂停；
    历史：`battle_01.png`、`battle_02.png`、`final.png`、`p02.png`）
- **安全备份**：`C:\Users\32360\Desktop\NewKOF_backups\NewKOF_pre_phase03a_20261107.tar.gz`
  （阶段 03A 开始前的完整工作区，含当时未提交的补修代码）
- **清理建议**：`bin/obj/.godot` 可安全删除，重新 `dotnet build` + `--import` 即可重建。
- **工作区状态判读**：以 `git status --porcelain` 的**实际输出**为准，不要凭文档猜。
  提交本轮补修后应无输出；提交前应恰好输出第 6 节列出的那组文件。
  若出现 `bin/`、`obj/`、`.godot/`、`shots/`、`.workbuddy/`，说明 `.gitignore` 被改坏。

---

## 7. 下一位 Agent 应优先阅读的文件

**按顺序阅读：**

1. `docs/AGENT_HANDOFF.md`（本文件）— 环境、命令、已知问题
2. `docs/ARCHITECTURE.md` — 第 4 节（D1–D35 决策）与第 5 节（未决定问题）
3. `docs/PROGRESS.md` — 已完成/未完成边界、141 项测试、下一阶段计划
4. `core/BattleCore/Simulation/FixedStepScheduler.cs` — **阶段 02 核心**，理解时间调度
5. `core/BattleCore/Simulation/BattleSession.cs` — 输入帧号与状态帧号的绑定规则
6. `core/BattleCore/Input/InputBuffer.cs` — **阶段 03A 核心**：边沿闩锁、两阶段提交、只读历史
7. `core/BattleCore/Input/IInputSource.cs` — 输入提交契约（Prepare/Commit）
8. `core/BattleCore/Input/DirectionResolver.cs` — SOCD 与相对朝向规则
9. `core/BattleCore/Simulation/BattleSimulation.cs` — 内核入口 `Step()` 契约
10. `core/BattleCore/State/BattleState.cs` — 不可变状态与所有权
11. `tests/BattleCore.Tests/` — 十一个测试文件构成当前回归基线
12. `scripts/BattleScene.cs` — 表现层如何只读消费状态（**应保持极薄**）
13. `scripts/KeyboardInputMap.cs` — 键位映射，唯一接触 Godot 输入 API 的地方

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
2. ~~提交阶段 03A~~ **已提交并推送（`0be9919`）**。
   **本轮 03A 专项补修尚未提交**——需用户明确授权后再 `git add/commit/push`。
3. **提交本轮补修** — 测试 141/141、Godot 已验证；授权后按第 6 节文件清单提交。
4. **实现运动系统** — 前提：先确定定点数格式并写入文档；方向直接用
   `DirectionResolver.Resolve(...)` 的结果，不要在运动系统里再解析 `InputBits`。
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
- [ ] `dotnet test` 在 `tests\BattleCore.Tests` 返回「通过: 141」
      （若数量变化，说明有 Agent 改过测试，需查 `PROGRESS.md` 是否同步）
- [ ] `core/BattleCore/BattleCore.csproj` 中**没有**任何 Godot 引用
- [ ] `scripts/BattleScene.cs` 中**没有**累加器/补帧/取整代码（只有 `_session.Advance`）
- [ ] `scripts/` 下**没有**任何权威判定逻辑、**没有**输入历史/SOCD 实现
- [ ] `FrameInput` 的帧号恒等于 `BattleState.Frame`（`BattleSession` 会抛异常，测试守护）
- [ ] `InputBuffer.History` 强转 `List<FrameInput>` 必须为 null（只读包装未被改回裸列表）
- [ ] `history[^1].Frame == CurrentState.Frame - 1`（`IsAlignedWith`），不用 `Count == Frame`
- [ ] 输入只有两条合法路径：`IInputSource`（两阶段）或幂等委托；`Take()` 不得被表现层当委托用
- [ ] `docs/ARCHITECTURE.md` 第 4 节决策（D1–D35）与代码实现一致
- [ ] `docs/PROGRESS.md` 已完成项与实际代码一致

**工作结束时必须做的事：**

- [ ] 更新 `docs/PROGRESS.md`（已完成/测试状态/下一阶段）
- [ ] 更新 `docs/AGENT_HANDOFF.md` 第 4/5/6 节
- [ ] 若做了架构决策，写入 `docs/ARCHITECTURE.md` 第 4 节，并从第 5 节移除
- [ ] 实际运行测试并在文档中记录真实结果（**不得编造**）
