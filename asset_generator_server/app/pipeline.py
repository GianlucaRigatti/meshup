from __future__ import annotations

import hashlib
import json
import logging
import os
import subprocess
import tempfile
import threading
import time
from collections.abc import Callable
from datetime import UTC, datetime
from pathlib import Path

from PIL import Image

from app.background_removal import TorchBiRefNet, prepare_foreground
from app.config import (
    BACKGROUND_REMOVAL_MODEL,
    BIREFNET_MODEL_ID,
    BIREFNET_MODEL_REVISION,
    DEVICE,
    FLUX_MODEL_ID,
    FLUX_MODEL_REVISION,
    FLUX_VAE_MODEL_ID,
    FLUX_VAE_REVISION,
    IMAGE_GENERATOR,
    MODEL_3D,
    OUTPUT_MODE,
    PIPELINE_SCHEMA_VERSION,
    PIPELINE_VERSION,
    PROMPT_SUFFIX,
    QWEN_MODEL_ID,
    QWEN_MODEL_REVISION,
    STABLE_DIFFUSION_CPP_REVISION,
    TRELLIS_CPP_REVISION,
    TRELLIS_MODEL_ID,
    TRELLIS_MODEL_REVISION,
    Settings,
    is_wsl,
)

LOGGER = logging.getLogger(__name__)
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
CommandRunner = Callable[[list[str], dict[str, str], int, str], None]


class BusyError(RuntimeError):
    pass


class GenerationError(RuntimeError):
    pass


