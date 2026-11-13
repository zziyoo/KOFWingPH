param(
    [string]$IkemenRoot = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Ikemen-GO'),
    [switch]$Prepare,
    [string]$RunDirectory = 'local-mugen/stage14-reference',
    [ValidateSet('whiff', 'hit', 'guard', 'mirror', 'crouch-guard', 'crouch-guard-late', 'crouch-guard-held', 'boundary-78', 'boundary-79', 'boundary-80')]
    [string[]]$Cases = @('whiff', 'hit', 'guard', 'mirror', 'crouch-guard', 'crouch-guard-late', 'crouch-guard-held', 'boundary-78', 'boundary-79', 'boundary-80')
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
$runtime = Join-Path $IkemenRoot 'test-runtime/round2-5dac5caf'
$tools = Join-Path $IkemenRoot 'test-runtime/tools'
$archive = Join-Path $root 'local-mugen/terry.zip'
$terryHash = '299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698'
$expectedSourceHashes = @{
    'Terry-N.cns' = 'dcbee6b2378faf00c7b7912a393e3c640ff3b9179b113b14b7c2ce9a5ecbab75'
    'Terry.cmd' = 'ee57ffe1d6d4a2770b47590ab69177de79b24ccfc0153702dd3602f633507b59'
    'Terry.air' = '252646cc2940f776e94df0b7b7fd3772e098d75fda6e9b781135eaa36e0fd851'
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
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'combat_research.go.txt') -Destination (Join-Path $engine 'src/combat_research.go')
    $bytecode = Join-Path $engine 'src/bytecode.go'
    $source = [IO.File]::ReadAllText($bytecode)
    $newline = if ($source.Contains([string][char]13 + [char]10)) { [string][char]13 + [char]10 } else { [string][char]10 }
    $before = 'crun.hitdef.finalizeParams(crun, nil)' + $newline
    $index = $source.IndexOf($before, [StringComparison]::Ordinal)
    if ($index -lt 0) { throw 'Cannot find HitDef finalization in pinned source' }
    $source = $source.Insert($index + $before.Length, [string][char]9 + 'researchHitDef(crun)' + $newline)
    [IO.File]::WriteAllText($bytecode, $source)
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
$distances = @{ whiff = 240; hit = 60; guard = 60; mirror = 60; 'crouch-guard' = 60; 'crouch-guard-late' = 60; 'crouch-guard-held' = 60; 'boundary-78' = 78; 'boundary-79' = 79; 'boundary-80' = 80 }
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
[IO.File]::WriteAllText((Join-Path $results 'run-metadata.json'), ($metadata | ConvertTo-Json -Depth 6) + [char]10, $enc)
foreach ($caseName in $Cases) {
    $env:RESEARCH_CASE = $caseName
    $env:RESEARCH_DISTANCE = [string]$distances[$caseName]
    $env:RESEARCH_TRACE = Join-Path $results "$caseName.jsonl"
    $stderr = Join-Path $results "$caseName-stderr.txt"
    $process = Start-Process -FilePath $exe -WorkingDirectory $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $results "$caseName-stdout.txt") -RedirectStandardError $stderr
    if (!$process.WaitForExit(45000)) {
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
    [IO.File]::WriteAllText((Join-Path $results "$caseName-metadata.json"), ($caseMetadata | ConvertTo-Json) + [char]10, $enc)
    $trace
}
