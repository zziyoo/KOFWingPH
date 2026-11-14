# 阶段 24 State 440 Vulkan 对照

从项目根目录运行。先采集原版轨迹，生成阶段 24 实验配置与本地视觉别名。固定帧按原版物理键注入，原版 F 对齐 Godot 逻辑 L=F+14，图像标记对应提交版本 S=L+1。原版 `b` 使用 P1 `I` 和 P2 `Semicolon`，与轻拳输入分开。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$native = 'local-mugen/stage24-reference/results'
$controls = 'local-mugen/stage24-reference-controls/results'
$routesFinal = 'local-mugen/stage24-reference-routes-final/results'
$boundaries = 'local-mugen/stage24-reference-boundaries/results'
$repeat = 'local-mugen/stage24-reference-repeat/results'
$priority = 'local-mugen/stage24-reference-priority/results'
$profile = 'data/imported-characters/terry98-stage24-final'
$body = 'data/imported-characters/terry98-stage24-body-final-verified'
$run = 'D:/NewKOF-stage24-vulkan-replay'
$driver = 'tools/Stage24Validation/crouch_kick_duel.py'
$common = @('--godot',$godot,'--reference',$repeat,'--reference',$priority,'--reference',$routesFinal,
  '--reference',$native,'--reference',$controls,'--reference',$boundaries)

& $godot --headless --path . --editor --import
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
dotnet build NewKOF.csproj -c Debug --no-restore
Remove-Item Env:DefaultItemExcludes
$fixed = @('--compare-plain','--fixed-resolution','640x360')
py $driver @common @fixed --profile-dir $body --output "$run/body" `
  --scene hit-0 --scene crouch-hit-0 --scene guard-0 --scene crouch-guard-0 `
  --scene held-down-0 --scene interrupt-0 --scene mirror-hit-0 --scene whiff-0 --scene stand-0 --scene air-0 `
  --scene down-forward-kick-0 --scene down-back-kick-0 --scene down-light-kicks-0 --scene down-light-and-kick-0 `
  --scene down-heavy-and-kick-0 --scene down-forward-heavy-and-kick-0 `
  --scene stand-to-down-0 --scene jump-and-kick-0 `
  --scene tech-55 --scene tech-56 --scene tech-57 --scene tech-60 --scene tech-61 --scene tech-held-55
py $driver @common --profile-dir $body --fixed-resolution 640x360 --output "$run/tech-landing" `
  --scene tech-56 --fixed-window 78 115 --repeat-from "$run/body"
py $driver @common @fixed --profile-dir $profile --output "$run/routes" `
  --scene hit-27 --scene hit-33 --scene hit-34 --scene hit-held-19 --scene hit-19 `
  --scene crouch-hit-27 --scene crouch-guard-27 --scene mirror-hit-27 `
  --scene whiff-12 --scene whiff-13 --scene whiff-14 --scene whiff-15 --scene whiff-16 --scene whiff-17 `
  --scene whiff-22 --scene whiff-23 --scene whiff-45 --scene compete-kick-16 --scene compete-heavy-16 `
  --scene repeat-kick-23 --scene repeat-kick-45 --scene mixed-27 --scene incoming-wave-26 --scene incoming-wave-34 `
  --scene edge-0 --scene edge-16 --scene incoming-wave-16 --scene incoming-wave-22 `
  --scene hit-repeat-kick-56 --scene hit-repeat-kick-walk-58 --scene tech-split-56 --scene repeat-kick-light-45
py $driver @common @fixed --profile-dir $profile --output "$run/mode2" --mode mode2 --scene hit-0 --scene hit-27 --fixed-window 51 136
py $driver @common --fixed-resolution 640x360 --profile-dir $profile --output "$run/repeat" `
  --scene hit-27 --fixed-window 74 136 --repeat-from "$run/routes"

py $driver --godot $godot --reference local-mugen/stage22-reference/results --previous-stage Stage22 `
  @fixed --profile-dir $profile --output "$run/source400" --scene hit-held-11
