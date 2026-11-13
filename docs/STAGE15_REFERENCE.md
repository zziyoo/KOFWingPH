# 阶段 15 原引擎基准

## 运行条件

在 Windows 上以 IKEMEN GO nightly 源提交 `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0` 构建独立引擎，运行原版 `Terry-KOF98.def` 双人 1v1 和 KFM 场景。原版角色包、CNS/CMD/AIR、场景、配置与阶段 14 相同，沿用其哈希。[阶段 14 原引擎基准](STAGE14_REFERENCE.md)

阶段 15 构建可执行文件 SHA-256 为 `0ef99d67a936376b603d741d9ee2387f6a44b388dc1c0a26a1c9b21bc9346a8a`。输入模式另涉及原 `Config.cns` 的 SHA-256 `331cfd1ee377f4649dc49b94d0fc0759f448206086ffc7bfe12e2c3073c5645c` 和 `Helper.cns` 的 SHA-256 `b2d683f70a3c6dd76587edee1a54f0638f9c6e006d5c70af598ff9b0163dd62a`。日志报告 OpenGL 3.3.0 和 NVIDIA GeForce RTX 3060 Laptop GPU。本基准在模拟阶段记录状态与待用图形资源，Vulkan 画面证据来自 NewKOF 的独立验收。

夹具向原引擎输入管线注入物理方向和按键，在 RoundState 2 首逻辑 tick 设置初始位置、朝向、零速度和 RNG 种子 1337。生命初始均为 1000，战斗规则与角色脚本照常执行。双方均无 AI 输入，关闭自动格挡，P2 固定调色板 2。未改写原始人物脚本或招式状态。

### 原始模式入口

- 默认模式使用 P1 调色板 1。对局中 P1 `var(10)=0`，State 210 采用 Action 210。
- 另一模式用原版 `Helper.cns:1540–1552` 入口。P1 以调色板 7 开场，由模式助手初始化 `var(10)=1`。RoundState 1 内在引擎 intro 相对帧 1、3、5、7、9、11 输入上、上、下、下、上、下，间隔帧中立。原 `Terry.cmd:216–219` 识别 `Grant`，intro 11 的 `after-command` 命令缓冲为 1，`end` 中 `var(10)` 从 1 变成 2。之后 `Terry-N.cns:228–232` 在 State 210 Time 0 切 Action 211。采集器没有向角色写入 `var(10)`。每个 `mode2-*` 场景均有配套 `*-mode.jsonl`，其中 `mode2-hit-mode.jsonl` SHA-256 为 `f3b1c998f7e8bbebbdaa8b9516c4e44aa160a32480989a93f207403d471f6df8`。

## 逐 tick 结果

`F` 相对 RoundState 2 第一个引擎模拟 tick 起算。P1 在 F5 按物理 `y`，入招为动作 tick 0。 `frame`、`tick`、`matchTime` 分别记录相对、引擎和对局时钟；角色 `time` 是 State 时间，`elem` 是零基动画元素。碰撞行的 `frame.sprite` 对应实际判定素材。动画游标可能先于当前素材更新，须同时查看采样点和资源。

| 实测项 | Action 210 | Action 211 |
| --- | --- | --- |
| State / 启动 | State 210，F5–9 | State 210，F5–9 |
| 空挥生效 | F10–17，动作 tick 5–12，8 tick | F10–16，动作 tick 5–11，7 tick |
| 空挥恢复和退出 | F18–34，F35 切回 State 0 | F17–34，F35 切回 State 0 |
| AIR 元素时长 | 2/3/8/8/9 | 1/2/2/3/4/3/4/4/7 |
| 首次普通命中 | F10 接触，F11 生命 1000→885 | 同左 |
| 站防、预蹲防、同帧蹲防 | F10 格挡，零削血 | 同左 |
| 普通命中受击方恢复控制 | F42 | F42 |
| 格挡方恢复控制 | F39，State 140 | 同左 |
| 接触后攻击方恢复控制 | F46，State 0 | 同左 |

