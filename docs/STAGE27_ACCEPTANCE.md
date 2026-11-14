# 阶段 27 Terry 站重脚与 440 恢复

开工时本地 `main`、`origin/main` 与 GitHub main 均为 `12776456c3382b7c7d1c0b87df49cb8dbe1ebb18`，工作区干净。使用 Godot 4.7.2 Mono/Vulkan、.NET SDK 10.0.403、Python 3.11.9。`project.godot` SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。

## 440 原失败复验

阶段 26 的物理输入从 F5 下＋重脚开始，随后 F56–79 前行。保留中心距 44、原种子、后续所有按键和帧号。在首处分歧 F56，IKEMEN GO 读取 `var(2)=1`、前方向与无下，经 `Terry.cmd:1806–1815` 的 Walk 控制器从 440 进入 State 20。旧姿态结束是 `Terry-N.cns:1342–1346` 的回蹲 State 11。两个阶段 26 实际生成配置都有 `returnsToCrouch`，缺少 `walkOnCrouchRecovery`。NewKOF 在 F56／L70 回蹲，X 仍为 −22，下一 tick 才移动。原版同 tick 已到 −18.83203125。

阶段 27 的 440 使用现成的 `walkOnCrouchRecovery`。原样重跑后，F56／L70 的 NewKOF 位移为 `207616/65536`，F95／L109 双方使用身体距 13.46875，进入 205。原失败构建处于 16.63671875，进入 200。持键对照 F99 进入 Wave，F115 命中。松键对照无 Wave。正式配置重跑两对，4,979 条开关视觉权威记录一致。 [修正前](../local-mugen/stage27-sweep-red.log) · [正式配置复验](../local-mugen/stage27-final-original-sweep.log)

独立自然结束对照从 440 空挥的 F45 旧 tick 40 开始，固定 Vulkan 核验前／后位移、持下与无方向回蹲、`F+a` 优先启动 230 且当帧不步行。正式配置五对共 5,199 条开关视觉权威记录一致。另四对检查 440 步行后的 28／48 身体距边界，共 6,180 条开关视觉权威记录一致。 [自然恢复](../local-mugen/stage27-final-sweep-recovery.log) · [后继选招](../local-mugen/stage27-final-sweep-thresholds.log)

## 验收矩阵

- **27A 原版依据与直接依赖 · PASS。** 440 保留原失败输入复验，240／245 的入口、攻击数据、State −1 和 PosAdd 均有签名轨迹。 [来源和逐条复现](STAGE27_REFERENCE.md)
- **27B 两招本体 · PASS。** 默认 Action 240 为 13／3／22 tick，近身 245 为 3／6／20 tick。模式 2 的远身 State 240 使用独立 Action 241，19／4／20 tick。逐帧核对本体、停顿、伤害、站蹲反应、两席和镜像；两种模式 2 防御均已复验。 [实际图形证据](../tools/Stage27Validation/EVIDENCE.md)
- **27C 输入与选招 · PASS。** 身体距 47.5／48／48.5 对应 245／245／240。双向缓冲跨距、消费时站蹲、旧 `a` 与新 `b`、分按与混合按键及世界暂停下的 Helper 有固定 Vulkan 轨迹。805 在身体距 10.300781 的倒地受击目标前回落 245，原版 5120 对照优先保留 805，NewKOF 单次消费。投技本体和原版随后的 805→245 不在本阶段。 [权威输入](../tools/Stage27Validation/EVIDENCE.md) · [805 复验](../local-mugen/stage27-delivery-805.log)
- **27D 普通技路径 · PASS。** 两招均无已证实的提前普通技入边、出边或同招重进。验证较早输入拒绝、自然恢复合法新开招、按新距离选择、连续攻击实例和独立接触。 [自然选招与接触](../local-mugen/stage27-final-quantized-contacts.log)
- **27E 特殊技边界 · PASS。** 245 旧 tick 4–8 可直接取消到弱 Wave，空挥、命中、格挡、冻结输入和手势竞争通过。240 只在自然恢复取得 Wave 资格。240／245 无已证实的提前普通技转接 Wave 路线。模式 2 1050 保留候选，七个既有默认弱 Wave 来源均在新配置实际复验。 [逐帧证据](../tools/Stage27Validation/EVIDENCE.md)
- **27F 确定性与画面 · PASS。** 固定回放逐帧断言资源与提交版本，同种子独立复跑 1,039 条记录一致。Vulkan 像素夹具通过。实时两档分别实测 30／27／30 和 59／60／60 FPS。30 档一次积压发生在取图之后，60 档无积压。 [固定帧、实时与像素证据](../tools/Stage27Validation/EVIDENCE.md)
- **27G 共存与交付 · PASS。** 最终候选构建按序回放阶段 13C–26 冻结门禁：阶段 14–26 共 542 对、65,069 逻辑帧、538,586 条权威记录逐条一致，13C 已在相同构建单独完成真实 Vulkan 复验。逐阶段结果见下节。 [回放命令](../tools/Stage27Validation/README.md)

