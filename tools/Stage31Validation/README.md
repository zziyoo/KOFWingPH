# 阶段 31 Godot 对局验证

在仓库根目录执行。构建与图集排除方式沿用 [阶段 30](../Stage30Validation/README.md#构建和图集)。使用 Godot 4.7.2 Mono Forward+ Vulkan。原版帧 `F` 对应 NewKOF 固定逻辑帧 `L=F+14`，提交版本 `S=L+1`。以下输出路径均须为新目录。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$env:PYTHONPATH = 'tools/MugenImporter'
$refs = @('pilot','input','audit','audit-more','edges') |
    ForEach-Object { '--reference'; "local-mugen/stage31-native-$_/results" }
$profile = 'data/imported-characters/terry98-stage31-final'
py -3.11 -m mugen_importer.state_rising_tackle local-mugen/terry.zip `
    data/imported-characters/terry98/manifest.json --base data/imported-characters/terry98-stage30-final `
    @refs --output $profile
foreach ($mode in 'default','mode2') {
    py -3.11 tools/Stage14Validation/stage_assets.py --binding "$profile/$mode.binding.json" `
       --destination "data/imported-characters/terry98-stage31-$mode"
}
& $godot --headless --editor --import --path .
$driver = 'tools/Stage31Validation/rising_tackle_duel.py'
$audit = @('audit','audit-more','edges','pilot','input') |
    ForEach-Object { '--reference'; "local-mugen/stage31-native-$_/results" }
$fixed = @('--godot',$godot,'--profile-dir',$profile,'--fixed-resolution','320x180','--compare-plain')
py -I $driver @fixed @audit --output local-mugen/stage31-repro-body --fixed-window 20 85 `
   --scene 1200-whiff --scene 1200-hit --scene 1200-guard --scene 1200-crouch-guard
py -I $driver @fixed @audit --output local-mugen/stage31-repro-reversal --fixed-window 25 52 `
   --scene 1200-reversal-cancel --scene 1200-reversal-miss --scene 1200-projectile-140-20
py -I $driver @fixed @audit --output local-mugen/stage31-repro-repeat `
   --fixed-window 20 85 --scene 1200-hit --repeat-from local-mugen/stage31-repro-body
```

`trace.json` 将实际 `FramePostDraw` PNG 与呈现提交版本、动作索引、AIR 元素、逻辑实例、冻结计数和碰撞事件对应。`candidate.json` 记录本次的两份 Debug DLL、Manifest、角色配置和 `project.godot`。沿用阶段 30 的冻结回放命令并使用新的输出目录。重跑时保留失败输出，只有相同构建的匹配记录可以用于继续上次回放。
