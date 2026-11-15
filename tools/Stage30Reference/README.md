# Stage 30 native reference

Run from the NewKOF root in PowerShell 7. `Cases.json` specifies original physical inputs and initial distances. Default P1 uses palette 1. Mode 2 P1 starts with palette 7 and goes through the original `Grant` sequence. P2 uses palette 2. The runner fixes both modes at seed 1337 and verifies the original Terry archive and source identities before capture.

## Standard evidence

The recorded batches are `local-mugen/stage30-native-{pilot,input,routes,wave,wave-edge,contact,tackle}/results`. Each batch contains the SHA-256 of the pinned executable, source scripts, run configuration and per-case JSONL traces. The following commands reproduce those batches in a new directory.

```powershell
$engine = 'local-mugen/stage27-native-engine/engine'
$root = 'local-mugen/stage30-native-repro'
$all = [string[]](Get-Content tools/Stage30Reference/Cases.json -Raw |
    ConvertFrom-Json -AsHashtable).Keys
$groups = [ordered]@{
    pilot = $all | Where-Object {
        $_ -match 'heavy-kick-310-(hit|whiff|crouch-hit|guard|crouch-guard|air|wave-hit-19|wave-hit-20|wave-hit-21)$' -and
        $_ -ne 'mode2-standing-heavy-kick-310-crouch-guard'
    }
    input = $all | Where-Object {
        $_ -match '310-(input|global|natural|negative|trade|interrupt|seat2|guard-near)' -or
        $_ -match '(mirror-310-hit|mode2.*310-crouch-guard)'
    } | Where-Object { $_ -notmatch '310-input-(air-cross|805|df-over-805)$' }
    routes = $all | Where-Object {
        $_ -match '310-entry-' -and $_ -notmatch '(try-whiff|two-blocks|true-guard-last)'
    }
    wave = $all | Where-Object {
        $_ -match '310-(wave-separated|wave-tip|wave-air|wave-guard|wave-crouch-guard|wave-natural|wave-whiff)'
    }
    'wave-edge' = $all | Where-Object {
        $_ -match '310-(wave-250|wave-delayed|input-air-cross|wave-205-air-35)'
    }
    contact = $all | Where-Object {
        $_ -match '310-(contact-entry|wave-walking|wave-205-air-36|entry-245-try-whiff|crossing-jump)'
    }
    tackle = $all | Where-Object { $_ -match '310-tackle-priority' }
}
foreach ($entry in $groups.GetEnumerator()) {
    $last = if ($entry.Key -in @('routes','contact','tackle')) { 105 } else { 120 }
    & tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -PreparedEngineDirectory $engine `
        -LastFrame $last -RunDirectory "$root/$($entry.Key)" -Cases ([string[]]$entry.Value)
}
$refs = @($groups.Keys | ForEach-Object { "$root/$_/results" })
py -I tools/Stage30Reference/Check-Traces.py @refs
```

`completeVerified` requires signed body and input controls, all nine measured entry windows, the recovery boundary, both modes' contact permissions, and the earlier 1200 candidate's exact motion overlap. `F` is a reference fixture's global frame. Old ticks come from State −1 decision snapshots. Check attacks and collision boxes in `after-collision`, and check target reactions and first control in `end`.

The body pilot `310-guard` at distance 70 falls short after the defender walks back. Use `310-guard-near` for standing block. `wave-hit-19`, `wave-hit-20` in default mode and the separated forward variants demonstrate the original 1200 overlap. Focused default Wave positives are `wave-250-guard-54`, `wave-250-guard-55`, `wave-205-air-35` and `wave-walking-hit-22`. The exploratory `entry-245-try-whiff` jump setups hit an airborne target. Use `contact` for 245's grounded hit and guard or `245-whiff` for its noncontact window.

## Read-only command audit

The patch reads the root `y`, `holdfwd`, `holddown`, `MoveReversed`, and Helper 99999's `var(41)`. It also records each actual HitDef activation while a root is in State 310. It builds the same IKEMEN GO source as the standard executable in a separate directory. The checker compares the command probes with the standard executable over their matching frames and state transitions. Its only omitted data apart from the added fields is unseeded intro `var(21)` at the two F0 phases before the first seeded update.

```powershell
$first = @(
    'default-standing-heavy-kick-310-hit',
    'default-standing-heavy-kick-310-wave-hit-19',
    'mode2-standing-heavy-kick-310-wave-hit-20'
)
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -Prepare -LastFrame 120 `
    -RunDirectory "$root/audit" -Cases $first
$more = $all | Where-Object {
    $_ -match '310-(entry-250-(helper|split|frozen)|entry-(235|245)-helper|global-pause|input-(held|df-before|y-before|df-chord)|wave-250-guard-5[46]|crossing-jump)'
}
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -PreparedEngineDirectory "$root/audit/engine" `
    -LastFrame 105 -RunDirectory "$root/audit-more" -Cases ([string[]]$more)
py -I tools/Stage30Reference/Check-Traces.py @refs `
    --input-audit "$root/audit/results" --input-audit "$root/audit-more/results"
```

## Supplemental original engine boundaries

The instrumented `special-controls` and `special-edges` batches provide independent controls for actual 805 entry, 230/240's natural exits, both-hit 215 guard freezes, and the 310 permission when the opponent reverses it. The useful reversal cases are distances 70–84. At distance 65 the attack boxes do not overlap and ordinary 310 damage connects instead. The original-only reversal tests use the unimplemented State 1200 body. The `projectiles` and `exit` batches cover enemy Wave interruption and exact old tick 33 movement, jump, and fresh command distinctions. `245-whiff` uses the established Stage 27 sweep and recovery setup to prove the near kick permits 310 without contact.

```powershell
$controls = $all | Where-Object { $_ -match '310-(reversed-by|input-805|input-df-over|exact-natural|crouched-input)' }
$edges = $all | Where-Object { $_ -match '310-(reversal-distance|entry-215-two-blocks|entry-200-true-guard-last)' }
$projectiles = $all | Where-Object { $_ -match '310-projectile-cross' }
$recover = $all | Where-Object { $_ -match '310-recovery-' }
$whiffs = $all | Where-Object { $_ -match '310-true-245-whiff' }
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -PreparedEngineDirectory "$root/audit/engine" `
    -LastFrame 105 -RunDirectory "$root/special-controls" -Cases ([string[]]$controls)
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -PreparedEngineDirectory "$root/audit/engine" `
    -LastFrame 105 -RunDirectory "$root/special-edges" -Cases ([string[]]$edges)
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -PreparedEngineDirectory "$root/audit/engine" `
    -LastFrame 105 -RunDirectory "$root/projectiles" -Cases ([string[]]$projectiles)
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -PreparedEngineDirectory $engine `
    -LastFrame 75 -RunDirectory "$root/exit" -Cases ([string[]]$recover)
& tools/Stage14Reference/Run-Reference.ps1 -CaptureProfile Stage30 -PreparedEngineDirectory $engine `
    -LastFrame 145 -RunDirectory "$root/245-whiff" -Cases ([string[]]$whiffs)
py -I tools/Stage30Reference/Check-Traces.py @refs `
    --input-audit "$root/audit/results" --input-audit "$root/audit-more/results" `
    --controls "$root/special-controls/results" --controls "$root/special-edges/results" `
    --controls "$root/projectiles/results" --controls "$root/exit/results" --controls "$root/245-whiff/results"
```

This is native engine evidence. Use the Stage 30 validation tools for NewKOF runtime and actual Vulkan image captures. The paths under `local-mugen` are machine-local Git-ignored outputs.
