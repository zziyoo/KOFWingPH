# 阶段 20 近身轻拳 Power Wave 取证

在项目根目录使用 Godot 4.7.2 Mono/Vulkan Forward+。先采集[原引擎轨迹](../../docs/STAGE20_REFERENCE.md)，按[导入命令](../../docs/MUGEN_IMPORT.md)生成两个预设及安装视觉别名。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$native = 'local-mugen/stage20-reference/results'
$mixed = 'local-mugen/stage20-reference-mixed/results'
$terminal = 'local-mugen/stage20-reference-terminal/results'
$run = 'local-mugen/stage20-validation/replay'
$meta = Get-Content "$native/run-metadata.json" -Raw | ConvertFrom-Json
$defaults = @($meta.executed_cases | Where-Object { $_ -like 'default-near-wave-*' } |
    ForEach-Object { '--scene'; $_.Substring('default-near-wave-'.Length) })

py tools/Stage20Validation/near_wave_duel.py --godot $godot --reference $native --reference $mixed `
    --output "$run/fixed" --compare-plain @defaults --scene mixed-sequential --scene mirror-mixed-sequential
py tools/Stage20Validation/near_wave_duel.py --godot $godot --reference $native `
    --output "$run/mode2" --compare-plain --mode mode2 --scene hit-20 --scene reentry-23
py tools/Stage20Validation/near_wave_duel.py --godot $godot --reference $mixed `
    --output "$run/seed-repeat" --scene mixed-sequential --repeat-from "$run/fixed"
py tools/Stage20Validation/near_wave_duel.py --godot $godot --reference $terminal `
    --output "$run/terminal" --compare-plain --scene whiff-terminal-heavy-light-25 --scene whiff-terminal-heavy-wave-25
py tools/Stage20Validation/stage19_authority.py --godot $godot --output "$run/stage19"
py tools/Stage20Validation/reentry_controls.py --godot $godot --output "$run/reentry-controls"

$live = @('--godot',$godot,'--reference',$native,'--scene','hit-held-11','--disable-vsync',
    '--realtime-resolution','320x180','--realtime-warmup-frames','150','--require-no-backlog')
py tools/Stage20Validation/near_wave_duel.py @live --output "$run/real30" --fps 30 --realtime-window 214 220
py tools/Stage20Validation/near_wave_duel.py @live --output "$run/real60" --fps 60 --realtime-window 224 230
py tools/Stage20Validation/near_wave_duel.py @live --output "$run/real30-sparse" --fps 30 --realtime-sparse-audit --realtime-window 216 218
py tools/Stage20Validation/near_wave_duel.py @live --output "$run/real60-sparse" --fps 60 --realtime-sparse-audit --realtime-window 230 232

dotnet build NewKOF.csproj -c Debug --no-restore
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj --no-restore --filter 'FullyQualifiedName~MultiSourceCancelTests|FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~ComboCancelTests|FullyQualifiedName~ScriptedPauseTests|FullyQualifiedName~ProjectileTimelineTests|FullyQualifiedName~DynamicDamageTests'
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj --no-restore
py -m unittest discover -s tools/MugenImporter/tests
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

固定帧使用 `logic = native frame + 14`。图像来自 `FramePostDraw` 的提交版 `state = logic + 1`。核对按键物理边沿、双方 State 200／205、原动作本地 tick、新实例清理、HitPause、实体年龄／镜像／位置、命中或格挡、全局 Pause 及视觉开关的权威日志一致性。常规案例保存完整动作与实体序列，较长的双席混合样本采样第二席从远身轻拳到实体消失的区间，同时对两席完整权威序列进行比较。

- 跳跃空挥。原版目标于 F0 轻点跳跃。NewKOF 短跳在旧轻拳接触帧位置较低。Godot 重放把防守方跳跃调整到渲染 R17 按下／R23 松开，保留进攻方的原始按键和动作帧。`whiff-16/17` 的原版 Wave 接触时目标尚在空中，Godot 在接触时刚落地。`whiff-18/19` 的双方 Wave 接触均发生在地面，实体与 Pause 逐帧对照。
- 同按键回落。`whiff-20` 同为 205→205 且无 Wave，第二轻拳因跳跃轨迹在原版 F23 接触、Godot L40 接触。`hit-23` 同为 205→205，第二轻拳接触帧一致。`hit-far-23` 同为 205→200，Godot 通用受击框让第二拳提前一 tick。`guard-23` 与 `guard-far-23` 同为 205→200，保留阶段 18 的独立实例续防限制，原版第二拳格挡而 Godot L40 命中。这些差异按场在 `trace.json` 的 `compatibility` 明列。
- 双席。`dual-20` 和镜像样本是原版双席 205 相打后无法取消的负例。`mixed-sequential` 和镜像在同一对局先由第一席 205→1000，再由另一席 200→1000。两个 Helper 保留各自的 ID、owner、位置、朝向和结束条件。
- 模式 2。原版 QCF 进入 1050。Godot 控制配置保持已有正常技和 205→205 重进，并验证 1000 或 Helper 1020 均未误触发。1050 后续原版接触只留在原始轨迹。

`fixed-final`、`fixed-mode2` 与 `terminal-priority` 共保存 35 场固定帧双跑、4,059 个逻辑帧、38,690 条逐行一致权威记录和 2,688 张 `FramePostDraw` 图像。两个路径在同一对局实跑的 `seed-repeat` 另外逐行复核 1,903 条权威日志。阶段 19 的当前二进制回放保存于 `local-mugen/stage20-history/stage19`，10 场代表案例、1,207 个逻辑帧、11,812 条权威记录逐行与原日志一致。共享验证器的原阶段 19 CLI 另复验了 `guard-20` 的 1,114 条记录。

`reentry-controls` 与 `reentry-down-controls` 另有六场，两套预设各验证 200→205、200→200 及下＋轻拳无站轻拳补发。六场含 564 个逻辑帧、4,512 条权威记录和 126 张 `FramePostDraw` 图像。

阶段 13C–18 使用[冻结权威日志复现命令](../Stage19Validation/README.md)，阶段 19 使用上面的 `stage19_authority.py`。本轮原始回归日志保存在 `local-mugen/stage20-history`。

实时重放调整方向输入间隔，保留物理输入顺序。在两个帧率上限下均验证了 205 命中、HitPause、旧 tick 4 取消、新 1000 从 tick 0 播放、实际 Wave 命中以及七张状态有序的 Vulkan 图像。`real30-compact-v1` 在图像采样前后报告 24／29／30 FPS，R214–220 对应状态 53／55／57／60／63／65／68。`real60-compact-v1` 预热报告 60 FPS，活动对局报告 30–36 FPS，R224–230 对应状态 73／75／77／79／81／82／84。两个样本均无积压警告。60 FPS 是该运行的上限，实际活动采样未持续达到该值。

稀疏审计关闭逐精灵日志，以三次 `FramePostDraw` 图像关联同次权威状态。`real30-sparse-final` 的 R216–218 对应状态 48／50／51，Godot 在采样两侧均报告 30 FPS。`real60-sparse-final` 的 R230–232 对应状态 39／40／41，采样前报告 60 FPS，采样后报告 58 FPS。两场都验证了 11 个 HitPause tick 后进入 1000、同一 1020 的身份及寿命顺序和实际命中。两场均无积压警告。实时对照按实际逻辑版本关联图像，重复执行的渲染与逻辑版本采样可不同。