- 原 `Terry-N.cns:247–284` 的首次普通 HitDef 为 `floor((16−1)×9.615)=144`。测得 `var6=0,var21=1,var40=0,Helper(40000).var(0)=0`，攻击补正 .8、接触时目标有效防御 1，实扣 115。F10 排入伤害，F11 的状态提交时生命减少。站／蹲防 chip 为 0。双方命中停顿原值为 11/13，格挡为 11/11；F10 碰撞后攻击方 HitPause 12 属于调度补偿，F10 结束为 11，F11–21 停在已提交的攻击元素，F22 继续。
- 命中目标 F10–23 在 State 5000，F23 HitShake 为零，F24–41 在 State 5001，F42 可控。配置的 `ground.hittime=17`，碰撞后计数 18。击退最初 +10.5，随后每 tick .85 保留并按原引擎站姿低速阈值归零，在 ±36 夹具中达到 X99.8852。镜像中初始位移和位置反号。
- 站防目标 F10–21 State 150，F22–38 State 151，站姿 .85 保留。蹲防（早、晚）分别进入 152→153，Action 151 自 F11 开始，F22–38 的 .82 保留使目标达到 X92.33462。两者格挡原始 HitTime/CtrlTime 为 17，F22 开始位移 +10.5，F39 State 140 恢复控制。站防从 F9 按后，预蹲防从 F5、同帧蹲防从 F10 按下后，均持续到 F39。
- 两条动作在活动阶段的规范化 Clsn1 均为 X [12,67]、Y [61,84]（NewKOF Y 向上）。Action 210 在 AIR 写成 `[12,-61,67,-84]`，IKEMEN 轨迹显示已将上下端点规范化。Action 211 的第四、第五元素各有相同 Clsn1。二者不同 Clsn2 在轨迹中可区分，例如 F10 Action 210 的两个框为 `[-20,-88,19,-1]`、`[-4,-100,51,-69]`，Action 211 是 `[-12,-88,27,-1]`、`[0,-96,55,-65]`。正常目标面对拳的边界是 67+19=86，分离 86 恰好接触并命中，分离 87 有一单位间隙而空挥，两模式相同。
- `Terry.cmd:1601–1622` 先检查 State 215。实测分离 60、64、65 的 `P2BodyDist X` 为 30、34、35，前两组从 F5 进入 215，F8 与 F22 两次接触，实扣 61 与 29。65 从 F5 进入 210，F10 接触一次。这验证双方面朝时扣除两个前宽 15 的规则，并限定了远站接入范围。State 215 仅用于参考边界。

### 两招互动

- 两模式在分离 72 且 P1 F5 重拳、P2 F7 轻拳时于 F10 同时命中。碰撞前双方处于攻击 MoveType，真实 Action Clsn2 参与判定。F10 碰撞后 P1 HitPause/HitShake 为 12/13，P2 为 0/13；F10 结束两者均为 0/13 并进入 State 5000。F11 P1 生命为 946、P2 为 846，F24 同时开始击退，P1 F34、P2 F42 恢复控制。
- 两席均在 F5 出拳时，P2 State 200 于 F8 命中仍在启动中的 State 210，重拳从未发出 HitDef。F9 P1 生命 946，F22 位移 −6，F32 恢复控制，P2 F28 回 State 0。Action 210 与 211 的启动 Clsn2 在 F8 不同，两者均被原轻拳击中。
- 两种互动中原脚本使用反击变量 `var(6)`。在对手 MoveType=A 时持续设 1，退出攻击后逐 tick 累增直至大于 3 时归零（`Terry-2-3.cns:421–436`）。`var(21)` 每 tick 由 `VarRandom` 取 0–2（614–619）。本固定夹具中对攻原值 192 和 67，实际 154 和 54；打断时轻拳原值 67，实际 54。四个互动场景在独立目录重复运行，F5 起状态、生命、两变量、伤害和双方停顿一致。完整原始文件可因开局绝对时钟和介绍段而有不同 SHA。普通地面 115/46 与反击 154/54 是不同原始分支，后续对照应单独核对范围。

## 复现

