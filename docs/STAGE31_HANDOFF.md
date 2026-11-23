# 阶段 31 接续交接

更新时间 2026-11-16（第二轮）。阶段 31 仍未正式验收，也没有阶段 31 commit。用户要求保留当前工作区并交由下一位 agent 完成剩余任务。

## 基线与保护

- 仓库 `C:/Users/32360/Desktop/NewKOF`，`main` 本地 HEAD 与本地 `origin/main` 均为阶段 30 的 `a96233e43d35671b9dd70a476888ce1b3ac08a79`。本轮没有改远端配置，也没有提交。Git 可执行文件在本机 `C:/Users/32360/.workbuddy/binaries/PortableGit/versions/1.3.0/cmd/git.exe`（不在 PATH）。
- 本轮构建核对：`NewKOF.dll` `02f2b5d19187e240e27b87ba98e3c79837dbd0d243107ad28cde4451c0b3ceeb`、`NewKOF.BattleCore.dll` `8cbf9e057e5f4bd1a3b841999aff3108a830cba277c6bd8bfb103d7141f934c9`，与上轮交接逐字节一致；`project.godot` 仍为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。全量测试 1602/1602。
- 工作区改动（27 个已修改文件）和未跟踪 `.cs.uid` 原样保留。不要 reset、clean 或覆盖。本轮只新增 `local-mugen/stage31-rt-diag/`、`stage31-realtime30-r10`、`r11`、`r12`、`r13` 等证据目录（`local-mugen/` 已被 `.gitignore` 忽略），并改动 `docs/STAGE31_ACCEPTANCE.md`、`docs/PROGRESS.md`、`docs/AGENT_HANDOFF.md`、本文件。
- `.cs.uid` 归属已核清：21 个未跟踪 `.uid` = 15 个「已跟踪 `.cs` 但缺 `.uid`」（`LaunchReactionProfile`、`FeedbackTiming`、`HitReactionVisualPose`、`ProjectileVisuals`、`ThrowVisual`、`ScriptedLaunchLanding`、`LaunchReactionState`、`ProjectileVisualLayer`、`ForwardLightPunchTests`、`GroundBlowbackTests`、`JsonForwardLightPunchProbeTests`、`ProjectileVisualTests`、`ProximityGuardTests`、`RisingUpperTests`、`ThrowVisualTests`）+ 6 个本轮新增源文件（`MoveAirMotion`、`MoveReversal`、`AuthoredMoveMotionTests`、`ReversalResolverTests`、`ReversalSettlementTests`、`Stage31RuntimeTests`）。提交时全部纳入可使 181 个 `.cs` 全部带 `.uid`。
- 本轮曾用 `git worktree` 在 `C:/Users/32360/Desktop/NewKOF-baseline` 临时构建基线 DLL 做性能归因，工作树已删除；`.godot/mono/temp/bin/Debug/` 里的两份 DLL 已还原为本轮候选哈希（上面两个值）。
- 本轮曾临时切到「高性能」电源方案并复制出一个临时方案，测完已恢复「平衡」并删除临时方案，`powercfg /list` 只剩平衡一种。

## 本轮已完成

