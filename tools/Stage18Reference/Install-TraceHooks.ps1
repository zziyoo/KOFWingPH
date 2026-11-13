param(
    [Parameter(Mandatory)]
    [string]$Engine
)

$ErrorActionPreference = 'Stop'
$path = Join-Path $Engine 'src/char.go'
$source = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$anchor = "`tc.ss.prevno = c.ss.no`n`tc.ss.no = Max(0, no)"
$index = $source.IndexOf($anchor, [StringComparison]::Ordinal)
if ($index -lt 0 -or $source.IndexOf($anchor, $index + $anchor.Length, [StringComparison]::Ordinal) -ge 0) {
    throw "Stage 18 state transition trace anchor is missing or ambiguous in $path"
}
$source = $source.Insert($index, "`tresearchStateChange(c, no)`n")
[IO.File]::WriteAllText($path, $source)
