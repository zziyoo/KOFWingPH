# 阶段 27 Vulkan 证据

Godot 4.7.2 Mono，Vulkan Forward+，NVIDIA GeForce RTX 3060 Laptop GPU。原版 F 对应逻辑 `L=F+14`，图像标识的提交版本为 `S=L+1`。回放串行使用 GPU。 [复现命令](README.md)

正式配置共 79 对独立固定回放，默认 67，模式 2 为 12。合计 12,210 逻辑帧、113,995 条视觉开关权威记录和 3,245 张 `FramePostDraw` 图像。相同场景重复验收只计最后一次成功，交付构建专项复验另执行 21 对、31,241 条一致权威记录。实时、同种子复跑、试跑、历史回放和新配置旧招共存均单独计数。 [交付状态](../../local-mugen/stage27-vulkan-delivery-status.log) · [交付默认](../../local-mugen/stage27-delivery-default.log) · [交付模式 2](../../local-mugen/stage27-delivery-mode2.log)

## 440 恢复回归

- 阶段 26 原始物理输入在此前配置重放，F56 首次出现一步 `3.16796875` 差异。后续 F95 原版选择 205，旧 NewKOF 选择 200。新配置重放同一按键清单，保持、松开 `x` 两对通过，4,979 条开关权威记录一致。最终配置再用同一按键清单时仍为 4,979 条，F115 Wave 接触位置与原版一致。 [原输入失败](../../local-mugen/stage27-sweep-red.log) · [同输入试跑](../../local-mugen/stage27-sweep-green.log) · [正式配置重放](../../local-mugen/stage27-final-original-sweep.log) · [F115 命中图](<D:/NewKOF-stage27-verified/final-original-sweep/near-to-close-jab-wave-grounded-held/sprite/frames/render-00139-state-00130.png>)
- 440 可行走的自然恢复另在前、后、下前、中立及当帧出招五场通过，4,074 条开关记录一致。 [恢复动作对照](../../local-mugen/stage27-sweep-walk-vulkan.log)

## 站重脚本体试跑

- 默认与模式 2 的本体配置完成七对回放，7,273 条开关权威记录一致，210 张 `FramePostDraw` 图像。检查首末帧、攻击／受击框、地面反应、受击恢复及各自独立停顿。 [默认](../../local-mugen/stage27-heavy-body-default.log) · [模式 2](../../local-mugen/stage27-heavy-body-mode2.log)
- 默认 245 在 F8 命中，两引擎各自的 84 HP 匹配公式。默认 240 在 F18 命中，原版 107 HP，NewKOF 99 HP，分别按共享原版与分席 NewKOF 随机流验证。两招近／远命中与近蹲防／远站防图像均与权威提交状态对应。 [245 命中](<D:/NewKOF-stage27-body-smoke/default/near-hit/sprite/frames/render-00032-state-00023.png>) · [240 命中](<D:/NewKOF-stage27-body-smoke/default/far-hit/sprite/frames/render-00042-state-00033.png>) · [245 蹲防](<D:/NewKOF-stage27-body-smoke/default/near-crouch-guard/sprite/frames/render-00032-state-00023.png>) · [240 站防](<D:/NewKOF-stage27-body-smoke/default/far-guard/sprite/frames/render-00042-state-00033.png>)
- 模式 2 远脚仍为 State 240，实际显示 Action 241。命中发生在 F24，原版 107 HP，NewKOF 115 HP，分别匹配伤害公式。F6／9／12／15／17 五次移动都精确为 8。默认 Action 240 在旧 tick 28 移动 32，代表命中样本对应 F44。 [241 命中](<D:/NewKOF-stage27-body-smoke/mode2/far-hit/sprite/frames/render-00048-state-00039.png>) · [241 空挥及移动](<D:/NewKOF-stage27-body-smoke/mode2/far-whiff/sprite/trace.json>) · [240 位移](<D:/NewKOF-stage27-body-smoke/default/far-hit/sprite/trace.json>)

## 正式配置选招与输入

