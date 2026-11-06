# ARCHITECTURE.md — NewKOF 架构说明

> 最后更新：阶段 02 完成（2026-11-07）
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
│   └── CaptureHelper.cs       # 开发期截图工具（仅 --shot= 参数启用）
│
├── core/
│   └── BattleCore/            # ★ 权威战斗内核（纯 C#，零 Godot 依赖）
│       ├── BattleCore.csproj
│       ├── BattleCoreInfo.cs
│       ├── Input/FrameInput.cs
│       ├── State/FighterState.cs
│       ├── State/BattleState.cs
│       └── Simulation/
│           ├── BattleSimulation.cs
│           ├── BattleStepResult.cs
│           ├── FixedStepScheduler.cs   # ★ 阶段 02：固定步长调度器（可独立测试）
│           └── BattleSession.cs        # ★ 阶段 02：调度 + 模拟 + 状态绑定
│
├── tests/
│   └── BattleCore.Tests/      # 独立 xUnit 测试工程（54 个测试）
│       ├── BattleCore.Tests.csproj
│       ├── BattleSimulationTests.cs        # 阶段 01：8 个
│       ├── FixedStepSchedulerTests.cs      # 阶段 02：调度器测试
│       ├── DeterminismTests.cs             # 阶段 02：确定性与长跑
│       └── GodotIntegrationContractTests.cs # 阶段 02：Godot 层契约
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
| `BattleSimulation` | 唯一调度入口，`Step()` 推进一逻辑帧 | 输入采样、渲染、读写文件、**时间累积** |
| `FixedStepScheduler` | **时间累积与补帧决策**：把渲染 delta 转成"该推进几帧" | 战斗规则、状态、Godot API |
| `BattleSession` | 把调度器输出与 `Step()` 绑定，保证输入帧号 == 状态帧号 | 渲染、输入采样 |
| `BattleState` | 权威状态快照（不可变） | 被外部修改 |
| `FighterState` | 单角色数据（身份 + 初始数据） | 招式、受击、碰撞 |
| `FrameInput` | 一帧内双方输入（不可变值类型） | 输入归一化、SOCD、命令识别 |
| `BattleStepResult` | 单次 Step 的结果描述 | 权威判定 |
| `BattleScene` (Godot) | 把 `delta` 交给 `BattleSession`、把状态映射为画面 | 任何权威判定、**任何时间累积逻辑** |
| `CaptureHelper` (Godot) | 开发期截图验收 | 战斗逻辑（正常运行零副作用） |

**阶段 02 的关键分工**：时间调度逻辑**已全部移出 Godot 层**，位于
`core/BattleCore/Simulation/FixedStepScheduler.cs`。`BattleScene._Process`
现在只有一行调度调用 `_session.Advance(delta, inputProvider: null)`，
不含任何累加器、补帧或取整代码。这使得调度行为可被纯 C# 测试覆盖。

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
| **D10** | **调度器置于 `BattleCore`，不在 Godot 层** | 调度逻辑若留在 `_Process` 内则无法被无 GUI 测试覆盖，只能靠肉眼观察 HUD。抽到纯 C# 后可用 54 个测试逐项验证。 |
| **D11** | **补帧上限 `MaxStepsPerAdvance`，超限显式记录** | 防止卡顿后 `while` 无限补帧造成死亡螺旋。超限部分累加进 `DroppedStepCount`，**绝不静默丢弃**。`BattleScene` 设为 5，并在首次丢弃时告警一次。 |
| **D12** | **显式 `Pause()/Resume()`，恢复后不回补暂停时间** | 暂停期间 `Advance` 直接返回 0，且不累积 delta。恢复后不会突然补上暂停期间的数百帧。 |
| **D13** | **`Advance` 拒绝 NaN / ±Infinity / 负 delta** | 这些值会污染累加器（NaN 一旦进入永不恢复），必须立即失败。 |
| **D14** | **`BattleSession` 强制「输入帧号 == 状态帧号」** | 把配对逻辑收进内核，避免表现层出现"状态推进了但输入用旧帧号"的错帧。`inputProvider` 返回错误帧号时立即抛异常。 |
| **D15** | **`BattleScene` 不含任何时间逻辑** | `_Process` 只调用 `_session.Advance(delta, null)`，并根据返回值决定是否重绘。 |
| **D16** | **积压告警只输出一次** | 阶段 02 实现中曾用 `if (DroppedStepCount > 0)` 判断，导致每帧刷屏（实测一次运行输出数十条）。改为 `_backlogWarningIssued` 标志位。 |

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
4. **输入归一化策略**：SOCD 采用哪种规则（上优先 / 中立 / 后输入优先）？方向是绝对还是相对朝向？
5. **碰撞几何**：采用 AABB 还是 OBB？推挤算法（对称分配 vs 墙角余量）尚未定。
6. **事件流设计**：表现层如何消费 `(frame, eventId)` 事件？阶段 01 尚无事件流，直接轮询状态。
7. **规则数据格式**：招式定义用 JSON / 自定义文本 / C# 代码？加载期校验策略？
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
