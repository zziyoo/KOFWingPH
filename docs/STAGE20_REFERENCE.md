# 阶段 20 Terry 近身轻拳取消原版取证

原版 IKEMEN GO 固定在 `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`，使用原始 Terry-KOF98 双席与 KFM 场景。F0 是 RoundState 2 首个 tick。夹具设置开局位置、朝向及种子 1337，再经 InputUpdate 向根角色及独立 Helper 99999 发送物理按键。镜像反转开局坐标、朝向及方向键。默认 `var(10)=0`，模式 2 经原版 `Grant` 开场序列进入。State 200 的原版与 NewKOF 冻结基线见[阶段 19 取证](STAGE19_REFERENCE.md)及[阶段 19 验收](STAGE19_ACCEPTANCE.md)。

## 显式窗口与普通许可

`Terry-2-3.cns:52–70` 对 200 和 205 分别检查 `AnimElemTime(2)>0` 及 `AnimElemTime(3)<0`。Action 205 的元素时长为 3／4／5 tick，第二元素内部对应旧状态时间 4–6。`Terry.cmd:1140–1152` 在默认模式优先判定弱 Power Wave 入口，接受根角色命令或 Helper 的 `var(10)>0`，`var(1)=1` 提供显式取消许可。三个窗口 tick 均由实际命令验证，允许先前命中、格挡或空挥。

- 近身空挥样本起始中心距 36，P2 F0 垂直跳，P1 F13 首次按 `x`，进入 205。F17、18、19 完成手势，同帧由旧时间 4、5、6 进入 1000。F16 已完成手势时旧时间仍为 3，F17 根命令已无效，Helper 值 11 触发合法取消。F16 样本为避免覆盖 F13 首次 `x`，第一拍下方向放在 F12，F14 下前、F15 前、F16 前加 `x`。
- F20 完成手势时，旧时间 7、`var(1)=0`，根 QCF 和普通 `x` 同时有效。F20 选择新 205，F23 接触当时尚在空中的目标，伤害 22。F25 完成手势时旧时间 12、`var(2)=1`，仍可由普通许可进入 1000。F25 属于 205 收招结束边界。
- 收招竞争补测沿用同一近身空挥开局。F24 只按 `y`，F25 按 `x`，旧时间 12 时重拳根缓冲已空但 Helper 重拳值仍为 2，原版先选择 205，F28 命中 30。另一场 F22 下、F23 下前、F24 前加 `y`、F25 前加 `x`。F25 根 QCF、根 `x`、Helper 重拳值 2 均可用，原版先选 1000，F41 的 Helper 命中 84。两场均由 `var(2)=1` 的普通许可进入，`var(1)=0`。
- F16／17 的空挥取消均在 F17 进入 1000，Helper 在 F33 命中尚在空中的目标，原版 Y 为 −4.640022。F18／19 的取消样本中，Helper 分别在 F34／35 命中已落地的目标，伤害均为 76。旧近轻空挥的证据与 Helper 碰撞时的目标姿态分别记录。

## 近身命中、格挡与回落

- 地面对局中心距 36，P1 F5 进入 205，F8 命中实扣 30，守方 Shake 13。攻方 F9–19 共 11 tick HitPause，旧动作时间保持 4。F11、17、18、19 完成 QCF 均在 F20 恢复后取消，根命令有效，Helper 值分别是 −1、3、7、11。F11 按住 `x` 到 F20 时 Helper 值 3。F20、21、22 直接完成分别在旧时间 4、5、6 生效。F20 只按 `x` 的对照不产生 1000，旧动作于 F28 退出。
- 标准 F20 命中取消的 Helper F35 创建，F36 再命中，目标 X49.569145、伤害 76、Shake 13。F21 和 F22 取消的 Helper 命中分别在 F37 和 F38，实扣 84 和 76。P1/P2 镜像保持接触 tick、结算和停顿，世界 X 翻转。
- P2 从 F7 按后，F8 格挡轻拳，削血 0。F20／22 的手势分别在旧时间 4／6 取消；F36／38 格挡 Helper，削血 9、Shake 11、站防目标 X48.735325。蹲防保持下后时 F36 削血 9，目标 X45.746014，碰撞时为原版 State 131，帧末进入 152，F37 显示 Action 151。站防与蹲防的格挡样本均未触发全局 Pause。
- F23 同键回落根据当帧距离重新选招。中心距 36 的先命中样本以身体距 12 进入新 205，F26 第二次命中实扣 22。中心距 44 的先命中样本以身体距 20 进入 200，F27 第二次命中实扣 37。F26 近端地面受击框与 F27 的变化沿用[阶段 18 的记录](STAGE18_REFERENCE.md)。中心距 36／44 的先格挡样本在 F23 分别以身体距约 21.435／29.435 回落到 200，原版均于 F26 第二次格挡、零削血。回落样本未生成 1020。
- 距离边界样本的中心距分别为 46 和 46.5，原版身体距 16／16.5，首个 `x` 分别选中 205／200。两条路径均于 F20 以旧时间 4 启动同一 1000。F8 普通轻拳的伤害分别为 30／46，F36 Helper 实扣均为 76。

