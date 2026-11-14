# 阶段 24 原引擎参考

使用固定 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`、原始 Terry-KOF98 包和种子 1337。F 是原版 RoundState 2 帧，动作 tick 从零起算。捕获脚本注入物理输入并设置双方起始间距。状态、碰撞、动作、RNG、暂停及实体记录均读取原引擎。

## 源定义

- `Terry.cmd:1734–1744` 定义人体蹲重脚。CMD 要求下方向、`b` 或 Helper 99999 的 `var(3)>0`、非空中、对战中、人体模式，及可控状态、跑步 State 100／101 或 `var(2)>=1`。固定 IKEMEN 配置 P1 的 `b` 是物理 X，P2 的 `b` 是物理 G。注入 `IB_B` 为 32。NewKOF 使用现有 HeavyKick，P1 为 I，P2 为小键盘 5 或分号。
- `Terry-N.cns:1247–1346` 绑定 Action 440，蹲姿、无自身位移、进入时面对对手。AIR 时长 2／4／4／8／8／7／7，起手 10、生效 8、收招 22。单段 Clsn1 在第四元素，本地 X24–95、Y29–52。攻击属性 `C,NA`、`guardflag=L`、`hitflag=MAF`。除生效帧外受击框为 X−12–27、Y1–72，生效帧使用 X−16–31、Y1–56 及 X8–79、Y13–52 两个矩形。
- HitDef 基础伤害 `floor((ifelse(var(6),18,16)-var(21))*fvar(7))`，普通 16、反击 18，格挡零削血。原脚本地面 Trip，默认初速 X−2／Y−8、重力 .5、落地反弹 Y−3。`var(40)` 可额外补正初速、格挡推力等参数。默认格挡推力 10.5，停顿命中 11／13、格挡 11／11，攻击方数值还受 Helper 40000 `var(0)` 影响。自然退出至可控 State 11。
- `Terry-2-3.cns:68` 的 `var(1)` 以 `AnimElemTime(4)>0 && AnimElemTime(5)<0` 许可取消，静态窗口为旧 tick 11–17。弱 Power Wave 根指令或 Helper 99999 的 `var(10)>0` 是 CMD 输入来源。旧 tick 40 的自然退出入口单独读取 `var(2)`。
- State 5070 震停，State 5071 按 HitDef 速度和重力滑出，使用 Action 5070。`common_kof.cns` 关闭了通用空中 5200／5210 受身控制器。原版 State 5100 则可用 `fall.recover` 和 `var(4)` 转至 State 5201。Terry 的 `x+a` 或恢复 Helper 设置 `var(4)=6`，按本地计时衰减。State 5201 使用 28 tick 的 Action 5200、初速 −8、.85 衰减、第五元素停止，并设置打击无敌及关闭角色推挤。

## 本体与完整恢复

- 标准站姿、蹲姿、站防尝试及镜像均在 F15 命中。规范样本实扣 115，命中 11／13 tick 停顿。目标到 5070，F16 首次显示 Action 5070。F29 进入 5071，同帧水平前移 2、Y 从 0 到 −7.5。F59 落至 Y0，F60 提交 5100，F64 反弹，F74 倒地冲击、F77 转平躺、F87 起身、F112 恢复控制。规范目标 X27→89→102.5，镜像同刻反号。
- 蹲防合法，F15 零削血、11／11 停顿，F27 首次水平移动 10.5，蹲防惯性 .82。F44 恢复控制时 X83.33462。按后站防则同 F15 进入 Trip，伤害与无防样本相同。无接触时攻击者 F45 回到 State 11；有接触时 F56 回到 11。205 在 F6 打断 440 的样本，440 无接触，目标 F30 恢复可控蹲姿。
- 接地受身 `x+a` 的 F55 短按或持续按到 F67 均失败。F56／57 短按和 F60 当帧短按成功，F61 按键过晚。成功在 F60 先经过 5100 内部入口再同帧提交 5201／Action 5200，首帧 X89→97。逐帧按 .85 衰减，到 F76 停于 X138.37329，F88 恢复控制。失败沿普通倒地至 F112。

## 初始竞争与默认取消

- F5 原始输入 Down＋`a+b` 进入 430，Down＋`x+b` 进入 400，Down＋`y+b` 进入 410，DownForward＋`y+b` 进入 310。单独前下或后下加 `b` 均进入 440。站立单按 `b` 在中心距 54 进入 245。起跳后 Down＋`b` 进入空中 State 55，Down＋`a` 单独进入 430。模式 2 对照同样使用 440 本体。
- 默认 `hit-27` 在 F27 的旧 tick 11 从 440 进入 1000，`var(1)=1`，根手势有效且 HitPause 已解除。F33 的旧 tick 17 手势也成立，F34 的旧 tick 18 手势拒绝且无普通技回落。F19 在冻结中完成手势，短按及持键版本都到 F27 才提交，根命令仍有效，Helper 值分别为 −1 和 4。相同物理手势在模式 2 的 F27 进入 1050。
- 距离 150 空挥样本 F12 的旧 tick 7 短按过期，F13／14／15 的旧 tick 8／9／10 短按由 Helper 保留至 F16 旧 tick 11，计数为 3／7／11。F16／17／22 的直接手势有效，F23 的旧 tick 18 拒绝。F45 旧 tick 40 的新手势使用自然恢复许可 `var(2)=1`。旧 tick 18 重按 `b` 也拒绝。自然恢复当帧下＋`b` 会再次进入 440。F16 的前＋`x+b` 或前＋`x+y+b` 均由 Wave 优先。

## Wave 后继接触

- `Terry-2-3.cns:488–534` 在攻击确认且目标处于空中受击状态时，根据 `NumTarget(20)` 将 `var(13)` 设为 1。其后的控制器使 `var(12)=1`。规范 Trip 样本在 F18–60 持续锁存，目标 F60 进入地面 5100，F61 复位。弱 Wave 的早、晚段 HitDef 都要求 `Root,var(12)=0`。因此 440 命中取消后，Helper 仍出生、移动和消失，AIR 可有 Clsn1，活动期实际 `HitDef.attr=0`。`hit-27` Helper F42 出生、F67 消失，`hit-33` F48 出生、F73 消失。两者目标均只有 F15 的 440 命中并在 F112 恢复。
- `crouch-guard-27` 先于 F15 蹲防，F27 取消，Helper F42 出生。F45 在目标 X83.33462 时再次蹲防，削血 9，F74 恢复控制。距离 150 的空挥 `whiff-16` 没有 Trip 锁存，Helper F31 出生，F40 晚段地面命中，F41 显示 5012，F54 开始滑动，F59 转 5017，F81 控制恢复。F55 首次全局 Pause 冻结实体本地年龄 24，F57 销毁。
- 混合 `mixed-27` 的 P1 在 F27 从 440 取消，P2 恢复控制后在 F117 从 400 取消。两枚 Helper 各自出生、老化及销毁，后者在 F138 命中 P1。在场 Helper 可在 F25／29 打断 440，场景保留真正的远程接触及双方恢复。
- 同帧对照从中心距 116 起步。两场对手都在 F16 前移至 X54.83203。`edge-0` 该帧被 440 命中并于 F113 恢复控制。`edge-16` 同帧进入 1000，旧 440 接触未结算，Wave F31 出生，F34 命中并使对方击飞，F140 恢复控制。
- `incoming-wave-16` 中 P2 F5 施放，Helper F20 出生。P1 F16 合法取消，F28 在自己的 Helper 出生前被 P2 打断。中心距 180 的 `incoming-wave-22` 保留两枚同时存续的 Helper，P2 F20、P1 F37 出生，P1 的 Wave F46 晚段命中。初始站立 `b` 后接 Down，或同帧 Up＋`b`，原版均进入 State 245，之后没有补发 440。

## 复现

```powershell
$primary = @('hit-0','crouch-hit-0','crouch-guard-0','guard-0','mirror-hit-0','whiff-0','interrupt-0',
  'hit-27','tech-55','tech-56','tech-57') | ForEach-Object { "default-crouch-kick-wave-$_" }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage24 -Prepare -LastFrame 185 `
  -RunDirectory local-mugen/stage24-reference -Cases $primary
$controls = @('tech-held-55','tech-60','tech-61','down-light-kicks-0','down-light-and-kick-0',
  'down-heavy-and-kick-0','down-forward-heavy-and-kick-0') | ForEach-Object { "default-crouch-kick-wave-$_" }
$controls += @('mode2-crouch-kick-wave-hit-0','mode2-crouch-kick-wave-hit-27')
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage24 -LastFrame 185 `
  -RunDirectory local-mugen/stage24-reference-controls -PreparedEngineDirectory local-mugen/stage24-reference/engine `
  -Cases $controls
$routes = @('held-down-0','stand-0','air-0','light-kick-0','down-forward-kick-0','down-back-kick-0','hit-33','hit-34',
  'hit-held-19','hit-19','crouch-guard-27','guard-27','crouch-hit-27','mirror-hit-27','whiff-12','whiff-13','whiff-14',
  'whiff-15','whiff-16','whiff-17','whiff-22','whiff-23','whiff-45','repeat-kick-23','repeat-kick-45','compete-kick-16',
  'compete-heavy-16','approach-0','approach-16','mixed-27','incoming-wave-26','incoming-wave-34','incoming-wave-35') |
  ForEach-Object { "default-crouch-kick-wave-$_" }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage24 -LastFrame 185 `
  -RunDirectory local-mugen/stage24-reference-routes -PreparedEngineDirectory local-mugen/stage24-reference/engine `
  -Cases $routes
$bounds = @('stand-to-down-0','jump-and-kick-0','edge-0','edge-16','incoming-wave-16','incoming-wave-22') |
  ForEach-Object { "default-crouch-kick-wave-$_" }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage24 -LastFrame 185 `
  -RunDirectory local-mugen/stage24-reference-boundaries -PreparedEngineDirectory local-mugen/stage24-reference/engine `
  -Cases $bounds
py tools/Stage24Reference/Check-Traces.py local-mugen/stage24-reference/results local-mugen/stage24-reference-controls/results local-mugen/stage24-reference-routes/results local-mugen/stage24-reference-boundaries/results
py tools/Stage24Reference/Check-Traces.py local-mugen/stage24-reference/results --extract-body local-mugen/stage24-reference-body/results
```

独立本体十条轨迹由主运行原样提取，保留逐场哈希。主、控制与取消三批共 53 条原版轨迹。另有六条补充边界取证使用独立目录，实际由增加物理边沿夹具的同一固定版本二进制采集。每批元数据保存其二进制哈希。上面的新运行统一采用包含全部夹具的版本。