- 默认模式身体距 47.5／48／48.5 分别进入 245／245／240。每对 1,039 条权威记录。F44 旧 `b` 在 51.054688 按下，F45 以年龄 1 在 47.886719 选近重脚，前一 tick 持下再释放同样进入近脚。仍保持下进入 440，旧 `a` 和新 `b` 同时可选时 235 优先。 [阈值](<D:/NewKOF-stage27-verified/distance-decisive/threshold-exact/sprite/trace.json>) · [接近与释放下](<D:/NewKOF-stage27-verified/distance-decisive/sweep-b-buffer-release-down/sprite/trace.json>) · [持下](<D:/NewKOF-stage27-verified/distance-decisive/sweep-b-buffer-down/sprite/trace.json>) · [旧 a／新 b](<D:/NewKOF-stage27-verified/distance-decisive/sweep-old-a-new-b/sprite/trace.json>)
- 真正后退穿过阈值时，F44 的身体距为 45.3046875，F45 消费时为 48.47265625，进入 240。持续持后对照 F58 站防，两引擎坐标、反应及恢复符合已知定点边界。释放后探针在 F47–50 保留原版守势惯性，F58 接触时原版 X 为 19.12713、NewKOF 为 11.83335876。两边同在 F58 命中且各扣 107 HP，F90 目标恢复，按各自接触起点核对结算期间的位移，F90 剩余定点差为 0.00334354。两对各 1,448 条开关权威记录。 [持后](<D:/NewKOF-stage27-verified/outward/sweep-b-buffer-away-ground-guard/sprite/trace.json>) · [释放后](<D:/NewKOF-stage27-verified/outward-release/sweep-b-buffer-away-ground/sprite/trace.json>) · [原始差异日志](../../local-mugen/stage27-final-distance-decisive.log)
- 440 自然恢复 F55／56／57 的两引擎 X 依次为 −22、−18.83203125、−15.6640625。保持原输入，F90 `a` 在 26.140625 选 235、29.30859375 选 230，`b` 在 45.1484375 选 245、48.31640625 选 240。四对 6,180 条视觉开关权威记录一致。 [两条 28 与两条 48 对照](../../local-mugen/stage27-final-sweep-thresholds.log)
- F103 全局 Pause 内物理 `b` 写入 Helper 时钟，F104 以年龄 1 提交新的 240。F106→107 同样只提交一次，另核对 F102 的 State 1000 自然许可 `var(2)=2`。已有 Wave 实体及前后状态三对各 1,685 条权威记录一致。 [F103→104](<D:/NewKOF-stage27-verified/distance-decisive/far-global-pause-b-103/sprite/trace.json>) · [F106→107](<D:/NewKOF-stage27-verified/wave-boundaries/far-global-pause-b-106/sprite/trace.json>)
- 默认正式配置的本体 12/12，覆盖双席、镜像、站蹲命中、合法格挡和起手中断。 [最终默认本体日志](../../local-mugen/stage27-final-bodies-default.log)
- 模式 2 原配置漏掉 F24 的远脚格挡。加入原版 `attack.dist=230` 和入防惯性后，F24 正确格挡。站防候选又在 F53 提前移动一步，最终保持该帧位置，F54 才迈步 3.16796875，蹲防仍原地恢复。两对 2,483 条视觉开关权威记录一致。 [第一次失败](../../local-mugen/stage27-final-bodies-mode2.log) · [F53 失败](../../local-mugen/stage27-final-mode2-guard.log) · [最终站防图](<D:/NewKOF-stage27-verified/mode2-guard-exit/far-guard/sprite/frames/render-00048-state-00039.png>) · [最终蹲防图](<D:/NewKOF-stage27-verified/mode2-guard-exit/far-crouch-guard/sprite/frames/render-00048-state-00039.png>) · [修正复验](../../local-mugen/stage27-final-mode2-guard-exit.log)

## 取消与保留候选

