# 阶段 19 Power Wave 取证

在项目根目录使用 Godot 4.7.2 Mono。原版运行与源码哈希见 [阶段 19 原版参考](../../docs/STAGE19_REFERENCE.md)。以下命令使用本地素材及新的取证目录。`power_wave_duel.py --output` 可指向另一磁盘的空路径。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$base = 'data/imported-characters/terry98-stage18-data'
$profiles = 'data/imported-characters/terry98-stage19-data'
$native = 'local-mugen/stage19-reference/results'
$supplement = 'local-mugen/stage19-reference-supplement/results'
$launch = 'local-mugen/stage19-reference-launch-recovery/results'
$run = 'local-mugen/stage19-validation/replay'
$env:PYTHONPATH = 'tools/MugenImporter'
py -m mugen_importer.state_power_wave local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
    --base-profile-dir $base --reference $native --reference $supplement --output-dir $profiles
py tools/Stage14Validation/stage_assets.py --binding "$profiles/default.binding.json" `
    --destination data/imported-characters/terry98-stage19-default
& $godot --headless --path . --editor --import
dotnet build NewKOF.csproj -c Debug --no-restore
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProximityGuardTests|FullyQualifiedName~ScriptedPauseTests|FullyQualifiedName~ProjectileTimelineTests|FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~FighterDataTests|FullyQualifiedName~ComboCancelTests|FullyQualifiedName~DynamicDamageTests'
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj --no-restore
py -m unittest discover -s tools/MugenImporter/tests

$cases = @('outside-whiff-9','whiff-8','whiff-9','whiff-10','whiff-11','whiff-12','hit-11','hit-held-11',
    'hit-17','hit-18','hit-19','hit-20','hit-21','hit-22','hit-23','hit-no-motion-20','guard-20','guard-22','guard-23',
    'far-whiff-9','mirror-hit-20','dual-9','mirror-dual-9','terminal-heavy16-17')
$scenes = @($cases | ForEach-Object { '--scene'; $_ })
py tools/Stage19Validation/power_wave_duel.py --godot $godot --reference $native --reference $supplement `
    --output "$run/fixed" --compare-plain @scenes
py tools/Stage19Validation/power_wave_duel.py --godot $godot --reference $launch `
    --output "$run/recovery" --recovery --compare-plain --scene whiff-9 --scene hit-20 --scene mirror-whiff-9

py tools/Stage19Validation/power_wave_duel.py --godot $godot --reference $native --output "$run/real30" `
    --fps 30 --scene neutral-9 --disable-vsync --realtime-resolution 480x270 --realtime-window 59 63 --require-clean-sample
py tools/Stage19Validation/power_wave_duel.py --godot $godot --reference $native --output "$run/real60" `
    --fps 60 --scene neutral-9 --disable-vsync --realtime-resolution 480x270 --realtime-window 69 73 --require-no-backlog
py tools/Stage19Validation/power_wave_duel.py --godot $godot --reference $native --output "$run/real60-warmed" `
    --fps 60 --scene neutral-9 --disable-vsync --realtime-resolution 320x180 --realtime-warmup-frames 150 `
    --realtime-input-spacing 2 --realtime-window 225 229 --require-no-backlog

py tools/Stage19Validation/stage18_authority.py --godot $godot --output "$run/stage18"
py tools/Stage18Validation/stage17_authority.py --godot $godot --output "$run/stage17"
py tools/Stage17Validation/stage16_authority.py --godot $godot --output "$run/stage16"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/stage15" --compare-plain
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fixed-v3 --candidate "$run/stage15"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/stage15-fractional" --compare-plain --scene far-64.5
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fractional --candidate "$run/stage15-fractional" --scene far-64.5
py tools/Stage14Validation/state200_duel.py --godot $godot --output "$run/stage14" --compare-plain
py tools/Stage15Validation/stage14_authority.py --baseline local-mugen/stage14-validation/fixed-v3 --candidate "$run/stage14"
py tools/Stage14Validation/frozen_authority.py --godot $godot --output "$run/stage13c"
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

- 固定帧取证将原版 F0 对齐到 NewKOF 逻辑帧 14。每个 `FramePostDraw` PNG 的 `state` 对应提交后的版本 `logic + 1`。`trace.json` 保留原版转换、实际战斗事件、Power Wave 权威实体记录、Pause 记录和每张图片的角色与实体索引。
- 预防御使用原版按后序列。轻拳接触保持 X30，Wave 格挡接触 X60.734207 对应原版 X60.73532，差值 0.001113。校验器断言接触帧、11 tick 震停、9 点削血和坐标容差 0.002。
- `hit-23` 和 `guard-23` 断言原版同键回落时机，验证 L37 重进 State 200 且无 Wave。阶段 18 的通用反应框会额外产生 L40 命中 44，原版没有第二次接触。该差异在两组轨迹中分别记录为 `compatibility`。
- 完整击飞样本以 Wave 接触位置为位移基准，比较到起身后恢复站立。NewKOF 在穿越地面的一帧将 Y 限制到地面。图像范围覆盖撞地、反弹、倒地、起身和站立。
- 实时模式按实际提交的逻辑状态采样。`--require-clean-sample` 拒绝动作开始至最后一张图像之间报告的积压。`trace.json` 的 `backlogAfterLogic` 保留启动和后续警告位置，`rendererFps` 保存 Godot `--print-fps` 的实测输出。实时对局建议在历史回归结束后采集，采样窗口可按实际生成帧调整。
- RNG 比较以实际接触 tick 的公开操作数重算每次命中伤害。普通命中、格挡、Power Wave 和全局 Pause 的权威日志在开启与关闭视觉覆盖的同种子运行中逐条比较。

固定帧证据分批保存在 `local-mugen/stage19-validation/fixed-final`、`fixed-final-rest`、`fixed-final-rest-d`。其中 `fixed-final-rest-d` 指向 D 盘完整证据。24 场包含 2,772 个逻辑帧、26,952 条一致权威日志和 1,956 张取证图。延长恢复证据在 `recovery-v2` 与 `recovery-v3`，另有三场、318 个原版反应轨迹采样和 255 张取证图。

`real30-accepted` 实测 30 FPS，R59–63 对应提交状态 33／35／37／40／42。`real60-accepted` 以 60 FPS 为上限，Godot 报告 57 FPS，R69–73 对应提交状态 27／31／32／33／34。两场日志均无调度积压警告。

`real60-stop` 使用同一命令的 R71–75 窗口，Godot 报告 55 FPS。提交状态为 36／38／40／41／42，Helper 年龄从 7、9 推进到 11、12、13，覆盖停止边界。日志无积压警告。

`real60-warmed` 延长暂停预热并把相邻物理按键分到不同渲染帧。活动对局采样前报告 60 FPS，之后报告 58 FPS。R225–229 提交状态为 36／37／37／38／39，Helper 年龄为 7／8／8／9／10。日志无积压警告。`ground-recovery-final` 是最终预设的 `guard-20`、`guard-22` 和 `far-whiff-9` 复跑，增加了至恢复控制的 102 个地面状态断言。
