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
