# 阶段 19 原版 Terry 弱 Power Wave 取证

本轮是原版规则研究。使用 IKEMEN GO `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0`、原始 Terry 包及 KFM 场景，沿用[阶段 18 夹具](STAGE18_REFERENCE.md)。F0 是 RoundState 2 首 tick。夹具仅设定开局位置、朝向、种子 1337，并经原输入系统给根角色与独立 Helper 99999 提供相同的物理键。默认角色 `var(10)=0`。模式 2 通过原版开场 `Grant` 进入。

## 站轻拳取消条件

原 `Terry.cmd:167–169` 弱 Power Wave 命令为 `~D, DF, F, x`，总时限 16。根角色的原版控制器可从普通可控状态、`var(2)` 末端许可或 `var(1)` 取消许可进入。默认预设进入 State/Action 1000，模式 2 进入 1050（`Terry.cmd:1140–1181`）。State −2 在 200／205 的第二元素内部，即元素局部时间 1–3，置 `var(1)=1`。在这条规则下，命中、格挡与空挥均可取消。

- 首个轻拳 F5，空挥且中心距 100。QCF 完成 F9、F10、F11 时，同 tick 转到 1000，旧动作时间依次 4、5、6。三次原版 `MoveContact=0`。例如 F9 的物理键序列为 F6 下、F7 下前、F8 前、F9 前加 `x`。
- F8 完成时，F5 的第一次轻拳仍合法启动。该夹具把 QCF 第一拍下方向提前至 F4，随后 F6 下前、F7 前、F8 前加 `x`。F8 尚为旧时间 3，F9 根角色 QCF 已空，Helper 99999 的 `var(10)=11`，在旧时间 4 允许取消。Helper 原脚本识别后置 12，同一 Helper tick 在继续按 `x` 时减 1，松开时每 tick 减 4（`Helper.cns:2116–2124`）。
- F12 空挥样本的 QCF 已识别，但旧时间 7 时 `var(1)=0`，该物理 `x` 仍触发已有的 200→200。命中和格挡样本在停顿后 F23 也有相同回退。只有 `x` 而没有运动序列的 F20 对照，未识别弱 Power Wave，旧招按原规则退出。
- 空挥 F17 的 QCF 走旧时间 12、`AnimTime=0`、`var(2)=1` 的普通许可。混合边沿样本 F14 下、F15 下前、F16 前加 `y`、F17 前加 `x`，末端时 Helper 的重拳仍为 2，原控制器顺序仍选择 1000。这是末端普通起手。独立的普通输入样本先由前方向产生原 State 20，F9 以 `ctrl=true,var(1)=var(2)=0` 转到 1000。
- 近身 205 样本以中心距 36 令对方 F0 垂直跳，P1 F13 首次 `x`，其近身轻拳空挥，F17 QCF 在旧时间 4 转到 1000。模式 2 的远身命中和空挥样本按同一取消规则进入 1050。镜像的 1000 命中、空挥和双发均有证据。

## HitPause 与命令保留

中心距 60 首次轻拳在 F8 接触。命中实扣 46、守方 Shake 13。P2 从 F7 按后时，格挡零削血、守方 Shake 11。两者攻方 F9–19 冻结在旧动作时间 4，F20 恢复并允许取消。F20／21／22 的直接 QCF 对应旧时间 4／5／6。

- QCF 在冻结中的 F11、F17、F18、F19 完成时都在 F20 生效。根角色的 QCF 在 HitPause 保持到 F20，随后衰减。F11 完成后立即松键的样本，Helper 在恢复前已降为 −1，仍由根角色缓冲触发。F11 完成后继续按住 `x` 的样本，F20 的 Helper 值为 3。F17／18／19 松键后 F20 的 Helper 值依次为 3／7／11。Stage 18 站轻拳重进窗口从旧时间 7 开始，故其冻结期间输入的到期结果不同。
- 主轨迹中的 `powerWaveCommand` 为根命令，`helperWave` 为 Helper 99999 的 `var(10)`。`state-change` 在原版状态切换前采样，保留旧 `stateTime`、元素、根命令、Helper、命中确认和 `var(1)`／`var(2)`。`after-collision` 则记录提交姿态、实际接触及当帧生命；伤害由原版反应阶段扣除。

## State 1000 与 Helper 1020

