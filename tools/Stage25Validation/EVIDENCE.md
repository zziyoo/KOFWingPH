# 阶段 25 Vulkan 取证索引

Godot 4.7.2 Mono、Vulkan Forward+、NVIDIA GeForce RTX 3060 Laptop GPU。固定场景的原版 F 对应逻辑 L=F+14，`FramePostDraw` 图片为提交版本 S=L+1。目录中的 `sprite/trace.json` 关联原版轨迹、实际命令、权威状态和保存的图像。

58 对阶段 25 固定回放共比较 6,116 逻辑帧和 56,271 条视觉开关权威记录，保留 1,017 张逐帧图像及 116 张结束截图。每对日志和 `trace.json` 引用的实际图像都已复核。后续修正改变了 430 的近拳选择、430／400 的连续格挡和已完成手势的识别，受影响场次用最终构建和配置重跑。固定目录在 `D:/NewKOF-stage25-verified`。

- `key` 取 `hit`、`crouch-hit`、`stand-guard`、`crouch-guard`、`mirror-hit`、`interrupt`。
- `controls` 取 `whiff`、`p2-hit`、`sweep-aged-a`、`both-kicks`、`forward-down`、`back-down`、`light-punch-compete`、`heavy-punch-compete`、`down-forward-heavy-compete`、`to-kick-before-direction`、`counter-sweep`、`air-entry`。`falling` 取 `airborne-fall-locked`、`airborne-fall-overlap`。
- `chain` 取 `far-jab-to-kick-9`、`far-jab-to-kick-12`、`far-jab-to-kick-16`、`crouch-jab-to-kick-11`、`crouch-jab-to-kick-17`、`to-kick-11`、`to-kick-18`、`to-crouch-jab-11`、`to-far-jab-11`。`grounded-final` 取 `hit-to-near-jab-22`、`guard-to-kick-22`、`guard-release-back-22`、`guard-jab-release-back-22`、`sweep-release-down`。`final-air-probe` 取 `to-close-high-target-jab-11`。
- `wave-final` 取 `wave-9`、`wave-18`、`wave-19`、`wave-20`、`hit-wave-10`、`wave-17-to-down`、`wave-forward-neutral-boundary`、`near-jab-to-kick-hit`。`wave-control` 取 `wave-forward-held-through-exit`、`through-400-wave`。`postfix-wave-guard` 取 `hit-wave-30`、`guard-release-down-22`。`final-completed-wave` 取 `wave-mixed-at-exit`、`wave-crouch-fallback`、`wave-startup-7`。
- `final-motion-through-recovery` 取 `wave-extra-forward-before-button`、`wave-df-hold-before-button`、`wave-db-on-button`、`wave-back-on-button`。四场均在 F19 自然进入 Wave，F43 命中，F84 受击者恢复。每场各 115 逻辑帧、1,096 条权威记录、13 张关键图。最初 F75 的原版短轨迹不足以检查恢复，最终使用 F100 轨迹重跑。 [重复前方向轨迹](<D:/NewKOF-stage25-verified/final-motion-through-recovery/wave-extra-forward-before-button/sprite/trace.json>) · [逐场日志](../../local-mugen/stage25-verified-final-motion-through-recovery.log)
- `mode2` 取 `mode-body-guard`、`mode-chain`、`wave-13-mode`。`final-mode2` 取 `wave-19-mode`，保存 100 个 NewKOF 逻辑帧。原版 Pause 对照截止于 1050 输入被保留的 F19，之后原版运行 1050。最终构建重跑两场 Wave 边界，每场 909 条开关记录一致，均无弱 Wave 实体。 [模式 2 最终复跑](../../local-mugen/stage25-mode2-final-gate.log)
- 最终配置重新运行 `hit` 与 `stand-guard`，分别与各自两种视觉的旧权威记录一致。`final-repeat/hit` 与 `final-key/hit` 的 904 条同种子权威记录逐条一致。 [最终复跑日志](../../local-mugen/stage25-verified-postfix.log)