在 NewKOF 仓库根目录运行。已有原始日志的目录会被拒绝，重跑选新目录。需本机已有的 IKEMEN 研究环境、运行依赖和 `local-mugen/terry.zip`，可用 `-IkemenRoot` 指定研究目录。

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage15 -Prepare -RunDirectory local-mugen/stage15-reference-new
python tools/Stage15Reference/Check-Traces.py local-mugen/stage15-reference-new/results
```

标准产物在 `local-mugen/stage15-reference/results/`，共 26 个场景和 13 份模式入口证据。每场包含 F0–75 五个采样点及 HitDef 执行行。中立命中、站防、预蹲防、同帧蹲防、镜像、对攻及打断的分离均为 72，空挥为 240，其他由场景名标明。原始 stderr 保留旧脚本和 Terry 素材诊断。以下为检查器通过的 JSONL 哈希，每场的可执行文件、配置和启动参数也记录在 `*-metadata.json`。

### 64.5 分离补测

同一引擎在独立目录 `local-mugen/stage15-reference-fractional/results/` 复查位置 ±32.25。两模式的原生 `P2BodyDist X` 均为 34.5，F5 进入 State 210，分别选 Action 210 和 211，F10 命中，F11 目标生命 885。MUGEN 1.0 的浮点距离判断保留了小数，远身条件为角色中心分离严格大于 64。

```powershell
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage15 -Prepare -RunDirectory local-mugen/stage15-reference-fractional-new -Cases default-far-64p5,mode2-far-64p5
python tools/Stage15Reference/Check-Traces.py local-mugen/stage15-reference-fractional-new/results --fractional
```

- `default-far-64p5.jsonl` SHA-256 `b4cb1b5072e4fad309fe05ede4cd5227bdb68d05759d29630f518c48daa79ee3`
- `mode2-far-64p5.jsonl` SHA-256 `e909b39a815ec02830e182cc31ce38bf9422d827bd112957cffaf870db145fd4`

### 标准轨迹哈希

| 场景 | 行数 | SHA-256 |
| --- | ---: | --- |
| `default-whiff` | 388 | `751a3d74f907471f78d0b7aa27b3f6c0ca5a88ff7116a6fd5b56cace715977d1` |
| `default-hit` | 381 | `6b76dcbdc9b2dd439d6a2d9058b3df4812e416f32e9e2e31bbf67ea78861c684` |
| `default-guard` | 381 | `3d16e323ca665133e9a19ad16c4293f60e8a80408a0e583b1d68d581c87b8558` |
| `default-crouch-guard` | 381 | `690f08fd8ab4e69800905c9470e74d39e74549bdc6cc35de683cf503e670054e` |
| `default-crouch-guard-late` | 381 | `4e19da777623ed609f187f0f7e2e4cc038c3f0a38a0efaf2f72674f7836e2386` |
| `default-mirror` | 381 | `5f6e0d8a7169cfbd1f65b4099416ed62db9b67a61256bf3ccccbb9accfb50910` |
| `default-close-60` | 382 | `030a7fb43c6ed3ca5969e34700dd6b0d19e81d49a1e32c79e9ee1d8d87d0f4ac` |
| `default-close-64` | 382 | `d05d990ff572aa5d00dd870c0e673cbf4503022d8438ebb95bd586e9a27cb00e` |
| `default-far-65` | 381 | `d6f50b8e41b757dd714bf1aa8784eaf4d85df419ac878eed4dbe250f5bb5cb90` |
| `default-boundary-86` | 381 | `fb2dec21db1fd3f31a203f5393795af9b37afcf792709d71912d574e6c09f1ae` |
| `default-boundary-87` | 388 | `9d788368b6a527901aeb1eec1515e3f7bc857b52c6cf7e0df8d9e7d84b58521e` |
| `default-trade` | 382 | `4134829b885ec45195106dbe9ac2575e032411be16e290b74af2c061977824f6` |
| `default-interrupt` | 381 | `08d7de19990a573f1a8e42d36002f8824ce17e469f4cb33e0073a13017b8adb2` |
| `mode2-whiff` | 387 | `b1c3dbe967a80f2e1b1448b84b4b01e0154781128f163ba17294b801d6f36aed` |
| `mode2-hit` | 381 | `5ea279a51320fee30686443e51ddbbbc48addf477df15d3e186034698eee4394` |
| `mode2-guard` | 381 | `9f085db55ef2395ad8d0c22f43340d889d28808283436db2eb8587844cd4383b` |
| `mode2-crouch-guard` | 381 | `d756e9b805cc8ca086ec242c78edd439d1aa1694ec3a275646699298650b73bf` |
| `mode2-crouch-guard-late` | 381 | `6bf6598615b82f0563eae1a5bcc6a9f718c7624d10276a02f666c3e4811fd734` |
| `mode2-mirror` | 381 | `2bc348708ceb725b42b5ce24fa20946444210d5d09b7ab14974f92f5b9efefac` |
| `mode2-close-60` | 382 | `baf86e6b40df2630cc4ee5e0af999615aacae12483e1b11486c38ff18059929b` |
| `mode2-close-64` | 382 | `068b5b85c00b31c2f0b66796fe14fdc8a34a91162f9015089106255d5ca82f2d` |
| `mode2-far-65` | 381 | `87a7453a64494b3b84810c9f24cc56eec1e9e871d14d23ff9e3480538d1cf16d` |
| `mode2-boundary-86` | 381 | `eb7a39e655babfb4bf3f8e24637c6227a9ea2847384707006ca98082e32799b5` |
| `mode2-boundary-87` | 387 | `3ae983bfd41102a95b575ea54875c3578842f173b4a23756be2cf803b559b2d3` |
| `mode2-trade` | 382 | `be1672f1e235502962cb4a5a54eb590c1b65b8859f3586314127b0a9ac1238f3` |
| `mode2-interrupt` | 381 | `00f2a91798f5a65ff4bbd1bb84830535b70017ed2da6395c0d8273283f4d9725` |
