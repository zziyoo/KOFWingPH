# 阶段 23 Godot Vulkan 回放

在项目根目录运行。按[导入说明](../../docs/MUGEN_IMPORT.md#阶段-23-蹲重拳与弱-power-wave)生成正式与本体预设，安装两个 Stage 23 视觉别名，再导入 atlas。原引擎运行和资源来源见[阶段 23 原版参考](../../docs/STAGE23_REFERENCE.md)。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$main = 'local-mugen/stage23-reference/results'
$directions = 'local-mugen/stage23-reference-supplement/results'
$boundaries = 'local-mugen/stage23-reference-boundaries/results'
$priority = 'local-mugen/stage23-reference-priority-v2/results'
$probes = 'local-mugen/stage23-reference-final-probes/results'
$combined = 'local-mugen/stage23-reference-combined-input/results'
$profile = 'data/imported-characters/terry98-stage23-data'
$body = 'data/imported-characters/terry98-stage23-body'
$run = 'D:/NewKOF-stage23-vulkan-replay'
$driver = 'tools/Stage23Validation/crouch_heavy_duel.py'
$common = @('--godot',$godot,'--reference',$priority,'--reference',$main,'--reference',$directions,
  '--reference',$boundaries,'--reference',$probes,'--reference',$combined)

& $godot --headless --path . --editor --import
dotnet build NewKOF.csproj -c Debug --no-restore
$bodyCases = @('hit-0','crouch-hit-0','guard-0','crouch-guard-0','held-down-0','mirror-hit-0',
  'interrupt-0','whiff-0','stand-0','dual-button-0','down-forward-heavy-0','down-forward-both-0','down-back-heavy-0','crouch-hit-release-0')
$bodyArgs = @($bodyCases | ForEach-Object { '--scene'; $_ })
py $driver @common --profile-dir $body --output "$run/body" --compare-plain @bodyArgs

$routeCases = @('hit-22','hit-28','hit-29','hit-43','hit-held-14','hit-14','guard-22',
  'crouch-guard-22','guard-release-22','crouch-guard-release-22','crouch-hit-22','mirror-hit-22','mixed-22','whiff-7','whiff-8','whiff-9','whiff-10','whiff-11',
  'whiff-17','whiff-18','whiff-32','compete-11','incoming-wave-20','incoming-wave-22','incoming-wave-28',
  'approach-0','approach-11')
$routeArgs = @($routeCases | ForEach-Object { '--scene'; $_ })
py $driver @common --profile-dir $profile --output "$run/default" --compare-plain @routeArgs
py $driver @common --profile-dir $profile --output "$run/mode2" --mode mode2 --compare-plain --scene hit-0 --scene hit-22
py $driver @common --profile-dir $profile --output "$run/mode2-shared-input" --mode mode2 `
  --mode2-shared-input --compare-plain --scene down-forward-both-0
py $driver @common --profile-dir $profile --output "$run/repeat" --scene hit-22 --repeat-from "$run/default"
py $driver @common --profile-dir $profile --output "$run/recovery-images" --scene hit-22 --fixed-window 110 171 --repeat-from "$run/default"
py $driver @common --profile-dir $profile --output "$run/live30" --scene hit-held-14 --fps 30 --realtime-window 219 221
py $driver @common --profile-dir $profile --output "$run/live60" --scene hit-held-14 --fps 60 `
  --realtime-input-spacing 2 --realtime-window 238 240

py $driver --godot $godot --reference local-mugen/stage22-reference/results --stage22-light `
  --profile-dir $profile --output "$run/source400" --compare-plain --scene hit-held-11
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --profile-dir $profile --alias-prefix terry98-stage23 `
  --reference local-mugen/stage21-reference-final/results --output "$run/source215" --compare-plain --scene hit-22
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --profile-dir $profile --alias-prefix terry98-stage23 `
  --reference local-mugen/stage20-reference-mixed/results --output "$run/sources200-205" `
  --legacy-stage20 --compare-plain --scene mixed-sequential

& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

固定帧按原引擎物理边沿注入。F 对齐为 Godot 逻辑帧 L=F+14，PNG 标识为提交版本 L+1。完整权威轨迹包含攻击实例、逐帧碰撞、HitPause、全局 Pause、Helper 身份与寿命，以及从地面滑动或击飞到恢复控制。图像读回只覆盖关键区间。`trace.json` 关联实际绘制版本。模式 2 的 QCF 仍作为同输入拒绝探针，原版进入 1050 后另用无手势轨迹核对共同的 State 410 本体。下前＋重拳按相同输入验证保留 310 槽位和拒绝 State 410，310 本体不属于本阶段。模式 2 的额外方向探针复用默认模式的原版输入和公共 CMD 优先级，记录为条件对照。

实时采集应在 GPU 空闲时串行运行。30 档实测 30／30 FPS，R219–221 对应 S54／56／58，Wave 于 L50 命中。60 档实测 57／58 FPS，R238–240 对应 S48／49／50，Wave 于 L45 命中。两场均采集三张 Vulkan 图像并核对提交版本和实体时钟，无帧积压。实时注入以渲染帧为单位，60 档拉开相邻输入间距，保留物理边沿顺序；源招取消分别落在旧 tick 7 与 8，完整同输入逐帧对照以固定帧结果为准。

## 本轮证据

原始输出保存在 D 盘的 `NewKOF-stage23-vulkan-*` 目录，旧来源兼容输出位于 `D:/NewKOF-stage23-coexistence`。每个通过的配对目录内保留 `sprite/trace.json`、`sprite/duel.log`、`plain/duel.log` 及采集图。下列路径均指实际输出。

选定的 47 个固定帧对照共进行 94 次对局，检查 7,898 个逻辑帧、73,151 条完全相同的视觉开关权威记录，保留 2,090 张逐帧图像及 94 张结束截图。 [逐场目录与记录数](EVIDENCE.md)

- `D:/NewKOF-stage23-vulkan-replay-primary/hit-22/sprite/frames/render-00064-state-00055.png` 记录 Wave 命中前一提交版本，下一张 R65／S56 显示命中后的首帧。
- `D:/NewKOF-stage23-vulkan-replay-recovery/hit-22/sprite/frames/render-00116-state-00107.png` 为撞地前一提交版本，R117／S108 为撞地。R145／S136 为起身前一版本，R146／S137 为起身。R170／S161 为恢复控制前一帧，R171／S162 为重新站立。该窗口与首跑的 1,754 条权威记录逐条相同。
- `D:/NewKOF-stage23-vulkan-bounded/mixed-22/sprite/trace.json` 记录同一席位两次击退的定点复算与 P2 Helper 出生坐标。
- `D:/NewKOF-stage23-vulkan-live30/hit-held-14/sprite/frames/render-00219-state-00054.png` 与 `D:/NewKOF-stage23-vulkan-live60/hit-held-14/sprite/frames/render-00238-state-00048.png` 为两档实际实时样本。

阶段 13C–21 权威门禁沿用[阶段 22](../Stage22Validation/README.md)、[阶段 21](../Stage21Validation/README.md)和[阶段 19](../Stage19Validation/README.md)的复现命令。使用以下命令复跑阶段 22 的 29 组视觉开关对照。

```powershell
py tools/Stage23Validation/stage22_authority.py --archive D:/NewKOF-stage22-vulkan --godot $godot --output "$run/stage22-frozen"
```
