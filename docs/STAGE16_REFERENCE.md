# 阶段 16 原引擎基准

## 运行条件

用 Windows 上的 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0` 原始战斗内核运行 Terry 对 Terry 的 1v1 地面对照。仍使用 `Terry-KOF98.def`、KFM 场景、原始角色包与阶段 15 的配置和源文件哈希。场景进入后由夹具初始化位置、方向、速度和 RNG 种子 1337，经正常命令管线注入物理输入。P2 调色板 2，默认 P1 调色板 1，模式 2 的 P1 以调色板 7 通过原始开场 `Grant` 序列进入 `var(10)=2`。两种模式的 205/215 共用动作资源，远身重拳仍分别取 Action 210/211。[阶段 15 原引擎基准](STAGE15_REFERENCE.md)

`F` 从 RoundState 2 第一个逻辑 tick 起算。常规场景 P1 F5 按物理 `x` 或 `y`，入招为动作 tick 0。帧上 `after-collision` 的 `frame.sprite` 与 Clsn1/2 记录实际碰撞资源，`hitdef` 表示原脚本求值点，可能尚保留前一素材。`end` 给出本帧提交后的状态、生命和暂停。采集是引擎逻辑证据，NewKOF 的图形验收单独记录。

## 距离、时序与受击框

- `Terry.cmd:1567–1622` 先检查近身候选 205 和 215。实测地面相向时两个 Terry 的 `P2BodyDist X` 是中心距减两个前宽 15。轻拳分离 46（身体距 16）入 205，46.5 和 47 入 200。重拳分离 64（身体距 34）入 215，64.5 和 65 入 210。默认与模式 2 一致。原 `MugenVersion=1.0` 的距离触发保留小数。中心距 60 能触发原版 215，触发 205 则需要低于 NewKOF 默认推挤间距 60 的测试配置。
- State 205 以源 Action 205 的 3/4/5 tick 播放。起手 0–2，生效 3–6，恢复 7–11，tick 12 退出。其 Clsn1 局部 X [3,49]、Y [67,91]（Y 向上）。普通轻拳在 F8 接触，F9 目标生命 970，原始 HitDef38，攻击倍率 .8。击停攻方 11、目标 13，地面 HitTime/SlideTime 9。目标从 F22 开始以 +6 位移，F32 恢复控制，攻方 F28 恢复。早蹲防和接触帧蹲防均合法，F8 格挡零削血并进入 State 152，下一帧 Action 151，F20 推退，F29 可控。站防是 State 150→151，Action 150，攻守停顿均为 11。
- State 215 使用 Action 215 的 3/3/7/7/8 tick。起手 0–2，第 1 段 3–5，第 2 段 6–12，恢复 13–27，tick 28 退出。第一段 Clsn1 X [14,47]、Y [26,80]，第二段 X [30,75]、Y [44,80]。源 `Terry-N.cns:336–498` 进入设置 `var(23)=1`，当普通命中或格挡形成 MoveContact 时按条件增至 2。第 3 元素 `MoveHitReset` 使第二 HitDef 独立接触，两个 HitDef 的源 ID 都为 10。原始目录保存逐元素 Clsn2，例如 215 生效一是 `[-20,-80,19,-1]`、`[9,-92,31,-69]`，生效二是 `[-4,-80,35,-1]`、`[8,-92,47,-53]`，Y 向下。
- 已使用真实 Terry Manifest 逐帧解析 Action 205/215 的图集矩形并检查非零 alpha。205 帧索引 167/168/165，215 为 179/180/181/182/1。已有 Terry 其他动作的缺图继续由导入诊断记录。

## 215 两段实测

| 情况 | 第一段 | 第二段 | 恢复 |
| --- | --- | --- | --- |
| 双段命中，中心距 62 | F8，HitDef 76 实扣 61，F9 生命 939，水平速度 0 | F22，HitDef 38 实扣 29，F23 生命 910，水平速度 +10.5 | 目标 F36 推退、F54 可控，攻方 F55 可控 |
| 站防，两段 | F8 格挡，零削血，进入 State 150 | F22 在首段 Blockstun 的 State 151 中再次格挡，重进 150，零削血 | F34 开始推退，目标 F51 可控，攻方 F55 可控 |
| 早/晚蹲防，两段 | F8 蹲防 State 152，零削血 | F22 在 State 153 中再次蹲防，重进 152，零削血 | F34 开始推退，以 .82 至 X87.33462，F51 可控 |
| 第一段空挥、第二段命中，中心距 64 | P2 F5 仅按后一次，退至 X35.16797，实际间距 67.16797 超出第一段与站姿受击框的接触距离 66。F8–10 均空挥 | F11 接触，`var(23)=1,var(21)=0`，HitDef 86 实扣 69，F12 生命 931 | 目标 F25 推退、F43 可控，攻方 F44 可控 |
| 第一段空挥、第二段站防，中心距 64 | P2 从 F5 一直按后到 F55，F8–10 仍空挥 | F11 在 State 130 格挡第二段，零削血，水平速度 +10.5 | 目标 F23 推退、F40 可控，攻方 F44 可控 |

两段命中各自重新给双方 11/13 tick 停顿，格挡为 11/11。两段 HitTime/SlideTime 均配置 17。第 2 段受击目标经历 F22–35 Shake、F36–53 滑移，首个可控帧 F54。格挡则是 F22–33 Shake、F34–50 滑移，首个可控帧 F51。第一段原版未在普通中立地面命中后单独恢复，实测第一段和第二段的原始参数相同。215 第一段格挡及命中均没有击退，第二段局部 HitDef X −10.5，向右打中时目标世界坐标增加，左向镜像反号。

连续命中的 38 点第二段 HitDef 乘攻方 .8，再除以已受击目标的有效防御约 1.030928 后取整为 29。第一段空挥时，第二段同时受不同的 `var(21)` 样本和中立防御倍率影响，86 点 HitDef 实扣 69。

追加 6 项确认两个模式的连续防御行为。P2 在 F7–8 站防，F9 释放方向，F22 仍在 State 151 内成功格挡。P2 F5–8 蹲防，F9 释放所有方向或只保留后方向，F8 为 State 152，F20 变为站立 State 151，F22 的新格挡进入 State 150 并用 Action 150。两段均零削血，第二段采用站姿 .85 保留，目标 F51 恢复控制。原引擎 `char.go:12833–12834` 在活动的 Guard State 内维持防御标记。

两个预设的轻重拳均有合法空挥路径。P2 F0 原地起跳，P1 F13 以初始分离 36（轻）或 54（重）出拳，仍进入原始 205/215。目标已高于攻击范围，轻拳 F16–19、重拳 F16–25 的 HitDef 全部落空，攻击者分别在 F25、F41 恢复控制。该样本验证原始判定及空挥退出，空中命中规则不在阶段范围。

同时出拳的真实混合样本有两个。中心距 44 的 P1 205 对 P2 215 于 F8 两边都命中，双方分别受到 61、30。中心距 62 的 P1 215 对 P2 200 于 F8 两边都命中，双方各受 54。两场均有 `var(6)=1`，伤害还依赖 `var(21)`，原版 215 的第二段被受击打断。不能用固定普通模式伤害推断所有交互的原值。

同席同时按 `x+y` 的四项补测均在 F5 `after-command` 中得到两个有效命令。两个模式在中心距 44 时优先选 205，F8 接触并扣 30。在中心距 70 时优先选 200，F8 接触并扣 46。两种距离的重拳都可作为候选，实际选择与源 CMD 的轻拳优先顺序一致。

## 证据与复现

标准证据 `local-mugen/stage16-reference/results/`，46 项断言通过。初次执行文件 SHA-256 `f479139098dd610c013f6596722dd26099ce9d4450a4ba2b7d324d71798da917`。各 JSONL 的 SHA 和运行条件记录于对应 `*-metadata.json`，模式入口记录于 `mode2-*-mode.jsonl`。原始 stderr 保留素材和旧脚本诊断。采集器会拒绝覆盖既有结果，请使用新目录。

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage16 -Prepare -RunDirectory local-mugen/stage16-reference-new
python tools/Stage16Reference/Check-Traces.py local-mugen/stage16-reference-new/results
```

