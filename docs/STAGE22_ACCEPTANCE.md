# 阶段 22 Terry 蹲轻拳与弱 Power Wave 验收

范围为两套实验预设的 State 400 本体及默认模式的 400→1000。既有阶段 21 正式提交 `9c61715f19396a7e3c14185a8884291972129f50` 已在 GitHub `main` 历史中。本轮开工时 GitHub 实际 HEAD 为 `0e9db8e3b9f92ca2ad96a0c505fd713fe0029b52`，该提交已纳入先前的用户项目配置差异。开工与复核的 `project.godot` SHA-256 均为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。

## 验收矩阵

- **22A 原版规则 · PASS。** 29/29 轨迹验证下＋轻拳、蹲姿锁定、两种防御、打断、镜像及恢复。默认模式仅旧 tick 4 允许显式取消。旧 tick 13 的新手势使用自然恢复许可。模式 2 原版进入 1050。[原引擎轨迹与复现](STAGE22_REFERENCE.md)
- **22B 本体 · PASS。** 固定帧核对 3／2／8 的起手、生效、收招，Action 400 的碰撞框、动态伤害和独立 HitPause，及 State 11 后的 Action 12 可控起身。标准命中 38 HP，停顿 11／13，格挡零伤害且停顿 11／11。持续按下保留蹲姿，提前松下保留攻击中的蹲姿。被近站轻拳打断后无旧攻击框或迟到命中。无 400 取消配置的独立本体验收通过 8 场、16 次对局和 13,032 条视觉开关一致的权威记录。
- **22C 取消与输入 · PASS。** F8 的早手势经 Helper 缓冲在 F9／旧 tick 4 提交。旧 tick 4 可在空挥、命中、站防及蹲防后取消，HitPause 内的有效手势解冻后提交。旧 tick 5 手势拒绝弱 Wave，符合年龄与方向的轻拳候选在旧 tick 6 回落。按下方向可进 400，松下后普通候选按身体距离选站轻拳。新实例从 tick 0 开始。阶段 18 的按下后松键防补发由冻结门禁复核。
- **22D 兼容选项与共存 · PASS。** 阶段 22 默认预设同场通过 400／200 与 205／200 的混合取消，并对照 215 双段及守势松键。模式 2 的相同物理手势验证未生成 1000／1020。补充实战覆盖在场 Helper 于取消前和取消后打断对手的两条路径，原 Helper 继续存续。
- **22E 原版与实际图形 · PASS。** 27 个默认场景、2 个模式 2 对照，固定 Vulkan 共 58 次对局、1,884 张实际图像、48,260 条覆盖开关一致的权威记录。另有 8 场沿用旧来源和守势对照，9,695 条权威记录一致。同种子 `hit-held-11` 与 `mixed-20` 分别复跑 1,765 和 1,680 行完全一致。稀疏实时实测 30／30 和 59／57 FPS。像素夹具输出 `MUGEN_GPU_PIXELS_PASS`。
- **22F 历史与交付 · 进行中。** C# 专项 71/71、全量 1,379/1,379，Python 新专项 3/3、全量 86/86，Debug 构建 0 警告、0 错误。阶段 13C 的 323 帧及 69 条取消记录已经逐条重跑对齐。

## 配置及结算

- 阶段 21 的 `groundHurtboxes.hitstun` 和 `groundHurtboxes.hitstunRecoveryFrames` 在退出冻结后的最后若干硬直 tick 采样晚段矩形。依据 Action 5007 的持帧证据。
- `protectGroundHitstunExit` 只在地面受击硬直退出当帧排除近战和投射物接触，依据 State 5001 的单帧 `NotHitBy`。投技仍沿用自身判定。
- `retainProjectileGuard` 与 `projectileGuardRecoveryFrames` 表达有条件的地面防御续接和恢复期，`preserveGuardEntryMomentum` 保留进入守势时的权威水平惯性。阶段 21 的松键与守势起步样本分别验证这些规则。阶段 22 默认继承，模式 2 保持阶段 21 的关闭值。
- `returnsToCrouch` 和 `groundHurtboxes.crouchRise` 保存蹲拳退出后的权威起身进度。`lightGroundReaction` 与 `groundHurtboxes.lightHitstun` 对 State 400 造成的地面反应启用源受击框。新规则随战斗快照保存。无配置时沿用历史受击和待机规则。
- 取消执行器只增加对蹲姿目标的当帧方向许可。窗口、命令优先级、输入消费、State 1000 和 Helper 1020 共用原实现。旧攻击在检测前终止，已提交接触保留。原版扣血提交相位与共享 RNG、NewKOF 的原子提交及独立 RNG 保持既有边界。

