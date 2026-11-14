# MUGEN 视觉资源导入（13A）

输入 ZIP 和转换结果均属本地第三方素材，不提交到 Git；使用者须自行确认素材授权。导入器只读取 ZIP 成员，不解压路径、不执行包内代码、不导入战斗规则或声音。Python 3.11 标准库即可运行：

```powershell
$env:PYTHONPATH='tools/MugenImporter'
python -m mugen_importer local-mugen/terry.zip data/imported-characters/terry98 --character terry98
```

输出目录必须为空；中断时保留不完整目录，换一个空目录重试。ZIP 含多个同类文件时，用 `--sff`、`--air`、`--def-file` 指定唯一成员后缀；`--act ''` 使用内嵌调色板，`--act act/act02.act` 选择另一套。`--atlas-size` 范围 32–4096，默认 2048。ZIP 只按名称查找并读入，不写出包内路径。SFF 数据限制 128 MiB、100000 项、单图片 64 MiB；PCX 解码上限 16777216 像素。SFF v2 明确拒绝，尚未实现。

Manifest v1 包含源 ZIP、SFF、AIR、DEF、ACT 的 SHA-256、导入器版本、选项、localcoord、源序索引、原点、图集区域、链接索引、完整 AIR 帧和仅供参考的 Clsn 原文。不保存导入时间。图集依源顺序使用固定大小的 shelf 打包，透明色为索引 0，ACT 按 MUGEN 倒序读取。重复 Sprite 键与 Action 均保留并诊断；按键或动作查找采用第一个。负图片号作为保留时长的空白帧，非负缺图诊断并回退几何人物；非法时间帧诊断后丢弃。`Time=-1` 永久保持，零时长跳过。Clsn 不接入战斗判定。

运行 `res://scenes/MugenPreview.tscn` 预览，左右方向键切换内置常用 Action；`--mugen-dir=res://data/imported-characters/terry98 --preview-action=200` 可选目录和动作。显示采用最近邻采样、AIR 偏移和 SFF 原点，左右并排镜像。它只是素材检查器，不代表 Terry 可操作。

可选战斗表现绑定使用 `data/imported-characters/<FighterId>/binding.json`，内容示例 `{"clips":{"idle":0,"walk":20}}`；clip 键对应已有几何表现配置。没有绑定、贴图或对应动作时回退几何人物。动画时钟来自逐逻辑帧表现投影，招式使用 MoveElapsedFrames；图像不会改变战斗状态或碰撞。仅授权素材才可分发。

## 13B：视觉 ID 和对局覆盖

启动参数 `--p1-visual=terry98` 和 `--p2-visual=terry98` 独立指定视觉资源，不改变 Kai/Rei 的战斗配置或 AI；可追加 `--p1-visual-fighter=Kai`（P2 同理）使覆盖仅作用于指定队员。资源路径为 `res://data/imported-characters/<visualId>/`，该目录及第三方文件仍由 Git 忽略。未指定覆盖沿用战斗角色 ID 查找。

绑定 schema v2 示例（数值仅说明字段，不构成正式角色动作配置）：

```json
{"schemaVersion":2,"visualId":"example","scale":1.0,"offsetX":0,"offsetY":0,
 "clips":{"idle":{"action":0},"punch":{"action":200,"clock":"move","startTick":0,"firstFrame":0,"lastFrame":2,"loop":false}},
 "moves":{"StandLightPunch":{"Startup":"punch","Active":"punch","Recovery":"punch"}}}
```

`clock` 为 `move`（使用 MoveElapsedFrames）或 `phase`（使用表现时钟），`startTick` 是时间偏移；`firstFrame` / `lastFrame` 限定包含端点的片段；`loop:false` 播放完保持最后帧。可选 `fallback` 指向已有 clip；旧版整数映射继续支持。空白 AIR 帧按时长保持透明，正数缺图、缺动作、缺图集或非法配置回退几何图，并在异常路径输出诊断。

