# 阶段 30 Terry State 310 验收

阶段 29 基线为 `04a83d642f6d7b0b671ac52005b538b356461fb9`。开工时本地 `main`、`origin/main`、GitHub `main` 一致，工作区干净。环境为 Godot 4.7.2 Mono Forward+ Vulkan、.NET SDK 10.0.403 和 Python 3.11.9。 [原始定义、键位和轨迹](STAGE30_REFERENCE.md) · [复现入口](../tools/Stage30Validation/README.md)

阶段 30 正式 PASS，30A–30G 共 7/7。

## 验收矩阵

- **30A 原版依据 · PASS。** 174/174 个签名场景、28/28 个重叠的只读命令探针和 29/29 个补充对照通过。配置根据该输入集核对 Action 310、两模式、九个入边、接触后许可、自然恢复、根指令和 Helper 41。 [原引擎取证](../tools/Stage30Reference/README.md) · [最终核验日志](../local-mugen/stage30-delivery-r3-native.log)
- **30B 本体 · PASS。** 310 是全程站地的 3／6／24 tick 招式。两个 AIR 攻击区域共用一个接触组。站姿及蹲姿地面命中走对应重击反应，站防与蹲防允许，空中持后样本进入击飞反应。出招时按 StateDef 的 `facep2=1` 对准对手。固定图形对照覆盖两席、镜像、对打、投射物打断和两模式。 [最终地面](../local-mugen/stage30-delivery-r3-body.log) · [最终对空](../local-mugen/stage30-delivery-r3-air.log) · [最终模式 2](../local-mugen/stage30-delivery-r3-mode2.log) · [完整证据](../tools/Stage30Validation/EVIDENCE.md)
- **30C 输入 · PASS。** 真实 `RisingUpper` 替换新配置中的 `ReservedRisingUpper`，沿用识别期方向门、消费期根与 Helper 的 OR 条件及一次消费。默认弱 Rising Tackle 的优先候选只保留输入资格，模式 2 保持隔离。F102／F103／F104 的最终构建 Pause 对照 3/3，共 5,301 条权威记录与 171 张实际提交图像。 [方向与按键](../local-mugen/stage30-delivery-r2-input.log) · [根和 Helper](../local-mugen/stage30-delivery-r2-buffer.log) · [最终 Pause 对照](../local-mugen/stage30-delivery-r3-pause.log)
- **30D 入边与实例 · PASS。** 200、205、215、235、245、250、400、410、440 经 `var(1)=1` 的各自窗口进入 310。成功时新动作从 tick 0 开始，旧接触确认和旧判定框清除。第二候选的 16 对固定 Vulkan 包含九个来源、250 的窗口边界、430 提前拒绝与 230／240 自然恢复，共 17,422 条视觉开关权威记录一致。交付构建复验 200／205／235／245 入边和 250 的冻结消费、禁追及反击。 [入边窗口](STAGE30_REFERENCE.md#地面入口与权限) · [完整入边批次](../local-mugen/stage30-delivery-r2-routes.log) · [交付构建](../local-mugen/stage30-delivery-r3-routes.log)
- **30E 出边和追击 · PASS。** `maximumContactAge=1` 以动作有效时钟限制命中或格挡后的特殊技窗口。默认复用 1000／1020，模式 2 只保留 1050。旧 tick 33 的自然许可另行核对。250 普通击飞后接 310 无第二次接触，反击后在 F43 追加。250 格挡接 310 并在旧 tick 4 接 Wave 的续防成立。 [延后接触](../local-mugen/stage30-delivery-r3-late-wave.log) · [追击和续防](../local-mugen/stage30-delivery-r3-pursuit.log)
- **30F 确定性和画面 · PASS。** 交付构建固定 Vulkan 29 对、3,985 个逻辑帧、36,956 条视觉开关权威记录一致、1,481 张实际提交图像。独立同种子复跑 1,220 条权威记录逐条匹配。实时 30 FPS 档实测 30／30／30，60 FPS 档实测 59／60／60，两档均无逻辑帧积压。第二候选已通过的 75 对对照单独归档，交付构建针对受影响路径和关键代表场景复验。 [固定帧统计](../local-mugen/stage30-delivery-vulkan-summary.log) · [实际图像](../tools/Stage30Validation/EVIDENCE.md)
- **30G 共存与交付 · PASS。** 13C Vulkan、阶段 14–26 的 542 对最终构建冻结回放、27／28／29 定向门禁、八个既有普通技 Wave 来源和配置生成哈希全部通过。BattleCore、Godot Debug 和测试工程分开构建。 [历史逐阶段回放](#阶段-1426-冻结回放) · [构建与共存结果](#构建自动化与试跑)

## 构建、自动化与试跑

- 最终 Debug 候选 `NewKOF.dll` SHA-256 为 `731bdaf3f1d9447407990fbe0019d4e2b2cc1f924c871c9dd595bf132733ddae`，`NewKOF.BattleCore.dll` 为 `1b94c655d5cddeda64e6fe42e7188673cd9ea52256d0ccfbb6ae79a327a947ad`。BattleCore 与 Godot Debug 各为零警告零错误。测试工程为零错误，保留既有 `CS8600`（`ThrowTests.cs:284`）与 `xUnit2029`（`CombatEventTests.cs:197`）。 [BattleCore](../local-mugen/stage30-delivery-r3-core-build.log) · [Godot](../local-mugen/stage30-delivery-r3-godot-build.log) · [测试工程](../local-mugen/stage30-delivery-r3-tests-build.log)
- 最终 C# 专项 346/346，全量 1,575/1,575，零失败零跳过。新增测试验证接触年龄、攻守两个结果、冻结、保留目标、对空击飞、朝向、自然恢复、重放和视觉选帧。 [专项日志](../local-mugen/stage30-delivery-r3-csharp-focused.log) · [全量日志](../local-mugen/stage30-delivery-r3-csharp-full.log)
- 最终 Python 专项 7/7、全量 138/138，零失败零跳过。两次生成的六份 JSON 逐字节一致。阶段 29 的六份父配置分别命中固定 SHA-256。 [专项](../local-mugen/stage30-delivery-r3-python-focused.log) · [全量](../local-mugen/stage30-delivery-r3-python-full.log) · [哈希](../local-mugen/stage30-importer-determinism-r3.log)
- 最终构建的 13C Vulkan 复验及像素夹具通过。投技 80 帧、道具抵消 68 帧、道具组队 105 帧、道具打断 70 帧和取消 69 帧，合计 323 帧角色／实体和 69 帧取消记录逐条匹配。 [13C 复验](../local-mugen/stage30-delivery-r3-frozen-13c.log) · [像素夹具](../local-mugen/stage30-delivery-r3-pixels.log)
- 阶段 29 冻结配置的七组定向对照在交付构建上重放，阶段 30 配置以相同物理输入在 Vulkan 重放，9,473 条权威记录匹配。普通 250 击飞保持禁追，反击样本 F60 仍由 Wave 追加，250→300 的两段续防接 Wave、默认直接 Wave 与模式 2 保留许可均通过。 [阶段 29 定向门禁](../local-mugen/stage30-delivery-r3-stage29.log)
- 七个既有弱 Wave 来源用阶段 30 配置重放 13 对、1,965 逻辑帧，19,076 条共享权威记录匹配，当前视觉开关记录 19,218 条一致。`launchAir` 的 Clsn2 形状继续单独对照原版 AIR。 [旧 Wave 共存复验](../local-mugen/stage30-delivery-r3-old-wave.log)
- 第八个来源 245 在旧 tick 4 和 8 各以原版扩展轨迹核对，均在 Vulkan 中发动一次 Wave，每对 2,751 条权威记录一致。805 的不可投与保留候选两对共 3,702 条记录，模式 2 Action 241 的站防与蹲防两对共 2,483 条记录。 [245](../local-mugen/stage30-delivery-r3-legacy245.log) · [805](../local-mugen/stage30-delivery-r3-legacy805.log) · [Action 241](../local-mugen/stage30-delivery-r3-legacy241.log)
- 交付构建加载阶段 30 配置时，440 的 F56 自然步行与 F95 选择 205 保持，2,574 条开关视觉记录一致。阶段 28 六组 300 定向控制的 2,412 条记录逐条匹配。 [440 自然步行](../local-mugen/stage30-delivery-r3-legacy440.log) · [阶段 28 定向门禁](../local-mugen/stage30-delivery-r3-stage28.log)
- 首次空中对照在 F41 发现 5050 提前一帧使用下一张图。310 的受击绑定启用 `zeroBasedLaunchClock`，按实际提交的反应时钟取图。复验 1,318 条视觉开关记录一致、90 张实际提交图像。早期验证适配器还因反应区间缺少 `begin` 报错，修复后保留同构建复验结果。 [初次对照](../local-mugen/stage30-pilot-body.log) · [适配器问题](../local-mugen/stage30-pilot-air-clock.log) · [复验](../local-mugen/stage30-pilot-air-clock-r2.log)
- 持 DF+y 到自然退出的原始 Vulkan 对照在 F38 提交 Crouching／Action 11，原版同帧为 Standing／Action 0。增加单独延后被动蹲姿的配置后，六个恢复样本全部匹配，共 5,797 条权威记录、408 张实际提交图像。旧构建的历史回放已单独归档并停止，统计仅计最终构建。 [恢复和输入](../local-mugen/stage30-delivery-r2-recovery.log) · [复现测试](../local-mugen/stage30-recovery-projectile-red.log)
- Pause 首轮选择的原版轨迹只到 F105，三场不足以验证既有 Wave 的完整受击恢复。改用同签名长轨迹后，F104 新输入场在 F108 暴露受击恢复提前。原版 State 10 的临时时钟归零会请求 F105 暂停。最终配置用 `resetPauseClockOnCrouchEntry` 捕获这一差异，三场重新通过。保留两个早期构建日志，未计入最终历史总数。 [短轨迹](../local-mugen/stage30-delivery-r2-buffer.log) · [时序失败](../local-mugen/stage30-delivery-r2-pause.log) · [最终复验](../local-mugen/stage30-delivery-r3-pause.log)

## 阶段 14–26 冻结回放

使用同一交付构建，以 `--headless-authority` 重放各封存配置和物理输入，逐条与各阶段原权威记录比较。图形门禁的 Vulkan 日志单列。发生 Mono 退出异常时，已有工具将首次日志保存为对应回放目录下的 `duel-first-shutdown.log`，只重试该变体。

13 个阶段均出现 `Matched Stage`，合计 **542 对、65,069 逻辑帧、538,586 条权威记录**。以下各项依次为对局、逻辑帧、权威记录。

- 阶段 14 为 8／640／2,560，阶段 15 为 28／2,710／10,840，阶段 20 为 41／4,623／43,202。 [回放日志 A](../local-mugen/stage30-delivery-frozen-r3-a.log)
- 阶段 16 为 58／5,676／22,704。 [回放日志 B](../local-mugen/stage30-delivery-frozen-r3-b.log)
- 阶段 17 为 45／4,247／25,482，阶段 19 为 10／1,207／11,812。 [回放日志 C](../local-mugen/stage30-delivery-frozen-r3-c.log)
- 阶段 18 为 53／4,972／39,776。 [回放日志 D](../local-mugen/stage30-delivery-frozen-r3-d.log)
- 阶段 21 为 47／6,062／56,315，阶段 22 为 29／5,249／48,260。 [回放日志 E](../local-mugen/stage30-delivery-frozen-r3-e.log)
- 阶段 23 为 47／7,898／73,151。 [回放日志 F](../local-mugen/stage30-delivery-frozen-r3-f.log)
- 阶段 24 为 63／8,192／79,058。 [回放日志 G](../local-mugen/stage30-delivery-frozen-r3-g.log)
- 阶段 25 为 58／6,116／56,271。 [回放日志 H](../local-mugen/stage30-delivery-frozen-r3-h.log)
- 阶段 26 为 55／7,477／69,155。 [回放日志 I](../local-mugen/stage30-delivery-frozen-r3-i.log)

四个无视觉覆盖变体首次发生 Mono 退出异常，分别是阶段 16 的 `mode2-sequence`、20 的 `fixed-final/whiff-19`、25 的 `falling/airborne-fall-locked`、26 的 `final-ordinary/far-no-chain-13`。四次自动重试均匹配，当次日志保留。最终构建未从任何早期构建沿用回放结果。

## 兼容范围

原版扣血发生在接触后的受击更新，NewKOF 在接触 tick 原子扣血。伤害独立按各自的 RNG、反击上下文、进攻倍率和目标防御倍率复算。250 反击后重新接触的原版样本以目标保留的防御倍率计算，不能直接把 HitDef 伤害乘 0.8 当成实扣。

独立对空样本在两引擎均于 F15 接触。启动飞行至倒落的速度与有效相位一致。先前起跳的接触高度为原版 39.600、NewKOF 36.240，落地进入撞击分别在 F60、F59，控制恢复分别在 F114、F113。采集分别核对各自轨迹、触地结果和实际提交图像。

`310-wave-205-air-35` 是原版较高空中目标下 205 空挥后接 310 的证据。同一物理输入在 NewKOF 的较低起跳轨迹下，205 于 F16 先命中并改变停顿，随后的 310 输入已过期。第二候选加载阶段 29 冻结预设时，同一物理输入也于 F16 发生该次 205 命中。该复合空战场景作为已知兼容限制，不计入通过的对照。310 的延后接触取消另有地面动态样本，两引擎均逐帧对照。 [原版输入](STAGE30_REFERENCE.md#接触许可与自然恢复的对照) · [差异日志](../local-mugen/stage30-final-wave.log) · [冻结预设对照](../local-mugen/stage30-legacy205-controlled.log)

`project.godot` SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。原版初态记录及本机 GPU 图像位于忽略目录，复现命令和签名校验器纳入仓库。800／805、720、1050、1200 本体、通用空战和其他必杀技留在已记录的兼容边界。
