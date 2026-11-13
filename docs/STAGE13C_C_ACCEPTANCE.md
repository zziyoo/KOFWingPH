# 13C-C 验收矩阵（PASS）

- C1 投技 PASS（`c23b2b4`）。固定帧 Vulkan `13cc_throw_hit`、`13cc_throw_miss`、`13cc_throw_tech`、`13cc_throw_mirror`。状态 22–23 施投 810/受投 815 同时出现，命中状态 24 才扣 60、切释放与倒地，六个停顿逻辑帧后继续。落空没有受投姿态，拆投在状态 23 分离、双方满血并回待机。拆投无可确认的专属角色素材，沿用权威分离和拆投火花。原投技基线为 `13cc_baseline_throw_{hit,miss,tech}`。投技专项 44 项、Debug 构建通过。
- C2 飞行道具：每交锋以权威 ProjectileId 建立独立显示实例，按寿命差推进，镜像取速度。1070 是原 Terry 横向飞行形状，当前导入调色板将其可见像素解成纯黑，绑定明确用金色剪影和表现原点偏移适配，旧绑定用原创几何形状。固定帧 `13cc_final_projectile_clash` 的状态 29 同时显示年龄 4/0 的两个镜像实例，状态 38 抵消后均移除。`13cc_final_projectile_interrupt`、`13cc_final_projectile_block`、`13cc_final_projectile_team` 分别验证命中、格挡、KO 和换人后重新编号及回退。
- C3 特效：实现并验证。命中、投中、击飞、格挡和抵消按事件选择火花。投技冲击用落地位置，飞行道具对撞用权威锚点，普通命中用受击者胸口。火花寿命为 9 个已提交逻辑帧，闪白为 5 个；二者在 HitStop 期间继续计龄，角色动作冻结，飞行道具按实体寿命推进。取消和打断后已有冲击按原锚点收尾，KO 后短效以等效 60 Hz 淡出，换人清空。F4 关闭会同时清除火花、闪白和抖动。见 `13cc_gate_cancel`、`13cc_gate_guard`、`13cc_final_sweep`、`13cc_gate_throw`、`13cc_gate_fx_toggle`。
- 最终门禁 PASS。固定帧 1v1/3v3、实时 30/60 FPS、覆盖开关权威日志对照、专项 152/152、全量 1204/1204，均零失败、零跳过。Godot Debug 构建零警告、零错误。原创 Vulkan 夹具输出 `MUGEN_GPU_PIXELS_PASS`。B 的升龙、防御、取消及换人通过上述实际场次复核。A 工具与原始 Manifest 无改动。

## 图形证据

Godot 4.7.2 Mono、Vulkan Forward+、RTX 3060 Laptop GPU。每个场次在 `local-mugen/<场次>.args.txt` 保存完整命令参数，`.log` 保存同次日志，`shots/<场次>/` 保存 FramePostDraw 图像。`SpriteFrameAudit.logic + 1` 对应 `SpriteRenderAudit.logic`、`FeedbackAudit.state`、`ProjectileRenderAudit.state` 与 HUD 状态号。3v3 按 `VisualAudit.scope` 和捕获序号区分交锋。

1. 投中、挥空和拆投：`13cc_gate_throw`、`13cc_final_throw_miss`、`13cc_final_throw_tech`。镜像为 `13cc_final_throw_mirror`。
2. 发射、飞行、同屏实例、抵消：`13cc_gate_clash` 状态 25、29–37、38，抵消火花锚点 (12,60)。过期 `13cc_gate_expiry` 状态 114 剩 1 tick，115 实体及贴图消失。
3. 命中、格挡、打断：`13cc_final_projectile_interrupt` 状态 46–52，`13cc_final_projectile_block` 状态 31–36。发射方被重扫的同一逻辑帧，已发出的火球仍命中，见 `13cc_gate_owner_trade` 状态 25–26，双方各扣 40，两个反馈各自生成。另有被打断时继续保持实体年龄的专项。
4. 轻拳、合法取消、重扫、空地防御及特殊：`13cc_gate_cancel`、`13cc_gate_guard`、`13cc_final_sweep`、`13cc_gate_real30`、`13cc_gate_real60`。实时最终复查使用轻量末帧读回，前两次密集读回的局部积压只作为取证限制记录。
5. 3v3 多实体与换人：`13cc_gate_team` 首场两枚对撞，后续命中 KO 状态 78，状态重置后 P2 未配置覆盖的 Kai 发射原创回退实体。首场旧纹理、编号年龄与特效不进入第二场。

覆盖开关对照使用 `13cc_authority_<场景>_{sprite,plain}.log`，`--visual-audit --entity-audit` 记录角色血量、定点坐标、状态、事件、抓取身份及各飞行道具 ID/所有者/位置/速度/寿命。投中 80、对撞 68、3v3 105、打断 70，共 323 帧逐条相同。另复核取消场景 69 条原权威日志一致。

范围为现有 Kai/Rei 战斗规则。第三方素材和截图保留在 Git 忽略目录，Terry 缺图诊断沿用 A。
