# 阶段 28 原引擎参考

## 原包与取证

原版 Terry 与固定 IKEMEN GO 同前几阶段：`local-mugen/terry.zip`（SHA-256 `29920333…`），探针二进制 `d319e3ab…`（复用阶段 27 已构建引擎 `local-mugen/stage27-native-engine/engine`）。种子 1337，`F` 为原引擎全局帧，`旧 tick` = State −1 求值时的 stateTime（0 基）。原生探针目录为 `local-mugen/stage28-native-probe3`（58 用例）、`probe4`（全局暂停 F145）、`probe5*`（helper×4）、`probe6*`（干净接触×5）、`probe7*`/`probe8*`（215 窗口与 helper）、`probe9*`（dist 64 helper 否定）；`probe`/`probe2` 为早期部分捕获（见验收文档的工具故障记录）。核对脚本 [Check-Traces.py](../tools/Stage28Reference/Check-Traces.py) 共 82 个原生用例全部通过（`completeVerified=true`）。

## State 300（バックナックル）本体

- `Terry.cmd:1538–1547` 的人类候选：`command = "x" && command = "holdfwd" && command != "holddown" || helper(99999),var(40) > 0`，权限 `trigger1 = ctrl || (stateno = [100,101]) || var(2) >= 1`、`trigger2 = var(1) = 1`。CMD 文件顺序为 250 → 805 → 300 → 310 → 普通技。`Terry-N.cns:1823–1974` 定义 State 300：S/A/N、ctrl=0、velset 0,0、`anim = 300`、facep2。
- Action 300（`Terry.air:1002–1048`）共 10 元素 47 tick（3/4/4/3/3/3/8/6/6/7）。两段攻击框：元素 4（stateTime 11–13）`[36,-92,83,-61]`、元素 6（stateTime 17–19）`[36,-88,99,-57]`；元素 6 同时运行 `MoveHitReset`。`Terry-N.cns:1835–1843` 的 PosAdd 在元素 2/3/4/5/6 各 +8、元素 8/9/10 各 −8（合计 +16）。
- 两个 HitDef（ID=30）均为 `attr=S,NA`、`guardflag=M`、`hitflag=MAF`、`animtype=Hard`，伤害 `floor((ifelse(var(6),9,5)-var(21))*fvar(7)),0`，停顿 `11+Helper(40000),var(0)`/13，防御停顿 11/11，`ground.hittime/slidetime=17`，推力 −10.5（MAX 模式 `var(40)!=0` 时 −11，未实现场景恒 0），`ground.type` 第一段 High、第二段 Low。受击侧的 5002/5007 站姿与 5022/5027 蹲姿反应沿用重击家族。守护削血 `fvar(10)` 与 `var(12)` 崩防状态在原档存在但此前阶段未建模，保持不建模。
- **双段命中为真实可达行为**：seg1 命中把目标推出后，受击姿态（5007/5012）的 Clsn2 向后延伸，seg2（MoveHitReset 后）再次接触同一目标（dist 50/60/121 均复现双命中；seg1 命中后 seg2 落空在开放场地无 fixtures 可达距离）。NewKOF 以既有 `contactGroup` 1/2 表达，内核零改动。
- State 300 无模式分支（对比 240 的 `Terry-N.cns:713–717` ChangeAnim）；默认与模式 2 轨迹逐帧一致（mode2-free-hit / mode2-wave-exit-guard 验证）。

## 输入、锁存与权限

- `Terry.cmd:311–314` 的 `x` 为 time=1 的独立物理边沿，`command.buffer.time = 1`（`Terry.cmd:81` 全局）。原生验证：根 `command="x"` 只在按键当帧为真（**age-1 根边沿不存在**——`200-age1-entry`/`200-age2-expired`/`age1-late-direction` 三个否定样本）。
- `Helper.cns:2203–2222`：`var(40)` 在 `x+holdfwd+!holddown` 当帧写 4 并同轮递减至 3，此后每 tick −1，可用年龄 0–2。Helper 在角色 HitPause 与全局 Pause 中照常采样与衰减（与 b 的 var(3) 同构；`global-pause` 探针 F103 暂停帧按键、F104 经 Helper 提交 300、根命令已死）。
- `Terry-2-3.cns:52–112`（State −2）：`var(1)=1` 是九个普通技的取消窗口旗标（200: 4–6、205: 4–6、215: 4–12、235: 5–8、245: 4–8、250: 11–12 区间、400: 4–4、410: 6–12、440: 11–17，均为 stateTime 单位，含两侧）；`var(1)=2` 是 `stateno=300`（元素 6 时间 >0 且元素 7 时间 <0 且 `movecontact||movereversed`，即 stateTime 18–19 且需接触）、310、721 的接触窗口。`var(2)=1/2` 在 AnimTime=0 给全部已列状态自然恢复权限（300/310/特殊技属 `var(2)=2`）。
- 命中停顿冻结 stateTime 与根命令缓冲（`frozen-both`/`split-*`：F11–F12 的冻结按键与方向在解冻 F20 组合提交）；停顿中的窗口在解冻后续跑（245 命中 stateTime 3、恢复 F45）。
- 蹲起（state 12）与下＋前的互斥：`holddown` 阻断根组合与 Helper 武装；Helper 存活时消费帧持下不影响 300（`215-helper-holddown-63` 系否定样本另证 `holddown` 不延长 Helper）。蹲起途中 `statetype=C ≠ A` 可入 300（`crouch-rise-fwd-x`：state 12 → 300）。

