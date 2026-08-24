from __future__ import annotations

import hashlib
import json
import logging
import os
import tempfile
import threading
import time
from datetime import UTC, datetime
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image

from app.backends import AssetBackend, ImageBackend, create_backends
from app.config import Settings
from app.presets import PipelinePreset

LOGGER = logging.getLogger(__name__)
PROMPT_SUFFIX = (
    ", one centered object, full object visible, isolated neutral studio product "
    "render, plain light gray background, no text, no floor, no shadows, "
    "no surrounding objects"
)


class BusyError(RuntimeError):
    pass


class GenerationError(RuntimeError):
    pass


class AssetGenerator:
    """Cross-platform text-to-3D pipeline selected by a named preset."""

    def __init__(
        self,
        settings: Settings,
        image_backend: ImageBackend | None = None,
        asset_backend: AssetBackend | None = None,
        preset: PipelinePreset | None = None,
    ) -> None:
        self.settings = settings
        self.preset = preset or settings.preset
        self.output_dir = settings.asset_output_dir
        if image_backend is None or asset_backend is None:
            default_image, default_asset = create_backends(settings, self.preset)
            image_backend = image_backend or default_image
            asset_backend = asset_backend or default_asset
        self.image_backend = image_backend
        self.asset_backend = asset_backend
        self.ready = False
        self.load_error: str | None = None
        self._rembg = None
        self._rembg_session = None
        self._lock = threading.Lock()

    @property
    def busy(self) -> bool:
        return self._lock.locked()

    @property
    def text_device(self) -> str:
        return self.image_backend.device

    @property
    def model_device(self) -> str:
        return self.asset_backend.device

    @property
    def output_mode(self) -> str:
        return self.asset_backend.output_mode

    def load(self) -> None:
        self.output_dir.mkdir(parents=True, exist_ok=True)
        started = time.perf_counter()
        try:
            self._check_common_model_files()
            self.image_backend.load()
            self._load_background_removal()
            self.asset_backend.load()
            self.image_backend.move_to_cpu()
            self.image_backend.release_device_memory()
            self.asset_backend.move_to_cpu()
            self.asset_backend.release_device_memory()
        except Exception as exc:
            self.load_error = str(exc)
            self.ready = False
            LOGGER.exception("The asset generator did not load.")
            return

        self.ready = True
        self.load_error = None
        LOGGER.info(
            "Loaded profile %s in %.2f seconds (image=%s, asset=%s, output=%s)",
            self.preset.name,
            time.perf_counter() - started,
            self.text_device,
            self.model_device,
            self.output_mode,
        )

    def generate(self, prompt: str) -> tuple[str, bool, dict[str, int]]:
        if not self.ready:
            raise RuntimeError("The asset generator is not ready.")

        prompt = " ".join(prompt.split())
        asset_id = self._asset_id(prompt)
        asset_path = self.output_dir / f"{asset_id}.glb"
        image_path = self.output_dir / f"{asset_id}.png"
        metadata_path = self.output_dir / f"{asset_id}.json"
        if asset_path.is_file() and metadata_path.is_file():
            return asset_id, True, self._cached_timings()

        if not self._lock.acquire(blocking=False):
            if asset_path.is_file() and metadata_path.is_file():
                return asset_id, True, self._cached_timings()
            raise BusyError

        started = time.perf_counter()
        try:
            if asset_path.is_file() and metadata_path.is_file():
                return asset_id, True, self._cached_timings()

            seed = int(asset_id[:16], 16) % (2**31)
            timings: dict[str, int] = {}
            if self.preset.platform != "macos" and not getattr(
                self.image_backend, "isolated_process", False
            ):
                import torch

                torch.cuda.reset_peak_memory_stats(0)

            stage = time.perf_counter()
            image = self.image_backend.generate(prompt + PROMPT_SUFFIX, seed)
            timings["text_to_image_ms"] = self._elapsed_ms(stage)
            image_peak_memory = self._image_peak_memory()

            stage = time.perf_counter()
            self.image_backend.move_to_cpu()
            self.image_backend.release_device_memory()
            timings["image_model_release_ms"] = self._elapsed_ms(stage)

            stage = time.perf_counter()
            image = self._remove_background(image)
            timings["preprocess_ms"] = self._elapsed_ms(stage)

            temporary_image_path = image_path.with_suffix(".png.tmp")
            image.save(temporary_image_path, format="PNG")
            os.replace(temporary_image_path, image_path)

            with tempfile.TemporaryDirectory(
                dir=self.output_dir.parent, prefix="asset-generator-"
            ) as temporary_dir:
                temporary_glb = Path(temporary_dir) / "asset.glb"
                stage = time.perf_counter()
                self.asset_backend.generate(image, seed, temporary_glb)
                timings["reconstruction_ms"] = self._elapsed_ms(stage)

                stage = time.perf_counter()
                self._validate_glb(temporary_glb)
                os.replace(temporary_glb, asset_path)
                timings["export_ms"] = self._elapsed_ms(stage)

            total_ms = self._elapsed_ms(started)
            timings["total_ms"] = total_ms
            self._write_metadata(
                metadata_path,
                {
                    "asset_id": asset_id,
                    "prompt_hash": hashlib.sha256(prompt.encode()).hexdigest(),
                    "seed": seed,
                    "configured_profile": self.settings.pipeline_profile,
                    "pipeline_profile": self.preset.name,
                    "pipeline_version": self._version,
                    "models": {
                        "image": {
                            "id": self.preset.image.model_id,
                            "revision": self.preset.image.revision,
                        },
                        "asset": {
                            "id": self.preset.asset.model_id,
                            "revision": self.preset.asset.revision,
                        },
                    },
                    "device": self.preset.device,
                    "output_mode": self.output_mode,
                    "texture_resolution": self.preset.asset.texture_resolution,
                    "created_at": datetime.now(UTC).isoformat(),
                    "timings": timings,
                    "memory": self._memory_metadata(image_peak_memory),
                },
            )
            return asset_id, False, timings
        except BusyError:
            raise
        except Exception as exc:
            LOGGER.exception("Asset generation failed for asset %s", asset_id)
            asset_path.unlink(missing_ok=True)
            image_path.unlink(missing_ok=True)
            image_path.with_suffix(".png.tmp").unlink(missing_ok=True)
            metadata_path.unlink(missing_ok=True)
            metadata_path.with_suffix(".json.tmp").unlink(missing_ok=True)
            raise GenerationError from exc
        finally:
            self.asset_backend.move_to_cpu()
            self.asset_backend.release_device_memory()
            self._lock.release()

    @staticmethod
    def _cached_timings() -> dict[str, int]:
        return {
            "text_to_image_ms": 0,
            "reconstruction_ms": 0,
            "total_ms": 0,
        }

    @property
    def _version(self) -> str:
        identity = {
            "schema": self.preset.schema_version,
            "profile": self.preset.name,
            "image": self.preset.image.__dict__,
            "asset": self.preset.asset.__dict__,
            "implementation": "cross-platform-v1",
        }
        digest = hashlib.sha256(
            json.dumps(identity, sort_keys=True).encode()
        ).hexdigest()[:16]
        return f"{self.preset.name}-{digest}"

    def _check_common_model_files(self) -> None:
        rembg = self.settings.model_cache_dir / "models" / "rembg" / "u2netp.onnx"
        if not self.settings.image_model_path.is_dir() or not rembg.is_file():
            raise FileNotFoundError(
                "Models are missing. Run `uv run python scripts/download_models.py "
                f"--profile {self.preset.name} --accept-licenses`."
            )

    def _load_background_removal(self) -> None:
        import rembg

        rembg_dir = self.settings.model_cache_dir / "models" / "rembg"
        os.environ["U2NET_HOME"] = str(rembg_dir.resolve())
        self._rembg = rembg
        self._rembg_session = rembg.new_session("u2netp")

    def _remove_background(self, image: Image.Image) -> Image.Image:
        rgba = self._rembg.remove(image, session=self._rembg_session).convert("RGBA")
        alpha = np.asarray(rgba.getchannel("A"))
        points = np.argwhere(alpha > 8)
        if points.size == 0:
            raise RuntimeError("The generated image does not contain a visible object.")

        y0, x0 = points.min(axis=0)
        y1, x1 = points.max(axis=0) + 1
        cropped = rgba.crop((int(x0), int(y0), int(x1), int(y1)))
        canvas_size = self.preset.image.width
        occupancy = (
            self.preset.asset.foreground_ratio
            if self.preset.asset.backend == "stable-fast-3d"
            else 435 / 512
        )
        target = round(canvas_size * occupancy)
        scale = min(target / cropped.width, target / cropped.height)
        size = (round(cropped.width * scale), round(cropped.height * scale))
        cropped = cropped.resize(size, Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (canvas_size, canvas_size), (255, 255, 255, 0))
        canvas.alpha_composite(
            cropped, ((canvas_size - size[0]) // 2, (canvas_size - size[1]) // 2)
        )
        return canvas

    def _validate_glb(self, path: Path) -> None:
        if not path.is_file() or path.read_bytes()[:4] != b"glTF":
            raise RuntimeError("The asset backend created an invalid GLB file.")
        loaded = trimesh.load(path, force="scene", process=False)
        geometries = list(loaded.geometry.values())
        if not geometries:
            raise RuntimeError("The GLB does not contain geometry.")
        for mesh in geometries:
            if not isinstance(mesh, trimesh.Trimesh):
                continue
            if len(mesh.vertices) == 0 or len(mesh.faces) == 0:
                raise RuntimeError("The GLB contains an empty mesh.")
            if not np.isfinite(mesh.vertices).all():
                raise RuntimeError("The GLB contains non-finite vertices.")
            if not np.isfinite(mesh.vertex_normals).all():
                raise RuntimeError("The GLB contains non-finite normals.")
        if self.output_mode == "pbr_texture":
            textured = [mesh for mesh in geometries if mesh.visual.kind == "texture"]
            if not textured:
                raise RuntimeError("The CUDA GLB does not contain texture coordinates.")
            if not any(_has_base_color_texture(mesh) for mesh in textured):
                raise RuntimeError(
                    "The CUDA GLB does not contain a base-color texture."
                )
            required = self.preset.asset.texture_resolution
            if required and (required, required) not in {
                size for mesh in textured for size in _texture_sizes(mesh)
            }:
                raise RuntimeError(
                    f"The CUDA GLB does not contain its required {required}px texture."
                )

    def _asset_id(self, prompt: str) -> str:
        return hashlib.sha256(f"{self._version}\0{prompt}".encode()).hexdigest()[:32]

    def _image_peak_memory(self) -> int | None:
        if self.preset.platform != "macos":
            return None
        try:
            import torch

            return torch.mps.driver_allocated_memory()
        except (AttributeError, ImportError, RuntimeError):
            return None

    def _memory_metadata(self, image_peak_memory: int | None) -> dict[str, int]:
        if self.preset.platform == "macos":
            return (
                {"peak_mps_driver_allocated_bytes": image_peak_memory}
                if image_peak_memory is not None
                else {}
            )
        if getattr(self.image_backend, "isolated_process", False):
            return {}
        try:
            import torch

            return {
                "peak_torch_allocated_bytes": torch.cuda.max_memory_allocated(0),
                "peak_torch_reserved_bytes": torch.cuda.max_memory_reserved(0),
            }
        except (ImportError, RuntimeError):
            return {}

    @staticmethod
    def _elapsed_ms(started: float) -> int:
        return max(0, round((time.perf_counter() - started) * 1000))

    @staticmethod
    def _write_metadata(path: Path, metadata: dict) -> None:
        temporary = path.with_suffix(".json.tmp")
        temporary.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        os.replace(temporary, path)


def _has_base_color_texture(mesh: trimesh.Trimesh) -> bool:
    material = getattr(mesh.visual, "material", None)
    if material is None:
        return False
    image = getattr(material, "baseColorTexture", None) or getattr(
        material, "image", None
    )
    return image is not None and getattr(image, "size", (0, 0)) != (0, 0)


def _texture_sizes(mesh: trimesh.Trimesh) -> set[tuple[int, int]]:
    material = getattr(mesh.visual, "material", None)
    if material is None:
        return set()
    images = [
        getattr(material, "baseColorTexture", None),
        getattr(material, "image", None),
        getattr(material, "normalTexture", None),
    ]
    return {
        image.size
        for image in images
        if image is not None and getattr(image, "size", None) is not None
    }
