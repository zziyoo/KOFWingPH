# 阶段 28 Terry 前＋轻拳（State 300）

开工时本地 `main`、`origin/main` 与 GitHub main 均为 `85b8ff843291c65aca6972aa33a757ed44c6c131`（阶段 27 正式收口），工作区干净。使用 Godot 4.7.2 Mono/Vulkan、.NET SDK 10.0.403、Python 3.11.9。`project.godot` SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`。原包、探针二进制与固定 IKEMEN GO 提交沿用阶段 27（[参考](STAGE28_REFERENCE.md)）。

## 440/300 关系与既有行为保护

阶段 27 的 440 恢复步行、F95 选招、同帧新招式优先等结论全部保留；本阶段不改动 440 本体。240 的模式 2 Action 241 与 `positionSteps` 相位保持。805 对 5110/5120 的资格边界、模式 2 的 attack.dist 与入防惯性、`holdGroundBlockstunExitPosition`、全局 Pause 的 Helper 配置均未触碰。

## 验收矩阵

- **28A 原版依据 · PASS。** `Terry.cmd:1538–1547` 的完整入口（根组合 OR Helper var(40)、两组权限触发）、`Terry-N.cns:1823–1974` 的 State 300 与 Action 300（`anim=300` 由 Statedef 证实）、两模式共用（无 ChangeAnim 分支）均有源定义与运行证据。 [逐项取证](STAGE28_REFERENCE.md)
- **28B 本体 · PASS。** 47 tick（11/9/27），双段 HitDef（stateTime 11–13 High / 17–19 Low，ID=30），伤害 `floor((5|9−var21)×fvar7)`，停顿 11/13、防御 11/11，hittime/slidetime 17，推力 10.5，PosAdd 五进三退。双段命中（MoveHitReset 语义）原生可达并由 `contactGroup` 1/2 承载，内核零改动。 [82 用例核对](../tools/Stage28Reference/Check-Traces.py)
- **28C 输入 · PASS。** 共享命令能力从占位迁移到真实动作。识别期方向门控**按原样保留**：冻结重放的权威对照显示按住 DF+x（holddown）不得产生候选（原生 `near-to-crouch-jab-wave-free-release` 逻辑 38 无候选），先移除门控的尝试被该对照否决并回退——边缘方向保留指令的候选在按键边沿创建，但仍受「根方向不在序列中且非停顿帧」的 CMD 门约束。「根组合或 Helper」的 OR 在消费期 `AllowsReservation` 求值（根组合 = `buffer.time=1` 的按键 tick 当帧 + holdfwd + !holddown；Helper 4→3 武装、3 tick 存活、HitPause 冻结、全局 Pause 按 Helper 配置采样衰减，`global-pause` F103/F104 样本）；根 `command="x"` 单 tick（age-1 三例原生否定样本）。蹲起（state 12）入边、延迟方向否定、单次消费均有专项测试。 [C# 专项 21 项](../tests/BattleCore.Tests/ForwardLightPunchTests.cs)
- **28D 入边与普通出边 · PASS。** 八个已实现来源的 var(1)=1 窗口全部接入（200/205: 4–6、215: 4–12、235: 5–8、245: 4–8、400: 4–4、410: 6–12、440: 11–17，stateTime 单位），窗口首/末/越界各有原生轨迹；越界回落按来源区分（200 自链、235 命中后滑出 16 阈值走普通链、245/215/440 无链拒绝、430 无窗口走普通链）。取消当帧建立独立攻击实例；未绑定的 805/310/720/250 占位语义不变。
- **28E 特殊技 · PASS。** 300 的直接弱 Wave 许可 = var(1)=2 窗口（stateTime 18–19）+ `movecontact||movereversed`（seg1 接触被 MoveHitReset 清除，实际需 seg2 接触）；默认模式经停顿冻结命令缓冲在解冻后触发（原生 F45 样本 + 运行时 wave-exit-guard 场景）。模式 2 无 1000/1050 状态变化。300 无普通技出边、不能自连；自然恢复 tick（var(2)=2）重开已验证（原生 300→300@stateTime 47 + C# 专项）。
- **28F 确定性与画面 · PASS（有界范围）。** 真实 Godot 运行时 6 个代表场景全部通过（自由命中、Helper 松向、440 取消首 tick、窗口越界拒绝、双停顿后的 Wave 出边、模式 2 一致性），最终构建复跑目录 `local-mugen/stage28-runtime-check-final`（首跑目录 `stage28-runtime-check` 为识别还原前的构建）；工具 [runtime_check.py](../tools/Stage28Validation/runtime_check.py)。最终构建上完整复验 13C Vulkan 门禁（7 用例逐帧与像素夹具对齐，见下表 13C 行）；实时 30/60 FPS 采样未重复执行（无渲染管线变化，历史证据可追溯）。
- **28G 共存与交付 · 见下节。** C# 专项 21/21、全量 1,513/1,513（1,491 基线零改动通过）；Python 专项 5/5、全量 125 通过；六份 JSON 两轮生成逐字节一致（确定性）。冻结门禁结果见下。

## 冻结门禁（阶段 13C–27）

最终 Godot Debug 构建（`NewKOF.dll` 99607d79…、`NewKOF.BattleCore.dll` 4b1233ab…）重放阶段 14–26 冻结输入与权威记录；13C 为同构建 Vulkan 门禁。收口时因 git stash 往返把内核源文件行尾统一（内容不变），同源重建的 `NewKOF.BattleCore.dll` 指纹变为 6c99bc6c…；与 4b1233ab 逐字节对比仅 72 字节差异（COFF 时间戳、MVID GUID、PDB 签名），IL 完全一致，且用当时失败的 13C `13cc_plain_cancel` 用例在两个 DLL 上分别复跑均命中黄金记录——车道证据有效。结果以 `local-mugen/stage28-frozen-*.log` 的 `Matched Stage N` 行为准：

| 阶段 | 对 | 逻辑帧 | 权威行 | 日志 |
|---|---:|---:|---:|---|
| 14 | 8 | 640 | 2,560 | [f](../local-mugen/stage28-frozen-f.log) |
| 15 | 28 | 2,710 | 10,840 | [f](../local-mugen/stage28-frozen-f.log) |
| 16 | 58 | 5,676 | 22,704 | [f](../local-mugen/stage28-frozen-f.log) |
| 17 | 45 | 4,247 | 25,482 | [f](../local-mugen/stage28-frozen-f.log) |
| 18 | 53 | 4,972 | 39,776 | [f](../local-mugen/stage28-frozen-f.log) |
| 19 | 10 | 1,207 | 11,812 | [f2](../local-mugen/stage28-frozen-f2.log) |
| 20 | 41 | 4,623 | 43,202 | [g](../local-mugen/stage28-frozen-g.log) |
| 21 | 47 | 6,062 | 56,315 | [g2](../local-mugen/stage28-frozen-g2.log) |
| 22 | 29 | 5,249 | 48,260 | [g2](../local-mugen/stage28-frozen-g2.log) |
| 23 | 47 | 7,898 | 73,151 | [g2](../local-mugen/stage28-frozen-g2.log) |
| 24 | 63 | 8,192 | 79,058 | [g2](../local-mugen/stage28-frozen-g2.log) |
| 25 | 58 | 6,116 | 56,271 | [g2](../local-mugen/stage28-frozen-g2.log) |
| 26 | 55 | 7,477 | 69,155 | [e](../local-mugen/stage28-frozen-e.log) |
| 合计 | 542 | 65,069 | 538,586 | 与阶段 27 封版记录一致 |
| 13C | Vulkan 门禁（7 用例全对齐：throw_hit 80 / clash 68 / team 105 / interrupt 70 / cancel 69 帧） | | | [13c2](../local-mugen/stage28-frozen-13c2.log) |

f 车道与 g 车道首跑各有一次 25 秒逐例超时（双车道争用，非逻辑失败），把 `run_logged` 的单例超时放宽到 60 秒后用新输出目录（f2/g2）复跑成功；`Matched Stage N` 行数与阶段 27 封版一致。13C 首跑在 `13cc_plain_cancel` 出现一次实时抖动（S 松开事件相对按键晚落一个逻辑帧 → 236 手势差一环，缓冲的普通轻拳迟到发动），属固定帧率实时门禁固有的罕见注入偏斜；同 DLL 同输入复跑命中黄金记录后全量 13C 复验通过（13c2 目录）。

## 测试与构建

- C# 专项 21/21（`ForwardLightPunchTests`）、JSON 加载探针 1/1、全量 1,513/1,513，零失败零跳过。BattleCore 与 Godot Debug 构建 0 警告 0 错误。测试工程保留既有 `CS8600`（`ThrowTests.cs:284`）与 `xUnit2029`（`CombatEventTests.cs:197`）。 [专项](../local-mugen/stage28-tests-focused.log) · [全量](../local-mugen/stage28-tests-full.log)
- 构建修复：`NewKOF.csproj` 在 Godot SDK 组合下默认 `**/*.resx` 通配把未展开字面量传给 GenerateResource（MSB3552，仓库内本无任何 resx），按既有"显式编译边界"方针补 `EnableDefaultEmbeddedResourceItems=false`；修复前后场景 DLL 指纹不变（99607d79…）。
- Python 专项 5/5（`test_state_forward_light_punch.py`）、全量套件通过。 [专项](../local-mugen/stage28-py-focused.log) · [全量](../local-mugen/stage28-py-full.log)
- 原生探针 82 用例 `completeVerified=true`。 [核对日志](../local-mugen/stage28-check-traces.log)
- 生成器修复记录：命令 JSON 的 `sourceLines` 触发严格 DTO 拒绝（已剥离）；Wave 取消窗口单位勘误（7–8 → 18–19，MoveElapsedFrames 单位）；补齐 200/205/215/400/410/440 的 reservedCancels（任务书预见的"不能仅替换两个现成占位"）。

## 兼容边界与保留差异

- 守护削血 `fvar(10)` 与 `var(12)` 崩防状态在原档存在，维持前几阶段的"不建模"决定。
- MAX 模式（根 `var(40)`）未实现，HitDef 的 `ifelse(var(40),…)` 分支恒取非 MAX 值。
- 跑步 State 100/101 的入口许可未接入（跑步本体延期）；空中探针仅用于资格核对。
- 原版扣血相位、共享/分席 RNG、定点与镜头差异继续按既有记录保留。
