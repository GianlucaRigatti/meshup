"""Generate assets and prepare them for manual one-by-one inspection.

Run from ``asset_generator_server`` while the server is running::

    uv run python generation_evaluation/evaluate_generation.py --count 50

Each successful response is moved to ``generation_evaluation`` as
``<case_number>-<asset_id>.glb``. Review the renamed GLB files manually and
update ``generation_success`` and ``notes`` in the CSV.
"""

from __future__ import annotations

import argparse
import csv
import json
import shutil
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen


BASE_URL = "http://127.0.0.1:8000"
ROOT = Path(__file__).resolve().parents[1]
ASSET_DIR = ROOT / "generated_assets"
EVALUATION_DIR = Path(__file__).resolve().parent
CSV_PATH = EVALUATION_DIR / "generation_evaluation.csv"
FIELDS = ["case_number", "prompt", "asset_id", "generation_success", "notes"]

PROMPTS = [
    "A camera",
    "A treasure chest",
    "A coffee mug",
    "A school desk",
    "A game controller",
    "A hard hat",
    "A camping lantern",
    "A cactus",
    "A book",
    "A dinner plate",
    "A fire extinguisher",
    "An adjustable wrench",
    "A beach ball",
    "A padlock",
    "A hiking boot",
    "A perfume bottle",
    "A violin",
    "A banana",
    "A pumpkin",
    "A flower vase",
    "A microphone",
    "A toolbox",
    "A keyboard",
    "A watering can",
    "A wristwatch",
    "A rocking horse",
    "A glass jar",
    "A beach umbrella",
    "A garden statue",
    "A skateboard",
    "A picnic basket",
    "A traffic cone",
    "Headphones",
    "A table lamp",
    "An apple",
    "A frying pan",
    "A water bottle",
    "A chess piece",
    "A toy train",
    "A desk fan",
    "A rubber duck",
    "A camping chair",
    "A wallet",
    "A teapot",
    "A flashlight",
    "A drone",
    "An electric guitar",
    "An umbrella",
    "A telescope",
    "A sailing boat",
]


def request_json(path: str, payload: dict | None, timeout: float) -> tuple[int, dict]:
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    request = Request(
        f"{BASE_URL.rstrip('/')}{path}",
        data=data,
        headers={"Content-Type": "application/json"} if data else {},
    )
    try:
        with urlopen(request, timeout=timeout) as response:
            return response.status, json.loads(response.read())
    except HTTPError as error:
        try:
            return error.code, json.loads(error.read().decode("utf-8"))
        except json.JSONDecodeError:
            return error.code, {}


def write_rows(rows: dict[int, dict[str, str]]) -> None:
    temporary = CSV_PATH.with_suffix(".csv.tmp")
    with temporary.open("w", newline="", encoding="utf-8") as output:
        writer = csv.DictWriter(output, fieldnames=FIELDS)
        writer.writeheader()
        writer.writerows(
            {field: rows[number].get(field, "") for field in FIELDS}
            for number in sorted(rows)
        )
    temporary.replace(CSV_PATH)


def prepare_asset(case_number: int, asset_id: str) -> Path:
    source = ASSET_DIR / f"{asset_id}.glb"
    destination = EVALUATION_DIR / f"{case_number}-{asset_id}.glb"
    if destination.is_file():
        return destination
    if not source.is_file():
        raise FileNotFoundError(source)
    shutil.move(source, destination)
    return destination


def generate(case_number: int, prompt: str, timeout: float) -> dict[str, str]:
    try:
        status, body = request_json("/generate_asset", {"prompt": prompt}, timeout)
    except OSError as error:
        return {"generation_success": "no", "notes": str(error)}
    error = body.get("error", {})

    asset_id = body.get("asset_id", "")
    if status != 200 or not asset_id:
        return {
            "asset_id": asset_id,
            "generation_success": "no",
            "notes": error.get("code", "API request failed"),
        }
    try:
        destination = prepare_asset(case_number, asset_id)
    except FileNotFoundError as error:
        return {
            "asset_id": asset_id,
            "generation_success": "no",
            "notes": f"Missing generated GLB: {error}",
        }
    return {
        "asset_id": asset_id,
        "generation_success": "yes",
        "notes": f"Manual review file: {destination.name}",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--count", type=int, default=50)
    parser.add_argument("--timeout", type=float, default=300.0)
    arguments = parser.parse_args()
    if not 1 <= arguments.count <= len(PROMPTS):
        parser.error(f"--count must be between 1 and {len(PROMPTS)}")

    EVALUATION_DIR.mkdir(parents=True, exist_ok=True)
    rows = {}
    for case_number, prompt in enumerate(PROMPTS[: arguments.count], start=1):
        print(f"[{case_number}/{arguments.count}] generating: {prompt}", flush=True)
        result = generate(case_number, prompt, arguments.timeout)
        rows[case_number] = {
            "case_number": str(case_number),
            "prompt": prompt,
            **result,
        }
        write_rows(rows)
        print(f"    {result['generation_success']} {result.get('asset_id', '')}")

    write_rows(rows)
    successes = sum(row.get("generation_success") == "yes" for row in rows.values())
    print(f"Prepared assets: {successes}/{arguments.count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())