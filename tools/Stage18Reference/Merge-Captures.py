"""Collect individually completed Stage 18 traces from isolated runs of one native build."""

import argparse
import hashlib
import json
import shutil
from pathlib import Path


IDENTITY = ("engine", "commit", "executable_sha256", "terry_archive_sha256",
            "source_sha256", "config_sha256", "arguments", "capture_profile", "rng_seed")


def load(path):
    return json.loads(path.read_bytes())


def merge(output, inputs):
    if output.exists():
        raise ValueError(f"output already exists: {output}")
    metadata = load(inputs[0] / "run-metadata.json")
    if metadata["capture_profile"] != "Stage18":
        raise ValueError("capture profile must be Stage18")
    combined = {}
    captures = {}
    expected_files = {}
    for directory in inputs:
        path = directory / "run-metadata.json"
        run = load(path)
        if any(run[key] != metadata[key] for key in IDENTITY):
            raise ValueError(f"incompatible capture identity: {directory}")
        completed = []
        for name in run["executed_cases"]:
            case_path = directory / f"{name}-metadata.json"
            if not case_path.is_file():
                continue
            case = load(case_path)
            trace = directory / f"{name}.jsonl"
            digest = hashlib.sha256(trace.read_bytes()).hexdigest()
            if case["case"] != name or case["trace_sha256"] != digest or any(
                case[key] != run[key] for key in ("rng_seed", "config_sha256", "executable_sha256")
            ) or case["initial_distance"] != run["scenario_distance"][name]:
                raise ValueError(f"invalid individual trace metadata: {case_path}")
            if name in combined:
                raise ValueError(f"duplicate completed native case: {name}")
            files = [trace, case_path]
            for suffix in ("stderr.txt", "stdout.txt"):
                log = directory / f"{name}-{suffix}"
                if not log.is_file():
                    raise ValueError(f"missing native run log: {log}")
                files.append(log)
            if name.startswith("mode2-"):
                intro = directory / f"{name}-mode.jsonl"
                if not intro.is_file():
                    raise ValueError(f"missing native mode trace: {intro}")
                files.append(intro)
            combined[name] = case
            expected_files[name] = files
            completed.append(name)
        captures[directory.as_posix()] = {
            "metadata_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "completed_cases": completed,
        }
    if not combined:
        raise ValueError("no completed captures")
    output.mkdir(parents=True)
    for files in expected_files.values():
        for path in files:
            shutil.copy2(path, output)
    metadata["scenario_distance"] = {name: case["initial_distance"] for name, case in combined.items()}
    metadata["executed_cases"] = list(combined)
    metadata["captures"] = captures
    (output / "run-metadata.json").write_text(
        json.dumps(metadata, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"{len(combined)} completed traces from {len(captures)} runs")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("inputs", type=Path, nargs="+")
    args = parser.parse_args()
    merge(args.output, args.inputs)
