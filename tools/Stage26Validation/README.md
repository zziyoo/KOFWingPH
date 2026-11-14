# 阶段 26 State 230／235 Vulkan 回放

从项目根目录运行。按[原引擎参考](../../docs/STAGE26_REFERENCE.md)准备 IKEMEN GO 轨迹及阶段 26 的两套生成配置，按[导入说明](../../docs/MUGEN_IMPORT.md)准备对应视觉别名。Godot 4.7.2 Mono 使用 Vulkan Forward+。固定回放以 `L = F + 14` 对齐战斗逻辑，`FramePostDraw` 图像引用提交状态 `S = L + 1`。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$driver = 'tools/Stage26Validation/standing_light_kick_duel.py'
$nativeRoot = 'local-mugen/stage26-native-repro'
$native = "$nativeRoot-body/results"
$out = 'D:/NewKOF-stage26-vulkan-replay'

& $godot --headless --path . --editor --import
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
dotnet build NewKOF.csproj -c Debug --no-restore
Remove-Item Env:DefaultItemExcludes

py $driver --godot $godot --reference $native --output "$out/body" --compare-plain --fixed-resolution 320x180 `
  --scene near-hit --scene far-hit --scene near-crouch-hit --scene far-crouch-hit `
  --scene near-guard --scene far-guard --scene near-crouch-guard --scene far-crouch-guard `
  --scene near-whiff --scene far-whiff --scene near-interrupt --scene far-interrupt `
  --scene mirror-near-hit --scene mirror-far-hit --scene p2-near-hit --scene p2-far-hit
py $driver --godot $godot --reference $native --output "$out/threshold" --compare-plain --fixed-resolution 320x180 `
  --scene threshold-below --scene threshold-exact --scene threshold-above `
  --scene mirror-threshold-exact --scene p2-threshold-exact --scene crouch-threshold-exact
py $driver --godot $godot --reference $native --output "$out/mode2" --compare-plain --fixed-resolution 320x180 `
  --mode mode2 --scene near-hit --scene far-hit --scene near-crouch-guard --scene far-crouch-guard

$referenceArgs = @()
foreach ($batch in 'wave-extended','ground-routes','air-boundary','frozen','split-probe',
                   'forward','reservations','walk','ground','competition','dodge','controls','routes','priority','body') {
  $referenceArgs += @('--reference',"$nativeRoot-$batch/results")
}
$fixed = @('--godot',$godot,'--compare-plain','--fixed-resolution','320x180')
py $driver @fixed @referenceArgs --output "$out/decisive" `
  --scene near-to-far-guard-release-back-40.25 --scene near-to-far-guard-release-down-40.25 `
  --scene recovery-backstep-distance --scene recovery-backstep-own-kick `
  --scene sweep-buffer-ground-cross --scene sweep-buffer-ground-remain-far `
  --scene near-wave-whiff-9 --scene near-wave-whiff-10 --scene near-wave-whiff-13 --scene near-wave-whiff-14 `
  --scene far-wave-whiff-13 --scene far-wave-whiff-24 `
  --scene near-to-jab-wave-clean-held-58 --scene near-to-jab-wave-clean-release-58 `
  --scene near-to-crouch-jab-wave-free-held --scene near-to-crouch-jab-wave-free-release `
  --scene near-reenter-guard-release-back-23 --scene near-reenter-guard-release-down-23

$grounded205 = @('near-to-close-jab-wave-grounded-aligned-held','near-to-close-jab-wave-grounded-aligned-release')
$grounded205Args = @($grounded205 | ForEach-Object { '--scene'; $_ })
py $driver @fixed --reference "$nativeRoot-grounded-205-aligned/results" `
  --output "$out/grounded-205-aligned" --fixed-window 109 141 --timeout 100 @grounded205Args

py $driver @fixed @referenceArgs --output "$out/priority" `
  --scene near-frozen-direction-x-11 --scene near-frozen-direction-y-11 `
  --scene near-frozen-direction-split-button-first --scene near-frozen-direction-split-direction-first `
  --scene near-frozen-direction-split-button-neutral-first --scene near-frozen-direction-split-diagonal-first `
  --scene air-body-threshold-below --scene air-body-threshold-exact --scene air-body-threshold-above

py $driver @fixed --reference "$nativeRoot-dodge/results" --output "$out/natural-720" `
  --scene near-dodge-recovery-edge --scene far-dodge-recovery-edge

py $driver @fixed @referenceArgs --output "$out/mode2-wave" --mode mode2 `
  --scene near-wave-mode2-10 --scene near-wave-mode2-natural --scene far-wave-mode2-natural

