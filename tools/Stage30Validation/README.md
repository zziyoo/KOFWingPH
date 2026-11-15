# 阶段 30 State 310 运行时验收

所有命令从 NewKOF 根目录执行。使用 Godot 4.7.2 Mono Forward+ Vulkan，原版参考取自阶段 30 的签名轨迹。原版脚本和固定输入见 [原引擎取证](../Stage30Reference/README.md)。

## 构建和图集

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build core/BattleCore/BattleCore.csproj -c Debug --no-restore
$savedExcludes = $env:DefaultItemExcludes
$env:DefaultItemExcludes = 'local-mugen/**;data/imported-characters/**;shots/**;**/.git/**'
try { dotnet build NewKOF.csproj -c Debug --no-restore }
finally { $env:DefaultItemExcludes = $savedExcludes }
$tests = 'tests/BattleCore.Tests/BattleCore.Tests.csproj'
dotnet build $tests -c Debug --no-restore -t:Rebuild -p:BuildProjectReferences=false
$profile = 'data/imported-characters/terry98-stage30-final'
foreach ($mode in 'default','mode2') {
  py -3.11 tools/Stage14Validation/stage_assets.py --binding "$profile/$mode.binding.json" `
    --destination "data/imported-characters/terry98-stage30-$mode"
  if ($LASTEXITCODE) { throw "Stage 30 $mode visual staging failed" }
}
& $godot --headless --editor --import --path . --log-file local-mugen/stage30-atlas-import-engine.log
```

完成配置生成后执行视觉暂存。检查 `candidate.json` 记录的 Debug DLL、双模式六个 JSON、Manifest 和 `project.godot` 哈希。若编辑器导入改变 DLL，使用同一最终候选进行验收。

## 物理输入和 Vulkan 实图

回放器复用阶段 26 的签名核对、物理键盘输入和 FramePostDraw。原版帧 `F` 对应 NewKOF 逻辑帧 `L=F+14`，图像提交版本为 `S=L+1`。每场检查实际输入、命令消费、攻击实例、接触事件、伤害及恢复。固定帧在两种视觉覆盖变体之间逐条比较权威记录。实际图像和所依据的已提交版本存于 `sprite/trace.json` 与 `sprite/frames/`。

```powershell
$driver = 'tools/Stage30Validation/rising_upper_duel.py'
$out = 'D:/NewKOF-stage30-repro'
$refs = @('audit','audit-more','pilot','input','routes','wave','wave-edge','contact','tackle','exit','projectiles','245-whiff') |
  ForEach-Object { '--reference'; "local-mugen/stage30-native-$_/results" }
$fixed = @('--godot',$godot,'--compare-plain','--fixed-resolution','320x180','--timeout','100')
py -I $driver @fixed @refs --output "$out/body" --fixed-window 28 70 `
  --scene 310-hit --scene 310-crouch-hit --scene 310-crouch-guard --scene 310-whiff
py -I $driver @fixed @refs --mode mode2 --output "$out/mode2" --scene 310-hit
py -I $driver @fixed @refs --output "$out/recovery" --fixed-window 28 95 `
  --scene 310-input-held --scene 310-recovery-down --scene 310-recovery-forward --scene 310-recovery-up `
  --scene 310-recovery-down-x --scene 310-recovery-down-y
py -I $driver @fixed @refs --output "$out/late-wave" --fixed-window 28 115 `
  --scene 310-wave-walking-hit-22 --scene 310-wave-walking-hit-23 --scene 310-projectile-cross-140-20
py -I $driver @fixed @refs --output "$out/pursuit" --fixed-window 64 153 `
  --scene 310-entry-250-hit --scene 310-entry-250-counter --scene 310-wave-250-guard-54
py -I $driver @fixed @refs --output "$out/routes" `
  --scene 310-entry-200-first --scene 310-contact-entry-205-hit-first --scene 310-contact-entry-215-hit-first `
  --scene 310-contact-entry-235-hit-first --scene 310-contact-entry-245-hit-first --scene 310-entry-250-first `
  --scene 310-entry-400-first --scene 310-entry-410-last --scene 310-entry-440-last
py -I $driver @fixed --reference local-mugen/stage30-native-input/results `
  --output "$out/pause" --fixed-window 124 180 `
  --scene 310-global-pause-102 --scene 310-global-pause-103 --scene 310-global-pause-104
py -I $driver @fixed @refs --output "$out/repeat" --repeat-from "$out/body" `
  --fixed-window 28 70 --scene 310-hit
py -I $driver --godot $godot @refs --output "$out/realtime30" --scene 310-hit --fps 30 --realtime-render 276
py -I $driver --godot $godot @refs --output "$out/realtime60" --scene 310-hit --fps 60 --realtime-render 294
```

选择原版签名集内的场景名，省略文件前缀 `default-standing-heavy-kick-` 或 `mode2-standing-heavy-kick-`。入边覆盖九个普通技、冻结输入的根与 Helper 分离、旧 250 窗口边界。长链用 `--fixed-window` 保证仍能观察完整 Wave 动作，权威日志也需覆盖其后实际恢复。全局 Pause 回放选取标准输入批次的 F120 长轨迹。只读命令探针的 F105 样本可证明根与 Helper 的取值，长度不足以验收前置 Wave 的完整恢复。实时运行与固定采集串行执行，30、60 两档各一次稀疏读回。定位时先查每场 `duel.args.txt` 和 `duel.log`，使用新输出目录保留失败。

## 最终构建回归

```powershell
# 13C 使用实际 Vulkan，阶段 14–26 使用固定物理输入的 headless 权威对照。
$history = @(14..26 | ForEach-Object { '--stage'; $_ })
py -3.11 -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ `
  --stage 13C --output local-mugen/stage30-frozen-13c-repro
py -3.11 -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ `
  --headless-authority --output local-mugen/stage30-frozen-repro @history
py -I tools/Stage29Validation/stage28_regression.py --godot $godot `
  --output local-mugen/stage30-stage28-repro
py -I tools/Stage30Validation/stage29_regression.py --godot $godot --archive D:/ `
  --output "$out/stage29"
py -I tools/Stage30Validation/stage29_regression.py --godot $godot --archive D:/ --old-wave-only `
  --output "$out/old-wave"
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```

同一构建的 headless 历史回放可按阶段分组。中断后的复跑使用新输出目录和每个同名 `.log`，传入一次或多次 `--resume-headless-from`，由脚本复核输入和归档权威行后补跑缺项。只有出现所选阶段的 `Matched Stage N` 才计入本次总数。历史阶段 29 回放选择普通吹飞禁追、反击后飞行道具续追、转 300 后防御或禁追、直接 Wave 和模式 2 的保留许可。脚本在阶段 29 封存配置上回放并比较原权威记录，再在阶段 30 配置上重跑相同物理输入。

```powershell
$heavy = 'tools/Stage27Validation/standing_heavy_kick_duel.py'
$current = @('--profile-dir',$profile,'--visual-alias','terry98-stage30-default')
py $heavy @fixed --reference local-mugen/stage27-native-wakeup-reservation-probe/results `
  --output "$out/legacy805" --fixed-window 105 185 --scene wakeup-reservation-84 --scene wakeup-reservation-95
py $heavy @fixed --mode mode2 --reference local-mugen/stage27-native-canonical/body-extra/results `
  --reference local-mugen/stage27-native-canonical/engine/results `
  --output "$out/legacy241" --scene far-guard --scene far-crouch-guard
py $heavy @fixed @current --reference local-mugen/stage27-native-wave-extended/results `
  --output "$out/legacy245" --fixed-window 116 168 --scene near-wave-whiff-4 --scene near-wave-whiff-8
py tools/Stage26Validation/standing_light_kick_duel.py @fixed @current `
  --reference local-mugen/stage26-native-grounded-205-final/results `
  --output "$out/legacy440" --fixed-window 75 164 --scene near-to-close-jab-wave-grounded-held
```

```powershell
$filter = 'FullyQualifiedName~RisingUpperTests|FullyQualifiedName~GroundBlowbackTests|FullyQualifiedName~ForwardLightPunchTests|FullyQualifiedName~CommandPriorityTests|FullyQualifiedName~ProjectileTimelineTests|FullyQualifiedName~MultiHitCancelTests|FullyQualifiedName~ScriptedPauseTests|FullyQualifiedName~ProximityGuardTests|FullyQualifiedName~FighterDataTests|FullyQualifiedName~SpriteBindingTests'
dotnet test $tests -c Debug --no-restore --no-build --filter $filter
dotnet test $tests -c Debug --no-restore --no-build
$env:PYTHONPATH = 'tools/MugenImporter'
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
py -3.11 -m unittest discover -s tools/MugenImporter/tests -p test_state_rising_upper.py
py -3.11 -m unittest discover -s tools/MugenImporter/tests
```
