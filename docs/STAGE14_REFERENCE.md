# 阶段 14 原引擎基准

## 运行来源

2026-11-13 在 Windows 上以 IKEMEN GO nightly 源提交 `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0` 重建独立运行目录。用本机已有研究工具链、运行资源和 KFM 场景执行原始 `Terry-KOF98.def` 双人对局。角色包的 localcoord 为 320，两个原版 Terry 均为正常模式、初始生命 1000，关闭自动防御、引擎防御崩坏与眩晕，按默认 60 Hz 模拟。启动日志显示 NVIDIA GeForce RTX 3060 Laptop GPU、OpenGL 3.3.0；NewKOF 的 Vulkan 图形验收另行记录。

| 输入 | SHA-256 |
| --- | --- |
| `local-mugen/terry.zip` | `299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698` |
| `Terry-KOF98.def` | `eb6507b8e5fed6e4f71107a539616c7c6f0d4a568e67f7b9c8fc5cd7445471d7` |
| `Terry-N.cns` | `dcbee6b2378faf00c7b7912a393e3c640ff3b9179b113b14b7c2ce9a5ecbab75` |
| `Terry.cmd` | `ee57ffe1d6d4a2770b47590ab69177de79b24ccfc0153702dd3602f633507b59` |
| `Terry.air` | `252646cc2940f776e94df0b7b7fd3772e098d75fda6e9b781135eaa36e0fd851` |
| `Terry-2-3.cns` | `90b83cdacd33a3800a825ee42bdbd6502b7989f4dec358126587edeb4495fdc4` |
| `common_kof.cns` | `3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b` |
| `Constants.cns` | `f2dec82cca8857f78a702ee03149642c632a2efa2a70f01ac2129ca82fa98c55` |
| `stages/kfm.def` | `00da2b49c600acc685f7c4f7f2c4d468323cc4620ebcdf76a098e40f1696608c` |
| `save/config.ini` | `7732670d10806157c548420949df3e65b906e2cbe6e1a0f456e467b0d7be256c` |
| 规范回放 `Ikemen_GO.exe` | `89a43e8bdeee1fc4aa62128a5a3b523184b897eb420614d8932bd38e27e3ca6e` |

采集器使用本机研究仓库已有的命令、碰撞和全局 tick 钩子，另在 HitDef 求值完成处记录有效值。夹具仅在 RoundState 2 的首 tick 设置位置、朝向、速度和随机种子，并向主角色注入物理输入。角色脚本、资源、碰撞和结算均沿原引擎路径执行。双方保持默认站立直至相应输入。启动日志中的无关重复资源、旧脚本参数、缺少 Action 9999 的提示保存在各场景 stderr。

## 时序和结算

`F` 为从首个活跃对局 tick 起算的帧号，F5 按物理 `x` 并进入 State 200，动作 tick 0 即 F5。JSONL 的 `tick`、`matchTime` 是引擎原始模拟计数，`time` 为角色状态 Time，`elem` 与 `elemTime` 为动画元素及其局部时间。它们的推进点不同。采集器在逻辑阶段取样，未采集 GPU 提交或屏幕渲染帧。源命令先尝试近身 State 205，远端入口要求地面、不按下且满足控制条件。场景中的 F5 入招与 F8 接触均为原引擎确认值。

