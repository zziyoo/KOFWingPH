param(
    [Parameter(Mandatory)]
    [string]$Engine
)

$ErrorActionPreference = 'Stop'
function Add-TraceProbe {
    param([string]$File, [string]$Anchor, [string]$Replacement)
    $path = Join-Path $Engine "src/$File"
    $source = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $anchorText = $Anchor.TrimEnd()
    $index = $source.IndexOf($anchorText, [StringComparison]::Ordinal)
    if ($index -lt 0 -or $source.IndexOf($anchorText, $index + $anchorText.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Stage 17 trace anchor is missing or ambiguous in $path"
    }
    $source = $source.Remove($index, $anchorText.Length).Insert($index, $Replacement.TrimEnd())
    [IO.File]::WriteAllText($path, $source)
}

$anchor = @'
func Random() int32 {
	w := sys.randseed / 127773
'@
$replacement = @'
func Random() int32 {
	before := sys.randseed
	w := sys.randseed / 127773
'@
Add-TraceProbe 'common.go' $anchor $replacement
$anchor = @'
	return sys.randseed
}

func Srand(s int32) {
'@
$replacement = @'
	researchRandom(before, sys.randseed)
	return sys.randseed
}

func Srand(s int32) {
'@
Add-TraceProbe 'common.go' $anchor $replacement
$anchor = @'
	crun.varSet(v, RandI(min, max))
'@
$replacement = @'
	value := RandI(min, max)
	crun.varSet(v, value)
	researchVarRandom(crun, v, min, max, value)
'@
Add-TraceProbe 'bytecode.go' $anchor $replacement
$anchor = @'
	c.cnsvar[i] = v       // Create or update the key
'@
$replacement = @'
	before := c.cnsvar[i]
	c.cnsvar[i] = v       // Create or update the key
	researchDamageVariable(c, "set", i, before, c.cnsvar[i])
'@
Add-TraceProbe 'char.go' $anchor $replacement
$anchor = @'
	if _, ok := c.cnsvar[i]; ok {
		c.cnsvar[i] += v
	} else {
		c.cnsvar[i] = v
	}
	return BytecodeInt(c.cnsvar[i])
'@
$replacement = @'
	before := c.cnsvar[i]
	if _, ok := c.cnsvar[i]; ok {
		c.cnsvar[i] += v
	} else {
		c.cnsvar[i] = v
	}
	researchDamageVariable(c, "add", i, before, c.cnsvar[i])
	return BytecodeInt(c.cnsvar[i])
'@
Add-TraceProbe 'char.go' $anchor $replacement