class AssetGenerator:
    def __init__(
        self,
        settings: Settings,
        *,
        runner: CommandRunner | None = None,
        background_remover: TorchBiRefNet | None = None,
        pipeline_version: str = PIPELINE_VERSION,
    ) -> None:
        self.settings = settings
        self.output_dir = settings.asset_output_dir
        self.pipeline_version = pipeline_version
        self._runner = runner or _run_native
        self._background_remover = background_remover
        self._lock = threading.Lock()
        self.ready = False
        self.load_error: str | None = None

    @property
    def busy(self) -> bool:
        return self._lock.locked()

    @property
    def output_mode(self) -> str:
        return OUTPUT_MODE

    def load(self) -> None:
        self.output_dir.mkdir(parents=True, exist_ok=True)
        try:
            self._validate_installation()
            if self._background_remover is None:
                self._background_remover = TorchBiRefNet(
                    self.settings.background_removal_model_path
                )
            self._background_remover.load()
        except Exception as exc:
            self.ready = False
            self.load_error = str(exc)
            LOGGER.exception("The asset generator is not ready.")
            return
        self.ready = True
        self.load_error = None
        LOGGER.info("Asset generator ready: image=%s 3d=%s", IMAGE_GENERATOR, MODEL_3D)

    def generate(self, prompt: str) -> tuple[str, bool, dict[str, int]]:
        if not self.ready:
            raise RuntimeError("The asset generator is not ready.")

        normalized_prompt = " ".join(prompt.split())
        asset_id = self.asset_id(normalized_prompt)
        if self._cache_exists(asset_id):
            return asset_id, True, _cached_timings()
        if not self._lock.acquire(blocking=False):
            if self._cache_exists(asset_id):
                return asset_id, True, _cached_timings()
            raise BusyError

        started = time.perf_counter()
        try:
            if self._cache_exists(asset_id):
                return asset_id, True, _cached_timings()
            self._remove_artifacts(asset_id)
            seed = self.seed(asset_id)
            timings: dict[str, int] = {}
            with tempfile.TemporaryDirectory(
                dir=self.output_dir.parent, prefix="asset-generator-"
            ) as temporary_dir:
                temporary = Path(temporary_dir)
                prompt_path = temporary / "prompt.txt"
                source_image = temporary / "source.png"
                temporary_glb = temporary / "asset.glb"
                cutout_image = temporary / "input.png"
                prompt_path.write_text(
                    normalized_prompt + PROMPT_SUFFIX, encoding="utf-8"
                )

                stage = time.perf_counter()
                self._runner(
                    self._flux_command(prompt_path, source_image, seed),
                    _runtime_environment(
                        self.settings.stable_diffusion_executable_path.parent
                    ),
                    self.settings.image_timeout_seconds,
                    "FLUX.2 Klein",
                )
                _require_signature(source_image, PNG_SIGNATURE, "FLUX output PNG")
                timings["text_to_image_ms"] = _elapsed_ms(stage)

                stage = time.perf_counter()
                if self._background_remover is None:
                    raise RuntimeError("BiRefNet has not been loaded.")
                with Image.open(source_image) as generated:
                    cutout = prepare_foreground(
                        generated.convert("RGB"), self._background_remover
                    )
                cutout.save(cutout_image, format="PNG")
                _require_signature(cutout_image, PNG_SIGNATURE, "BiRefNet cutout PNG")
                timings["preprocess_ms"] = _elapsed_ms(stage)
                timings.update(self._background_remover.last_timings)

                stage = time.perf_counter()
                self._runner(
                    self._trellis_command(cutout_image, temporary_glb, seed),
                    _runtime_environment(self.settings.trellis_build_path),
                    self.settings.trellis_timeout_seconds,
                    "TRELLIS.2",
                )
                _require_signature(temporary_glb, b"glTF", "TRELLIS output GLB")
                timings["reconstruction_ms"] = _elapsed_ms(stage)
                timings["total_ms"] = _elapsed_ms(started)

                os.replace(temporary_glb, self._asset_path(asset_id))
                os.replace(cutout_image, self._image_path(asset_id))
                self._write_metadata(
                    self._metadata_path(asset_id),
                    self._metadata(asset_id, normalized_prompt, seed, timings),
                )
            return asset_id, False, timings
        except BusyError:
            raise
        except Exception as exc:
            LOGGER.exception("Asset generation failed for asset %s", asset_id)
            self._remove_artifacts(asset_id)
            raise GenerationError from exc
        finally:
            self._lock.release()

    def asset_id(self, prompt: str) -> str:
        normalized = " ".join(prompt.split())
        return hashlib.sha256(
            f"{self.pipeline_version}\0{normalized}".encode()
        ).hexdigest()[:32]

    @staticmethod
    def seed(asset_id: str) -> int:
        return int(asset_id[:16], 16) % (2**31)

    def _validate_installation(self) -> None:
        if not is_wsl():
            raise RuntimeError("The asset generator requires WSL 2.")
        missing = [
            path.resolve()
            for path in self.settings.required_files
            if not path.is_file()
        ]
        if missing:
            raise FileNotFoundError(
                "The fixed model installation is incomplete. Missing files:\n- "
                + "\n- ".join(str(path) for path in missing)
                + "\nRun `uv run python scripts/install_models.py --accept-licenses`."
            )
        if not self.settings.background_removal_model_path.is_dir():
            raise FileNotFoundError(
                "The BiRefNet checkpoint is missing. Run the model installer."
            )
        _require_git_revision(
            self.settings.stable_diffusion_source_path,
            STABLE_DIFFUSION_CPP_REVISION,
        )
        _require_git_revision(self.settings.trellis_source_path, TRELLIS_CPP_REVISION)
        _require_marker(
            self.settings.flux_model_path / ".model-revision", FLUX_MODEL_REVISION
        )
        _require_marker(
            self.settings.flux_model_path / ".text-encoder-revision",
            QWEN_MODEL_REVISION,
        )
        _require_marker(
            self.settings.flux_model_path / ".vae-revision", FLUX_VAE_REVISION
        )
        _require_marker(
            self.settings.trellis_model_root / ".model-revision",
            TRELLIS_MODEL_REVISION,
        )
        _require_marker(
            self.settings.background_removal_model_path / ".model-revision",
            BIREFNET_MODEL_REVISION,
        )
        _check_help(
            self.settings.stable_diffusion_executable_path,
            _runtime_environment(self.settings.stable_diffusion_executable_path.parent),
        )
        _check_help(
            self.settings.trellis_executable_path,
            _runtime_environment(self.settings.trellis_build_path),
        )

    def _flux_command(self, prompt: Path, output: Path, seed: int) -> list[str]:
        return [
            str(self.settings.stable_diffusion_executable_path.resolve()),
            "--diffusion-model",
            str(self.settings.flux_diffusion_path.resolve()),
            "--llm",
            str(self.settings.flux_text_encoder_path.resolve()),
            "--vae",
            str(self.settings.flux_vae_path.resolve()),
            "--prompt-file",
            str(prompt.resolve()),
            "--output",
            str(output.resolve()),
            "--steps",
            "4",
            "--cfg-scale",
            "1.0",
            "--sampling-method",
            "euler",
            "--width",
            "768",
            "--height",
            "768",
            "--seed",
            str(seed),
            "--rng",
            "cpu",
            "--diffusion-fa",
            "--offload-to-cpu",
            "--auto-fit",
            "--max-vram",
            "cuda0=11",
        ]

    def _trellis_command(self, image: Path, output: Path, seed: int) -> list[str]:
        return [
            str(self.settings.trellis_executable_path.resolve()),
            str(image.resolve()),
            str(output.resolve()),
            "--models",
            str(self.settings.trellis_model_path.resolve()),
            "--gpu",
            "0",
            "--seed",
            str(seed),
            "--res",
            "512",
            "--max-tokens",
            "49152",
            "--atlas",
            "1024",
            "--webp",
            "off",
            "--box-uv",
            "--require-gpu",
        ]

    def _cache_exists(self, asset_id: str) -> bool:
        return all(
            path.is_file()
            for path in (
                self._asset_path(asset_id),
                self._image_path(asset_id),
                self._metadata_path(asset_id),
            )
        )

    def _remove_artifacts(self, asset_id: str) -> None:
        for path in (
            self._asset_path(asset_id),
            self._image_path(asset_id),
            self._metadata_path(asset_id),
            self._metadata_path(asset_id).with_suffix(".json.tmp"),
        ):
            path.unlink(missing_ok=True)

    def _asset_path(self, asset_id: str) -> Path:
        return self.output_dir / f"{asset_id}.glb"

    def _image_path(self, asset_id: str) -> Path:
        return self.output_dir / f"{asset_id}.png"

    def _metadata_path(self, asset_id: str) -> Path:
        return self.output_dir / f"{asset_id}.json"

    def _metadata(
        self,
        asset_id: str,
        prompt: str,
        seed: int,
        timings: dict[str, int],
    ) -> dict:
        return {
            "schema_version": PIPELINE_SCHEMA_VERSION,
            "pipeline_version": self.pipeline_version,
            "asset_id": asset_id,
            "prompt_hash": hashlib.sha256(prompt.encode()).hexdigest(),
            "seed": seed,
            "image_generator": IMAGE_GENERATOR,
            "model_3d": MODEL_3D,
            "background_removal_model": BACKGROUND_REMOVAL_MODEL,
            "models": {
                "image": {"id": FLUX_MODEL_ID, "revision": FLUX_MODEL_REVISION},
                "text_encoder": {
                    "id": QWEN_MODEL_ID,
                    "revision": QWEN_MODEL_REVISION,
                },
                "vae": {
                    "id": FLUX_VAE_MODEL_ID,
                    "revision": FLUX_VAE_REVISION,
                },
                "asset": {
                    "id": TRELLIS_MODEL_ID,
                    "revision": TRELLIS_MODEL_REVISION,
                },
                "background_removal": {
                    "id": BIREFNET_MODEL_ID,
                    "revision": BIREFNET_MODEL_REVISION,
                },
            },
            "runtimes": {
                "stable_diffusion_cpp": STABLE_DIFFUSION_CPP_REVISION,
                "trellis_cpp": TRELLIS_CPP_REVISION,
            },
            "device": DEVICE,
            "output_mode": OUTPUT_MODE,
            "output_settings": {
                "image_resolution": [768, 768],
                "background_removal_resolution": 1024,
                "foreground_ratio": 435 / 512,
                "geometry_resolution": 512,
                "texture_resolution": 1024,
                "box_uv": True,
            },
            "created_at": datetime.now(UTC).isoformat(),
            "timings": timings,
        }

    @staticmethod
    def _write_metadata(path: Path, metadata: dict) -> None:
        temporary = path.with_suffix(".json.tmp")
        temporary.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        os.replace(temporary, path)


