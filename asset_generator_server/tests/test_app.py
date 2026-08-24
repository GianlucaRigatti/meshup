from __future__ import annotations

import json
import threading

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.generator import GenerationError
from app.main import create_app
from tests.conftest import GLB_BYTES


def test_health_and_ready(client: TestClient) -> None:
    assert client.get("/healthz").json() == {"status": "ok"}
    assert client.get("/readyz").json() == {
        "status": "ready",
        "ready": True,
        "busy": False,
        "configured_profile": "auto",
        "profile": "macos-mlx",
        "device": "mps+mlx-metal",
        "output_mode": "vertex_color",
    }


def test_generate_serve_and_cache(client: TestClient) -> None:
    first = client.post("/generate_asset", json={"prompt": "  a   red chair  "})
    assert first.status_code == 200
    body = first.json()
    assert body["url"].startswith("http://testserver/assets/")
    assert body["cached"] is False
    assert isinstance(body["image_generation_time_ms"], int)
    assert isinstance(body["model_generation_time_ms"], int)
    assert isinstance(body["generation_time_ms"], int)

    asset = client.get(body["url"])
    assert asset.headers["content-type"] == "model/gltf-binary"
    assert asset.content == GLB_BYTES

    second = client.post("/generate_asset", json={"prompt": "a red chair"}).json()
    assert second["asset_id"] == body["asset_id"]
    assert second["cached"] is True
    assert second["image_generation_time_ms"] == 0
    assert second["model_generation_time_ms"] == 0
    assert second["generation_time_ms"] == 0


@pytest.mark.parametrize(
    "payload",
    [{}, {"prompt": ""}, {"prompt": "   "}, {"prompt": 42}, {"prompt": "x" * 501}],
)
def test_invalid_prompts_return_422(client: TestClient, payload: dict) -> None:
    assert client.post("/generate_asset", json=payload).status_code == 422


def test_failure_is_sanitized_and_leaves_no_files(app_parts) -> None:
    app, generator, fake, settings = app_parts
    fake.fail = True
    with pytest.raises(GenerationError):
        generator.generate("failed asset")
    assert list(settings.asset_output_dir.iterdir()) == []

    with TestClient(app) as client:
        response = client.post("/generate_asset", json={"prompt": "failed asset"})
    assert response.status_code == 500
    assert response.json()["error"]["code"] == "generation_failed"
    assert "private model failure" not in response.text


def test_busy_rejects_uncached_but_allows_cached(app_parts) -> None:
    app, _, fake, _ = app_parts
    with TestClient(app) as client:
        assert (
            client.post("/generate_asset", json={"prompt": "cached asset"}).status_code
            == 200
        )

        fake.started = threading.Event()
        fake.release = threading.Event()
        first_result = {}
        thread = threading.Thread(
            target=lambda: first_result.setdefault(
                "response",
                client.post("/generate_asset", json={"prompt": "new asset"}),
            )
        )
        thread.start()
        assert fake.started.wait(timeout=2)

        busy = client.post("/generate_asset", json={"prompt": "another new asset"})
        cached = client.post("/generate_asset", json={"prompt": "cached asset"})
        fake.release.set()
        thread.join(timeout=2)

    assert busy.status_code == 503
    assert busy.json()["error"]["code"] == "generator_busy"
    assert cached.status_code == 200
    assert cached.json()["cached"] is True
    assert first_result["response"].status_code == 200


def test_metadata_does_not_store_prompt(app_parts) -> None:
    _, generator, _, settings = app_parts
    asset_id, _, _ = generator.generate("a confidential shaped object")
    metadata = json.loads((settings.asset_output_dir / f"{asset_id}.json").read_text())
    assert "prompt_hash" in metadata
    assert "prompt" not in metadata
    assert metadata["timings"]["text_to_image_ms"] >= 0
    assert metadata["timings"]["reconstruction_ms"] >= 0
    assert metadata["timings"]["total_ms"] >= 0


def test_generation_saves_preprocessed_input_image(app_parts) -> None:
    _, generator, _, settings = app_parts
    asset_id, _, _ = generator.generate("a debug object")

    image_path = settings.asset_output_dir / f"{asset_id}.png"
    assert image_path.is_file()
    assert image_path.read_bytes().startswith(b"\x89PNG\r\n\x1a\n")


def test_missing_models_fail_readiness(tmp_path) -> None:
    settings = Settings(
        ASSET_OUTPUT_DIR=tmp_path / "assets",
        MODEL_CACHE_DIR=tmp_path / "missing",
    )
    with TestClient(create_app(settings)) as client:
        ready = client.get("/readyz")
        generation = client.post("/generate_asset", json={"prompt": "a chair"})
    assert ready.status_code == 503
    assert generation.status_code == 503
    assert generation.json()["error"]["code"] == "generator_not_ready"
