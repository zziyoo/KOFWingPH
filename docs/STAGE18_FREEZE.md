# 阶段 18 封存

正式验收提交 `71155157438798ca8e58cdfc1561783f20935fcd`。它的直接父提交 `9a8a13282132cb6d28e50d5073e16408fe01710c` 记录末端指令优先级取证，两者从 `f527ff642c57644e2893daf53d73b9db14e4c277` 顺序前进。正式验收范围见[阶段 18 验收](STAGE18_ACCEPTANCE.md)。

## 冻结证据

- 原版档案复核 80 场。规范 70 场、方向补测 6 场、自然收招优先级补测 4 场。核对了两套预设、四条重进路径、镜像、窗口 tick 7–11、年龄 2 的缓冲、冻结输入、长按、轻重拳竞争和持下。命令、SHA-256 与原始文件位置见[原引擎参考](STAGE18_REFERENCE.md)。
- 固定帧图形归档 52 场、92 个攻击实例、3432 张图。覆盖开关两侧 4888 帧、39104 条权威记录一致。真实 Vulkan 实时 30／60 FPS 均在旧实例收招到新实例起手两侧采样，实际两次入招相差 8 tick。复现使用新输出目录，保留 `local-mugen/stage18-validation/` 的原始帧、日志和参数。见[图形取证命令](../tools/Stage18Validation/README.md)。
- 既有 C# 专项 188/188、全量 1289/1289，Python 专项 8/8、全量 69/69，Debug 0 警告 0 错误及 `MUGEN_GPU_PIXELS_PASS` 仍为封版结果。阶段 13C–17 门禁保留原始对比，阶段 17 归档复核匹配 45 场、4247 帧、25482 行。阶段 18 的动态伤害按记录的 RNG 值复算，开关比较同时包含 `DamageAudit` 和 `ReentryAudit`。历史全量测试沿用封版日志。
- 受击打断在阶段 17 的冻结对局两套预设均已有直接证据。阶段 18 另补实际 Vulkan 对局，P1 在逻辑 26 重进 State 200，逻辑 27 在起手中被 P2 State 210 打断，未触发新实例的生效或接触，旧确认和局部伤害状态清除。覆盖开启、关闭和种子 1337 重跑的 672 条权威记录逐条一致。命令、画面及日志在 `local-mugen/stage18-closeout/interruption-final/`，独立于冻结 52 场。
- 跨引擎差异有独立复现，保留原始结果与归一化边界。见[兼容性审查](STAGE18_COMPATIBILITY.md)。

直接打断可重放 `interruption-final/{sprite,plain,seed-repeat}/duel.args.txt` 的实参。将各次 `--shot` 与 `--capture-dir` 的路径指向新目录，保持输入、`--fixed-fps 60`、`--damage-seed=1337` 和审计开关。`interruption-repro.py` 包含实际断言，在副本中把 `base` 改为新的空目录后执行。原始截图的状态版本 26／27／28 对应旧招收招、新招首帧与受击。

## 工程和远端

`project.godot` 的原有编辑器差异继续保留，文件 SHA-256 为 `2f4d1c2c5c1b088ce2607698690f176e10e34e50101c95633fee26cf4e95859b`，Git 内容哈希为 `8fa21348031c553ccd4184beb3019a65e88228ec`。历史 `stage-13c` 仍指向 `dbf000e58971d30bf4f9cf32d71f1a0c2168786a`。第三方资源、生成预设和真实图形证据留在本地忽略目录。

本轮首次 `git ls-remote origin refs/heads/main` 返回 `f527ff642c57644e2893daf53d73b9db14e4c277`。前两次正常 push 遇到 HTTPS 低速超时，分别为 18 秒及 HTTP/1.1 的 16 秒。使用现有 `gh` 登录作为本次调用的凭据助手后，正常快进推送成功，`git ls-remote` 直接确认远端 `main` 为阶段 18 正式验收提交 `71155157438798ca8e58cdfc1561783f20935fcd`。
