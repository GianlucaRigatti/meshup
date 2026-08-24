from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import diffusers
import numpy as np
import pytest
import trimesh
from PIL import Image

from app.backends import (
    IMAGE_RUNNER,
    INSTANTMESH_RUNNER,
    PIXAL3D_RUNNER,
    DiffusersImageBackend,
    IsolatedDiffusersImageBackend,
    InstantMeshBackend,
    Pixal3DBackend,
    TrellisCppBackend,
    ZImageCppBackend,
    create_backends,
)
from app.config import Settings
from app.generator import AssetGenerator, GenerationError
from app.presets import PRESETS, resolve_profile


def test_auto_profile_resolution() -> None:
    assert (
        resolve_profile("auto", platform_name="darwin", machine="arm64").name
        == "macos-mlx"
    )
    assert (
        resolve_profile("auto", platform_name="win32", machine="AMD64").name
        == "windows-cuda-sana"
    )
    assert (
        resolve_profile("auto", platform_name="linux", machine="x86_64").name
        == "linux-cuda-sana"
    )
    assert (
        resolve_profile("linux-cuda-fast", platform_name="linux", machine="AMD64").name
        == "linux-cuda-fast"
    )


@pytest.mark.parametrize(
    ("profile", "host"),
    [
        ("macos-mlx", "win32"),
        ("windows-cuda-fast", "darwin"),
        ("linux-cuda-fast", "win32"),
        ("windows-cuda-quality", "linux"),
    ],
)
def test_wrong_platform_profile_is_rejected(profile: str, host: str) -> None:
    with pytest.raises(ValueError, match="requires"):
        resolve_profile(
            profile,
            platform_name=host,
            machine="arm64" if host == "darwin" else "AMD64",
        )


def test_linux_profiles_require_x86_64() -> None:
    with pytest.raises(ValueError, match="64-bit x86 Linux"):
        resolve_profile("linux-cuda-quality", platform_name="linux", machine="aarch64")


@pytest.mark.parametrize("variant", ["quality", "fast"])
def test_linux_profiles_match_windows_pipeline_settings(variant: str) -> None:
    windows = PRESETS[f"windows-cuda-{variant}"]
    linux = PRESETS[f"linux-cuda-{variant}"]
    assert linux.image == windows.image
    assert linux.asset == windows.asset
    assert linux.device == windows.device
    assert linux.platform == "linux"


def test_sana_profiles_match_and_use_native_settings() -> None:
    windows = PRESETS["windows-cuda-sana"]
    linux = PRESETS["linux-cuda-sana"]

    assert linux.image == windows.image
    assert linux.asset == windows.asset
    assert windows.image.backend == "sana-sprint"
    assert windows.image.dtype == "bfloat16"
    assert windows.image.variant is None
    assert (windows.image.width, windows.image.height) == (1024, 1024)
    assert windows.image.steps == 2
    assert windows.image.guidance == 4.5


def test_pixal3d_profile_uses_low_vram_quality_settings() -> None:
    preset = PRESETS["wsl-cuda-pixal3d"]

    assert preset.platform == "linux"
    assert preset.image.backend == "sana-sprint"
    assert preset.asset.backend == "pixal3d"
    assert preset.asset.pipeline_resolution == 1024
    assert preset.asset.texture_resolution == 4096
    assert preset.asset.camera_fov == 0.2


def test_sd35_pixal3d_profile_quantizes_both_large_image_components() -> None:
    preset = PRESETS["wsl-cuda-sd35-pixal3d"]

    assert preset.platform == "linux"
    assert preset.image.backend == "stable-diffusion-3.5"
    assert preset.image.quantization == "nf4"
    assert preset.image.dtype == "bfloat16"
    assert preset.image.steps == 28
    assert preset.image.guidance == 7.0
    assert (preset.image.width, preset.image.height) == (1024, 1024)
    assert preset.asset == PRESETS["wsl-cuda-pixal3d"].asset


