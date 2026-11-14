"""Stage an independent local experimental alias from an existing Terry import."""

import argparse
import json
from pathlib import Path
import shutil

from frozen_authority import ROOT


IMPORTS = ROOT / "data/imported-characters"
TERRY = IMPORTS / "terry98"
ALIAS = "terry98-stage14"
ALIASES = (ALIAS, "terry98-stage15-default", "terry98-stage15-mode2",
           "terry98-stage16-default", "terry98-stage16-mode2",
           "terry98-stage17-default", "terry98-stage17-mode2",
           "terry98-stage18-default", "terry98-stage18-mode2", "terry98-stage19-default",
           "terry98-stage20-default", "terry98-stage20-mode2",
           "terry98-stage21-default", "terry98-stage21-mode2",
           "terry98-stage22-default", "terry98-stage22-mode2",
           "terry98-stage23-default", "terry98-stage23-mode2",
           "terry98-stage24-default", "terry98-stage24-mode2",
           "terry98-stage25-default", "terry98-stage25-mode2")


def stage(source, binding, destination):
    visual_id = json.loads(binding.read_text(encoding="utf-8-sig")).get("visualId")
    if visual_id != destination.name:
        raise ValueError(f"binding visualId {visual_id!r} does not match alias {destination.name!r}")
    manifest = source / "manifest.json"
    data = json.loads(manifest.read_text(encoding="utf-8-sig"))
    pages = sorted({sprite["region"]["page"] for sprite in data["sprites"] if sprite.get("region")})
    inputs = [(binding, "binding.json"), (manifest, "manifest.json")]
    inputs += [(source / f"atlas-{page:03}.png", f"atlas-{page:03}.png") for page in pages]
    if not pages or not all(path.is_file() for path, _ in inputs):
        raise ValueError(f"incomplete atlas set in {source}")
    if source in destination.parents or destination in source.parents:
        raise ValueError("source and destination must be independent directories")

    destination.mkdir(exist_ok=False)
    for path, filename in inputs:
        shutil.copyfile(path, destination / filename)
    print(f"Staged {len(pages)} atlas pages, manifest and binding in {destination}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=TERRY)
    parser.add_argument("--binding", type=Path, default=TERRY / "state200.binding.json")
    parser.add_argument("--destination", type=Path, default=IMPORTS / ALIAS)
    options = parser.parse_args()
    try:
        destination = options.destination.resolve()
        if destination.parent != IMPORTS.resolve() or destination.name not in ALIASES:
            parser.error(f"--destination must be an ignored experimental import directory: {', '.join(ALIASES)}")
        stage(options.source.resolve(strict=True), options.binding.resolve(strict=True), destination)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Visual staging failed: {error}\n")


if __name__ == "__main__":
    main()
