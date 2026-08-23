from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import diffusers
import numpy as np
import pytest
import trimesh
from PIL import Image

from app.backends import DiffusersImageBackend, Pixal3DBackend
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
        @classmethod
        def from_pretrained(cls, *_args, **kwargs):
            pipeline_options.update(kwargs)
            return cls()

        def enable_vae_tiling(self):
            pass

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
    assert command[command.index("--model_path") + 1] == str(
        settings.asset_model_path.resolve()
    )
    assert command[command.index("--resolution") + 1] == "1024"
    assert command[command.index("--fov") + 1] == "0.2"
    assert command[command.index("--seed") + 1] == "123"
    assert "--low_vram" in command
    assert kwargs["env"]["ATTN_BACKEND"] == "sdpa"
    assert kwargs["env"]["HF_HUB_OFFLINE"] == "1"


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