py $driver --godot $godot --reference local-mugen/stage23-reference/results --previous-stage Stage23 `
  @fixed --profile-dir $profile --output "$run/source410" --scene hit-22 --scene whiff-11
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --profile-dir $profile --alias-prefix terry98-stage24 `
  --reference local-mugen/stage21-reference-final/results --output "$run/source215" --compare-plain --scene hit-22
py tools/Stage21Validation/heavy_wave_duel.py --godot $godot --profile-dir $profile --alias-prefix terry98-stage24 `
  --reference local-mugen/stage20-reference-mixed/results --output "$run/sources200-205" `
  --legacy-stage20 --compare-plain --scene mixed-sequential

& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

固定取证分别记录物理输入、原版候选与状态转换、Godot 指令候选与消费、实际 `MoveStarted`、攻防框、动态伤害、HitPause、Pause、实体生命周期和反应阶段。`trace.json` 关联同次运行的 `FramePostDraw` 图像和权威状态。 [证据索引](EVIDENCE.md)

实时稀疏采集用一张图核对已呈现的状态和实体，记录截取前、同步读取后的采样档位和恢复 FPS。30 与 60 FPS 采用两个原版空挥输入序列，以便手势各方向在实际绘制周期内得到采样。真实逻辑帧和输入在日志中保留。逐 tick 原版对照仍使用固定帧。

```powershell
py $driver --godot $godot --reference $routesFinal --profile-dir $profile --output "$run/rt30" `
  --scene whiff-13 --fps 30 --realtime-window 293 293
py $driver --godot $godot --reference $routesFinal --profile-dir $profile --output "$run/rt60" `
  --scene whiff-16 --fps 60 --realtime-window 307 307
```

`crouch-guard-27` 的两次 10.5 初速、0.82 衰减分别逐帧按 Q16 复算，原版浮点坐标和 NewKOF 的恢复位置差为 0.00274134 与 0.00549268。该用例的接触时点、格挡姿态、削血和恢复均逐帧对照。

`incoming-wave-16`／`incoming-wave-26` 首次横向偏差分别出现在原版 F91／F88 的镜头边界，之后保持相同水平速度和纵向轨迹。比较器按权威速度核算 NewKOF 余下弹道，逐帧核对反应阶段和 F134／F131 恢复控制。原版终点 X−218，NewKOF X−229.125。

## 阶段 23 冻结门禁

原始目录选取与阶段 23 证据索引一致。脚本移除密集图像读取并改用新的结束截图路径。部分试跑配置含退役的 `excludedStartDirection`。脚本先校验源 SHA-256，并在独立本地目录复制角色文件，仅移除这一个字段，随后核对这些录制输入未使用其前下＋重拳过滤条件。其余配置、物理输入和权威归档按原版本逐条比较。`--archive-only` 仅比较已存在的开关归档。 [阶段 23 证据索引](../Stage23Validation/EVIDENCE.md)

```powershell
py tools/Stage24Validation/stage23_authority.py --archive D:/ --archive-only
py tools/Stage24Validation/stage23_authority.py --archive D:/ --godot $godot --output "$run/stage23-frozen"
```

## 阶段 19–22 冻结门禁

对照阶段 23 已核实的冻结复跑归档。脚本沿用各阶段的权威字段、配对数量和实际注入命令。阶段 22 归档用 `duel.log` 定位与原门禁相同的配对场景。阶段 22 回放降低渲染分辨率，脚本仍检查两种视觉覆盖、真实 Vulkan 启动和结束截图。原始归档保持只读。

```powershell
$history = 'tools/Stage24Validation/stage19_22_authority.py'
py $history --archive D:/ --archive-only
py $history --archive D:/ --godot $godot --output "$run/history" --stage 19 --stage 20 --stage 21
py $history --archive D:/ --godot $godot --output "$run/history" --stage 22 --resolution 320x180
```

阶段 13C–18 使用[既有权威日志回放命令](../Stage19Validation/README.md)，逐场保留已封存的角色数据和物理输入。
