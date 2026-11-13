param(
    [string]$IkemenRoot = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Ikemen-GO'),
    [switch]$Prepare,
    [ValidateSet('Stage14', 'Stage15', 'Stage16', 'Stage17', 'Stage18')]
    [string]$CaptureProfile = 'Stage14',
    [string]$RunDirectory = 'local-mugen/stage14-reference',
    [ValidateRange(1, 2147483646)]
    [int]$Seed = 1337,
    [string[]]$Cases
)

$ErrorActionPreference = 'Stop'
$commit = '5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$workspace = [IO.Path]::GetFullPath((Join-Path $root $RunDirectory))
$localMugen = [IO.Path]::GetFullPath((Join-Path $root 'local-mugen')) + [IO.Path]::DirectorySeparatorChar
if (!$workspace.StartsWith($localMugen, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Reference output must be contained in local-mugen'
}
$engine = Join-Path $workspace 'engine'
$results = Join-Path $workspace 'results'
if ((Test-Path -LiteralPath (Join-Path $results 'run-metadata.json')) -or
    @(Get-ChildItem -LiteralPath $results -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Extension -eq '.jsonl' -or $_.Name -match '-(stdout|stderr)\.txt$'
    }).Count -gt 0) {
    throw "Reference evidence already exists at $results. Choose a fresh -RunDirectory."
}
$captureDir = $PSScriptRoot
$distances = [ordered]@{ whiff = 240; hit = 60; guard = 60; mirror = 60; 'crouch-guard' = 60; 'crouch-guard-late' = 60; 'crouch-guard-held' = 60; 'boundary-78' = 78; 'boundary-79' = 79; 'boundary-80' = 80 }
if ($CaptureProfile -eq 'Stage15') {
    $captureDir = Join-Path $root 'tools/Stage15Reference'
    $distances = [ordered]@{}
    foreach ($mode in @('default', 'mode2')) {
        foreach ($case in @('whiff', 'hit', 'guard', 'crouch-guard', 'crouch-guard-late', 'mirror', 'close-60', 'close-64', 'far-65', 'boundary-86', 'boundary-87', 'trade', 'interrupt')) {
            $distances["$mode-$case"] = switch ($case) {
                whiff { 240 }
                'close-60' { 60 }
                'close-64' { 64 }
                'far-65' { 65 }
                'boundary-86' { 86 }
                'boundary-87' { 87 }
                default { 72 }
            }
        }
        $fractionalCase = "$mode-far-64p5"
        if ($Cases -contains $fractionalCase) { $distances[$fractionalCase] = 64.5 }
    }
}
if ($CaptureProfile -eq 'Stage16') {
    $captureDir = Join-Path $root 'tools/Stage15Reference'
    $distances = [ordered]@{}
    foreach ($mode in @('default', 'mode2')) {
        foreach ($normal in @('light', 'heavy')) {
            $casesForNormal = @('hit', 'guard', 'crouch-guard', 'crouch-late', 'mirror', 'whiff', 'trade')
            $casesForNormal += if ($normal -eq 'light') { @('boundary-46', 'far-46p5', 'far-47') } else { @('sample-60', 'boundary-64', 'far-64p5', 'far-65', 'second-only', 'second-guard') }
            foreach ($case in $casesForNormal) {
                $distances["$mode-$normal-$case"] = switch ($case) {
                    'boundary-46' { 46 }
                    'far-46p5' { 46.5 }
                    'far-47' { 47 }
                    'sample-60' { 60 }
                    'boundary-64' { 64 }
                    'far-64p5' { 64.5 }
                    'far-65' { 65 }
                    'second-only' { 64 }
                    'second-guard' { 64 }
                    'whiff' { if ($normal -eq 'light') { 36 } else { 54 } }
                    default { if ($normal -eq 'light') { 44 } else { 62 } }
                }
            }
        }
        foreach ($scenario in @('guard-release', 'crouch-release', 'crouch-to-stand')) {
            $releaseCase = "$mode-heavy-$scenario"
            if ($Cases -contains $releaseCase) { $distances[$releaseCase] = 62 }
        }
        foreach ($distance in @(44, 70)) {
            $simultaneousCase = "$mode-light-both-$distance"
            if ($Cases -contains $simultaneousCase) { $distances[$simultaneousCase] = $distance }
        }
    }
}
if ($CaptureProfile -eq 'Stage17') {
    $captureDir = Join-Path $root 'tools/Stage15Reference'
    $fixtures = [ordered]@{
        'light-near' = 44; 'light-far' = 60; 'heavy-near' = 62; 'heavy-far' = 72
        'far-trade' = 72; 'near-trade' = 44; 'near-heavy-trade' = 62; 'interrupt' = 72; 'second-only' = 64
        'carry-live' = 80; 'carry-last' = 80; 'carry-expired' = 80
        'mirror-heavy-near' = 62; 'mirror-heavy-far' = 72; 'mirror-far-trade' = 72; 'mirror-second-only' = 64
    }
    $distances = [ordered]@{}
    foreach ($mode in @('default', 'mode2')) {
        foreach ($fixture in $fixtures.GetEnumerator()) {
            $distances["$mode-damage-$($fixture.Key)"] = $fixture.Value
        }
        $carryEdgeCase = "$mode-damage-carry-edge"
        if ($Cases -contains $carryEdgeCase) { $distances[$carryEdgeCase] = 80 }
        foreach ($guardFixture in @('heavy-guard', 'mirror-heavy-guard', 'heavy-far-guard', 'mirror-heavy-far-guard')) {
            $guardCase = "$mode-damage-$guardFixture"
            if ($Cases -contains $guardCase) { $distances[$guardCase] = 62 }
        }
    }
}
if ($CaptureProfile -eq 'Stage18') {
    $captureDir = Join-Path $root 'tools/Stage15Reference'
    $distances = [ordered]@{}
    foreach ($mode in @('default', 'mode2')) {
        foreach ($second in @(8, 9, 10, 11, 12, 13, 15, 16, 17, 18)) {
            $distances["$mode-reentry-whiff-$second"] = 100
        }
        foreach ($second in @(9, 17, 18, 19, 20, 21, 22, 23, 24, 26, 27, 28, 29)) {
            $distances["$mode-reentry-far-hit-$second"] = 60
        }
        foreach ($fixture in @(
            @{Name = 'held'; Distance = 100},
            @{Name = 'whiff-held-10'; Distance = 100},
            @{Name = 'whiff-held-11'; Distance = 100},
            @{Name = 'whiff-both-12'; Distance = 100},
            @{Name = 'whiff-heavy-12'; Distance = 100},
            @{Name = 'mirror-whiff-12'; Distance = 100},
            @{Name = 'near-jump-20'; Distance = 36},
            @{Name = 'boundary-46-23'; Distance = 46},
            @{Name = 'boundary-46p5-23'; Distance = 46.5},
            @{Name = 'near-hit-23'; Distance = 44},
            @{Name = 'near-tight-hit-23'; Distance = 36},
            @{Name = 'near-guard-23'; Distance = 44},
            @{Name = 'near-crouch-23'; Distance = 44},
            @{Name = 'mirror-near-hit-23'; Distance = 44},
            @{Name = 'far-guard-23'; Distance = 60},
            @{Name = 'far-hit-held-17'; Distance = 60},
            @{Name = 'whiff-down-11'; Distance = 100},
            @{Name = 'whiff-down-12'; Distance = 100},
            @{Name = 'far-hit-down-19'; Distance = 60},
            @{Name = 'approach-90-20'; Distance = 90},
            @{Name = 'approach-92-20'; Distance = 92},
            @{Name = 'approach-94p5-20'; Distance = 94.5},
            @{Name = 'approach-95-20'; Distance = 95},
            @{Name = 'mirror-approach-90-20'; Distance = 90},
            @{Name = 'approach-96-20'; Distance = 96},
            @{Name = 'approach-100-20'; Distance = 100},
            @{Name = 'approach-104-20'; Distance = 104},
            @{Name = 'mirror-approach-100-20'; Distance = 100}
        )) {
            $distances["$mode-reentry-$($fixture.Name)"] = $fixture.Distance
        }
    }
}
if (!$Cases) { $Cases = @($distances.Keys) }
foreach ($caseName in $Cases) {
    if (!$distances.Contains($caseName)) { throw "Unknown $CaptureProfile case $caseName" }
}
$runtime = Join-Path $IkemenRoot 'test-runtime/round2-5dac5caf'
$tools = Join-Path $IkemenRoot 'test-runtime/tools'
$archive = Join-Path $root 'local-mugen/terry.zip'
$terryHash = '299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698'
$expectedSourceHashes = @{
    'Terry-N.cns' = 'dcbee6b2378faf00c7b7912a393e3c640ff3b9179b113b14b7c2ce9a5ecbab75'
    'Terry.cmd' = 'ee57ffe1d6d4a2770b47590ab69177de79b24ccfc0153702dd3602f633507b59'
    'Terry.air' = '252646cc2940f776e94df0b7b7fd3772e098d75fda6e9b781135eaa36e0fd851'
}
if ($CaptureProfile -in @('Stage17', 'Stage18')) {
    $expectedSourceHashes['Terry-2-3.cns'] = '90b83cdacd33a3800a825ee42bdbd6502b7989f4dec358126587edeb4495fdc4'
}
if ($CaptureProfile -eq 'Stage18') {
    $expectedSourceHashes['Helper.cns'] = 'b2d683f70a3c6dd76587edee1a54f0638f9c6e006d5c70af598ff9b0163dd62a'
}

