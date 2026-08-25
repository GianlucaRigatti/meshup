from __future__ import annotations

import threading
from pathlib import Path

import pytest
import trimesh
from fastapi.testclient import TestClient
from PIL import Image

from app.config import Settings
from app.generator import AssetGenerator
from app.main import create_app

GLB_BYTES = trimesh.creation.box().export(file_type="glb")


class FakeImageBackend:
    device = "fake-image"

    def load(self) -> None:
        return

    def generate(self, _prompt: str, _seed: int) -> Image.Image:
        return Image.new("RGB", (16, 16))

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        return


class FakeAssetBackend:
    device = "fake-asset"
    output_mode = "vertex_color"

    def __init__(self) -> None:
        self.started: threading.Event | None = None
        self.release: threading.Event | None = None
        self.fail = False

    def load(self) -> None:
        return

    def generate(self, _image: Image.Image, _seed: int, output: Path) -> None:
        if self.started:
            self.started.set()
        if self.release:
            self.release.wait(timeout=5)
        if self.fail:
            raise RuntimeError("private model failure")
        output.write_bytes(GLB_BYTES)

    def move_to_cpu(self) -> None:
        return

    def release_device_memory(self) -> None:
        return


@pytest.fixture
def app_parts(tmp_path: Path):
    settings = Settings(
        ASSET_OUTPUT_DIR=tmp_path / "assets",
        MODEL_CACHE_DIR=tmp_path / "models",
    )
    fake = FakeAssetBackend()
    generator = AssetGenerator(
        settings,
        image_backend=FakeImageBackend(),
        asset_backend=fake,
    )
    generator._remove_background = lambda image: image.convert("RGBA")
    generator.ready = True
    return create_app(settings, generator), generator, fake, settings


@pytest.fixture
def client(app_parts):
    with TestClient(app_parts[0]) as test_client:
        yield test_client
