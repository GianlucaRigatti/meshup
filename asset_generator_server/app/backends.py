from __future__ import annotations

import gc
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Protocol

import numpy as np
import trimesh
from PIL import Image

from app.config import Settings
from app.presets import (
    DINOV2_LARGE_REVISION,
    Z_IMAGE_TEXT_ENCODER_REVISION,
    Z_IMAGE_VAE_REVISION,
    PipelinePreset,
)


PIXAL3D_RUNNER = Path(__file__).resolve().parents[1] / "scripts" / "run_pixal3d.py"
INSTANTMESH_RUNNER = (
    Path(__file__).resolve().parents[1] / "scripts" / "run_instantmesh.py"
)
IMAGE_RUNNER = Path(__file__).resolve().parents[1] / "scripts" / "run_image_model.py"


def _trim_process_heap() -> None:
    """Return freed CPU pages to Linux before starting a memory-heavy subprocess."""
    if not sys.platform.startswith("linux"):
        return
    try:
        import ctypes

        libc = ctypes.CDLL(None)
        malloc_trim = getattr(libc, "malloc_trim", None)
        if malloc_trim is not None:
            malloc_trim.argtypes = [ctypes.c_size_t]
            malloc_trim.restype = ctypes.c_int
            malloc_trim(0)
    except (AttributeError, OSError):
        # malloc_trim is a glibc optimization, not a correctness requirement.
        return


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
        if self.preset.image.backend == "stable-diffusion-3.5":
            self._load_quantized_sd35(torch, dtype)
            return

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

    def _load_quantized_sd35(self, torch_module, dtype) -> None:
        if self.preset.image.quantization != "nf4":
            raise RuntimeError("The WSL SD 3.5 preset requires NF4 quantization.")
        if not _is_wsl():
            raise RuntimeError("The quantized SD 3.5 profile currently requires WSL 2.")

        from diffusers import (
            BitsAndBytesConfig as DiffusersBitsAndBytesConfig,
            SD3Transformer2DModel,
            StableDiffusion3Pipeline,
        )
        from transformers import (
            BitsAndBytesConfig as TransformersBitsAndBytesConfig,
            T5EncoderModel,
        )

        transformer_quantization = DiffusersBitsAndBytesConfig(
            load_in_4bit=True,
            bnb_4bit_quant_type="nf4",
            bnb_4bit_compute_dtype=dtype,
            bnb_4bit_use_double_quant=True,
        )
        text_quantization = TransformersBitsAndBytesConfig(
            load_in_4bit=True,
            bnb_4bit_quant_type="nf4",
            bnb_4bit_compute_dtype=dtype,
            bnb_4bit_use_double_quant=True,
        )
        model_path = self.settings.image_model_path
        transformer = SD3Transformer2DModel.from_pretrained(
            model_path,
            subfolder="transformer",
            quantization_config=transformer_quantization,
            torch_dtype=dtype,
            local_files_only=True,
        )
        text_encoder_3 = T5EncoderModel.from_pretrained(
            model_path,
            subfolder="text_encoder_3",
            quantization_config=text_quantization,
            dtype=dtype,
            local_files_only=True,
        )
        self._pipeline = StableDiffusion3Pipeline.from_pretrained(
            model_path,
            transformer=transformer,
            text_encoder_3=text_encoder_3,
            torch_dtype=dtype,
            local_files_only=True,
            device_map="balanced",
            max_memory={0: "9GiB", "cpu": "24GiB"},
        )
        self._pipeline.vae.enable_tiling()
        self._pipeline.set_progress_bar_config(disable=True)
        self._uses_cpu_offload = False

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
        elif self.preset.asset.backend in {"pixal3d", "instantmesh"}:
            # These subprocess backends keep large model collections in system
            # memory. Drop the image pipeline completely before starting them.
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
            _trim_process_heap()
            return
        if self.preset.platform == "macos" and self._torch.backends.mps.is_available():
            self._torch.mps.empty_cache()
        elif self.preset.platform != "macos" and self._torch.cuda.is_available():
            self._torch.cuda.empty_cache()
        _trim_process_heap()


