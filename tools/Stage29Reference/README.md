# Stage 29 native reference

Run from the project root in PowerShell 7. The fixtures use the same original Terry ZIP, signed Stage 27 research binary, and fixed seed as the prior stage. Preserve output in fresh directories.

```powershell
$engine = 'local-mugen/stage27-native-engine/engine'
$root = 'local-mugen/stage29-native-repro'
$all = [string[]](Get-Content tools/Stage29Reference/Cases.json -Raw |
    ConvertFrom-Json -AsHashtable).Keys
$groups = [ordered]@{
    core = $all[0..29]
    edges = @($all[0]) + $all[30..60]
    extras = $all[61..68]
    air = @($all[69])
    mixed = $all[70..72]
    counter = @($all[73])
    old20 = @($all[74])
}
foreach ($entry in $groups.GetEnumerator()) {
    $last = if ($entry.Key -eq 'core') { 100 } else { 145 }
    & tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage29 -PreparedEngineDirectory $engine `
        -LastFrame $last -RunDirectory "$root/$($entry.Key)" -Cases ([string[]]$entry.Value)
}
$refs = @($groups.Keys | ForEach-Object { "$root/$_/results" })
py -I tools/Stage29Reference/Check-Traces.py @refs
```

The actual run directories were `local-mugen/stage29-native-{core,edges,extras,air,mixed,counter,old20}/results`. The extended hit in `edges` supersedes the shorter F100 copy in `core` so the canonical count remains 75. The checker verifies signed source hashes, original physical input bits by tick, the exact Action 250 sequence, contact formulas, launch and guard phases, source windows, and concrete transition outcomes. Its `completeVerified` field refers to this original engine set.

## Read-only command audit

The added instrumentation records the root Japanese command, Helper 99999's `var(7)`, and the sound category in root `var(32)`. It builds the same IKEMEN GO commit in a separate engine directory. Its output is checked against the pinned standard executable over common frames.

```powershell
$audit = $all | Where-Object {
    $_ -match '250-(hit$|side-cd-fore$|cd-|[yb]-then-)' -and $_ -like 'default-*' -and
    $_ -notmatch 'cd-old-'
}
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage29 -Prepare -LastFrame 145 `
    -RunDirectory "$root/audit" -Cases ([string[]]$audit)
py -I tools/Stage29Reference/Check-Traces.py @refs --input-audit "$root/audit/results"
```

This is a CPU-side native reference. Use the Stage 29 Vulkan runner for NewKOF render evidence.
