from __future__ import annotations

import argparse
import json
import platform
import statistics
import subprocess
import sys
import threading
import time
from pathlib import Path

import httpx
import torch
import trimesh

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from app.config import Settings

PROMPTS = [
    "a simple oak dining chair",
    "a small red medieval treasure chest",
    "a blue ceramic teapot",
    "a yellow toy excavator",
    "a gray hiking boot",
]
STAGES = (
    "text_to_image_ms",
    "preprocess_ms",
    "reconstruction_ms",
    "export_ms",
    "total_ms",
)


class NvidiaMemorySampler:
    def __init__(self) -> None:
        self.peak_mib = 0
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def __enter__(self):
        if platform.system() == "Windows":
            self._thread = threading.Thread(target=self._sample, daemon=True)
            self._thread.start()
        return self

    def __exit__(self, *_args) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=2)

    def _sample(self) -> None:
        while not self._stop.wait(0.1):
            value = hardware_value(
                [
                    "nvidia-smi",
                    "--query-gpu=memory.used",
                    "--format=csv,noheader,nounits",
                    "--id=0",
                ],
                "0",
            ).splitlines()[0]
            try:
                self.peak_mib = max(self.peak_mib, int(value))
            except ValueError:
                pass


def percentile(values: list[int], percentage: float) -> float:
    ordered = sorted(values)
    index = (len(ordered) - 1) * percentage
    lower = int(index)
    upper = min(lower + 1, len(ordered) - 1)
    fraction = index - lower
    return ordered[lower] + (ordered[upper] - ordered[lower]) * fraction


def hardware_value(command: list[str], fallback: str = "unknown") -> str:
    try:
        return subprocess.run(
            command,
            check=True,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
        ).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return fallback


def system_metadata() -> dict[str, str]:
    system = {
        "os": platform.platform(),
        "architecture": platform.machine(),
        "python": sys.version.split()[0],
        "torch": torch.__version__,
    }
    if platform.system() == "Darwin":
        system.update(
            chip=hardware_value(["sysctl", "-n", "machdep.cpu.brand_string"]),
            memory_bytes=hardware_value(["sysctl", "-n", "hw.memsize"]),
        )
    elif platform.system() in {"Windows", "Linux"}:
        system.update(
            gpu=hardware_value(
                ["nvidia-smi", "--query-gpu=name", "--format=csv,noheader", "--id=0"]
            ),
            driver=hardware_value(
                [
                    "nvidia-smi",
                    "--query-gpu=driver_version",
                    "--format=csv,noheader",
                    "--id=0",
                ]
            ),
            cuda=str(torch.version.cuda),
        )
    return system


def main() -> None:
    parser = argparse.ArgumentParser(description="Measure warm API generation times.")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    parser.add_argument("--runs", type=int, default=10)
    args = parser.parse_args()
    if args.runs < 1:
        parser.error("--runs must be at least 1")
    settings = Settings()
    base_url = args.base_url.rstrip("/")

    artifact_stats: list[dict] = []
    with (
        NvidiaMemorySampler() as gpu_sampler,
        httpx.Client(base_url=base_url, timeout=600.0) as client,
    ):
        ready = client.get("/readyz")
        ready.raise_for_status()
        ready_body = ready.json()

        stage_values: dict[str, list[int]] = {stage: [] for stage in STAGES}
        first_prompt = ""
        for index in range(args.runs):
            base_prompt = PROMPTS[index % len(PROMPTS)]
            prompt = f"{base_prompt}, benchmark sample {time.time_ns()}"
            if index == 0:
                first_prompt = prompt
            response = client.post("/generate_asset", json={"prompt": prompt})
            response.raise_for_status()
            asset_id = response.json()["asset_id"]
            metadata_path = settings.asset_output_dir / f"{asset_id}.json"
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
            for stage in STAGES:
                stage_values[stage].append(metadata["timings"][stage])
            artifact_stats.append(
                asset_statistics(settings.asset_output_dir / f"{asset_id}.glb")
            )

        cached_started = time.perf_counter()
        cached_response = client.post("/generate_asset", json={"prompt": first_prompt})
        cached_response.raise_for_status()
        cached_ms = round((time.perf_counter() - cached_started) * 1000)

    system = system_metadata()

    report = {
        "system": {
            **system,
            "image_generator": ready_body["image_generator"],
            "model_3d": ready_body["model_3d"],
            "device": ready_body["device"],
        },
        "results_ms": {
            stage: {
                "p50": round(statistics.median(values), 1),
                "p95": round(percentile(values, 0.95), 1),
            }
            for stage, values in stage_values.items()
        },
        "peak_nvidia_memory_mib": gpu_sampler.peak_mib,
        "artifacts": artifact_stats,
        "cached_response_ms": cached_ms,
    }
    print(json.dumps(report, indent=2))
    if report["results_ms"]["total_ms"]["p95"] > 3000:
        print(
            "WARNING: Warm p95 generation time is greater than the 3000 ms target.",
            file=sys.stderr,
        )


def asset_statistics(path: Path) -> dict:
    scene = trimesh.load(path, force="scene", process=False)
    meshes = list(scene.geometry.values())
    materials = [
        getattr(mesh.visual, "material", None)
        for mesh in meshes
        if mesh.visual.kind == "texture"
    ]
    texture_sizes = sorted(
        {
            image.size
            for material in materials
            for image in (
                getattr(material, "baseColorTexture", None),
                getattr(material, "image", None),
                getattr(material, "normalTexture", None),
            )
            if image is not None and getattr(image, "size", None) is not None
        }
    )
    return {
        "bytes": path.stat().st_size,
        "faces": sum(len(mesh.faces) for mesh in meshes),
        "uv": any(mesh.visual.kind == "texture" for mesh in meshes),
        "material_modes": sorted(
            {type(material).__name__ for material in materials if material is not None}
        ),
        "texture_dimensions": [list(size) for size in texture_sizes],
    }


if __name__ == "__main__":
    main()
