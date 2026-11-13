# 阶段 16 对局取证

从项目根目录运行。先按阶段 16 导入说明生成两套实验数据和原引擎轨迹。取证使用 Godot 4.7.2 Mono 控制台可执行文件与 Python 3.11 标准库。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build NewKOF.csproj
foreach ($preset in @('default','mode2')) {
    py tools/Stage14Validation/stage_assets.py `
        --binding "data/imported-characters/terry98-stage16-data/$preset.binding.json" `
        --destination "data/imported-characters/terry98-stage16-$preset"
}
& $godot --headless --path . --editor --import

$run = 'local-mugen/stage16-validation/acceptance'
py tools/Stage16Validation/normals_duel.py --godot $godot --output "$run/fixed" --compare-plain
py tools/Stage16Validation/compare_reference.py --reference local-mugen/stage16-reference/results --candidate "$run/fixed"
py tools/Stage16Validation/compare_reference.py --reference local-mugen/stage16-reference/results --candidate "$run/fixed" --scene light-trade --scene heavy-trade
py tools/Stage16Validation/compare_reference.py --reference local-mugen/stage16-reference-guard-release/results --candidate "$run/fixed" --scene heavy-guard-release --scene heavy-crouch-release --scene heavy-crouch-to-stand
py tools/Stage16Validation/compare_reference.py --reference local-mugen/stage16-reference-simultaneous/results --candidate "$run/fixed" --scene light-both-near --scene light-both-far
py tools/Stage16Validation/normals_duel.py --godot $godot --output "$run/real30" --fps 30 --mode default --scene heavy-hit --realtime-window 55 58 --require-no-backlog
py tools/Stage16Validation/normals_duel.py --godot $godot --output "$run/real60" --fps 60 --mode mode2 --scene light-mirror --realtime-window 50 53 --require-no-backlog
py tools/Stage16Validation/normals_duel.py --godot $godot --output "$run/real60-thaw" --fps 60 --mode mode2 --scene light-mirror --realtime-window 61 64 --require-no-backlog
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels

py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/stage15" --compare-plain
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fixed-v3 --candidate "$run/stage15"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/stage15-fractional" --scene far-64.5 --compare-plain
py tools/Stage16Validation/stage15_authority.py --baseline local-mugen/stage15-validation/fractional --candidate "$run/stage15-fractional" --scene far-64.5
py tools/Stage14Validation/state200_duel.py --godot $godot --output "$run/stage14" --compare-plain
py tools/Stage15Validation/stage14_authority.py --baseline local-mugen/stage14-validation/fixed-v3 --candidate "$run/stage14"
py tools/Stage14Validation/frozen_authority.py --godot $godot --output "$run/stage13c"
```

素材脚本仅创建新的视觉别名目录。目录已经存在时，先核对绑定与原 Manifest、图集的 SHA-256。取证脚本要求新的忽略输出目录，保存参数、日志、PNG、双方结构化状态及逐帧差异。固定帧夹具启用计划输入隔离并核对实际收到的按键顺序。实时 30 和 60 为帧率上限，先预热 Vulkan，采集少量已提交状态以检查当时的画面。

已有完整采集后，`normals_duel.py --scene <名称>` 可只重跑受影响场景。对照器的 `--updated-candidate` 从该目录优先读取重跑证据，其余场景继续使用 `--candidate` 原结果。

两套预设分别跑四招的直接对局、轻重拳的远近分界、正反向命中、站防和两种蹲防。合法跳跃落空用来观察近身招的空挥和恢复。第二段单独命中或防御场景以两段独立接触为验收依据。连续动作场景覆盖远身转近身、近身转远身、无控制期间输入、双席各自动作和合法打断。逐帧对照以各自的入招 tick 归零，检查资源、攻击和受击框、结算、停顿、击退及恢复。IKEMEN 在碰撞后一帧扣血，NewKOF 在碰撞的逻辑提交中原子扣血。

原引擎在近身重拳站防的 F7 已进入守势，NewKOF 夹具在首个生效 tick 按后以免预先退离首段范围。两边各以接触点为击退零点。中立跳跃落空使用相同的原版 Action 41/49 受击框。NewKOF 夹具长按上以执行大跳，起跳时钟与高度沿用各自的跳跃规则，只对照攻击者时序和零接触结果。
