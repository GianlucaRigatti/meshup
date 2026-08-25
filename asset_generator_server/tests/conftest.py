from __future__ import annotations

import threading
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from app.pipeline import PNG_SIGNATURE, AssetGenerator


class FakeRunner:
    def __init__(self) -> None:
        self.calls: list[tuple[list[str], dict[str, str], int, str]] = []
        self.prompts: list[str] = []
        self.fail = False
        self.started: threading.Event | None = None
        self.release: threading.Event | None = None

    def __call__(
        self,
        command: list[str],
        environment: dict[str, str],
        timeout: int,
        label: str,
    ) -> None:
        self.calls.append((command, environment, timeout, label))
        if self.fail:
            raise RuntimeError("private native model failure")
        if label == "FLUX.2 Klein":
            prompt_path = Path(command[command.index("--prompt-file") + 1])
            self.prompts.append(prompt_path.read_text(encoding="utf-8"))
            output = Path(command[command.index("--output") + 1])
            output.write_bytes(PNG_SIGNATURE + b"source")
            if self.started is not None:
                self.started.set()
            if self.release is not None:
                assert self.release.wait(timeout=2)
            return
        output = Path(command[2])
        output.write_bytes(b"glTF" + b"asset")
        output.with_name(f"{output.stem}_cutout.png").write_bytes(
            PNG_SIGNATURE + b"cutout"
        )


@pytest.fixture
def settings(tmp_path: Path) -> Settings:
    output = tmp_path / "assets"
    output.mkdir()
    return Settings(
        asset_output_dir=output,
        model_cache_dir=tmp_path / "models",
    )


@pytest.fixture
def generator(settings: Settings) -> tuple[AssetGenerator, FakeRunner]:
    runner = FakeRunner()
    service = AssetGenerator(settings, runner=runner)
    service.ready = True
    return service, runner


@pytest.fixture
def client(settings: Settings, generator: tuple[AssetGenerator, FakeRunner]):
    service, runner = generator
    with TestClient(create_app(settings, service)) as test_client:
        yield test_client, service, runner
