# 阶段 24 Vulkan 取证索引

Godot 4.7.2 Mono、Vulkan Forward+、NVIDIA GeForce RTX 3060 Laptop GPU。固定帧对照含 63 组、126 次对局、8,192 逻辑帧、79,058 条视觉开关逐条一致的权威记录和 3,128 张 PNG。每组的 `sprite/trace.json` 记录原版事件、输入、状态、提交版本和实际图片。固定模式中，原版 F 对应 Godot 逻辑 L=F+14，`FramePostDraw` 图片标记提交版本 S=L+1。

## 固定对局

- 本体 24 组，2,784 逻辑帧、26,369 条、1,188 图。目录为 `D:/NewKOF-stage24-vulkan-` 加 `body-v3`（`crouch-guard-0`、`down-forward-heavy-and-kick-0`）、`body-controls`（18 组）、`body-boundaries`（`stand-to-down-0`、`jump-and-kick-0`）、`tech-landing`（`tech-56`）、`final-canonical`（`hit-0`）。
- 取消、后继与命令边界 32 组，4,318 逻辑帧、42,030 条、1,512 图。`routes-final` 取通过的 23 组；`camera` 重跑 `incoming-wave-16`、`incoming-wave-26`；`extended` 取延长原版参考后的 `mixed-27` 与 `hit-repeat-kick-56`、`hit-repeat-kick-walk-58`；`routes-first` 取 `whiff-16`；`final-canonical` 取 `hit-27`；`final-priority` 取 `tech-split-56`、`repeat-kick-light-45`。
- 模式 2 采用 `mode2-landing` 的 `hit-0`、`hit-27`，290 逻辑帧、2,816 条、172 图。共用扫击、反弹、倒地与起身按原版逐图核对。原版 `hit-27` 输入提交 1050，NewKOF 未进入 1000，也无弱 Wave；已提交的扫倒轨迹与同模式 `hit-0` 原版控制轨迹核对。`mode2` 保存修复前 F60 误选 Action 5000 的证据；修复后 Impact 使用 Action 5100。
- 同一完整配置的旧来源 5 组，800 逻辑帧、7,843 条、256 图。`coexistence-sources200-205/mixed-sequential`、`coexistence-source215/hit-22`、`coexistence-source400-final/hit-held-11`、`coexistence-source410/{hit-22,whiff-11}`。
- `recovery/hit-27` 另存 63 图，复用 `routes-first/hit-27` 的种子和输入，逐条比较 1,461 条权威记录。该补采在总数之外。Godot Vulkan 像素夹具输出 `MUGEN_GPU_PIXELS_PASS`。
- 阶段 23 的 47 组冻结门禁在 `D:/NewKOF-stage24-stage23-frozen-compatible`，7,898 逻辑帧、73,151 条在黄金归档及两种新回放之间逐条相同。试跑历史配置的兼容副本及命令见[复现说明](README.md#阶段-23-冻结门禁)。

## 关键画面

- [F15 重脚命中](<D:/NewKOF-stage24-vulkan-final-canonical/hit-0/sprite/frames/render-00039-state-00030.png>)、[F29 跌落起始](<D:/NewKOF-stage24-vulkan-body-v3/hit-0/sprite/frames/render-00053-state-00044.png>)与 [F15 镜像](<D:/NewKOF-stage24-vulkan-body-controls/mirror-hit-0/sprite/frames/render-00039-state-00030.png>)。
- 取消后的完整跌落分支包括 [F60 撞地](<D:/NewKOF-stage24-vulkan-recovery/hit-27/sprite/frames/render-00084-state-00075.png>)、[F64 反弹](<D:/NewKOF-stage24-vulkan-recovery/hit-27/sprite/frames/render-00088-state-00079.png>)、[F74 倒地撞击](<D:/NewKOF-stage24-vulkan-recovery/hit-27/sprite/frames/render-00098-state-00089.png>)、[F77 倒地静止](<D:/NewKOF-stage24-vulkan-recovery/hit-27/sprite/frames/render-00101-state-00092.png>)、[F87 起身](<D:/NewKOF-stage24-vulkan-recovery/hit-27/sprite/frames/render-00111-state-00102.png>)和 [F112 双方站立可控](<D:/NewKOF-stage24-vulkan-recovery/hit-27/sprite/frames/render-00136-state-00127.png>)。
- 接地受身用例在 [F60 进入 Action 5200](<D:/NewKOF-stage24-vulkan-tech-landing/tech-56/sprite/frames/render-00084-state-00075.png>)，在 [F88 恢复控制](<D:/NewKOF-stage24-vulkan-tech-landing/tech-56/sprite/frames/render-00112-state-00103.png>)。
- 同输入接触边界 [F16 不取消时扫中](<D:/NewKOF-stage24-vulkan-routes-final/edge-0/sprite/frames/render-00040-state-00031.png>)、[F16 取消抑制同帧接触](<D:/NewKOF-stage24-vulkan-routes-final/edge-16/sprite/frames/render-00040-state-00031.png>)、[F34 改由 Wave 击中](<D:/NewKOF-stage24-vulkan-routes-final/edge-16/sprite/frames/render-00058-state-00049.png>)。
- [F45 蹲防 Wave 削血 9](<D:/NewKOF-stage24-vulkan-routes-final/crouch-guard-27/sprite/frames/render-00069-state-00060.png>)、[F40 晚段 Wave 地面命中](<D:/NewKOF-stage24-vulkan-routes-first/whiff-16/sprite/frames/render-00064-state-00055.png>)、[F38 双席不同实体 ID 共存](<D:/NewKOF-stage24-vulkan-routes-final/incoming-wave-22/sprite/frames/render-00062-state-00053.png>)。
- [模式 2 F60 Action 5100](<D:/NewKOF-stage24-vulkan-mode2-landing/hit-27/sprite/frames/render-00084-state-00075.png>)至 [F112 模式 2 恢复](<D:/NewKOF-stage24-vulkan-mode2-landing/hit-27/sprite/frames/render-00136-state-00127.png>)。

## 实时稀疏取证

- 30 FPS `whiff-13` 的逻辑 L164 进入 440、L181 取消、L205 Wave 命中。[R293 / S205](<D:/NewKOF-stage24-vulkan-rt30-single/whiff-13/sprite/frames/render-00293-state-00205.png>)实际显示接触前的已提交状态，Action 1000 tick 23、Helper Action 1020 age 8、目标站立。读回前 30 FPS，紧随读回的样本 26 FPS，之后恢复 30 FPS；L211 记录一次取证开销造成的积压，发生于核对区间之后。[状态及实测 FPS](<D:/NewKOF-stage24-vulkan-rt30-single/whiff-13/sprite/trace.json>)
- 60 FPS `whiff-16` 的逻辑 L85 进入 440、L101 取消、L125 Wave 命中。[R307 / S126](<D:/NewKOF-stage24-vulkan-rt60-single-long/whiff-16/sprite/frames/render-00307-state-00126.png>)显示同次已提交的命中，Action 1000、Helper Action 1020 age 9 与目标 Action 5012。读回前 56 FPS，紧随读回的样本 42 FPS，之后恢复 60 FPS；L130 的一次积压发生于核对区间之后。[状态及实测 FPS](<D:/NewKOF-stage24-vulkan-rt60-single-long/whiff-16/sprite/trace.json>)
- 三张连续读回的首次稳定帧率试跑分别测得 30→24 与 59→41 FPS，并出现读回期积压。正式稀疏复查改用每次一张。实时结果核对实际提交画面、源动作顺序、合法取消窗口及实体时钟。完整原版逐帧对照使用上面的固定取证。