if ((Get-FileHash -LiteralPath $archive).Hash -ne $terryHash) {
    throw 'Terry archive hash differs from the Stage 14 reference'
}
if ($Prepare) {
    if (Test-Path -LiteralPath $engine) {
        throw "The isolated engine already exists at $engine. Run without -Prepare to reuse it."
    }
    New-Item -ItemType Directory -Force -Path $engine | Out-Null
    $sourceZip = Join-Path $workspace 'ikemen-source.zip'
    & git -C $IkemenRoot archive --format=zip --output=$sourceZip $commit
    if ($LASTEXITCODE -ne 0) { throw "Could not archive IKEMEN GO $commit" }
    Expand-Archive -LiteralPath $sourceZip -DestinationPath $engine
    foreach ($directory in @('data', 'external', 'font', 'stages', 'chars/kfm', 'sound')) {
        & robocopy (Join-Path $runtime $directory) (Join-Path $engine $directory) /E /XC /XN /XO /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { throw "Could not copy reference runtime directory $directory" }
    }
    $save = Join-Path $engine 'save'
    New-Item -ItemType Directory -Force -Path $save | Out-Null
    Copy-Item -LiteralPath (Join-Path $runtime 'save/config.ini') -Destination $save

    $terry = Join-Path $engine 'chars/terry'
    Expand-Archive -LiteralPath $archive -DestinationPath $terry
    if (!(Test-Path -LiteralPath (Join-Path $terry 'Terry-KOF98.def'))) {
        $def = @(Get-ChildItem -LiteralPath $terry -Recurse -File -Filter 'Terry-KOF98.def')
        if ($def.Count -ne 1) { throw 'Terry archive has no unique Terry-KOF98.def' }
        Copy-Item -Path (Join-Path $def[0].DirectoryName '*') -Destination $terry -Recurse
    }

    # A separate Git directory makes git apply resolve paths against the isolated engine.
    & git -C $engine init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the isolated source directory' }
    & git -C $engine apply (Join-Path $IkemenRoot 'docs/combat-research/runtime-round2/instrumentation.patch')
    if ($LASTEXITCODE -ne 0) { throw 'Could not apply the pinned research hooks' }
    Copy-Item -LiteralPath (Join-Path $captureDir 'combat_research.go.txt') -Destination (Join-Path $engine 'src/combat_research.go')
    $bytecode = Join-Path $engine 'src/bytecode.go'
    $source = [IO.File]::ReadAllText($bytecode)
    $newline = if ($source.Contains([string][char]13 + [char]10)) { [string][char]13 + [char]10 } else { [string][char]10 }
    $before = 'crun.hitdef.finalizeParams(crun, nil)' + $newline
    $index = $source.IndexOf($before, [StringComparison]::Ordinal)
    if ($index -lt 0) { throw 'Cannot find HitDef finalization in pinned source' }
    $source = $source.Insert($index + $before.Length, [string][char]9 + 'researchHitDef(crun)' + $newline)
    [IO.File]::WriteAllText($bytecode, $source)
    if ($CaptureProfile -eq 'Stage17') {
        & (Join-Path $root 'tools/Stage17Reference/Install-TraceHooks.ps1') -Engine $engine
    }
    if ($CaptureProfile -eq 'Stage18') {
        & (Join-Path $root 'tools/Stage18Reference/Install-TraceHooks.ps1') -Engine $engine
    }
    $env:MSYSTEM = 'MINGW64'
    $env:GOFLAGS = '-buildvcs=false'
    $env:STAGE14_IKEMEN_ENGINE = $engine
    $env:STAGE14_IKEMEN_GO_BIN = Join-Path $tools 'go/bin'
    & (Join-Path $tools 'msys64/usr/bin/bash.exe') -lc 'export PATH="$(cygpath "$STAGE14_IKEMEN_GO_BIN"):$PATH"; cd "$(cygpath "$STAGE14_IKEMEN_ENGINE")"; BUILD_FFMPEG=no DEBUG_BUILD=1 bash build/build.sh Win64'
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated IKEMEN GO runtime' }
}