@pytest.mark.parametrize("quantization", ["q4", "q8"])
def test_sd35_trellis2_profiles_use_matching_quantization(quantization: str) -> None:
    preset = PRESETS[f"wsl-cuda-sd35-trellis2-{quantization}"]

    assert preset.image == PRESETS["wsl-cuda-sd35-pixal3d"].image
    assert preset.asset.backend == "trellis-cpp"
    assert preset.asset.quantization == quantization
    assert preset.asset.pipeline_resolution == 1024
    assert preset.asset.max_tokens == 49152
    assert preset.asset.texture_resolution == 2048


@pytest.mark.parametrize("quantization", ["q4", "q6"])
def test_zimage_trellis2_profiles_use_native_quantized_image_runtime(
    quantization: str,
) -> None:
    preset = PRESETS[f"wsl-cuda-zimage-{quantization}-trellis2-q4"]

    assert preset.image.backend == "z-image-cpp"
    assert preset.image.quantization == quantization
    assert preset.image.steps == 8
    assert preset.image.guidance == 1.0
    assert (preset.image.width, preset.image.height) == (1024, 1024)
    assert preset.asset == PRESETS["wsl-cuda-sd35-trellis2-q4"].asset


def test_zimage_fast_profile_only_reduces_trellis_quality() -> None:
    preset = PRESETS["wsl-cuda-zimage-q4-trellis2-fast"]
    quality = PRESETS["wsl-cuda-zimage-q4-trellis2-q4"]

    assert preset.image == quality.image
    assert preset.asset.pipeline_resolution == 512
    assert preset.asset.texture_resolution == 1024
    assert preset.asset.box_uv is False


def test_zimage_turbo_profile_uses_aggressive_latency_settings() -> None:
    preset = PRESETS["wsl-cuda-zimage-q3-trellis2-turbo"]

    assert preset.image.backend == "z-image-cpp"
    assert preset.image.quantization == "q3"
    assert preset.image.steps == 6
    assert (preset.image.width, preset.image.height) == (768, 768)
    assert preset.asset.quantization == "q4"
    assert preset.asset.pipeline_resolution == 512
    assert preset.asset.texture_resolution == 1024
    assert preset.asset.box_uv is True


def test_zimage_instantmesh_profile_uses_low_vram_latency_settings() -> None:
    preset = PRESETS["wsl-cuda-zimage-q4-instantmesh-fast"]

    assert preset.image == PRESETS["wsl-cuda-zimage-q4-trellis2-q4"].image
    assert preset.asset.backend == "instantmesh"
    assert preset.asset.steps == 30
    assert preset.asset.views == 4
    assert preset.asset.grid_resolution == 96
    assert preset.asset.texture_resolution == 512


def test_sd35_backend_selects_quantized_loader(tmp_path, monkeypatch) -> None:
    preset = PRESETS["wsl-cuda-sd35-pixal3d"]
    model_path = tmp_path / "models" / preset.image.directory_name
    model_path.mkdir(parents=True)
    (model_path / ".model-revision").write_text(preset.image.revision + "\n")
    captured = {}
    backend = DiffusersImageBackend(
        SimpleNamespace(image_model_path=model_path), preset
    )
    monkeypatch.setattr("app.backends._validate_cuda", lambda _torch: None)
    monkeypatch.setattr(
        backend,
        "_load_quantized_sd35",
        lambda torch_module, dtype: captured.update(torch=torch_module, dtype=dtype),
    )

    backend.load()

    assert captured["dtype"].__str__() == "torch.bfloat16"


