# 阶段 27 Vulkan 回放

从 NewKOF 根目录运行。先按原引擎参考准备签名轨迹，生成并安装阶段 27 两套角色配置及视觉绑定。使用 Godot 4.7.2 Mono 与 Vulkan Forward+。原引擎 F 对应 NewKOF 逻辑 `L=F+14`，画面读回关联提交版本 `S=L+1`。

固定回放和实时采样串行使用 GPU。若冻结队列正在运行，先启用其 `--hold-file`，等待日志出现 `Queue paused` 后再运行本节其余图形命令。结束后仅删除该次创建的暂停文件，冻结队列继续执行。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$driver = 'tools/Stage27Validation/standing_heavy_kick_duel.py'
$lite = 'tools/Stage26Validation/standing_light_kick_duel.py'
$out = 'D:/NewKOF-stage27-repro'
$profile = 'data/imported-characters/terry98-stage27-final'
$fixed = @('--godot',$godot,'--compare-plain','--fixed-resolution','320x180')
$native = 'local-mugen/stage27-native-canonical'
$nativeBatches = Get-Content tools/Stage27Reference/Batches.json -Raw | ConvertFrom-Json -AsHashtable
$refs = @($nativeBatches.Keys | ForEach-Object { '--reference'; "$native/$_/results" })

py $driver @fixed @refs --output "$out/body" --scene near-hit --scene far-hit --scene near-crouch-guard `
  --scene far-guard --scene near-interrupt --scene far-interrupt --scene mirror-near-hit --scene mirror-far-hit `
  --scene p2-near-hit --scene p2-far-hit
py $driver @fixed @refs --mode mode2 --output "$out/mode2" --scene near-hit --scene far-hit --scene far-whiff `
  --scene far-guard --scene far-crouch-guard

py $driver @fixed @refs --output "$out/distance" --scene threshold-below --scene threshold-exact --scene threshold-above `
  --scene sweep-b-buffer-toward --scene sweep-b-buffer-away-ground --scene sweep-b-buffer-release-down `
  --scene sweep-b-buffer-down --scene sweep-old-a-new-b --scene near-to-far-new-instance --scene far-to-near-new-instance `
  --scene sweep-walk-threshold-a-near --scene sweep-walk-threshold-a-far `
  --scene sweep-walk-threshold-b-near --scene sweep-walk-threshold-b-far

py $lite @fixed --profile-dir $profile --visual-alias terry98-stage27-default `
  --reference local-mugen/stage26-native-grounded-205-final/results --output "$out/sweep-205" `
  --fixed-window 76 140 --timeout 100 --scene near-to-close-jab-wave-grounded-held --scene near-to-close-jab-wave-grounded-release

py tools/Stage27Validation/sweep_recovery.py @fixed @refs --output "$out/sweep-recovery" `
  --scene sweep-walk-forward --scene sweep-walk-back --scene sweep-walk-down-forward --scene sweep-walk-idle --scene sweep-walk-attack

py $driver @fixed --reference local-mugen/stage27-native-away-guard/results @refs --output "$out/outward" `
  --scene sweep-b-buffer-away-ground --scene sweep-b-buffer-away-ground-guard

py $driver @fixed @refs --output "$out/wave-boundaries" --scene near-wave-hit-4 --scene near-wave-guard-8 `
  --scene near-wave-pause-3 --scene near-wave-whiff-9 --scene far-wave-whiff-13 --scene far-wave-whiff-38 `
  --scene far-global-pause-b-102 --scene far-global-pause-b-103 --scene far-global-pause-b-106

py $driver @fixed --reference local-mugen/stage27-native-wave-extended/results @refs --output "$out/wave-extended" `
  --fixed-window 116 168 --timeout 100 --scene near-wave-whiff-4 --scene near-wave-whiff-8 `
  --scene near-wave-whiff-29 --scene near-wave-mixed-4

py $driver @fixed @refs --output "$out/isolation" --scene near-mix-a-b --scene near-mix-x-b --scene near-mix-y-b `
  --scene near-mix-d-y-b --scene throw-range-f-b --scene throw-range-f-b-above --scene near-early-4-f-x `
  --scene near-early-8-d-f-y --scene near-early-9-f-x

py $driver @fixed --reference local-mugen/stage27-native-wakeup-reservation-probe/results @refs `
  --output "$out/wakeup-805" --scene wakeup-reservation-84 --scene wakeup-reservation-95

py $driver @fixed @refs --mode mode2 --output "$out/mode2-isolation" --scene near-wave-whiff-4 `
  --scene near-wave-whiff-9 --scene near-wave-whiff-29 --scene far-wave-whiff-43 `
  --scene near-mix-y-b --scene throw-range-b-b

py $driver @fixed @refs --output "$out/ordinary" --scene near-early-4-a --scene near-early-9-x `
  --scene near-natural-a --scene near-natural-d-x --scene near-natural-b --scene far-early-b --scene far-natural-b
py $driver @fixed @refs --output "$out/repeat" --repeat-from "$out/distance" --scene threshold-exact
```

`sprite/trace.json` 保存输入、命令候选、出招、身体距离、攻击实例、碰撞框、受击反应、实际图像及图像对应动作。`distanceSelections` 保存按键时和实际消费时的源和当前身体距离及输入年龄。`attackerSteps` 核对 240／241 的原版 PosAdd，镜像下使用当帧朝向换算。伤害分别用两引擎各自的权威 RNG 与相同公式重算。图像不参与战斗决策。

实时取证每档只读取一张 `FramePostDraw` 图片，分别保存实测帧率和积压相位。等待 GPU 和其他验收进程空闲后串行执行。

```powershell
py $driver --godot $godot @refs --output "$out/rt30" --fps 30 --realtime-render 274 --scene near-hit
py $driver --godot $godot @refs --output "$out/rt60" --fps 60 --realtime-render 276 --scene far-whiff
```

## 历史和新配置共存门禁

冻结门禁按原阶段角色和输入在当前 Godot Debug 运行时重新执行，并核对实际权威日志。13C 直接调用既有 Vulkan 门禁。阶段 14–25 使用阶段 26 已对原始轨迹验证过的实际回放输出，断点归档按每个变体选齐。阶段 26 使用 55 组正式证据。`--headless-authority` 保留相同调用次序、60 固定帧、输入注入和两套视觉开关，检查全部物理输入注入日志与权威记录。历史 CPU 模式没有图像读回，仍可用串行 Vulkan 模式复跑。新配置共存门禁单独跑旧 Wave、430／235 和 440 已验收场景。跨配置比较只忽略角色 ID 和命令候选扩展，当前开关视觉的完整权威记录仍逐条相等。

阶段 18 早期回放保存的文件名位于 `res://local-mugen/stage18-import-smoke-v2/`。CPU 门禁在核对两个 SHA-256 后从已封存的 `terry98-stage18-data` 读取同版配置。若用原命令逐场跑 Vulkan，先在空的旧路径下放入这两份冻结配置的副本。黄金日志与输入脚本无需修改。

```powershell
py tools/Stage27Validation/frozen_replay.py --archive-only --archive D:/
py -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ `
  --stage 13C --output local-mugen/stage27-frozen-13c-repro
$historical = @(14..26 | ForEach-Object { '--stage'; $_ })
py -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ --headless-authority `
  --output local-mugen/stage27-frozen-cpu-repro @historical
py -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ `
  --output local-mugen/stage27-frozen-vulkan-repro --hold-file local-mugen/stage27-gpu-hold.flag
py -u tools/Stage27Validation/current_profile.py --godot $godot --archive D:/ --output "$out/current-profile"
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
```