class IsolatedDiffusersImageBackend:
    """Run a large image model in a disposable process to reclaim all memory."""

    isolated_process = True

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "cuda:0"

    def load(self) -> None:
        if not _is_wsl():
            raise RuntimeError("The isolated SD 3.5 profile currently requires WSL 2.")
        if not self.settings.image_model_path.is_dir():
            raise FileNotFoundError(
                f"Image model is missing at {self.settings.image_model_path}."
            )
        _require_revision(self.settings.image_model_path, self.preset.image.revision)
        if not IMAGE_RUNNER.is_file():
            raise FileNotFoundError(f"Image runner is missing at {IMAGE_RUNNER}.")

    def generate(self, prompt: str, seed: int) -> Image.Image:
        self.load()
        with tempfile.TemporaryDirectory(prefix="asset-image-") as temporary_dir:
            output = Path(temporary_dir) / "image.png"
            command = [
                sys.executable,
                str(IMAGE_RUNNER),
                "--model-cache",
                str(self.settings.model_cache_dir.resolve()),
                "--output",
                str(output),
            ]
            if self.settings.pipeline_profile is not None:
                command += ["--profile", self.settings.pipeline_profile]
            else:
                command += [
                    "--image-generator",
                    self.settings.image_generator,
                    "--model-3d",
                    self.settings.model_3d,
                ]
            try:
                completed = subprocess.run(
                    command,
                    cwd=IMAGE_RUNNER.parent.parent,
                    env={
                        **os.environ,
                        "HF_HUB_OFFLINE": "1",
                        "PYTORCH_CUDA_ALLOC_CONF": "expandable_segments:True",
                    },
                    input=json.dumps({"prompt": prompt, "seed": seed}),
                    check=False,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    timeout=self.settings.image_timeout_seconds,
                )
            except subprocess.TimeoutExpired as exc:
                raise RuntimeError(
                    "Isolated SD 3.5 generation timed out after "
                    f"{self.settings.image_timeout_seconds} seconds."
                ) from exc
            if completed.returncode != 0 or not output.is_file():
                tail = completed.stdout[-4000:].strip()
                message = "Isolated SD 3.5 generation failed."
                if tail:
                    message += f"\n{tail}"
                raise RuntimeError(message)
            with Image.open(output) as generated:
                return generated.convert("RGB")

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        _trim_process_heap()


