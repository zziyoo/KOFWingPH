# 阶段 31 验收记录

## 2026-11-23 正式验收结论

阶段 31 正式 PASS，31A–31G 共 7/7；未进入阶段 32。正式代码修复提交为 `83d7066`，原始可回滚 WIP 为 `9bc743c`。最终交付提交以本次 main HEAD 和最终报告为准；远端须核对正常 push 后的实际 SHA，不能用本地 tracking ref 代替。

- **31A 原版依据 PASS**：69 个原版签名重新核验，固定输入和 ZIP/Manifest/规则证据保留。
- **31B 本体 PASS**：69/69 固定 Vulkan 双视觉通过（default 67，mode2 2）。205 窗口场实际是 205 后接 200，首个错误来自站姿受击框回退；修复后接触由 L40 对齐到 L41（F27/S42，实际 render51）。
- **31C 输入竞争 PASS**：236/弱 Tackle、冻结 Helper、输入/自然恢复边界、方向门和模式2隔离均通过。
- **31D 取消交互 PASS**：反制、Helper、205边界、310接触年龄、250禁追及反击追击、245/440共存通过。
- **31E 反应防御 PASS**：air-held-back 四次均 Hit（L28/34/41/48）；首次目标为源 State55 ctrl=0。可选小跳防御资格和普通 Airborne 状态条件保留既有高跳空防、地面续防及近战/Helper共用表。
- **31F 确定性实时 PASS**：同种子 2,355 行一致、六 JSON 两轮一致、Vulkan 像素夹具通过。完整审计实时30采样30/29/30、60采样57/60/60，均无逻辑积压；未改采样、阈值或60Hz步进。
- **31G 共存交付 PASS**：最终候选阶段14–26全部542对/65,069帧/538,586行匹配、13C Vulkan323角色/实体帧＋69取消、阶段28六组2,412行、阶段29七组9,473行、旧Wave十三组19,218完整行、阶段30九组原版对照、245两组和440一组通过。C#1606/1606、导入器Python141/141、验证器8/8；独立代码评审无未解决项。

权威最终历史日志为 `local-mugen/stage31-recovery-mirror-replay-r2.log`：13个Matched，最终退出码0。它严格复用同候选已验证项，并新跑剩余项；参考是原档删除前已完整验证的权威副本，不声称还原已删除的旧图像。参考完整性及新跑证据分开记录；全部1,084变体仍须匹配原总数和原行筛选器，不减样本。新图形门禁均为实际Vulkan，headless只承担历史权威回归。

验收 DLL：NewKOF `5e6cebb79211ed7ca4e757476695ec733d67a2f901cc0af70a2fd76cfc36f02b`；BattleCore `76198dd2173b6688856103ef49b196eb4ec04462365b9a9d7d23093710e342de`。测试目录核心与候选相同。`project.godot` 未改，SHA-256 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。

证据入口：`stage31-recovery-matrix-{default,mode2}-r1`、`batch-contract-r1`、`realtime{30,60}-batch-r1`、`directed-mirror-r1/mirror-report.json`、`13c-r1`、`stage28-r1`、`stage30-{pause-r2,contact-r1,pursuit-r1}`、`legacy245-r1`、`legacy440-r1`、`pixels-r1.log`、`repeat-hit-r1`、`csharp-final.log`、`python-final.log`、`validator-final.log`，均在 local-mugen 下带 stage31-recovery- 前缀。像素夹具的六个缺图警告是源码故意设置的负例，最终标记 MUGEN_GPU_PIXELS_PASS、退出0，无ERROR。

性能根因是同步逐行GD.Print开销。相同DLL/配置的A/B/A/B中，约1200行由1200次打印降至约480次，打印耗时约3.4秒降至1.4秒；字段、行顺序和逐帧覆盖不变。统计应用级打印调用，不将其冒充系统磁盘flush次数。CPU温度不可读取，未据此作硬件归因或豁免。

兼容边界仍遵循既有契约：分席RNG与原版共享RNG分别复算，逐次实扣可能不同；原子扣血相位和普通跳跃积分差异保留。投技800/805、720、1050、强版1201和通用空战未扩展。下一候选只建议静态评估强版依赖，本轮在阶段31停止。

## 以下为历史候选及救援过程记录

以下旧WIP、失败和硬件归因段落保留用于追溯；当前结论以上述2026-11-23正式验收为准。

阶段 31 当前为候选，尚未正式 PASS。基线 `a96233e43d35671b9dd70a476888ce1b3ac08a79`。原版来源和复现入口见 [原版参考](STAGE31_REFERENCE.md)、[验证说明](../tools/Stage31Validation/README.md)。