- 245 空挥样本的旧 tick 4、8 直接进 1000，旧 tick 29 走自然许可。签名轨迹延长至 F260，分别核对 F115→221、F119→225、F140→246 的 Wave 命中到恢复，三对共 8,253 条开关记录。 [首 tick Wave 图](<D:/NewKOF-stage27-verified/wave-extended-signed/near-wave-whiff-4/sprite/frames/render-00138-state-00129.png>) · [末 tick Wave 图](<D:/NewKOF-stage27-verified/wave-extended-signed/near-wave-whiff-8/sprite/frames/render-00142-state-00133.png>) · [自然 Wave 图](<D:/NewKOF-stage27-verified/wave-extended-signed/near-wave-whiff-29/sprite/frames/render-00163-state-00154.png>)
- 混合 `236+x+b` 仍在 245 旧 tick 4 优先 Wave，F115 命中、F221 恢复，2,751 条开关记录。直接命中／冻结输入两个样本都在旧 tick 4 提交，F42 Wave 命中。F109 原引擎触及镜头边界 x252.25，NewKOF 按各自速度推进，随后两边的落地与恢复帧相同。合计 3,676 条权威记录。 [混合指令](<D:/NewKOF-stage27-verified/wave-extended-signed/near-wave-mixed-4/sprite/trace.json>) · [命中 Wave](<D:/NewKOF-stage27-verified/wave-boundaries/near-wave-hit-4/sprite/trace.json>) · [冻结输入](<D:/NewKOF-stage27-verified/wave-boundaries/near-wave-pause-3/sprite/trace.json>)
- `a+b`、`x+b`、`y+b`、下＋`y+b` 依原始候选顺序处理。250 的输入只保留，805 的距离内输入同样只保留，越界回落 245。245 旧 tick 4 前＋`x` 保留 300，旧 tick 8 前下＋`y` 保留 310，旧 tick 9 拒绝提前取消。九对共 11,289 条开关记录。 [候选竞争与保留日志](../../local-mugen/stage27-final-isolation.log)
- 805 的目标资格按 MoveType。F84 的目标为 5110／H，物理前＋`b` 回落 245。F95 的目标为 5120／I，原版选择 805，NewKOF 在对应帧消费候选。原版 F96 的 805→245 由延期本体产生，已核对 NewKOF 无错误动作。两对各 1,635 条视觉开关权威记录，交付构建重放。 [F84 图像](<D:/NewKOF-stage27-verified/delivery-805/wakeup-reservation-84/sprite/frames/render-00108-state-00099.png>) · [F95 与延期后继](<D:/NewKOF-stage27-verified/delivery-805/wakeup-reservation-95/sprite/trace.json>) · [交付复验](../../local-mugen/stage27-delivery-805.log)
- 245 第一次格挡后，F24 合法 Wave，F48 第二次格挡并按原版削血 9。另跑 245→240、240 自然重进与 240→245。两引擎首次接触的位移使用既定定点误差，二次接触核对各自已验证静止点后的位移。五对 7,741 条开关记录一致。 [Wave 格挡](<D:/NewKOF-stage27-verified/quantized-contacts/near-wave-guard-8/sprite/trace.json>) · [再次接触](../../local-mugen/stage27-final-quantized-contacts.log)
- 模式 2 交付构建八对通过。245 旧 tick 4 保留 1050，旧 tick 9 拒绝，旧 tick 29 恢复后保留 1050。Action 241 在旧 tick 43 恢复后保留 1050。`y+b` 和后＋`b` 分别保留 250、805。八对均无弱 Wave 实体。 [模式 2 交付回放](../../local-mugen/stage27-delivery-mode2.log)

## 新配置兼容回放

- 使用阶段 27 默认角色重放七个旧 Wave 来源，含 235 的旧 tick 5 和 8。另核对 430 自然及间接 Wave、235 到 430、440 扫倒、接地受身和追击限制。13 对固定 Vulkan、1,965 逻辑帧，匹配 19,076 条共享历史记录和 19,218 条当前视觉开关记录。 [共存日志](../../local-mugen/stage27-final-current-profile.log)
- 交付构建实际 Vulkan 重放冻结阶段 13C，323 个角色及实体帧和 69 个取消帧全部一致。 [13C](../../local-mugen/stage27-delivery-13c.log)

## 实时采样

- 30 档选择默认 245 近身命中，R274／S163 显示 245 的动作 tick 1。图像读回前、后、稳定采样实测 **30／27／30 FPS**。取证时刻前无逻辑积压，读回后 L170 报告一次积压，随后回到 30。 [实际图像](<D:/NewKOF-stage27-verified/realtime-30/near-hit/sprite/frames/render-00274-state-00163.png>) · [帧率及事件](<D:/NewKOF-stage27-verified/realtime-30/near-hit/sprite/trace.json>)
- 60 档选择默认 240 远身空挥，R276／S85 显示 240 的动作 tick 2。图像读回前、后、稳定采样实测 **59／60／60 FPS**，全场未报告逻辑积压。 [实际图像](<D:/NewKOF-stage27-verified/realtime-60/far-whiff/sprite/frames/render-00276-state-00085.png>) · [帧率及事件](<D:/NewKOF-stage27-verified/realtime-60/far-whiff/sprite/trace.json>)
- 距离 48 固定回放独立同种子重跑，1,039 条权威记录一致。Vulkan 像素夹具确认绘制及渲染路径，继续报告已有缺失绑定诊断。 [同种子](../../local-mugen/stage27-final-repeat.log) · [像素验证](../../local-mugen/stage27-final-gpu-pixels.log)