class ZImageCppBackend:
    """Generate with Z-Image in a disposable native CUDA process."""

    isolated_process = True

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "cuda:0"

    def load(self) -> None:
        if not _is_wsl():
            raise RuntimeError("The Z-Image GGUF profiles currently require WSL 2.")
        missing = [
            path.resolve()
            for path in self.settings.z_image_required_files
            if not path.is_file()
        ]
        if missing:
            raise FileNotFoundError(
                "The Z-Image runtime is incomplete. Missing files:\n- "
                + "\n- ".join(str(path) for path in missing)
                + "\nRerun the model installer for this profile."
            )
        _require_git_revision(
            self.settings.stable_diffusion_cpp_source_path,
            self.preset.image.runtime_revision,
        )
        _require_revision(
            self.settings.image_model_path,
            self.preset.image.revision,
        )
        _require_revision(
            self.settings.z_image_components_path,
            Z_IMAGE_TEXT_ENCODER_REVISION,
            marker=".text-encoder-revision",
        )
        _require_revision(
            self.settings.z_image_components_path,
            Z_IMAGE_VAE_REVISION,
            marker=".vae-revision",
        )
        completed = subprocess.run(
            [str(self.settings.stable_diffusion_cpp_executable_path.resolve()), "--help"],
            env=self._environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=60,
        )
        if completed.returncode != 0:
            tail = completed.stdout[-4000:].strip()
            message = "The stable-diffusion.cpp runtime failed its readiness check."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    def generate(self, prompt: str, seed: int) -> Image.Image:
        self.load()
        with tempfile.TemporaryDirectory(prefix="z-image-") as temporary_dir:
            temporary = Path(temporary_dir)
            prompt_path = temporary / "prompt.txt"
            output = temporary / "image.png"
            prompt_path.write_text(prompt, encoding="utf-8")
            image = self.preset.image
            command = [
                str(self.settings.stable_diffusion_cpp_executable_path.resolve()),
                "--diffusion-model",
                str(self.settings.z_image_diffusion_path.resolve()),
                "--llm",
                str(self.settings.z_image_text_encoder_path.resolve()),
                "--vae",
                str(self.settings.z_image_vae_path.resolve()),
                "--prompt-file",
                str(prompt_path),
                "--output",
                str(output),
                "--steps",
                str(image.steps),
                "--cfg-scale",
                str(image.guidance),
                "--width",
                str(image.width),
                "--height",
                str(image.height),
                "--seed",
                str(seed),
                "--rng",
                "cpu",
                "--diffusion-fa",
                "--vae-tiling",
                "--vae-conv-direct",
                "--auto-fit",
                "--max-vram",
                "cuda0=10.5",
            ]
            try:
                completed = subprocess.run(
                    command,
                    env=self._environment,
                    check=False,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    timeout=self.settings.image_timeout_seconds,
                )
            except subprocess.TimeoutExpired as exc:
                raise RuntimeError(
                    "Z-Image generation timed out after "
                    f"{self.settings.image_timeout_seconds} seconds."
                ) from exc
            if completed.returncode != 0 or not output.is_file():
                tail = completed.stdout[-4000:].strip()
                message = "Z-Image generation failed."
                if tail:
                    message += f"\n{tail}"
                raise RuntimeError(message)
            with Image.open(output) as generated:
                return generated.convert("RGB")

    @property
    def _environment(self) -> dict[str, str]:
        binary_dir = self.settings.stable_diffusion_cpp_executable_path.parent.resolve()
        existing = os.environ.get("LD_LIBRARY_PATH")
        library_path = (
            str(binary_dir) if not existing else f"{binary_dir}:{existing}"
        )
        return {**os.environ, "LD_LIBRARY_PATH": library_path}

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        _trim_process_heap()


class Flux2KleinCppBackend:
    """Generate with FLUX.2 Klein in a disposable native CUDA process."""

    isolated_process = True

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "cuda:0"

    def load(self) -> None:
        if not _is_wsl():
            raise RuntimeError("The FLUX.2 Klein models currently require WSL 2.")
        missing = [
            path.resolve()
            for path in self.settings.flux2_klein_required_files
            if not path.is_file()
        ]
        if missing:
            raise FileNotFoundError(
                "The FLUX.2 Klein runtime is incomplete. Missing files:\n- "
                + "\n- ".join(str(path) for path in missing)
                + "\nRerun the model installer for this selection."
            )
        image = self.preset.image
        _require_git_revision(
            self.settings.stable_diffusion_cpp_source_path,
            image.runtime_revision,
        )
        _require_revision(self.settings.image_model_path, image.revision)
        _require_revision(
            self.settings.image_model_path,
            image.text_encoder_revision,
            marker=".text-encoder-revision",
        )
        _require_revision(
            self.settings.image_model_path,
            image.vae_revision,
            marker=".vae-revision",
        )
        completed = subprocess.run(
            [str(self.settings.stable_diffusion_cpp_executable_path.resolve()), "--help"],
            env=self._environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=60,
        )
        if completed.returncode != 0:
            tail = completed.stdout[-4000:].strip()
            message = "The stable-diffusion.cpp runtime failed its readiness check."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    def generate(self, prompt: str, seed: int) -> Image.Image:
        self.load()
        with tempfile.TemporaryDirectory(prefix="flux2-klein-") as temporary_dir:
            temporary = Path(temporary_dir)
            prompt_path = temporary / "prompt.txt"
            output = temporary / "image.png"
            prompt_path.write_text(prompt, encoding="utf-8")
            image = self.preset.image
            command = [
                str(self.settings.stable_diffusion_cpp_executable_path.resolve()),
                "--diffusion-model",
                str(self.settings.flux2_klein_diffusion_path.resolve()),
                "--llm",
                str(self.settings.flux2_klein_text_encoder_path.resolve()),
                "--vae",
                str(self.settings.flux2_klein_vae_path.resolve()),
                "--prompt-file",
                str(prompt_path),
                "--output",
                str(output),
                "--steps",
                str(image.steps),
                "--cfg-scale",
                str(image.guidance),
                "--sampling-method",
                "euler",
                "--width",
                str(image.width),
                "--height",
                str(image.height),
                "--seed",
                str(seed),
                "--rng",
                "cpu",
                "--diffusion-fa",
                "--offload-to-cpu",
                "--vae-tiling",
                "--auto-fit",
                "--max-vram",
                "cuda0=10.5",
            ]
            try:
                completed = subprocess.run(
                    command,
                    env=self._environment,
                    check=False,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    timeout=self.settings.image_timeout_seconds,
                )
            except subprocess.TimeoutExpired as exc:
                raise RuntimeError(
                    "FLUX.2 Klein generation timed out after "
                    f"{self.settings.image_timeout_seconds} seconds."
                ) from exc
            if completed.returncode != 0 or not output.is_file():
                tail = completed.stdout[-4000:].strip()
                message = "FLUX.2 Klein generation failed."
                if tail:
                    message += f"\n{tail}"
                raise RuntimeError(message)
            with Image.open(output) as generated:
                return generated.convert("RGB")

    @property
    def _environment(self) -> dict[str, str]:
        binary_dir = self.settings.stable_diffusion_cpp_executable_path.parent.resolve()
        existing = os.environ.get("LD_LIBRARY_PATH")
        library_path = str(binary_dir) if not existing else f"{binary_dir}:{existing}"
        return {**os.environ, "LD_LIBRARY_PATH": library_path}

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        _trim_process_heap()


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