- **31A 原版依据 · 已完成基础核验。** 69 个不重复签名轨迹经来源哈希、物理输入和动作检查。310 被反制、Helper 反制、正常命中和防御、命令竞争、自然恢复均有原版样本。 [原版](STAGE31_REFERENCE.md)
- **31B 命令 · 完成（67/69 场景通过）。** 默认弱 Tackle 真实候选与普通技及 310 的配置接线已生成。模式 2 隔离（`1200-priority-20`、`1200-mode2-fallback`）与输入边界 `310-hit-input-{7,8,9,11,17,19,20,21}`、`310-natural-{35,37,38,39}`、`priority-{19,21,22}`、`held`、`natural-1205-boundary`、`late-1205-no-early-cancel` 全部固定 Vulkan 通过。 [输入边界](../local-mugen/stage31-vulkan-input-r10) · [自然恢复](../local-mugen/stage31-vulkan-natural-r10)
- **31C 本体 · 通过。** 固定 Vulkan 空挥 41 tick Action 1200、14 tick Action 1205、自然站立逐帧核对，实际提交图像与日志同版本；命中、站防、蹲防、对空、双席、镜像、相杀在本构建复跑一致。 [空挥轨迹](../local-mugen/stage31-vulkan-full-r5/1200-whiff/sprite/trace.json) · [命中与防御](../local-mugen/stage31-vulkan-body-r5/1200-hit/sprite/trace.json)
- **31D 反制 · 通过。** 310 被反制后重新进入 1200、Helper 1020 反制与负例、双席冻结（`310-freeze-held-0/1`）、`1200-dual-reversal-close`、被反制后的 Wave 竞争 `reversed-310-wave-{7-21,7-22,8-22,8-23}` 均通过。 [对局轨迹](../local-mugen/stage31-vulkan-body-r5/1200-reversal-cancel/sprite/trace.json) · [Helper 轨迹](../local-mugen/stage31-vulkan-body-r5/1200-projectile-140-20/sprite/trace.json)
- **31E 交互 · 通过（除下表两项）。** 取消窗口 `200-window-{8,9,11,12}`、`205-window-{20,22}`、投射物 `projectile-{100-20,100-21,120-19,140-18,140-20,140-22,160-20,180-20}`、`projectile-invuln-{19,20,21,22}`、`1205-helper-boundary-{56,57,58,59}`、`hit-boundary-{85,104,106}` 全部一致。**未通过：`205-window-23`（接触早 1 逻辑帧：原版对应逻辑帧 41，实测 40）与 `air-held-back`（原版 4 次全 Hit，实测 3 Blocked + 1 Hit）。** `air-held-back` 的配置已与原版源码对齐（`Terry-S.cns` 的 1200：第 3 元素 `guardflag = M`，第 4／6／8／10 元素 `guardflag = MA`），所以就当前证据看差异在运行时空中防御资格（交叉换边后朝向与 `B` 的解析、AirBlockstun 的续防判定），不在导入配置的守卫标记。 两者日志在 `local-mugen/stage31-vulkan-windows-r10/<场景>/`。[窗口与投射物](../local-mugen/stage31-vulkan-windows-r10) · [晚期边界](../local-mugen/stage31-vulkan-boundary-r10)
- **31F 回归 · 冻结回放通过，其余未完成。** 本次 C# 专项 141/141，全量 1602/1602。Python 全量 141/141。BattleCore 和 Godot Debug 构建各 0 警告 0 错误。测试工程构建有已有 CS8600（`ThrowTests.cs:284`）和 xUnit2029（`CombatEventTests.cs:207`）各一项。两次独立生成配置六份哈希相同。14–26 冻结回放 542 对／65,069 帧／538,586 行全部匹配；13C（323 帧＋69 条取消）与阶段 28（6/6、2,412 行）、阶段 29（7 对、9,473 行）定向回归均在本构建通过。
- **31G 交付 · 未完成。** 本构建已通过：13C Vulkan（323 帧角色／实体 + 69 条取消）、阶段 28（6/6、2,412 行）与阶段 29（7 对、9,473 行）定向回归、同种子复跑 `1200-hit`（2,355 条权威记录逐条相同）、Vulkan 像素夹具（`MUGEN_GPU_PIXELS_PASS`）、69 个原版场景中 67 个固定 Vulkan 对照。**未完成：实时 30／60 FPS（本机阻塞，见下节）、上述两项场景差异、正式提交与远端核对。** [同种子](../local-mugen/stage31-vulkan-repeat-r10.log) · [像素夹具](../local-mugen/stage31-vulkan-pixels-r10.log)

