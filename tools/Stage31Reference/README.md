# 阶段 31 IKEMEN GO 取证

在仓库根目录使用 PowerShell 7。固定 IKEMEN GO 源提交、Terry ZIP、种子和原版规则见 [参考记录](../../docs/STAGE31_REFERENCE.md)。场景文件 `Cases.json` 记录距离与物理输入。方向按原版朝向解释。探针读取根命令、Helper 槽、实际启用的 HitDef 和 ReversalDef 事件。

现有证据位于未纳入 Git 的 `local-mugen/stage31-native-{pilot,input,audit,audit-more,edges}/results`。元数据与每场 SHA-256 绑定。重叠场景的物理输入必须匹配 `Cases.json`，校验器随后优先采用带只读探针的记录。

```powershell
$refs = @('pilot','input','audit','audit-more','edges') |
    ForEach-Object { "local-mugen/stage31-native-$_/results" }
py -I tools/Stage31Reference/Check-Traces.py @refs
```

从同一源重采可使用下列命令。`-Prepare` 生成新的隔离引擎。已有经哈希确认的探针程序可传给 `-PreparedEngineDirectory`。

```powershell
$names = [string[]](Get-Content tools/Stage31Reference/Cases.json -Raw | ConvertFrom-Json -AsHashtable).Keys
$target = 'local-mugen/stage31-native-repro'
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage31 -Prepare `
    -LastFrame 155 -RunDirectory $target -Cases $names
py -I tools/Stage31Reference/Check-Traces.py "$target/results"
```

反制的物理交互读 `after-collision` 和 `reversal`，自然出招与冻结结束读 `state-change`、`end`。输入完成和实际出招可能分属两个逻辑帧，以 `state-change` 的 `stateTime` 区分提前取消和自然恢复。
