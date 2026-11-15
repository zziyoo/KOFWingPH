# 阶段 30 Vulkan 证据

## 本体试跑

本体候选使用 Godot 4.7.2 Mono Forward+ Vulkan。试跑前对双模式暂存并导入 16 张图集。两份 Debug DLL 的 SHA-256 为 `831ffb3ee76203ff9029f02b3d38ca9d75e097507f11974ee76f463b19af7aa8` 与 `fcdc42d2cff8c43e3939f70483e76fc81f520e6d8e958f8fd307d414e8b7a243`。 [导入日志](../../local-mugen/stage30-pilot-import.log) · [候选身份](<D:/NewKOF-stage30-pilot-body/candidate.json>)

- 站姿命中、空挥、站防、蹲防 4 对通过，开关视觉逐条一致 4,880 行，实际提交图像 172 张。距离 70 站姿命中在原版 F8、NewKOF L22 接触，本种子实扣 92，目标 F40 恢复，施攻者 F49 自然退出。 [固定回放](../../local-mugen/stage30-pilot-body.log) · [命中轨迹](<D:/NewKOF-stage30-pilot-body/310-hit/sprite/trace.json>) · [接触画面](<D:/NewKOF-stage30-pilot-body/310-hit/sprite/frames/render-00032-state-00023.png>)
- 空中持后样本在 F15／L29 命中，反应与首次升空计算匹配，原始试跑在 F41 检测到 Action 5050 的 Defender 图像早一 tick。F38 已进入下降阶段，原版从 AIR 元素 tick 0 呈现，候选用积分后的阶段计数 1 采样。首次失败原始日志和实际图像保留。 [首跑错误](../../local-mugen/stage30-pilot-body.log) · [空中原始对局](<D:/NewKOF-stage30-pilot-body/310-air/sprite/duel.log>)
- 空中样本在接触前已有既知跳跃轨迹差异。两边的首次升空、上升、下降均发生在 F29、F30、F38。同一接触 F15 时原版 Y 为 39.600002，NewKOF 为 36.240051，按各自接触位置和同一积分规则到达地板，因而首次着地反应分别在 F60、F59。其后各自经过相同的接地、反弹、倒地和起身阶段，恢复帧分别是 F114 和 F113。 [原版轨迹](../../local-mugen/stage30-native-pilot/results/default-standing-heavy-kick-310-air.jsonl) · [NewKOF 权威和渲染日志](<D:/NewKOF-stage30-pilot-body/310-air/sprite/duel.log>)

## 空中反应图像修正

保留权威反应时钟，为 310 的受击视觉绑定从 0 读取每个反应阶段的 AIR 元素。修正构建为 `92513094a65d8bdd7d884197887f626eb58aca3813dc4913bd50189b5718561c` 与 `07992da553cdbd0043329b6a6a65baed5357affac85d82a66b59ec6d177cf415`。首次复跑的新增反应窗口元数据缺 `begin`，采集器拒绝汇总，原始对局日志保留。补齐窗口字段后同构建输出 90 张 Vulkan 提交画面、开关视觉逐条一致 1,318 行。共同升空阶段内逐帧核对 Action 5030、5035、5050 的实际 sprite，包含原故障 F41。两引擎按各自轨迹核对完整接地恢复。 [首跑采集器错误](../../local-mugen/stage30-pilot-air-clock.log) · [修正复跑](../../local-mugen/stage30-pilot-air-clock-r2.log) · [空中全过程](<D:/NewKOF-stage30-pilot-air-clock-r2/310-air/sprite/trace.json>) · [F41 修正画面](<D:/NewKOF-stage30-pilot-air-clock-r2/310-air/sprite/frames/render-00065-state-00056.png>)

## 首轮完整配置

两模式各导入 8 张图集，导入前后的两份 DLL 与 `project.godot` 哈希一致。完整配置的独立本体组通过 10 对固定回放，包含站姿／蹲姿命中和格挡、空挥、镜像、P2、互中、打断和空中持后命中。本组开关视觉逐条一致 12,307 行，记录了 477 张实际提交图像。 [图集导入](../../local-mugen/stage30-final-atlas-import.log) · [地面本体](../../local-mugen/stage30-final-body.log) · [空中完整轨迹](<D:/NewKOF-stage30-final-air/310-air/sprite/trace.json>)

