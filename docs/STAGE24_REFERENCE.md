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

- F5 原始输入 Down＋`a+b` 进入 430，Down＋`x+b` 进入 400，Down＋`y+b` 进入 410，DownForward＋`y+b` 进入 310。上述样本均没有进入 440。复现的模式 2 对照同样使用 440 本体。
- 默认 `hit-27` 在 F27 的旧 tick 11 从 440 进入 1000，`var(1)=1`，根手势有效且 HitPause 已解除。Helper 于 F42 出生，在 F67 销毁。目标在 Wave 可接触时仍处于 Trip 且位置和高度脱离 Wave 的有效矩形。唯一伤害为 F15 的 440 命中，目标仍在 F112 恢复控制。相同物理手势在模式 2 的 F27 进入 1050。

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
py tools/Stage24Reference/Check-Traces.py local-mugen/stage24-reference/results local-mugen/stage24-reference-controls/results
py tools/Stage24Reference/Check-Traces.py local-mugen/stage24-reference/results --extract-body local-mugen/stage24-reference-body/results
```
