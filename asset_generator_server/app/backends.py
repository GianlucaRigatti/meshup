from __future__ import annotations

import gc
import os
import subprocess
import sys
from pathlib import Path
from typing import Protocol

import numpy as np
import trimesh
from PIL import Image

from app.config import Settings
from app.presets import DINOV2_LARGE_REVISION, PipelinePreset


class ImageBackend(Protocol):
    device: str

    def load(self) -> None: ...

    def generate(self, prompt: str, seed: int) -> Image.Image: ...

    def move_to_cpu(self) -> None: ...

    def release_device_memory(self) -> None: ...


class AssetBackend(Protocol):
    device: str
    output_mode: str

    def load(self) -> None: ...

    def generate(self, image: Image.Image, seed: int, output: Path) -> None: ...

    def move_to_cpu(self) -> None: ...

    def release_device_memory(self) -> None: ...


class DiffusersImageBackend:
    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "mps" if preset.platform == "macos" else "cuda:0"
        self._pipeline = None
        self._torch = None
        self._uses_cpu_offload = False

    def load(self) -> None:
        if self._pipeline is not None:
            return
        if not self.settings.image_model_path.is_dir():
            raise FileNotFoundError(
                f"Image model is missing at {self.settings.image_model_path}."
            )
        _require_revision(self.settings.image_model_path, self.preset.image.revision)

        import torch
        from diffusers import AutoPipelineForText2Image, SanaSprintPipeline

        if self.preset.platform == "macos":
            if not torch.backends.mps.is_available():
                raise RuntimeError(
                    "The macos-mlx profile requires an available MPS device."
                )
        else:
            _validate_cuda(torch)

        self._torch = torch
        pipeline_class = (
            SanaSprintPipeline
            if self.preset.image.backend == "sana-sprint"
            else AutoPipelineForText2Image
        )
        dtype = getattr(torch, self.preset.image.dtype, None)
        if dtype is None:
            raise RuntimeError(
                f"Unsupported image model dtype {self.preset.image.dtype!r}."
            )
        load_options = {
            "torch_dtype": dtype,
            "local_files_only": True,
        }
        if self.preset.image.backend == "auto":
            load_options.update(
                safety_checker=None,
                requires_safety_checker=False,
            )
        if self.preset.image.variant is not None:
            load_options["variant"] = self.preset.image.variant
        self._pipeline = pipeline_class.from_pretrained(
            self.settings.image_model_path,
            **load_options,
        )
        self._pipeline.set_progress_bar_config(disable=True)
        if self.preset.platform != "macos":
            self._pipeline.enable_model_cpu_offload(device="cuda")
            self._uses_cpu_offload = True
        else:
            self._pipeline.to(self.device)

    def generate(self, prompt: str, seed: int) -> Image.Image:
        self.load()
        if self.preset.platform == "macos":
            self._pipeline.to(self.device)
        generator = self._torch.Generator(device="cpu").manual_seed(seed)
        result = self._pipeline(
            prompt,
            num_inference_steps=self.preset.image.steps,
            guidance_scale=self.preset.image.guidance,
            height=self.preset.image.height,
            width=self.preset.image.width,
            generator=generator,
        )
        return result.images[0].convert("RGB")

    def move_to_cpu(self) -> None:
        if self._pipeline is None:
            return
        if self.preset.platform == "macos":
            # Releasing the pipeline, instead of retaining a CPU copy, preserves
            # the existing 16 GB Apple Silicon memory lifecycle.
            self._pipeline = None
            self._uses_cpu_offload = False
        elif self._uses_cpu_offload:
            free_hooks = getattr(self._pipeline, "maybe_free_model_hooks", None)
            if free_hooks:
                free_hooks()
        else:
            self._pipeline.to("cpu")

    def release_device_memory(self) -> None:
        gc.collect()
        if self._torch is None:
            return
        if self.preset.platform == "macos" and self._torch.backends.mps.is_available():
            self._torch.mps.empty_cache()
        elif self.preset.platform != "macos" and self._torch.cuda.is_available():
            self._torch.cuda.empty_cache()