## 双席与模式边界

- 中心距 36、两席 F5 同时轻拳，均进入 205，F8 双方互中并进入 5000。P1 失血 38、P2 失血 30。F20 的两席手势未产生 1000，场上无 Helper 或全局 Pause。正向及镜像样本均确认这个中断边界。实际同时存在两枚 1020 的原版场景保留在阶段 19 双发基线。
- 混合双席采用同一对局内的两条合法路径。P1 F5 进入 205，F20 进入 1000，F35 创建 Helper ID 72。P2 F7–62 按后，F8 普通格挡削血 0、F36 Helper 格挡削血 9。第一枚 Helper F60 销毁，P1 F72 恢复站立。P2 F90 已可控并以身体距 100.620514 进入 200，F91 下、F92 下前、F93 前、F94 前加 `x` 在旧时间 4 空挥取消到 1000。F109 创建另一枚 Helper ID 73，F115 命中 P1 实扣 76，F136 销毁。P2 F154 在本地时间 52 回到站立。两条镜像轨迹一致，实体出生朝向及世界坐标翻转。原版跟踪数组在销毁后仍保留记录，生存判定使用 `destroyed`。
- 模式 2 运行时开场实际得到 `var(10)=2`。地面中心距 36，F5 进入 205、F8 命中 30，F20 QCF 在相同窗口进入 **1050**，F34 实扣 69。另一份模式 2 对照 F23 只按 `x`，同招 205 重进，F26 实扣 22。1050 属于另一个实体和动作规则，本证据用于划定 Stage 20 默认弱 Power Wave 范围。

## 采集与验证

主采集 31 场为 F0–F75，位于 `local-mugen/stage20-reference/results`。混合双席及镜像 2 场为 F0–F175，位于 `local-mugen/stage20-reference-mixed/results`。运行元数据 SHA-256 依次为 `f1a45eb7bc653ffce65f686b4ae5b6a7f7b16ffdccabeb4e4a6215a235d1a2c`、`7a19a4bdc98d2dce51fbc1486109e3ec24ba05120816a5647154809bf85e97f1`。执行文件 SHA-256 依次为 `75af2c4b52fbcb9bdcbf1f4f00fd81e881ac79bb9f2a8ca5deb94226f21cc0d0`、`5e31f42a25da0d9705ed410f37c1b6544b9cfcd8b800269308608ab7520bcafe`。主采集启动后才给补采添加了 `Constants.cns` 哈希验证，主采集仍由同一 Terry ZIP 哈希与六份脚本哈希固定。两个采集目录及 Stage 19 原始取证保留原样。

逐场保存 JSONL、逐项 SHA-256、stdout、stderr 及完成标记。JSONL 保留四种既有阶段记录、状态切换、HitDef、根／Helper 输入、双方坐标、`rng`、`var23`、`var31`。验证器校验 33/33，包括来源、进入首帧与清空旧命中确认、停顿缓冲、回落、范围、镜像、中断与双席实体生命周期。

```powershell
$run = 'tools/Stage14Reference/Run-Reference.ps1'
& $run -CaptureProfile Stage20 -Prepare -RunDirectory local-mugen/stage20-reference-new
$mixed = 'default-near-wave-mixed-sequential','default-near-wave-mirror-mixed-sequential'
& $run -CaptureProfile Stage20 -LastFrame 175 -Prepare -Cases $mixed -RunDirectory local-mugen/stage20-reference-mixed-new
python tools/Stage20Reference/Check-Traces.py local-mugen/stage20-reference-new/results local-mugen/stage20-reference-mixed-new/results
```

另有两个收招竞争样本保存在 `local-mugen/stage20-reference-terminal/results`，验收器合计 35/35。运行元数据 SHA-256 为 `f0bd3fbe9fad1b7b0c6794735271aaae58fa57fae8294246ec86e8f0e3688c5e`，执行文件 SHA-256 为 `6153de72db80ebfc0358651686d2b8645bd94d57c59802f07303cd290a0ae94c`。两项为独立补采，导入器沿用主采集与混合双席证据。

```powershell
$terminal = 'default-near-wave-whiff-terminal-heavy-light-25','default-near-wave-whiff-terminal-heavy-wave-25'
& $run -CaptureProfile Stage20 -Prepare -Cases $terminal -RunDirectory local-mugen/stage20-reference-terminal-new
python tools/Stage20Reference/Check-Traces.py local-mugen/stage20-reference/results local-mugen/stage20-reference-mixed/results local-mugen/stage20-reference-terminal-new/results
```
