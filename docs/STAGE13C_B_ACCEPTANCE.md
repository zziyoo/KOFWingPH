# 13C-B 验收矩阵（9/9 PASS）

验收环境为 Godot 4.7.2 Mono、Vulkan Forward+、RTX 3060 Laptop GPU。以下为当前结果，历史失败与中间验证保留在后文。

1. **PASS｜待机、移动、蹲伏、跳跃、落地。** 旧版移动时坐标改变而 Action 仍为 0。用提交前后坐标和已识别方向修复，双席前进 20、后退 21、停步 0、蹲伏 11、起跳预备 40、原地/向前/向后跳 41/42/43，落地恢复 0。见 `13cb_movement_variants`、`13cb_jump_mirror`。
2. **PASS｜普通攻击、重攻击与连续攻击（现有招式）。** Kai 轻拳 200 多次从 tick 0 开始，命中取消到 1000 再恢复待机。Rei 用重拳键完成 214B 重扫，Action 440 的伸腿帧与 3/4/9 时序对齐。角色配置无独立站重拳可执行，210 保留为已有素材绑定，不声称存在站重拳。见 `13cb_guard_repeat`、`13cb_cancel_final`、`13cb_sweep_final`。
3. **PASS｜防御、受击、击退、倒地、起身。** 同席连续防御 150→150→151→151（帧 22/37/52/69）及交叉切换 150→151→151→150（22/40/58/76）。交替防御 P2 151、P1 150，均无掉血。冻结和松键期间保持姿态，出硬直、KO 及换人清除。旧绑定移除 `guard_crouch` 后仍正常走 150，权威事件与使用新绑定时一致。D79/D81 规定 Blockstun 中无防御资格，且同一近战实例只结算一次。多攻击窗口专项验证后续接触没有第二次结算，锁存保留，新合法事件可更新。空防 132、空防硬直 152 后恢复跳跃；Rei 扫倒实测 5050→5110→5120→0，BackDraft 实测 5000 与击退。见 `13cb_guard_repeat`、`13cb_guard_chain`、`13cb_guard_alternate`、`13cb_legacy_guard`、`13cb_air_block`、`13cb_sweep_final`、`13cb_team_third`。
4. **PASS｜升龙及已接入特殊动作。** 固定帧双席 Action 1200 均完整显示 18 tick，对应 13 个源帧及恢复待机。Action 1000 分别按 4/3/8 和 5/2/10 对齐近战原型与实际投射，源 AIR 发射帧 6 出现在生效首 tick。BackDraft 共用 200 素材，出拳姿态对齐第 5 tick，回收从第 7 tick 开始。440 为 Terry 原脚本带击倒的下段重扫，替换蹲轻脚 430。见 `13c_b_fixed_contact.png`、`13cb_rising_p2`、`13cb_cancel_final`、`13cb_projectile_final`、`13cb_backdraft_final`、`13cb_sweep_final`。
5. **PASS｜P1/P2 镜像与原点。** 两席前后走、前后跳及升龙均验证，受击方向和着地坐标与 HUD 对齐。Vulkan 原创夹具另断言 AIR 偏移、SFF 原点和 H/V 翻转。见 `13cb_movement_variants`、`13cb_jump_mirror`、`13cb_rising_p2`、`13cb_guard_alternate`。
6. **PASS｜HitStop、打断、取消和恢复。** 双 Kai 相杀图像状态 28–32 中升龙保持 tick 3/源帧 2，状态 33 恢复 tick 4/源帧 3，状态 42 回 0。飞行道具在逻辑帧 46 打断 P2 的 1200，切 5000 的 tick 0，仅 P2 在 47–50 冻结，51 恢复 tick 1；P1 待机继续。轻拳命中后停顿恢复，再按现有窗口合法取消到 1000 的 tick 0。升龙自身 HitStop=0，无配置取消窗口。见 `13cb_clash_fixed`、`13cb_projectile_final`、`13cb_cancel_final`。
7. **PASS｜非循环末尾零时长。** Terry 实际 Manifest 无末尾零时长动作。原创 Godot 夹具以 Vulkan 像素验证保持最后可见帧，末尾两个零时长帧未绘制。另有纯时间线回归。见 `local-mugen/13cb_gpu_fixture.log`。
8. **PASS｜连续零时长与全零异常。** 专项验证连续零帧被跳过且全零序列在大 tick 有界返回末帧，Godot 夹具用真实像素验证全零兼容回退。见 `local-mugen/13cb_gpu_fixture.log`。
9. **PASS｜1v1 与 3v3。** 1v1 固定帧和实时 30/60 帧上限实测。3v3 连续两次 KO，P2 Rei（Terry）→ Kai（无覆盖，几何）→ Rei（Terry），P1 的 Terry 独立保留。新交锋从待机首帧开始，第三场新蹲防从 151 首帧开始。图像和日志均按交锋复位。见 `13cb_realtime`、`13cb_realtime_30`、`13cb_team_third`。

