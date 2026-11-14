# 阶段 27 收口交接

> **2026-11-14 收口补记：阶段 27 正式 PASS（27A–27G 7/7）。** 冻结回放已由早期 lane（14–19、25）与续接 lane r1–r3（20–24、26）在同一候选构建指纹上全部完成并逐项核对；逐阶段结果与执行记录见 [STAGE27_ACCEPTANCE.md](STAGE27_ACCEPTANCE.md)。以下交接内容按当时状态保留存档。

> 状态：**阶段 27 尚未正式 PASS**。新招式、输入竞争、本阶段图形和专项回归已有大量通过证据；当前正式收口阻塞是用最终 Debug 构建完成阶段 14–26 冻结回放、汇总门禁、更新文档并提交/推送。接手时先续接现有验证，不要重做已通过的取证。

## 基线与保护项

- 仓库：`C:\Users\32360\Desktop\NewKOF`，分支 `main`。
- 最近核实 HEAD：`bdc8cfd5ce7a477f51df94c1ef5d8565343f398d`。阶段 27 checkpoint 已本地提交；HEAD 在 `origin/main` 跟踪点 `12776456c3382b7c7d1c0b87df49cb8dbe1ebb18` 之上 11 个提交。推送前必须重新 `git ls-remote origin refs/heads/main`，不要假设远端仍停在该 SHA，也不要 force push。
- 当前工作区阶段 27 尚有未提交文档/脚本：`docs/AGENT_HANDOFF.md`、`docs/ARCHITECTURE.md`、`docs/PROGRESS.md`、`tools/Stage27Validation/README.md`、`docs/STAGE27_ACCEPTANCE.md`、`tools/Stage27Validation/frozen_replay.py`、`tools/Stage27Validation/sweep_recovery.py`。先读 diff，按阶段 27 内容整理提交，不要覆盖已有修改。
- `project.godot` 无工作区差异。已核验 SHA-256：`2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。不得改写、恢复或混入提交。
- 使用 Godot 4.7.2 Mono/.NET、Python 3.11.9。候选 Debug 构建指纹：`NewKOF.dll=5a397045774217ce2852360c000788250f34431e119ba11153e2f28042280d8a`；`NewKOF.BattleCore.dll=d59689ddde69b97fad8f54dd0c83ce1d367ea2087e3852416d848c349c521e8b`。任何代码重建后，旧回放不能算新指纹的证明。

## 已有结果：不要无故重跑

- 原版 IKEMEN GO 标准轨迹 169/169；补充签名轨迹覆盖 Wave 延长恢复、向外跨距持防和 Wakeup 候选。原版源、哈希和细节见 [`STAGE27_REFERENCE.md`](STAGE27_REFERENCE.md)。
- 验收矩阵 27A–27F 已通过；27G 标为“验证中”，见 [`STAGE27_ACCEPTANCE.md`](STAGE27_ACCEPTANCE.md)。该矩阵当前只能作为先前工作记录，待最终冻结回归成功后再更新 27G。
- C# 专项 281/281、全量 1,491/1,491；Python 专项 8/8、全量 120/120；Debug 构建结果和既有测试工程两项警告见阶段验收中的日志链接。BattleCore/Godot Debug 为 0 警告、0 错误；测试工程保留既有 `CS8600` 与 `xUnit2029`。
- 阶段 27 固定 Vulkan 79 对、12,210 逻辑帧、113,995 条视觉开关权威记录、3,245 张提交图像；实时采样实测 30 FPS 档为 30/27/30，60 FPS 档为 59/60/60。阶段专属 Vulkan 图形复现与限制见 [`tools/Stage27Validation/EVIDENCE.md`](../tools/Stage27Validation/EVIDENCE.md)。
- 13C 在候选构建上已单独执行真实 Vulkan 回归；历史门禁脚本对阶段 14–26 使用 `--headless-authority`，证明固定输入注入和权威行与冻结证据一致，**不是各历史阶段的 Vulkan 图形验收**。
- 440 自然退出修正、模式 2 防御/受击、Wakeup 对 805 的资格、保留地面候选、辅助按键事件释放问题等根因和复测已写入阶段验收/架构文档，不要重新引入阶段 24/26 旧行为。

## 交接时正在运行的冻结回放

以下是本次核对时的进程与日志状态（2026-11-14，PID 会变化；以日志和进程命令行为准）。不要杀进程、删除输出目录、对相同阶段启动第二份任务，或清理 `local-mugen`。任务用 `tools/Stage27Validation/frozen_replay.py --headless-authority` 在当前 Debug 指纹上重放固定输入。

| PID | 日志 | 计划阶段 | 本次检查时已完成 |
|---:|---|---|---|
| 40288 | `local-mugen/stage27-frozen-delivery-a.log` | 14、17、19、22、24 | 14、17 |
| 40968 | `local-mugen/stage27-frozen-delivery-c.log` | 16、21、26 | 16 |
| 36496 | `local-mugen/stage27-frozen-delivery-d.log` | 24 | 尚无完成标记 |
| 27716 | `local-mugen/stage27-frozen-delivery-e.log` | 25 | 尚无完成标记 |
| 31512 | `local-mugen/stage27-frozen-delivery-f.log` | 18、20、23 | 尚无完成标记；此队列在修复 Stage 18 路径后重跑 |
| 38748 | `local-mugen/stage27-frozen-delivery-s23.log` | 23 | 尚无完成标记 |
| 31356 | `local-mugen/stage27-frozen-delivery-s26.log` | 26 | 尚无完成标记 |

另有 `local-mugen/stage27-frozen-delivery-b.log`：Stage 15 已通过；Stage 18 首次执行失败，原因是已归档 `.args.txt` 指向不存在的 `res://local-mugen/stage18-import-smoke-v2/`，不是规则差异。当前脚本已在核对默认/模式 2 配置 SHA-256 后将路径映射到冻结的 `data/imported-characters/terry98-stage18-data/`。修复后的复跑归 lane `f`，等待其结果即可。

