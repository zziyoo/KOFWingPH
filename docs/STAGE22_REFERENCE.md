# 阶段 22 原引擎参考

使用固定 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`、原始 Terry-KOF98 包、默认种子 1337 和物理按键。`F` 为原引擎 RoundState 2 逻辑帧，动作 tick 从 0 起算。

- `Terry.cmd:1688` 的 State 400 是蹲轻拳，要求当前下方向及 `x` 或 Helper 99999 的普通轻拳缓冲。`Terry-N.cns:936` 为蹲姿攻击，Action 400 的 3／2／3／5 个 tick 对应起手 0–2、生效 3–4、收招 5–12。生效框本地 X 为 12–67、Y 为 33–56。松下后动作继续保持蹲姿，旧 tick 13 进入可控 State 11。继续按下维持 11，松下在下一帧进入 Action 12 的四帧起身。
- 攻击属性 `C,NA`，`guardflag=M` 允许站防、蹲防，首段无削血。原始动态基数为普通 6、反击 7，减 `var(21)` 后乘 `fvar(7)` 并向下取整。标准命中 F8 实扣 38，击退 6，双方停顿 11／13。格挡 F8 实扣 0，击退 6，停顿 11／11。镜像重复同一时序。打断样本中原版 State 205 在 F6 命中蹲姿起手，State 400 未形成接触。
- 默认 `Terry-2-3.cns:66` 的 `var(1)=1` 需要 `AnimElemTime(2)>0 && AnimElemTime(3)<0`，因此显式窗口只有 **旧 tick 4**。`Terry.cmd:1141` 读取弱 Power Wave 根指令或 Helper 的 `var(10)>0`。F20 命中、站防、蹲防样本及 F9 空挥样本均成功，模式 2 的同一输入进入 1050。
- 空挥 F8 的旧 tick 3 已识别指令，F9 根候选过期后由 Helper 缓冲进入 1000。F10 的旧 tick 5 手势无法特殊取消，普通轻拳边沿保留到 F11／旧 tick 6，进入远站轻拳 200。对应命中样本 F21 输入，F22 回落。旧 tick 13 的新 QCF 可由自然恢复许可启动。命中冻结中完成的 F11 持键 QCF 在 F20 解冻后执行。
- F22／23 的下＋轻拳可 400→400。F23 的站轻拳可因下方向进入 400。中立状态直接完成 QCF 选择 1000。双席混合样本先于 F20 从 400 取消，后于 F90 从 200 取消，Helper 独立出生并分别格挡与命中。

26/26 原版轨迹通过校验。原始日志和逐场哈希在 `local-mugen/stage22-reference/results`。空挥使用地面中心距 140，原版与 Godot 可使用相同初始条件和按键。

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage22 -Prepare -LastFrame 165 -RunDirectory local-mugen/stage22-reference-new
py tools/Stage22Reference/Check-Traces.py local-mugen/stage22-reference-new/results
```
