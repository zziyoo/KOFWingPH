param([Parameter(Mandatory)][string]$Engine)

$ErrorActionPreference = 'Stop'
$probe = Join-Path $Engine 'src/combat_research.go'
$source = [IO.File]::ReadAllText($probe)
$newline = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }

function Insert-TraceBlock([string]$Anchor, [string]$Block) {
    $first = $source.IndexOf($Anchor, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $source.LastIndexOf($Anchor, [StringComparison]::Ordinal)) {
        throw "Expected one State 250 trace anchor: $Anchor"
    }
    return $source.Insert($first, ($Block.TrimEnd() -replace '\r?\n', $newline) + $newline)
}

$probeHeader = @'
// The command name is encoded as CP932 in the original Terry archive.
const researchBlowbackCommand = "\x82\xd3\x82\xc1\x82\xc6\x82\xce\x82\xb5\x8d\x55\x8c\x82"
var researchBlowback = researchStage27 && strings.Contains(researchScenario, "-250-")

func researchBlowbackHelper(c *Char) int32 {
	for _, helper := range sys.chars[c.playerNo][1:] {
		if helper.helperId == 99999 {
			return helper.varGet(7).ToI()
		}
	}
	return -1
}

'@
$source = Insert-TraceBlock 'func researchCommandHelper' $probeHeader

$snapshot = @'
		if researchBlowback {
			char["blowbackCommand"] = c.commandByName(researchBlowbackCommand)
			char["helperBlowback"] = researchBlowbackHelper(c)
			char["vars"].(map[string]interface{})["var32"] = c.varGet(32).ToI()
		}
'@
$source = Insert-TraceBlock ("`t`t" + 'chars = append(chars, char)') $snapshot

$transition = @'
	if researchBlowback {
		row["blowbackCommand"] = c.commandByName(researchBlowbackCommand)
		row["helperBlowback"] = researchBlowbackHelper(c)
	}
'@
$anchor = 'if err := researchEncoder.Encode(row); err != nil {'
$first = $source.IndexOf('func researchStateChange', [StringComparison]::Ordinal)
$index = $source.IndexOf($anchor, $first, [StringComparison]::Ordinal) - 1
if ($first -lt 0 -or $index -lt $first) { throw 'Expected the State 250 transition trace anchor' }
$source = $source.Insert($index, ($transition.TrimEnd() -replace '\r?\n', $newline) + $newline)
[IO.File]::WriteAllText($probe, $source)
