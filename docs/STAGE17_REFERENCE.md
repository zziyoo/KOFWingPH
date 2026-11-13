# 阶段 17 原版 Terry 伤害基准

## 采集范围

在 Windows 的 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0` 中，用未经修改的 `Terry-KOF98.def`、原始 Terry 包和 KFM 场景运行 1v1 地面对局。沿用[阶段 16 夹具](STAGE16_REFERENCE.md)的物理输入管线。进入 RoundState 2 后，夹具一次性设定双方位置、朝向、初速度与全局 RNG 种子。默认 P1 用调色板 1，模式 2 用调色板 7 和原版开场 `Grant` 输入得到 `var(10)=2`。P2 用调色板 2。镜像场景反转双方位置和朝向，并反转防御方向输入。F0 是 RoundState 2 的第一个逻辑 tick。

在独立引擎副本记录 `Random()` 的前后状态、根角色 `VarRandom(21)` 的取值、`var(6)` 和 `var(23)` 的写入、实际 HitDef、碰撞结果及每帧提交。采集保持原控制器、素材、碰撞及伤害公式。种子在夹具 F0 设定，原引擎全局流仍包含 Helper、音效等脚本用途的随机抽样，角色控制器执行顺序也会影响各席拿到的值。该记录能证明原版公式与观察到的随机选择。NewKOF 的独立战斗 RNG 需要单独验收，固定种子数值相同不足以保证两种引擎具有相同的全局抽样序列。

## 61 与 54 的可重复来源

同一个打断场景中，中心距 72，P1 在 F5 按 `y`，P2 同帧按 `x`。F8 P2 State 200 命中仍处于远身重拳起手的 P1，三个种子下的接触帧、出手者、反击变量 1、攻方倍率约 0.8 和目标有效防御 1 都相同。只有原控制器抽中的 `var(21)` 变化。

- 种子 1，P2 抽取 RNG `590357944`，补正 0，HitDef 76，实扣 61，F9 生命 939。
- 种子 1337，P2 抽取 RNG `1182072679`，补正 1，HitDef 67，实扣 54，F9 生命 946。
- 种子 2026，P2 抽取 RNG `2064286812`，补正 2，HitDef 57，实扣 46，F9 生命 954。

默认与模式 2 都得到这三组结果。固定种子 1337 的 13 项独立重跑，其 RNG、反击变量写入、碰撞结算事件流和归一化对局快照逐项相同。快照比较去掉全局时间戳以及 F0 在 State -2 首次抽样前继承开场随机值的两条记录。固定同一种子而改变合法输入也能改变抽样。P1 的 215 第一段在普通命中时抽到 `1035193108`，得到补正 1、实扣 61。在 P2 同帧出远身轻拳并相杀时，P1 抽到 `1725541809`，得到补正 2、实扣 54。这两次 215 的源伤害基数、分母、攻防倍率相同。

## 原版控制器及结算

- `Terry-2-3.cns:421–437` 中的三个 State -2 控制器依序处理反击变量。观测到对手 `MoveType=A` 时设置 `var(6)=1`。离开 A 后，未受攻方 HitPause 阻止的控制器执行依序产生 2、3、4，第三个控制器在同一次执行中把 4 清为 0。非零值均允许原反击公式。相杀时两个角色读取的是各自控制器执行点的对手状态。
- `Terry-2-3.cns:615–619` 中的 State -2 `VarRandom` 取 `var(21)=0..2`。`common.go` 中每次 `Random()` 等价于 `r = (r * 16807) mod 2147483647`，此范围的结果是 `r // 715827883`。HitDef 采用相应席位最近一次 `VarRandom(21)` 取值。实际攻方 HitPause 暂停相应控制器，目标 HitShake 与其独立。打断场景 F8 的碰撞中间快照显示攻方 `hitPause=12`、目标 `hitShake=13`，后续提交按[阶段 16 停顿记录](STAGE16_REFERENCE.md)推进。
- `Terry-N.cns` 中的普通／反击基数分别为 200 的 7／8，205 的 5／6，210 的 16／20。Mode 2 的 210 使用 Action 211，伤害表达式保持一致。215 两段均使用基数 9，在乘 `fvar(7)=9.615` 并向下取整前，对 `(9-var(21))/var(23)` 做整数除法。其第一段接触命中或格挡后，在符合原脚本触发条件时写入分母 2。第一段落空仍用分母 1。
- 原始 HitDef 经攻方倍率及目标当时的有效防御结算，正伤害按原引擎 `computeDamage` 四舍五入。此批地面普通攻击的格挡削血为 0。碰撞所在 F 记录结算值及提交前生命，F+1 扣除。State -2 的 `DefenceMulSet` 写入 .97，原引擎对该 MUGEN 角色按倒数转换，并在目标处于 `MoveType=H` 时应用。第二段连续命中的有效防御约 `1.0309277772903442`。第一段落空后目标在第二次碰撞时仍使用有效防御 1。

### 样本和边界

