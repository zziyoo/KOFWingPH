import hashlib
import json
from argparse import ArgumentParser
from pathlib import Path

parser = ArgumentParser(description="Verify the isolated IKEMEN GO State 200 trace fixtures")
parser.add_argument("directory", type=Path)
args = parser.parse_args()
directory = args.directory
if not __debug__:
    raise SystemExit("Run the trace checker without Python optimization")
metadata = json.loads((directory / "run-metadata.json").read_bytes())
cases = ("whiff", "hit", "guard", "mirror", "crouch-guard", "crouch-guard-late")
all_results = {}
if all((directory / f"boundary-{distance}.jsonl").is_file() for distance in (78, 79, 80)):
    cases += ("boundary-78", "boundary-79", "boundary-80")
if "crouch-guard-held" in metadata["scenario_distance"]:
    cases += ("crouch-guard-held",)

for case in cases:
    file = directory / f"{case}.jsonl"
    raw = file.read_bytes()
    rows = [json.loads(line) for line in raw.splitlines()]
    end = {row["frame"]: row["chars"] for row in rows if row["phase"] == "end"}
    collision = {row["frame"]: row["chars"] for row in rows if row["phase"] == "after-collision"}
    hitdefs = [row["frame"] for row in rows if row["phase"] == "hitdef"]
    assert list(end) == list(range(66))
    assert list(collision) == list(end)
    assert len(rows) == 330 + len(hitdefs)
    assert all(row["case"] == case and row["roundState"] == 2 for row in rows)
    assert all(end[tick][0]["state"] == 0 for tick in range(5))
    assert end[5][0]["state"] == 200 and end[5][0]["anim"] == 200
    assert [collision[tick][0]["frame"]["sprite"] for tick in range(5, 12)] == [[200, 0]] * 3 + [[200, 1]] * 4
    assert all(collision[tick][0]["frame"]["clsn1"] is None for tick in range(5, 8))
    assert collision[8][0]["frame"]["clsn1"] == [[4, -88, 59, -73]]
    assert collision[8][0]["frame"]["clsn2"] == [[-20, -88, 19, -1], [-8, -100, 39, -69]]
    contacts = [tick for tick, chars in collision.items() if chars[1]["gethit"]]
    crouch = case in ("crouch-guard-late", "crouch-guard-held")
    guard = case == "guard" or crouch
    hit = case in ("hit", "mirror", "boundary-78")
    if hit or guard:
        assert contacts == [8] and hitdefs == [8]
        assert collision[8][1]["guarded"] == guard
        hitdef = collision[8][0]["hitDef"]
        assert hitdef["damage"] == 57 and hitdef["guardDamage"] == 0
        assert hitdef["hitPause"] == [11, 13] and hitdef["guardPause"] == [11, 11]
        assert hitdef["hitTime"] == hitdef["guardHitTime"] == hitdef["guardCtrlTime"] == 9
        assert hitdef["groundVel"] == hitdef["guardVel"] == [-6, 0, 0]
        assert end[8][1]["life"] == 1000
        assert end[9][1]["life"] == (954 if hit else 1000)
        assert end[8][0]["hitPause"] == 11 and end[8][1]["hitShake"] == (13 if hit else 11)
        assert len({(end[tick][0]["time"], end[tick][0]["elem"], end[tick][0]["elemTime"]) for tick in range(8, 20)}) == 1
        assert end[20][0]["time"] == end[19][0]["time"] + 1
        assert end[27][0]["state"] == 200 and end[28][0]["state"] == 0 and end[28][0]["ctrl"]
        target_start = end[8][1]["pos"][0]
        movement_start = 22 if hit else 20
        target_recovery = 32 if hit else 29
        assert all(end[tick][1]["pos"][0] == target_start for tick in range(8, movement_start))
        direction = -1 if case == "mirror" else 1
        assert end[movement_start][1]["pos"][0] - target_start == 6 * direction
        friction = 0.82 if crouch else 0.85
        assert abs(end[movement_start][1]["vel"][0] + 6 * friction) < 1e-5
        assert all(not end[tick][1]["ctrl"] for tick in range(8, target_recovery))
        assert end[target_recovery][1]["ctrl"]
        if hit:
            assert [end[tick][1]["state"] for tick in range(8, 33)] == [5000] * 14 + [5001] * 10 + [0]
            assert abs(end[31][1]["pos"][0] - end[30][1]["pos"][0] - (0.6 * abs(end[30][1]["vel"][0])) * direction) < 1e-4
        elif case == "guard":
            assert [end[tick][1]["state"] for tick in range(8, 30)] == [150] * 12 + [151] * 9 + [140]
        else:
            assert [end[tick][1]["state"] for tick in range(8, 30)] == [152] * 12 + [153] * 9 + [140]
            assert end[9][1]["anim"] == 151
            for tick in range(20, 29):
                ratio = 0.85 if case == "crouch-guard-late" and tick >= 26 else 0.82
                previous_speed = 6 if tick == 20 else end[tick - 1][1]["vel"][0]
                assert abs(abs(end[tick][1]["vel"][0]) - abs(previous_speed) * ratio) < 1e-5
    else:
        assert contacts == [] and hitdefs == [8, 9, 10, 11]
        assert end[16][0]["state"] == 200 and end[17][0]["state"] == 0 and end[17][0]["ctrl"]
        assert all(c["life"] == 1000 and c["hitPause"] == 0 and c["hitShake"] == 0 for chars in end.values() for c in chars)
        assert all(collision[tick][0]["frame"]["clsn1"] == [[4, -88, 59, -73]] for tick in range(8, 12))
        assert collision[12][0]["frame"]["clsn1"] is None
        if case == "crouch-guard":
            assert all(collision[tick][1]["guardFlag"] for tick in range(8, 12))
            assert all(min(box[1] for box in collision[tick][1]["frame"]["clsn2"]) == -72 for tick in range(8, 12))
    all_results[case] = end
    print(f"{case:18} {len(rows):3} {hashlib.sha256(raw).hexdigest()}")

for tick, chars in all_results["hit"].items():
    mirrored = all_results["mirror"][tick]
    for character, mirror in zip(chars, mirrored, strict=True):
        assert character["pos"][0] == -mirror["pos"][0] and character["facing"] == -mirror["facing"]
        for key in ("state", "time", "elem", "elemTime", "life", "hitPause", "hitShake", "ctrl", "frame"):
            assert character[key] == mirror[key]