class HunyuanMlxBackend:
    output_mode = "vertex_color"

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "mlx-metal"

    @property
    def executable(self) -> Path:
        return self.settings.hunyuan_runtime_path / "hy3d"

    def load(self) -> None:
        model = self.settings.asset_model_path
        _require_git_revision(
            self.settings.hunyuan_source_path, self.preset.asset.source_revision
        )
        metallib = (
            self.executable.parent
            / "mlx-swift_Cmlx.bundle"
            / "Contents"
            / "Resources"
            / "default.metallib"
        )
        required = [
            self.executable,
            model / "model.fp16.safetensors",
            model / "config.yaml",
            metallib,
        ]
        if not all(path.exists() for path in required):
            raise FileNotFoundError("The macos-mlx model or runtime is incomplete.")
        _require_revision(model, self.preset.asset.revision)
        completed = subprocess.run(
            [str(self.executable.resolve()), "--help"],
            env={**os.environ, "LLVM_PROFILE_FILE": os.devnull},
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=30,
        )
        if completed.returncode != 0:
            raise RuntimeError("The Hunyuan MLX executable failed its readiness check.")

    def generate(self, image: Image.Image, seed: int, output: Path) -> None:
        input_path = output.with_name("input.png")
        image.save(input_path)
        asset = self.preset.asset
        command = [
            str(self.executable.resolve()),
            "shape",
            str(input_path),
            "-o",
            str(output),
            "--weights",
            str(self.settings.asset_model_path.resolve()),
            "--steps",
            str(asset.steps),
            "--guidance",
            "5",
            "--octree",
            str(asset.octree_resolution),
            "--seed",
            str(seed),
        ]
        if asset.quantization:
            command += ["--quantize", str(asset.quantization)]
        completed = subprocess.run(
            command,
            env={**os.environ, "LLVM_PROFILE_FILE": os.devnull},
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=self.settings.hunyuan_timeout_seconds,
        )
        if completed.returncode != 0 or not output.is_file():
            raise RuntimeError("Hunyuan shape generation failed.")
        _add_vertex_colors(output, image)

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        return


class StableFast3DBackend:
    output_mode = "pbr_texture"

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "cuda:0"
        self._model = None
        self._torch = None

    def load(self) -> None:
        if self._model is not None:
            return
        if not self.settings.asset_model_path.is_dir():
            raise FileNotFoundError(
                f"Stable Fast 3D weights are missing at {self.settings.asset_model_path}."
            )
        if not self.settings.sf3d_source_path.is_dir():
            raise FileNotFoundError(
                f"Stable Fast 3D source is missing at {self.settings.sf3d_source_path}."
            )
        _require_git_revision(
            self.settings.sf3d_source_path, self.preset.asset.source_revision
        )
        _require_revision(self.settings.asset_model_path, self.preset.asset.revision)
        _require_revision(self.settings.dinov2_model_path, DINOV2_LARGE_REVISION)

        import torch

        _validate_cuda(torch)
        source = str(self.settings.sf3d_source_path.resolve())
        if source not in sys.path:
            sys.path.insert(0, source)
        try:
            import texture_baker  # noqa: F401
            import uv_unwrapper  # noqa: F401
            from sf3d.system import SF3D
        except ImportError as exc:
            raise RuntimeError(
                "Stable Fast 3D native extensions are not installed."
            ) from exc

        self._torch = torch
        self._model = SF3D.from_pretrained(
            str(self.settings.asset_model_path.resolve()),
            config_name="config.yaml",
            weight_name="model.safetensors",
        )
        self._model.eval().to("cpu")

    def generate(self, image: Image.Image, seed: int, output: Path) -> None:
        self.load()
        free_bytes, _ = self._torch.cuda.mem_get_info(0)
        if free_bytes < 7 * 1024**3:
            raise RuntimeError(
                "Stable Fast 3D requires at least 7 GiB of free CUDA memory."
            )
        self._torch.manual_seed(seed)
        self._torch.cuda.manual_seed_all(seed)
        self._model.to(self.device)
        try:
            with (
                self._torch.inference_mode(),
                self._torch.autocast(device_type="cuda", dtype=self._torch.bfloat16),
            ):
                mesh, _ = self._model.run_image(
                    [image.convert("RGBA")],
                    bake_resolution=self.preset.asset.texture_resolution,
                    remesh=self.preset.asset.remesh,
                    vertex_count=-1,
                )
            if isinstance(mesh, list):
                if len(mesh) != 1:
                    raise RuntimeError(
                        "Stable Fast 3D returned an unexpected mesh batch."
                    )
                mesh = mesh[0]
            mesh.export(output, file_type="glb", include_normals=True)
        finally:
            self.move_to_cpu()
            self.release_device_memory()

    def move_to_cpu(self) -> None:
        if self._model is not None:
            self._model.to("cpu")

    def release_device_memory(self) -> None:
        gc.collect()
        if self._torch is not None and self._torch.cuda.is_available():
            self._torch.cuda.empty_cache()


