from __future__ import annotations

import numpy as np
import pytest
import trimesh
from PIL import Image

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
        == "windows-cuda-quality"
    )


@pytest.mark.parametrize(
    ("profile", "host"),
    [("macos-mlx", "win32"), ("windows-cuda-fast", "darwin")],
)
def test_wrong_platform_profile_is_rejected(profile: str, host: str) -> None:
    with pytest.raises(ValueError, match="requires"):
        resolve_profile(
            profile,
            platform_name=host,
            machine="arm64" if host == "darwin" else "AMD64",
        )


def test_unknown_and_unsupported_auto_profiles_are_rejected() -> None:
    with pytest.raises(ValueError, match="Unknown"):
        resolve_profile("anything", platform_name="darwin", machine="arm64")
    with pytest.raises(ValueError, match="does not support"):
        resolve_profile("auto", platform_name="linux", machine="x86_64")


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