## 图形与对照证据

固定复跑输出位于 `D:/NewKOF-stage22-vulkan`。每场 `sprite/trace.json` 可配对同一次运行的 Vulkan 图像。固定 F→L 为 F+14，图片为对应提交状态 L+1。

- `fixed/whiff-0/sprite/frames/render-00043-state-00034.png` 显示自然退出后的第一个起身画面。接下来三帧使用 Action 12 第二张图。
- `fixed/hit-held-11/sprite/frames/render-00044-state-00035.png` 显示冻结后从旧 tick 4 进入新招式。R59／S50 显示年龄 0 的 Helper。
- `mixed-full-v2/mixed-20` 的 R62／S53 和 R144／S135 分别显示 400 来源的 P1 Helper 和 200 来源的 P2 Helper。实体 ID、拥有者和本地时钟独立。
- `fixed/hit-21` 的修复前日志记录 L39 多结算一次站轻拳。修复后沿用相同输入和距离，F25／L39 保留站轻拳出框，原版与 NewKOF 均无第二次接触。
- `near-fallback-v2/near-hit-21` 将两边的初始中心距都设为 36。F22／L36 改进 205，F25／L39 两边均发生第二次接触。对照验证距离会改变合法回落目标，第一拳的受击框也能被第二次实际接触替换。
- `incoming/incoming-wave-25` 的 F24／L38 中，P2 已存在的 Helper 命中 P1 的 State 400，原版接触伤害为 99。两边均取消未提交的出招机会，已有实体在其独立时钟上继续运行。双视觉覆盖 1,763 条权威记录一致。
- `after-cancel-v3/incoming-wave-20` 的 F20／L34 合法从 400 取消到 1000，F29／L43 被 P2 既有 Helper 打断。两边只有 P2 的实体出生和继续运行。双视觉覆盖 1,655 条权威记录一致。原版此接触实扣 107，NewKOF 实扣 99，分别用各自的权威 RNG 操作数重算。
- 实时 `live30` 的 R216–218 对应状态 47／49／51，实测 30／30 FPS，Wave 在 L42 命中。`live60-spacing2` 的 R231–233 对应状态 42／43／44，实测 59／57 FPS，Wave 在 L38 命中。实时按实际逻辑采样对照。60 FPS 首轮的一对物理边沿落到同一个逻辑帧，导致方向先于按键采样释放，验证器拒绝该次结果。重跑仅调整测试注入间隔，保留物理边沿顺序。
- 本阶段默认固定样本使用双方相同初始条件和按键。模式 2 对照在原版进入 1050 后，使用无 QCF 的原版控制轨迹核对共同的 State 400 本体。阶段 21 旧空挥样本仍保留已记录的跳跃高度差。
- 同种子权威日志以 LF 拼接并保留末尾换行后取 SHA-256。`hit-held-11` 为 `4a45bbeb12b09be0bdc0810dfc0f9b282da9a61457e7629ede92a0abdedb11f4`，`mixed-20` 为 `2dcfc9b1eace280b761ce626d3311f472268ae8068815a1735594858b6ba39da`。

复现命令见[验证工具](../tools/Stage22Validation/README.md)。C# 测试日志、Vulkan 执行记录及原版逐场哈希保存在 `local-mugen/stage22-*.log` 和 `local-mugen/stage22-reference/results`。

## 下一条路径的静态准备

候选为默认模式蹲重拳 State 410→1000。`Terry.cmd:1703–1714` 的人体入口要求下方向及重拳，`Terry-N.cns:1034` 绑定 Action 410，结束进入可控 State 11。AIR 时长 2／3／8／7／7，单段 Mid HitDef 推导起手 5、生效 8、收招 14，Clsn1 本地 X20–75、Y49–64。伤害基础值普通 13、反击 15，停顿、击退及原版实际伤害仍待实测。State −2 的 `AnimElemTime(3)>0 && AnimElemTime(4)<0` 静态推导旧 tick 6–12。应先采集窗口边界、下＋重拳竞争和命中／格挡／空挥。当前阶段仅记录静态结论。