坐标从权威 Fighter Root 出发：按 localcoord 和 scale 缩放 AIR 偏移及 SFF 原点，再组合帧自身 H/V 与角色朝向；不会写入战斗坐标。闪白 shader 仅改变可见像素颜色并保持 alpha，时间沿用已提交逻辑帧和原有反馈时长。贴图以 Godot `ResourceLoader` 加载并缓存，换人时清除旧纹理引用。

当回合或比赛已经结束、逻辑帧不再推进时，闪白剩余时间按 60 Hz 对应的表现时间收尾；此过程不推进战斗、Hitstop 或动画。新回合及换人会重置闪白。`--visual-audit` 可打印每个已提交帧的双方血量、定点坐标、状态、招式与事件，用于比较视觉覆盖前后。

默认 Windows 导出预设使用 `all_resources` 和 JSON include filter，因此本地存在的图集和绑定可能被打进导出包；只有取得分发授权后才应有意包含它们，或在导出预设中排除导入素材目录。发布包纹理加载仍须用已安装的导出模板实测；本机检查时没有模板。

## 13C-A：动作审查与本地交互预览

从仓库根目录运行（输出目录须已存在）：

```powershell
$env:PYTHONPATH='tools/MugenImporter'
python -m mugen_importer.action_audit data/imported-characters/terry98/manifest.json local-mugen/13c-action-audit.json --preview local-mugen/13c-preview.html
```

直接打开生成的 HTML 即可；图集以 data URI 嵌入，无需服务、联网或识别现有浏览器 URL。JSON 保留动作出现次序、编号、帧持续时间、偏移、原点、图集文件名、共享关系及来源诊断。名称来自相邻 `binding.json` 的 clip 键（显示 `binding:`）；Manifest 没有名称的动作标为 unnamed，不推测原始动作含义。

预览有播放/暂停、Next frame（推进一个可显示动画帧）、One tick（推进一个 1/60 秒逻辑 tick）、镜像与循环开关。零时长帧跳过，`-1` 自动播放时永久保持，手动 Next frame 可继续查看其后帧。关闭循环后保持最后一个有持续时间的帧；全零/空动作显示诊断。非法持续时间使该动作停止播放，其余动作仍可选择。缺图、损坏图集及越界区域显示提示并清除上一张画面。

审查器报告重复 Action/Sprite、缺图、负编号允许空白、全透明图片、PNG 损坏/截断、越界区域、无效链接、异常尺寸/原点。共享 Sprite 或链接到相同图集区域本身不算错误。尺寸大于 localcoord 的四倍、原点绝对值大于自身宽/高四倍只作人工复查警告（特效可能合法）；不是自动修图依据。PNG 检查针对本导入器产生的 RGBA/filter-0 格式，其他格式明确诊断为不支持。输出目标禁止覆盖 Manifest、binding 或原图集。

测试与真实图形交互验收入口（从 `tools/MugenImporter` 运行）：

```powershell
python -m unittest discover -s tests -v
python -m pip install playwright
python browser_acceptance.py --manifest ../../data/imported-characters/terry98/manifest.json --evidence ../../shots/13c-a
```

验收脚本主动启动独立、有窗口的 Edge，通过确切 file URL 导航，不接管已有浏览器；用控件、Canvas 像素、截图和异常监听验证。需要本机 Edge；其他环境可在脚本浏览器启动处改为已安装的 Chromium。它同时检查真实 Manifest 的编号、全部帧时长、偏移、尺寸/原点及重复生成输出稳定性，前后核对整个原资源目录文件 SHA-256。证据保存于本地 `shots/13c-a/`，第三方图片及内嵌图片的 HTML 不提交。

## 14B：Terry State 200 离线验证与转换

使用 Python 3.11 从仓库根目录运行。先按阶段 14 参考工具生成同一次运行的 `run-metadata.json` 和九份 JSONL 轨迹。