def create_backends(
    settings: Settings, preset: PipelinePreset
) -> tuple[ImageBackend, AssetBackend]:
    image = DiffusersImageBackend(settings, preset)
    if preset.asset.backend == "hunyuan-mlx":
        return image, HunyuanMlxBackend(settings, preset)
    return image, StableFast3DBackend(settings, preset)


def _require_revision(model_path: Path, expected: str) -> None:
    revision_file = model_path / ".model-revision"
    installed = (
        revision_file.read_text(encoding="utf-8").strip()
        if revision_file.is_file()
        else None
    )
    if installed != expected:
        raise RuntimeError(
            f"Model revision mismatch at {model_path}; rerun the model installer."
        )


def _require_git_revision(source_path: Path, expected: str) -> None:
    try:
        installed = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=source_path,
            check=True,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            timeout=30,
        ).stdout.strip()
    except (OSError, subprocess.SubprocessError) as exc:
        raise RuntimeError(
            f"Cannot verify the source revision at {source_path}; rerun the installer."
        ) from exc
    if installed != expected:
        raise RuntimeError(
            f"Source revision mismatch at {source_path}; rerun the model installer."
        )


def _validate_cuda(torch_module) -> None:
    if not torch_module.cuda.is_available():
        raise RuntimeError("The selected CUDA profile requires CUDA.")
    if torch_module.cuda.device_count() != 1:
        raise RuntimeError("The selected CUDA profile requires exactly one GPU.")
    properties = torch_module.cuda.get_device_properties(0)
    if properties.total_memory < 10 * 1024**3:
        raise RuntimeError("The selected CUDA profile requires at least 10 GiB VRAM.")
    if properties.major < 8:
        raise RuntimeError("The selected CUDA profile requires an Ampere or newer GPU.")
    if not torch_module.cuda.is_bf16_supported():
        raise RuntimeError("The selected CUDA profile requires CUDA BF16 support.")
    try:
        torch_module.zeros(1, device="cuda")
    except Exception as exc:
        raise RuntimeError(
            "The installed PyTorch CUDA build does not support this GPU."
        ) from exc


def _add_vertex_colors(path: Path, image: Image.Image) -> None:
    mesh = trimesh.load(path, force="mesh", process=False)
    if not isinstance(mesh, trimesh.Trimesh) or len(mesh.vertices) == 0:
        raise RuntimeError("Hunyuan returned a GLB without a mesh.")

    pixels = np.asarray(image.convert("RGBA"))
    opaque = pixels[..., 3] > 8
    base_color = (
        np.median(pixels[..., :3][opaque], axis=0)
        if opaque.any()
        else np.array([180.0, 180.0, 180.0])
    )
    vertices = np.asarray(mesh.vertices)
    low = vertices.min(axis=0)
    extent = np.maximum(vertices.max(axis=0) - low, 1e-6)
    x = np.rint((vertices[:, 0] - low[0]) / extent[0] * (pixels.shape[1] - 1)).astype(
        int
    )
    y = np.rint(
        (1 - (vertices[:, 1] - low[1]) / extent[1]) * (pixels.shape[0] - 1)
    ).astype(int)
    sampled = pixels[y, x, :3].astype(float)
    alpha = pixels[y, x, 3:4].astype(float) / 255
    strength = np.abs(np.asarray(mesh.vertex_normals)[:, 2:3]) ** 2 * alpha
    colors = sampled * strength + base_color * (1 - strength)
    mesh.visual.vertex_colors = np.column_stack(
        [colors.astype(np.uint8), np.full(len(colors), 255, dtype=np.uint8)]
    )
    mesh.export(path, file_type="glb")
