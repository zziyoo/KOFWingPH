# 阶段 31 Godot 对局验证

在仓库根目录执行。构建与图集排除方式沿用 [阶段 30](../Stage30Validation/README.md#构建和图集)。使用 Godot 4.7.2 Mono Forward+ Vulkan。原版帧 `F` 对应 NewKOF 固定逻辑帧 `L=F+14`，提交版本 `S=L+1`。以下输出路径均须为新目录。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$env:PYTHONPATH = 'tools/MugenImporter'
$refs = @('pilot','input','audit','audit-more','edges') |
    ForEach-Object { '--reference'; "local-mugen/stage31-native-$_/results" }
$profile = 'data/imported-characters/terry98-stage31-final'
py -3.11 -m mugen_importer.state_rising_tackle local-mugen/terry.zip `
    data/imported-characters/terry98/manifest.json --base data/imported-characters/terry98-stage30-final `
    @refs --output $profile
foreach ($mode in 'default','mode2') {
    py -3.11 tools/Stage14Validation/stage_assets.py --binding "$profile/$mode.binding.json" `
       --destination "data/imported-characters/terry98-stage31-$mode"
}
& $godot --headless --editor --import --path .
$driver = 'tools/Stage31Validation/rising_tackle_duel.py'
$audit = @('audit','audit-more','edges','pilot','input') |
    ForEach-Object { '--reference'; "local-mugen/stage31-native-$_/results" }
$fixed = @('--godot',$godot,'--profile-dir',$profile,'--fixed-resolution','320x180','--compare-plain')
py -I $driver @fixed @audit --output local-mugen/stage31-repro-body --fixed-window 20 85 `
   --scene 1200-whiff --scene 1200-hit --scene 1200-guard --scene 1200-crouch-guard
py -I $driver @fixed @audit --output local-mugen/stage31-repro-reversal --fixed-window 25 52 `
   --scene 1200-reversal-cancel --scene 1200-reversal-miss --scene 1200-projectile-140-20
py -I $driver @fixed @audit --output local-mugen/stage31-repro-repeat `
   --fixed-window 20 85 --scene 1200-hit --repeat-from local-mugen/stage31-repro-body
```

`trace.json` 将实际 `FramePostDraw` PNG 与呈现提交版本、动作索引、AIR 元素、逻辑实例、冻结计数和碰撞事件对应。`candidate.json` 记录本次的两份 Debug DLL、Manifest、角色配置和 `project.godot`。沿用阶段 30 的冻结回放命令并使用新的输出目录。重跑时保留失败输出，只有相同构建的匹配记录可以用于继续上次回放。

## 2026-11-23 救援候选

使用新目录 `data/imported-characters/terry98-stage31-recovery-r2`，旧 final 配置和失败记录保留。首个 WIP 提交 `9bc743c` 保存原始候选，规则与审计修复提交 `83d7066`，均在本地 `wip/stage31-recovery`。

- 原规则失败复现两次：`local-mugen/stage31-recovery-repro-r1/r2.log`。
- 修复与审计等价：`stage31-recovery-fix-r2`、`stage31-recovery-batch-contract-r1`；后者可用上述固定帧入口加 `--repeat-from local-mugen/stage31-recovery-fix-r2` 复验。
- F/L/S 导出：`py tools/Stage31Validation/recovery_phases.py --native <签名jsonl> --before <旧duel.log> --after <新duel.log> --output <新json>`。包含原版三个相位与未改写的运行审计文本。
- 实时完整审计沿用 `--fps 30 --realtime-render 276` 或 `--fps 60 --realtime-render 294`；每次新输出目录，单独运行，勿与其他 Godot 回放并行。
- `performance_ab.py` 为独立、顺序的诊断入口，依赖本机 `psutil`（本轮 7.2.2）、`nvidia-smi`、`powercfg`。`--baseline-bin` 指向从固定阶段 30 源独立构建的两份 DLL，`--command-file` 指向上述实时入口产生的 duel.args.txt，`--candidate-profile` 指向新配置，`--output` 必须不存在。脚本先保存当前 DLL，再串行 A/B/A/B × audit ON/OFF，停止并等待其拥有的 Godot 进程树后还原 DLL；不修改电源方案。脚本拒绝已有 Godot 进程。输入 SHA 只取完整 renderkey 列表，两种配置与视觉别名分别显式记录。
- Godot 可选 `--audit-cost-probe` 记录 GD.Print/参数读取总耗时和调用数；`--sprite-audit-unbatched` 使用原逐行输出，供相同构建等价比较。系统 write/flush 次数未作推断。

当前修复原理和验收状态以 `docs/STAGE31_ACCEPTANCE.md` 最新救援轮章节为准；记录的旧阶段成功不能跨核心 DLL 指纹沿用。