def test_sd35_quantized_loader_uses_supported_balanced_device_map(
    tmp_path, monkeypatch
) -> None:
    preset = PRESETS["wsl-cuda-sd35-pixal3d"]
    settings = SimpleNamespace(image_model_path=tmp_path / "sd35")
    settings.image_model_path.mkdir()
    backend = DiffusersImageBackend(settings, preset)
    pipeline_options = {}

    class FakeModel:
        @classmethod
        def from_pretrained(cls, *_args, **_kwargs):
            return object()

    class FakePipeline:
        vae_tiling_enabled = False

        def __init__(self):
            self.vae = SimpleNamespace(enable_tiling=self._enable_vae_tiling)

        def _enable_vae_tiling(self):
            self.vae_tiling_enabled = True

        @classmethod
        def from_pretrained(cls, *_args, **kwargs):
            pipeline_options.update(kwargs)
            return cls()

        def set_progress_bar_config(self, **_kwargs):
            pass

    class FakeQuantizationConfig:
        def __init__(self, **_kwargs):
            pass

    monkeypatch.setattr("app.backends._is_wsl", lambda: True)
    monkeypatch.setattr(
        "diffusers.BitsAndBytesConfig", FakeQuantizationConfig
    )
    monkeypatch.setattr("diffusers.SD3Transformer2DModel", FakeModel)
    monkeypatch.setattr("diffusers.StableDiffusion3Pipeline", FakePipeline)
    monkeypatch.setattr(
        "transformers.BitsAndBytesConfig", FakeQuantizationConfig
    )
    monkeypatch.setattr("transformers.T5EncoderModel", FakeModel)

    backend._load_quantized_sd35(SimpleNamespace(), "bfloat16")

    assert pipeline_options["device_map"] == "balanced"
    assert pipeline_options["max_memory"] == {0: "9GiB", "cpu": "24GiB"}
    assert backend._pipeline.vae_tiling_enabled is True


def test_sd35_pixal3d_uses_isolated_image_backend(tmp_path) -> None:
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    preset = PRESETS["wsl-cuda-sd35-pixal3d"]

    image, asset = create_backends(settings, preset)

    assert isinstance(image, IsolatedDiffusersImageBackend)
    assert isinstance(asset, Pixal3DBackend)


@pytest.mark.parametrize("quantization", ["q4", "q8"])
def test_sd35_trellis2_uses_isolated_image_backend(
    tmp_path, quantization: str
) -> None:
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    preset = PRESETS[f"wsl-cuda-sd35-trellis2-{quantization}"]

    image, asset = create_backends(settings, preset)

    assert isinstance(image, IsolatedDiffusersImageBackend)
    assert isinstance(asset, TrellisCppBackend)


@pytest.mark.parametrize("quantization", ["q4", "q6"])
def test_zimage_trellis2_uses_native_isolated_image_backend(
    tmp_path, quantization: str
) -> None:
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    preset = PRESETS[f"wsl-cuda-zimage-{quantization}-trellis2-q4"]

    image, asset = create_backends(settings, preset)

    assert isinstance(image, ZImageCppBackend)
    assert image.isolated_process is True
    assert isinstance(asset, TrellisCppBackend)


def test_zimage_instantmesh_uses_two_isolated_process_backends(tmp_path) -> None:
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    preset = PRESETS["wsl-cuda-zimage-q4-instantmesh-fast"]

    image, asset = create_backends(settings, preset)

    assert isinstance(image, ZImageCppBackend)
    assert image.isolated_process is True
    assert isinstance(asset, InstantMeshBackend)


def test_zimage_cpp_backend_invokes_memory_bounded_native_cli(
    tmp_path, monkeypatch
) -> None:
    preset = PRESETS["wsl-cuda-zimage-q6-trellis2-q4"]
    executable = tmp_path / "runtime" / "bin" / "sd-cli"
    settings = SimpleNamespace(
        stable_diffusion_cpp_executable_path=executable,
        z_image_diffusion_path=tmp_path / "models" / "z-image-q6.gguf",
        z_image_text_encoder_path=tmp_path / "models" / "qwen-q4.gguf",
        z_image_vae_path=tmp_path / "models" / "ae.safetensors",
        image_timeout_seconds=600,
    )
    calls = []

    def run(command, **kwargs):
        prompt_path = Path(command[command.index("--prompt-file") + 1])
        calls.append((command, kwargs, prompt_path.read_text()))
        output = Path(command[command.index("--output") + 1])
        Image.new("RGB", (12, 10), "blue").save(output)
        return SimpleNamespace(returncode=0, stdout="")

    backend = ZImageCppBackend(settings, preset)
    monkeypatch.setattr(backend, "load", lambda: None)
    monkeypatch.setattr("app.backends.subprocess.run", run)

    image = backend.generate("private test prompt", 123)

    command, kwargs, prompt = calls[0]
    assert command[0] == str(executable.resolve())
    assert "private test prompt" not in command
    assert prompt == "private test prompt"
    assert command[command.index("--steps") + 1] == "8"
    assert command[command.index("--cfg-scale") + 1] == "1.0"
    assert command[command.index("--width") + 1] == "1024"
    assert command[command.index("--height") + 1] == "1024"
    assert command[command.index("--max-vram") + 1] == "cuda0=10.5"
    assert "--auto-fit" in command
    assert "--diffusion-fa" in command
    assert "--vae-tiling" in command
    assert str(executable.parent.resolve()) in kwargs["env"]["LD_LIBRARY_PATH"]
    assert image.size == (12, 10)


