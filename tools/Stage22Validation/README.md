# 阶段 22 Vulkan 与冻结回归

从项目根目录运行。按[导入说明](../../docs/MUGEN_IMPORT.md#阶段-22-蹲轻拳与弱-power-wave)生成两个实验预设并安装绑定。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$native = 'local-mugen/stage22-reference/results'
$profile = 'data/imported-characters/terry98-stage22-data'
$run = 'D:/NewKOF-stage22-replay'
$common = @('--godot',$godot,'--reference',$native,'--profile-dir',$profile)
$cases = @((Get-Content "$native/run-metadata.json" -Raw | ConvertFrom-Json).executed_cases |
  Where-Object { $_ -like 'default-crouch-wave-*' } |
  ForEach-Object { '--scene'; $_.Substring('default-crouch-wave-'.Length) })
dotnet build NewKOF.csproj -c Debug --no-restore
py tools/Stage22Validation/crouch_wave_duel.py @common --output "$run/fixed" --compare-plain @cases
py tools/Stage22Validation/crouch_wave_duel.py @common --output "$run/mode2" --mode mode2 --compare-plain --scene hit-0 --scene hit-20
py tools/Stage22Validation/crouch_wave_duel.py @common --output "$run/repeat" --scene hit-held-11 --repeat-from "$run/fixed"
py tools/Stage22Validation/crouch_wave_duel.py @common --output "$run/live30" --fps 30 --scene hit-held-11 --realtime-window 216 218
py tools/Stage22Validation/crouch_wave_duel.py @common --output "$run/live60" --fps 60 --scene hit-held-11 --realtime-input-spacing 2 --realtime-window 231 233
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels

dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~CrouchRouteTests|FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~MultiHit|FullyQualifiedName~ComboCancelTests'
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj -c Debug --no-restore
$env:PYTHONPATH = 'tools/MugenImporter'
py -m unittest discover -s tools/MugenImporter/tests
```

本体门禁可单独复跑。以已安装的绑定绘制同一素材，角色数据在独立输出目录生成。

```powershell
$body = 'data/imported-characters/terry98-stage22-body-only'
py -m mugen_importer.state_crouch_wave local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --base data/imported-characters/terry98-stage21-data --reference $native --output $body --body-only
py tools/Stage22Validation/crouch_wave_duel.py --godot $godot --reference $native --profile-dir $body `
  --output "$run/body-only" --compare-plain --scene hit-0 --scene held-down-0 --scene guard-0 `
  --scene crouch-guard-0 --scene interrupt-0 --scene mirror-hit-0 --scene stand-0 --scene whiff-0
```

已存在的 Helper 打断 State 400 的补充取证使用同一场景输入。

```powershell
$incoming = 'local-mugen/stage22-reference-incoming'
$afterCancel = 'local-mugen/stage22-reference-after-cancel'
$nearFallback = 'local-mugen/stage22-reference-near-fallback'
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage22 -Prepare -LastFrame 165 `
  -RunDirectory $incoming -Cases default-crouch-wave-incoming-wave-25
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage22 -Prepare -LastFrame 165 `
  -RunDirectory $afterCancel -Cases default-crouch-wave-incoming-wave-20
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage22 -Prepare -LastFrame 165 `
  -RunDirectory $nearFallback -Cases default-crouch-wave-near-hit-21
py tools/Stage22Reference/Check-Traces.py $native "$incoming/results" "$afterCancel/results" "$nearFallback/results"
py tools/Stage22Validation/crouch_wave_duel.py --godot $godot --reference "$incoming/results" `
  --profile-dir $profile --output "$run/incoming" --compare-plain --scene incoming-wave-25
py tools/Stage22Validation/crouch_wave_duel.py --godot $godot --reference "$afterCancel/results" `
  --profile-dir $profile --output "$run/after-cancel" --compare-plain --scene incoming-wave-20
py tools/Stage22Validation/crouch_wave_duel.py --godot $godot --reference "$nearFallback/results" `
  --profile-dir $profile --output "$run/near-fallback" --compare-plain --scene near-hit-21
```

同一默认预设的旧来源及守势复查使用现有阶段 21 执行器和阶段 20／21 轨迹。

```powershell
$reuse = @('--godot',$godot,'--profile-dir',$profile,'--alias-prefix','terry98-stage22','--compare-plain')
py tools/Stage21Validation/heavy_wave_duel.py @reuse --reference local-mugen/stage20-reference/results `
  --reference local-mugen/stage20-reference-mixed/results --output "$run/light-routes" --legacy-stage20 --scene hit-20 --scene mixed-sequential
py tools/Stage21Validation/heavy_wave_duel.py @reuse --reference local-mugen/stage21-reference-final/results `
  --output "$run/heavy215" --scene hit-22 --scene hit-25
py tools/Stage21Validation/heavy_wave_duel.py @reuse --reference local-mugen/stage21-reference-guard-release/results `
  --output "$run/guard-release" --scene guard-release-20 --scene guard-release-34
```

固定帧的 `trace.json` 把原版逻辑帧 F 对齐为 NewKOF 的 L=F+14。PNG 文件由同次 Vulkan `FramePostDraw` 采集，记录的提交版本为 L+1。对照脚本核对 Action 400 的阶段和实际 Clsn1／Clsn2、Action 12、命中与防御、取消实例、Helper 和 Pause。实时只读取三张实际图像，并保存采样前后测量值、提交顺序与实体时钟。

历史冻结门禁复用[阶段 21](../Stage21Validation/README.md)与[阶段 19](../Stage19Validation/README.md)的权威回归命令。阶段 21 固定样本可用如下命令比较历史权威日志。

```powershell
py tools/Stage22Validation/stage21_authority.py --godot $godot --output "$run/stage21" `
  --whiff-fallback D:/NewKOF-stage21-evidence-vulkan-final-20261113/whiff-17
```