```powershell
$env:PYTHONPATH='tools/MugenImporter'
python -m mugen_importer.state200 local-mugen/terry.zip data/imported-characters/terry98/manifest.json --audit-only
python -m mugen_importer.state200 local-mugen/terry.zip data/imported-characters/terry98/manifest.json --reference local-mugen/stage14-reference-replay/results/run-metadata.json --output-dir data/imported-characters/terry98
python -m unittest discover -s tools/MugenImporter/tests -p test_state200.py
```

`--audit-only` 输出静态来源、命令入口、状态、AIR 时序与判定框。`--reference` 读取相邻的原引擎逐帧记录，按 State 200 进入时刻归零，核对判定窗口、双方停顿、生命变化和控制恢复。78 单位命中且 79、80 单位落空的轨迹用于验证边缘接触。命中帧才按住下后的场景可以蹲防，已进入蹲伏的场景会因受击框高度而落空。数据缺失、归档哈希或状态不符、图集异常、必需实测值不一致均导致转换失败。

输出为 `state200.fighter.json`、`state200.binding.json` 和 `state200.source.json`。来源记录包含输入及逐场轨迹的哈希与适用范围。可指定新的输出目录检验重复转换，程序拒绝覆盖同名文件。生成文件和原始资源留在本地忽略目录。实验只覆盖经审查的地面远距离站轻拳，包含原版静止站立和蹲伏的受击框、普通模式首个生效 tick 的结算和动作恢复。完整脚本解释、同招重进和近身优先级留待后续阶段。

## 阶段 15 多招式实验配置

在仓库根目录运行。转换使用原始 Terry ZIP、已导入的 Manifest，以及阶段 14 和阶段 15 的规范参考轨迹。生成目录必须独立，已有同名配置会被拒绝。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
py -m mugen_importer.state210 local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --state200-reference local-mugen/stage14-reference-replay/results/run-metadata.json `
  --reference local-mugen/stage15-reference/results/run-metadata.json `
  --fractional-reference local-mugen/stage15-reference-fractional/results/run-metadata.json `
  --output-dir data/imported-characters/terry98-stage15-data
py -m unittest discover -s tools/MugenImporter/tests
```

每套预设输出角色、绑定和来源三个 JSON。两套预设都包含 `5A` 对应的 State 200 和 `5B` 对应的 State 210。用 `default` 或 `mode2` 预设把同一重拳绑定到 Action 210 或 211。`--audit-only` 单独输出静态来源审查，无需运行轨迹。模式来源、距离边界、反击样本及限制见 [阶段 15 原引擎基准](STAGE15_REFERENCE.md)。图形启动方法见 [取证命令](../tools/Stage15Validation/README.md)。

## 阶段 16 近身轻拳与双段重拳

从仓库根目录生成独立的实验预设，使用已核对的三个阶段原引擎轨迹。源 ZIP、Manifest 和阶段 14、15 的输出可以继续复用。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
python -m mugen_importer.state205215 local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --state200-reference local-mugen/stage14-reference-replay/results/run-metadata.json `
  --state210-reference local-mugen/stage15-reference/results/run-metadata.json `
  --fractional-reference local-mugen/stage15-reference-fractional/results/run-metadata.json `
  --reference local-mugen/stage16-reference/results/run-metadata.json `
  --output-dir data/imported-characters/terry98-stage16-data
