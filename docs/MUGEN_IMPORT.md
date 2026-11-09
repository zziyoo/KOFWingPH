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
