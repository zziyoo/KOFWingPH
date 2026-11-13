# 阶段 17 对局取证

从项目根目录运行。先按阶段 17 原引擎取证和导入说明生成实验数据。固定帧运行使用 Godot 4.7.2 Mono Vulkan Forward+、计划输入和同次 `FramePostDraw` PNG。每次运行使用新的 `local-mugen` 输出目录。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$profiles = 'data/imported-characters/terry98-stage17-data'
$native = 'local-mugen/stage17-reference/results'
$edge = 'local-mugen/stage17-reference-carry-edge/results'
$run = 'local-mugen/stage17-validation/acceptance'

foreach ($preset in @('default','mode2')) {
    py tools/Stage14Validation/stage_assets.py `
        --binding "$profiles/$preset.binding.json" `
        --destination "data/imported-characters/terry98-stage17-$preset"
}
& $godot --headless --path . --editor --import

py tools/Stage17Validation/damage_duel.py --godot $godot --output "$run/fixed" --compare-plain
py tools/Stage17Validation/compare_damage.py --reference $native --edge-reference $edge --candidate "$run/fixed"

py tools/Stage17Validation/damage_duel.py --godot $godot --output "$run/guard" --compare-plain --scene heavy-guard --scene mirror-heavy-guard --scene heavy-far-guard --scene mirror-heavy-far-guard
py tools/Stage17Validation/compare_damage.py --reference local-mugen/stage17-reference-guard-v2/results --candidate "$run/guard" --scene heavy-guard --scene mirror-heavy-guard --scene heavy-far-guard --scene mirror-heavy-far-guard

py tools/Stage17Validation/damage_duel.py --godot $godot --output "$run/seed2026" --mode default --scene interrupt --damage-seed 2026 --compare-plain
py tools/Stage17Validation/compare_damage.py --reference local-mugen/stage17-reference-seed-2026/results --candidate "$run/seed2026" --mode default --scene interrupt
py tools/Stage17Validation/damage_duel.py --godot $godot --output "$run/repeat" --mode default --scene interrupt --damage-seed 2026 --compare-plain --repeat-of "$run/seed2026"

py tools/Stage17Validation/damage_duel.py --godot $godot --output "$run/real30" --fps 30 --mode default --scene heavy-near --realtime-window 55 58 --require-no-backlog
py tools/Stage17Validation/damage_duel.py --godot $godot --output "$run/real60" --fps 60 --mode mode2 --scene mirror-heavy-far --realtime-window 56 59 --require-no-backlog

py tools/Stage17Validation/stage16_authority.py --godot $godot --output "$run/stage16"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/stage15" --compare-plain
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fixed-v3 --candidate "$run/stage15"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/stage15-fractional" --scene far-64.5 --compare-plain
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fractional --candidate "$run/stage15-fractional" --scene far-64.5
py tools/Stage14Validation/state200_duel.py --godot $godot --output "$run/stage14" --compare-plain
py tools/Stage15Validation/stage14_authority.py --baseline local-mugen/stage14-validation/fixed-v3 --candidate "$run/stage14"
py tools/Stage14Validation/frozen_authority.py --godot $godot --output "$run/stage13c"
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

`duel.log`、`duel.args.txt`、`trace.json` 与 `frames/` 保存在每个场景的目录中。`damage-comparison.json` 记录接触时刻、攻击席位、伤害参数、原始伤害及扣血。原引擎以共用随机流采样，NewKOF 实验预设使用各席独立的可重放流。对照分别使用实际抽样数值验证原公式，并核对反击年龄、分母与接触时序。连续段的除法在缩放前执行整数截断。原引擎在下一帧提交受击扣血，NewKOF 在碰撞提交时原子扣血。

覆盖开关门禁逐条比较战斗、实体、招式及伤害审计。实时 30／60 是帧率上限，只采集少量实际提交的画面。阶段 16 门禁使用原始参数重新运行 58 个冻结场景并逐条比较权威日志。`--archive-only` 可独立检查冻结证据是否齐全。

双 KO 补测复用固定批次 `default-damage-near-trade` 的 sprite/plain 两条 `duel.args.txt`。分别将 `--shot`、`--capture-dir` 指向新输出目录，追加 `--hp=1`。在同一 `VisualAudit` 行核对两条实际扣血 1 的 Hit、一个 DoubleKnockout 及双方生命为 0。按上述前缀比较两种运行的权威日志。实际结果在 `local-mugen/stage17-validation/fixed-ko/`。
