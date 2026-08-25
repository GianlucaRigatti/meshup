from __future__ import annotations

import json
import threading
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from PIL import Image

from app.config import Settings
from app.main import create_app
from app.pipeline import AssetGenerator


class FakeRunner:
    def __init__(self) -> None:
        self.calls: list[tuple[list[str], dict[str, str], int, str]] = []
        self.prompts: list[str] = []
        self.fail = False
        self.fail_label: str | None = None
        self.started: threading.Event | None = None
        self.release: threading.Event | None = None
        self.trellis_input_modes: list[str] = []
        self.transcript = "a small medieval treasure chest"
        self.language = "English"
        self.enhanced_prompt = (
            "A compact medieval treasure chest made from dark oak with iron bands"
        )

    def __call__(
        self,
        command: list[str],
        environment: dict[str, str],
        timeout: int,
        label: str,
    ) -> None:
        self.calls.append((command, environment, timeout, label))
        if self.fail or self.fail_label == label:
            raise RuntimeError("private native model failure")
        if label == "FLUX.2 Klein":
            prompt_path = Path(command[command.index("--prompt-file") + 1])
            self.prompts.append(prompt_path.read_text(encoding="utf-8"))
            output = Path(command[command.index("--output") + 1])
            Image.new("RGB", (64, 64), "blue").save(output, format="PNG")
            if self.started is not None:
                self.started.set()
            if self.release is not None:
                assert self.release.wait(timeout=2)
            return
        if label == "Audio preprocessing":
            Path(command[-1]).write_bytes(b"RIFF" + b"fake-wave")
            return
        if label == "Qwen3-ASR-1.7B":
            output = Path(command[command.index("--output") + 1])
            output.write_text(
                json.dumps({"text": self.transcript, "language": self.language}),
                encoding="utf-8",
            )
            return
        if label == "Qwen3.5-4B":
            output = Path(command[command.index("--output") + 1])
            output.write_text(self.enhanced_prompt, encoding="utf-8")
            return
        output = Path(command[2])
        with Image.open(command[1]) as image:
            self.trellis_input_modes.append(image.mode)
        output.write_bytes(b"glTF" + b"asset")


class FakeBackgroundRemover:
    def __init__(self) -> None:
        self.last_timings = {"background_inference_ms": 1}

    def load(self) -> None:
        return

    def remove(self, image: Image.Image) -> Image.Image:
        result = image.convert("RGBA")
        alpha = Image.new("L", image.size, 0)
        alpha.paste(255, (8, 8, image.width - 8, image.height - 8))
        result.putalpha(alpha)
        return result


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
    service = AssetGenerator(
        settings,
        runner=runner,
        capture_runner=lambda command, timeout, label: json.dumps(
            {
                "streams": [{"codec_name": "pcm_s16le", "duration": "1.0"}],
                "format": {"duration": "1.0", "format_name": "wav"},
            }
        ),
        background_remover=FakeBackgroundRemover(),
    )
    service.ready = True
    return service, runner


@pytest.fixture
def client(settings: Settings, generator: tuple[AssetGenerator, FakeRunner]):
    service, runner = generator
    with TestClient(create_app(settings, service)) as test_client:
        yield test_client, service, runner
