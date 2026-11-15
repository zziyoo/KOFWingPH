# 阶段 29 图形证据

## 交付候选定向回放

使用 Godot 4.7.2 Mono／Vulkan Forward+，Debug 指纹 `94a311587ac85b0e3a4182e7fd181a7000f5988f6b47593087671e6b2845f85d`（NewKOF）及 `cab11a293d189618b5ac5bb1be9c9436b03fba4fd6b4e172d28b770956f420fa`（BattleCore）。最终别名导入 16 张图集，两个 DLL 和 `project.godot` 前后 SHA 一致。 [图集导入日志](../../local-mugen/stage29-delivery-atlas-import.log)

最终配置的阶段 29 固定帧回放通过 44 对，其中一对为同种子独立复跑。共 6,244 个逻辑帧、58,580 条开关视觉的权威记录及 2,172 张提交图像。分组包括定向 5 对、输入与全局 Pause 12 对、模式 2 五对、单独本体 11 对、窗口与退出 10 对、独立复跑一对。最终 `250-hit` 同种子对齐 1,538 行。 [本体与恢复](../../local-mugen/stage29-delivery-body.log) · [首末窗口](../../local-mugen/stage29-delivery-routes.log) · [输入竞争](../../local-mugen/stage29-delivery-input.log) · [同种子复跑](../../local-mugen/stage29-delivery-repeat.log)

- State 250 的空挥至旧 tick 49 恢复。固定帧检验两种受击姿态、两种格挡、异侧镜像、被 215 抢先打断、接地受身、146 中／147 空的距离边界。双席互击 F28 都受击，首轮空中反弹在 F76 触发原版镜头限制，两席停在 −145/+145。NewKOF 继续用同样速度 −3.375/+3.375 积分，最终落在 −169.125/+169.125。逐帧动态与两种镜头限制分别校验，1,189 行权威记录一致。 [互击](../../local-mugen/stage29-delivery-trade.log) · [完整站姿命中](<D:/NewKOF-stage29-delivery-body/250-hit/sprite/trace.json>)

- 普通命中后 250→1000 仍创建 Helper 1020。F56–65 追击锁正确挡住接触，敌方继续原来反应，1,593 行开关视觉权威记录一致。反击场景在 F60／L74 真正命中空中目标，双方准确配对飞行道具接触与重新震停、首次升空，1,626 行一致。两个引擎分别按自己的 RNG 重算伤害。 [定向日志](../../local-mugen/stage29-delivery-directed.log) · [反击续追画面](<D:/NewKOF-stage29-delivery-directed/250-counter-hit-wave/sprite/frames/render-00084-state-00075.png>)
- 普通命中后 250→300 同样进入新攻击实例，原版和 NewKOF 的两段后续均因空中追击限制没有接触，1,117 行一致。 [命中转 300 轨迹](<D:/NewKOF-stage29-delivery-directed/250-hit-cancel-24/sprite/trace.json>)
- 防御链 F28、F51、F68、F105 四次接触逐帧一致，各为 250 格挡、300 第一段格挡、300 第二段格挡和 Wave 格挡。从同一 300 的第二段确认在 F80 进入 1000，1,504 行一致。前三段守势分别复算位移，既有定点容差内的最大相对误差为 0.002671、0.001039、0.003354。Wave 接触继承已测的累计位置差 −0.007063，维持原有逐段容差。 [链路日志](../../local-mugen/stage29-delivery-directed-recheck.log) · [Wave 格挡画面](<D:/NewKOF-stage29-delivery-directed-recheck/250-guard-300-wave/sprite/frames/render-00129-state-00120.png>)
- 跳起后持后样本在两个引擎均为 F28 命中，F42 第一次升空位移均为 X +4.5／Y +6.5。原版接触前 X/Y 为 50/22.32，NewKOF 为 39.5/20.640015，差异来自震停前的跳跃轨迹。本样本核对地面限定防御和从各自位置执行的首段升空，完整落地恢复用地面命中样本证明。1,541 行开关视觉权威记录一致。 [空中样本输出](<D:/NewKOF-stage29-delivery-directed-recheck/250-air-held-back/sprite/trace.json>) · [接触画面](<D:/NewKOF-stage29-delivery-directed-recheck/250-air-held-back/sprite/frames/render-00052-state-00043.png>)
- 全局 Pause 的 F103 采样在 F104／L118 只启动一次 250。首跑图像收尾不足以覆盖恢复末端，延长采样窗口后 1,775 行一致。 [延长复跑](../../local-mugen/stage29-delivery-pause.log) · [F104 提交画面](<D:/NewKOF-stage29-delivery-pause/250-cd-global-pause-103/sprite/frames/render-00128-state-00119.png>)
- 5110／5120 的混合 `F+y+b` 都优先进入 250，F84／F95 的命令选择与原版一致。后一对 1,943 行，前一对 1,878 行。前一对在无攻击框的 F114／F117 有碰撞推挤差异。原版攻击者单独前移 +8／+5，NewKOF 双方各前移 +4／+2.5，整体位置增量和动作时序不变，无额外接触。复验单场量化双方位置，保留首跑结果。 [F84 复跑](../../local-mugen/stage29-delivery-lying-cd.log) · [F95 轨迹](<D:/NewKOF-stage29-delivery-throw-competition/250-wakeup-cd/sprite/trace.json>)
- 模式 2 的合法 1050 候选分别在旧 24、49 消费。两场均无 State 1000 或 Helper 1020，窗口对照 1,044 行，自然退出对照 1,449 行一致。 [窗口](../../local-mugen/stage29-delivery-mode2-window.log) · [自然退出](../../local-mugen/stage29-delivery-mode2.log)
- 阶段 28 的六个固定输入定向控制在交付构建实际回放。2,412 条 `MoveAudit`、`VisualAudit`、`EntityAudit`、`SpriteFrameAudit` 与阶段 28 的封存日志一致。 [定向权威比较](../../local-mugen/stage29-stage28-runtime-compare.log)
- 阶段 29 新配置重放七个原直连 Wave 来源及原有蹲轻脚、轻脚衔接和扫倒控制，13 对、1,965 逻辑帧，19,076 条共享权威记录通过对照，19,218 条新配置权威记录在开关视觉间逐条相同。检测到的 728 条跨配置受击框差异（两种视觉版本合计）均逐条吻合原版初段空中反应 Clsn2 投影。其余字段按原顺序逐条相同，事件和相位均一致。 [共存回放](../../local-mugen/stage29-delivery-coexistence.log)
- 第八个既有来源 245 的旧 tick 4／8 也通过两对 Vulkan 回放，5,502 行一致。首跑选中只到早期落地的原版轨迹，改用阶段 27 已存档至 F260 的对应签名轨迹复验完整恢复。 [延长轨迹复跑](../../local-mugen/stage29-delivery-legacy245-extended.log)
- 最终构建的旧规则定向复跑保留 440 的 F56 步行及 F95→205（阶段 29 配置）、805 的 5110／5120 资格、模式 2 Action 241 的两种防御与退出。后一组使用阶段 27 冻结配置，四对合计 5,753 行视觉开关权威记录一致。另有 440 一对 2,574 行。 [440 恢复](../../local-mugen/stage29-delivery-legacy440.log) · [805 资格](../../local-mugen/stage29-delivery-legacy805.log) · [Action 241](../../local-mugen/stage29-delivery-legacy241.log)

