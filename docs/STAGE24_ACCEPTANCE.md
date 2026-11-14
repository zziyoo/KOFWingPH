# 阶段 24 State 440 与弱 Power Wave

阶段 23 基线 `88316a7520bb3448f81102473f59a677fb920e9a` 开工时与 GitHub main 一致，工作区干净。`project.godot` SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。

**正式验收 PASS，7/7。** State 440 本体、默认模式取消与其后继行为均通过。封版使用本文件所在的 `Complete Stage 24 Terry sweep and Power Wave acceptance` 提交。源码、运行时、图形分别由 `d903771`、`501eaec`、`bd89e81`、`cd58067`、`bc77c0d`、`255c482`、`9c33a65` 保存。HTTPS 超时后使用同一 GitHub 仓库的 SSH 正常快进推送。

## 验收矩阵

- **24A 原版取证 · PASS。** 固定版本 IKEMEN GO、种子 1337，63 条原始轨迹通过结构化检查。包含独立本体、两种方向镜像、合法与非法防御、三类取消、候选优先级、受身边界、同帧抑制及双实体。 [原版参考](STAGE24_REFERENCE.md)
- **24B 本体 · PASS。** `Down+b` 对应现有 HeavyKick，P1 `I`，P2 `Semicolon`／`Kp5`。Action 440 单段下段 `C,NA`，`guardflag=L`，`hitflag=MAF`。10／8／22 tick，命中停顿 11／13、格挡 11／11。站防被扫倒，蹲防零削血。目标的震停、击飞、撞地、反弹、倒地、起身与地面受身使用权威时钟。标准 F15 命中，F29 飞出，F60 撞地，F64 反弹，F74 倒地，F87 起身，F112 恢复控制。接地受身使 F60 进入 5201／Action 5200、F88 恢复。验证使用独立本体配置。
- **24C 取消与 CMD · PASS。** 显式旧 tick 11–17 许可命中、格挡与空挥。旧 tick 7 短按过期，8–10 先缓冲至 11，18 拒绝并无轻拳补发。冻结中的短按和持续按压均在攻击方解冻且窗开放时提交。旧 tick 40 自然恢复可读取新命令。原版下＋`a+b` 先匹配 430，下＋`x+b` 进入 400，下＋`y+b` 进入 410，前下＋`y+b` 先匹配 310。旧 tick 39 的 `a` 经 Helper 保留，在旧 tick 40 当前方向仍为下时优先于新 `b`。`a` 与 `b` 保持独立。窗口内有效 `236+x` 优先于同帧 `b` 或 `y+b`。310、430、245 是占位，模式 2 原版 1050 未接入。
- **24D 后继接触 · PASS。** 首末窗口扫击命中取消均保留已提交伤害和目标反应。弱 Wave 同一施放者的追击限制在接地提交后的下一 tick 清除，Helper 仍按本地时钟推进。在蹲防样本 Wave 合法削血 9，空挥样本有晚段地面命中。`edge-0` 和 `edge-16` 对手输入相同，F16 的未提交接触只在取消样本中消失，F34 改由早段 Wave 命中。`incoming-wave-16` 的已提交 1000 被对方现存实体打断，己方未生成 Helper。`incoming-wave-22` 验证两枚实体同场存续。
- **24E 确定性 · PASS。** C# 快照从首接触前、独立 HitPause、提交前、落地和恢复边界复跑，比较角色、实体、接触、事件、ID 和全局 Pause。视觉开关、同种子对照与坐标边界见下方图形证据。
- **24F 图形与共存 · PASS。** 固定帧按原始物理输入驱动，验证逻辑 L=F+14 与实际绘制版本 S=L+1。实时复查使用稀疏采样。完整默认配置包含 200、205、215、400、410、440 六来源。模式 2 的共同本体和 1050 输入隔离单独运行。
- **24G 交付 · PASS。** 核心、Godot 和测试工程分开构建。原引擎、专项、全量、Vulkan 与 13C–23 冻结门禁逐项通过。

## 精度与兼容边界

- 伤害仍在 NewKOF 接触 tick 原子结算，原版于目标反应 tick 提交。规范扫倒的阶段、位移和独立停顿按原帧对照。
- 标准无受身的扫倒轨迹 X/Y 完全对齐。受身的 .85 Q16 衰减产生最大 X 误差 0.00266928。第一次 .82 蹲防滑动在 Wave 接触点 X 误差 0.00274134，两次滑动后误差 0.00549268。按各自衰减方程复算，接触帧、格挡与恢复一致。受身 `x+a` 需要两键同帧新按。扫倒反弹期间原版目标属于 L，即使第二次扫击的框重叠也无合法接触。
- `incoming-wave-16`／`incoming-wave-26` 的原版镜头边缘限制与此前舞台差异相同。分别从 F91／F88 开始出现 X 差，反弹落地时原版 −218、NewKOF −229.125，最大差 11.125。两者的 Y、速度、反应边界和 F134／F131 恢复一致，差值未改变后续接触。使用独立轨迹断言保留这一差值。
- 共享原版 RNG 与实验配置的分席 RNG、扣血相位及阶段 18–23 已知兼容差异继续分别保留。State 310／430／245／1050 和通用空战属于后续范围。`Up+b` 和空中 `Down+b` 只核对不会误进 440。State 430 占位验证地面可行动时的优先级，其原版专用衔接留待该招式接入。440 本体的原版地面受身已验证。

