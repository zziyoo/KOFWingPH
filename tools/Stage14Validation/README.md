# 阶段 14 对局取证

从仓库根目录执行。先按 [导入说明](../../docs/MUGEN_IMPORT.md) 和 [参考运行](../../docs/STAGE14_REFERENCE.md) 生成本地 Terry Manifest、原引擎轨迹及 `state200.fighter.json`、`state200.binding.json`。验收使用 Godot 4.7.2 Mono 控制台可执行文件。工具使用 Python 3.11 标准库。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build NewKOF.csproj
py tools/Stage14Validation/stage_assets.py
& $godot --headless --path . --editor --import
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels

$run = 'local-mugen/stage14-validation/acceptance'
py tools/Stage14Validation/state200_duel.py --godot $godot --output "$run/fixed" --compare-plain
py tools/Stage14Validation/compare_reference.py --reference local-mugen/stage14-reference-replay/results --candidate "$run/fixed"
py tools/Stage14Validation/state200_duel.py --godot $godot --output "$run/real30" --fps 30 --scene hit
py tools/Stage14Validation/state200_duel.py --godot $godot --output "$run/real60" --fps 60 --scene guard
py tools/Stage14Validation/frozen_authority.py --archive-only
py tools/Stage14Validation/frozen_authority.py --godot $godot --output "$run/frozen"
```

`stage_assets.py` 要求独立的 `terry98-stage14` 目标目录尚未存在。重复运行时可复用该目录，先用生成的绑定和现有 Manifest 比对 SHA-256。每次图形运行传入新的输出目录。脚本把实际启动参数、原始日志、PNG 和结构化轨迹保存在 `local-mugen`。

`state200_duel.py` 使用真实选人、轻拳和防御按键路径。固定帧模式覆盖空挥、命中、站防、镜像、提前蹲防的空挥、接触帧才蹲防、78 距离接触边界、80 距离越界空挥。它校验 Action 200 的 3/4/5 帧及 165/166/165 素材，确认对应版本的 `SpriteRenderAudit` 实际选择贴图。`FrameCapture` 记录渲染号和已提交的逻辑状态号。后者等于生成 `SpriteFrameAudit` 的 Step 编号加一。固定帧要求每个动作状态都有截图。实时模式只采集连续四个渲染帧，检查采样顺序及画面所提交的状态。查看 PNG 验证位置、朝向、特效和恢复边界。

`compare_reference.py` 用双方首次进入招式的权威记录分别作为零点。IKEMEN `after-collision` 提供已执行的状态、时钟和攻击框，`end` 提供下一帧开始前的停顿计数。命中扣血在 IKEMEN 接触后一帧可见，NewKOF 在接触提交时可见。格挡接触前的移动规则不同，击退对照以各自接触位置为起点。位置差的 0.002 容差覆盖浮点与 Q16 积分差。

`frozen_authority.py` 校验 13C 归档 SHA-256 并读取原始参数。正式回归用新构建运行五场的覆盖开关对照，要求 323 帧角色和实体日志与 69 帧取消日志同时逐条等于封版记录。取证图片写入当前回归目录。

若实时全审计运行出现调度积压，可复用对应 `duel.args.txt` 中的原始命令。降低分辨率到 800×450，保留 `--visual-audit`、`--sprite-frame-audit`，关闭实体和额外招式审计。新输出目录以渲染帧 49 注入轻拳，采样 49–54。站防同时从渲染帧 49 按住右，55 松开。帧率上限仍为 30 或 60，并以日志区分实际采样与逻辑推进。