1000 根动作无直接攻击框，Action 1000 共 52 本地 tick。它在第七元素局部时间 1 创建 Helper ID 1000、State/Action 1020（`Terry-S.cns:34–46`）。生成事件在进入后的第 15 个世界 tick，本帧 Helper 已推进 +2.5 局部单位。有效朝向在镜像中翻转，`vel.x` 仍是朝向相对的 +2.5。

- 中心距 180 全程空挥。F9 进入、F24 生成 Helper、F35 停止移动、F49 标记销毁、F61 根动作退出。Helper 的 Action 1020 播放 25 tick。销毁后的辅助数组仍可保留已标记条目，验收以 `destroyed=true` 为生命周期终点。
- 中心距 100 的空挥取消样本 F25 由早段 Helper 接触。中心距 60 的命中取消样本在 F36 由 Helper 实扣 76，目标 Shake 13 并进入击飞。对应站防 F36 削血 9、Shake 11，HP 991。Helper 的 HitDef 配置攻方停顿 0，碰撞中间记录的 1 在同帧结束归零，根角色没有自身 HitPause。早段地面速度为 −4.5、−6.5，设置 fall。
- 中心距 142 的较远样本在 F33 仅晚段命中，Helper 元素零基 5、伤害 92、地面类别 Low、无 fall、地面速度 −10.5、0。F68 根动作仍在本地时间 52 退出，绝对帧较无接触样本延后。Helper 命中后给父角色 `var(31)=1`（`Terry-S.cns:374–377`）。State −2 在敌方 Shake 结束后按条件发出一 tick 全局 Pause（`Terry-2-3.cns:624–640`），补测 F48、F54、F60 可见 `globalPause=1` 且 `turbo=1`。原版局部动作时钟在这些帧保持。
- 双方中心距 100 同时输入，F9 两边进入 1000，F24 分别创建两个不同实体。F25 各命中对方根角色，P1 实扣 107，P2 实扣 99，双方 1000 被打断到 5000，已存在的两个 Helper 继续至 F54 销毁。镜像保持相同结算值。原版以 Helper 实现 1020，并非标准 Projectile 槽，本样本无弹体互相抵消。模式 2 的 1050／1070 使用另一套行为，本轮只记录分支及观察结果。

## 证据和复现

主采集 25 场保存在 `local-mugen/stage19-reference/results`，补测 5 场保存在 `local-mugen/stage19-reference-supplement/results`。每场保留 JSONL、运行元数据、stdout、stderr；stderr 包含引擎既有素材和脚本警告及 `COMBAT_RESEARCH_DONE`。两个运行目录的元数据 SHA-256 分别为 `98f1331c7019fcdd5d50bd1b9cd7e862fe171f0d511c3d8b32e37ae56d266bf5` 和 `70742e976185a811dc8bc5dfea7a714cf8d8026d1b44ddef37f082d57a7f8af0`，执行文件 SHA-256 分别为 `d5b4216da97499296903bc0442daeb479e0ad697f8a07f90097c3b76396f863b` 和 `3cdae8473761aa3e74cc9750fb7e84a27a2c8aa66a5e2f0f4fbbe6f47e00482c`。补测额外记录实体销毁与全局暂停。原角色包 SHA-256 为 `299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698`，其中 `Terry-S.cns` 为 `60171bf640c7e8316d34e2b0ab001f663472bfd14dc66d174dcf5a8eebd8dc21`，其余脚本哈希随运行元数据保存。

```powershell
$run = 'tools/Stage14Reference/Run-Reference.ps1'
& $run -CaptureProfile Stage19 -Prepare -RunDirectory local-mugen/stage19-reference-new
$supplement = 'default-special-outside-whiff-9','default-special-far-whiff-9','default-special-terminal-heavy16-17','default-special-dual-9','default-special-mirror-dual-9'
& $run -CaptureProfile Stage19 -Prepare -Cases $supplement -RunDirectory local-mugen/stage19-reference-supplement-new
python tools/Stage19Reference/Check-Traces.py local-mugen/stage19-reference/results local-mugen/stage19-reference-supplement/results
```

验证器逐项检查来源哈希、原版完成标记、逻辑帧、状态切换、根与 Helper 输入、接触及实体生命周期，共 30/30。该结果作为后续实现依据。