- 以 `after-collision` 栏对齐 3/4/5 的实际判定和画面用资源：启动 F5–7 为 200,0，生效 F8–11 为 200,1，空挥恢复 F12–16 回到 200,0。F17 按 AnimTime 归零切 State/Action 0 并恢复控制。表中的 `end` 记录提交后的状态。它的 `elem` 可以在当前显示资源之后先推进，如 F7 的 `elem=1` 但 `frame.sprite=[200,0]`，F16 的 `elem=0` 仍处于 State 200；应根据 `frame.sprite`、时钟与采样阶段一起解释。
- Action 200 仅源元素 2 持有 Clsn1 `[4,-88,59,-73]`。F8 攻击方 Clsn2 是 `[-20,-88,19,-1]`、`[-8,-100,39,-69]`，站立受击方 Clsn2 是 `[-20,-88,19,-1]`、`[-8,-104,15,-81]`。原引擎坐标 Y 向下，镜像由朝向换边。F8 标准 HitDef 属性为 S,NA，地面可防标志 3 对应站／蹲。空挥 F8–11 每 tick 求值一次，命中或防御后再无重发。
- 首次接触的 HitDef 标称伤害为 `floor((7−1)×9.615)=57`。F8 攻击方 `var6=0,var12=0,var21=1,var40=0,Helper(40000).var0=0`。全局攻击补正 0.8、接触前目标防御倍率 1，IKEMEN 对 `57×0.8` 四舍五入得 46。F8 碰撞排入 46，F8 结束仍为 1000，F9 结束提交到 954。Terry 的 `DefenceMulSet=.97` 在原引擎的 MUGEN 兼容模式延迟应用于受击状态，F9 可见防御倍率约 1.030928。空挥 F11 处原脚本变量可改写后续求值，初次接触 46 是本夹具测得的普通模式值。
- 命中和防御两种 HitDef 均给攻击方 11 tick 停顿，受击方 HitShake 分别为 13、11。F8 碰撞后攻击方计数 12 是调度补偿，同一 tick 结束计数 11。攻击者在 F9–19 保持动作时间与显示姿态，F20 继续，F28 回 State 0。受击者从 F8 起在 State 5000，F21 HitShake 降至零，F22–31 进入 State 5001，F32 回 State 0 并可控。原始 ground.hittime 和 ground.slidetime 均为 9，State 5001 实际占 10 个结束采样，末 tick 的制动先乘 .6，再归零。防御者 F20–28 推退 9 tick，F29 在 State 140 恢复控制。
- 地面普通命中和防御的局部 HitDef 速度同为 −6。以世界坐标看，正向命中初始位移 +6，后续每 tick 用 .85 立姿摩擦；F30 速度幅值 1.389702，F31 位移约 +0.83382，对应 `common_kof.cns:1460–1463` 的 .6。左向正好镜像。站防从 F20 开始推退并用 .85。F8 才开始按下后防的蹲防也从 F20 推退，持续按下用 .82，F25 后放开则按 `common_kof.cns:1230–1238` 在 F26 切回 .85，同时保留 State 153 与 Action 151。两种防御均无末段 .6 制动。
- 站立边界在分离 78 接触。攻击右界 59 加面左目标近侧范围 19，恰好边接触。IKEMEN `src/common.go:325–355` 用 `overlapX >= 0 && overlapY >= 0`，79、80 有正间隙而落空。NewKOF 阶段既有正面积门禁应独立记录这条边接触差异。
- 预先蹲防的目标在生效窗上缘为 −72，拳框下缘为 −73，相距 1 个单位。F5 就下后防的四个生效 tick 均不接触。F8 才开始下后防时，碰撞仍采前一个站姿框，随后按当前下后输入选择 State 152 与 Action 151，证明原版允许这个特殊时序下的成功蹲防。

## 场景与证据

输入中 `右` 是 P2 朝左时的物理后方向。除了镜像，P1 向右、P2 向左。坐标是两角色在 F0 的 X，Y/Z 均为 0，镜像保留 P1 为施攻席位。每份轨迹包含 F0–65 的五个观察点，以及实际 HitDef 执行行。以下 SHA 对应 `local-mugen/stage14-reference-replay/results/` 的同一构建重放。首次采集仍保留在 `local-mugen/stage14-reference/results/`，其初期字段较少。

| 场景 | 初始 X／输入 | 实测 | JSONL 行数与 SHA-256 |
| --- | --- | --- | --- |
| `whiff` | −120／+120，F5 P1 x | F8–11 HitDef，F17 退出，无接触 | 334 · `276c6b1e4e03700ba3c15aefa4cb2841455e660c5caeb0f3ffc68269a5d3a158` |
| `hit` | −30／+30，F5 P1 x | F8 命中，F9 扣 46，F22 位移，F32 目标恢复 | 331 · `39afffaa1a842ce4a2167ed298de1a7093fd01a30c16054ed4294d7241792437` |
| `guard` | −30／+30，F5 P1 x，P2 F5–25 右 | F8 站防，目标 X 已后退到 +40.095127，0 chip，State 150→151→140，F29 可控 | 331 · `fb2121862ef521f5cb456a1d333b8cfe044b96be4238d249d43db6273d739aeb` |
| `mirror` | +30／−30，F5 P1 x | 与 hit 的生命、状态及停顿一致，X 和朝向反号 | 331 · `4de6f42399a2d6084ece9cbc71288fef870548eea7f923f328bfbc4a991cfc67` |
| `crouch-guard` | −30／+30，F5 P1 x，P2 F5–25 下右 | F8–11 有防御资格但伤害框相隔 1，F17 空挥退出 | 334 · `3bb560a9b17c66c7c3d7d550e4bd0c3a51b4d828358f1c4d2291cab82db461f3` |
| `crouch-guard-late` | −30／+30，F5 P1 x，P2 F8–25 下右 | F8 蹲防，0 chip，State 152→153→140，F29 可控 | 331 · `8dfd053aefb7ed65a0cd874bdbad7165e11d814d780f0bf45968ca4895f3b399` |
| `boundary-78` | −39／+39，F5 P1 x | F8 边接触，F9 扣 46 | 331 · `6ae06a015c8dbdf3c124711bc9bbc5a99228e7b674cfe36f17661949ddaadfd6` |
| `boundary-79` | −39.5／+39.5，F5 P1 x | 1 单位间隙，空挥 | 334 · `180a690afedff19be593ec87032ea4d1f3a032863df9280ed3accd0a99d28d44` |
| `boundary-80` | −40／+40，F5 P1 x | 2 单位间隙，空挥 | 334 · `72f0f3e1cf1af33dd4f3d6561a57ced8d58c21c8695518ee5b225aa45bb8fe5b` |