## 本体与动作边界

- `D:/NewKOF-stage25-verified/key` 的 6 场使用 640×360，覆盖站姿命中、蹲姿命中、站防尝试、蹲防、镜像和中断。两种视觉覆盖核对 5,428 条权威记录，保留 144 张逐帧图像。 [F8 站姿命中](<D:/NewKOF-stage25-verified/key/hit/sprite/frames/render-00032-state-00023.png>) · [F8 蹲姿命中](<D:/NewKOF-stage25-verified/key/crouch-hit/sprite/frames/render-00032-state-00023.png>) · [F8 蹲防零削血](<D:/NewKOF-stage25-verified/key/crouch-guard/sprite/frames/render-00032-state-00023.png>) · [F6 轻脚中断](<D:/NewKOF-stage25-verified/key/interrupt/sprite/frames/render-00030-state-00021.png>)
- `wave-startup-7` 在原版 F7 识别根 Wave，Godot L21 保留尚未消费的 `236A`，当时仍处于 430 Startup。两边均无 200 或 1000 的后续启动。`wave-9`、`wave-18` 检查 430 中的 200 回落，`hit-wave-10` 检查命中停顿中的手势失效。 [Startup 轨迹](<D:/NewKOF-stage25-verified/final-completed-wave/wave-startup-7/sprite/trace.json>)
- `wave-19` 在 F19 自然结束的旧 tick 14 进入 1000，F34 Helper 1020 出生，F43 接触。`wave-20` 在 F19 自然结束当帧前行 3.16796875 像素，F20 才进入 1000。 [F34 Wave 出生](<D:/NewKOF-stage25-verified/wave-final/wave-19/sprite/frames/render-00058-state-00049.png>) · [F43 Wave 接触](<D:/NewKOF-stage25-verified/wave-final/wave-19/sprite/frames/render-00067-state-00058.png>) · [下一帧 Wave 输入轨迹](<D:/NewKOF-stage25-verified/wave-final/wave-20/sprite/trace.json>)
- 205→430 在 `near-jab-to-kick-hit` 的 F23 旧 tick 7 发动，430→205 在 `hit-to-near-jab-22` 的 F22 旧 tick 6 发动。后者选招时原版身体距为 6，L36 启动 205，L39 命中。每场两种视觉覆盖均比较 908 条权威记录。 [205→430](<D:/NewKOF-stage25-verified/wave-final/near-jab-to-kick-hit/sprite/trace.json>) · [430→205](<D:/NewKOF-stage25-verified/grounded-final/hit-to-near-jab-22/sprite/trace.json>)
- F8 第一次格挡后，F25／L39 的第二次 430 在保持下后的 `guard-to-kick-22` 与释放后键但保持下的 `guard-release-back-22` 中仍为零削血格挡。`guard-jab-release-back-22` 的后继 400 同样格挡。`guard-release-down-22` 改为站姿轻击反应，恢复控制时两引擎走速相同，NewKOF 的走位早一 tick。 [连续格挡](<D:/NewKOF-stage25-verified/grounded-final/guard-release-back-22/sprite/trace.json>) · [放开下方向](<D:/NewKOF-stage25-verified/postfix-wave-guard/guard-release-down-22/sprite/trace.json>)
- `wave-forward-held-through-exit` 的 430→200 发生于 F17，仍按 x 时 200 在 F21 进入 1000，F45 Wave 命中，F86 恢复。`through-400-wave` 的 430→400→1000 分别在 F11 与 F15，F39 命中，F80 恢复。`hit-wave-30` 的 Wave 在 F46 击飞，逐 tick 对照至 F152 恢复控制。 [保持 x 后的 Wave](<D:/NewKOF-stage25-verified/wave-control/wave-forward-held-through-exit/sprite/trace.json>) · [经过 400 的 Wave](<D:/NewKOF-stage25-verified/wave-control/through-400-wave/sprite/trace.json>) · [击飞至恢复](<D:/NewKOF-stage25-verified/postfix-wave-guard/hit-wave-30/sprite/trace.json>)
- `near-high-target-jab-to-kick-12` 是普通跳跃物理的兼容性样本。原版 205 在 F18 空挥，NewKOF 的既有跳跃位置 Y=53.2005 使 205 在 L32 命中，停顿使 F22 对应的轻脚输入过期。该对局保留差异日志。 [兼容性样本](<D:/NewKOF-stage25-verified/chain/near-high-target-jab-to-kick-12/sprite/duel.log>)
- `to-close-high-target-jab-11` 在对方跳跃时验证另一方向，F21 的旧 tick 6 从 430 转到 205，F24／L38 命中，双方视觉覆盖核对 908 条记录。 [近拳转接图像与轨迹](<D:/NewKOF-stage25-verified/final-air-probe/to-close-high-target-jab-11/sprite/trace.json>)
- `wave-mixed-at-exit` 的 F19 同时输入斜下前、x、a、b。原版和最终 NewKOF 都读取已经完成的手势，在自然恢复当帧优先进入 1000，F43 Wave 命中。`wave-crouch-fallback` 在 F12 也识别斜下前＋x，旧 tick 7 只提交 400。两场分别比较 961 与 909 条权威记录。 [混合输入 Wave](<D:/NewKOF-stage25-verified/final-completed-wave/wave-mixed-at-exit/sprite/trace.json>) · [动作中回落](<D:/NewKOF-stage25-verified/final-completed-wave/wave-crouch-fallback/sprite/trace.json>)

