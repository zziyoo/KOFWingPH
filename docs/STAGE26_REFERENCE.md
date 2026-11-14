# 阶段 26 原引擎参考

固定 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`，使用 `Terry-KOF98.def` 双席、KFM 场景和种子 1337。原包 `local-mugen/terry.zip` 的 SHA-256 为 `299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698`。初态由夹具设定中心位置和朝向，随后只注入物理输入。模式 2 通过原版入场 `Grant` 输入选择。原引擎 `F` 从 RoundState 2 开始，Godot 固定回放以 `L=F+14` 对齐，实际画面对应提交版本 `S=L+1`。旧 tick 指状态转换读取的有效动作时间，HitPause 和全局 Pause 不计入该时钟。

本轮隔离取证程序 SHA-256 为 `21261d8dbab9a34e107d3d9bf1d92a5e72f68560a0e1208f77588e2886a45722`，引擎配置哈希及八个原版脚本哈希随每批元数据保存。检查器核对源版本、脚本、输入逐帧位、两席初态、原始轨迹哈希及脚本退出标记。 [原引擎元数据](../local-mugen/stage26-native-body/results/run-metadata.json)

## 源定义

- `Terry.cmd:1627–1659` 的站轻脚为无下方向的物理 `a` 新边沿或 Helper 99999 `var(1)>0`。同一条件先检查 `p2bodydist X <= 28` 的 235，之后才评估 230。`Helper.cns:2314–2322` 将 a 锁存为 3 并在自身时钟衰减，根角色可读取之后两 tick 的有效缓冲。原版 P1／P2 a 为 Z／F，NewKOF P1 为 U，P2 为 KP4 或 `/`。
- `Terry-N.cns:503–596` 定义 230，`602–695` 定义 235。两者类型 S、初始 MoveType A、物理 N，入口停止移动并朝向对手。第四 AIR 元素切至 MoveType I，`AnimTime=0` 自然退到可控 State 0。
- `Terry.air:684–710` 的 Action 230 有 6 元素，持续 `2／3／3／3／4／4`，即 `5／3／11`，生效旧 tick 5–7。局部攻击框 X36–107、Y49–72，生效段扩展腿部受击框 X16–103、Y57–96。`712–734` 的 Action 235 有 5 元素，持续 `1／3／5／3／4`，即 `4／5／7`，生效旧 tick 4–8。攻击框 X17–62、Y17–75，第三元素身体受击框 X−20–19、Y1–88，头部受击框 X−8–15、Y81–104。框坐标以 Y 向上列出。
- 两招各一段 `S,NA`，`guardflag=M`、`hitflag=MAF`，站防和蹲防均合法。地面反应为 Low／Light，原始 `ground.hittime` 和 `slidetime` 均为 9，标准推力 6，`var(40)` 有效时为 6.5。HitDef 要求第三元素、`var(12)=0`、没有接触或被反击。公式 `floor((ifelse(var(6),6,5)-var(21))*fvar(7))`，零削血，后接原版攻防倍率与整数结算。命中停顿 `11+Helper(40000).var(0)／13`，格挡 `11+Helper(40000).var(0)／11`。种子 1337 的站姿基本样本两招各扣 30 HP。标准受击地面有效硬直 11，格挡 10。站姿反应使用 5010／5015，蹲姿使用 5020／5025。235 在 F9 接触后目标 F33 命中恢复或 F30 格挡恢复，230 在 F10 接触后对应 F34／F31。
- `Terry-2-3.cns:52–98` 的 `var(1)` 表允许 235 在第三元素已经过首 tick 且尚未进入第四元素时直接取消，旧 tick **5–8**。230 没有显式许可。两个动作的 `var(2)=1` 自然许可分别在旧 tick **19／16**。默认的 State 1000 与模式 2 的 1050 由各自 CMD 条件选择。

## 选招和已实现普通技衔接

- 自由地面、自然恢复和已许可的普通技衔接共用身体距离判定。固定 IKEMEN `char.go:9269–9300` 按朝向比较两侧身体边缘，MUGEN 1.0 保留小数。原包 `Constants.cns` 的地面前／后宽为 15／16，空中为 11／12。两名相向的地面 Terry 的中心距 57.5／58／58.5，对手在空中时的中心距 53.5／54／54.5，分别实测身体距 27.5／28／28.5，选择 235／235／230。地面等号在镜像、P2 和蹲姿对手对照中也选择 235。 [地面阈值](../local-mugen/stage26-native-body/results) · [空中身体距](../local-mugen/stage26-native-air-boundary/results)
- 200／205 的旧 tick 7–11、400 的旧 tick 6–12、430 的旧 tick 6–13 读取无下 a 后按距离选 235／230。235 的旧 tick 9–15 可按 x 接 205／200 或下＋x 接 400，按 a 接 235／230 或下＋a 接 430。235 自连重建新实例，230 的普通技提前出边不在原版许可表中。这些普通技入口没有命中确认条件。
- `buffer-cross-toward` 在 F10 的 a 边沿处身体距 28.5，F11 以 Helper A=2 读取身体距 25，选 235。`buffer-cross-away` 在同样边沿和消费帧从 27.5 移至 31，选 230。消费时持下的对照均走 430。两组使用原版跳跃移动穿越阈值。完整通用空战与跑步、245、300、310、1050 本体仍在延期范围。
- `recovery-backstep-distance` 起始中心距 34，F5 的 430 命中。F32 P2 恢复可控，保持后的当帧没有位移。F33 P1 以实际身体距 26.065239 选 235，P2 随后后退 3.16796875。阶段 25 的提前一步会使距离变为 29.233208，误选 230。`recovery-backstep-own-kick` 则在 F32 由正在结束受击的 P2 同帧出招。原版先推进可行动对手的位移，身体距从帧开始的 29.233208 变为消费时的 26.065239，再选 235。固定 IKEMEN `char.go:13947–14025` 在角色阶段按攻击、待机、受击的优先级推进。两个样本的接触、扣血和碰撞结算仍保持既有架构。 [原版边界轨迹](../local-mugen/stage26-native-body/results)
- 地面 `sweep-buffer-ground-cross` 用 440 空挥的旧 tick 39 缓冲 a，P2 的真实步行使身体距从 F44 的 31.054688 降到 F45 的 27.886719，旧 tick 40 进入 235。同场新按的 b 保持独立，F46–48 的 Helper B 分别为 2／1／0。边界对照 F45 身体距 29.136719 回落 230。 [原版地面缓冲轨迹](../local-mugen/stage26-native-walk/results)
- 200／205 的命中衔接在 F23／F27 读取旧 tick 7／11。400、430 命中后的首帧 F22 旧 tick 6 都能选 235，末帧分别为 F28 旧 tick 12 和 F29 旧 tick 13，距离增大后选 230。235 空挥样本旧 tick 6 的提前输入过期，旧 tick 8 的有效边沿延迟至 9 消费。此时仍按下进入 400／430，按键后松下则选择站姿对应技。旧 tick 15 是最后的提前衔接，旧 tick 16 改读自然许可。`235→230` 在命中和格挡均观察到旧 tick 9／15 的实例，230 动作中提前输入无法直接衔接。 [普通技地面证据](../local-mugen/stage26-native-ground-routes/results) · [窗口对照](../local-mugen/stage26-native-routes/results)

## 特殊技与格挡连续性

- 235 的默认弱 Wave 显式窗口允许命中、格挡和空挥。有效手势可在接触冻结期间进入 Helper，恢复到旧 tick 5 时提交。模式 2 也在此窗口由同一物理手势进入原版 1050，NewKOF 保留该候选，阶段范围只运行默认 State 1000 与 Helper 1020。
- 235 的窗口结束后，有效 x 可按 235 的普通技窗口转入 200／205／400。格挡样本在 F25 旧 tick 9 转 200，保持 x 在 F29 由 200 自身旧 tick 4 进入 1000。地面对照先由 440 在 F15 扫倒对手，F86 开始 235，F95 在旧 tick 9 以实际身体距 13.46875 转 205。目标持续为接地倒地，235 与 205 都未接触。保持 x 的样本 F99 由 205 自身旧 tick 4、Helper Wave 计数 8 进入 1000，Wave F115 命中。松键样本的 205 于 F107 自然退出。前下＋x 对照在 F24 转 400，F28 由 400 自身旧 tick 4 进入 1000。三条松键对照均无后继 Wave。230 的早期手势可过期，F23 手势的 Helper 能在 F24 的旧 tick 19 自然许可提交。F25 新手势属于恢复后的正常启动。 [200 对照](../local-mugen/stage26-native-ground/results) · [205 地面对照](../local-mugen/stage26-native-grounded-205-final/results) · [400 对照](../local-mugen/stage26-native-ground-routes/results)
- 上述 205 地面样本中，NewKOF 在 440 自然恢复边界少走一步。F95 身体距为 16.636719，因而选 200，原版身体距 13.46875 选 205。这处差异针对 205 的阈值 16，单列保留。补充对照把前行输入延至 F80，并把 235 和 236 手势各移后一帧。两边在 F96 都选 205，原版身体距 10.300781，NewKOF 为 13.46875。Wave 出生位置仍相差同一个步长 3.16796875。随后用同一组 F57–80 物理输入在两边等待 440 恢复后再前行。原版 F96 身体距 13.46875 选 205，持 x 在 F100 进入 1000、F116 命中，松键在 F108 自然退出。 [阈值余量对照](../local-mugen/stage26-native-grounded-205-margin-final/results) · [恢复后同步前行对照](../local-mugen/stage26-native-grounded-205-aligned/results) · [两场断言结果](../local-mugen/stage26-native-grounded-205-aligned-check.log)
- `Terry.cmd:1538–1560` 在特殊技之后、普通技之前检查 300 和 310。235 旧 tick 5–8 的显式许可同样适用。前＋x、前下＋y 的组合可由 `Helper.cns:2204–2222` 的 `var(40)／var(41)` 锁存 4，并以每 tick 1 衰减。旧 tick 4 输入后松开方向，旧 tick 5 可使用 Helper 进入 300／310。F11 的 HitPause 内输入也可在 F21 解冻时提交，届时物理方向与 Helper 已清空，根命令仍由冻结的 `curbuftime` 保留。四个额外样本将方向和按键分别安排在 F11 与 F21，两种输入顺序都在 F21 进入对应的 300／310。原版根命令独立缓冲按键与持方向，并在消费时组合。230 没有这两个动作中的许可。阶段 26 保留输入，不执行延期招式。 [同帧冻结输入](../local-mugen/stage26-native-frozen/results) · [分离输入](../local-mugen/stage26-native-split-probe/results)
- 接触当帧的单独前方向不会进入冻结缓冲。中心距 50、P1 在 F5 按 a、F9 只按前、F21 只按 x 的补充对照中，F9 命令阶段尚未进入 HitPause，F10–20 均为中立。F21 解冻后仍维持 235，F32 自然退出。 [接触边界对照](../local-mugen/stage26-native-contact-direction/results)
- `Terry.cmd:246–249,1392–1402` 的默认移动预设把同帧站姿 a+x 解释为回避 720。可控地面与 `var(2)` 自然恢复具有该许可，235 旧 tick 9 的普通技提前衔接没有这项许可。后一情况下站立 x 胜过 a。合法 Wave 优先于回避。720 本体延期，阶段 26 保留其命令占用。 [原版混合输入轨迹](../local-mugen/stage26-native-competition/results)
- 235 在蹲防硬直中重进，第二个实例在 F29 独立格挡且零削血。只松后仍按下，F30 回到 Action 151。松下保留后，F30 回到站防 Action 150。起始中心距 40.25 的地面对照在 F25 从 235 改出 230。F30 的目标已恢复控制，`common_kof.cns:1105–1111,1212–1218` 的 `NotHitBy` 在受击方站／蹲格挡退出时给出一 tick 的 `SCA` 保护。230 在 F31 才接触。保持后仍格挡，松后改为命中 30 HP。两招分别按接触时的状态判定。 [235 连挡](../local-mugen/stage26-native-controls/results) · [230 与格挡退出](../local-mugen/stage26-native-ground-routes/results)

## 实测复现

`Cases.json` 保留实际物理输入，各项是 `[首帧, 末帧, 按键]` 闭区间，重叠项取并集。`Batches.json` 记录本轮各批的场次顺序和采集上限，基础与补测合计 **242/242**。主体上限按批为 F90–140，205 地面对照采集至 F240。模式 2 后继或复杂空中表现超出本阶段时，Godot 比较在相应范围边界结束。阶段 25 的 `near-high-target-jab-to-kick-12` 仍作为旧 205 空中接触兼容差异单列。

```powershell
$batches = Get-Content tools/Stage26Reference/Batches.json -Raw | ConvertFrom-Json -AsHashtable
$refs = @()
foreach ($entry in $batches.GetEnumerator()) {
  $destination = "local-mugen/stage26-native-repro-$($entry.Key)"
  $names = @($entry.Value.cases | ForEach-Object {
    if ($_.StartsWith('mode2:')) { "mode2-standing-light-kick-$($_.Substring(6))" }
    else { "default-standing-light-kick-$_" }
  })
  $engine = if ($entry.Key -eq 'body') { @{Prepare=$true} }
    else { @{PreparedEngineDirectory='local-mugen/stage26-native-repro-body/engine'} }
  & tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage26 @engine `
    -LastFrame $entry.Value.lastFrame -RunDirectory $destination -Cases $names
  $refs += "$destination/results"
}
py tools/Stage26Reference/Check-Traces.py @refs

$waveRecovery = @('near-wave-whiff-9', 'near-wave-whiff-13', `
  'near-to-close-jab-wave-apex-early-held', 'near-to-crouch-jab-wave-free-held') |
  ForEach-Object { "default-standing-light-kick-$_" }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage26 -LastFrame 190 `
  -PreparedEngineDirectory local-mugen/stage26-native-repro-body/engine `
  -RunDirectory local-mugen/stage26-native-repro-wave-extended -Cases $waveRecovery

& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage26 -LastFrame 95 `
  -PreparedEngineDirectory local-mugen/stage26-native-repro-body/engine `
  -RunDirectory local-mugen/stage26-native-repro-contact-direction `
  -Cases default-standing-light-kick-near-frozen-direction-contact-first

$groundedMargin = @('held', 'release') | ForEach-Object {
  "default-standing-light-kick-near-to-close-jab-wave-grounded-margin-$_"
}
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage26 -LastFrame 240 `
  -PreparedEngineDirectory local-mugen/stage26-native-repro-body/engine `
  -RunDirectory local-mugen/stage26-native-repro-grounded-205-margin -Cases $groundedMargin

$groundedAligned = @('held', 'release') | ForEach-Object {
  "default-standing-light-kick-near-to-close-jab-wave-grounded-aligned-$_"
}
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage26 -LastFrame 240 `
  -PreparedEngineDirectory local-mugen/stage26-native-repro-body/engine `
  -RunDirectory local-mugen/stage26-native-repro-grounded-205-aligned -Cases $groundedAligned
```

最终实际目录用 `local-mugen/stage26-native-` 加同名批次。四条 F190 延伸轨迹用于 Vulkan 的完整 Wave 恢复对照，在 242 场原版门禁中按原有身份计数一次。 [逐场状态与断言结果](../local-mugen/stage26-native-canonical-gate.log) · [Wave 延伸轨迹](../local-mugen/stage26-native-wave-extended/results)
