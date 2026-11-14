# Stage 27 native fixtures

`Cases.json` contains physical schedules and initial centre distances. `Batches.json` records the exact canonical case order and trace horizons. The seed is 1337. The runner prepares the same `5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0` IKEMEN GO tree, audio is disabled, and both seats use the original Terry. The default palette for P1 is 1. Mode 2 selects P1 palette 7 and verifies the mode-selection intro. P2 uses palette 2. `F` and `B` are facing-relative at the fixture's initial facing. The native trace checker also recomputes each physical input from the schedule.

Run from the project root in PowerShell 7. Use a fresh `local-mugen` directory for each capture. An earlier completed reference directory can be checked directly without recapture.

```powershell
$batches = Get-Content tools/Stage27Reference/Batches.json -Raw | ConvertFrom-Json -AsHashtable
$nativeRoot = 'local-mugen/stage27-native-repro'
foreach ($entry in $batches.GetEnumerator()) {
    $options = @{
        CaptureProfile = 'Stage27'
        RunDirectory = "$nativeRoot/$($entry.Key)"
        LastFrame = $entry.Value.lastFrame
        Cases = [string[]]$entry.Value.cases
    }
    if ($entry.Key -eq 'engine') {
        $options.Prepare = $true
    } else {
        $options.PreparedEngineDirectory = "$nativeRoot/engine/engine"
    }
    & tools/Stage14Reference/Run-Reference.ps1 @options
}
$resultDirs = @($batches.Keys | ForEach-Object { "$nativeRoot/$_/results" })
python tools/Stage27Reference/Check-Traces.py @resultDirs
```

The acceptance capture's `$resultDirs` are under `local-mugen/stage27-native-canonical`. That root groups completed traces from separate signed captures. Case metadata, original stdout/stderr and JSONL bytes were kept. Its aggregate metadata lists only the cases included in each group. The runner reproduces the same schedules from a new directory and writes fresh aggregate metadata and per-case hashes.

Use a separate output directory with `-LastFrame 260` and the four cases below when checking full target recovery after the late absolute launches. These are longer observations of existing cases and are kept outside the canonical set.

```powershell
$longCases = @('near-wave-whiff-4', 'near-wave-whiff-8', 'near-wave-whiff-29', 'near-wave-mixed-4') |
    ForEach-Object { "default-standing-heavy-kick-$_" }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage27 `
    -PreparedEngineDirectory "$nativeRoot/engine/engine" -LastFrame 260 `
    -RunDirectory "$nativeRoot/wave-extended" -Cases $longCases
```

The separate `default-standing-heavy-kick-sweep-b-buffer-away-ground-guard` trace holds the defender's Back through F62 to pair the outward threshold case with a full guard reaction. Its input is in `Cases.json`. Capture it the same way with `-LastFrame 145` in a fresh `away-guard` run directory.

`Check-Traces.py` validates hashes, mode, physical inputs, the normalized RNG path, full action clocks on whiff, collision timing, ground hit/guard metrics, movement commits, source permissions, and command choice at the fixture boundaries. `docs/STAGE27_REFERENCE.md` records source locations and the rules extracted from those traces.
