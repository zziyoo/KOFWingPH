param([Parameter(Mandatory)][string]$Engine)

$ErrorActionPreference = 'Stop'
$probe = Join-Path $Engine 'src/combat_research.go'
$source = [IO.File]::ReadAllText($probe)
$newline = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }

function Add-TraceBlock([string]$Anchor, [string]$Block, [int]$Offset = 0) {
    $first = $source.IndexOf($Anchor, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $source.LastIndexOf($Anchor, [StringComparison]::Ordinal)) {
        throw "Expected one State 1200 trace anchor: $Anchor"
    }
    return $source.Insert($first + $Offset, ($Block.TrimEnd() -replace '\r?\n', $newline) + $newline)
}

$helpers = @'
// CP932 bytes for 弱ライジングタックル in the Terry package.
const researchRisingTackleCommand = "\x8e\xe3\x83\x89\x83\x43\x83\x57\x83\x93\x83\x4f\x83\x5e\x83\x62\x83\x4e\x83\x8b"
var researchRisingTackle = researchStage27 && strings.Contains(researchScenario, "-1200-")

func researchRisingTackleInput(c *Char, row map[string]interface{}) {
	var helperDP int32 = -1
	for _, helper := range sys.chars[c.playerNo][1:] {
		if helper.helperId == 99999 {
			helperDP = helper.varGet(14).ToI()
			break
		}
	}
	row["helperRisingTackle"] = helperDP
	row["rootRisingTackle"] = c.commandByName(researchRisingTackleCommand)
	row["moveReversed"] = c.moveReversed()
	row["reversalAttr"] = c.hitdef.reversal_attr
}

func researchRisingTackleReversal(c, getter *Char) {
	if !researchRisingTackle || researchEncoder == nil {
		return
	}
	researchStage17Event("reversal", map[string]interface{}{
		"seat": c.playerNo + 1, "target": getter.playerNo + 1,
		"sourceState": c.ss.no, "targetState": getter.ss.no,
		"sourceId": c.id, "targetId": getter.id,
		"reversalAttr": c.hitdef.reversal_attr, "incomingAttr": getter.hitdef.attr,
	})
}

'@
$source = Add-TraceBlock 'func researchCommandHelper' $helpers

$snapshot = @'
		if researchRisingTackle {
			researchRisingTackleInput(c, char)
		}
'@
$source = Add-TraceBlock ("`t`t" + 'chars = append(chars, char)') $snapshot

$anchor = 'if err := researchEncoder.Encode(row); err != nil {'
$first = $source.IndexOf('func researchStateChange', [StringComparison]::Ordinal)
$index = $source.IndexOf($anchor, $first, [StringComparison]::Ordinal) - 1
if ($first -lt 0 -or $index -lt $first) { throw 'Expected the State 1200 transition trace anchor' }
$transition = @'
	if researchRisingTackle {
		researchRisingTackleInput(c, row)
	}
'@
$source = $source.Insert($index, ($transition.TrimEnd() -replace '\r?\n', $newline) + $newline)

$hitdef = @'
	if researchRisingTackle && c.helperIndex == 0 && c.ss.no == 1200 {
		researchTraceWithOwner("hitdef", c.playerNo+1)
	}
'@
$anchor = 'func researchHitDef(c *Char) {' + $newline
$source = Add-TraceBlock $anchor $hitdef $anchor.Length
[IO.File]::WriteAllText($probe, $source)

$collision = Join-Path $Engine 'src/char.go'
$source = [IO.File]::ReadAllText($collision)
$newline = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }
$anchor = 'getter.hitdef.hitonce = -1 // Neutralize Hitdef' + $newline
$source = Add-TraceBlock $anchor (("`t" * 6) + 'researchRisingTackleReversal(c, getter)') $anchor.Length
[IO.File]::WriteAllText($collision, $source)
