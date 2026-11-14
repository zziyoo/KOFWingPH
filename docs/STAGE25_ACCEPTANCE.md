# 阶段 25 Terry State 430

基线为阶段 24 正式提交 `2212335ec07f00daf047de79834a304b8e934af0`。开工时本地、`origin/main` 和 GitHub main 一致，工作区干净。Godot 4.7.2 Mono、.NET SDK 10.0.403、Python 3.11.9 与本机原包可用。

**正式 PASS，7/7。** 本文随阶段 25 封版提交入库。Vulkan 证据、冻结运行与各自命令保留在下列索引中。

## 验收矩阵

- **25A 原版依据 · PASS。** 固定 IKEMEN GO、种子 1337。132 条主体轨迹、4 条连续格挡补充轨迹及 4 条末方向补充轨迹通过。State 430、Action 430、Helper 99999、State −2 和物理 CMD 源定义与实测边界一致。另有三条 Wave 补充轨迹延长至 F165。 [原引擎参考](STAGE25_REFERENCE.md)
- **25B 独立本体 · PASS。** 蹲轻脚使用下＋a，Action 430 为 3／3／8 tick。单段下段，攻击框 X0–63、Y1–16，普通／反击基数 7／8，动态伤害按实际补正结算。双席、镜像及两种模式的本体均核对。地面命中停顿 11／13，格挡 11／11，初始推力 6。站防尝试被命中，蹲防零削血。动作从 F5 开始，空挥 F19 恢复，有接触 F30 恢复。轻击反应按姿态选择 5010／5015 或 5020／5025。 [固定画面](../tools/Stage25Validation/EVIDENCE.md#本体与动作边界)
- **25C 输入竞争 · PASS。** 实测正下、前下、后下、缓冲消费时的方向和新边沿。下＋a+b 选 430，下＋a+x 选 400，下＋a+y 选 410，前下＋a+y 保留 310。440 旧 tick 39 的 a 在旧 tick 40 胜过新 b，直接进入真实 430。b 保留至独立缓冲过期，未产生补发。 [原版及日志](STAGE25_REFERENCE.md#实测规则与边界)
- **25D 普通技衔接 · PASS。** 200／205 的旧 tick 7–11、400 的旧 tick 6–12 可衔接 430。430 的旧 tick 6–13 可衔接 200／205／400 或重进 430。有效输入年龄 0–2，按消费时方向和身体距选招，允许命中、格挡或空挥。新实例独立结算。连续低段接触在原格挡硬直中保留防御，保持下方向可挡，松下改为站姿受击。230／235 和相关 235 来源延期。 [衔接与连挡轨迹](../tools/Stage25Validation/EVIDENCE.md#本体与动作边界)
- **25E Wave 边界 · PASS。** 430 没有直接 Wave 取消。起手、停顿和收招已核对。有效 x 可回落 200／205／400。继续按 x 可令 200 在自身旧 tick 4 使用 Helper 中的手势，松键样本提前过期。另有 430→400→1000 走 400 自己的许可窗口。自然恢复旧 tick 14 使用 `var(2)`，默认 1000，模式 2 的 1050 保留输入。16 tick 手势窗口内完成正前步骤后，前下＋x+a+b 仍由 Wave 优先。自然恢复后的新手势与仅前＋x 的 State 300 分别核验。 [固定时序](../tools/Stage25Validation/EVIDENCE.md#本体与动作边界)
- **25F 确定性与画面 · PASS。** 快照验证有效 tick、按键过期和实例身份。最终同种子复跑 904 条记录逐条一致。固定 Vulkan 58 对、6,116 逻辑帧、56,271 条视觉开关权威记录一致，留存 1,017 张逐帧图像及 116 张结束图，实际像素夹具通过。30／60 FPS 最终稀疏采样分别为 30／29／30 和 59／58／60 FPS，取样时没有调度积压。 [图形证据](../tools/Stage25Validation/EVIDENCE.md)
- **25G 共存与交付 · PASS。** 六个既有 Wave 来源、扫倒、接地受身、倒地目标和 Wave 追击限制通过。阶段 13C–24 冻结回放与封版逐条一致，三个工程构建通过，C# 和 Python 全量零失败、零跳过。 [冻结回放与构建](#冻结回放记录)

## 冻结回放记录

历史门禁重新运行实际 Godot Vulkan 对局，并分别用两个视觉变体核对封版权威日志。旧配置未开启本阶段新增的动作、手势、续防或反应选项。归档的物理输入和日志保持原样。

- 13C 的 323 帧角色／实体和 69 帧取消记录、阶段 14 的 640 帧和 2,560 条记录全部一致。最后一次指令识别修复后，使用最终 Godot 构建重跑 13C，结果仍逐帧一致。 [13C／14 日志](../local-mugen/stage25-frozen-13c-23.log) · [13C 最终复跑](../local-mugen/stage25-frozen-13c-final.log)
- 阶段 15 的 2,520 帧和 10,080 条记录，以及分数边界的 190 帧和 760 条记录一致。阶段 16 的 58 场、5,676 帧、22,704 条记录一致。阶段 17 的 45 场、4,247 帧、25,482 条记录一致。 [阶段 15 主回放](../local-mugen/stage25-frozen15-resume.log) · [边界与阶段 16–18](../local-mugen/stage25-frozen-15f-23.log)
- 阶段 18 的 53 场、4,972 帧、39,776 条记录一致，包括直接打断。 [阶段 18 日志](../local-mugen/stage25-frozen-15f-23.log#L167)
- 阶段 19 的 10 对、1,207 帧、11,812 条记录，阶段 20 的 41 对、4,623 帧、43,202 条记录，以及阶段 21 的 47 对、6,062 帧、56,315 条记录一致。 [阶段 19–21 日志](../local-mugen/stage25-frozen-19-22-live.log)
- 阶段 22 的 29 对、5,249 帧、48,260 条记录一致。 [阶段 22 日志](../local-mugen/stage25-frozen-22-live.log)
- 阶段 23 的 47 对、7,898 帧、73,151 条记录与阶段 24 的 63 对、8,192 帧、79,058 条记录一致。 [阶段 23 日志](../local-mugen/stage25-frozen-23-live.log) · [阶段 24 日志](../local-mugen/stage25-frozen24-resume.log)
- 阶段 24 的 `repeat-kick-light-45` 和 `tech-split-56` 使用最终构建复核，分别匹配 837 和 1,412 条记录，覆盖冻结输入优先级、扫倒及受身。阶段 24 共存组也在最终构建通过，5 对、800 帧、7,843 条记录。六个旧 Wave 来源使用最终构建和阶段 25 配置重跑。 [优先级与受身](../local-mugen/stage25-frozen24-final-boundaries.log) · [阶段 24 共存组](../local-mugen/stage25-frozen24-final-coexistence.log) · [Wave 六来源](../local-mugen/stage25-old-wave-final.log)
- 初次并发回放的阶段 15 有一次 Godot 启动错误 `0xc06d007f`，用新输出目录完整重跑后通过。历史回放分流至独立输出目录。原串行队列进入已经分流的阶段时由输出目录检查提前结束，独立门禁日志保留实际结果。 [复现命令](../tools/Stage25Validation/README.md)

## 兼容边界

- 原版生命值在目标反应 tick 提交，NewKOF 在接触 tick 原子结算。原版 RNG 流共享，NewKOF 每席独立。命中对照分别复算来源伤害公式和实际取样，保留真实样本值。
- 430 从地面轻击反应恢复、正常跳跃目标资格及受击倒地资格分别对照。430 的完整空中受击速度、其他空中招式与通用空战属于后续范围。
- 原版 State 100／101 提供跑步入口。230／235、245、300、310、1050 仍属于实验配置的范围边界。已取证的站立轻脚窗口和 430 恢复时的前＋x 保留候选优先级。模式 2 验证 430 本体、共同衔接及自然手势隔离。
- 站防尝试和连续格挡中松下的样本分别在 F32、F49 恢复控制。NewKOF 比原版早一 tick 应用持后步行，单步 3.16796875。比较器分别核验恢复帧、单步位移和此前逐帧位置。
- 原版近身空挥用已起跳的对手隔离攻击。NewKOF 的 `near-high-target-jab-to-kick-12` 在 L32（F18）先发生旧 State 205 对空接触，因停顿未在 L36 接出 430。该样本记录了既有空中运动与碰撞差异，近身 205 的地面路径另行比较。

## 本轮构建和测试

- BattleCore Debug 0 警告、0 错误。Godot Debug 0 警告、0 错误。测试工程 2 警告、0 错误，保留既有 `ThrowTests.cs:284` CS8600 和 `CombatEventTests.cs:197` xUnit2029。
- C# 最终专项 229/229、全量 1,450/1,450，均零失败、零跳过。日志位于 `local-mugen/stage25-release-csharp-focused.log` 和 `local-mugen/stage25-release-csharp-full.log`。
- Python 专项 7/7、全量 108/108，均零失败、零跳过。记录位于 `local-mugen/stage25-importer-focused.log` 和 `local-mugen/stage25-importer-full-final.log`。
- 以相同原包和七批参考在独立目录生成，6 份 JSON 逐字节一致，两套视觉别名均与各自 binding 一致。最终默认 fighter SHA-256 为 `eecc43b01bff6ec0c4ed11c6faa97f07253aff805bfeeeee688f803641ac089e`，模式 2 为 `b47b323f17ea1f161a3b52106c3d81e65251dc23e79dc91170d0a6cc6b85b882`。 [导入复现](MUGEN_IMPORT.md#阶段-25-蹲轻脚与普通技衔接)

```powershell
dotnet build core/BattleCore/BattleCore.csproj -c Debug --no-restore -t:Rebuild
dotnet build tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-restore -t:Rebuild
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~CrouchRouteTests|FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~FighterDataTests|FullyQualifiedName~ComboCancelTests|FullyQualifiedName~MultiSourceCancelTests|FullyQualifiedName~ScriptedPauseTests'
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-build --no-restore
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
dotnet build NewKOF.csproj -c Debug --no-restore
Remove-Item Env:DefaultItemExcludes
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
$env:PYTHONPATH = 'tools/MugenImporter'
py -m unittest discover -s tools/MugenImporter/tests -p test_state_crouch_light_kick.py
py -m unittest discover -s tools/MugenImporter/tests
```

`project.godot` SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。

## 复现入口

- [原版轨迹、输入和源文件哈希](STAGE25_REFERENCE.md)
- [导入命令](MUGEN_IMPORT.md#阶段-25-蹲轻脚与普通技衔接)
- [阶段 24 冻结参考](STAGE24_ACCEPTANCE.md)
- [Godot Vulkan 命令](../tools/Stage25Validation/README.md) · [图像与日志索引](../tools/Stage25Validation/EVIDENCE.md)
