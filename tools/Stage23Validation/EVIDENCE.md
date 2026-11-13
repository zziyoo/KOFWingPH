# 阶段 23 固定帧证据索引

每个用例目录包含实际 Vulkan 运行的 `sprite/trace.json`、视觉开关两份 `duel.log` 及 PNG。目录以 `D:/NewKOF-stage23-` 为前缀。每个用例只计一组权威记录。补采恢复窗口的 62 张及实时两档的 6 张另计。

## 默认模式本体

14 组、2,326 逻辑帧、20,934 权威记录、602 张逐帧图。

- `vulkan-body-v2` 含 `hit-0`、`held-down-0`、`guard-0`、`crouch-guard-0`、`crouch-hit-0`、`interrupt-0`、`mirror-hit-0`、`stand-0`、`whiff-0`，各 1,809 行。
- `vulkan-recheck/body` 含 `dual-button-0`、`down-back-heavy-0`、`crouch-hit-release-0`，各 711 行。
- `vulkan-reserved/down-forward-both-0` 含 1,809 行。
- `vulkan-down-forward-final/down-forward-heavy-0` 含 711 行。

## 默认模式取消及后继对照

27 组、4,831 逻辑帧、45,182 权威记录、1,182 张逐帧图。

- `vulkan-routes-pilot` 采用 `approach-0` 1,809 行、`approach-11` 1,945 行、`crouch-hit-22` 1,944 行。
- `vulkan-low-pilot` 采用 `compete-11`、`incoming-wave-20` 和 `whiff-11`，各 1,835 行。
- `vulkan-guard-final/crouch-guard-22` 含 1,834 行。
- `vulkan-recheck/guards` 采用 `guard-22` 1,006 行、`guard-release-22` 988 行、`crouch-guard-release-22` 988 行。
- `vulkan-reserved` 采用 `hit-28`、`hit-43`、`hit-held-14`、`mirror-hit-22` 各 1,943 行，`hit-29`、`whiff-7`、`whiff-18` 各 1,809 行，`whiff-8`、`whiff-17`、`whiff-32` 各 1,835 行。
- `vulkan-replay-primary` 采用 `hit-14`、`hit-22` 各 1,754 行，`whiff-9`、`whiff-10` 各 1,070 行。
- `vulkan-bounded` 采用 `mixed-22` 1,625 行、`incoming-wave-22` 1,576 行、`incoming-wave-28` 1,610 行。

## 既有四来源及模式 2

- `coexistence/source215/hit-22` 1,288 行，`coexistence/source400/hit-held-11` 1,711 行，`coexistence/sources200-205/mixed-sequential` 1,903 行。3 组、504 逻辑帧、4,902 权威记录、177 张逐帧图。
- `vulkan-mode2-final` 的 `hit-0`、`hit-22` 和 `vulkan-mode2-input/down-forward-both-0` 各 711 行。3 组、237 逻辑帧、2,133 权威记录、129 张逐帧图。

## 复跑边界

- `mixed-22` 的第二枚 Helper 出生 X 原版为 152.27042，NewKOF 为 152.26373291015625。核对两次完整的 Q16 地面滑动和精确出生偏移。只为这组源坐标对照启用 0.01 容差，接触与 Helper 寿命逐帧核对。
- `incoming-wave-22/28` 分别在 F87／F91 出现原版舞台镜头边界压缩，水平差从 −0.5 累计到 −2.375。此前完整核对原版与 NewKOF 的 X，此后验证 NewKOF 权威水平速度和积分。Y、反应阶段与控制恢复仍逐帧对照。
- 取证保留原版在目标反应帧扣血、NewKOF 接触帧原子扣血和各自 RNG 抽样。未纳入的 310／1050 路线见[原版参考](../../docs/STAGE23_REFERENCE.md)。