py $driver @fixed @referenceArgs --output "$out/ordinary" `
  --scene near-to-crouch-kick-whiff-8 --scene near-to-crouch-kick-whiff-9 --scene near-to-crouch-kick-whiff-16 `
  --scene near-self-whiff-15 --scene near-self-whiff-16 --scene far-no-chain-13 --scene near-to-far-ground-hit-34-31
py $driver @fixed @referenceArgs --output "$out/ordinary-mode2" --mode mode2 `
  --scene near-self-whiff-9 --scene near-self-whiff-15

$outcomes = @('near-wave-hit-21','near-wave-guard-21','near-wave-mixed-natural','far-wave-mixed-natural')
$nativeOutcomes = @($outcomes | ForEach-Object { "default-standing-light-kick-$_" })
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage26 -LastFrame 190 `
  -PreparedEngineDirectory local-mugen/stage26-native-repro-body/engine `
  -RunDirectory local-mugen/stage26-native-repro-wave-outcomes -Cases $nativeOutcomes
$outcomeArgs = @($outcomes | ForEach-Object { '--scene'; $_ })
py $driver @fixed --reference local-mugen/stage26-native-repro-wave-outcomes/results `
  --output "$out/wave-outcomes" @outcomeArgs

& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage26 -LastFrame 100 `
  -PreparedEngineDirectory local-mugen/stage26-native-repro-body/engine `
  -RunDirectory local-mugen/stage26-native-repro-grounded-430-last `
  -Cases default-standing-light-kick-near-to-crouch-kick-grounded-last
py $driver @fixed --reference local-mugen/stage26-native-repro-grounded-430-last/results `
  --output "$out/grounded-430-last" --scene near-to-crouch-kick-grounded-last
```

`sprite/trace.json` 关联物理输入、原版状态变化、NewKOF 的 `CommandAudit`、接触结算、框和实际提交图像。站轻脚选择附带按键与消费时刻的身体距离。原版实际选招读 `state-change.bodyDistX`，按键采样读 `before-command`。NewKOF 身体距离由消费前的权威坐标、朝向与角色配置的前后尺寸复算。选招阈值 **28** 按 Q16 检查，图形不参与规则计算。

恢复顺序对照另保存消费前的帧初距离。在 P2 刚结束硬直且 P1 已前行的固定样本中，另外读取 P1 的当帧提交位置。旧 440 收招的竞争样本检查较新的 `b` 在旧 `a` 胜出后自然过期。300／310／720／1050 的原版转换只核验输入保留。逐帧等价比较截止到该转换的范围边界。旧 205 对空差异样本另见[证据索引](EVIDENCE.md)。

205 地面路线保留 F116 命中到 F222 恢复的完整状态，图像读回只覆盖 R109–141。`--timeout 100` 调整每次 Godot 进程的墙钟上限，普通固定回放仍用 50 秒。

实时用同一原版输入，每档只读取一张提交图像。等待其余 Vulkan 客户端完成，再串行运行两档。检查图像前后的实测 FPS、逻辑帧积压和已提交动作。

```powershell
py $driver --godot $godot --reference $native --output "$out/rt30" --scene near-hit --fps 30 --realtime-render 274
py $driver --godot $godot --reference $native --output "$out/rt60" --scene far-whiff --fps 60 --realtime-render 276
```

## 冻结门禁与新配置共存

阶段 13C–24 沿用[既有归档和回放命令](../Stage25Validation/README.md)。使用本次构建和新的证据目录。阶段 25 归档固定场景使用封版角色和物理输入，先重放两种视觉配置，再核对权威记录。另一组用阶段 26 角色重放六个旧 Wave 来源及 430／440 的关键场景。新配置的原始 `CommandAudit` 分开比较，两种视觉覆盖之间完整比较；跨配置对照共用权威记录时归一化角色 ID。

```powershell
$frozen25 = 'tools/Stage26Validation/stage25_authority.py'
$current = 'data/imported-characters/terry98-stage26-final'
py $frozen25 --archive D:/NewKOF-stage25-verified --godot $godot --output "$out/frozen25"
py $frozen25 --archive D:/ --godot $godot --output "$out/current-legacy" --current-profile $current
py tools/Stage25Validation/stage24_authority.py --archive D:/ --godot $godot `
  --output "$out/old-wave" --resolution 320x180 --wave-profile $current --visual-alias terry98-stage26-default
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```
