# 阶段 15 对局取证

从项目根目录运行。先按阶段 15 参考与导入文档生成本地 IKEMEN 轨迹和两个实验配置。Godot 控制台可执行文件须为 4.7.2 Mono。以下命令使用 Python 3.11 标准库。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build NewKOF.csproj
foreach ($preset in @('default','mode2')) {
    py tools/Stage14Validation/stage_assets.py `
        --binding "data/imported-characters/terry98-stage15-data/$preset.binding.json" `
        --destination "data/imported-characters/terry98-stage15-$preset"
}
& $godot --headless --path . --editor --import

$run = 'local-mugen/stage15-validation/acceptance'
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/fixed" --compare-plain
py tools/Stage15Validation/compare_reference.py --reference local-mugen/stage15-reference/results --candidate "$run/fixed"
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/real30" --fps 30 --mode default --scene hit --realtime-resolution 640x360 --realtime-window 50 52 --require-no-backlog
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/real60" --fps 60 --mode mode2 --scene mirror --realtime-resolution 640x360 --realtime-window 52 54 --require-no-backlog
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels

py tools/Stage14Validation/state200_duel.py --godot $godot --output "$run/stage14" --compare-plain
py tools/Stage15Validation/stage14_authority.py --baseline local-mugen/stage14-validation/fixed-v3 --candidate "$run/stage14"
py tools/Stage14Validation/frozen_authority.py --godot $godot --output "$run/stage13c"
```

分数入招边界复现使用同一实验配置或由 `--profile-dir` 指定一个独立生成的配置目录。`far-64.5` 夹具使用 `--duel=32.25`，双方固定坐标相距 64.5。参考轨迹来自补充原引擎运行。

```powershell
py tools/Stage15Validation/state210_duel.py --godot $godot --output "$run/fractional" --scene far-64.5 --compare-plain
py tools/Stage15Validation/compare_reference.py --reference local-mugen/stage15-reference-fractional/results --candidate "$run/fractional" --scene far-64.5
```

素材准备命令只接受新的独立目录。已准备时可复用，先核对绑定、Manifest 和图集与所用版本一致。输出使用新的 `local-mugen` 子目录，保留原命令、同次运行日志、截图及 `trace.json`。`--capture-input-only` 将本次验收的计划按键送到正常对局输入处理，防止窗口失焦和桌面键盘输入影响重复测试。

实时夹具先暂停对局，让 Vulkan 完成首轮绘制准备，再恢复并执行轻量采集。30 和 60 是渲染帧率上限。每个样本关联当次绘制所使用的已提交逻辑状态。

固定帧验收覆盖两套 210 模式的空挥、命中、站防、提前／同 tick 蹲防、镜像、64/65 入招边界、86/87 碰撞边界、轻拳打断及双方同 tick 命中。另用两个不同模式的席位验证重拳收招后的轻拳、收招前无效输入和对席重拳。用真实 Manifest 验证 200、210 和 211 的显示序列，检查 FramePostDraw 图像所对应的两个席位状态、停顿和恢复，再人工查看各边界图。

参考对照以两边首次进入招式的权威记录分别归零。IKEMEN 碰撞相位提供攻击框和受击框，结束相位提供已提交的状态和下一 tick 的停顿。原引擎接触后一帧扣血，NewKOF 在接触 tick 提交。击退以双方各自接触位置为基准，在硬直窗口内比较，允许 Q16 与浮点积分误差至 0.004。恢复 tick 另行对照。近身原版进入 215 的边界应拒绝远身 210，215 不在实验配置内。多招顺序场次检验项目输入与状态独立，原版脚本取消不属于本对照。
