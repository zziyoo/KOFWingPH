# 阶段 18 对局取证

在项目根目录运行。先生成阶段 18 两套实验配置和原版轨迹。Godot 使用 4.7.2 Mono/Vulkan Forward+，每次取证使用新的 `local-mugen` 输出目录。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$profiles = 'data/imported-characters/terry98-stage18-data'
$native = 'local-mugen/stage18-reference-canonical/results'
$supplement = 'local-mugen/stage18-reference-final/results'
$down = 'local-mugen/stage18-reference-down/results'
$run = 'local-mugen/stage18-validation/acceptance'
foreach ($preset in @('default','mode2')) {
    py tools/Stage14Validation/stage_assets.py `
        --binding "$profiles/$preset.binding.json" `
        --destination "data/imported-characters/terry98-stage18-$preset"
}
& $godot --headless --path . --editor --import

$cases = @('whiff-9','whiff-10','whiff-11','whiff-12','whiff-16','whiff-17','held',
    'whiff-both-12','whiff-heavy-12','whiff-down-11','whiff-down-12',
    'far-hit-19','far-hit-20','far-hit-21','far-hit-22','far-hit-23',
    'near-hit-23','near-tight-hit-23','near-guard-23','far-guard-23',
    'approach-90-20','mirror-approach-90-20','boundary-46-23','boundary-46p5-23','mirror-near-hit-23','mirror-whiff-12')
$scenes = @($cases | ForEach-Object { '--scene'; $_ })
py tools/Stage18Validation/reentry_duel.py --godot $godot --reference $native --reference $supplement --reference $down `
    --output "$run/fixed" --adapt-source --compare-plain @scenes

py tools/Stage18Validation/reentry_duel.py --godot $godot --reference $native --output "$run/real30" `
    --fps 30 --mode default --scene whiff-12 --realtime-window 51 55 --require-no-backlog
py tools/Stage18Validation/reentry_duel.py --godot $godot --reference $native --output "$run/real60" `
    --fps 60 --mode mode2 --scene mirror-whiff-12 --realtime-second-delay 4 --realtime-window 50 55 --require-no-backlog

py tools/Stage18Validation/stage17_authority.py --godot $godot --output "$run/stage17"
py tools/Stage17Validation/stage16_authority.py --godot $godot --output "$run/stage16"
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

阶段 15、14 与 13C 的复跑命令沿用[阶段 17 取证说明](../Stage17Validation/README.md)。阶段 17 门禁复跑 45 个冻结对局和视觉开关，每个场景与原日志逐条比较。`--archive-only` 可检查冻结证据的完整性。

本次固定帧归档按 `fixed-down/`、`fixed-refined/`、`fixed-boundary/`、`fixed-v1/` 顺序取每个场景首份通过记录，共 52 场、4,888 个逻辑帧、39,104 条一致的权威记录与 3,432 张 PNG。两端分别记录 46 次 NewKOF 接触和 40 次原版接触。实时证据见 `real30/` 和 `real60-reentry/`。两次均在第二次入招前后采到画面，两次发动各相隔 8 个逻辑 tick。阶段 17 冻结门禁逐条匹配 4,247 帧、25,482 行。

固定帧脚本读取原引擎物理按键的变化，检查合法入招、目标动作、第一姿态、实例确认及接触去重，按 NewKOF 自身实际随机样本重算伤害。渲染取证以 `FramePostDraw` 记录的实际提交状态定位 PNG，并和同次 `SpriteFrameAudit` 对照。实时采集使用帧率上限。脚本检查两次实际入招间隔为 7–11 个逻辑 tick，采集两次实例的实际呈现。`--realtime-second-delay` 可调整双击的渲染帧间隔。

`--adapt-source` 明确记录少数夹具的对照边界。200→205 使用地面前进和短暂蹲伏建立近身距离，复现原版跳跃靠近夹具的选招时钟和新实例首帧。NewKOF 的通用受击框使部分第二击提前一 tick，或命中原版动态受击框原本躲过的一击。连续站防中，原版在第二个独立轻拳实例格挡，NewKOF 按既有规则在仍处于防御硬直时受击。每种差异都校验首击、两次入招和 NewKOF 后续结算。原版同时按下方向与轻拳可在特定场景进入 State 400，本阶段验证站轻拳分支被正确抑制。每个场景的 `trace.json` 保存原始按键、实际 Godot 输入、状态、接触和例外。