class Pixal3DBackend:
    output_mode = "pbr_texture"

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "cuda:0"

    def load(self) -> None:
        if not _is_wsl():
            raise RuntimeError("The Pixal3D profile currently requires WSL 2.")
        missing = [
            path.resolve()
            for path in self.settings.pixal3d_required_files
            if not path.is_file()
        ]
        if missing:
            raise FileNotFoundError(
                "The Pixal3D runtime is incomplete. Missing files:\n- "
                + "\n- ".join(str(path) for path in missing)
                + "\nRerun the model installer with --force to rebuild source and "
                "runtime artifacts. Existing model weights will be reused."
            )
        _require_git_revision(
            self.settings.pixal3d_source_path,
            self.preset.asset.source_revision,
        )
        _require_revision(
            self.settings.asset_model_path,
            self.preset.asset.revision,
        )
        completed = subprocess.run(
            [
                str(self.settings.pixal3d_python_path.absolute()),
                str(PIXAL3D_RUNNER),
                "--help",
            ],
            cwd=self.settings.pixal3d_source_path,
            env=self._environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=60,
        )
        if completed.returncode != 0:
            tail = completed.stdout[-4000:].strip()
            message = "The Pixal3D runtime failed its readiness check."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    def generate(self, image: Image.Image, seed: int, output: Path) -> None:
        self.load()
        input_path = output.with_name("input.png")
        image.save(input_path)
        asset = self.preset.asset
        command = [
            str(self.settings.pixal3d_python_path.absolute()),
            str(PIXAL3D_RUNNER),
            "--image",
            str(input_path.resolve()),
            "--output",
            str(output.resolve()),
            "--seed",
            str(seed),
            "--model_path",
            str(self.settings.asset_model_path.resolve()),
            "--low_vram",
            "--resolution",
            str(asset.pipeline_resolution),
        ]
        if asset.camera_fov is not None:
            command += ["--fov", str(asset.camera_fov)]
        completed = subprocess.run(
            command,
            cwd=self.settings.pixal3d_source_path,
            env=self._environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=self.settings.pixal3d_timeout_seconds,
        )
        if completed.returncode != 0 or not output.is_file():
            tail = completed.stdout[-4000:].strip()
            message = "Pixal3D generation failed."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    @property
    def _environment(self) -> dict[str, str]:
        runtime = self.settings.pixal3d_runtime_path.resolve()
        return {
            **os.environ,
            "ATTN_BACKEND": "sdpa",
            "HF_HOME": str(runtime / "huggingface"),
            "HF_HUB_OFFLINE": "1",
            "PYTORCH_CUDA_ALLOC_CONF": "expandable_segments:True",
            "TORCH_HOME": str(runtime / "torch"),
        }

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        return


