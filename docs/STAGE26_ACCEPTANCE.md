# 阶段 26 Terry State 230／235 验收

开工基线为阶段 25 的 `70c3fdf39aa4c95479e5ee1fd54133a664bf4b5b`。本地、远端跟踪和 GitHub main 同一提交，工作区干净。使用 Godot 4.7.2 Mono/Vulkan、.NET SDK 10.0.403、Python 3.11.9 和固定 IKEMEN GO。`project.godot` SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。

**正式 PASS，7/7。** 本文随阶段 26 封版提交入库。阶段 13C–25 冻结权威回放全部通过。

## 验收矩阵

- **26A 原版依据 · PASS。** 两招的 AIR、HitDef、CMD、Helper、原始权限及身体距离边界按固定源码和实测交叉验证，242/242 原版场景通过。 [原引擎参考](STAGE26_REFERENCE.md) · [完整门禁](../local-mugen/stage26-native-canonical-gate.log)
- **26B 两招本体 · PASS。** 230 为 5／3／11 tick，235 为 4／5／7 tick。均为单段中段，动态伤害基数 5／6，种子 1337 的站姿标准命中为 30 HP，命中停顿 11／13，格挡停顿 11／11，初始推力 6，零削血。两套模式、站／蹲命中、两种格挡、中断、空挥、镜像和双席完成原版与 Vulkan 对照。 [本体证据](../tools/Stage26Validation/EVIDENCE.md#本体试跑)
- **26C 选招与输入 · PASS。** 27.5／28／28.5 依次选择 235／235／230。恢复当帧与 440 缓冲样本按消费时的方向、身体距离和相位选招。旧 a 胜出后，新的 b 自然过期。地面 a+x 可保留 720。HitPause 方向／按键四种分离顺序在解冻旧 tick 5 保留 300 或 310，4,007 条开关记录一致。 [距离与命令证据](../tools/Stage26Validation/EVIDENCE.md#选招及衔接试跑) · [分离输入复跑](../local-mugen/stage26-final-split-resume.log)
- **26D 普通技衔接 · PASS。** 200／205 在旧 tick 7–11、400 在 6–12、430 在 6–13 可接 235／230。235 在 9–15 可接 200／205／400／430／235／230。230 的普通技只在自然结束后出招。按键有效年龄 0–2，接续建立独立攻击实例。核对窗前缓冲到首帧、旧 tick 15 的 235 自连、近转远和接 430，以及旧 tick 16 的自然选择。模式 2 的自连窗口一致。 [原版窗口](STAGE26_REFERENCE.md#选招和已实现普通技衔接) · [普通技实测](../local-mugen/stage26-final-ordinary.log) · [旧 tick 15 地面对照](../local-mugen/stage26-final-430-last.log) · [模式 2](../local-mugen/stage26-final-ordinary-mode2.log)
- **26E 特殊技边界 · PASS。** 默认 235 直接 Wave 只在旧 tick 5–8，命中、格挡、空挥均已取证。235、230 和 430 分别在旧 tick 16、19、14 自然取得许可。模式 2 保留 1050，无弱 Wave 实体。经 200／205／400 的间接路径由后继普通技自己的时钟取得许可。持 x 可保留 Helper，松键对照先过期。 [固定回放](../tools/Stage26Validation/EVIDENCE.md#最终配置) · [直接 Wave 与混合输入](../local-mugen/stage26-final-wave-outcomes.log) · [205 地面对照](../local-mugen/stage26-final-grounded-205-aligned-vulkan.log)
- **26F 确定性与画面 · PASS。** 正式配置固定 Vulkan 55 对、7,477 逻辑帧、69,155 条视觉开关权威记录一致，留存 2,051 张 `FramePostDraw` 图像。独立同种子复跑另有 949 条一致，像素夹具通过。实时稀疏采样为 30／30／30 和 60／60／60 FPS，启动 L4 各有一次积压，动作采样区间无积压。 [固定图像](../tools/Stage26Validation/EVIDENCE.md#最终配置) · [实时图像](../tools/Stage26Validation/EVIDENCE.md#实时采样) · [像素夹具](../local-mugen/stage26-gpu-pixels-release.log)
- **26G 共存与交付 · PASS。** 阶段 26 新配置的六个旧 Wave 来源通过 5 对回放，7,855 条共享历史记录和 7,997 条视觉开关记录一致。430 连挡及 Wave、440 扫倒／接地受身／Wave 追击限制另有 5 对、6,138 条开关记录。三个工程构建、专项、全量与阶段 13C–25 冻结门禁通过。 [六来源](../local-mugen/stage26-resume-old-wave.log) · [当前配置兼容回放](../local-mugen/stage26-current-legacy.log) · [冻结记录](#冻结回放记录)

## 兼容边界

- 原引擎在目标反应帧提交扣血，NewKOF 在接触帧原子提交。原引擎 RNG 共享，NewKOF 每席独立。核对新动作时用各自权威轨迹和公式重算每次接触。既有定点和镜头位置偏差按具体场景核对。
- 已确认的受击恢复后步行相位差可改变 235／230 离散结果。阶段 26 配置修正该相位，以及对手已运动时的同 tick 受击结束选招。地面时序继续使用既有快照字段，分离方向的锁存单独保存在不可变指令快照中。冻结阶段 25 配置按原来的时序运行。
- 普通跳跃和对空反应仍保留已有引擎边界。旧 205 探针提前接触；235 接 430 的空中目标探针也在 F33 提前接触，原版为 F34。独立地面对照在旧 tick 15 完成相同转接。440 的自然收招样本还保留一步 `3.16796875` 的原有偏差，可改变后继 205／200 的阈值结果，详见原版参考。跑步、240／245、300、310、720、1050 本体继续延期。 [差异与地面对照](STAGE26_REFERENCE.md#特殊技与格挡连续性)

## 本轮构建与测试

- BattleCore 和 Godot Debug 均为 0 警告、0 错误。测试工程的重新编译记录有两项既有警告，`ThrowTests.cs:284` 的 `CS8600` 和 `CombatEventTests.cs:197` 的 `xUnit2029`，0 错误。 [核心构建](../local-mugen/stage26-final-core-build.log) · [Godot 构建](../local-mugen/stage26-final-godot-build.log) · [测试工程](../local-mugen/stage26-final-tests-rebuild.log)
- C# 专项 246/246、全量 1,467/1,467，零失败、零跳过。分离方向回归在修正前准确失败 4 项，修正后 6/6。 [专项](../local-mugen/stage26-final-csharp-focused.log) · [全量](../local-mugen/stage26-final-csharp-full.log)
- Python 专项 11/11、全量 112/112，设定原包审查环境后零失败、零跳过。 [专项](../local-mugen/stage26-final-python-focused.log) · [全量](../local-mugen/stage26-final-python-full.log)
- 两次独立转换的 6 份 JSON 逐字节一致。两套已安装 fighter／binding 和两套视觉别名也通过核对。默认 fighter SHA-256 为 `08c016725b08f206aa2c6eac9f4bc03ad418f95f95c10b4d354dec23aeb71434`，模式 2 为 `bdec9aa9095652861862414d4a1260814f509515eed32d1c385c4694868c21d4`。 [生成物哈希](../local-mugen/stage26-importer-final-242-determinism.log)

```powershell
dotnet build core/BattleCore/BattleCore.csproj -c Debug --no-restore
dotnet build tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-restore -t:Rebuild -p:BuildProjectReferences=false
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~CrouchRouteTests|FullyQualifiedName~FighterDataTests|FullyQualifiedName~ComboCancelTests|FullyQualifiedName~MultiSourceCancelTests|FullyQualifiedName~ScriptedPauseTests'
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-build --no-restore
$env:PYTHONPATH = 'tools/MugenImporter'
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
py -m unittest discover -s tools/MugenImporter/tests -p 'test_state_*light_kick*.py'
py -m unittest discover -s tools/MugenImporter/tests
```

## 冻结回放记录

历史门禁使用原阶段角色和物理输入实际运行 Godot Vulkan。开关视觉覆盖各核对一次。执行环境重启时保留已经核对过的日志与结束图，缺失对局补跑到独立目录。阶段 26 的方向锁存只影响 `reserveOnEdgeDirection`，地面受击排序和格挡退出保护也由新配置显式启用。先前完成的冻结配置均未使用这些字段。

- 13C 的 323 帧角色／实体与 69 帧取消记录一致。阶段 14 的 640 帧／2,560 条、阶段 15 的 2,520 帧／10,080 条与分数边界 190 帧／760 条一致。阶段 16 的 58 对／5,676 帧／22,704 条、阶段 17 的 45 对／4,247 帧／25,482 条一致。 [13C–17 回放](../local-mugen/stage26-freeze-13c-18.log)
- 阶段 18 的 53 对／4,972 帧／39,776 条、阶段 19 的 10 对／1,207 帧／11,812 条、阶段 20 的 41 对／4,623 帧／43,202 条、阶段 21 的 47 对／6,062 帧／56,315 条一致。 [18](../local-mugen/stage26-resume-18.log) · [19–20](../local-mugen/stage26-frozen19-22.log) · [21](../local-mugen/stage26-resume-21.log)
- 阶段 22 的 29 对／5,249 帧／48,260 条一致。 [22](../local-mugen/stage26-resume-22.log)
- 阶段 23 的 47 对／7,898 帧／73,151 条一致。断点恢复核对 20 个已有变体，并用最终构建补跑 74 个变体。 [23](../local-mugen/stage26-frozen23-compact.log)
- 阶段 24 的 63 对／8,192 帧／79,058 条一致。断点恢复核对 4 个已有变体，并用最终构建补跑 122 个变体。 [24](../local-mugen/stage26-frozen24-resume.log)
- 阶段 25 的 58 对／6,116 帧／56,271 条一致。 [25](../local-mugen/stage26-frozen25.log)

[原版复现](STAGE26_REFERENCE.md) · [导入生成](MUGEN_IMPORT.md#阶段-26-站轻脚与近远选招) · [Godot 复现](../tools/Stage26Validation/README.md) · [图像与日志索引](../tools/Stage26Validation/EVIDENCE.md)
