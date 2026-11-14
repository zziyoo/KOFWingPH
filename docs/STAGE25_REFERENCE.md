# 阶段 25 原引擎参考

使用固定 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`、原始 Terry-KOF98 包和种子 1337。F 为 RoundState 2 的原版全局帧。旧 tick 使用 State −1 选招时读取的 `stateTime`，不把 HitPause 期间的全局帧数加进动作时钟。轨迹同时保留命令前、命令后、碰撞后、帧结束和 State 切换的记录。状态入口的同帧逻辑结算与动画缓存推进分开看。

## 固定输入与源定义

- 原包 `local-mugen/terry.zip` SHA-256 为 `299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698`。运行元数据记录固定引擎版本、可执行文件、配置及 `Terry.cmd`、`Terry-N.cns`、`Terry.air`、`Terry-2-3.cns`、`Helper.cns`、`Terry-S.cns`、`Constants.cns`、`common_kof.cns` 的 SHA-256。每场元数据另存初距、输入排程及轨迹哈希。P1/P2 在 F0 居于 X ± 初距/2，镜像场交换朝向，双方初速为零。模式 2 由原版入场 `Grant` 输入进入。
- `Terry.cmd:297–319` 将 a／b／x／y 定义为 1 tick 独立边沿命令。固定原引擎配置，P1 x／y／a／b 是 A／S／Z／X，P2 是 R／T／F／G。NewKOF `scripts/KeyboardInputMap.cs` 复用 J／K／U／I，以及小键盘 1／2／4／5 或逗号／句点／斜杠／分号。
- `Helper.cns:1911` 的 Helper 99999 用于命令缓冲。`Terry-2-3.cns:186–198` 以 `keyctrl=1`、无限 `pausemovetime`／`supermovetime` 创建它。`Helper.cns:2314–2322` 每次 a 命令设置 `var(1)=3`，随后减一。`2325–2332` 对 x 执行同样的 `var(0)` 规则。`Terry.cmd:1724–1725` 读取根 a 或 Helper `var(1)>0`，再独立读取当前下方向。
- `Terry-N.cns:1138–1231` 绑定 Action 430，类型 C，MoveType 初为 A、第四元素改 I，物理 N，速度清零、朝向对手，AnimTime=0 回到可控 State 11。原版挥动／命中声音和量表控制器位于同一段。`Terry.air:1168–1190` 六元素时长 1／2／3／3／2／3、图号 0／1／2／3／1／0，所有偏移为零。第三元素的攻击框 X0–63、Y1–16。各元素基础受击框 X−20–19、Y1–72，第三元素另有 X−12–51、Y1–24，第四元素另有 X12–43、Y1–32。
- HitDef 只在第三元素期间、`var(12)=0`、未接触且未被反击时启用。属性 `C,NA`，`guardflag=L`、`hitflag=MA`、ID 10。地面反应 Low/Light，滑行和硬直原始参数均 9，水平速度 −6（`var(40)` 有效时 −6.5），墙推为 0。命中停顿 11 加 Helper 40000 的 `var(0)`／13，格挡 11 加同一修正／11。空中反应 High/Back，速度 −4.5／−6.5、硬直 12、重力 .5，`var(40)` 有效时两轴各再减 .5。原脚本指定反弹 Y−3.5，未设置强制倒地。反击条件为 `var(6)>0`，基础伤害公式 `floor((ifelse(var(6),8,7)-var(21))*fvar(7))`，零削血。State −2 的反击年龄与伤害变量见 `Terry-2-3.cns:422–438` 和阶段 17 参考。不得从 440 推导 Trip 或 MAF。
- `Terry.cmd:1141–1152` 弱 Power Wave 由根 `236+x` 或 Helper `var(10)>0` 读取，模式 2 走独立 1050 入口。`Terry-2-3.cns:52–78` 的 `var(1)` 许可表未列 430，`83–98` 的 `var(2)=1` 自然退出许可表明确列 430。其他候选依 CMD 实际排序。300 在 Wave 之后、普通技之前，要求非下的前＋x 或 Helper `var(40)>0`，且只允许常规控制权或 `var(1)=1`。

## 状态选择

| 类型 | 来源 → 目标 | 静态窗口与输入 |
|---|---|---|
| 入口 | 可行动地面、State 100／101 → 430 | 人体、RoundState 2、非 Helper、非空中。当前按下方向和新 a 或 Helper a 计数。 |
| 提前衔接 | 200／205 → 430 | 第三元素起，旧 tick 7–11。当前按下方向和新 a 或 Helper a 计数。 |
| 提前衔接 | 400 → 430 | 第三元素第 1 tick 起，旧 tick 6–12。同上。 |
| 提前衔接 | 430 → 430／400／205／200 | 第四元素起，旧 tick 6–13。a＋下为 430，x＋下为 400，无下 x 按 `p2bodydist X <=16` 选 205／200。 |
| 延期 | 235 → 430、430 → 235／230 | 235 第四元素起，430 第四元素起。站立轻脚读取无下 a，近身阈值 `p2bodydist X <=28`。 |
| 自然结束 | 430 → 11 或一般地面候选 | 旧 tick 14，AnimTime=0 时 `var(2)=1`。提前衔接和自然结束共用同一套当前方向判断，但自然结束开放一般控制权候选。 |
| 特殊技 | 430 → 1000／1050 | `var(1)=0`，动作中没有直接许可。旧 tick 14 的 `var(2)=1` 为自然恢复许可。 |

## 复现

`tools/Stage25Reference/Cases.json` 记录逐场输入。每项为闭区间 [首帧, 末帧, 输入]，重叠项取并集。D／U 是下／上，F／B 是初始朝向的前／后，a／b／x／y 保留原包独立物理输入位。除起始位置、模式输入和固定 RNG 外，调试钩子不注入 State、碰撞或生命。

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage25 -Prepare -LastFrame 85 -RunDirectory local-mugen/stage25-native-repro
py tools/Stage25Reference/Check-Traces.py local-mugen/stage25-native-repro/results

# Extend the hit → natural Wave case through the target's full recovery.
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage25 -LastFrame 145 `
  -RunDirectory local-mugen/stage25-native-wave-repro `
  -PreparedEngineDirectory local-mugen/stage25-native-repro/engine `
  -Cases default-crouch-light-kick-hit-wave-30
