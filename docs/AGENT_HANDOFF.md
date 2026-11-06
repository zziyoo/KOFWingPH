# AGENT_HANDOFF.md — Agent 交接说明

> **每一位 Agent 在修改代码前必须先读本文件，并核对文档与实际代码是否一致。**
> 最后更新：2026-11-07（阶段 02 完成）

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
期望：`失败: 0，通过: 54，已跳过: 0，总计: 54`

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
| K3 | `CaptureHelper.cs` 位于 `scripts/`，会打进游戏程序集 | 正常运行零副作用 | 见 `ARCHITECTURE.md` 第 5 节问题 9 |
| K4 | `--headless --import` 末尾打印 `ERROR: Parameter "singleton" is null` | Godot 4.7 headless 已知无害告警，导入成功（`[ DONE ]`） | 忽略，勿误判 |
| K5 | 启动首帧有 3 帧被丢弃（`DroppedStepCount = 3`） | 资源加载尖峰导致，900 帧稳态不再增加 | 属正常现象；若稳态持续增长才是性能问题 |
| K6 | 输入固定为 `InputBits.None` | 角色静止，HUD 帧号持续增长 | 属阶段 02 设计边界，输入链路属阶段 03 |
| K7 | 无 `.sln` 文件 | `dotnet build` 需在具体工程目录执行 | 可选加 `NewKOF.sln`（不必须） |
| K8 | `BattleState.StateVersion` 恒为 `1` | 仅作快照格式标记，无升级机制 | 见 `ARCHITECTURE.md` 第 5 节问题 1 |

**无阻塞性问题。** 编译 0 警告 0 错误，54/54 测试通过，可正常启动运行。

---

## 6. 未提交文件与工作区状态

- **Git**：项目已初始化并推送到 https://github.com/zziyoo/KOFWingPH
  （分支与提交状态见 `git log` / `git status` 实际输出）。
- **`.gitignore` 覆盖**：`.godot/`、`bin/`、`obj/`、`shots/`、`.vs/`、`*.user` 等。
- **构建产物**（被忽略，不影响源码）：
  - `bin/`、`obj/`（根目录、`core/BattleCore/`、`tests/BattleCore.Tests/`）
  - `.godot/`
  - `shots/`（验收截图：`battle_01.png` 阶段 01 修复前、`battle_02.png` 阶段 01 修复后、`final.png`、`p02.png` 阶段 02）
- **清理建议**：`bin/obj/.godot` 可安全删除，重新 `dotnet build` + `--import` 即可重建。

---

## 7. 下一位 Agent 应优先阅读的文件

**按顺序阅读：**

1. `docs/AGENT_HANDOFF.md`（本文件）— 环境、命令、已知问题
2. `docs/ARCHITECTURE.md` — 特别是第 4 节（D1–D16 决策）与第 5 节（未决定问题）
3. `docs/PROGRESS.md` — 已完成/未完成边界、54 项测试、下一阶段计划
4. `core/BattleCore/Simulation/FixedStepScheduler.cs` — **阶段 02 核心**，理解时间调度
5. `core/BattleCore/Simulation/BattleSession.cs` — 输入帧号与状态帧号的绑定规则
6. `core/BattleCore/Simulation/BattleSimulation.cs` — 内核入口 `Step()` 契约
7. `core/BattleCore/State/BattleState.cs` — 不可变状态与所有权
8. `tests/BattleCore.Tests/` — 四个测试文件构成当前回归基线
9. `scripts/BattleScene.cs` — 表现层如何只读消费状态（**应保持极薄**）

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

1. **实现阶段 03 输入链路** — 前提：先在 `ARCHITECTURE.md` 第 4 节写下 SOCD 与相对朝向决策。
   `BattleSession.Advance` 的 `inputProvider` 参数已就绪，直接接入。
2. **实现运动系统** — 前提：先确定定点数格式并写入文档。
3. **为现有测试增加用例** — 只要不改动 `BattleCore` 公共契约。
4. **把 `CaptureHelper.cs` 移到 `tools/`** — 需同步改 `scenes/Battle.tscn` 的 `ext_resource`
   与 `NewKOF.csproj` 的 `Compile Include`。
5. **添加 `.sln`** — 纯工程整理。

### 需要先决策 ⚠️

6. **修改 `BattleState` 结构**（增加速度/状态字段）— 先读 `ARCHITECTURE.md` 第 5 节问题 1。
7. **引入碰撞/判定框** — 先决定几何类型（AABB/OBB）与推挤算法。
8. **引入事件流** — 先设计 `(frame, eventId)` 事件契约。

### 禁止 ❌

9. **不得**修改 `C:\Users\32360\Desktop\Ikemen-GO\` 仓库。
10. **不得**复制或逐函数翻译 Ikemen GO 源码。
11. **不得**使用 Godot 物理系统决定权威战斗结果。
12. **不得**在 `BattleScene._Process` 中重新引入时间累积/补帧/取整逻辑（见原则第 8 条）。
13. **不得**擅自执行 `git commit` / `git push`（除非用户明确要求）。
14. **不得**用空类或未实际运行的测试冒充完成。

---

## 9. 交接自检清单

新 Agent 开工前请逐项核对，任何不一致都要先修正文档再写代码：

- [ ] `C:\Users\32360\Desktop\NewKOF` 存在，`project.godot` 可读
- [ ] `dotnet build` 在项目根目录返回 0 错误
- [ ] `dotnet test` 在 `tests\BattleCore.Tests` 返回「通过: 54」
      （若数量变化，说明有 Agent 改过测试，需查 `PROGRESS.md` 是否同步）
- [ ] `core/BattleCore/BattleCore.csproj` 中**没有**任何 Godot 引用
- [ ] `scripts/BattleScene.cs` 中**没有**累加器/补帧/取整代码（只有 `_session.Advance`）
- [ ] `scripts/` 下**没有**任何权威判定逻辑
- [ ] `docs/ARCHITECTURE.md` 第 4 节决策（D1–D16）与代码实现一致
- [ ] `docs/PROGRESS.md` 已完成项与实际代码一致

**工作结束时必须做的事：**

- [ ] 更新 `docs/PROGRESS.md`（已完成/测试状态/下一阶段）
- [ ] 更新 `docs/AGENT_HANDOFF.md` 第 4/5/6 节
- [ ] 若做了架构决策，写入 `docs/ARCHITECTURE.md` 第 4 节，并从第 5 节移除
- [ ] 实际运行测试并在文档中记录真实结果（**不得编造**）