- **31F 冻结回放 · 通过。** 同构建在 `local-mugen/stage31-frozen-resume-final` 跑完阶段 14–26，13 个阶段全部 `Matched Stage N`：**542 对／65,069 逻辑帧／538,586 条权威记录**，无 mismatch 或异常，与阶段 27／28／30 的封存数字逐项相同。
- **31B／31C／31D／31E · 67/69 场景通过。** 69 个原版签名场景全部用固定 Vulkan（320x180，视觉开关双跑）跑过，证据目录：`stage31-vulkan-input-r10`、`-natural-r10`、`-windows-r10`（含 2 个失败）、`-reversed-r10`、`-boundary-r10`、`-contactguard-r10`，以及上轮的 `-full-r5`／`-body-r5`／`-resume-r6`／`-priority-r7`／`-mode2-r7`／`-reentry-r7`。
- **31G 部分通过。** 本构建已通过 13C Vulkan（323 帧＋69 条取消）、阶段 28（6/6、2,412 行）、阶段 29（7 对、9,473 行）、同种子复跑（`1200-hit` 2,355 行逐条相同）、Vulkan 像素夹具（`MUGEN_GPU_PIXELS_PASS`）。
- **仍有两个可复现差异（不是偶发抖动，已复跑两次）**：
  1. `1200-205-window-23` — 接触早 1 逻辑帧：原版对应逻辑帧 41，实测 40。日志 `stage31-vulkan-windows-r10/1200-205-window-23/`。
  2. `1200-air-held-back` — 原版 4 次接触全为 `Hit`，实测前 3 次 `Blocked`。运行日志显示 P2 在 logic 30／36／42 处于 `AirBlockstun`（此时仍按住 `B`）。已比对原版源码 `Terry-S.cns`：1200 第 3 元素 `guardflag = M`、第 4／6／8／10 元素 `guardflag = MA`，与当前配置（contactGroup 1 `groundGuardOnly: true`，2–5 `false`）一致，**所以不是导入配置错**。差异更可能在运行时空中防御资格：Tackle 穿过对手后的朝向更新，或 `B` 在换边后被解析成前方向；也可能是 AirBlockstun 期间的续防规则。
