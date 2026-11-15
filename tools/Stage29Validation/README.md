# 阶段 29 State 250 Vulkan 回放

由 NewKOF 根目录运行。准备[原引擎签名轨迹](../../docs/STAGE29_REFERENCE.md)、`data/imported-characters/terry98-stage29-final` 的双模式配置与 `terry98-stage29-default`、`terry98-stage29-mode2` 视觉别名。Godot 使用 4.7.2 Mono 的 Forward+ Vulkan。计划内的 GPU 回放串行执行。

## 生成角色配置和视觉别名

在原版签名轨迹已齐备时使用新的空输出目录。实际对局前暂存角色绑定，随后按下节导入图集。两次独立生成的 `fighter`、`binding` 和 `source` JSON 应逐项具有相同 SHA-256。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
$refs = @('edges','core','counter','air','extras','mixed','old20') |
  ForEach-Object { '--reference'; "local-mugen/stage29-native-$_/results" }
$profile = 'data/imported-characters/terry98-stage29-final'
py -3.11 -m mugen_importer.state_ground_blowback local-mugen/terry.zip `
  data/imported-characters/terry98/manifest.json --base data/imported-characters/terry98-stage28-final `
  @refs --output $profile
if ($LASTEXITCODE) { throw 'Stage 29 conversion failed' }
foreach ($mode in 'default','mode2') {
  py -3.11 tools/Stage14Validation/stage_assets.py --binding "$profile/$mode.binding.json" `
    --destination "data/imported-characters/terry98-stage29-$mode"
  if ($LASTEXITCODE) { throw "Stage 29 $mode visual staging failed" }
}
```

## 固定采样

运行器复用阶段 26 的物理输入、图像提交与权威对照链路。原版帧 `F` 映射到 NewKOF 逻辑 `L=F+14`，`FramePostDraw` 图像记录相应提交版本 `S=L+1`。每个用例比较 `MoveStarted` 顺序、接触、命令消费、动作时钟、Clsn1/Clsn2、HitPause、引擎各自 RNG 下的动态扣血、位置和反应相位。`trace.json` 记录实际捕获的相位范围。较短的原版轨迹只覆盖到 `lastCompared`，`recovered` 为空时需用独立长轨迹核对完整恢复。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
# 首次暂存新别名后导入对应图集；待其他 Godot 进程退出再导入。
& $godot --headless --editor --import --path . --log-file local-mugen/stage29-atlas-import-engine.log
$driver = 'tools/Stage29Validation/ground_blowback_duel.py'
$out = 'D:/NewKOF-stage29-repro'
$fixed = @('--godot',$godot,'--compare-plain','--fixed-resolution','320x180')
$refs = @('edges','core','counter','air','extras','mixed','old20') |
  ForEach-Object { '--reference'; "local-mugen/stage29-native-$_/results" }
py -I $driver @fixed @refs --output "$out/body" --fixed-window 28 76 `
  --scene 250-hit --scene 250-crouch-hit --scene 250-whiff --scene mirror-250-hit --scene 250-trade `
  --scene 250-guard --scene 250-crouch-guard --scene 250-interrupt --scene 250-contact-146 `
  --scene 250-contact-147 --scene 250-ground-tech
py -I $driver @fixed @refs --output "$out/routes" `
  --scene 250-cancel-20 --scene 250-cancel-23 --scene 250-cancel-24 --scene 250-cancel-25 --scene 250-cancel-26 `
  --scene 250-hit-cancel-24 --scene 250-frozen-cancel --scene 250-310-24 `
  --scene 250-wave-25 --scene 250-wave-26 --scene 250-exit-wave --scene 250-hit-wave-24 --scene 250-counter-hit-wave
py -I $driver @fixed @refs --output "$out/directed" --fixed-window 45 133 `
  --scene 250-guard-300-wave --scene 250-air-held-back
py -I $driver @fixed @refs --output "$out/input" --scene 250-cd-direction-release --scene 250-cd-direction-add `
  --scene 250-y-then-b-held --scene 250-b-then-y --scene 250-cd-repress --scene 250-side-cd-fore `
  --scene 250-cd-down-forward --scene 250-cd-plus-wave --scene 250-cd-plus-x
py -I $driver @fixed @refs --output "$out/world-pause" --fixed-window 124 181 --scene 250-cd-global-pause-103
py -I $driver @fixed @refs --output "$out/throw-competition" --fixed-window 105 188 `
  --scene 250-lying-cd --scene 250-wakeup-cd
py -I $driver @fixed @refs --mode mode2 --output "$out/mode2" `
  --scene 250-hit --scene 250-guard --scene 250-cancel-24 --scene 250-wave-24 --scene 250-exit-wave
py -I $driver @fixed @refs --output "$out/repeat" --repeat-from "$out/body" --fixed-window 28 76 --scene 250-hit
```

通过时每个用例输出动作实例、Wave 实体及权威记录数。同种子复跑同时校验独立运行的权威行。`candidate.json` 记录两个 Debug DLL、两种当前模式文件和 Manifest 的 SHA-256。`sprite/duel.args.txt` 保留实际命令，`sprite/trace.json` 指向同次对局提交的 Vulkan 图像，`plain/duel.log` 用于关视觉的对照。

## 实时和旧行为回归

实时仅采一张行动中的图像，每档保留实测帧率与帧积压的逻辑编号。

```powershell
py -I $driver --godot $godot @refs --output "$out/realtime30" --scene 250-hit --fps 30 --realtime-render 282
py -I $driver --godot $godot @refs --output "$out/realtime60" --scene 250-hit --fps 60 --realtime-render 294
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

旧行为对照复用[阶段 27 共存回放](../Stage27Validation/README.md#历史和新配置共存门禁)。阶段 29 新配置的 `launchAir` 来自原版 5030／5035／5050 的共同 Clsn2。适配器只接受处于初段击飞且精确匹配原框投影的 `MoveAudit.hurt` 差异，逐条核对其余权威字段和视觉开关结果。State 245 另用 `standing_heavy_kick_duel.py` 对照旧许可的有效 tick。历史 Stage 14–26 权威回放和 13C Vulkan 门禁使用各自冻结预设。

```powershell
py -I tools/Stage29Validation/wave_coexistence.py --godot $godot --archive D:/ --output "$out/coexistence"
py tools/Stage27Validation/standing_heavy_kick_duel.py @fixed `
  --reference local-mugen/stage27-native-wave-extended/results `
  --profile-dir data/imported-characters/terry98-stage29-final --visual-alias terry98-stage29-default `
  --fixed-window 116 168 --timeout 100 --output "$out/legacy245" --scene near-wave-whiff-4 --scene near-wave-whiff-8
py -I tools/Stage29Validation/stage28_regression.py --godot $godot --output local-mugen/stage29-stage28-repro
```

若同候选的阶段 28 六个运行时控制已落盘，可用 `stage28_regression.py --compare-only --output <现有结果目录>` 只执行顺序精确的归档对照。

## 冻结回放与续接

阶段 14–26 使用固定输入的 headless 权威回放，13C 独立使用 Vulkan。可以按阶段分列不同输出目录。由 Mono 退出异常中断的 headless 回放使用新输出目录，并传入同构建已验证的结果目录及同名 `.log`。`--resume-headless-from` 可重复传入，逐条重核输入和权威记录，未匹配样本会实际重跑。

```powershell
$historical = @(14..26 | ForEach-Object { '--stage'; $_ })
py -3.11 -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ --headless-authority `
  --output local-mugen/stage29-frozen-repro @historical
py -3.11 -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ --stage 13C `
  --output local-mugen/stage29-frozen-13c-repro
```

## 构建和自动化测试

```powershell
dotnet build core/BattleCore/BattleCore.csproj -c Debug --no-restore
$savedExcludes = $env:DefaultItemExcludes
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
try { dotnet build NewKOF.csproj -c Debug --no-restore }
finally { $env:DefaultItemExcludes = $savedExcludes }
$tests = 'tests/BattleCore.Tests/BattleCore.Tests.csproj'
dotnet build $tests -c Debug --no-restore -t:Rebuild -p:BuildProjectReferences=false
$filter = 'FullyQualifiedName~GroundBlowbackTests|FullyQualifiedName~ForwardLightPunchTests|FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~ProjectileTimelineTests|FullyQualifiedName~MultiHitCancelTests|FullyQualifiedName~ScriptedPauseTests|FullyQualifiedName~ProximityGuardTests|FullyQualifiedName~FighterDataTests'
dotnet test $tests -c Debug --no-restore --no-build --filter $filter
dotnet test $tests -c Debug --no-restore --no-build
py -3.11 -m unittest discover -s tools/MugenImporter/tests -p test_state_ground_blowback.py
py -3.11 -m unittest discover -s tools/MugenImporter/tests
```