## 实时与像素夹具

- 30 FPS 使用 `whiff`，L163 启动 430，R274／S165 显示 Action 430 tick 1。读回前、读回后与稳定样本为 30／29／30 FPS，无积压。 [提交图像](<D:/NewKOF-stage25-verified/rt30-final/whiff/sprite/frames/render-00274-state-00165.png>) · [FPS 与动作轨迹](<D:/NewKOF-stage25-verified/rt30-final/whiff/sprite/trace.json>)
- 60 FPS 使用同一原版空挥输入，L85 启动 430，R276／S91 显示 Action 430 tick 5。读回前、读回后与稳定样本为 59／58／60 FPS，无积压。 [提交图像](<D:/NewKOF-stage25-verified/rt60-final/whiff/sprite/frames/render-00276-state-00091.png>) · [FPS 与动作轨迹](<D:/NewKOF-stage25-verified/rt60-final/whiff/sprite/trace.json>)
- 最初两次实时命中探针保留在 `rt30` 与 `rt60`。30 档在图像提交前出现积压，60 档的截取前样本为 54 FPS。正式对照使用上述两次空挥稀疏采集。
- 最终构建按既有像素夹具的原始窗口尺寸运行，输出 `MUGEN_GPU_PIXELS_PASS`。夹具预期的缺失资源回退警告仍有记录。 [Vulkan 日志](../../local-mugen/stage25-gpu-pixels-release.log)

## 六个既有 Wave 来源

- 在最终运行时和阶段 25 配置下复用 5 个录制输入，验证 205、200、215、400、410、440 的原有 Wave 入口。805 逻辑帧包含 7,855 条与阶段 24 逐条相符的共享记录，阶段 25 的两种视觉覆盖相符记录共 7,997 条。 [实际输出](<D:/NewKOF-stage25-verified/old-wave-final>) · [逐场日志](../../local-mugen/stage25-old-wave-final.log)

## 阶段 24 冻结回放

- 63 对视觉开关、126 次 Vulkan 对局、8,192 逻辑帧和 79,058 条权威记录与黄金归档逐条一致。按原始角色配置及物理输入回放，分辨率为 320×180，保留每次对局的结束截图。 [总日志](../../local-mugen/stage25-frozen24-resume.log) · [候选目录](<D:/NewKOF-stage25-frozen24-resume>)
