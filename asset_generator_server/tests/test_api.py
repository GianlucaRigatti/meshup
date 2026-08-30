from __future__ import annotations

import json
from dataclasses import replace
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app import cli
from app.config import Settings
from app.main import create_app
from app.pipeline import AssetGenerator


def test_settings_keep_only_operational_environment_values(tmp_path: Path) -> None:
    env_file = tmp_path / ".env"
    env_file.write_text(
        "ASSET_OUTPUT_DIR=from-file\n"
        "IMAGE_TIMEOUT_SECONDS=30\n"
        "AUDIO_MAX_BYTES=2048\n"
        "IMAGE_GENERATOR=removed-selection\n",
        encoding="utf-8",
    )

    settings = Settings.from_env(
        {
            "IMAGE_TIMEOUT_SECONDS": "45",
            "TRELLIS_TIMEOUT_SECONDS": "90",
            "ASR_TIMEOUT_SECONDS": "12",
        },
        env_file=env_file,
    )

    assert settings.asset_output_dir == Path("from-file")
    assert settings.image_timeout_seconds == 45
    assert settings.trellis_timeout_seconds == 90
    assert settings.audio_max_bytes == 2048
    assert settings.asr_timeout_seconds == 12
    assert not hasattr(settings, "image_generator")


def test_health_and_ready(client) -> None:
    test_client, _, _ = client
    assert test_client.get("/healthz").json() == {"status": "ok"}
    assert test_client.get("/readyz").json() == {
        "status": "ready",
        "ready": True,
        "busy": False,
        "image_generator": "flux2-klein-9b-q4-k-m-fast",
        "model_3d": "trellis2-fast",
        "background_removal_model": "birefnet-general",
        "speech_to_text_model": "qwen3-asr-1.7b",
        "prompt_enhancer_model": "qwen3.5-4b",
        "device": "cuda:0",
        "output_mode": "pbr_texture",
    }


def test_generate_serves_all_artifacts_and_cache(client) -> None:
    test_client, _, _ = client
    first = test_client.post("/generate_asset", json={"prompt": "  a   red chair  "})
    assert first.status_code == 200
    body = first.json()
    assert body["url"].startswith("http://testserver/assets/")
    assert body["cached"] is False
    assert isinstance(body["image_generation_time_ms"], int)
    assert isinstance(body["model_generation_time_ms"], int)
    assert isinstance(body["generation_time_ms"], int)
    assert test_client.get(body["url"]).headers["content-type"] == "model/gltf-binary"
    assert test_client.get(body["url"].replace(".glb", ".png")).status_code == 200
    metadata_response = test_client.get(body["url"].replace(".glb", ".json"))
    assert metadata_response.status_code == 200
    assert json.loads(metadata_response.text)["asset_id"] == body["asset_id"]

    second = test_client.post("/generate_asset", json={"prompt": "a red chair"})
    assert second.json()["asset_id"] == body["asset_id"]
    assert second.json()["cached"] is True
    assert second.json()["generation_time_ms"] == 0