def test_trellis_cpp_backend_invokes_pinned_quality_cli(tmp_path, monkeypatch) -> None:
    preset = PRESETS["wsl-cuda-sd35-trellis2-q4"]
    executable = tmp_path / "runtime" / "trellis-cli"
    models = tmp_path / "models" / "q4"
    settings = SimpleNamespace(
        trellis_cpp_executable_path=executable,
        trellis_cpp_model_path=models,
        trellis_cpp_build_path=executable.parent,
        trellis_timeout_seconds=1800,
    )
    calls = []

    def run(command, **kwargs):
        calls.append((command, kwargs))
        Path(command[2]).write_bytes(b"glTF")
        return SimpleNamespace(returncode=0, stdout="")

    backend = TrellisCppBackend(settings, preset)
    monkeypatch.setattr(backend, "load", lambda: None)
    monkeypatch.setattr("app.backends.subprocess.run", run)
    output = tmp_path / "job" / "asset.glb"
    output.parent.mkdir()

    backend.generate(Image.new("RGBA", (16, 16)), 123, output)

    command, kwargs = calls[0]
    assert command[0] == str(executable.resolve())
    assert command[command.index("--models") + 1] == str(models.resolve())
    assert command[command.index("--res") + 1] == "1024"
    assert command[command.index("--max-tokens") + 1] == "49152"
    assert command[command.index("--atlas") + 1] == "2048"
    assert command[command.index("--webp") + 1] == "off"
    assert command[command.index("--seed") + 1] == "123"
    assert "--require-gpu" in command
    assert str(executable.parent.resolve()) in kwargs["env"]["LD_LIBRARY_PATH"]


def test_trellis_cpp_turbo_backend_uses_light_path_and_box_uv(
    tmp_path, monkeypatch
) -> None:
    preset = PRESETS["wsl-cuda-zimage-q3-trellis2-turbo"]
    executable = tmp_path / "runtime" / "trellis-cli"
    settings = SimpleNamespace(
        trellis_cpp_executable_path=executable,
        trellis_cpp_model_path=tmp_path / "models" / "q4",
        trellis_cpp_build_path=executable.parent,
        trellis_timeout_seconds=1800,
    )
    calls = []

    def run(command, **kwargs):
        calls.append(command)
        Path(command[2]).write_bytes(b"glTF")
        return SimpleNamespace(returncode=0, stdout="")

    backend = TrellisCppBackend(settings, preset)
    monkeypatch.setattr(backend, "load", lambda: None)
    monkeypatch.setattr("app.backends.subprocess.run", run)
    output = tmp_path / "job" / "asset.glb"
    output.parent.mkdir()

    backend.generate(Image.new("RGBA", (16, 16)), 123, output)

    command = calls[0]
    assert command[command.index("--res") + 1] == "512"
    assert command[command.index("--atlas") + 1] == "1024"
    assert "--box-uv" in command