python -m unittest discover -s tools/MugenImporter/tests
```

`--audit-only` 仅审查 State 205/215 的原始脚本、AIR 和图集。完整转换在两个预设中保留远站分支，并从参考轨迹生成同键近身分支、各段停顿和伤害。原版 State 215 在第二段前重置接触记号，转换后为两段各分配一个接触组。来源 JSON 记录相同身体距离判定、两段接触、第二段单独命中和反击伤害所用轨迹。默认及模式 2 各生成角色、绑定和来源三个文件。图形启动与图集别名见 [阶段 16 取证命令](../tools/Stage16Validation/README.md)。

## 阶段 17 动态伤害与反击延续

在仓库根目录运行，输出到独立的新目录。转换先检查源脚本、既有阶段 14–16 参考、阶段 17 逐场轨迹及哈希，再生成默认及模式 2 的角色、绑定和来源 JSON。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
python -m mugen_importer.state_damage local-mugen/terry.zip --audit-only
python -m mugen_importer.state_damage local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --state200-reference local-mugen/stage14-reference-replay/results/run-metadata.json `
  --state210-reference local-mugen/stage15-reference/results/run-metadata.json `
  --fractional-reference local-mugen/stage15-reference-fractional/results/run-metadata.json `
  --state205215-reference local-mugen/stage16-reference/results/run-metadata.json `
  --reference local-mugen/stage17-reference/results/run-metadata.json `
  --reference local-mugen/stage17-reference-seed-1/results/run-metadata.json `
  --reference local-mugen/stage17-reference-seed-2026/results/run-metadata.json `
  --reference local-mugen/stage17-reference-replay/results/run-metadata.json `
  --reference local-mugen/stage17-reference-carry-edge/results/run-metadata.json `
  --reference local-mugen/stage17-reference-guard-v2/results/run-metadata.json `
  --output-dir data/imported-characters/terry98-stage17-data
python -m unittest discover -s tools/MugenImporter/tests -p test_state_damage.py
```

State 200、205、210 按反击状态选择基础值，再减去随机量。State 215 使用固定基础值，减去随机量后按当次接触的分母作整数除法。两条路径随后乘以脚本倍率并向下取整，应用实际攻防倍率后按最近整数结算生命变化。来源 JSON 记录每次原版 HitDef 对应的 `var(21)` 抽样、反击年龄、双段分母及命中伤害，并在同种子重跑时核对对应接触。同一站轻拳打断场景中，种子 1、1337、2026 的 `var(21)` 分别为 0、1、2，实际伤害为 61、54、46。格挡补测核对近身两段分母由 1 变为 2，近身和远身重拳的实扣均为 0。

NewKOF 的预设使用每席独立的可重放伤害随机流，原引擎使用全局共享随机流。跨引擎比较以记录中的随机量和反击等条件为准。预设覆盖审查过的普通模式站立轻重拳以及模式 2 的动作选择，其他脚本控制器仍超出范围。

## 阶段 18 站轻拳重进

用冻结的阶段 17 角色和绑定文件生成独立预设。转换校验四个基线文件的 SHA-256、原始 ZIP 和 Manifest，再校验所提供运行的完整轨迹及逐场哈希。两个预设各生成角色、绑定和来源三个 JSON。使用新的输出目录。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
python -m mugen_importer.state_reentry local-mugen/terry.zip data/imported-characters/terry98/manifest.json --audit-only
python -m mugen_importer.state_reentry local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --base-profile-dir data/imported-characters/terry98-stage17-data `
  --reference local-mugen/stage18-reference-canonical/results/run-metadata.json `
  --reference local-mugen/stage18-reference-down/results/run-metadata.json `
  --output-dir data/imported-characters/terry98-stage18-data
python -m unittest discover -s tools/MugenImporter/tests -p test_state_reentry.py
```

State 200／205 均可在零基动作 tick 7–11 以站轻拳重进。近身分支优先，身体距离等于 16 时选 205，超过 16 选 200。原版按键 Helper 99999 支持年龄最多 2 个逻辑 tick 的 `x` 边沿，转入新状态时读取当时的下方向。新预设用既有命令缓冲和可选取消规则表达这些条件。动作 tick 12 的原版普通技入口属于自然恢复后的再起手。收招中下加轻拳会走原版 State 400，阶段 18 仅覆盖站轻拳入口。保留阶段 17 的伤害和 RNG 范围，空中轨迹仅核对距离与分支选择。

## 阶段 20 近身轻拳取消弱 Power Wave

