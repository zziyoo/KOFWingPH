param([Parameter(Mandatory)][string]$Engine)

$ErrorActionPreference = 'Stop'
$probe = Join-Path $Engine 'src/combat_research.go'
$source = [IO.File]::ReadAllText($probe)
$newline = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }

function Add-TraceBlock([string]$Anchor, [string]$Block, [int]$Offset = 0) {
    $first = $source.IndexOf($Anchor, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $source.LastIndexOf($Anchor, [StringComparison]::Ordinal)) {
        throw "Expected one State 310 trace anchor: $Anchor"
    }
    return $source.Insert($first + $Offset, ($Block.TrimEnd() -replace '\r?\n', $newline) + $newline)
}

$helpers = @'
var researchRisingUpper = researchStage27 && strings.Contains(researchScenario, "-310-")

func researchRisingUpperInput(c *Char, row map[string]interface{}) {
	var helperDFY int32 = -1
	for _, helper := range sys.chars[c.playerNo][1:] {
		if helper.helperId == 99999 {
			helperDFY = helper.varGet(41).ToI()
			break
		}
	}
	row["helperRisingY"] = helperDFY
	row["rootY"] = c.commandByName("y")
	row["rootForward"] = c.commandByName("holdfwd")
	row["rootDown"] = c.commandByName("holddown")
	row["moveReversed"] = c.moveReversed()
}

'@
$source = Add-TraceBlock 'func researchCommandHelper' $helpers

$snapshot = @'
		if researchRisingUpper {
			researchRisingUpperInput(c, char)
		}
'@
$source = Add-TraceBlock ("`t`t" + 'chars = append(chars, char)') $snapshot

$anchor = 'if err := researchEncoder.Encode(row); err != nil {'
$first = $source.IndexOf('func researchStateChange', [StringComparison]::Ordinal)
$index = $source.IndexOf($anchor, $first, [StringComparison]::Ordinal) - 1
if ($first -lt 0 -or $index -lt $first) { throw 'Expected the State 310 transition trace anchor' }
$transition = @'
	if researchRisingUpper {
		researchRisingUpperInput(c, row)
	}
'@
$source = $source.Insert($index, ($transition.TrimEnd() -replace '\r?\n', $newline) + $newline)

$hitdef = @'
	if researchRisingUpper && c.helperIndex == 0 && c.ss.no == 310 {
		researchTraceWithOwner("hitdef", c.playerNo+1)
	}
'@
$anchor = 'func researchHitDef(c *Char) {' + $newline
$source = Add-TraceBlock $anchor $hitdef $anchor.Length
[IO.File]::WriteAllText($probe, $source)