重跑在仓库根目录执行。为新采集选择新的忽略目录，保留已归档原始轨迹。需要本机 `Ikemen-GO` 研究目录中的 `test-runtime/round2-5dac5caf` 运行资源、`test-runtime/tools` Go 和 MSYS2 环境，路径可通过 `-IkemenRoot` 指定。

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -Prepare -RunDirectory local-mugen/stage14-reference-new
python tools/Stage14Reference/Check-Traces.py local-mugen/stage14-reference-new/results
```

执行完成后保留 `run-metadata.json`、各 `*-metadata.json`、进程 stdout/stderr 与 JSONL。采集器会拒绝已有元数据或场景日志的输出目录。检查器验证上述九项及同构建补充项的入口、接触、动画／判定、伤害、停顿、位置和恢复。独立采集的这些可观察断言一致。开局前脚本变量和带构建时间的二进制哈希可变，原始 SHA 归档用于校验对应的实测文件。参考矩阵限制为同一原版对手、固定输入和 1v1 地面样本；未测迟到首次接触、空中、反制、AI、MAX、同招重入和跨招取消。

补充运行 `local-mugen/stage14-reference-held/results/` 使用同一引擎源与扩展输入夹具，十项断言全部通过。额外的 `crouch-guard-held.jsonl` 从 F8 持下后防到 F28，F29 放开。F20–28 均保留 .82 摩擦，F28 目标 X 为 57.746014，F29 恢复控制。轨迹共 331 行，SHA-256 为 `a0f36158c716b605f1264dc9fa00f06bc4ca06f140f3d62890de323f263d63c3`，采集器可执行文件 SHA-256 为 `787e900bad8b15e6f477e1c22ff104cc9bb15845a852d15b88c56f0266dbd297`。

## 第二招静态审查

候选为 Terry State 210，远站重拳。选择它是因为仍使用普通地面命中，却可验证更长的生效、不同击退、受击硬直和分支动画。这里只对照原脚本与当前真实 Manifest。

- `Terry.cmd:317–319` 为物理 `y`，1601–1622 先检查近身 State 215（`p2bodydist X<=34`），再尝试远身 210。远身分支要求可用地面输入、未按下，允许 `ctrl`、特定跑动状态或脚本变量条件。3919 处另有 AI 入招。
- `Terry-N.cns:216–232` 定义站立、攻击、无物理、进入清速度和控制、朝向对手。默认 Action 210，`Time=0 && var(10)=2` 时切换 Action 211。210 的 2/3/8/8/9 是 30 tick，默认判定窗候选 tick 5–12。211 的 1/2/2/3/4/3/4/4/7 同为 30 tick，候选判定窗 tick 5–11，横跨两个元素。两个 Action 的所有帧都能解析为现有图集矩形，使用 Pillow 验证了每个矩形含非零 alpha。
- `Terry.air:590–657` 的默认 Clsn1 `[12,-61,67,-84]` 需先规范化端点，再换算成面向右、Y 向上 X [12,67]、Y [61,84]。211 的第 4、5 元素使用同一规范化矩形，两个 Action 的 Clsn2 均按帧变化。相对于 200，210 的拳框更长、下沿更低，存在覆盖已蹲目标的可能，须由未来实测确认。
- `Terry-N.cns:247–321` 的 HitDef 与 200 共用尚未接触／未被反制等条件，伤害表达式改用 20 或 16，反应改用 Hard，地面硬直和滑行 17，速度为 `-10.5-ifelse(var(40),0.5,0)`。原始停顿表达式沿用 200。另有音效、硬直期间 Guard／Stun 变量控制和分支相关的 MoveType 清除。两条动画均在 AnimTime 归零回 State 0。
- 接入难度为中等。普通模式可复用有类型招式定义、独立停顿和移动档案，仍需实测强拳变量伤害、动作受击框和 215 距离优先级。完整 State 210 还需将 Action 211 的模式选择与阶段时序显式关联，再核对分支与恢复。本阶段未实现 State 210。