- 普通命中种子 1337，200 在 F8 用补正 1、HitDef 57、实扣 46，205 在 F8 用补正 1、HitDef 38、实扣 30。210/211 在 F10 用补正 1、HitDef 144、实扣 115。远身轻重拳相杀 F10，State 200 补正 1、实扣 54，State 210 补正 0、实扣 154。近身 205/215 相杀 F8，分别实扣 30 和 61。近身 215 与远身 200 相杀 F8，种子 1337 两边实扣 54，215 的第二段因中断未发生。
- 215 双段命中种子 1337，中心距 62，P1 F5 按 `y`，第一段 F8 的分母 1、补正 1、HitDef 76、实扣 61，第二段 F22 的分母 2、补正 1、HitDef 38、实扣 29。第二段单独命中场景中心距 64，P2 F5 向后一步使第一段 F8–10 落空，第二段在 F11 以分母 1 接触。种子 1337 抽到补正 0、实扣 69，种子 2026 抽到补正 2、实扣 54。默认、模式 2 及种子 1337 的左右镜像均有运行证据。
- 站防补测使用种子 1337、中心距 62、P1 F5 按 `y`。P2 从 F7 开始按后，215 在 F8/F22 两段均被格挡，分母依次 1/2，HitDef 76/38，目标两次 HitShake 11 且生命保持 1000。P2 提前至 F4 按后时，后退使 F5 选择 210/211，F10 单次格挡，HitDef 144，目标生命保持 1000。两种预设及左右镜像分别采集。
- 默认模式中心距 80，P2 用 State 200 空挥。P2 F2、P1 F5 出拳时，P1 F10 命中 P2 的 State 200／MoveType I，反击变量 2、实扣 154。P2 改为 F1 出拳，P1 保持 F5，F10 命中时变量 3、实扣 154。P2 F1、P1 F6 时，F11 先 3→4→0，再以普通基数实扣 122。P1 延至 F8 时，F13 以已过期变量 0 和新补正 2 实扣 107。四例均为种子 1337。
- 原版模式 2 的 Action 211 起手受击框向前达 27，默认 Action 210 的对应身体框只达 19。同一中心距 80 的 `carry-live`、`carry-edge`、`carry-last` 中，P2 State 200 改为在 F6、F6、F7 打断 P1，实扣均为 46。它们是该几何条件下的原版打断样本。模式 2 的 `carry-expired` 仍在 F13 正常命中，反击变量 0、实扣 107。

## 复现

主运行和种子矩阵使用执行文件 SHA-256 `419b8f34748e3fce4d74ad58fa90f04f70f0dea16ae6623de0b3810abb4b49b4`，反击边界补测使用 `2c3e0dff51257e13e0ce13ba8cc477b62e87ebf78c99fbca4fed60adf899c095`。每个 `results/run-metadata.json` 记录该次引擎、角色包、源文件、配置、种子及场景清单。各 `*-metadata.json` 记录输入场景、距离及 JSONL 的 SHA-256。模式入口日志保存在对应 `*-mode.jsonl`。后续采集会验证原始 `Terry-2-3.cns` SHA-256 `90b83cdacd33a3800a825ee42bdbd6502b7989f4dec358126587edeb4495fdc4`。采集器拒绝覆盖已存在的运行目录。

```powershell
$run = 'tools/Stage14Reference/Run-Reference.ps1'
$cases = @(
    'default-damage-near-heavy-trade','default-damage-heavy-near','default-damage-second-only','default-damage-far-trade','default-damage-interrupt',
    'default-damage-carry-live','default-damage-carry-last','default-damage-carry-expired',
    'mode2-damage-near-heavy-trade','mode2-damage-heavy-near','mode2-damage-second-only','mode2-damage-far-trade','mode2-damage-interrupt'
)
& $run -CaptureProfile Stage17 -Prepare -Seed 1337 -RunDirectory local-mugen/stage17-reference-new
& $run -CaptureProfile Stage17 -Prepare -Seed 1 -Cases $cases -RunDirectory local-mugen/stage17-reference-seed-1-new
& $run -CaptureProfile Stage17 -Prepare -Seed 2026 -Cases $cases -RunDirectory local-mugen/stage17-reference-seed-2026-new
& $run -CaptureProfile Stage17 -Prepare -Seed 1337 -Cases $cases -RunDirectory local-mugen/stage17-reference-replay-new
& $run -CaptureProfile Stage17 -Prepare -Seed 1337 -Cases default-damage-carry-edge,mode2-damage-carry-edge -RunDirectory local-mugen/stage17-reference-carry-edge-new
```

已采集证据在以下本地目录。验证器重新计算 RNG、检查控制器取样及 HitDef，并对照双方逐帧结算和生命提交。

```powershell
$guardCases = foreach ($mode in @('default', 'mode2')) {
    foreach ($variant in @('heavy-guard','mirror-heavy-guard','heavy-far-guard','mirror-heavy-far-guard')) {
        "$mode-damage-$variant"
    }
}
& $run -CaptureProfile Stage17 -Prepare -Seed 1337 -Cases $guardCases -RunDirectory local-mugen/stage17-reference-guard-new
python tools/Stage17Reference/Check-Traces.py local-mugen/stage17-reference/results local-mugen/stage17-reference-seed-1/results local-mugen/stage17-reference-seed-2026/results local-mugen/stage17-reference-carry-edge/results local-mugen/stage17-reference-guard-v2/results --replay local-mugen/stage17-reference-replay/results
```

正式格挡补测保存在 `stage17-reference-guard-v2`，执行文件 SHA-256 `100b3a747b75125f105a0d9d942848e6dc33a84be323be2eaae59fb6e5a7bc9f`。初次 F4 按后补测保留在 `stage17-reference-guard`，当时场景名称沿用了近身假设，实测走远身分支，后续命名已区分两种输入。