- 同一候选的 13C Vulkan 门禁匹配 323 个角色／实体帧和 69 个取消帧。像素夹具输出 `MUGEN_GPU_PIXELS_PASS`，夹具原有故障回退诊断照常输出。 [13C](../../local-mugen/stage30-final-frozen-13c.log) · [像素](../../local-mugen/stage30-final-pixels.log)
- 阶段 27 的 805 保留资格两对及模式 2 Action 241 站／蹲防两对均通过。各组合计 3,702 和 2,483 行视觉开关权威记录。阶段 28 的六个定向控制对照已封存日志逐条匹配，共 2,412 行。 [805](../../local-mugen/stage30-final-legacy805.log) · [Action 241](../../local-mugen/stage30-final-legacy241.log) · [阶段 28](../../local-mugen/stage30-final-stage28.log)

- 首次 250→310→Wave 站防链的观察尾端不足以覆盖 Wave 自然退出。延长图像窗口后核对 F28、F43 和 F75 三次格挡，F40 进入 310，F55 进入 State 1000，三次 `MoveStarted` 与 1,522 条开关视觉记录一致。 [首轮](../../local-mugen/stage30-final-wave.log) · [延长固定回放](<D:/NewKOF-stage30-final-guard-chain/310-wave-250-guard-54/sprite/trace.json>)
- `310-wave-walking-hit-22` 在 F10 晚接触、F22 转 Wave、F46 飞行道具命中。F46 接触点仍含前一段地面反应的定点位移差 ΔX −0.003334545。F72 的累计差 −0.005205918 中，Wave 自身的相对差为 −0.001871373。复核以两次实际接触各自的位置为起点，逐段沿用 0.005 的门限。 [首轮超差记录](../../local-mugen/stage30-final-late-contact.log)
- `310-wave-205-air-35` 原版的 205 空挥，NewKOF 的 205 在 F16 先打中较低的跳跃目标。这早于比较的 310 取消窗。保留这一复合场景偏差，分别由地面的 205 入边、独立的 310 对空及 `310-wave-walking-hit-22` 的真实晚接触验证相应规则。 [原版复合场景](../../local-mugen/stage30-native-wave-edge/results/default-standing-heavy-kick-310-wave-205-air-35.jsonl) · [首轮差异](<D:/NewKOF-stage30-final-wave/310-wave-205-air-35/sprite/duel.log>)
- 在保持 DF+y 的空挥里，原版 F38（旧 tick 33）以 State/Action 0 中立恢复，F39 才转蹲。首轮候选在 F38 即进入 Crouching，Action 11。逐帧回放现要求自然退出先匹配原版状态及中立图片。 [原版保持输入](../../local-mugen/stage30-native-input/results/default-standing-heavy-kick-310-input-held.jsonl) · [候选首轮偏差](<D:/NewKOF-stage30-final-input/310-input-held/sprite/duel.log>)

## 第二候选（r2）

第二候选的 Debug DLL SHA-256 为 `203936b2b71b67e813e1978291bc3286e91377e48abd17587f469abb0dc1b24e` 与 `e48e151261796f8189e406d393c69392a4f44b6706874db31df038811cd19318`。再生成后两个绑定与已导入图集的版本一致。每组 `candidate.json` 核对 DLL、六份配置、Manifest 和项目哈希。