def test_instantmesh_backend_invokes_sequential_low_vram_runner(
    tmp_path, monkeypatch
) -> None:
    preset = PRESETS["wsl-cuda-zimage-q4-instantmesh-fast"]
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    calls = []

    def run(command, **kwargs):
        calls.append((command, kwargs))
        Path(command[command.index("--output") + 1]).write_bytes(b"glTF")
        return SimpleNamespace(returncode=0, stdout="")

    backend = InstantMeshBackend(settings, preset)
    monkeypatch.setattr(backend, "load", lambda: None)
    monkeypatch.setattr("app.backends.subprocess.run", run)
    output = tmp_path / "job" / "asset.glb"
    output.parent.mkdir()

    backend.generate(Image.new("RGBA", (16, 16)), 123, output)

    command, kwargs = calls[0]
    assert command[0] == str(settings.instantmesh_python_path.absolute())
    assert command[1] == str(INSTANTMESH_RUNNER)
    assert command[command.index("--diffusion-steps") + 1] == "30"
    assert command[command.index("--views") + 1] == "4"
    assert command[command.index("--grid-resolution") + 1] == "96"
    assert command[command.index("--texture-resolution") + 1] == "512"
    assert kwargs["env"]["HF_HUB_OFFLINE"] == "1"


def test_isolated_sd35_backend_passes_private_request_over_stdin(
    tmp_path, monkeypatch
) -> None:
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    backend = IsolatedDiffusersImageBackend(
        settings, PRESETS["wsl-cuda-sd35-pixal3d"]
    )
    calls = []

    def run(command, **kwargs):
        calls.append((command, kwargs))
        output = Path(command[command.index("--output") + 1])
        Image.new("RGB", (12, 10), "red").save(output)
        return SimpleNamespace(returncode=0, stdout="")

    monkeypatch.setattr(backend, "load", lambda: None)
    monkeypatch.setattr("app.backends.subprocess.run", run)

    image = backend.generate("private test prompt", 123)

    command, kwargs = calls[0]
    assert command[1] == str(IMAGE_RUNNER)
    assert "private test prompt" not in command
    assert kwargs["input"] == '{"prompt": "private test prompt", "seed": 123}'
    assert kwargs["env"]["HF_HUB_OFFLINE"] == "1"
    assert image.size == (12, 10)


def test_pixal3d_backend_invokes_pinned_low_vram_cli(tmp_path, monkeypatch) -> None:
    preset = PRESETS["wsl-cuda-pixal3d"]
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    settings.pixal3d_python_path.parent.mkdir(parents=True)
    settings.pixal3d_python_path.touch()
    settings.pixal3d_source_path.mkdir(parents=True)
    (settings.pixal3d_source_path / "inference.py").touch()
    settings.asset_model_path.mkdir(parents=True)
    (settings.asset_model_path / "pipeline.json").touch()
    (settings.asset_model_path / ".model-revision").write_text(
        preset.asset.revision + "\n"
    )
    calls = []

    def run(command, **kwargs):
        calls.append((command, kwargs))
        output = Path(command[command.index("--output") + 1])
        output.write_bytes(b"glTF")
        return SimpleNamespace(returncode=0, stdout="")

    monkeypatch.setattr("app.backends.subprocess.run", run)
    backend = Pixal3DBackend(settings, preset)
    monkeypatch.setattr(backend, "load", lambda: None)
    output = tmp_path / "job" / "asset.glb"
    output.parent.mkdir()

    backend.generate(Image.new("RGBA", (8, 8)), 123, output)

    command, kwargs = calls[0]
    assert command[0] == str(settings.pixal3d_python_path.absolute())
    assert command[1] == str(PIXAL3D_RUNNER)
    assert command[command.index("--model_path") + 1] == str(
        settings.asset_model_path.resolve()
    )
    assert command[command.index("--resolution") + 1] == "1024"
    assert command[command.index("--fov") + 1] == "0.2"
    assert command[command.index("--seed") + 1] == "123"
    assert "--low_vram" in command
    assert kwargs["env"]["ATTN_BACKEND"] == "sdpa"
    assert kwargs["env"]["HF_HUB_OFFLINE"] == "1"