## 入边（var(1)=1 窗口，全部原生验证）

| 来源 | 窗口（stateTime） | 首tick | 末tick | 越界行为 |
|---|---|---|---|---|
| 200 | 4–6 | F9 | F10 | 越界走自链重进（F12 → 200） |
| 205 | 4–6 | — | — | 与 200 同窗（config 已有 Wave 取消覆盖） |
| 215 | 4–12 | F9（接触前） | 停顿后续跑 F34（time 7） | 越界无链，自然结束 |
| 235 | 5–8 | 冻结按键 → F21（time 5） | F24（time 8） | 越界走普通链（命中后滑出 16 阈值 → 200） |
| 245 | 4–8 | 冻结按键 → F20（time 4） | F24（time 8） | 越界无链拒绝（F25 → 无事发生） |
| 400 | 4–4 | F9 经 Helper（F8 按键） | 同左 | 越界走普通链 → 205 |
| 410 | 6–12 | F11 | F16 | 无链 |
| 440 | 11–17 | F16 | F22（time 17，末 tick） | 越界无链拒绝（F23） |
| 430 | 无窗口 | — | — | F+x 走普通链 → 200 |
| 240 | 无窗口 | — | — | 拒绝（多条否定样本） |

215 的窗口前 Helper 持久样本在 seg1 接触（stateTime 3）下不可达（dist 60/63/64 三组否定）；Helper 持久跨 tick 的正例由 210 自然恢复样本提供：`helper-recovery-215`（dist 65 实选 210）在 F43 按键，Helper 存活至 210 的 AnimTime=0，F46 经 `var(2)>=1` + Helper 进入 300。

## 出边与恢复

- `Terry.cmd:1150–1152`（弱 Wave）与 `1180–1182`（模式 2 的 1050）接受 `var(1)=2`：300 的接触窗口 stateTime 18–19 内完成 236x 手势可取消特殊技。原生验证：seg2 在 stateTime 17 格挡接触 → 第二次停顿（F34–44）→ 解冻后 F45（time 18）触发 1000——按键发生在停顿期间、命令缓冲被停顿冻结（`wave-exit-late-reject` 用例）；解冻后新按键同样成立（`wave-exit-postpause`）。停顿前按键（F33–35）命令过期则不触发（`wave-exit-guard` 否定）。模式 2 同场景无 1000/1050 状态变化。
- 普通技链不含 `stateno=300`（人类段）；300 不能自连（`var(1)=2` 不被自身 trigger2 接受）。
- 自然恢复：300 的 AnimTime=0（stateTime 47）设 `var(2)=2`，当帧合法新手势直接重开（`natural-reentry`：F52 300→300）。

## 与 NewKOF 实现的对应

- 根组合/Helper 的 OR 判据由 `RecognitionState.ReserveOnEdgeDirection` 的消费期 `AllowsReservation` 求值；识别期认按键边沿且保留原方向门（根方向不在序列中、非停顿帧才建候选——冻结重放 `near-to-crouch-jab-wave-free-release` 逻辑 38 证明按住 DF+x 无候选，先移除该门的实现已回退），方向与 Helper 的组合在消费帧核对（阶段 26 冻结/分离语义不变）。
- 保留指令绑定真实招式：`FighterDefinition` 放开"保留指令不可绑定招式"，限定每条保留指令至多一个招式；自由入口与 `ReservedCancels` 窗口在消费时发动新攻击实例（`FighterStateMachine` 自由路径与 `TryPriorityCancel`）。未绑定的 805/310/720/250 占位语义不变。
- 300 的命令 ID 由占位 `ReservedForwardLightPunch` 迁移为 `ForwardLightPunch`（`bufferLifetimeFrames` 保持 0、`helperRunsDuringGlobalPause=true`、priority 4 不变）；235/245 的 `reservedCancels` 同步改名。模式 2 的 1050 保留候选项维持原状。