本次检查时 `local-mugen/stage27-delivery-hold.flag` 不存在；各 lane 可继续启动。完成状态必须以各自日志出现 `Matched Stage N: ...` 为准，不能仅凭进程退出码、旧黄金文件对比或 `duel.log` 存在来算通过。`b` 的 Stage 18 失败日志应保留作路径故障证据，不要编辑黄金输入或历史结果。

### 接手者的冻结回放动作

1. 先检查所有 lane 是否仍在运行，逐份读取上述日志尾部和 `Matched Stage` 行；等待现有任务自然完成。所有输出目录由 `frozen_replay.py` 以 `exist_ok=False` 创建，禁止复用或覆盖。
2. 确认最终必需清单为 Stage 14–26 全部有**候选构建指纹相符**的 `Matched Stage N` 行。预期数量定义在 `tools/Stage27Validation/frozen_replay.py` 的 `TOTALS`/对应 authority 模块；逐项核对场景数、逻辑帧和 authority 行数。并确认 Stage 13C 已有当前构建 Vulkan 结果。
3. 如队列自然失败，只重跑失败阶段，输出目录另取从未存在的名字；先确认没有其他 Vulkan/回放队列占用机器。示例（替换阶段和新输出名）：

   ```powershell
   $godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
   py -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ `
     --headless-authority --output local-mugen/stage27-frozen-delivery-retry `
     --stage 18 --stage 20 --stage 23
   ```

   先读 `frozen_replay.py` 与 [`tools/Stage27Validation/README.md`](../tools/Stage27Validation/README.md)。如果错误是脚本/路径修复或新代码造成，先做对应检查；不要无限重试同一失败。精确 `InputEventKey` Mono 关闭错误有脚本的一次有限重试；除此之外失败要读取 `godot-engine.log`、对应 `duel.log` 和 `.args.txt`，判断是运行时异常、输入注入不符还是证据差异。
4. 若阶段 14–26 所有组均匹配，阶段 27 运行时/配置未再改动，且现有专项、构建、Python 与 13C Vulkan 证据均可追溯，可收口，不再全量重复测试。若为排错修改了代码，只重跑受影响专项、构建以及失败的冻结范围；最终按现有项目门禁要求补必要回归。

## 阶段 27 已实现的行为摘要

- 默认配置 State 240 为远身站重脚 Action 240（13/3/22 tick）；近身 State 245 Action 245（3/6/20 tick）。模式 2 的远身 State 240 使用 Action 241（19/4/20 tick），位移由配置的 `positionSteps` 驱动。身体距离 48 及以内选择 245。
- 245 旧动作 tick 4–8 可按原版规则直接取消到弱 Power Wave；240 没有同等直接取消窗口，只在自然恢复按普通入口资格重新出招。模式 2 保留 1050 候选，1050 本体未接入。
- 805 仅在特定 Wakeup 资格边界可保留/回落；投技本体、805→245 实际后继、250/300/310/720/1050 本体均不在本阶段。440 扫倒、追击和接地受身遵守已有配置规则。
- 440 自然恢复同帧步行依赖已有 `walkOnCrouchRecovery`；模式 2 前向防御入口和 Blockstun 退出当帧位置有 opt-in 配置修正。细节、限制及图像对应表以 `STAGE27_ACCEPTANCE.md` 和 `EVIDENCE.md` 为准。

## 收口后必须完成的交付

- 将冻结 replay 结果按阶段逐条填入 [`STAGE27_ACCEPTANCE.md`](STAGE27_ACCEPTANCE.md)，保留初次路径失败为“工具路径问题，已修复并复验”，不能把它写成规则回归或藏掉。
- 依据实际日志更新 [`PROGRESS.md`](PROGRESS.md)、[`AGENT_HANDOFF.md`](AGENT_HANDOFF.md)、[`ARCHITECTURE.md`](ARCHITECTURE.md)、[`MUGEN_IMPORT.md`](MUGEN_IMPORT.md) 与 Stage27 README（只更新受影响部分）；在 `AGENT_HANDOFF.md` 指向本文。
- 确认 `project.godot` 哈希仍一致、Git diff 仅含本阶段代码/配置/测试/文档，不提交 `project.godot` 或无关生成资源。
- 按仓库约定将阶段 27 工作整理提交，记录每个 checkpoint 与正式收口 SHA。先核对 `git ls-remote`；若远端是本地祖先则正常推送，若有新远端提交则审阅后安全整合，禁止 force push。
- 推送后核对 GitHub `main` 实际 SHA 与本地 HEAD，并再次检查工作区；有剩余用户修改时明确保留和归属。
- 只有上述门禁均有本次证据后才将 27G 及阶段 27 标为 PASS。否则给出失败/跳过项、具体日志和复现命令，不得把阶段 27 checkpoint 说成正式完成。

## 关键入口

- 原版：[`STAGE27_REFERENCE.md`](STAGE27_REFERENCE.md)
- 阶段矩阵：[`STAGE27_ACCEPTANCE.md`](STAGE27_ACCEPTANCE.md)
- 图形证据索引：[`tools/Stage27Validation/EVIDENCE.md`](../tools/Stage27Validation/EVIDENCE.md)
- 回放/恢复命令：[`tools/Stage27Validation/README.md`](../tools/Stage27Validation/README.md)
- 440 恢复复现：`local-mugen/stage27-final-sweep-recovery.log`、`local-mugen/stage27-final-sweep-thresholds.log`
- 生成器与配置：`tools/MugenImporter/`、`data/imported-characters/terry98-stage27-final/`