首次冻结回放发现新增空 `ProjectileId` 改变旧事件文本，已修复并用专项断言覆盖。候选 r5 的历史回放已重新通过阶段 14；并发批次在中断时出现 headless Godot 进程异常，原日志保存在 `local-mugen/stage31-frozen-authority-r5*.log`，续跑目录为 `local-mugen/stage31-frozen-resume-r6`。首次 Vulkan 试跑 r2 因绑定字段错误回退几何图形，失败日志保留在 `local-mugen/stage31-vulkan-r2`。r3 的格挡事件金额与实扣血不一致，修正后 r4 已通过相应复验。

同构建的阶段 28 定向回放 6/6，共 2,412 条权威记录匹配。模式 2 两个固定对局通过。`1200-priority-21` 首次截图窗口早于 Helper 出生，`1200-1205-helper-boundary-57` 首次校验器误将同 State 新实例当成旧实例残留，补采和校验修正后的 r7 均通过。 [Wave 重采](../local-mugen/stage31-vulkan-priority-r7.log) · [恢复边界](../local-mugen/stage31-vulkan-reentry-r7.log) · [模式 2](../local-mugen/stage31-vulkan-mode2-r7.log)

实时帧率仍未通过。30 FPS 请求档在并发运行时测得 9–16 FPS，隔离重测采集附近为 8–20 FPS。60 FPS 隔离测试采集附近为 13–24 FPS。两次原始结果分别保存在 `local-mugen/stage31-realtime30-r7.log`、`local-mugen/stage31-realtime30-r8.log` 和 `local-mugen/stage31-realtime60-r8.log`。不能用固定帧图像替代该门禁。

## 2026-11-16 第二轮：构建核对与实时帧率归因

本轮从阶段 30 基线 `a96233e` 接手工作区，未修改任何已有实现文件，只新增诊断证据。仓库 `main` 与本地 `origin/main` 仍在 `a96233e`，工作区改动和未跟踪 `.cs.uid` 原样保留。

- **构建可复现 · 已核对。** 在本机构建得到 `NewKOF.dll` `02f2b5d1…`、`NewKOF.BattleCore.dll` `8cbf9e05…`，与交接记录的最终候选逐字节一致；`project.godot` 仍为 `2f4d1c2c…`。全量测试 1602/1602 通过（`dotnet test`，1 m 27 s）。
- **31F 冻结回放 · 通过。** 同构建在 `local-mugen/stage31-frozen-resume-final` 完成阶段 14–26 全部续跑，13 个阶段均输出 `Matched Stage N`，合计 **542 对／65,069 逻辑帧／538,586 条权威记录**，与阶段 27／28／30 的封存数字逐项相同；日志无 mismatch、无 Traceback。逐阶段：14（8 对／640 帧／2,560 行）、15（28／2,710／10,840）、16（58／5,676／22,704）、17（45／4,247／25,482）、18（53／4,972／39,776）、19（10／1,207／11,812）、20（41／4,623／43,202）、21（47／6,062／56,315）、22（29／5,249／48,260）、23（47／7,898／73,151）、24（63／8,192／79,058）、25（58／6,116／56,271）、26（55／7,477／69,155）。
- **实时帧率 · 未通过，已定位归因。** 隔离、空闲机器上重测仍失败：30 FPS 请求档 `before [30,30,30,30,30,30,18,26,24]`、`after [16,20,26,24,27,27]`（`local-mugen/stage31-realtime30-r12`），复测 `before [30,30,30,30,30,19,23,27,22]`、`after [17,25,26,22,20,23]`（`r13`）。两次都在战斗开始约 5 s 后从 30 掉到 17–27，采集前后均有掉帧，不能判 PASS。
- **归因证据（不变更门禁、不改采样口径）。** 同一组渲染参数下：①关闭逐帧精灵审计 `--sprite-frame-audit` 后 13 个 FPS 样本全为 30、零帧积压，且战斗与招式事件正常（`local-mugen/stage31-rt-diag/visual-only.log`、`noaudit.log`）；②打开该审计即掉帧，且与输出目标是管道还是文件无关（`file-redirect.log`）；③用阶段 30 封存配置和同一参数重跑同样掉到 17–27（`curDLL-stage30cfg-1/-2.log`），用阶段 30 基线 DLL 构建（`git worktree` 临时构建，已删除）首次运行能维持 30（`baseDLL-stage30cfg.log`）；④临时切到高性能电源方案并降温后重测仍失败（`r12`/`r13`），电源方案已恢复为「平衡」。结论：本机在持续负载下的帧预算不足，逐帧精灵审计的固定开销把 30 FPS 推到 25 以下；该现象不局限于阶段 31 内容，不构成阶段 31 的代码回归，但也不能据此改写门禁。
- 实时档失败日志按新目录保留：`stage31-realtime30-r10`（审计全开）、`r11`（曾尝试在实时档移除精灵审计，校验器缺 `[SpriteFrameAudit]` 立刻暴露依赖，改动已回退，不再使用）、`r12`/`r13`。驱动脚本与阶段 30 一致，未做任何放宽带宽或跳过采样的修改。

