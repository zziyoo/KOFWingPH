# 阶段 22 原引擎参考

使用固定 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`、原始 Terry-KOF98 包、默认种子 1337 和物理按键。`F` 为原引擎 RoundState 2 逻辑帧，动作 tick 从 0 起算。

- `Terry.cmd:1688` 的 State 400 是蹲轻拳，要求当前下方向及 `x` 或 Helper 99999 的普通轻拳缓冲。`Terry-N.cns:936` 为蹲姿攻击，Action 400 的 3／2／3／5 个 tick 对应起手 0–2、生效 3–4、收招 5–12。生效框本地 X 为 12–67、Y 为 33–56。松下后动作继续保持蹲姿，旧 tick 13 进入可控 State 11。继续按下维持 11，松下在下一帧进入 Action 12 的四帧起身。
- 攻击属性 `C,NA`，`guardflag=M` 允许站防、蹲防，首段无削血。原始动态基数为普通 6、反击 7，减 `var(21)` 后乘 `fvar(7)` 并向下取整。标准命中 F8 实扣 38，击退 6，双方停顿 11／13。格挡 F8 实扣 0，击退 6，停顿 11／11。镜像重复同一时序。打断样本中原版 State 205 在 F6 命中蹲姿起手，State 400 未形成接触。
- 默认 `Terry-2-3.cns:66` 的 `var(1)=1` 需要 `AnimElemTime(2)>0 && AnimElemTime(3)<0`，因此显式窗口只有 **旧 tick 4**。`Terry.cmd:1141` 读取弱 Power Wave 根指令或 Helper 的 `var(10)>0`。F20 命中、站防、蹲防样本及 F9 空挥样本均成功，模式 2 的同一输入进入 1050。
- 空挥 F8 的旧 tick 3 已识别指令，F9 根候选过期后由 Helper 缓冲进入 1000。F10 的旧 tick 5 手势无法特殊取消，普通轻拳边沿保留到 F11／旧 tick 6，进入远站轻拳 200。对应命中样本 F21 输入，F22 回落。旧 tick 13 的新 QCF 可由自然恢复许可启动。命中冻结中完成的 F11 持键 QCF 在 F20 解冻后执行。
- F22／23 的下＋轻拳可 400→400。F23 的站轻拳可因下方向进入 400。中立状态直接完成 QCF 选择 1000。双席混合样本先于 F20 从 400 取消，后于 F90 从 200 取消，Helper 独立出生并分别格挡与命中。

26/26 原版轨迹通过校验。原始日志和逐场哈希在 `local-mugen/stage22-reference/results`。空挥使用地面中心距 140，原版与 Godot 可使用相同初始条件和按键。

窗口外命中样本 `hit-21` 中，F22 确实进入 200，F25–28 的第二拳没有接触。F23–26 目标使用 Action 5010 的第二元素，身体最高 Y72，另一矩形前缘 X7，避开了第二拳的 X/Y 重叠。F27 改用 Action 5015。蹲轻拳的 CNS 声明 `animtype=Light` 与 `ground.type=Low`，`common_kof.cns` 的 State 5000／5001 选择对应的初始和滑动姿态。

自然退出的 F18／F29 保留可控 State 11。松下之后连续四帧为可控 State 12，对应 Action 12 的 1／3 tick 及两套 Clsn2，然后进入 State 0。

同输入补测中，P2 在 F5 从可控状态进入 1000，F20 生成 Helper。`incoming-wave-25` 的 P1 在 F21 进入 400，F24 旧 tick 3 被已在场的 Helper 命中，F25 的弱 Wave 物理输入被拒绝。原版目标反应帧扣除 99 HP。`incoming-wave-20` 的 P1 在 F20 从旧 tick 4 合法进入 1000，F29 被在场的 Helper 打断，未生成自己的 Helper，原版扣除 107 HP。P2 的 Helper 在两场的 F39 均仍存在。

`near-hit-21` 使用相同手势和初始中心距 36，F22／旧 tick 6 进入近轻拳 205，F25 第二次接触；标准 `hit-21` 使用中心距 54，回落到 200 并且第二次接触落空。与主矩阵合计验证 29 条原引擎轨迹。

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage22 -Prepare -LastFrame 165 -RunDirectory local-mugen/stage22-reference-new
py tools/Stage22Reference/Check-Traces.py local-mugen/stage22-reference-new/results
```
