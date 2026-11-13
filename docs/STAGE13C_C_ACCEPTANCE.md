# 13C-C 验收矩阵

- C1 投技：实现并验证。固定帧 Vulkan `13cc_throw_hit`、`13cc_throw_miss`、`13cc_throw_tech`、`13cc_throw_mirror`。状态 22–23 施投 810/受投 815 同时出现，命中状态 24 才扣 60、切释放与倒地，六个停顿逻辑帧后继续。落空没有受投姿态，拆投在状态 23 分离、双方满血并回待机。拆投无可确认的专属角色素材，沿用权威分离和拆投火花。原投技基线为 `13cc_baseline_throw_{hit,miss,tech}`。投技专项 44 项、Debug 构建通过。
- C2 飞行道具：每交锋以权威 ProjectileId 建立独立显示实例，按寿命差推进，镜像取速度。1070 是原 Terry 横向飞行形状，当前导入调色板将其可见像素解成纯黑，绑定明确用金色剪影和表现原点偏移适配，旧绑定用原创几何形状。固定帧 `13cc_final_projectile_clash` 的状态 29 同时显示年龄 4/0 的两个镜像实例，状态 38 抵消后均移除。`13cc_final_projectile_interrupt`、`13cc_final_projectile_block`、`13cc_final_projectile_team` 分别验证命中、格挡、KO 和换人后重新编号及回退。
- C3 特效：命中、重扫、防御、投技、特殊技、闪白、HitStop 和清场。待验收。
- 最终门禁：固定帧及实时 1v1/3v3、覆盖开关权威日志对照、专项、Debug 和全量回归。

范围为现有 Kai/Rei 战斗规则。第三方素材和截图保留在 Git 忽略目录，Terry 缺图诊断沿用 A。