### 取证和回归

- 各场次的 `local-mugen/<场次>.args.txt` 为完整参数，`.log` 为同次输出，`shots/<场次>/` 为 FramePostDraw PNG，`shots/<场次>_sheet.png` 为抽取的实际画面。对局捕获状态号等于 `SpriteFrameAudit` 的 logic+1，等于 `SpriteRenderAudit` 和截图 HUD 的状态号。换人后状态号重置，由渲染编号与 `VisualAudit` 的 scope 区分。
- 复核脚本 `local-mugen/13cb-check-captures.py` 核对了 11 次固定帧和 3 次实时采集的 PNG、状态顺序及两席渲染资源。实时采集存在重复或跳过绘制的状态，已提交逻辑帧和 1200 的 tick 0–17 均连续。密集 GPU 读回会触发已有积压上限，丢弃的是超限时间预算，日志中没有跳过已提交的状态号。无密集读回的实时复查 `13cb_realtime_playback.log` 未触发积压警告。
- 58 项专项与 1195 项全量测试通过，均 0 失败、0 跳过。Debug 构建 0 警告、0 错误。Godot 原创夹具返回 `MUGEN_GPU_PIXELS_PASS`。同一 1v1 取消场景使用 Terry 覆盖与几何配置时，69 条权威日志逐条相同。
- 投技演出与飞行道具实体外观由 13C-C 验收。Terry 原有 126 个缺图帧及 4 个全透明引用详见 13C-A。真实素材、图集、第三方截图保留在 Git 忽略的本地目录。

### 历史失败与中间验证（归档）

