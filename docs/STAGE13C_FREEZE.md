# 阶段 13C 封版

封版基线为 `dbf000e58971d30bf4f9cf32d71f1a0c2168786a`，Git 标签 `stage-13c` 指向该提交。封版时已核对本地与远端 `main`，并验证以下提交均为基线的祖先：

- 13C-A `e4ea3e10a196e9c4561dca365eb8825f1b88caf8`，动作审查和有窗口浏览器验收，见 [MUGEN_IMPORT.md](MUGEN_IMPORT.md)。
- 13C-B `5c83e82d9ff7c7b925bde9b9b9fd3e3362cc6e17`，九项对局动画验收，见 [STAGE13C_B_ACCEPTANCE.md](STAGE13C_B_ACCEPTANCE.md)。
- 13C-C `c23b2b4dee8e506152546e6754da6fa6e74e569a`、`d6ef764cd9bb758be70c77371f4adec8f7e399c0`、`dbf000e58971d30bf4f9cf32d71f1a0c2168786a`，投技、飞行道具、特效及最终门禁，见 [STAGE13C_C_ACCEPTANCE.md](STAGE13C_C_ACCEPTANCE.md)。

复查了现存日志：专项 152/152、全量 1204/1204、Debug 零警告零错误、Vulkan 像素夹具通过。重新比较存档日志内容，投中 80、飞行道具对撞 68、3v3 105、打断 70，共 323 帧角色与实体记录一致；取消场景 69 条角色日志一致。本次封版没有重新运行全量测试。

## 本地证据与复现

原始目录和文件保留。另将 5758 个文件备份到 Git 忽略的 `local-mugen/releases/stage-13c-dbf000e/evidence.zip`（255400002 字节）。归档含 PNG 与联系图、同次运行日志、命令参数、验证脚本、浏览器输出、导入 ZIP、Manifest、图集、绑定配置及当时的工程设置。包内 `evidence-manifest.json` 逐文件记录 SHA-256，归档写完后已逐条读取核验。归档 SHA-256 为 `d6d8fe3ed100aec9f28a86dd6b8eb76ccd380bfd70b00c89e3b08030c2465ab8`，同目录保留 `evidence.sha256`。证据含第三方素材，仅本地留存。Terry 原有缺图 126 帧、全透明引用 4 条继续保留诊断。

使用独立的 13C 检出目录复现时，从归档恢复忽略目录和验收所用工程设置。以下命令中的场次名可替换成矩阵所列的其他场次；按矩阵对照同次日志和状态编号。

```powershell
$godot = 'C:/Users/32360/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build NewKOF.csproj
& $godot --headless --path . --editor --import
$args13c = (Get-Content local-mugen/13cc_gate_throw.args.txt -Raw).Trim().Split(' ')
& $godot @args13c
& $godot --path . --rendering-method forward_plus --rendering-driver vulkan res://tests/fixtures/mugen/SpriteSmoke.tscn -- --gpu-pixels
dotnet test tests/BattleCore.Tests/BattleCore.Tests.csproj
```

专项结果保存在 `local-mugen/13cc_targeted_final.log`。复跑过滤器为 `FullyQualifiedName~ProjectileVisualTests|FullyQualifiedName~ThrowVisualTests|FullyQualifiedName~FeedbackTests|FullyQualifiedName~SpriteBindingTests|FullyQualifiedName~ProjectileTests|FullyQualifiedName~ThrowTests|FullyQualifiedName~SpriteTimelineTests|FullyQualifiedName~GuardVisualPoseTests|FullyQualifiedName~AnimationTests`。A 的浏览器命令见导入文档。

## 工作区工程设置

`project.godot` 自初始提交 `9937b3f` 后没有 Git 提交记录；现有差异在阶段 13C-B 矩阵中已有记录。文件时间为本机 2026-11-09 21:49:29。格式与 Godot 编辑器序列化一致，但没有证据可确认具体保存者、操作或用途。变化为编辑器模板注释、`[dotnet]` 区块排序、浮点写法，以及省略显式 `window/stretch/aspect="keep"`、`common/physics_ticks_per_second=60`、`renderer/rendering_method="forward_plus"`。主场景、程序集、视口尺寸与 stretch mode 值相同。已知验收命令显式指定 Vulkan/Forward+，固定逻辑推进由现有 C# 调度器管理。若以后要提交这些配置变化，应单独验证窗口拉伸与默认启动行为。

保留工作区文件原样，归档中另存验收副本。文件 SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`，Git blob 为 `8fa21348031c553ccd4184beb3019a65e88228ec`。