- **实时帧率归因完成（仍未通过）。** 详见 [验收记录](STAGE31_ACCEPTANCE.md#2026-11-16-第二轮构建核对与实时帧率归因)。要点：隔离空闲重测 30 FPS 请求档战斗开始约 5 s 后从 30 掉到 17–27；关闭逐帧精灵审计后 13 个样本全 30、零积压；阶段 30 封存配置同样掉帧；临时高性能电源方案无效。结论是本机持续负载帧预算不足叠加审计固定开销，不是阶段 31 内容回归。**不要**用放宽带宽、缩短采样或改写结果的方式让它变绿。

## 下一位 agent 的执行顺序

1. 先修上面两个差异。守卫标记按原版 `guardflag` 重推后，用这条命令复验（新目录）：

   ```powershell
   $godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
   $audit = @('audit','audit-more','edges','pilot','input') | ForEach-Object { '--reference'; "local-mugen/stage31-native-$_/results" }
   py -I tools/Stage31Validation/rising_tackle_duel.py --godot $godot `
     --profile-dir data/imported-characters/terry98-stage31-final --fixed-resolution 320x180 --compare-plain @audit `
     --output local-mugen/stage31-vulkan-fix-r1 --fixed-window 20 85 `
     --scene 1200-205-window-23 --scene 1200-air-held-back
   ```

   任一配置改动都要重跑受影响场景＋同种子复跑；若改到 1200 的守卫或接触时序，还要重跑阶段 14–26 冻结回放（1 帧接触差属于共享命中链路）。
2. 需要重跑冻结回放时：

   ```powershell
   $godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
   $stages = @(14..26 | ForEach-Object { '--stage'; $_ })
   $dirs = @('stage31-frozen-authority-r5','stage31-frozen-authority-r5-B',
     'stage31-frozen-authority-r5-C','stage31-frozen-authority-r5-D',
     'stage31-frozen-resume-r6','stage31-frozen-resume-r8','stage31-frozen-resume-r9',
     'stage31-frozen-resume-next','stage31-frozen-resume-final')
   $resume = $dirs | ForEach-Object { '--resume-headless-from'; "local-mugen/$_" }
   py -3.11 -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ `
     --headless-authority --output local-mugen/stage31-frozen-resume-next2 @resume @stages `
     2>&1 | Tee-Object local-mugen/stage31-frozen-resume-next2.log
   ```

   只有看到各阶段 `Matched Stage N` 才算通过；部分运行不能当完整回归。完整跑一遍约 2 小时（3 对/分钟）。
3. 实时 30／60 FPS 是本机阻塞项。若换机器或散热条件改善，可在**不改**采样口径、带宽或审计的前提下重跑到通过；否则保持阶段 31 未通过，并在验收记录写明复现命令与日志路径。
4. 以上全绿后按最终构建补跑 C# 全量与 Python 全量，再创建正式提交并推送，核实 GitHub 实际 HEAD。仍有阻塞时只能保存明确标注 WIP 的可验证 checkpoint，不能宣称阶段 PASS。


本轮没有开始下一阶段。

## 2026-11-23 救援轮接续

原始 WIP 已在本地 `wip/stage31-recovery` 保存 `9bc743c`，不推送。两处规则分歧的修复候选、同步日志输出性能根因、批处理输出等价证明和 30/60 FPS 新实测见 STAGE31_ACCEPTANCE.md 的救援轮章节。使用 `data/imported-characters/terry98-stage31-recovery-r2`，不要覆写旧 final 配置。当前批处理 DLL 为 `5e6cebb7…` / `76198dd2…`；全矩阵与最终历史门禁正在补齐，阶段仍 WIP。

## 2026-11-23 中途快照：删除归档后的严格补跑

候选 DLL 仍为 `5e6cebb7… / 76198dd2…`；不要重新构建或替换正在验收的 DLL。新配置目录为 `terry98-stage31-recovery-r2`，六份 JSON 两轮一致。69/69 固定 Vulkan 已全部通过，13C 323 角色/实体帧＋69 取消、阶段 28 六组 2,412 行、阶段 29 镜像参考七组 9,473 行、旧 Wave 十三组 19,218 完整行均已在此候选通过。完整审计实时 30/60 已通过，采样为 30/29/30 与 57/60/60，均无积压。

用户确认删除了原 D:/NewKOF-stage26-* 与 stage29-delivery-*。第一次新核心历史回放 `stage31-recovery-frozen-r1` 在阶段 18 只枚举到 27/53 对，未出现结算差异；保留其成功单项和失败日志。`verified_mirror_replay.py` 已核验旧成功记录 `stage31-frozen-resume-final.log` 的全部 1,084 个参考变体（固定旧 DLL 02f2/8cb、13 个 Matched、542 对/65,069 帧/538,586 行）。旧记录仅作为参考副本，绝不冒充新候选 PASS。当前新跑 `stage31-recovery-mirror-replay-r1` 严格比较每条记录，复用同候选 r1 的已验证项，继续补缺；必须等 13 个 Matched 和最终退出码 0 后才可称整个冻结门禁通过。

续跑命令：`py -X utf8 -u tools/Stage31Validation/verified_mirror_replay.py --godot <Mono控制台exe> --resume local-mugen/stage31-recovery-frozen-r1 --resume local-mugen/stage31-recovery-mirror-replay-r1 --output local-mugen/<新目录>`。两个来源均须保留，当前脚本核候选指纹、完整输入参数、输入审计与权威行后才复用。`--verify-only` 只核参考，不计新构建通过。

阶段 29/旧 Wave 的已验证参考保留在 D:/NewKOF-stage30-delivery-r3-{stage29,old-wave}；`mirror_regressions.py` 对旧 Stage29 headless→Stage30 Vulkan 的链逐条复核，再用新核心/新配置重新跑，结果在 `stage31-recovery-directed-mirror-r1/mirror-report.json`（new-runs-passed）。所有原版输入、JSON、参考日志和 PNG 未覆写。参考集 2,369 个文件（约 213 MB）以及原版阶段31的145个来源文件/ZIP/Manifest（约195MB）已在仓库外相邻 NewKOF_backups/stage31-recovery-20261123 独立复制并核哈希。

剩余图形门禁由本机脚本 `local-mugen/stage31-recovery-remaining-native-r2.ps1` 顺序运行：阶段30暂停、延后接触、追击，245/440原版对照，像素；随后队列中的 `1200-hit` 同种子复跑。观察 `stage31-recovery-remaining-exits.log`、各 lane 的 trace/日志和 `stage31-recovery-repeat-hit-r1.log`，只能计成功条目。暂停 r1 错选 audit-more 的 F105 短轨迹导致校验截断，r2 明确只选 input 的 F120 长签名，与阶段30正式来源相同，旧失败保留。尚未全部完成前仍 WIP，不推送。