- 独立本体重新通过 10 对，开关视觉逐条一致 12,307 行，记录 477 张实际提交图像。空中受击的分阶段恢复与修正后的逐帧图像仍成立。 [地面本体](../../local-mugen/stage30-delivery-r2-body.log) · [对空与恢复](<D:/NewKOF-stage30-delivery-r2-air/310-air/sprite/trace.json>)
- 保持 DF+y、下、前或上的自然出口，及下＋x、下＋y 当帧的合法普通技，共 6 对通过，5,797 行一致，408 张图像。F38 的中立版本绘制 Action 0 且 X 保持，后续输入由下一帧控制或对应招式接管。 [自然恢复](../../local-mugen/stage30-delivery-r2-recovery.log) · [F38 提交画面](<D:/NewKOF-stage30-delivery-r2-recovery/310-input-held/sprite/frames/render-00062-state-00053.png>)
- 晚接触的 F22 Wave、F23 拒绝及 P2 飞行道具打断生效中的 310 共 3 对通过，3,575 行一致，264 张图像。F10 命中、F22 取消与 F46 Wave 命中的两段定点位移按各自接触点核对。 [晚接触](../../local-mugen/stage30-delivery-r2-late-wave.log) · [F46 Wave 提交画面](<D:/NewKOF-stage30-delivery-r2-late-wave/310-wave-walking-hit-22/sprite/frames/render-00070-state-00061.png>)
- 250 普通命中后，310 的有效框在旧锁下无后继命中。250 反击例外允许 310 于 F43 追加。站防对照的 F28／43／75 均格挡，F40／55 进入 310／1000。三对通过，4,663 行一致，270 张图像。 [禁追、反击与守势](../../local-mugen/stage30-delivery-r2-pursuit.log) · [F75 Wave 守势画面](<D:/NewKOF-stage30-delivery-r2-pursuit/310-wave-250-guard-54/sprite/frames/render-00099-state-00090.png>)
- 同一候选用阶段 29 封存配置复播 `310-wave-205-air-35` 的物理输入，L27/F13 进入 205，L30/F16 已多出一个 205 命中，实扣 38。碰撞时旧配置和首轮新配置的目标 Y 同为 52.320190，原版在 59.040005。本场差异在旧配置中出现，早于 310 候选消费。 [封存配置控制](../../local-mugen/stage30-legacy205-controlled.log)
- 全局 Pause 首跑选短命令探针，F105 截止早于前置 Wave 的恢复。改用 F120 的标准证据后，F102 自然进入和 F103 由 Helper 于 F104 消费的两对通过。F104 同帧物理 DF+y 在两引擎都启动 310，但原版在同帧 State −3 先记录 0→10，再经 State −1 从旧 tick 0 启动 310。State −2 的时间奇偶因此在下一 F105 请求 Pause。r2 仍读取帧开始站立 tick 1，漏掉 F105 Pause，受击方在 F108 提前恢复。 [短轨迹首跑](../../local-mugen/stage30-delivery-r2-buffer.log) · [F120 对照](../../local-mugen/stage30-delivery-r2-pause.log) · [F104 原版命令探针](../../local-mugen/stage30-native-audit-more/results/default-standing-heavy-kick-310-global-pause-104.jsonl)
- r2 的实时失败记录保留。30 FPS 请求档在采集附近跌至 9–11 FPS，发生帧积压，R282 晚于实际 310 动作。60 FPS 请求档在采集前、刚采集后及末次采样分别为 53、55、60。 [30 FPS 原始日志](<D:/NewKOF-stage30-delivery-r2-realtime30/310-hit/sprite/duel.log>) · [60 FPS 原始日志](<D:/NewKOF-stage30-delivery-r2-realtime60/310-hit/sprite/duel.log>)

r2 的上述对照合计成功 75 对固定回放，含一个独立同种子复跑。共 9,646 个逻辑帧、88,591 条开关视觉权威记录、3,177 张 Vulkan 实图。九个普通技入边、250 旧 20 过期与旧 21–23 保留、先按钮和先方向的两种冻结组合均由实际事件核对。这些是 r2 的独立证据。 [出入边](../../local-mugen/stage30-delivery-r2-routes.log) · [缓冲](../../local-mugen/stage30-delivery-r2-buffer.log) · [独立复跑](../../local-mugen/stage30-delivery-r2-repeat.log)

## 交付候选（r3）

Godot 4.7.2 Mono Forward+ 使用 Vulkan 1.4.341 与 NVIDIA GeForce RTX 3060 Laptop GPU。Debug DLL 的 SHA-256 为 `731bdaf3f1d9447407990fbe0019d4e2b2cc1f924c871c9dd595bf132733ddae` 与 `1b94c655d5cddeda64e6fe42e7188673cd9ea52256d0ccfbb6ae79a327a947ad`。配置更新仅涉及两份 fighter JSON，视觉绑定仍匹配已导入别名。