def test_pixal3d_backend_reports_each_missing_runtime_file(
    tmp_path, monkeypatch
) -> None:
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    backend = Pixal3DBackend(settings, PRESETS["wsl-cuda-pixal3d"])
    monkeypatch.setattr("app.backends._is_wsl", lambda: True)

    with pytest.raises(FileNotFoundError) as error:
        backend.load()

    message = str(error.value)
    for path in settings.pixal3d_required_files:
        assert str(path.resolve()) in message
    assert "--force" in message


def test_pixal3d_readiness_failure_includes_subprocess_output(
    tmp_path, monkeypatch
) -> None:
    settings = Settings(MODEL_CACHE_DIR=tmp_path)
    for path in settings.pixal3d_required_files:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.touch()
    backend = Pixal3DBackend(settings, PRESETS["wsl-cuda-pixal3d"])
    monkeypatch.setattr("app.backends._is_wsl", lambda: True)
    monkeypatch.setattr("app.backends._require_git_revision", lambda *_args: None)
    monkeypatch.setattr("app.backends._require_revision", lambda *_args: None)
    monkeypatch.setattr(
        "app.backends.subprocess.run",
        lambda *_args, **_kwargs: SimpleNamespace(
            returncode=1, stdout="ModuleNotFoundError: missing dependency"
        ),
    )

    with pytest.raises(RuntimeError, match="ModuleNotFoundError: missing dependency"):
        backend.load()


def test_unknown_and_unsupported_auto_profiles_are_rejected() -> None:
    with pytest.raises(ValueError, match="Unknown"):
        resolve_profile("anything", platform_name="darwin", machine="arm64")
    with pytest.raises(ValueError, match="does not support"):
        resolve_profile("auto", platform_name="linux", machine="aarch64")


def test_cache_identity_changes_with_preset(tmp_path) -> None:
    settings = Settings(ASSET_OUTPUT_DIR=tmp_path, MODEL_CACHE_DIR=tmp_path / "models")
    mac = AssetGenerator(
        settings,
        image_backend=_ImageBackend(),
        asset_backend=_AssetBackend("vertex_color"),
        preset=PRESETS["macos-mlx"],
    )
    windows = AssetGenerator(
        settings,
        image_backend=_ImageBackend(),
        asset_backend=_AssetBackend("pbr_texture"),
        preset=PRESETS["windows-cuda-quality"],
    )
    assert mac._asset_id("chair") != windows._asset_id("chair")
    linux = AssetGenerator(
        settings,
        image_backend=_ImageBackend(),
        asset_backend=_AssetBackend("pbr_texture"),
        preset=PRESETS["linux-cuda-quality"],
    )
    assert linux._asset_id("chair") != windows._asset_id("chair")


def test_linux_image_backend_releases_cuda_memory(tmp_path) -> None:
    events: list[str] = []

    class _Cuda:
        @staticmethod
        def is_available() -> bool:
            return True

        @staticmethod
        def empty_cache() -> None:
            events.append("empty_cache")

    backend = DiffusersImageBackend(
        Settings(MODEL_CACHE_DIR=tmp_path), PRESETS["linux-cuda-quality"]
    )
    backend._torch = type("FakeTorch", (), {"cuda": _Cuda})
    backend.release_device_memory()
    assert events == ["empty_cache"]


def test_sana_backend_uses_sprint_pipeline_and_bfloat16(tmp_path, monkeypatch) -> None:
    preset = PRESETS["linux-cuda-sana"]
    model_path = tmp_path / "models" / preset.image.directory_name
    model_path.mkdir(parents=True)
    (model_path / ".model-revision").write_text(preset.image.revision + "\n")
    captured = {}

    class _Pipeline:
        @staticmethod
        def from_pretrained(path, **kwargs):
            captured.update(path=path, kwargs=kwargs)
            return _Pipeline()

        def set_progress_bar_config(self, **_kwargs) -> None:
            return

        def enable_model_cpu_offload(self, **_kwargs) -> None:
            return

    monkeypatch.setattr(diffusers, "SanaSprintPipeline", _Pipeline)
    monkeypatch.setattr("app.backends._validate_cuda", lambda _torch: None)
    backend = DiffusersImageBackend(
        SimpleNamespace(image_model_path=model_path), preset
    )

    backend.load()

    assert captured["path"] == model_path
    assert captured["kwargs"]["torch_dtype"].__str__() == "torch.bfloat16"
    assert "variant" not in captured["kwargs"]
    assert "safety_checker" not in captured["kwargs"]


