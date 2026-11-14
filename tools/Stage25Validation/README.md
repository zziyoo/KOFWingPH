# 阶段 25 State 430 Vulkan 回放

从项目根目录运行。先按[原版轨迹](../../docs/STAGE25_REFERENCE.md)与[导入说明](../../docs/MUGEN_IMPORT.md#阶段-25-蹲轻脚与普通技衔接)生成原版批次、`terry98-stage25-final` 和两套视觉别名。Godot 4.7.2 Mono 使用 Vulkan Forward+。固定回放把原版 F 对齐至逻辑 L=F+14，图片由 `FramePostDraw` 保存，对应提交版本 S=L+1。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$run = 'D:/NewKOF-stage25-vulkan-replay'
$driver = 'tools/Stage25Validation/crouch_light_kick_duel.py'
$native = 'local-mugen/stage25-native-final-primary/results'
$common = @('--godot',$godot,'--reference','local-mugen/stage25-native-wave-reaction-control/results',
  '--reference','local-mugen/stage25-native-held-wave-reaction-control/results',
  '--reference','local-mugen/stage25-native-crouch-jab-wave-reaction-control/results',
  '--reference','local-mugen/stage25-native-guard-controls/results')
foreach ($batch in 'primary','boundaries','controls','near','high','close','startup') {
  $common += @('--reference',"local-mugen/stage25-native-final-$batch/results")
}
if (Test-Path 'local-mugen/stage25-native-repro/results/run-metadata.json') {
  $native = 'local-mugen/stage25-native-repro/results'
  $common = @('--godot',$godot,'--reference','local-mugen/stage25-native-wave-reaction-control-repro/results',
    '--reference','local-mugen/stage25-native-held-wave-reaction-control-repro/results',
    '--reference','local-mugen/stage25-native-crouch-jab-wave-reaction-control-repro/results',
    '--reference','local-mugen/stage25-native-guard-controls-repro/results',
    '--reference','local-mugen/stage25-native-motion-controls-repro/results','--reference',$native)
} else {
  $common += @('--reference','local-mugen/stage25-native-motion-controls-through-recovery/results')
}

& $godot --headless --path . --editor --import
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
dotnet build NewKOF.csproj -c Debug --no-restore
Remove-Item Env:DefaultItemExcludes
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels

py $driver @common --output "$run/key" --compare-plain --fixed-resolution 640x360 --fixed-window 28 51 `
  --scene hit --scene crouch-hit --scene stand-guard --scene crouch-guard --scene mirror-hit --scene interrupt
py $driver @common --output "$run/wave" --compare-plain --fixed-resolution 320x180 `
  --scene wave-startup-7 --scene wave-9 --scene wave-18 --scene wave-19 --scene wave-20 `
  --scene hit-wave-10 --scene hit-wave-30 --scene wave-forward-held-through-exit `
  --scene wave-17-to-down --scene wave-forward-neutral-boundary --scene through-400-wave `
  --scene wave-mixed-at-exit --scene wave-crouch-fallback
py $driver @common --output "$run/wave-motion-controls" --compare-plain --fixed-resolution 320x180 `
  --scene wave-extra-forward-before-button --scene wave-df-hold-before-button --scene wave-db-on-button --scene wave-back-on-button
py $driver @common --output "$run/chain" --compare-plain --fixed-resolution 320x180 `
  --scene far-jab-to-kick-9 --scene far-jab-to-kick-12 --scene far-jab-to-kick-16 `
  --scene near-jab-to-kick-hit --scene crouch-jab-to-kick-11 --scene crouch-jab-to-kick-17 `
  --scene to-kick-11 --scene to-kick-18 --scene to-crouch-jab-11 --scene to-far-jab-11 `
  --scene hit-to-near-jab-22 --scene to-close-high-target-jab-11 --scene guard-to-kick-22 --scene guard-release-back-22 `
  --scene guard-release-down-22 --scene guard-jab-release-back-22
py $driver @common --output "$run/controls" --compare-plain --fixed-resolution 320x180 --fixed-window 28 36 `
  --scene whiff --scene p2-hit --scene sweep-aged-a --scene sweep-release-down --scene both-kicks `
  --scene forward-down --scene back-down --scene light-punch-compete --scene heavy-punch-compete `
  --scene down-forward-heavy-compete --scene to-kick-before-direction --scene counter-sweep --scene air-entry
py $driver @common --output "$run/falling" --compare-plain --fixed-resolution 320x180 --fixed-window 82 99 `
  --scene airborne-fall-locked --scene airborne-fall-overlap
py $driver @common --output "$run/mode2" --compare-plain --fixed-resolution 320x180 --fixed-window 28 44 `
  --mode mode2 --scene mode-body-guard --scene mode-chain --scene wave-13-mode --scene wave-19-mode
py $driver @common --output "$run/repeat" --fixed-resolution 640x360 --fixed-window 48 55 `
  --scene hit --repeat-from "$run/key"
```

固定回放逐场检查物理边沿、动作开始与旧 tick、独立停顿和命中实例、攻击框与受击框、地面反应、普通技转接、Wave 实体的生成与回收。权威日志包含 `CommandAudit` 和 `WaveRestrictionAudit`。`sprite/trace.json` 关联原版轨迹、逻辑状态和实际 PNG。图像只采关键区间，逻辑和权威比较运行至原版轨迹结束。

实时样本应在 GPU 空闲时串行运行。每档一张 PNG，核对采样动作和已提交图像，记录读回前、读回后及稳定 FPS。使用固定回放做逐 tick 原版比较。

```powershell
py $driver --godot $godot --reference $native --output "$run/rt30" --scene whiff --fps 30 --realtime-render 274
py $driver --godot $godot --reference $native --output "$run/rt60" --scene whiff --fps 60 --realtime-render 276
```

阶段 24 冻结门禁沿用原始配置与录制输入。`--wave-profile` 复用冻结场景的物理输入，替换为阶段 25 配置，核对 200／205／215／400／410／440 进入 Wave 的顺序。该模式对照旧场景的共享权威记录，将 `MoveAudit` 中的角色 ID 归一化，再逐条比较新配置的两种视觉覆盖。录制分辨率可降至 320×180，两个模式均只保存结束截图。

```powershell
$archive = 'tools/Stage25Validation/stage24_authority.py'
py $archive --archive D:/ --archive-only
py $archive --archive D:/ --godot $godot --output "$run/stage24-frozen" --resolution 320x180
py $archive --archive D:/ --godot $godot --output "$run/old-wave" --resolution 320x180 --wave-profile
```

更早的门禁使用封版角色配置、物理输入和本次构建的运行时。历史输出放在未占用的目录。阶段 23 的独立兼容副本仍由原脚本校验源哈希并生成。

```powershell
$legacy = 'local-mugen/stage25-frozen-replay'
py tools/Stage14Validation/frozen_authority.py --godot $godot --output "$legacy/stage13c"
py tools/Stage14Validation/state200_duel.py --godot $godot --output "$legacy/stage14" --compare-plain
py tools/Stage15Validation/stage14_authority.py --baseline local-mugen/stage14-validation/fixed-v3 --candidate "$legacy/stage14"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$legacy/stage15" --compare-plain
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fixed-v3 --candidate "$legacy/stage15"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$legacy/stage15-fractional" --compare-plain --scene far-64.5
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fractional --candidate "$legacy/stage15-fractional" --scene far-64.5
py tools/Stage17Validation/stage16_authority.py --godot $godot --output "$legacy/stage16"
py tools/Stage18Validation/stage17_authority.py --godot $godot --output "$legacy/stage17"
py tools/Stage19Validation/stage18_authority.py --godot $godot --output "$legacy/stage18"
py tools/Stage24Validation/stage19_22_authority.py --archive D:/ --godot $godot --output "$run/stage19-22" --resolution 320x180
py tools/Stage24Validation/stage23_authority.py --archive D:/ --godot $godot --output "$run/stage23" `
  --pilot-dir data/imported-characters/stage24-frozen23-pilots-stage25-replay
```

[阶段 24 冻结说明](../Stage24Validation/README.md#阶段-23-冻结门禁) · [本轮实际取证](EVIDENCE.md)