在仓库根目录运行，输出到新目录。转换校验阶段 19 默认与阶段 18 模式 2 的冻结文件、原始 ZIP、Manifest 和原引擎轨迹。输出包括两个预设各自的角色、绑定与来源 JSON。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
python -m mugen_importer.state_power_wave_routes local-mugen/terry.zip data/imported-characters/terry98/manifest.json --audit-only
python -m mugen_importer.state_power_wave_routes local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --stage19-profile-dir data/imported-characters/terry98-stage19-data `
  --stage18-profile-dir data/imported-characters/terry98-stage18-data `
  --reference local-mugen/stage20-reference/results `
  --reference local-mugen/stage20-reference-mixed/results `
  --output-dir data/imported-characters/terry98-stage20-data
python -m unittest discover -s tools/MugenImporter/tests -p test_state_power_wave_routes.py
```

默认预设从 Action 205 的 AIR 持续时间和 CNS 的严格元素比较计算出取消窗口 tick 4–6。窗口接受命中、格挡或空挥。tick 7–11 仍按身体距离优先尝试 205，其次 200。默认预设复用阶段 19 的 State 1000、Helper 1020 和绑定。原版模式 2 走 State 1050，实验对照只保留阶段 18 的普通技与重进。使用 `stage_assets.py` 分别将 `default.binding.json` 和 `mode2.binding.json` 安装到 `terry98-stage20-default` 和 `terry98-stage20-mode2`。

## 阶段 21 近重拳取消弱 Power Wave

先按[阶段 21 原引擎取证](STAGE21_REFERENCE.md)采集并验证轨迹。转换核对阶段 20 的角色、绑定与来源哈希，从 Action 215 与 State −2 的严格元素比较推导取消窗口。默认实验预设仅增加一条 215→1000 规则。模式 2 对照保留该预设原有的普通技与重进。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
python -m mugen_importer.state_power_wave_heavy local-mugen/terry.zip data/imported-characters/terry98/manifest.json --audit-only
python -m mugen_importer.state_power_wave_heavy local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --stage20-profile-dir data/imported-characters/terry98-stage20-data `
  --reference local-mugen/stage21-reference-final/results `
  --reference local-mugen/stage21-reference-mixed/results `
  --reference local-mugen/stage21-reference-guard-release/results `
  --output-dir data/imported-characters/terry98-stage21-data
foreach ($mode in 'default','mode2') {
  python tools/Stage14Validation/stage_assets.py --binding "data/imported-characters/terry98-stage21-data/$mode.binding.json" `
    --destination "data/imported-characters/terry98-stage21-$mode"
}
python -m unittest discover -s tools/MugenImporter/tests -p test_state_power_wave_heavy.py
```

## 阶段 22 蹲轻拳与弱 Power Wave

按[原引擎取证](STAGE22_REFERENCE.md)准备 26 条轨迹。转换器核对阶段 21 的预设哈希、原始 CNS/CMD/AIR 和 Manifest。默认实验预设增加 State 400、其原版轻拳回落与显式弱 Wave 取消。模式 2 只增加 State 400 本体。

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
$profiles = 'data/imported-characters/terry98-stage22-data'
py -m mugen_importer.state_crouch_wave local-mugen/terry.zip data/imported-characters/terry98/manifest.json `
  --base data/imported-characters/terry98-stage21-data --reference local-mugen/stage22-reference/results --output $profiles
