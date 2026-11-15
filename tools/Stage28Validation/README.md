# Stage 28 validation

从 NewKOF 根目录运行。先按 [原版取证](../Stage28Reference/README.md) 的批次准备签名轨迹（或直接使用 `local-mugen/stage28-native-*` 捕获目录），并生成/暂存阶段 28 两套配置（`data/imported-characters/terry98-stage28-final` 与视觉别名 `terry98-stage28-default`/`terry98-stage28-mode2`）。使用 Godot 4.7.2 Mono。

## 原生探针核对

82 个固定 IKEMEN GO 用例（Cases.json 见 `tools/Stage28Reference/`），覆盖自由入口、Helper 锁存（含持下与松向）、冻结/分离输入、年龄边界、八个来源的取消窗口首/末/越界、430/240 否定、双段命中、Wave 出边（含停顿冻结命令缓冲）、自然重开与全局暂停。

```powershell
py -I tools/Stage28Reference/Check-Traces.py local-mugen/stage28-native-probe3 local-mugen/stage28-native-probe4 `
  local-mugen/stage28-native-probe5 local-mugen/stage28-native-probe5b local-mugen/stage28-native-probe5c `
  local-mugen/stage28-native-probe5d local-mugen/stage28-native-probe6 local-mugen/stage28-native-probe6b `
  local-mugen/stage28-native-probe6c local-mugen/stage28-native-probe6d local-mugen/stage28-native-probe6e `
  local-mugen/stage28-native-probe7 local-mugen/stage28-native-probe7b local-mugen/stage28-native-probe7c `
  local-mugen/stage28-native-probe7d local-mugen/stage28-native-probe7e local-mugen/stage28-native-probe7f `
  local-mugen/stage28-native-probe7g local-mugen/stage28-native-probe8 local-mugen/stage28-native-probe8b `
  local-mugen/stage28-native-probe8c local-mugen/stage28-native-probe8d local-mugen/stage28-native-probe8e `
  local-mugen/stage28-native-probe9 local-mugen/stage28-native-probe9b local-mugen/stage28-native-probe9c
```

期望输出 `PASS: 82 native Stage 28 fixtures` 且 `completeVerified=true`。`stage28-native-probe`/`probe2` 是早期部分捕获（一次 PS 5.1 的 Start-Process ExitCode 缺陷与一次用例调度修正），保留作工具故障证据，不参与核对。

## 生成与暂存

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
$refs = @('probe3','probe4','probe5','probe5b','probe5c','probe5d','probe6','probe6b','probe6c','probe6d','probe6e',
          'probe7','probe7b','probe7c','probe7d','probe7e','probe7f','probe7g','probe8','probe8b','probe8c','probe8d',
          'probe8e','probe9','probe9b','probe9c') | ForEach-Object { '--reference'; "local-mugen/stage28-native-$_/results" }
py -m mugen_importer.state_forward_light_punch local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --base data/imported-characters/terry98-stage27-final @refs --output data/imported-characters/terry98-stage28-final
py tools/Stage14Validation/stage_assets.py --binding data/imported-characters/terry98-stage28-final/default.binding.json `
  --destination data/imported-characters/terry98-stage28-default
py tools/Stage14Validation/stage_assets.py --binding data/imported-characters/terry98-stage28-final/mode2.binding.json `
  --destination data/imported-characters/terry98-stage28-mode2
```

重复生成应使用空输出目录并比较六份 JSON 的 SHA-256（两轮逐字节一致为确定性证据）。

## 真实运行时场景

六个代表场景（自由命中、Helper 松向、440 取消首 tick、窗口越界拒绝、双停顿后的 Wave 出边、模式 2 一致性）在真实 Godot 运行时回放阶段 28 配置，核对 `MoveStarted` 权威行：

```powershell
py -I tools/Stage28Validation/runtime_check.py --godot $godot --output local-mugen/stage28-runtime-check
```

期望 `PASS: 6 Stage 28 runtime scenes`。选人序列固定（Key1/J/Comma/Enter），P1 前进为 D 键、轻拳 J、下 S、重脚 I、轻脚 U；P2 防御持 Right。逻辑帧 = 渲染帧 − 约 9–10（选人后偏移），场景按键按此校准。

## 冻结门禁

沿用 [Stage27 README](../Stage27Validation/README.md) 的冻结入口；阶段 28 收口在新构建指纹上重放阶段 13C–27：

```powershell
py -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ --headless-authority `
  --output local-mugen/stage28-frozen-a --stage 14 --stage 15 --stage 16 --stage 17 --stage 18 --stage 19 --stage 20
py -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ --headless-authority `
  --output local-mugen/stage28-frozen-b --stage 21 --stage 22 --stage 23 --stage 24 --stage 25 --stage 26
py -u tools/Stage27Validation/frozen_replay.py --godot $godot --archive D:/ `
  --output local-mugen/stage28-frozen-13c --stage 13C
```

完成以各日志的 `Matched Stage N` 行为准；输出目录由脚本以 `exist_ok=False` 创建，禁止复用。
