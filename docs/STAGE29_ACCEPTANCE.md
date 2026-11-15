# 阶段 29 Terry 站立双键吹飞攻击

阶段 28 基线为 `d4a0873542820f962d847b70b67af98d8fcb90f6`。开工时本地 `main`、`origin/main` 和 GitHub `main` 一致，工作区干净。使用 Godot 4.7.2 Mono、.NET SDK 10.0.403 和 Python 3.11.9。原包 SHA-256 为 `299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698`。`project.godot` SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。

正式验收 **PASS（29A–29G，7/7）**。原版、Godot 和自动化分别保存证据。 [复现入口](../tools/Stage29Validation/README.md)

## 验收矩阵

- **29A 原版合同 · PASS。** `Terry.cns` 定义 State 250，`Terry.air` 定义 Action 250。State −2 的第 11／12 动画元素谓词换算为旧 stateTime 24–25，原版实测两侧边界。75/75 个签名场景和 23/23 个追加命令观测通过。 [原版取证](STAGE29_REFERENCE.md)
- **29B 本体 · PASS。** 两模式共用 Action 250。有效时序 23／3／23，单段接触。站防、蹲防和空中禁止格挡均有实际对局。两席、镜像、对打、打断、距离 146／147、完整升空、着地反弹、受身、倒地及起身均有验证。 [Vulkan 证据](../tools/Stage29Validation/EVIDENCE.md)
- **29C 双键和竞争 · PASS。** 同帧 `y+b` 优先于合格的投技／普通技和新 `F+x`，完整弱 Wave 优先于双键。Helper 99999 的 `var(7)` 源写入、同轮衰减、输入年龄和冻结时钟有原版观察。复跑核对分离边沿、重复按键、缓冲下方向的释放和补入、世界 Pause、落败候选和单次消费。 [输入矩阵](../tools/Stage29Validation/EVIDENCE.md)
- **29D 进出边 · PASS。** 250→300 的旧 stateTime 24–25。旧 21–23 的有效 `F+x` 可通过 Helper 在 24 提交，旧 20 过期，旧 26 拒绝。自然恢复于旧 49 开放入口。冻结的命中／格挡输入在解冻后可提交合法的新攻击实例。 [窗口和实例](../tools/Stage29Validation/EVIDENCE.md)
- **29E 特殊技和两模式 · PASS。** 默认模式的 250→1000、250→300→1000 和恢复后 250→1000 分开对照。250 普通命中禁追，反击可让 Helper 1020 在 F60 追加接触。310 和模式 2 的 1050 已核实原版许可与 NewKOF 单次保留消费。模式 2 无默认 Wave 实体。八条既有弱 Wave 来源在新预设中通过。 [特殊技与共存记录](../tools/Stage29Validation/EVIDENCE.md)
- **29F 确定性和画面 · PASS。** 最终固定 Vulkan 44 对（43 个用例加一次独立复跑），6,244 逻辑帧、58,580 条视觉开关权威记录逐条一致，取证 2,172 张实际提交图像。独立同种子样本对齐 1,538 条权威记录。实时实测 30／30／30 与 60／60／60，两档各稀疏取图一次，采样日志未见积压。Vulkan 像素夹具通过。 [固定、实时与像素证据](../tools/Stage29Validation/EVIDENCE.md)
- **29G 共存与交付 · PASS。** 最终构建通过 C#、Python、Debug 构建及 13C Vulkan。阶段 14–26 的最终构建冻结回放逐条一致，阶段 27／28 的定向运行时门禁通过。阶段 28 父预设和项目文件哈希已核对。

## 测试、构建和已验证冻结门禁