foreach ($mode in 'default','mode2') {
  py tools/Stage14Validation/stage_assets.py --binding "$profiles/$mode.binding.json" `
    --destination "data/imported-characters/terry98-stage22-$mode"
}
py -m unittest discover -s tools/MugenImporter/tests -p test_state_crouch_wave.py
```

将转换输出改到新目录并加 `--body-only` 可单独验证本体。配置包含 Action 12 起身受击框和 Action 5010／5015 的地面轻击受击框。受击规则仅在攻击定义声明 `lightGroundReaction` 且目标配置了 `lightHitstun` 时生效。

## 阶段 23 蹲重拳与弱 Power Wave

转换核对六份阶段 22 冻结 JSON 的哈希，以及原始 State 410 的 CNS/CMD、Action 410 和站／蹲受击素材。先按原版参考采集轨迹并提取本体子集。以下命令将阶段 22 冻结数据生成到独立目录，保留现有历史预设。本体转换只读取提取的十条原始轨迹。 [原版参考](STAGE23_REFERENCE.md)

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
$base = 'local-mugen/stage23-repro-stage22'
$profiles = 'data/imported-characters/terry98-stage23-reserved'
$body = 'data/imported-characters/terry98-stage23-body-reserved'
$assets = @('local-mugen/terry.zip','data/imported-characters/terry98/manifest.json')
if (-not (Test-Path -LiteralPath $base)) {
  py -m mugen_importer.state_crouch_wave @assets --base data/imported-characters/terry98-stage21-data `
    --reference local-mugen/stage22-reference/results --output $base
  if ($LASTEXITCODE) { throw 'Stage 22 conversion failed' }
}
$source = @(
  '--reference','local-mugen/stage23-reference/results',
  '--reference','local-mugen/stage23-reference-supplement/results',
  '--reference','local-mugen/stage23-reference-boundaries/results',
  '--reference','local-mugen/stage23-reference-final-probes/results',
  '--reference','local-mugen/stage23-reference-combined-input/results')
if (-not (Test-Path -LiteralPath $profiles)) {
  py -m mugen_importer.state_crouch_heavy_wave @assets --base $base @source --output $profiles
  if ($LASTEXITCODE) { throw 'Stage 23 conversion failed' }
}
if (-not (Test-Path -LiteralPath $body)) {
  py -m mugen_importer.state_crouch_heavy_wave @assets --base $base `
    --reference local-mugen/stage23-reference-body/results --output $body --body-only
  if ($LASTEXITCODE) { throw 'Stage 23 body conversion failed' }
}
foreach ($mode in 'default','mode2') {
  $visual = "data/imported-characters/terry98-stage23-$mode"
  if (-not (Test-Path -LiteralPath $visual)) {
    py tools/Stage14Validation/stage_assets.py --binding "$profiles/$mode.binding.json" --destination $visual
    if ($LASTEXITCODE) { throw "Stage 23 $mode visual staging failed" }
  }
}
py -m unittest discover -s tools/MugenImporter/tests -p test_state_crouch_heavy_wave.py
```

站防、蹲防及持后续防分别读取 Action 150／151、130／131 的碰撞框。默认完整预设中，Helper 1020 只有晚段地面 HitDef 绑定 Action 5012／5017；早段击飞沿用原有实体反应链。单独的本体预设保留冻结的 Helper 1020 配置。

以上命令保留已有数据和别名。要复核已有文件，使用新输出目录生成后比对 SHA-256。 [候选哈希与验收](STAGE23_ACCEPTANCE.md)

前下＋重拳占用原版 State 310 的 CMD 候选优先级。`ReservedRisingUpper` 只保留合法地面输入的占用顺序，不分配招式。完整默认预设仍优先选择合规的弱 Power Wave。

## 阶段 24 蹲重脚与弱 Power Wave

转换校验阶段 23 正式预设、原始 State 440 的控制器与 Action 440，并从接收方资源读取 Action 5070 的受击框和 Action 5200 的受身素材。完整配置和独立本体分别生成。模式 2 输出共用本体，保留原版 1050 的隔离边界。原版素材和生成数据留在本地。 [原版采集](STAGE24_REFERENCE.md)

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
$base = 'data/imported-characters/terry98-stage23-reserved'
$profile = 'data/imported-characters/terry98-stage24-final'
$body = 'data/imported-characters/terry98-stage24-body-final-verified'
$assets = @('local-mugen/terry.zip','data/imported-characters/terry98/manifest.json')
$references = @('--reference','local-mugen/stage24-reference/results',
  '--reference','local-mugen/stage24-reference-controls/results',
  '--reference','local-mugen/stage24-reference-routes-final/results',
  '--reference','local-mugen/stage24-reference-boundaries/results',
  '--reference','local-mugen/stage24-reference-repeat/results',
  '--reference','local-mugen/stage24-reference-priority/results')
py -m mugen_importer.state_sweep_wave @assets --base $base @references --output $profile
if ($LASTEXITCODE) { throw 'Stage 24 conversion failed' }
py -m mugen_importer.state_sweep_wave @assets --base $base --body-only `
  --reference local-mugen/stage24-reference-body/results `
  --reference local-mugen/stage24-reference-controls/results --output $body
if ($LASTEXITCODE) { throw 'Stage 24 body conversion failed' }
foreach ($mode in 'default','mode2') {
  py tools/Stage14Validation/stage_assets.py --binding "$profile/$mode.binding.json" `
    --destination "data/imported-characters/terry98-stage24-$mode"
  if ($LASTEXITCODE) { throw "Stage 24 $mode visual staging failed" }
}
py -m unittest discover -s tools/MugenImporter/tests -p test_state_sweep_wave.py
```

完整配置的新增取消使用既有 `CancelDefinition`，保留阶段 23 的五个来源和 State 1000／Helper 1020。两个预设均启用 State 440 的扫倒反应和输入优先级。本体预设使用独立轨迹并省略 440 的取消项。 [阶段 24 验收](STAGE24_ACCEPTANCE.md)

## 阶段 25 蹲轻脚与普通技衔接

转换校验阶段 24 完整预设的 SHA-256，并从原包读取 State 430、Action 430、CMD、Helper 输入时钟和伤害变量。输入占位由真实 `2C` 接替。普通技衔接使用各来源的有效动作 tick。站立轻脚和自然恢复当帧的前＋x 仍只占用对应候选。弱 Power Wave 在 430 动作中没有直接许可，默认配置复用自然恢复入口。模式 2 保留已取证的自然手势占用。 [原版轨迹和采集命令](STAGE25_REFERENCE.md)

```powershell
$env:PYTHONPATH = 'tools/MugenImporter'
$env:NEWKOF_TERRY_AUDIT = (Get-Location).Path
$base = 'data/imported-characters/terry98-stage24-final'
$profile = 'data/imported-characters/terry98-stage25-final'
$assets = @('local-mugen/terry.zip','data/imported-characters/terry98/manifest.json')
$references = @('--reference','local-mugen/stage25-native-final-primary/results',
  '--reference','local-mugen/stage25-native-final-boundaries/results',
  '--reference','local-mugen/stage25-native-final-controls/results',
  '--reference','local-mugen/stage25-native-final-near/results',
  '--reference','local-mugen/stage25-native-final-high/results',
  '--reference','local-mugen/stage25-native-final-close/results',
  '--reference','local-mugen/stage25-native-final-startup/results')
