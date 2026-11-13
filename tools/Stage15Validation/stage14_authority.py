"""Compare a fresh Stage 14 Godot run against its existing local evidence."""

import argparse
from pathlib import Path

from state210_duel import AUTHORITY, require_same


SCENES = ("whiff", "hit", "guard", "mirror", "crouch-guard", "crouch-guard-late", "boundary-78", "boundary-80")
PREFIX_COUNTS = (80, 80, 160)


def rows(directory, scene, variant):
    path = directory / scene / variant / "duel.log"
    lines = path.read_bytes().splitlines()
    actual = tuple(sum(line.startswith(prefix) for line in lines) for prefix in AUTHORITY)
    if actual != PREFIX_COUNTS:
        raise ValueError(f"{path}: expected authority counts {PREFIX_COUNTS}, got {actual}")
    return [line for line in lines if line.startswith(AUTHORITY)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    options = parser.parse_args()
    try:
        for scene in SCENES:
            expected = rows(options.baseline, scene, "sprite")
            require_same(expected, rows(options.baseline, scene, "plain"), f"{scene} Stage 14 sprite vs plain")
            for variant in ("sprite", "plain"):
                require_same(expected, rows(options.candidate, scene, variant), f"{scene} candidate {variant} vs Stage 14")
            print(f"{scene}: {len(expected)} authority rows match baseline in both visual variants")
    except (OSError, ValueError) as error:
        parser.exit(1, f"Stage 14 authority gate failed: {error}\n")
    print("Matched 640 Stage 14 logic frames and 2,560 authority rows.")


if __name__ == "__main__":
    main()