## 实时采样与像素回归

- 30 FPS 档实测 30／30／30，R282／S179 显示 Action 250 的 tick 17（动作状态 S162–221）。60 FPS 档实测 60／60／60，R294／S103 显示 tick 20（S83–142）。每组只读回一张稀疏图像，两个日志均无逻辑帧积压。 [30 FPS 图像](<D:/NewKOF-stage29-delivery-realtime30/250-hit/sprite/frames/render-00282-state-00179.png>) · [60 FPS 图像](<D:/NewKOF-stage29-delivery-realtime60/250-hit/sprite/frames/render-00294-state-00103.png>)
- Vulkan Forward+ 的像素夹具返回 `MUGEN_GPU_PIXELS_PASS`。夹具原有的失效贴图及故障回退诊断继续输出。 [像素验证日志](../../local-mugen/stage29-delivery-pixels.log)

## 本体试跑与发现的差异

本节使用阶段 29 原版合同生成的 `terry98-stage29-body` 试跑配置。`candidate.json` 中记录了运行构建、角色配置、视觉绑定与 Manifest 的哈希。

- 首轮试跑发出了 Action 250 的逻辑提交记录，新增视觉别名尚未导入图集，Vulkan 实际走几何回退。补做 Godot 资产导入并核对构建 DLL 与 `project.godot` 哈希。命中样本改用签名碰撞后快照读取 State 250 HitDef，同版探针只为其他部分 State 输出单独 HitDef 行。 [首跑日志](../../local-mugen/stage29-vulkan-smoke.log) · [图集导入](../../local-mugen/stage29-atlas-import.log)
- 空挥与站姿命中各跑一对 Vulkan 开关视觉，分别有 1,039、1,538 行权威记录逐条一致。命中 F28／L42，目标 Lift F42，Rise F43，Fall F51，Impact F68，Bounce F72，LyingImpact F84，Lying F87，Wakeup F97，F122 恢复控制。配对的目标 X/Y 最大差均为 0。 [试跑结果](../../local-mugen/stage29-vulkan-smoke-green.log) · [F28 提交画面](<D:/NewKOF-stage29-smoke-green/250-hit/sprite/frames/render-00052-state-00043.png>) · [F42 提交画面](<D:/NewKOF-stage29-smoke-green/250-hit/sprite/frames/render-00066-state-00057.png>)
- `250-guard-300-wave` 的原版三次近战接触均为格挡。试跑在 F51／L65 和 F68／L82 将两次 300 接触结算为命中，暴露继承配置的防御资格问题。 [失败原始日志](<D:/NewKOF-stage29-chain-probe/250-guard-300-wave/sprite/duel.log>)
- `250-hit-wave-24` 原版普通命中后，F56–65 的 Wave Helper 因追击限制未开启 HitDef。试跑 F56／L70 错误产生飞行道具命中，额外世界暂停改变了 Helper 后续年龄。 [失败原始日志](<D:/NewKOF-stage29-chain-probe/250-hit-wave-24/sprite/duel.log>)

## 历史定向试跑

- 同轮候选运行时复跑 805 的 5110／5120 边界，2/2 对，3,270 条视觉开关权威记录一致。 [日志](../../local-mugen/stage29-baseline-wakeup.log)
- 同轮候选运行时复跑模式 2 的 Action 241 站防及蹲防，2/2 对，2,483 条视觉开关权威记录一致。 [日志](../../local-mugen/stage29-baseline-mode2.log)
- 阶段 29 试跑配置重放 440 的原始物理输入，F56 恢复步行与 F95 选 205，随后 Wave 原样通过，1/1 对，2,574 行视觉开关权威记录一致。 [日志](../../local-mugen/stage29-baseline-440.log)
