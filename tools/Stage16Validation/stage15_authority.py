"""Compare Stage 15 authority logs from frozen and candidate Vulkan runs."""

import argparse
from pathlib import Path

from normals_duel import AUTHORITY, require_same
from state210_duel import EXTRA_SCENES, MODES, SCENES


def compare(baseline, candidate, scenes):
    frames = rows = 0
    for mode in MODES:
        for scene in scenes:
            fixture = f"{mode}-{scene}"
            variants = []
            for root in (baseline, candidate):
                for variant in ("sprite", "plain"):
                    log = (root / fixture / variant / "duel.log").read_bytes().splitlines()
                    authority = [line for line in log if line.startswith(AUTHORITY)]
                    visual = sum(line.startswith(AUTHORITY[0]) for line in authority)
                    if not visual or len(authority) != 4 * visual:
                        raise ValueError(f"{root}/{fixture}/{variant}: incomplete authority audit")
                    variants.append(authority)
            for label, variant in zip(("baseline plain", "candidate sprite", "candidate plain"), variants[1:]):
                require_same(variants[0], variant, f"{fixture} {label} vs frozen sprite")
            print(f"{fixture}: {len(variants[0])} authority rows match in both candidate variants")
            rows += len(variants[0])
            frames += len(variants[0]) // 4
    return frames, rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--scene", choices=SCENES | EXTRA_SCENES, action="append")
    options = parser.parse_args()
    try:
        frames, rows = compare(options.baseline, options.candidate, options.scene or SCENES)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Stage 15 authority gate failed: {error}\n")
    print(f"Matched {frames} Stage 15 authority frames and {rows} rows.")


if __name__ == "__main__":
    main()