## 构建、测试与证据

- BattleCore 与 Godot Debug 分别为 0 警告、0 错误。测试工程 2 警告、0 错误。两项均与基线相同。`ThrowTests.cs:284` CS8600 为可能为空的值赋给非空变量，`CombatEventTests.cs:197` xUnit2029 为用 `Assert.Empty` 检查筛选后的不存在条件。诊断规则保留。早期并发构建曾因运行中的 Godot 锁住 DLL 触发一次 MSB3026 重试，独立输出构建和最终正常输出构建均为 0 警告。
- C# 专项 192/192、全量 1,410/1,410，零跳过。补充测试覆盖不同方向与输入年龄的 CMD 占位、反弹期间倒地资格、拆开输入的受身拒绝、六来源执行器和快照复跑。
- Python 专项 9/9、全量 101/101，零跳过。本体与完整预设各六份文件分别独立生成并逐字节一致。完整预设的默认 fighter SHA-256 为 `1e3081169e9f23f30cffea62031b142a3b5089860befb0c419bd73446e7118c0`，模式 2 为 `ab57c1f1aae86f64f47dbe7797282fd79c061a3291596eb1faf411ac1a106b69`。 [配置复现](MUGEN_IMPORT.md#阶段-24-蹲重脚与弱-power-wave)
- 固定 Vulkan 63 组、126 次对局、8,192 逻辑帧、79,058 条视觉开关逐条一致的权威记录。保留 3,128 张 PNG，另一次同种子复跑比较 1,461 条权威记录并补采 63 张恢复边界图片。像素夹具输出 `MUGEN_GPU_PIXELS_PASS`。逐场文件在各 `sprite` 和 `plain` 目录，`sprite/trace.json` 保存对照和采集记录。 [Vulkan 回放](../tools/Stage24Validation/README.md) · [实际图像索引](../tools/Stage24Validation/EVIDENCE.md)
- Vulkan 实时 30／60 档均运行 440→1000 和真实 Wave 接触，单次 `FramePostDraw` 读回与提交版本对应。30 档 R293/S205 实测 30／26／30 FPS，60 档 R307/S126 实测 56／42／60 FPS，三值分别为读回前、读回附近、恢复后。捕获边界前无调度丢帧。后续 L211 与 L130 记录到读回开销引起的积压，完整时序证据使用固定模式。

```powershell
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
dotnet build NewKOF.csproj -c Debug --no-restore
Remove-Item Env:DefaultItemExcludes
dotnet build core/BattleCore/BattleCore.csproj -c Debug --no-restore -t:Rebuild
dotnet build tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-restore -t:Rebuild
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-build --no-restore
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
py -m unittest discover -s tools/MugenImporter/tests
```

Godot 命令通过进程环境排除本地的原引擎工作目录、生成资源和截图，防止 MSBuild 在大型本地数据树中误解析 `**/*.resx`。仓库构建配置与警告设置保持原样。

## 历史冻结回放

- 13C 逐条核对 323 个角色／实体逻辑帧和 69 个取消帧。14 核对 8 场、640 帧、2,560 条。15 核对 26 场 2,520 帧、10,080 条，另有两场小数距离共 190 帧、760 条。
- 16 为 58 场、5,676 帧、22,704 条。17 为 45 场、4,247 帧、25,482 条。18 为 53 场、4,972 帧、39,776 条。
- 19 为 10 场、1,207 帧、11,812 条。20 为 41 场、4,623 帧、43,202 条。旧目录的原始 19 归档缺一个入口，按阶段 23 已核实的完整冻结复跑归档重新对照。20–22 同样从已核实的阶段 23 归档选取，输入及权威日志逐条比较。 [冻结复现](../tools/Stage24Validation/README.md#阶段-1922-冻结门禁)
- 21 为 47 场、6,062 帧、56,315 条，两个视觉配置均逐条对齐。
- 22 为 29 场、5,249 帧、48,260 条，320×180 Vulkan 实际启动和双视觉配置均通过，权威记录与冻结归档逐条一致。
- 23 为 47 场、7,898 帧、73,151 条。部分归档的试跑配置有已退役字段，回放脚本核对配置 SHA 后创建兼容副本，只移除原输入从未触发的字段。另用最终运行时重新核对 310 优先级代表场景，79 帧／711 条精确对齐。 [兼容副本与复现](../tools/Stage24Validation/README.md#阶段-23-冻结门禁)

回放输出及汇总在 `D:/NewKOF-stage24-legacy`，阶段 23 完整回放另保存在 `D:/NewKOF-stage24-stage23-frozen-compatible`。正式黄金日志保留原样。

## 下一候选静态准备

State 430 的地面优先级已在 24A 实测。`Terry.cmd:1718–1731` 的下＋`a` 或对应 Helper 可在控制、跑步、自然恢复或指定普通技衔接条件中进入。`Terry-N.cns:1138–1228` 和 AIR 给出 3／3／8 tick，单段下段 `C,NA`，`hitflag=MA`，规范命中／格挡停顿分别 11／13 和 11／11。State −2 的显式弱 Wave 窗口表未列入 430。下一轮宜先取证普通技衔接、自然恢复和下＋`a+b`，并实测弱 Wave 的拒绝边界。阶段 24 未接入该招式。