## 2026-11-23 救援轮：修复候选与证据边界

当前仍为 WIP，等待本轮最终门禁。已在本地 `wip/stage31-recovery` 保存原始检查点 `9bc743ce787d687985e13182732d9ff7b24ea250`（65 个路径；暂存区原为空），不推送。原文件、旧 DLL、指纹和关键失败证据独立备份在 仓库外相邻的 `NewKOF_backups/stage31-recovery-20261123`。Git 直连查询连接重置；GitHub 连接器读取 main 确认为阶段 30 `a96233e43d35671b9dd70a476888ce1b3ac08a79`。

两项失败在旧 DLL 上各复现两次（`stage31-recovery-repro-r1/r2.log`），未覆盖旧证据、输入或原版记录。

- **205-window-23 根因**：第二次接触属于 State 200，不是 1200。205 对站姿目标采用原版 Action 5000 的反应，旧配置对 `CrouchingLight` 的站姿分支没有时间线，回退到 ±30、高 120 的通用框。F26/L40 原版主框最高 72，200 攻击最低 73，另一块头框在攻击横向范围外，故不接触；原回退框提前重叠。新增可选 `standingLightHitstun`，从冻结 ZIP 的 AIR 5000 生成。首元素完整时长之外包含 NewKOF 接触当帧的原子反应提交（原版在碰撞后才 ChangeState），然后按 AIR 播放、接入既有恢复框；生成的 2/4/6 时间线与原有 205 站姿视觉绑定 followupTick=6 相符。F27/L41/S42 恢复形状才接触。修复前 L40，修复后 L41，伤害 52；初次 205 仍于 L22 接触、伤害 30。没有延后 1200，也没有调整输入或比较容差。
- **air-held-back 根因**：首次接触 F14/L28 的目标是原版小跳 State 55，`ctrl=0`，虽然持后且实际 HitDef 的 guardflag=MA，仍无防御资格。后续原版处于受击状态。新增角色可选 `movement.shortHopCanGuard`，旧配置默认为 true，阶段 31 根据源 State 55 明确写 false；并恢复共享 `CanGuard` 仅接受普通 `Airborne` 的状态资格，排除 AirHitstun、动作中和 AirBlockstun 被 `IsAirborne` 扩大纳入的错误分支。新配置小跳不能防；反例覆盖高跳 MA 仍可防及旧小跳默认资格。近战、Helper 共用同一条件。修复后 L28/34/41/48 四次均 Hit，伤害 30/29/15/22；旧候选为前三次 Blocked。

`stage31-recovery-fix-r2` 两项固定 Vulkan 双视觉分别 2,219/2,343 条权威记录通过，涵盖后续伤害、停顿和恢复。`stage31-recovery-phases-{205,air}-v2.json` 保存 F/L/S、原版 before-command/after-collision/end 与前后运行日志，入口 `tools/Stage31Validation/recovery_phases.py`。旧 v1 导出将 SpriteRenderAudit.logic 误按 L 归组，保留但不作相位依据；v2 按 S 归组并保存 FrameCapture 的实际 render、S 与 PNG SHA-256。两处新接触分别为 F27/L41/S42/render51 与 F14/L28/S29/render38。

性能旧归因不能直接定为硬件或豁免。`stage31-recovery-perf-ab-r1` 串行阶段 30 DLL+配置与修复前批处理的阶段 31 DLL+配置，audit ON/OFF 各 A/B/A/B，固定同一 320x180 Vulkan、30 FPS、输入、输出磁盘和电源方案。ON 四次均有波动（31 最低 19/20），OFF 四次各 13 个样本全 30、零积压。每次有 DLL/配置/输入哈希、退出码、GPU/驱动/温度/频率、进程清单与 CPU 采样。GPU 约 65°C，CPU 温度不可读取；未测原生文件系统 write/flush 次数，不将 GD.Print 调用数冒充系统调用数。