- C# 专项 302/302、全量 1,544/1,544，零失败、零跳过。31 个新增样本覆盖保留候选的真实入口、双键边沿、Helper 年龄、自然和取消时钟、镜像及守势反应、追击限制和反击例外。 [专项运行](../local-mugen/stage29-delivery-csharp-focused.log) · [全量](../local-mugen/stage29-delivery-csharp-full.log)
- Python 专项 6/6、全量 131/131，零失败、零跳过。两次独立生成的六份 JSON SHA-256 完全一致，校验了阶段 28 父配置哈希。 [专项](../local-mugen/stage29-importer-focused-delivery.log) · [全量](../local-mugen/stage29-importer-full-delivery.log) · [生成确定性](../local-mugen/stage29-importer-determinism.log)
- BattleCore 和 Godot Debug 均零警告、零错误。测试工程构建零错误，保留既有 `CS8600`（`ThrowTests.cs:284`）与 `xUnit2029`（`CombatEventTests.cs:197`）。 [BattleCore](../local-mugen/stage29-delivery-core-build.log) · [Godot](../local-mugen/stage29-delivery-godot-build.log) · [测试工程](../local-mugen/stage29-delivery-tests-build.log)
- 最终 Debug 构建 `NewKOF.dll` 为 `94a31158…`，`NewKOF.BattleCore.dll` 为 `cab11a29…`。同构建实际 Vulkan 重放 13C 的 323 个角色／实体帧与 69 个取消帧，均逐条匹配。阶段 28 记录的按键注入偏斜本次未再现。 [冻结回放](../local-mugen/stage29-delivery-frozen-13c.log)
- 阶段 28 六个签名范围内的既有运行时场景使用冻结预设再次执行 headless 回放。包含 `MoveAudit`、`VisualAudit`、`EntityAudit` 和 `SpriteFrameAudit` 的 2,412 条记录与阶段 28 最终归档逐条一致。窗口越界样本实际启动 440，没有 300 的 `MoveStarted`。 [执行日志](../local-mugen/stage29-stage28-equivalence.log) · [逐条对照](../local-mugen/stage29-stage28-runtime-compare.log)
- 同构建复验阶段 27 的 805 资格、模式 2 Action 241 防御退出、以及阶段 29 预设的 440 F56 自然步行和 F95 选择 205。旧 Wave 路径在新预设重放 13 对，共 1,965 逻辑帧、19,076 条共享记录与 19,218 条本阶段开关视觉记录，核对了实际生效的 `launchAir` Clsn2 投影。245 单独用 F260 原版扩展轨迹验证窗口首末两对，开关视觉各对 2,751 条记录一致。 [阶段 27 和旧招式门禁](../tools/Stage29Validation/EVIDENCE.md)

### 最终构建的阶段 14–26 冻结回放

全部使用与 13C Vulkan 相同的 Debug DLL 指纹。固定 60 FPS 的 headless 回放校验物理输入及开关视觉权威记录。合计 **542 对、65,069 个逻辑帧、538,586 条权威记录**，阶段 14–26 的 13 个 `Matched Stage` 标记完整且只计一次。

- 14、15、16 共 94 对、9,026 帧、36,104 条。 [14／15](../local-mugen/stage29-delivery-frozen-a.log) · [16](../local-mugen/stage29-delivery-frozen-a3.log)
- 17、18、19、20 共 149 对、15,049 帧、120,272 条。 [17](../local-mugen/stage29-delivery-frozen-b.log) · [18](../local-mugen/stage29-delivery-frozen-a4.log) · [19](../local-mugen/stage29-delivery-frozen-a5.log) · [20](../local-mugen/stage29-delivery-frozen-e.log)
- 21、22、23 共 123 对、19,209 帧、177,726 条。 [21](../local-mugen/stage29-delivery-frozen-b.log) · [22](../local-mugen/stage29-delivery-frozen-b2.log) · [23](../local-mugen/stage29-delivery-frozen-f.log)
- 24、25、26 共 176 对、21,785 帧、204,484 条。 [24／25](../local-mugen/stage29-delivery-frozen-c2.log) · [26](../local-mugen/stage29-delivery-frozen-d2.log)

## 定向失败与修正