class TrellisCppBackend:
    output_mode = "pbr_texture"

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "cuda:0"

    def load(self) -> None:
        if not _is_wsl():
            raise RuntimeError("The TRELLIS.2 GGUF profiles currently require WSL 2.")
        missing = [
            path.resolve()
            for path in self.settings.trellis_cpp_required_files
            if not path.is_file()
        ]
        if missing:
            raise FileNotFoundError(
                "The trellis.cpp runtime is incomplete. Missing files:\n- "
                + "\n- ".join(str(path) for path in missing)
                + "\nRerun the model installer for this profile."
            )
        _require_git_revision(
            self.settings.trellis_cpp_source_path,
            self.preset.asset.source_revision,
        )
        _require_revision(
            self.settings.asset_model_path,
            self.preset.asset.revision,
        )
        completed = subprocess.run(
            [str(self.settings.trellis_cpp_executable_path.resolve()), "--help"],
            env=self._environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=60,
        )
        if completed.returncode != 0:
            tail = completed.stdout[-4000:].strip()
            message = "The trellis.cpp runtime failed its readiness check."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    def generate(self, image: Image.Image, seed: int, output: Path) -> None:
        self.load()
        input_path = output.with_name("input.png")
        image.save(input_path, format="PNG")
        asset = self.preset.asset
        command = [
            str(self.settings.trellis_cpp_executable_path.resolve()),
            str(input_path.resolve()),
            str(output.resolve()),
            "--models",
            str(self.settings.trellis_cpp_model_path.resolve()),
            "--gpu",
            "0",
            "--seed",
            str(seed),
            "--res",
            str(asset.pipeline_resolution),
            "--max-tokens",
            str(asset.max_tokens),
            "--atlas",
            str(asset.texture_resolution),
            "--webp",
            "off",
            "--require-gpu",
        ]
        if asset.box_uv:
            command.append("--box-uv")
        try:
            completed = subprocess.run(
                command,
                env=self._environment,
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                timeout=self.settings.trellis_timeout_seconds,
            )
        except subprocess.TimeoutExpired as exc:
            raise RuntimeError(
                "TRELLIS.2 generation timed out after "
                f"{self.settings.trellis_timeout_seconds} seconds."
            ) from exc
        if completed.returncode != 0 or not output.is_file():
            tail = completed.stdout[-4000:].strip()
            message = "TRELLIS.2 generation failed."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    @property
    def _environment(self) -> dict[str, str]:
        build = self.settings.trellis_cpp_build_path.resolve()
        existing = os.environ.get("LD_LIBRARY_PATH")
        library_path = str(build) if not existing else f"{build}:{existing}"
        return {**os.environ, "LD_LIBRARY_PATH": library_path}

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        return