## 兼容边界与补测

- 模式 2 的早期 Vulkan 配置缺少 `attack.dist` 激活条件，F6 仍在向后走，F24 漏判了原版的格挡。先使用原版 `230` 与入防惯性，复现 F24 Blocked。站防退出另发现 F53 多走一步，按原版 State 140 加入当帧位移保持。输入、招式时钟与飞行道具防御沿用原有规则。修正后站／蹲防两对实际回放通过，共 2,483 条视觉开关权威记录一致，站防 F54 恢复步行。 [原版](STAGE27_REFERENCE.md) · [初始失败](../local-mugen/stage27-final-bodies-mode2.log) · [退出失败](../local-mugen/stage27-final-mode2-guard.log) · [两种姿态修正后](../local-mugen/stage27-final-mode2-guard-exit.log)
- 向外跨距的原物理输入中，F44 身体距 45.304688，F45 为 48.472656，两个引擎在同一帧选 240。F47 站防松开后原版沿已有惯性滑行，差值在 F58 接触前为 7.293771。原输入的 F58 命中、扣血公式、独立停顿、从各自接触起点计算的位移与恢复均对齐。保持后方向的独立对照选招相同且在 F58 格挡，逐帧位置也在原有定点容差内。 [保留原输入](../local-mugen/stage27-final-outward-release.log)
- 原版延后一帧扣血，NewKOF 在接触 tick 原子提交。原版随机流全局共享，NewKOF 分席。保留轻微定点摩擦误差及原版镜头边缘限制，重复接触从前一次已验证的静止点建立位移基准。地面对照使用真实投影与接触时机，已有空中行为和模式 2 的 1050 本体延期。

## 27G 冻结回放

最终候选构建（`NewKOF.dll` 5a397045774217ce2852360c000788250f34431e119ba11153e2f28042280d8a、`NewKOF.BattleCore.dll` d59689ddde69b97fad8f54dd0c83ce1d367ea2087e3852416d848c349c521e8b）以 `frozen_replay.py --headless-authority` 按各阶段冻结输入与黄金权威记录回放，阶段 14–26 每阶段恰有一条 `Matched Stage N`，合计 542 对、65,069 逻辑帧、538,586 条视觉开关权威记录逐条一致；13C 在相同构建的 Vulkan 复验见「测试与构建」。

| 阶段 | 对 | 逻辑帧 | 权威行 | 日志 |
|---|---:|---:|---:|---|
| 14 | 8 | 640 | 2,560 | [a](../local-mugen/stage27-frozen-delivery-a.log) |
| 15 | 28 | 2,710 | 10,840 | [b](../local-mugen/stage27-frozen-delivery-b.log) |
| 16 | 58 | 5,676 | 22,704 | [c](../local-mugen/stage27-frozen-delivery-c.log) |
| 17 | 45 | 4,247 | 25,482 | [a](../local-mugen/stage27-frozen-delivery-a.log) |
| 18 | 53 | 4,972 | 39,776 | [f](../local-mugen/stage27-frozen-delivery-f.log) |
| 19 | 10 | 1,207 | 11,812 | [a](../local-mugen/stage27-frozen-delivery-a.log) |
| 20 | 41 | 4,623 | 43,202 | [r1](../local-mugen/stage27-frozen-delivery-r1.log) |
| 21 | 47 | 6,062 | 56,315 | [r1](../local-mugen/stage27-frozen-delivery-r1.log) |
| 22 | 29 | 5,249 | 48,260 | [r2](../local-mugen/stage27-frozen-delivery-r2.log) |
| 23 | 47 | 7,898 | 73,151 | [r3](../local-mugen/stage27-frozen-delivery-r3.log) |
| 24 | 63 | 8,192 | 79,058 | [r2](../local-mugen/stage27-frozen-delivery-r2.log) |
| 25 | 58 | 6,116 | 56,271 | [e](../local-mugen/stage27-frozen-delivery-e.log) |
| 26 | 55 | 7,477 | 69,155 | [r3](../local-mugen/stage27-frozen-delivery-r3.log) |

