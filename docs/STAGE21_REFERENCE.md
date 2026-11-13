# 阶段 21 Terry 近重拳取消原版取证

使用固定版 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`、Terry-KOF98 双席、KFM 场景和种子 1337。F0 是 RoundState 2 的首 tick。取证只设置对局初始位置、朝向、种子和物理按键，原始 CMD、CNS 和 AIR 均由哈希核对。F 是对局全局帧，旧 tick 是控制器执行前的来源状态时间。引擎 JSONL 的 `elem` 从 0 计数，CNS 的 `AnimElemTime` 从 1 计数。

## 许可与控制器顺序

- `Terry-2-3.cns:52–70` 每次执行先清 `var(1)`，State 215 在 `AnimElemTime(2)>0 && AnimElemTime(4)<0` 时设 `var(1)=1`。Action 215 原始时长为 3／3／7／7／8 tick。显式窗口包含旧 tick **4–12**。两个生效元素的范围为 3–5 和 6–12，首段第一个 tick 3 当帧无显式许可，第二段第一个 tick 6 已在许可窗口内。
- `Terry.cmd:1140–1152` 的弱 Power Wave 是默认模式 `var(10)=0`、非 AI、根角色、非空中、RoundState 2，接受根弱 Power Wave 命令或 Helper 99999 的 `var(10)>0`。State 215 的显式许可通过 `var(1)=1`，无需 `MoveHit`、`MoveGuarded` 或 `MoveContact`。收招 `AnimTime=0` 另由 `var(2)=1` 提供普通入口，空挥样本 F41 旧 tick 28 进入 1000。模式 2 的对应目标为 1050。
- 两个站轻拳 CMD (`Terry.cmd:1566–1598`) 的显式重进来源列中没有 215。旧 tick 13 的有效 `236+x` 或单按 `x` 均拒绝，已进入 Recovery 时不会回落到 205／200。`whiff-26` 中 Helper 值先为 11，F41 前耗尽，215 在 F41 自然恢复。中心距 62 与 64 的样本都先进入 215。组二单独接触需要防守者 F5 的站后移动，原版近远选招边界沿用[阶段 16](STAGE16_REFERENCE.md)。
- `Terry-N.cns:327–497` 的首段 HitDef 在元素 2 生效，地面速度 0。元素 3 先执行 `MoveHitReset`，随后可创建第二段 HitDef，地面速度 −10.5。两份 HitDef 均用脚本 ID 10，但接触资格独立。首段成立后，普通情形下 `var(23)` 可从 1 增至 2。第二段单独接触仍用分母 1。`after-command` 在 State −2 刷新许可之前读取，故 F40 的旧 `var(1)=1` 仍可见，`end` 已清零，实际无状态切换。

## 可证伪的实战样本

- **首段命中** `hit-20`／`hit-held-11`。F5 起招，F8 首段 HitDef 76 实扣 61，目标 Shake 13。攻方 F9–19 停顿 11 tick，F20 旧 tick 4 进入 1000，F22 没有第二段。按住 `x` 的 F11 输入到 F20 仍有根命令与 Helper 值 3。F35 出生 1020，F36 Wave 实扣 89，F51 起触发 12 次全局 Pause，F65 实体销毁，F83 恢复站立。
- **同一全局帧抢占第二段** `hit-22`／`second-only-11`。前者 F22 旧 tick 6 的 State −1 先切换 215→1000，转移记录仍看见首段 `MoveContact=3`，该帧没有原 215 的第二段 HitDef 或接触。未取消对照在 F22 第二段实扣 29。后者 F8–10 首段空挥，F11 同样先取消再碰撞，转移时 `MoveContact=0`，无第二段 HitDef，Wave F27 实扣 76。两个原版正例证实已有结果保留、当帧尚未创建的攻击被抢占，退出后旧接触组不会补发。
- **两段均命中后取消** `hit-25`／`hit-34`／`mirror-hit-34`。F8／F22 实扣 61／29，攻方 F23–33 第二次停顿 11 tick。F25 预输入在 F34 由根命令接受，Helper 值已失效。F34 直接输入同样在旧 tick 7 进入 1000，`MoveContact=1` 来自第二段。F49 出生，F55 晚段 Wave 实扣 76。F39 旧 tick 12 仍合法，F40 旧 tick 13 的根命令有效但没有许可，215 自然恢复于 F55。
- **第二段末端受击反应**。`hit-34` 的目标在 F48–53 连续六次碰撞快照使用 Action 5007 第三个元素，`HitTime` 为 4／3／2／1／0／−1。F54 已站立，F55 Wave 接触。验证器逐帧断言后返回此段长度，可用于独立实验配置的数据生成。
- **首段空挥、第二段单独命中** `second-only-14`／`second-only-23`／`mirror-second-only-23`。中心距 64，防守方 F5 单帧按后。第二段 F11 HitDef 86 实扣 69，攻方 F12–22 停顿，F14 输入和 F23 直接输入均在 F23 旧 tick 7 取消，F38 出生，F44 晚段 Wave 实扣 76。F28 旧 tick 12 允许，F29 旧 tick 13 拒绝。未取消样本仅有 F11 接触。
- **防御**。中心距 62，防守方 F7–67 按后或下后，首段 F8 格挡、零削血，攻守冻结 11／11。`guard-20`、镜像和 `crouch-guard-20` 在 F20 取消，Wave F36 格挡削血 9。`guard-25`／`guard-34` 在两段 F8／F22 均格挡后进入 1000，Wave F54 格挡削血 9。`second-guard-14`／`second-guard-23` 用中心距 64、F5 起按后，首段空挥，第二段 F11 格挡零削血，F23 取消，Wave F47 格挡削血 9。格挡样本没有全局 Pause。
- **松键后的防御延续**。`guard-release-20` 在 F8 格挡首段后松开方向，F36 Wave 接触前目标 State 151、`raw=0`、`guardFlag=true`，仍格挡并削血 9。`guard-release-34` 保留 F8／F22 两次格挡，F42 后松开，F54 Wave 接触前目标 State 140、`raw=0`、`guardFlag=true`，也格挡并削血 9。两个样本说明已成立的防御会跨过本例的松键时点持续，实际接触时不要求继续按住后方向。
- **防御来源**。原始 IKEMEN GO `src/char.go:9964` 的 `inGuardState` 包含 120、130–132、140、150–155。`src/char.go:12835` 在当前有效角色 tick 为这些状态设置 `SCF_guard`，`src/char.go:11413` 的接触检查再读取该标志及攻击防御属性。Terry `common_kof.cns:1072–1137` 的 151 可按下方向切换站／蹲类型，`HitOver` 后进入 130。原始 `common_kof.cns` 的 SHA-256 为 `3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b`，已加入重采的直接文件校验，当前三个原始轨迹目录仍通过 Terry 完整归档哈希绑定该文件。
- **全空挥与输入边界**。中心距 54，防守方 F0 原地起跳，攻方 F13 进入 215。`whiff-16` 的指令于旧 tick 3 完成，F17 根命令已无效，Helper 值 11 在旧 tick 4 执行。`whiff-17` 直接成功。F25 旧 tick 12 空挥取消仍成功，F41 Wave 命中已落地目标。F26 旧 tick 13 的 236+x 和单按 x 都拒绝，F41 回到 State 0。新的 F41 指令经收招普通入口启动。
- **混合来源与预设**。双席混合正反向均由 P1 F20 走 215→1000，P2 F94 起远轻拳、F98 走 200→1000。实体 F35／F113 出生，ID 独立，出生朝向相反。首枚 F36 格挡削血 9，第二枚 F120 实扣 84。模式 2 的 F20 进入 1050，F34 实扣 81。模式 2 不输入手势时仍有 F8／F22 两段 61／29。

每场核对物理按键、来源和目标、根／Helper 有效期、碰撞相位、目标生命在 F+1 提交、两段 HitDef 数值、实体首帧及镜像。额外重算共享 RNG 的 Park–Miller 流、`VarRandom(21)` 桶和两段的原始／最终伤害。抽样流包含音效、Helper 及角色控制器。取消前后的实际伤害以同引擎抽样和受击方倍率计算，阶段 17 的跨引擎 RNG 差异仍适用。

## 采集与复验

主矩阵 34 场（F0–115），保存于 `local-mugen/stage21-reference-final/results`。混合双席及镜像 2 场（F0–185）保存于 `local-mugen/stage21-reference-mixed/results`。两者使用同一原版执行文件，SHA-256 `8147e38476548cd1a8ed3315fbb1993dad1a125a824aac531602dc008c6dc164`。主矩阵与混合矩阵的元数据 SHA-256 分别为 `dcd80ba25aad453bc8191e1f9d919bab14abd7eb620e08be3e8092bccb130702`、`21cdec2e4a857fb0c083a4f9345ce40d38e906673448159fe54aa4db503a1892`。松键补测 2 场（F0–115）保存于 `local-mugen/stage21-reference-guard-release/results`，补测仅扩充夹具按键场景，执行文件 SHA-256 为 `1c34825536ae267d2086134e8078d3c008a3c288136d8d89c333d296d95cf3cf`，元数据为 `48e76bd826b681d60909de7a4b5276742193468073105085a932dd85761ee97d`。全部 **38/38** 由同一验证器核对。结构化原始轨迹、逐场 SHA、退出标记及日志均保留。

```powershell
$run = 'tools/Stage14Reference/Run-Reference.ps1'
& $run -CaptureProfile Stage21 -Prepare -LastFrame 115 -RunDirectory local-mugen/stage21-reference-new
$mixed = 'default-heavy-wave-mixed-sequential','default-heavy-wave-mirror-mixed-sequential'
& $run -CaptureProfile Stage21 -Prepare -LastFrame 185 -Cases $mixed -RunDirectory local-mugen/stage21-reference-mixed-new
$release = 'default-heavy-wave-guard-release-20','default-heavy-wave-guard-release-34'
& $run -CaptureProfile Stage21 -Prepare -LastFrame 115 -Cases $release -RunDirectory local-mugen/stage21-reference-guard-release-new
py tools/Stage21Reference/Check-Traces.py local-mugen/stage21-reference-new/results local-mugen/stage21-reference-mixed-new/results local-mugen/stage21-reference-guard-release-new/results
```
