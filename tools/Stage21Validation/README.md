# 阶段 21 Vulkan 回放

在项目根目录运行。按[导入说明](../../docs/MUGEN_IMPORT.md)生成阶段 21 的两个预设并安装视觉别名。取证使用 Godot 4.7.2 Mono 与 Vulkan Forward+。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$native = 'local-mugen/stage21-reference-final/results'
$mixed = 'local-mugen/stage21-reference-mixed/results'
$release = 'local-mugen/stage21-reference-guard-release/results'
$run = 'local-mugen/stage21-validation/replay'
$defaultCases = @((Get-Content "$native/run-metadata.json" -Raw | ConvertFrom-Json).executed_cases |
  Where-Object { $_ -like 'default-heavy-wave-*' } |
  ForEach-Object { '--scene'; $_.Substring('default-heavy-wave-'.Length) })
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --reference $native --output "$run/fixed" --compare-plain @defaultCases
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --reference $native --output "$run/mode2" --mode mode2 --compare-plain --scene hit-20 --scene hit-none-0
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --reference $mixed --output "$run/mixed" --compare-plain --scene mixed-sequential --scene mirror-mixed-sequential
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --reference $release --output "$run/guard-release" --compare-plain --scene guard-release-20 --scene guard-release-34
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --reference $native --output "$run/seed-repeat" --scene hit-25 --repeat-from "$run/fixed" --fixed-window 98 101

$prior = 'local-mugen/stage20-reference/results'
$priorMixed = 'local-mugen/stage20-reference-mixed/results'
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --reference $prior --reference $priorMixed --output "$run/legacy" --legacy-stage20 --compare-plain --scene mixed-sequential --scene hit-23 --scene hit-far-23
py tools/Stage20Validation/reentry_controls.py --godot $godot --output "$run/reentry" --profile-dir data/imported-characters/terry98-stage21-data --alias-prefix terry98-stage21

$live = @('--godot',$godot,'--reference',$native,'--scene','hit-held-11')
py tools/Stage21Validation/heavy_wave_duel.py @live --output "$run/real30" --fps 30 --realtime-window 216 218
py tools/Stage21Validation/heavy_wave_duel.py @live --output "$run/real60" --fps 60 --realtime-window 230 232
```

固定帧通过真实物理输入驱动对局。使用 `logic = native frame + 14` 和 `state = logic + 1` 对齐同次 `FramePostDraw` 图像。`trace.json` 记录每次转换、取消前的攻击实例、两段接触组、已提交伤害和冻结、Manifest 姿态时钟、Helper 权威位置和寿命以及截图版本。含第一段与第二段的固定样本分别核对原版和 NewKOF 实现的 RNG／伤害操作数，原版目标反应帧扣血与 NewKOF 接触帧原子扣血分别保留。双跑另逐行比较视觉覆盖开启及关闭的权威记录。

原版垂直跳输入只持续 F0。NewKOF 对 `whiff-*` 把防守方跳键改为渲染 R17 按下、R23 松开，以获得足够高度复现空挥。发起方按键与 F→L 对齐保持不变。模式 2 的 QCF 输入在原版进入 1050，Godot 核对共同前缀，随后与原版无手势控制比较 215 的第二段及自然退出，要求不误入 1000 或生成 1020。

冻结权威回归使用[阶段 19 命令](../Stage19Validation/README.md)中的 13C、16、17、18 脚本，并补跑阶段 14／15、19、20。

```powershell
py tools/Stage21Validation/stage14_15_authority.py --godot $godot --output "$run/stage14-15"
py tools/Stage20Validation/stage19_authority.py --godot $godot --output "$run/stage19"
py tools/Stage21Validation/stage20_authority.py --godot $godot --output "$run/stage20"
```

阶段 21 的站防位置误差按 Q16 叠加控制为接触时 0.005、连续两次击退恢复时 0.01。接触帧、攻守结果、削血、停顿和恢复控制时机仍逐项检查。空挥起始样本的 Wave 在两边同一逻辑帧命中，原版目标此时距地 4.640022，Godot 已落地。中后段空挥样本的 Wave 接触在地面。该跳轨差异只用于空挥对照。

两个实时回放在 320×180 下使用稀疏审计，关闭逐精灵打印，每场读回三张图像。方向边沿按真实渲染帧注入并保留顺序，间隔压缩以便在 11 tick HitPause 中完成输入。以同次权威日志定位图像，实时渲染状态可能跳过或重复，验收比较所采到的提交版本、实体身份及寿命。复现时优先独占 GPU，以日志中的 `Project FPS` 和调度积压警告为准。

- `real30-final-v2` 采样前后均实测 30 FPS。R216–218 对应状态 48／50／52，Wave 在 L43 命中。首轮 `real30-final` 有一次调度积压告警，保留失败日志。
- `real60-final` 采样前为 60 FPS，采样后为 59 FPS。R230–232 对应状态 39／40／41，首张图对应 L38 的 Wave 命中。两次成功运行均无积压告警。

`seed-repeat/hit-25` 用同一种子和不同读回窗口独立重放，1,268 条权威记录逐行一致。按 LF 拼接且保留末尾换行的 SHA-256 为 `e1f6d930355d06c4f6e6eee7d63c0f66004ca30fbc2576a9e30557630c9801ed`。R98／99 对应 Helper 年龄 23／24，R100／101 的提交状态 91／92 已无该实体。

取证重点如下，路径以 `local-mugen/stage21-validation` 为根。

- `fixed/hit-22/sprite/frames/render-00045-state-00036.png` 是旧 Action 215 tick 5。随后 R46／S37 显示 Action 1000 tick 0，正好跨越原第二段计划接触的 L36。受击方保留先前实扣 61，无第二段接触、伤害或旧攻击框。R61／S52 显示年龄 0 的新 Helper。
- `fixed/hit-25/sprite/frames/render-00046-state-00037.png` 显示第二段已结算，受击方生命为 910。R58／S49 显示从旧 tick 7 解冻取消后的新 Action 1000。R79／S70 显示 Helper 年龄 6 命中，受击方生命 834。首个全局 Pause 为 L85，与原版 F71 对齐。
- `mixed/mixed-sequential/sprite/frames/render-00144-state-00135.png` 对应 P2 从远轻拳取消所生 Helper 的实际命中。P1 的 215→1000 更早结算，两条路径的实体 ID、归属、位置、寿命分别比较，镜像场也使用同一预设。