$terry = Join-Path $engine 'chars/terry'
foreach ($filename in $expectedSourceHashes.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $terry $filename)).Hash -ne $expectedSourceHashes[$filename]) {
        throw "Reference source hash mismatch for $filename"
    }
}
$exe = Join-Path $engine 'Ikemen_GO.exe'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$env:PATH = (Join-Path $tools 'msys64/mingw64/bin') + ';' + $env:PATH
$arguments = '-p1 terry/Terry-KOF98.def -p2 terry/Terry-KOF98.def -s stages/kfm.def -time 99 -rounds 1 -windowed -nojoy -nosound'
$enc = [Text.UTF8Encoding]::new($false)
$metadata = [ordered]@{
    engine = 'IKEMEN GO'
    commit = $commit
    executable_sha256 = (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant()
    terry_archive_sha256 = $terryHash
    source_sha256 = $expectedSourceHashes
    config_sha256 = (Get-FileHash -LiteralPath (Join-Path $engine 'save/config.ini')).Hash.ToLowerInvariant()
    arguments = $arguments
    scenario_distance = $distances
}
if ($CaptureProfile -ne 'Stage14') { $metadata.capture_profile = $CaptureProfile }
if ($CaptureProfile -in @('Stage17', 'Stage18')) {
    $metadata.rng_seed = $Seed
    $metadata.executed_cases = @($Cases)
}
[IO.File]::WriteAllText((Join-Path $results 'run-metadata.json'), ($metadata | ConvertTo-Json -Depth 6) + [char]10, $enc)
foreach ($caseName in $Cases) {
    $env:RESEARCH_CASE = $caseName
    $env:RESEARCH_DISTANCE = $distances[$caseName].ToString([Globalization.CultureInfo]::InvariantCulture)
    if ($CaptureProfile -in @('Stage17', 'Stage18')) {
        $env:RESEARCH_SEED = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    $env:RESEARCH_TRACE = Join-Path $results "$caseName.jsonl"
    $stderr = Join-Path $results "$caseName-stderr.txt"
    $caseArguments = $arguments
    if ($CaptureProfile -ne 'Stage14') {
        $env:RESEARCH_MODE_TRACE = Join-Path $results "$caseName-mode.jsonl"
        $p1Palette = if ($caseName.StartsWith('mode2-')) { 7 } else { 1 }
        $caseArguments += " -p1.pal $p1Palette -p2.pal 2"
    }
    $process = Start-Process -FilePath $exe -WorkingDirectory $engine -ArgumentList $caseArguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $results "$caseName-stdout.txt") -RedirectStandardError $stderr
    $timeout = if ($CaptureProfile -eq 'Stage18') { 90000 } else { 45000 }
    if (!$process.WaitForExit($timeout)) {
        Stop-Process -Id $process.Id
        throw "Reference case $caseName timed out. Inspect $stderr"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0 -or (Get-Content -LiteralPath $stderr -Raw) -notmatch "COMBAT_RESEARCH_DONE $caseName") {
        throw "Reference case $caseName did not complete. Inspect $stderr"
    }
    $trace = Get-FileHash -LiteralPath $env:RESEARCH_TRACE
    $caseMetadata = [ordered]@{
        case = $caseName
        executable_sha256 = $metadata.executable_sha256
        config_sha256 = $metadata.config_sha256
        initial_distance = $distances[$caseName]
        trace_sha256 = $trace.Hash.ToLowerInvariant()
    }
    if ($CaptureProfile -ne 'Stage14') { $caseMetadata.arguments = $caseArguments }
    if ($CaptureProfile -in @('Stage17', 'Stage18')) { $caseMetadata.rng_seed = $Seed }
    [IO.File]::WriteAllText((Join-Path $results "$caseName-metadata.json"), ($caseMetadata | ConvertTo-Json) + [char]10, $enc)
    $trace
}
