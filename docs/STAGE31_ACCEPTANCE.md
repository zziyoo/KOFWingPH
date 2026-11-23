# 阶段 31 验收记录

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
