"""Compare a candidate Vulkan build with the frozen Stage 13C authority gate."""

import argparse
from dataclasses import dataclass
from hashlib import file_digest
from itertools import zip_longest
from pathlib import Path
import subprocess
from zipfile import ZipFile


ROOT = Path(__file__).resolve().parents[2]
ARCHIVE = ROOT / "local-mugen/releases/stage-13c-dbf000e/evidence.zip"
ARCHIVE_SHA256 = "d6d8fe3ed100aec9f28a86dd6b8eb76ccd380bfd70b00c89e3b08030c2465ab8"
VISUAL = b"[VisualAudit] "
ENTITY = b"[EntityAudit] "
SPRITE = b"[SpriteRenderAudit] "


@dataclass(frozen=True)
class Scenario:
    label: str
    frames: int
    entity_audit: bool = True

    def filename(self, sprites):
        if self.label == "cancel":
            return "13cc_gate_cancel" if sprites else "13cc_plain_cancel"
        variant = "sprite" if sprites else "plain"
        return f"13cc_authority_{self.label}_{variant}"


SCENARIOS = (
    Scenario("throw_hit", 80),
    Scenario("projectile_clash", 68),
    Scenario("projectile_team", 105),
    Scenario("projectile_interrupt", 70),
    Scenario("cancel", 69, entity_audit=False),
)


def authority_rows(log, scenario, sprites, label):
    lines = log.splitlines()
    rows = [line for line in lines if line.startswith((VISUAL, ENTITY))]
    expected_prefixes = [VISUAL, ENTITY] if scenario.entity_audit else [VISUAL]
    expected_prefixes *= scenario.frames
    if len(rows) != len(expected_prefixes):
        raise ValueError(f"{label}: expected {len(expected_prefixes)} authority rows, got {len(rows)}")
    for number, (row, prefix) in enumerate(zip(rows, expected_prefixes), start=1):
        if not row.startswith(prefix):
            raise ValueError(f"{label}: authority row {number} does not begin with {prefix!r}")
    if any(line.startswith(SPRITE) for line in lines) != sprites:
        raise ValueError(f"{label}: sprite render audit does not match the requested variant")
    return rows


def require_same(expected, actual, label):
    for number, (left, right) in enumerate(zip_longest(expected, actual), start=1):
        if left != right:
            raise ValueError(f"{label}: authority row {number} differs\nexpected {left!r}\nactual   {right!r}")


def frozen_args(archive, name, shot):
    args = archive.read(f"local-mugen/{name}.args.txt").decode("utf-8-sig").split()
    if args.count("--") != 1 or sum(arg.startswith("--shot=") for arg in args) != 1:
        raise ValueError(f"{name}: expected one user-argument separator and one final shot")
    # Retain the original inputs, frame budget, and renderer. Write only to the new evidence directory.
    return [
        f"--shot={shot.as_posix()}" if arg.startswith("--shot=") else arg
        for arg in args
        if not arg.startswith(("--capture-dir=", "--capture-start=", "--capture-end="))
    ]


def run_godot(exe, args, evidence_log, final_shot, *, timeout=50):
    command = [str(exe), *args]
    evidence_log.with_suffix(".args.txt").write_text(subprocess.list2cmdline(command) + "\n", encoding="utf-8")
    with evidence_log.open("wb") as log_file:
        run = subprocess.run(command, cwd=ROOT, stdout=log_file, stderr=subprocess.STDOUT, timeout=timeout)
    log = evidence_log.read_bytes()
    lines = log.splitlines()
    if run.returncode != 0 or not final_shot.is_file():
        raise ValueError(f"Godot exit {run.returncode} or final screenshot missing. See {evidence_log}")
    if not any(line.startswith(b"Godot Engine v4.7.2.stable.mono.") for line in lines):
        raise ValueError(f"Godot 4.7.2 Mono runtime marker missing. See {evidence_log}")
    if not any(line.startswith(b"Vulkan ") and b" - Forward+ - " in line for line in lines):
        raise ValueError(f"Vulkan Forward+ renderer marker missing. See {evidence_log}")
    if any(line.startswith((b"ERROR:", b"SCRIPT ERROR:")) for line in lines):
        raise ValueError(f"Godot reported a runtime error. See {evidence_log}")
    return log


def verify(archive, godot, output):
    for scenario in SCENARIOS:
        frozen = []
        candidate = []
        for sprites in (True, False):
            name = scenario.filename(sprites)
            expected = authority_rows(
                archive.read(f"local-mugen/{name}.log"), scenario, sprites, f"frozen {name}"
            )
            frozen.append(expected)
            if godot is not None:
                shot = output / f"{name}.png"
                log = run_godot(godot, frozen_args(archive, name, shot), output / f"{name}.log", shot)
                actual = authority_rows(log, scenario, sprites, f"candidate {name}")
                require_same(expected, actual, f"candidate vs frozen {name}")
                candidate.append(actual)
        require_same(*frozen, f"frozen sprite vs plain {scenario.label}")
        if candidate:
            require_same(*candidate, f"candidate sprite vs plain {scenario.label}")
        source = "candidate and frozen" if candidate else "frozen"
        print(f"{source}: {scenario.label} {scenario.frames} matching frames", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, default=ARCHIVE)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--archive-only", action="store_true")
    mode.add_argument("--godot", type=Path, help="Path to the Godot 4.7.2 Mono console executable")
    parser.add_argument("--output", type=Path, help="New ignored local-mugen subdirectory for the candidate run")
    options = parser.parse_args()

    try:
        with options.archive.open("rb") as source:
            if file_digest(source, "sha256").hexdigest() != ARCHIVE_SHA256:
                raise ValueError("Stage 13C archive SHA-256 does not match STAGE13C_FREEZE.md")
        output = None
        godot = None
        if options.godot is not None:
            godot = options.godot.resolve(strict=True)
            if options.output is None:
                parser.error("--godot requires --output")
            output = options.output.resolve()
            local = (ROOT / "local-mugen").resolve()
            if local not in output.parents:
                parser.error("--output must be a new directory under local-mugen")
            output.mkdir(parents=True, exist_ok=False)
        with ZipFile(options.archive) as archive:
            verify(archive, godot, output)
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        parser.exit(1, f"Authority gate failed: {error}\n")

    print("Matched 323 role/entity frames and 69 cancel frames.")


if __name__ == "__main__":
    main()