class InstantMeshBackend:
    """Run the low-VRAM InstantMesh experiment in a disposable process."""

    output_mode = "pbr_texture"

    def __init__(self, settings: Settings, preset: PipelinePreset) -> None:
        self.settings = settings
        self.preset = preset
        self.device = "cuda:0"

    def load(self) -> None:
        if not _is_wsl():
            raise RuntimeError("The InstantMesh profile currently requires WSL 2.")
        missing = [
            path.resolve()
            for path in self.settings.instantmesh_required_files
            if not path.is_file()
        ]
        if not INSTANTMESH_RUNNER.is_file():
            missing.append(INSTANTMESH_RUNNER.resolve())
        if missing:
            raise FileNotFoundError(
                "The InstantMesh runtime is incomplete. Missing files:\n- "
                + "\n- ".join(str(path) for path in missing)
                + "\nRerun the model installer for this profile."
            )
        _require_git_revision(
            self.settings.instantmesh_source_path,
            self.preset.asset.source_revision,
        )
        _require_revision(self.settings.asset_model_path, self.preset.asset.revision)
        completed = subprocess.run(
            [
                str(self.settings.instantmesh_python_path.absolute()),
                str(INSTANTMESH_RUNNER),
                "--check",
            ],
            cwd=self.settings.instantmesh_source_path,
            env=self._environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=60,
        )
        if completed.returncode != 0:
            tail = completed.stdout[-4000:].strip()
            message = "The InstantMesh runtime failed its readiness check."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    def generate(self, image: Image.Image, seed: int, output: Path) -> None:
        self.load()
        input_path = output.with_name("input.png")
        image.save(input_path, format="PNG")
        asset = self.preset.asset
        command = [
            str(self.settings.instantmesh_python_path.absolute()),
            str(INSTANTMESH_RUNNER),
            "--image",
            str(input_path.resolve()),
            "--output",
            str(output.resolve()),
            "--source",
            str(self.settings.instantmesh_source_path.resolve()),
            "--model-path",
            str(self.settings.asset_model_path.resolve()),
            "--zero123-model",
            str(self.settings.zero123_model_path.resolve()),
            "--zero123-pipeline",
            str(self.settings.zero123_pipeline_path.resolve()),
            "--dino-model",
            str(self.settings.instantmesh_dino_path.resolve()),
            "--seed",
            str(seed),
            "--diffusion-steps",
            str(asset.steps),
            "--views",
            str(asset.views),
            "--grid-resolution",
            str(asset.grid_resolution),
            "--texture-resolution",
            str(asset.texture_resolution),
        ]
        try:
            completed = subprocess.run(
                command,
                cwd=self.settings.instantmesh_source_path,
                env=self._environment,
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                timeout=self.settings.instantmesh_timeout_seconds,
            )
        except subprocess.TimeoutExpired as exc:
            raise RuntimeError(
                "InstantMesh generation timed out after "
                f"{self.settings.instantmesh_timeout_seconds} seconds."
            ) from exc
        if completed.returncode != 0 or not output.is_file():
            tail = completed.stdout[-4000:].strip()
            message = "InstantMesh generation failed."
            if tail:
                message += f"\n{tail}"
            raise RuntimeError(message)

    @property
    def _environment(self) -> dict[str, str]:
        runtime = self.settings.instantmesh_runtime_path.resolve()
        return {
            **os.environ,
            "HF_HOME": str(runtime / "huggingface"),
            "HF_HUB_OFFLINE": "1",
            "PYTORCH_CUDA_ALLOC_CONF": "expandable_segments:True",
            "TOKENIZERS_PARALLELISM": "false",
        }

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        return


def create_backends(
    settings: Settings, preset: PipelinePreset
) -> tuple[ImageBackend, AssetBackend]:
    image: ImageBackend
    if preset.image.backend == "flux2-klein-cpp":
        image = Flux2KleinCppBackend(settings, preset)
    elif preset.image.backend == "z-image-cpp":
        image = ZImageCppBackend(settings, preset)
    elif preset.image.backend == "stable-diffusion-3.5" and preset.platform == "linux":
        image = IsolatedDiffusersImageBackend(settings, preset)
    else:
        image = DiffusersImageBackend(settings, preset)
    if preset.asset.backend == "hunyuan-mlx":
        return image, HunyuanMlxBackend(settings, preset)
    if preset.asset.backend == "pixal3d":
        return image, Pixal3DBackend(settings, preset)
    if preset.asset.backend == "trellis-cpp":
        return image, TrellisCppBackend(settings, preset)
    if preset.asset.backend == "instantmesh":
        return image, InstantMeshBackend(settings, preset)
    return image, StableFast3DBackend(settings, preset)


def _require_revision(
    model_path: Path, expected: str, *, marker: str = ".model-revision"
) -> None:
    revision_file = model_path / marker
    installed = (
        revision_file.read_text(encoding="utf-8").strip()
        if revision_file.is_file()
        else None
    )
    if installed != expected:
        actual = repr(installed) if installed is not None else "a missing marker"
        raise RuntimeError(
            f"Model revision mismatch at {model_path}: expected {expected!r}, "
            f"found {actual}. Rerun the model installer."
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


def _is_wsl() -> bool:
    if sys.platform != "linux":
        return False
    try:
        release = Path("/proc/sys/kernel/osrelease").read_text(encoding="utf-8")
    except OSError:
        return False
    return "microsoft" in release.lower()


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