def _run_native(
    command: list[str], environment: dict[str, str], timeout: int, label: str
) -> None:
    try:
        completed = subprocess.run(
            command,
            env=environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=timeout,
        )
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"{label} timed out after {timeout} seconds.") from exc
    if completed.returncode != 0:
        tail = completed.stdout[-4000:].strip()
        message = f"{label} failed with exit code {completed.returncode}."
        if tail:
            message += f"\n{tail}"
        raise RuntimeError(message)


def _runtime_environment(binary_dir: Path) -> dict[str, str]:
    binary_dir = binary_dir.resolve()
    existing = os.environ.get("LD_LIBRARY_PATH")
    library_path = str(binary_dir) if not existing else f"{binary_dir}:{existing}"
    return {**os.environ, "LD_LIBRARY_PATH": library_path}


def _check_help(executable: Path, environment: dict[str, str]) -> None:
    _run_native([str(executable.resolve()), "--help"], environment, 60, executable.name)


def _require_git_revision(source: Path, expected: str) -> None:
    completed = subprocess.run(
        ["git", "-C", str(source.resolve()), "rev-parse", "HEAD"],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        timeout=30,
    )
    actual = completed.stdout.strip() if completed.returncode == 0 else None
    if actual != expected:
        raise RuntimeError(
            f"Runtime revision mismatch at {source}: expected {expected}, got {actual!r}."
        )


def _require_marker(path: Path, expected: str) -> None:
    actual = path.read_text(encoding="utf-8").strip() if path.is_file() else None
    if actual != expected:
        raise RuntimeError(
            f"Model revision mismatch at {path.parent}: expected {expected}, got {actual!r}."
        )


def _require_signature(path: Path, signature: bytes, label: str) -> None:
    if not path.is_file():
        raise RuntimeError(f"{label} is missing or invalid.")
    with path.open("rb") as artifact:
        actual = artifact.read(len(signature))
    if actual != signature:
        raise RuntimeError(f"{label} is missing or invalid.")


def _cached_timings() -> dict[str, int]:
    return {"text_to_image_ms": 0, "reconstruction_ms": 0, "total_ms": 0}


def _elapsed_ms(started: float) -> int:
    return max(0, round((time.perf_counter() - started) * 1000))