释放防御补测独立保存在 `local-mugen/stage16-reference-guard-release/results/`，执行文件 SHA-256 `75ecf34c91e2403902e0fece5a7f56171b7961245b634c143830378f1ef0b070`。

```powershell
$guardRelease = 'default-heavy-guard-release','default-heavy-crouch-release','default-heavy-crouch-to-stand','mode2-heavy-guard-release','mode2-heavy-crouch-release','mode2-heavy-crouch-to-stand'
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage16 -Prepare -RunDirectory local-mugen/stage16-reference-guard-release-new -Cases $guardRelease
python tools/Stage16Reference/Check-Traces.py local-mugen/stage16-reference-guard-release-new/results --guard-release
```

同席同时按键补测独立保存在 `local-mugen/stage16-reference-simultaneous/results/`，执行文件 SHA-256 `e82c11f977b6e61c844e46e9ca2326bde5d45d44fa6181f1bb540317c6e083c2`。

```powershell
$simultaneous = 'default-light-both-44','default-light-both-70','mode2-light-both-44','mode2-light-both-70'
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage16 -Prepare -RunDirectory local-mugen/stage16-reference-simultaneous-new -Cases $simultaneous
python tools/Stage16Reference/Check-Traces.py local-mugen/stage16-reference-simultaneous-new/results --simultaneous
```

标准、释放防御和同席同时按键的 `run-metadata.json` SHA-256 依次为 `1927d1c93e7cbfb6bd592508920ebbadc5474421de93c6cb39f9bdb3d126c3ae`、`58ce04c812a3fc887f6f9f4e2826261b84cf5e5c62e5da50f58c35ad4c205247`、`24e344003ba17ba4547b31b72ec7d1b2869c15a65fda80e7ba792cb9b920c472`。