执行记录：早期并行 lane（a–f、s23、s26）在同一候选构建上完成阶段 14–19、25 的 Matched，其余阶段进行中随上次会话中断退出（d、s23、s26 无完成标记）。b 的 Stage 18 首次执行失败是已归档 `.args.txt` 指向不存在的 `res://local-mugen/stage18-import-smoke-v2/`，属工具路径问题而非规则回归——脚本核对默认/模式 2 配置 SHA-256 后映射到冻结 `data/imported-characters/terry98-stage18-data/`，由 lane f 复验通过；[b 日志](../local-mugen/stage27-frozen-delivery-b.log) 保留作路径故障证据。剩余阶段 20、21、22、23、24、26 由续接 lane r1–r3 在同一构建指纹复跑补齐。11 份日志头部指纹一致，r1–r3 无失败行；脚本在每阶段完成后复核构建指纹未变化。

## 测试与构建

- 最终运行时 C# 专项 281/281、全量 1,491/1,491，均零失败、零跳过。BattleCore 和 Godot Debug 构建均零警告、零错误。测试工程零错误，仍报告既有 `CS8600`（`ThrowTests.cs:284`）和 `xUnit2029`（`CombatEventTests.cs:197`）。 [专项](../local-mugen/stage27-delivery-csharp-focused.log) · [全量](../local-mugen/stage27-delivery-csharp-full.log) · [BattleCore](../local-mugen/stage27-delivery-core-build.log) · [Godot](../local-mugen/stage27-delivery-godot-build.log) · [测试工程](../local-mugen/stage27-delivery-tests-build.log)
- Python 专项 8/8、全量 120/120，零失败、零跳过。两次实际生成的六份 JSON 逐字节相同，两个视觉别名与上阶段六份冻结哈希也通过核对。 [专项](../local-mugen/stage27-mode2-guard-exit-importer-green.log) · [全量](../local-mugen/stage27-mode2-guard-exit-importer-full.log) · [确定性](../local-mugen/stage27-mode2-guard-exit-determinism.log)
- IKEMEN GO 标准矩阵 169/169。补充七条签名轨迹，包括四条 F260 Wave 完整恢复、一条向外跨距且持防、两条 805 候选的姿态边界。 [原版参考](STAGE27_REFERENCE.md)
- 最终 Godot Debug 实际重跑阶段 13C 的 323 个角色／实体帧和 69 个取消帧，权威记录与原始归档一致。 [13C 回放](../local-mugen/stage27-delivery-13c.log)
- 新默认配置与七个原有弱 Wave 来源共存，对 430／235 以及 440 扫倒、接地受身、追击限制共完成 13 对 Vulkan 回放，覆盖 1,965 逻辑帧。跨预设 19,076 条共同记录一致，新配置下 19,218 条视觉开关记录逐条一致。 [共存结果](../local-mugen/stage27-final-current-profile.log)
- 固定 Vulkan 共 79 对签名轨迹、12,210 逻辑帧，113,995 条视觉开关权威记录一致，核对了 3,245 张实际提交图像。交付构建针对性复验 21/21，其中候选 805 四对、模式 2 八对、默认九对，31,241 条记录一致。 [图形证据索引](../tools/Stage27Validation/EVIDENCE.md)

```powershell
dotnet build core/BattleCore/BattleCore.csproj -c Debug --no-restore
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
dotnet build NewKOF.csproj -c Debug --no-restore
Remove-Item Env:DefaultItemExcludes
dotnet build tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-restore -t:Rebuild -p:BuildProjectReferences=false
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~CrouchRouteTests|FullyQualifiedName~FighterDataTests|FullyQualifiedName~ComboCancelTests|FullyQualifiedName~MultiSourceCancelTests|FullyQualifiedName~ScriptedPauseTests|FullyQualifiedName~ProximityGuardTests'
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-build --no-restore
$env:PYTHONPATH = 'tools/MugenImporter'
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
py -m unittest discover -s tools/MugenImporter/tests -p test_state_standing_heavy_kicks.py
py -m unittest discover -s tools/MugenImporter/tests
```