`stage31-recovery-cost-probe-r1` 在输出路径内计时，1,200 次审计 GD.Print 共 3,718.385 ms，1,200 次参数读取共 44.0066 ms。瓶颈主要落在同步逐行输出。将相邻 Observe 两行、Render 两席及实体行批量提交，遇到其他审计前立即 Flush，不跨逻辑帧缓冲。保留全部行、字段、顺序和每帧采样。`--audit-cost-probe` 只加独立诊断，`--sprite-audit-unbatched` 可做相同构建下的旧输出对照。`stage31-recovery-batch-contract-r1` 与未批处理的 `fix-r2` 权威记录各 2,219/2,343 条逐条一致；精灵与实体审计行各 877/880 条逐字节及顺序一致。

批处理构建指纹：`NewKOF.dll=5e6cebb79211ed7ca4e757476695ec733d67a2f901cc0af70a2fd76cfc36f02b`，`NewKOF.BattleCore.dll=76198dd2173b6688856103ef49b196eb4ec04462365b9a9d7d23093710e342de`。完整审计实时 30 FPS 样本 `[30,29,30]`、60 FPS `[57,60,60]`，两档均零积压并通过原验收脚本（`stage31-recovery-realtime{30,60}-batch-r1`）。早期 60 档误选 render=552 未拍到动作，失败存档 `stage31-recovery-realtime60-r1` 保留，不计验收；正确入口沿用既有 60 档 render=294。

本轮 C# 1,606/1,606、Python 141/141；核心 DLL 与上述批处理构建相同。Godot 构建 0 警告 0 错误。六份 JSON 在 `terry98-stage31-recovery-r2/r3` 两轮生成逐字节一致（`stage31-recovery-determinism.log`）。当前应使用 recovery-r2 新目录，旧 `stage31-final` 原配置保留未覆写。原版 69 个签名重新校验完成（`stage31-recovery-native-check.log`）。全矩阵与最终历史门禁结果将在本节后续补齐；旧 542 对成功证据只代表旧核心 DLL，不替代本轮回归。

独立核心构建补充：后续 `dotnet build core/BattleCore/BattleCore.csproj` 产生 `cf8f3359…`，信息版本带提交 `3ed196a`；候选与 C# 测试目录的核心仍为 `76198dd2…`，信息版本为构建时的 WIP `9bc743c`。2,959 个方法体 IL 逐字节一致，证据 `stage31-recovery-core-il-comparison.json`。独立构建用于证明核心可单独构建，不将其日志冒充候选指纹；未替换正在回放的候选 DLL。

原 D:/阶段26与29归档删除已由用户确认；已使用保留的、旧候选完整验证过的 headless 权威副本恢复参考链，不伪造原截图。参考完整性核验和当前重放区分为 `stage31-recovery-mirror-audit-r1`（参考审计）与 `stage31-recovery-mirror-replay-r1`（当前候选严格重放）。阶段29及旧Wave同理由官方阶段30副本建立逐条相等链，当前结果 `stage31-recovery-directed-mirror-r1/mirror-report.json` 为 new-runs-passed：7 对/9,473 行和 13 对/1,965 帧/19,218 完整行。新验证器8项测试通过，另有错误参考头、缺变体、改总数、改输入时机的拒绝实验；不改原有行筛选器、总数、容差或样本。

最终 69/69 固定 Vulkan 已完成（default 67，mode2 2），13C 323＋69 与阶段28 6/6、2,412行通过。同DLL控制输出批处理 A/B/A/B 位于 `stage31-recovery-batch-abab-r1`：非批处理前1200行耗时3454/3379ms、打印1200次，批处理约1200行耗时1420/1401ms、打印480/481次；非批处理60档出现32/36低点，批处理对应最低59/56并回到60。四次均保存DLL/配置/物理输入哈希。这里统计应用GD.Print调用数，不伪称系统磁盘flush次数。

伤害兼容边界补充：以上救援轮所列 52、30/29/15/22 为 NewKOF 实扣，不意味着原版共享 RNG 与本项目分席 RNG 的逐次采样相同。原版对应 205→200 样本实扣为 30/37，空中四次为 15/22/15/22；新版分别为 30/52 与 30/29/15/22。现有签名比较按既定契约分别以各引擎 RNG、反击上下文与防御倍率验证同一伤害公式，并单独验证原版接触后的 HP 更新；未放宽此契约、未改 RNG 或测试预期。原版下一更新扣血与 NewKOF 接触 tick 原子扣血，以及普通跳跃积分的既有差异继续作为兼容边界，不宣称完全复刻通用空战。