- 待机、前后移动、蹲伏、跳跃、落地：13B 有静态截图；当前绑定的前后方向变体及落地连续性待图形验收。
- 普通攻击、重攻击、连续攻击：13B 有轻拳截图；重拳及连续切换待验收；不假定不存在的战斗招式已经实现。
- 防御、受击、击退、倒地、起身：绑定已初步修正；当前版本各状态切换、打断及落点待图形验收。
- 升龙：未通过完整衔接。真实 Godot Mono/Vulkan 中启用 `--sprite-frame-audit`，`local-mugen/13c-b-timeline.log` 分别记录逻辑帧投影及渲染调用的资源选择。Action 1200 原始13帧共41 tick（2,3,4,1,2,2,2,2,2,1,5,6,9），战斗动作仅18 tick；日志显示tick17停在帧7，之后战斗状态切Standing并切Action 0，帧8–12未播放。单帧截图 `shots/13c_b_checkpoint_rising.png` 不证明动作完整。需设计不改变战斗逻辑、又能明确解释时长/后继状态取舍的表现方案，并重新录制图形验收。
- 根因核查：Kai 的 `DragonPunchPrototype` 在阶段04提交 `b622d0f` 即定义为 3/5/10，早于素材导入；数据外置提交 `20d668e` 保持数值。`FighterStateMachine` 在Recovery计时结束后按既定规则同帧返回待机；D125/D156规定视觉只读投影并随状态切换。原素材 Terry-S.cns 的Statedef 1200在`AnimTime = 0`转1205，带多段AnimElem触发的HitDef及不同运动规则。两者是不同角色的战斗实现，不能据此把Kai改为41 tick。已确认冲突是完整Terry动画直接绑定短Kai原型导致截断；没有找到项目曾明确决定将1200加速或剪辑的证据，因此仍需明确并实现表现契约，不能宣称已解决。
- 用户确定适配契约后，未提交配置使用逐MoveElapsed tick明确帧表 `[0,0,1,2,3,4,5,6,7,8,9,10,10,11,11,12,12,12]`；3/5/10阶段分别选取0–1、2–6、7–12。全部13个源帧保留但时长被有意缩短；姿态联系图 `shots/13c_b_1200_sheet.png`。Godot逐提交帧日志 `local-mugen/13c-b-adapt-60.log` 显示18 tick序列与帧表一致并回待机，5次独立运行截图 `shots/13c_b_adapt_{34,38,43,48,60}.png` 显示局部姿态；截图与同次末条渲染日志存在约一逻辑帧偏移，且缺少逐逻辑帧图像序列，不能判定完整图形验收通过。防御、HitStop、中断、取消、镜像、3v3仍待验收。
- 续验：独立checkpoint `8a21aea`保存帧表及诊断。`CaptureHelper` 新增未提交的FramePostDraw取证，图像先缓存在内存、结束后写盘。固定模拟步长 `--fixed-fps 60` 真实Vulkan运行 `local-mugen/13c-b-fixed.log`，一轮采集46帧，动作状态版本25–42各有图像；文件名内state为BattleState.Frame，`SpriteFrameAudit` logic为提交前帧号（state=logic+1），`SpriteRenderAudit` logic为渲染状态版本。人工检查 `shots/13c_b_fixed_contact.png`（由同次GPU截图裁剪汇总），18个状态版本的起手、主体、收势、以及state43待机衔接符合配置。此结论仅限P1、无HitStop的固定帧运行；原始图片位于 `shots/13c_b_fixed/`。非固定帧运行因读回耗时观察到中间状态未渲染，日志 `local-mugen/13c-b-buffered.log` 和 `13c-b-buffered2.log` 不可重标为逐逻辑帧画面。
- HitStop局部证据：固定步长真实1v1轻拳命中，`local-mugen/13c-b-hitstop.log` 中逻辑帧23–26双方frozen=True，攻击方Action200 tick3/frame1与受击方Action5000 tick0/frame0保持；`shots/13c_b_hitstop/` 保存实际渲染画面。此项尚未覆盖升龙的frameSchedule、单方冻结或打断取消，不将整个HitStop项标记通过。
- 升龙HitStop局部 PASS：Kai升龙HitStop=0，不能用其命中伪造停顿。实际1v1双Kai在距离60的半场宽条件同时使用升龙和轻拳，`StrikeClash` 按现有规则取较大停顿4；`local-mugen/13c-b-clash.log` 中逻辑帧28–31双方冻结，升龙Action1200的tick3/frame2保持，帧32恢复tick4/frame3。`shots/13c_b_clash/render-00037-state-00028.png`与`render-00042-state-00033.png`为相杀和恢复实际画面；该次只有5张FramePostDraw截图，不能声称冻结期间逐帧画面完整，单方冻结及升龙直接命中停顿未验证。
- 站/蹲防御历史 FAIL：两次真实对局分别按后和下后防御同一中段轻拳，均有Blocked事件且HP保持；日志 `local-mugen/13c-b-guard-stand.log` 与 `13c-b-guard-crouch.log` 中受击状态都选择guard Action150（站防受击图），没有选择素材的蹲防受击Action151。`shots/13c_b_guard_stand/` 和 `shots/13c_b_guard_crouch/` 为实际图像。原因为战斗状态将站/蹲防御合并成Blockstun，绑定只有guard。
- 上述站/蹲防缺陷的局部修复 checkpoint `1a1d3b9`：命中帧Blocked/ProjectileBlocked时锁存姿态，非冻结帧取当帧提交的方向历史，冻结帧取帧前姿态；离开Blockstun或换人重置。`guard_crouch`可选绑定Action151，未配置时兼容原guard。固定帧Vulkan对局中P2站防150、蹲防151，松开方向后硬直中保持151、退出后回待机；日志 `local-mugen/13c-b-guard-fixed-{stand,crouch}.log`，画面 `shots/13c_b_guard_fixed_{stand,crouch}/render-00032-state-00023.png`。反向P1蹲防也选151，`local-mugen/13c-b-guard-p1.log` 和 `shots/13c_b_guard_p1/render-00032-state-00023.png`。两项锁存单元测试与关联专项共21项通过；多段、连续防御、同帧姿态转换等尚需真实战斗验证，防御整项暂不判PASS。
- 已接入特殊动作：1000 与 1200 有素材，动画与实际战斗帧调度的完整同步待验收；1100 为 Burn Knuckle，不作为升龙。
- P1/P2 镜像：13B 曾有截图；当前版本原点、受击朝向和连续变化待验收。
- HitStop、打断、取消、恢复：时钟现有专项测试覆盖 HitStop 与 30/60/144 fps；战斗运行图形连续性待验收。
- 非循环末尾零时长：checkpoint `bfa48d1`，相关17项专项测试通过；真实 Godot 渲染验证待执行。
- 连续零时长、全零异常：自动化 PASS，连续零帧跳过；全零序列在时间0和int最大值均返回末帧而不会循环（保留现有回退）。`dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj --filter FullyQualifiedName~SpriteTimelineTests --no-restore`：6项通过。全零动作是否应该以其他方式提示属于待定限制；真实 Godot 渲染仍待验证。
- 1v1/3v3：13B 有图形运行历史；本次版本尚未完成两种模式的完整图形验收。
- 素材：Terry 原始数据含126个缺图帧、4个全透明引用，具体条目见13C-A报告；不以其他动作伪造填补。投技当前回退、C未完成。

既有 `project.godot` 差异为编辑器注释、区块排序及默认设置序列化变更，来源未确认。工作区保留。