- 首轮候选视觉别名的图集尚未由 Godot 导入，该轮空挥的帧图像检查失败。导入图集后，空挥与完整命中两对 Vulkan 中敌我位置、命中帧和最终恢复逐帧对齐。终版重新导入别名时两个 DLL 和项目哈希仍与候选标识一致。 [初始日志](../local-mugen/stage29-vulkan-smoke.log) · [复跑](../local-mugen/stage29-vulkan-smoke-green.log) · [正式导入](../local-mugen/stage29-delivery-atlas-import.log)
- 原版常规击飞设置禁追许可。预配置的 Vulkan 复验在 F56 错误产生 ProjectileHit，使后继世界 Pause 和 Helper 年龄偏离；正式配置保留实体并拒绝接触。反击轨迹使用不同的原版限制条件，正式配置按实际 Clsn2 在 F60 发生飞行道具命中。250 接 300 后也读取同一追击资格。 [初始对局](../local-mugen/stage29-vulkan-chain-probe.log) · [定向日志](../local-mugen/stage29-delivery-directed.log)
- 250 格挡进入 300 后，原版在 F51、F68 继续防住两段。预配置继承的 300 把命中反应高度当成防御高度，且缺少已在硬直中的防御资格。新预设为两个 HitDef 导入 `guardflag=M` 和地面续防。固定 Vulkan 复跑验证了原版的两次格挡、F80 取消、F105 飞行道具格挡及削血。 [复跑](../local-mugen/stage29-delivery-directed-recheck.log)
- 阶段 16 两个 headless 变体出现 Mono 退出异常。两个失败日志保留。隔离续跑在同一构建指纹下重新校验了 113 个已成功的变体，只实际重跑其余 3 个。58 对、5,676 逻辑帧、22,704 条权威记录全部对齐。 [首次失败](../local-mugen/stage29-delivery-frozen-a.stderr.log) · [第二次失败](../local-mugen/stage29-delivery-frozen-a2.stderr.log) · [成功续跑](../local-mugen/stage29-delivery-frozen-a3.log)
- 阶段 19 的 `guard-20/plain` 和阶段 26 的 `near-frozen-direction-split-button-neutral-first/plain` 也遇到 Mono 退出异常。续跑保留原进程的失败输出，两场重新执行后完成各阶段权威匹配。 [阶段 19 失败](../local-mugen/stage29-delivery-frozen-a4.stderr.log) · [阶段 19 续跑](../local-mugen/stage29-delivery-frozen-a5.log) · [阶段 26 失败](../local-mugen/stage29-delivery-frozen-d.stderr.log) · [阶段 26 续跑](../local-mugen/stage29-delivery-frozen-d2.log)

## 兼容范围

原版扣血提交相位、共享与分席 RNG、定点和镜头边界按既有项目规则记录。250→300→Wave 的三次格挡位移存在可累计的定点误差。验证逐段比较位移增量，单段保持原阈值，并保留最终绝对位置差异。空中持后样本的起跳位移不同，两侧均在 F28 命中，F42 均从各自命中原点位移 (+4.5, +6.5)。地面反击后直取消的追击位于原版 28 个有效空中受击 tick 的期限内。从更高空中位置命中后超过该期限的通用追击有待实现。跑步入口、MAX 与崩防脚本、310、800／805、720、1050 本体和通用空战仍延期。强 Wave 与其他可读取 `var(1)` 的特殊技留在原版适用范围记录中。

双席对打中，原版镜头对称钳制在 ±145，NewKOF 反弹终点为 ±169.125。对 5110 的混合双键样本，两边均在 F84 选择 250，F107 进入攻击并空挥。后续起身后的 F114／F117 步进，原版把 +8／+5 全给施攻者，NewKOF 在双方均为地面时按现有推挤求解分摊。两套位置轨迹都单独核对。 [对打与推挤证据](../tools/Stage29Validation/EVIDENCE.md)