def test_windows_texture_failure_leaves_no_output(tmp_path) -> None:
    settings = Settings(
        ASSET_OUTPUT_DIR=tmp_path / "assets", MODEL_CACHE_DIR=tmp_path / "models"
    )
    settings.asset_output_dir.mkdir()
    generator = AssetGenerator(
        settings,
        image_backend=_ImageBackend(),
        asset_backend=_AssetBackend("pbr_texture"),
        preset=PRESETS["windows-cuda-quality"],
    )
    generator._remove_background = lambda image: image.convert("RGBA")
    generator.ready = True
    with pytest.raises(GenerationError):
        generator.generate("a chair")
    assert list(settings.asset_output_dir.iterdir()) == []


def test_image_device_is_released_before_asset_generation(tmp_path) -> None:
    events: list[str] = []
    settings = Settings(
        ASSET_OUTPUT_DIR=tmp_path / "assets", MODEL_CACHE_DIR=tmp_path / "models"
    )
    settings.asset_output_dir.mkdir()
    image = _ImageBackend(events)
    asset = _AssetBackend("vertex_color", events)
    generator = AssetGenerator(
        settings,
        image_backend=image,
        asset_backend=asset,
        preset=PRESETS["macos-mlx"],
    )
    generator._remove_background = lambda value: value.convert("RGBA")
    generator.ready = True
    generator.generate("a chair")
    assert events.index("image:cpu") < events.index("asset:generate")
    assert events.index("image:release") < events.index("asset:generate")


def test_isolated_image_generation_does_not_initialize_parent_cuda(
    tmp_path, monkeypatch
) -> None:
    import torch

    settings = Settings(
        ASSET_OUTPUT_DIR=tmp_path / "assets", MODEL_CACHE_DIR=tmp_path / "models"
    )
    settings.asset_output_dir.mkdir()
    image = _ImageBackend()
    image.isolated_process = True
    generator = AssetGenerator(
        settings,
        image_backend=image,
        asset_backend=_AssetBackend("vertex_color"),
        preset=PRESETS["wsl-cuda-sd35-pixal3d"],
    )
    generator._remove_background = lambda value: value.convert("RGBA")
    generator.ready = True
    monkeypatch.setattr(
        torch.cuda,
        "reset_peak_memory_stats",
        lambda *_args: pytest.fail("parent process initialized CUDA statistics"),
    )

    generator.generate("a chair")


class _ImageBackend:
    device = "fake"

    def __init__(self, events: list[str] | None = None) -> None:
        self.events = events

    def load(self) -> None:
        return

    def generate(self, _prompt: str, _seed: int) -> Image.Image:
        if self.events is not None:
            self.events.append("image:generate")
        return Image.new("RGB", (16, 16))

    def move_to_cpu(self) -> None:
        if self.events is not None:
            self.events.append("image:cpu")

    def release_device_memory(self) -> None:
        if self.events is not None:
            self.events.append("image:release")


class _AssetBackend:
    device = "fake"

    def __init__(self, output_mode: str, events: list[str] | None = None) -> None:
        self.output_mode = output_mode
        self.events = events

    def load(self) -> None:
        return

    def generate(self, _image: Image.Image, _seed: int, output) -> None:
        if self.events is not None:
            self.events.append("asset:generate")
        mesh = trimesh.creation.box()
        if self.output_mode == "pbr_texture":
            mesh.visual = trimesh.visual.texture.TextureVisuals(
                uv=np.zeros((len(mesh.vertices), 2)),
                material=trimesh.visual.material.PBRMaterial(),
            )
        mesh.export(output, file_type="glb")

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        return