- 交付构建的阶段 30 固定矩阵为 **29 对、3,985 个逻辑帧、36,956 条开关视觉权威记录、1,481 张实际提交图像**。总数包含一次独立同种子复跑，另逐条对齐 1,220 行。汇总从每组候选哈希、两种视觉日志及实际图像路径复核。下列封存回归和两档实时证据单列统计。 [逐组汇总](../../local-mugen/stage30-delivery-vulkan-summary.log) · [同种子复跑](../../local-mugen/stage30-delivery-r3-repeat.log)
- 定向复验覆盖命中、地面防御、镜像互中、完整对空反应、持键自然恢复、DF+y+b 与近距投技竞争、250 冻结中分离输入，以及 200／205／235／245 的实际入边。 [本体](../../local-mugen/stage30-delivery-r3-body.log) · [对空](../../local-mugen/stage30-delivery-r3-air.log) · [恢复](../../local-mugen/stage30-delivery-r3-recovery.log) · [输入](../../local-mugen/stage30-delivery-r3-input.log) · [入边](../../local-mugen/stage30-delivery-r3-routes.log)
- 特殊技复验覆盖晚接触后 Wave、P2 飞行道具打断、250 禁追与反击例外、连续防御链、默认优先保留 1200、自然 Wave 与普通技提前拒绝。模式 2 在接触和自然帧只消费 1050，不创建默认 Wave。 [晚接触和实体打断](../../local-mugen/stage30-delivery-r3-late-wave.log) · [禁追](../../local-mugen/stage30-delivery-r3-pursuit.log) · [特殊技选择](../../local-mugen/stage30-delivery-r3-wave.log) · [模式 2](../../local-mugen/stage30-delivery-r3-mode2.log)
- 用 F120 长轨迹重查三个世界 Pause 控制。F104 根组合新增了原版规定的 F105 Pause，F103 的 Helper 保留到 F104 消费且 F105 不冻结，F102 的正常路径不变。3 对、567 个逻辑帧、5,301 条开关视觉权威记录、171 张提交图像全部通过。 [Pause 回放](../../local-mugen/stage30-delivery-r3-pause.log) · [F104 完整轨迹](<D:/NewKOF-stage30-delivery-r3-pause/310-global-pause-104/sprite/trace.json>)
- 串行恢复稳定节奏后，两档实时各读回一张提交图像。30 FPS 请求档实测 30／30／30，R276/S168 显示 310 生效 tick 3 与目标 Action 5002。60 FPS 请求档实测 59／60／60，R294/S103 显示 310 回收 tick 9 与目标 Action 5007。两次 `backlogAfterLogic` 均为空。 [30 FPS 图像](<D:/NewKOF-stage30-delivery-r3-realtime30/310-hit/sprite/frames/render-00276-state-00168.png>) · [60 FPS 图像](<D:/NewKOF-stage30-delivery-r3-realtime60/310-hit/sprite/frames/render-00294-state-00103.png>)
- 同构建再执行 13C 真实 Vulkan，323 个角色／实体帧与 69 个取消帧逐条一致。Vulkan 像素夹具输出 `MUGEN_GPU_PIXELS_PASS`。 [13C](../../local-mugen/stage30-delivery-r3-frozen-13c.log) · [像素夹具](../../local-mugen/stage30-delivery-r3-pixels.log)
- 阶段 29 七对控制用阶段 29 封存预设执行 headless 权威回放，再用阶段 30 当前预设重播相同物理按键。两个比较均通过，核对 9,473 条权威记录，覆盖普通禁追、反击 F60 Wave、转 300 后无追击和连续两段格挡、默认直接 Wave 及模式 2 保留。旧招式共存池通过 13 对、1,965 个逻辑帧、19,076 条跨预设共享行与 19,218 条当前开关视觉行。跨预设的初段飞行受击框按原版 Clsn2 投影单独核对。 [阶段 29 精确对照](../../local-mugen/stage30-delivery-r3-stage29.log) · [旧 Wave 共存](../../local-mugen/stage30-delivery-r3-old-wave.log)
- 当前预设单独验证 245→1000 旧 4／8 窗口，两对 5,502 行，与共存池合计覆盖八个既有普通技来源。440 的 F56 步行与 F95 选 205 路径通过，2,574 行。封存预设的 805 两对及模式 2 Action 241 两对各匹配 3,702、2,483 行，阶段 28 六个控制与旧档案逐条一致 2,412 行。 [245](../../local-mugen/stage30-delivery-r3-legacy245.log) · [440](../../local-mugen/stage30-delivery-r3-legacy440.log) · [805](../../local-mugen/stage30-delivery-r3-legacy805.log) · [Action 241](../../local-mugen/stage30-delivery-r3-legacy241.log) · [阶段 28](../../local-mugen/stage30-delivery-r3-stage28.log)