```

实际采集为 `local-mugen/stage25-native-final-{primary,boundaries,controls,near,high,close,startup}/results` 七批，各批 33／24／48／12／10／4／1 场，共 132 场，全部 F0–85。对七个结果目录一起执行检查器，退出 0、132/132，通过日志为 `local-mugen/stage25-native-final-check.log`。原引擎可执行文件 SHA-256 为 `e63d55ca8a64f80cd044c0b3b28aadb2f53b4c8773124e15fc3c327669d50729`。独立 F145 补充取证位于 `local-mugen/stage25-native-wave-reaction/results`，只用于延长命中后自然 Wave 的图形对照，132 场检查未重复计数。


## 实测规则与边界

上述普通技衔接无需命中确认。空挥及普通命中／格挡场景均保留真实接触记录。无接触时统一从 F5 入招，`205` 与 `430→205` 近距空挥用 F15 入招和 F0 的 P2 原地跳跃避开接触。近距选招按消费时 `p2bodydist`，包括空中 size。中心距 36 的后者可在 F21／28／29 选 205，中心距 44 的空中对手同边界选 200。F22 的近距地面命中样本 430→205，F25 第二攻击重新结算 29 HP。

- 200、205 的旧 tick 7／11 接受下＋a，400 的旧 tick 6／12 接受下＋a。各自下一 tick 12／12／13 从自然权限进入 430。每个窗口前一 tick 的单帧下＋a 若松下，Helper 在首 tick 仍有计数 2 并选站立 230／235。单独保留下方向则选 430，提早 3 tick 的边沿过期。430 的四条已实现出边旧 tick 6／13 成立，14 走自然许可。旧 tick 5 的 x 可在 6 回落站轻拳，旧 tick 5 的下＋x 释放方向后也回落站轻拳。旧 tick 5 的 a 可经当前下方向进入 430，松下则 230。全程按住 a 不刷新边沿。
- 两席及镜像正常命中 F8，原版 F9 扣 46 HP（种子 1337 样本）。HitDef F8 的基础公式产生 57，再经攻防系数和整数结算。对手 F6 起手 440 时，反击样本 F8 的 `var(6)=1`，因本次 `var(21)` 不同，最终恰同为 46 HP。格挡 F8 零削血，只允许下后，站后试图防御仍命中。标准停顿 11／13 与 11／11，首次水平滑行分别 F22／F20，推力都是 6。站姿 F9 显示 5010，F27 转 5015，F32 可控且 X58.56914。蹲姿使用 5020／5025，F32 可控且 X55.349445。蹲防使用 151，F29 可控且 X54.746014。站姿／蹲姿滑行衰减分别 .85／.82。
- 空挥 F19 旧 tick 14 自然回可控 State 11，命中或格挡时施招者 F30 恢复。P2 F3 近轻拳在 F6 打断 430，430 无接触。先扫倒再接近的对照 F73–75 有 430 HitDef，`var(12)=0`，对方仍为 L，没有第二次接触。空中可接受非倒地目标的静态 MA 规则与倒地排除是独立条件。
- 下＋a+b 选 430，下＋a+x 选 400，下＋a+y 选 410，前下＋a+y 选延期 310。单独前下／后下＋a 均进 430。可控中立先按 a，再按下，先进入原版延期 235。阶段 24 扫脚空挥的旧 tick 39（F44）先按 a，F45 下＋新 b 时 430 胜出；根 a 已失效，Helper a=2。落败 b 保持独立，F46／47／48 的 Helper b 为 2／1／0，430 中无后续 440。F45 释放下则改进延期 230。
- 原版当前默认预设双前进入 110 Front Step，F8 的下＋a 不越过 110 的控制权限制。`Terry.cmd:1500–1521` 的 100 Run Forward 由独立的 ADV／EX 配置 `var(9)` 选择。Helper 7600 可在入场用前／后切换这个配置。430 入口的 100／101 条件来自原脚本，NewKOF 不扩建跑步。模式 2 本体和已实现轻攻击衔接与默认一致，仍隔离 1050。
- HitPause 中，F18 的 a／x 令 F19 的 Helper 计数 2、F20 为 1，F22 旧 tick 6 时已为 0。F19 边沿在 F20／21 为 2／1，也在 F22 过期。F20 边沿在 F22 仍为 1，方向松开时选 235／200。F22 新下＋a／x 直接进入 430／400。命中转 430 再在 F25 命中 44，命中转 400 再在 F25 命中 37。格挡转 430／400 也在 F25 独立判定，格挡转 430 的末端 F29 输入在 F32 独立格挡。

## Power Wave 的三个结果

1. **动作中拒绝**。F7 的有效 `236+x`（旧 tick 2）以及 F10 冻结时完成的有效手势都没有 1000。F7 的 x 也在普通技许可前过期。F9 的短按在 F11 以普通 x 进入 200，F12、F18 的新手势当帧回落 200，F12 消费时仍有下则进入 400。F18 后根手势失效，普通技已提交，Helper 的 Wave 和前＋x 分别经 11／7／3／−1 和 3／2／1／0 过期。自然恢复 F19 若只输入前＋x 而未做 236，则进延期 300。
2. **先衔接再取消**。F11 430→400，F15 400 旧 tick 4 的新手势经 `var(1)=1` 取消到 1000。另一场 F17 430→200，继续按住 x 到 F20，F21 200 旧 tick 4 用仍有效的 Helper Wave 取消到 1000，无新手势。对应松开 x 的 F17 样本只有 430→200。
3. **自然恢复入口**。空挥 F19、命中 F30 的 430 旧 tick 14 由 `var(2)=1` 正常进入 1000，`var(1)=0` 且原状态仍未提交 `ctrl=1`。命中样本 Helper F45 出生、F46 命中。空挥 F19 先前走进入 20，F20 新手势由常规控制权进入 1000。模式 2 同样的 F19 自然输入进延期 1050。