@pytest.mark.parametrize(
    ("filename", "content_type"),
    [
        ("sample.wav", "audio/wav"),
        ("sample.mp3", "audio/mpeg"),
        ("sample.flac", "audio/flac"),
        ("sample.ogg", "audio/ogg"),
        ("sample.m4a", "audio/mp4"),
    ],
)
def test_audio_generation_returns_intermediate_text_and_artifacts(
    client, filename: str, content_type: str
) -> None:
    test_client, _, _ = client
    response = test_client.post(
        "/generate_asset_from_audio",
        files={"audio": (filename, b"encoded audio", content_type)},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["transcript"] == "a small medieval treasure chest"
    assert body["transcript_language"] == "English"
    assert body["enhanced_prompt"] == "A small medieval treasure chest"
    assert body["cached"] is False
    assert isinstance(body["audio_preprocessing_time_ms"], int)
    assert isinstance(body["transcription_time_ms"], int)
    assert isinstance(body["prompt_enhancement_time_ms"], int)
    assert test_client.get(body["url"]).status_code == 200


def test_audio_metadata_contains_intermediate_text_and_hashes(client) -> None:
    test_client, _, _ = client
    body = test_client.post(
        "/generate_asset_from_audio",
        files={"audio": ("sample.wav", b"audio", "audio/wav")},
    ).json()

    metadata = test_client.get(body["url"].replace(".glb", ".json")).text
    parsed = json.loads(metadata)
    assert "transcript_hash" in parsed
    assert "enhanced_prompt_hash" in parsed
    assert parsed["transcript"] == body["transcript"]
    assert parsed["enhanced_prompt"] == body["enhanced_prompt"]


def test_empty_and_missing_audio_are_rejected(client) -> None:
    test_client, _, _ = client
    assert test_client.post("/generate_asset_from_audio").status_code == 422
    response = test_client.post(
        "/generate_asset_from_audio",
        files={"audio": ("empty.wav", b"", "audio/wav")},
    )
    assert response.status_code == 422
    assert response.json()["error"]["code"] == "invalid_audio"


def test_unsupported_and_overlong_audio_are_rejected(client) -> None:
    test_client, service, _ = client
    service._capture_runner = lambda *args: json.dumps(
        {
            "streams": [{"codec_name": "opus", "duration": "1"}],
            "format": {"format_name": "ogg"},
        }
    )
    unsupported = test_client.post(
        "/generate_asset_from_audio",
        files={"audio": ("sample.ogg", b"audio", "audio/ogg")},
    )
    assert unsupported.status_code == 415
    assert unsupported.json()["error"]["code"] == "unsupported_audio_type"

    service._capture_runner = lambda *args: json.dumps(
        {
            "streams": [{"codec_name": "aac", "duration": "61"}],
            "format": {"format_name": "mov,mp4,m4a,3gp,3g2,mj2"},
        }
    )
    overlong = test_client.post(
        "/generate_asset_from_audio",
        files={"audio": ("sample.m4a", b"audio", "audio/mp4")},
    )
    assert overlong.status_code == 422
    assert overlong.json()["error"]["code"] == "audio_too_long"


def test_oversized_audio_is_rejected_before_generation(client) -> None:
    _, service, runner = client
    configured = replace(service.settings, audio_max_bytes=4)
    with TestClient(create_app(configured, service)) as test_client:
        response = test_client.post(
            "/generate_asset_from_audio",
            files={"audio": ("sample.wav", b"12345", "audio/wav")},
        )
    assert response.status_code == 413
    assert response.json()["error"]["code"] == "audio_too_large"
    assert runner.calls == []


def test_audio_model_failure_is_sanitized(client) -> None:
    test_client, _, runner = client
    runner.fail_label = "Qwen3-ASR-1.7B"
    response = test_client.post(
        "/generate_asset_from_audio",
        files={"audio": ("sample.wav", b"audio", "audio/wav")},
    )
    assert response.status_code == 500
    assert response.json()["error"]["code"] == "generation_failed"
    assert "private native model failure" not in response.text


def test_busy_audio_request_is_rejected_before_pipeline(client) -> None:
    test_client, service, runner = client
    assert service._lock.acquire(blocking=False)
    try:
        response = test_client.post(
            "/generate_asset_from_audio",
            files={"audio": ("sample.wav", b"audio", "audio/wav")},
        )
    finally:
        service._lock.release()
    assert response.status_code == 503
    assert response.json()["error"]["code"] == "generator_busy"
    assert runner.calls == []


def test_audio_not_ready_returns_503(settings: Settings) -> None:
    service = AssetGenerator(settings)
    with TestClient(create_app(settings, service)) as test_client:
        response = test_client.post(
            "/generate_asset_from_audio",
            files={"audio": ("sample.wav", b"audio", "audio/wav")},
        )
    assert response.status_code == 503
    assert response.json()["error"]["code"] == "generator_not_ready"


@pytest.mark.parametrize(
    "payload",
    [
        {},
        {"prompt": ""},
        {"prompt": "   "},
        {"prompt": 42},
        {"prompt": "x" * 501},
        {"prompt": "chair", "model": "other"},
    ],
)
def test_invalid_prompts_return_422(client, payload: dict) -> None:
    test_client, _, _ = client
    assert test_client.post("/generate_asset", json=payload).status_code == 422


def test_public_base_url(settings: Settings, generator) -> None:
    service, _ = generator
    configured = Settings(
        public_base_url="https://assets.example.test/root/",
        asset_output_dir=settings.asset_output_dir,
        model_cache_dir=settings.model_cache_dir,
    )
    with TestClient(create_app(configured, service)) as test_client:
        response = test_client.post("/generate_asset", json={"prompt": "a chair"})
    assert response.json()["url"].startswith("https://assets.example.test/root/assets/")


def test_audio_public_base_url(settings: Settings, generator) -> None:
    service, _ = generator
    configured = replace(settings, public_base_url="https://assets.example.test/root/")
    with TestClient(create_app(configured, service)) as test_client:
        response = test_client.post(
            "/generate_asset_from_audio",
            files={"audio": ("sample.wav", b"audio", "audio/wav")},
        )
    assert response.json()["url"].startswith("https://assets.example.test/root/assets/")


def test_not_ready_returns_503(settings: Settings) -> None:
    service = AssetGenerator(settings)
    with TestClient(create_app(settings, service)) as test_client:
        ready = test_client.get("/readyz")
        generation = test_client.post("/generate_asset", json={"prompt": "chair"})
    assert ready.status_code == 503
    assert ready.json()["status"] == "not_ready"
    assert generation.status_code == 503
    assert generation.json()["error"]["code"] == "generator_not_ready"


def test_failure_is_sanitized(client) -> None:
    test_client, _, runner = client
    runner.fail = True
    response = test_client.post("/generate_asset", json={"prompt": "failed object"})
    assert response.status_code == 500
    assert response.json()["error"]["code"] == "generation_failed"
    assert "private native model failure" not in response.text


def test_busy_error_preserves_public_contract(client) -> None:
    test_client, service, _ = client
    assert service._lock.acquire(blocking=False)
    try:
        response = test_client.post(
            "/generate_asset", json={"prompt": "an uncached object"}
        )
    finally:
        service._lock.release()
    assert response.status_code == 503
    assert response.json()["error"]["code"] == "generator_busy"


def test_cli_has_no_model_selection_and_rejects_non_wsl(monkeypatch) -> None:
    help_text = cli.build_parser().format_help()
    assert "--image-generator" not in help_text
    assert "--model-3d" not in help_text
    assert "--list-models" not in help_text
    monkeypatch.setattr(cli, "is_wsl", lambda: False)
    with pytest.raises(SystemExit):
        cli.main([])