# A fresh capture can place all 132 cases in a single directory.
if (Test-Path 'local-mugen/stage25-native-repro/results/run-metadata.json') {
  $references = @('--reference','local-mugen/stage25-native-repro/results')
}
py -m mugen_importer.state_crouch_light_kick @assets --base $base @references --output $profile
if ($LASTEXITCODE) { throw 'Stage 25 conversion failed' }
foreach ($mode in 'default','mode2') {
  $visual = "data/imported-characters/terry98-stage25-$mode"
  if (-not (Test-Path -LiteralPath $visual)) {
    py tools/Stage14Validation/stage_assets.py --binding "$profile/$mode.binding.json" --destination $visual
    if ($LASTEXITCODE) { throw "Stage 25 $mode visual staging failed" }
  }
}
py -m unittest discover -s tools/MugenImporter/tests -p test_state_crouch_light_kick.py
```

站姿反应绑定 5010／5015，蹲姿反应绑定 5020／5025。两个预设复用各自冻结的地面反应框。430 衔接站轻拳时先匹配近身 205，再回落至远身 200。430、400 在本阶段预设中启用后续蹲防续接。原版松后仍按下时继续格挡，松下则失去这一资格。阶段 25 的两个 Wave 候选允许在 16 tick 手势时限内先完成 236，再按 x。补充轨迹见 [原版状态与输入对照](STAGE25_REFERENCE.md)。要核对转换结果，在新目录生成后逐一比较输出与现有视觉别名的 SHA-256。
