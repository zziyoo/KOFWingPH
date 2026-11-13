# 阶段 23 原引擎参考

使用固定 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`、原始 Terry-KOF98 包和种子 1337。F 是原版 RoundState 2 逻辑帧，动作 tick 从零起算。脚本只注入物理按键和起始距离，读取原版 CMD/CNS/AIR 并记录状态、实际 AIR 元素、接触、实体、随机操作数及 Pause。

## 410 本体

- `Terry.cmd:1703–1714` 要求当前下方向、重拳 `y` 或 Helper 99999 的 `var(2)>0`，玩家可控或满足既有状态许可，且非空中。单按站重拳的中心距 54 对照进入 215；下＋轻拳＋重拳进入 400。前下＋重拳及前下＋轻拳＋重拳均由优先级更高的 State 310 接受，后下＋重拳进入 410。默认与模式 2 的 410 本体同用 Action 410。
- `Terry-N.cns:1034` 的 State 410 为蹲姿、锁定脚底、进入时面对对手。Action 410 时长 2／3／8／7／7，起手 5、生效 8、收招 14。单段 HitDef 覆盖第三元素，对应 tick 5–12。Clsn1 为本地 X20–75、Y49–64。攻击属性 `C,NA`，`guardflag=M`，站防及蹲防均合法。
- 下方向在动作中松开后保持蹲姿、原时钟与碰撞框。自然退出时进入可控 State 11，持续按下保持蹲伏，松下以 Action 12 的 1／3 tick 过渡到站立。无接触 F32 退出；F10 形成接触时，进攻方停顿 11 tick，F43 退出。
- 伤害为 `floor((ifelse(var(6),15,13)-var(21))*fvar(7))`。标准 F10 命中 92 HP，目标震停 13 tick；两类格挡零伤害，目标震停 11 tick。水平首步均 10.5，站姿惯性系数 .85、蹲姿 .82。单段对地不击飞，无 Y 位移或倒地。格挡首个恢复控制 F39，对应站／蹲 Action 140／141。命中首个恢复控制 F42，站姿到 State 0，蹲姿到 State 11。
- 站姿重击目标 F11–23 走 5000／5002，F24–26 首段滑动仍用 Action 5002，F27 起 Action 5007。蹲姿对应 5010→5011、Action 5022→5027。全程重击硬直 19 个本地 tick，初始动画包含命中提交与首次滑动共 4 个非冻结姿态 tick。蹲姿目标在 HitPause 中松下仍先退出至 State 11，之后 F43–46 起身并于 F47 站立。
- 对手的 205 可在 F6 打断 410，后者没有旧接触。受击当帧已松下，按受击前蹲姿进入 5010／5020。F20 滑动首帧仍用 5020，F25 切 5025。F30 进入 State 11，F31–34 起身，F35 站立。该轻击硬直 11 tick，初始动画 6 个非冻结姿态 tick。

## 默认取消与候选竞争

- `Terry-2-3.cns:67` 给 `var(1)=1` 的条件是 `StateNo=410 && AnimElemTime(3)>0 && AnimElemTime(4)<0`。显式取消仅旧 tick 6–12，根 `236+x` 或 Helper 99999 `var(10)>0` 可触发，无命中确认限制。旧 tick 27 的新手势另经 `AnimTime=0` 的 `var(2)=1` 自然恢复入口启动。原版模式 2 相同手势进入 1050。
- 旧 tick 6 与 12 在命中后成功，旧 tick 13 的 QCF 拒绝且没有普通轻拳回落。空挥旧 tick 6 与 12 同样成功；旧 tick 13 拒绝。空挥旧 tick 3／4／5 完成手势，在旧 tick 6 由 Helper 提交，此时根命令为 false，Helper 计数分别为 3／7／11。旧 tick 2 手势的根命令有效，松开 X 后 Helper 计数 11／7／3／−1，窗口打开前过期。计数由 `Helper.cns:2117–2124` 设置 12，按住 X 每次减 1，松开每次减 4。命中冻结时 F14 的轻按及按住版本都在 F22 解除进攻方冻结后提交。
- 同 tick 的 Forward＋X＋Y 完成 QCF 时，弱 Wave 胜出。旧 tick 6 单独前下＋Y 则进入 310，旧 tick 13 的同一前下＋Y 拒绝。对照 `approach-0` 与 `approach-11` 从中心距 95 出发，目标均在 F11 前进一步。前者于 F11 形成 410 接触，后者同 tick 改入 State 1000，旧接触被取消，首个接触为 F27 的 Wave。

## 后继实体与完整反应

- `hit-22` 的 Wave 在 F37 出生，F41 于 X90.8852 命中仍处于 5001 的目标。原版 F42 启动击飞反应，F55 抬起、F56 上升、F68 下落、F93 落地冲击、F97 反弹、F109 倒地、F122 起身、F147 恢复控制。Pause 在 F57–90 每三帧冻结一次。取消末帧 `hit-28` 则于 F47 命中已恢复的目标，F153 恢复第二次控制。镜像保持对应坐标符号及相同接触时机。
- `crouch-hit-22` 的目标先走蹲重击反应，Wave 提前至 F40 在 X83.33462 命中 State 5011，之后击飞、反弹、倒地及起身，F146 恢复。站防与蹲防的 Wave 分别在 F41、F40 格挡，削血 9。补测双方防守输入均在 F30 后释放，Wave 接触时目标均处于 State 140 并仍格挡。站姿 X90.8852，原蹲姿目标 X81.164。
- 地面中心距 140 的 `whiff-11` 未形成 410 接触，Wave 晚段 F35 命中，保持地面。目标 F36–48 震停使用 5012，F49 首次滑动，F54 切至 5017。F50 起每三帧 Pause，控制恢复前共插入 9 个全局冻结 tick，F76 恢复控制。暂停外仍为 19 tick 硬直、4 tick 初始反应。
- `incoming-wave-22/28` 分别在 F21／F25 被先前发射的 P2 Helper 打断，P1 未取消。补测 `incoming-wave-20` 中 P1 F20 从旧 tick 6 成功取消，F29 在自己的 1000 出生窗前被 P2 Helper 打断，原实体仍存续。混合 `mixed-22` 中，P1 F22 从 410 取消，P2 F90 从 200 取消。前者格挡接触，后者落空，两个实体的出生、归属和销毁各自独立。
- 对照伤害使用实际采样。原版在目标反应帧扣血，NewKOF 在接触 tick 原子结算。原版共享 RNG，NewKOF 独立 RNG。对地空挥采用相同地面起始条件与物理输入。模式 2 的 1050 原版手势轨迹与无 QCF 的共同本体控制轨迹分别保存。

## 复现

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage23 -Prepare -LastFrame 185 -RunDirectory local-mugen/stage23-reference
$more = @('down-forward-heavy-0','down-back-heavy-0','crouch-hit-release-0')
$more = $more | ForEach-Object { "default-crouch-heavy-wave-$_" }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage23 -Prepare -LastFrame 185 -RunDirectory local-mugen/stage23-reference-supplement -Cases $more
$boundaries = @('default-crouch-heavy-wave-whiff-8','default-crouch-heavy-wave-incoming-wave-20')
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage23 -Prepare -LastFrame 185 -RunDirectory local-mugen/stage23-reference-boundaries -Cases $boundaries
$final = @('whiff-7','rising-upper-11','rising-upper-18','guard-release-22','crouch-guard-release-22')
$final = $final | ForEach-Object { "default-crouch-heavy-wave-$_" }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage23 -Prepare -LastFrame 185 -RunDirectory local-mugen/stage23-reference-final-probes -Cases $final
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage23 -Prepare -LastFrame 185 -RunDirectory local-mugen/stage23-reference-combined-input -Cases 'default-crouch-heavy-wave-down-forward-both-0'
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage23 -Prepare -LastFrame 185 -RunDirectory local-mugen/stage23-reference-priority-v2 -Cases 'default-crouch-heavy-wave-dual-button-0'
py tools/Stage23Reference/Check-Traces.py local-mugen/stage23-reference/results local-mugen/stage23-reference-supplement/results local-mugen/stage23-reference-boundaries/results local-mugen/stage23-reference-final-probes/results local-mugen/stage23-reference-combined-input/results
py tools/Stage23Reference/Check-Traces.py local-mugen/stage23-reference/results --extract-body local-mugen/stage23-reference-body/results
```

45 条原版轨迹和逐场校验哈希位于上述五个校验目录。独立本体子集从原始运行提取，保留逐场日志和哈希。早期主运行的双键轨迹以 F5 的提交状态和时钟确认 State 400 入口，独立复跑 `local-mugen/stage23-reference-priority-v2/results` 也记录了 F5 的 State 10→400 转换事件。该复跑供图形工具优先读取，45 条轨迹的转换和统计只计主运行一次。
