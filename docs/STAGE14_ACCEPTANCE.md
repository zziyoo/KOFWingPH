# 阶段 14 验收 · Terry State 200

验收范围为普通模式下的 Terry 单招式实验 1v1。双方可显式选择 `TerryS200` 规则及独立 `terry98-stage14` 视觉。阶段 13C 的 Terry 覆盖仍可用于 Kai/Rei。源码、采样相位、运行配置及轨迹 SHA 见 [原引擎基准](STAGE14_REFERENCE.md)，可执行命令见 [取证说明](../tools/Stage14Validation/README.md)。

| 工作单元 | 状态 | 核对结果 |
| --- | --- | --- |
| A 原引擎 | PASS | IKEMEN GO `5dac5caf…` 原 Terry 对局 9 个规范场景，另有持续蹲防补充回放。 |
| B 数据契约 | PASS | 原归档、AIR、真实 Manifest 和 9 份原引擎轨迹交叉验证，角色、绑定、来源输出重复生成字节一致。 |
| C 权威执行 | PASS | 独立席位配置经真实指令和结算路径执行。覆盖打断、非法输入、判定边界、一次结算和独立停顿。 |
| D Godot 对局 | PASS | Godot 4.7.2 Mono、Vulkan 1.4.341 Forward+。固定 60 FPS 八项、实时 30/60 FPS 采样及 Vulkan 像素夹具通过。 |
| E 回归 | PASS | C# 专项 92/92、全量 1229/1229。Python 导入器全量 44/44。Debug 0 警告、0 错误。13C 的 323 帧与 69 条取消日志逐条一致。 |
| F 缺陷修复 | PASS | 独立停顿、边接触、接触 tick 蹲防、比例击退和非法受击框坐标有针对性测试。 |
| G 交付 | PASS | 参考、数据、运行时分别提交 checkpoint。验收脚本与证据索引随阶段收口提交。远端同步以 `git ls-remote origin refs/heads/main` 核对。 |
| H 后续准备 | PASS | 最终构建 5 项无效 CLI 诊断及一次真实 Vulkan 复查通过。State 210/211 静态审查见原引擎基准。 |

## 原版规则对照

- `Terry.cmd` 物理 `x` 对应项目 `5A`，F5 进入 State 200。IKEMEN 碰撞采样的起手、生效、恢复各为 3/4/5 tick。NewKOF tick 0–2 / 3–6 / 7–11 分别对应三个阶段，空挥 tick 12 恢复，有接触 tick 23 恢复。
- AIR 第 2 元素 Clsn1 朝右、Y 向上为 X `[4,59]`、Y `[73,88]`。两席镜像的框逐 tick 对齐。原版 78 边接触命中，79、80 空挥。NewKOF Vulkan 实测 78、80，内核另断言精确 79。边接触仅对显式配置的近身攻击启用。
- IKEMEN F8 碰撞，F9 目标生命 1000→954。HitDef 标称 57，普通模式补正后实扣 46。NewKOF 在 tick 3 原子结算 46。攻守后续停顿 11/13，目标有效硬直 11 tick，归一化 tick 27 可控。
- 站防及接触 tick 才按下后的蹲防均零削血，攻守停顿 11/11，目标有效硬直 10 tick，归一化 tick 24 可控。NewKOF 显示 Action 150/151。提前下后时受击框低于拳框，两引擎均落空。
- 停顿结束后首次推退 6 单位，站姿每 tick 保留 .85 的速度，命中最后移动步乘 .6。蹲防按下时保留 .82，松开后回 .85。两引擎逐 tick 的接触后位移误差 ≤0.002 世界单位。镜像使用相反方向。
- Action 200 素材顺序为 165/166/165。原版帧末动画索引和碰撞用素材有采样相位差。NewKOF 用 `MoveElapsedFrames` 跨三个阶段采样，固定帧证据覆盖 tick 0–11、停顿及返回 Action 0。

## 证据与回归

- `local-mugen/stage14-validation/fixed-v3/` 保存八场各两种视觉运行、336 张 FramePostDraw 图、输入参数、日志和逐帧对照。159 个归一化状态通过原引擎对照。覆盖开关的 640 个权威逻辑帧和 2560 条角色、实体、招式日志一致。
- `fixed-v3/overview.png` 是 Vulkan 图像总览。各场 `sprite/frames/` 下 `render-00029-state-00020.png` 为入招，`render-00032-state-00023.png` 为首次生效，可见左右命中闪白、站防及蹲防姿态和蓝色防御效果。`hit` 的 `render-00044-state-00035.png` 对应攻击方先恢复，`render-00046-state-00037.png` 对应目标首次推退。攻击方接触后在 `render-00052-state-00043.png` 回待机，空挥在 `render-00041-state-00032.png` 回待机。
- FramePostDraw 的 `state` 为提交后状态版本，等于产生 `SpriteFrameAudit` 的 Step 加一。工具检查同一状态的 `SpriteRenderAudit` 确认选中贴图和素材。`MoveAudit` 的框来自结算后快照，接触时的框由原引擎碰撞采样和内核测试核查。
- `real30-v3` 命中、`real60-v3` 站防取得停顿、恢复、待机图。全审计运行出现调度积压。最终构建另以 800×450、角色与精灵审计、六张读回重测，见 `live-light/{hit,guard}`。两次均无积压告警，通常每次绘制间隔分别为 2、1 个提交状态。
- `frozen-final/` 以封版参数重跑投技、道具对撞、3v3 道具、道具打断及取消，共十次 Vulkan 对局。权威日志与归档及覆盖开关逐条一致。`gpu-pixels-final.log` 有 `MUGEN_GPU_PIXELS_PASS`。最终构建五项 CLI 负例见 `h1-negative/`，同构建的 24 tick Vulkan 命中对照见 `h1-positive/`。
- C# 新增 25 项，专项 92/92、全量 1229/1229。Python 转换器新增 14 项，导入器全量 44/44。以上无失败和跳过。Godot Debug 零警告、零错误。测试项目编译仍有原有 `ThrowTests.cs` CS8600 与 `CombatEventTests.cs` xUnit2029。

## 范围与差异

- IKEMEN 接触后的目标反应 tick 扣血，NewKOF 在接触 tick 原子结算。站防接触前，原引擎从 +30 后退至 +40.095127，NewKOF 按现有步行至 +42.671875。接触 tick、实扣伤害、后续停顿、恢复和接触后位移各自对照。
- 原版公共受击与防御子状态有独立的素材切换。实验用现有受击和防御投影显示关键姿态。完整反应动画、动作 Clsn2 表、State 205 近身优先、同招重入、取消、AI、反制、空中及其他模式延后。46 点伤害适用于实测普通模式首个有效 tick，空挥后段的脚本变量会变化。
- 第三方素材与运行证据留在本地忽略目录。Terry 原有缺图和透明引用诊断保留。`stage-13c` 指向 `dbf000e58971d30bf4f9cf32d71f1a0c2168786a`，现有 `project.godot` 工作区差异 SHA-256 仍为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。
