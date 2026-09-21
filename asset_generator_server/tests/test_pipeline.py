from __future__ import annotations

import json
import subprocess
import sys
import threading
import types
import warnings
from pathlib import Path

import numpy as np
import pytest
from conftest import FakeBackgroundRemover, FakeRunner
from PIL import Image

from app.background_removal import TorchBiRefNet, prepare_foreground
from app.config import (
    AUDIO_PIPELINE_VERSION,
    GENERATION_SEED_VERSION,
    PROMPT_SUFFIX,
    Settings,
)
from app.pipeline import (
    PNG_SIGNATURE,
    AssetGenerator,
    BusyError,
    EmptyTranscriptError,
    GenerationError,
    _run_native,
    _supported_audio_format,
)


def test_asset_identity_normalizes_prompt_and_includes_pipeline_version(
    settings: Settings,
) -> None:
    first = AssetGenerator(settings, pipeline_version="one")
    second = AssetGenerator(settings, pipeline_version="two")

    assert first.asset_id("  a   red chair ") == first.asset_id("a red chair")
    assert first.asset_id("a red chair") != second.asset_id("a red chair")
    assert first.generation_seed("  a   red chair ") == 1897674197
    assert first.generation_seed("a red chair") == 1897674197


def test_fixed_commands_are_sequential_and_prompt_is_private(
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    _asset_id, cached, _ = service.generate("private test object")

    assert cached is False
    assert [call[3] for call in runner.calls] == [
        "FLUX.2 Klein",
        "TRELLIS.2",
        "glTF-Transform simplification",
    ]
    flux = runner.calls[0][0]
    trellis = runner.calls[1][0]
    simplifier = runner.calls[2][0]
    assert runner.calls[2][2] == service.settings.gltf_transform_timeout_seconds
    assert "private test object" not in flux
    assert runner.prompts == ["private test object" + PROMPT_SUFFIX]
    assert flux[flux.index("--steps") + 1] == "4"
    assert flux[flux.index("--width") + 1] == "768"
    assert flux[flux.index("--height") + 1] == "768"
    assert flux[flux.index("--max-vram") + 1] == "cuda0=11"
    assert "--diffusion-fa" in flux
    assert "--offload-to-cpu" in flux
    assert "--auto-fit" in flux
    assert "--vae-tiling" not in flux
    assert trellis[trellis.index("--res") + 1] == "512"
    assert trellis[trellis.index("--max-tokens") + 1] == "49152"
    assert trellis[trellis.index("--atlas") + 1] == "1024"
    assert "--bg-removal" not in trellis
    assert "--dump-bg" not in trellis
    assert "--box-uv" not in trellis
    assert "--require-gpu" in trellis
    assert trellis[trellis.index("--seed") + 1] == str(
        service.generation_seed("private test object")
    )
    assert runner.trellis_input_modes == ["RGBA"]
    assert "--max-triangles" not in simplifier
    assert simplifier[simplifier.index("--max-texture-size") + 1] == "512"
    assert simplifier[simplifier.index("--error") + 1] == "0.0001"
    assert "--lock-border" in simplifier
    assert "minimal shading gradients" in PROMPT_SUFFIX
    assert "no cast shadows" in PROMPT_SUFFIX
    assert "no reflections" in PROMPT_SUFFIX
    assert "no specular highlights" in PROMPT_SUFFIX


def test_mesh_simplification_can_be_disabled(settings: Settings) -> None:
    disabled_settings = Settings(
        asset_output_dir=settings.asset_output_dir,
        model_cache_dir=settings.model_cache_dir,
        mesh_simplification=False,
    )
    runner = FakeRunner()
    service = AssetGenerator(
        disabled_settings,
        runner=runner,
        capture_runner=lambda command, timeout, label: "{}",
        background_remover=FakeBackgroundRemover(),
    )
    service.ready = True

    asset_id, cached, _ = service.generate("unsimplified object")

    assert cached is False
    command = runner.calls[2][0]
    assert "--skip-simplification" in command
    metadata = json.loads(
        (settings.asset_output_dir / f"{asset_id}.json").read_text(encoding="utf-8")
    )
    assert metadata["output_settings"]["mesh_simplification"] is False
    assert metadata["geometry"]["source_triangles"] == 120_000
    assert metadata["geometry"]["triangles"] == 120_000
    assert metadata["geometry"]["simplified"] is False
    assert metadata["pipeline_version"].endswith("-no-mesh-simplification")


def test_mesh_simplification_setting_changes_cache_identity(settings: Settings) -> None:
    enabled = AssetGenerator(settings)
    disabled = AssetGenerator(
        Settings(
            asset_output_dir=settings.asset_output_dir,
            model_cache_dir=settings.model_cache_dir,
            mesh_simplification=False,
        )
    )

    assert enabled.asset_id("same object") != disabled.asset_id("same object")


def test_texture_simplification_can_be_disabled(settings: Settings) -> None:
    configured_settings = Settings(
        asset_output_dir=settings.asset_output_dir,
        model_cache_dir=settings.model_cache_dir,
        texture_simplification=False,
    )
    runner = FakeRunner()
    service = AssetGenerator(
        configured_settings,
        runner=runner,
        capture_runner=lambda command, timeout, label: "{}",
        background_remover=FakeBackgroundRemover(),
    )
    service.ready = True

    asset_id, _, _ = service.generate("full resolution textures")

    command = runner.calls[2][0]
    assert "--skip-texture-simplification" in command
    assert service.asset_id("same object") != AssetGenerator(settings).asset_id(
        "same object"
    )
    metadata = json.loads(
        (settings.asset_output_dir / f"{asset_id}.json").read_text(encoding="utf-8")
    )
    assert metadata["output_settings"]["texture_simplification"] is False
    assert metadata["geometry"]["source_texture_bytes"] == 4_672_122
    assert metadata["geometry"]["texture_bytes"] == 4_672_122
    assert metadata["geometry"]["textures_resized"] is False
    assert metadata["geometry"]["max_texture_size"] is None
    assert metadata["geometry"]["texture_format"] == "source"
    assert metadata["output_settings"]["texture_resolution"] is None
    assert metadata["output_settings"]["texture_format"] == "source"


def test_simplification_error_changes_command_and_cache_identity(
    settings: Settings,
) -> None:
    configured_settings = Settings(
        asset_output_dir=settings.asset_output_dir,
        model_cache_dir=settings.model_cache_dir,
        simplification_error=0.0025,
    )
    runner = FakeRunner()
    configured = AssetGenerator(
        configured_settings,
        runner=runner,
        capture_runner=lambda command, timeout, label: "{}",
        background_remover=FakeBackgroundRemover(),
    )
    configured.ready = True

    asset_id, _, _ = configured.generate("configurable error")

    command = runner.calls[2][0]
    assert command[command.index("--error") + 1] == "0.0025"
    assert "--max-triangles" not in command
    assert configured.asset_id("same object") != AssetGenerator(settings).asset_id(
        "same object"
    )
    metadata = json.loads(
        (settings.asset_output_dir / f"{asset_id}.json").read_text(encoding="utf-8")
    )
    assert metadata["output_settings"]["simplification_error"] == 0.0025
    assert metadata["geometry"]["simplification_error"] == 0.0025


def test_max_texture_size_changes_command_metadata_and_cache_identity(
    settings: Settings,
) -> None:
    configured_settings = Settings(
        asset_output_dir=settings.asset_output_dir,
        model_cache_dir=settings.model_cache_dir,
        max_texture_size=1024,
    )
    runner = FakeRunner()
    configured = AssetGenerator(
        configured_settings,
        runner=runner,
        capture_runner=lambda command, timeout, label: "{}",
        background_remover=FakeBackgroundRemover(),
    )
    configured.ready = True

    asset_id, _, _ = configured.generate("configurable texture size")

    command = runner.calls[2][0]
    assert command[command.index("--max-texture-size") + 1] == "1024"
    assert configured.asset_id("same object") != AssetGenerator(settings).asset_id(
        "same object"
    )
    metadata = json.loads(
        (settings.asset_output_dir / f"{asset_id}.json").read_text(encoding="utf-8")
    )
    assert metadata["output_settings"]["texture_resolution"] == 1024
    assert metadata["geometry"]["max_texture_size"] == 1024


def test_max_texture_size_does_not_change_cache_when_textures_are_untouched(
    settings: Settings,
) -> None:
    first = AssetGenerator(
        Settings(texture_simplification=False, max_texture_size=512)
    )
    second = AssetGenerator(
        Settings(texture_simplification=False, max_texture_size=1024)
    )

    assert first.asset_id("same object") == second.asset_id("same object")


def test_audio_pipeline_is_sequential_and_private(
    tmp_path: Path,
    settings: Settings,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    audio = tmp_path / "spoken-description.bin"
    audio.write_bytes(b"private audio")

    result = service.generate_from_audio(audio)

    assert [call[3] for call in runner.calls] == [
        "Audio preprocessing",
        "Qwen3-ASR-1.7B",
        "Qwen3.5-4B",
        "FLUX.2 Klein",
        "TRELLIS.2",
        "glTF-Transform simplification",
    ]
    assert runner.prompts == [runner.enhanced_prompt + PROMPT_SUFFIX]
    commands = " ".join(part for call in runner.calls for part in call[0])
    assert runner.transcript not in commands
    assert runner.enhanced_prompt not in commands
    metadata_text = (settings.asset_output_dir / f"{result.asset_id}.json").read_text(
        encoding="utf-8"
    )
    metadata = json.loads(metadata_text)
    assert metadata["pipeline_version"] == AUDIO_PIPELINE_VERSION
    assert metadata["input_type"] == "audio"
    assert metadata["transcript"] == runner.transcript
    assert metadata["enhanced_prompt"] == runner.enhanced_prompt
    assert not list(tmp_path.glob("asset-generator-audio-*"))


def test_audio_pipeline_sanitizes_enhancer_output_before_flux(
    tmp_path: Path,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    runner.transcript = "A small red medieval chest with gold decorations."
    runner.enhanced_prompt = (
        "Isolated 3D medieval chest, small size, deep red wood, ornate gold inlay "
        "decorations, intricate carvings, glossy finish, studio lighting, neutral "
        "background."
    )
    audio = tmp_path / "sample.wav"
    audio.write_bytes(b"audio")

    result = service.generate_from_audio(audio)

    expected = "Medieval chest, small size, deep red wood, gold decorations"
    assert result.enhanced_prompt == expected
    assert runner.prompts == [expected + PROMPT_SUFFIX]


def test_audio_cache_reruns_text_stages_but_skips_asset_stages(
    tmp_path: Path,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    audio = tmp_path / "sample.wav"
    audio.write_bytes(b"audio")
    first = service.generate_from_audio(audio)
    runner.calls.clear()

    second = service.generate_from_audio(audio)

    assert second.asset_id == first.asset_id
    assert second.cached is True
    assert [call[3] for call in runner.calls] == [
        "Audio preprocessing",
        "Qwen3-ASR-1.7B",
        "Qwen3.5-4B",
    ]
    assert second.timings["text_to_image_ms"] == 0
    assert second.timings["reconstruction_ms"] == 0
    assert second.timings["simplification_ms"] == 0
    assert second.transcript == runner.transcript
    assert second.enhanced_prompt == runner.enhanced_prompt


def test_audio_identity_does_not_change_text_identity(
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    assert service.asset_id(runner.enhanced_prompt) != service.asset_id(
        runner.enhanced_prompt, pipeline_version=AUDIO_PIPELINE_VERSION
    )


def test_audio_helpers_use_separate_transformers_runtimes(
    tmp_path: Path,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, _ = generator
    base_python = tmp_path / "base-python"
    base_python.write_bytes(b"python")
    sidecar_python = service.settings.prompt_enhancer_python_path
    sidecar_python.parent.mkdir(parents=True)
    sidecar_python.symlink_to(base_python)
    asr = service._asr_command(tmp_path / "audio.wav", tmp_path / "asr.json")
    enhancer = service._prompt_enhancement_command(
        tmp_path / "asr.json", tmp_path / "prompt.txt"
    )

    assert asr[0] == sys.executable
    assert enhancer[0] == str(sidecar_python.absolute())
    assert enhancer[0] != str(sidecar_python.resolve())


@pytest.mark.parametrize(
    ("codec", "container"),
    [
        ("pcm_s16le", "wav"),
        ("mp3", "mp3"),
        ("flac", "flac"),
        ("vorbis", "ogg"),
        ("aac", "mov,mp4,m4a,3gp,3g2,mj2"),
        ("aac", "aac"),
    ],
)
def test_supported_audio_formats(codec: str, container: str) -> None:
    assert _supported_audio_format(codec, container)


@pytest.mark.parametrize(
    ("codec", "container"),
    [("opus", "ogg"), ("aac", "matroska"), ("pcm_s16le", "matroska")],
)
def test_unsupported_audio_formats(codec: str, container: str) -> None:
    assert not _supported_audio_format(codec, container)


def test_empty_transcript_is_rejected_and_lock_is_released(
    tmp_path: Path,
    settings: Settings,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    runner.transcript = "   "
    audio = tmp_path / "sample.wav"
    audio.write_bytes(b"audio")

    with pytest.raises(EmptyTranscriptError):
        service.generate_from_audio(audio)

    assert service.busy is False
    assert list(settings.asset_output_dir.iterdir()) == []
    assert not list(tmp_path.glob("asset-generator-audio-*"))


def test_invalid_enhanced_prompt_is_sanitized_and_cleans_up(
    tmp_path: Path,
    settings: Settings,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    runner.enhanced_prompt = "x" * 501
    audio = tmp_path / "sample.wav"
    audio.write_bytes(b"audio")

    with pytest.raises(GenerationError):
        service.generate_from_audio(audio)

    assert service.busy is False
    assert list(settings.asset_output_dir.iterdir()) == []


def test_preprocessing_restores_old_crop_scale_and_centering() -> None:
    source = Image.new("RGB", (200, 100), "white")
    remover = FakeBackgroundRemover()

    result = prepare_foreground(source, remover)

    assert result.mode == "RGBA"
    assert result.size == (768, 768)
    alpha_bbox = result.getchannel("A").getbbox()
    assert alpha_bbox is not None
    width = alpha_bbox[2] - alpha_bbox[0]
    height = alpha_bbox[3] - alpha_bbox[1]
    assert 650 <= width <= 654
    assert abs((alpha_bbox[0] + alpha_bbox[2]) / 2 - 384) <= 1
    assert abs((alpha_bbox[1] + alpha_bbox[3]) / 2 - 384) <= 1
    assert height < width


def test_constant_birefnet_prediction_produces_empty_mask() -> None:
    prediction = np.ones((1, 1, 4, 4), dtype=np.float32)

    mask = TorchBiRefNet._mask_from_prediction(prediction, (8, 6))

    assert mask.size == (8, 6)
    assert np.asarray(mask).max() == 0


def test_birefnet_load_suppresses_only_pinned_timm_deprecations(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    class FakeModel:
        def eval(self):
            return self

        def requires_grad_(self, value: bool):
            return self

        def to(self, **kwargs):
            return self

    class FakeAutoModel:
        @classmethod
        def from_pretrained(cls, *args, **kwargs):
            warnings.warn(
                "Importing from timm.models.layers is deprecated, "
                "please import via timm.layers",
                FutureWarning,
            )
            warnings.warn(
                "Importing from timm.models.registry is deprecated, "
                "please import via timm.models",
                FutureWarning,
            )
            warnings.warn("unrelated future warning", FutureWarning)
            return FakeModel()

    fake_torch = types.SimpleNamespace(
        cuda=types.SimpleNamespace(is_available=lambda: True),
        float16=object(),
        set_float32_matmul_precision=lambda value: None,
    )
    fake_transformers = types.SimpleNamespace(
        AutoModelForImageSegmentation=FakeAutoModel
    )
    monkeypatch.setitem(sys.modules, "torch", fake_torch)
    monkeypatch.setitem(sys.modules, "transformers", fake_transformers)
    model_path = tmp_path / "birefnet"
    model_path.mkdir()

    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        TorchBiRefNet(model_path).load()

    assert [str(item.message) for item in caught] == ["unrelated future warning"]


def test_success_creates_original_and_network_artifacts_without_storing_prompt(
    settings: Settings,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, _ = generator
    asset_id, _, timings = service.generate("a confidential object")

    glb = settings.asset_output_dir / f"{asset_id}.glb"
    original_glb = settings.asset_output_dir / f"{asset_id}.original.glb"
    image = settings.asset_output_dir / f"{asset_id}.png"
    metadata_path = settings.asset_output_dir / f"{asset_id}.json"
    assert glb.read_bytes().startswith(b"glTF")
    assert original_glb.read_bytes().startswith(b"glTF")
    assert image.read_bytes().startswith(PNG_SIGNATURE)
    with Image.open(image) as cutout:
        assert cutout.mode == "RGBA"
        assert cutout.size == (768, 768)
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    assert metadata["asset_id"] == asset_id
    assert "prompt_hash" in metadata
    assert metadata["generation_seed_version"] == GENERATION_SEED_VERSION
    assert "prompt" not in metadata
    assert "a confidential object" not in metadata_path.read_text(encoding="utf-8")
    assert metadata["timings"] == timings
    assert metadata["models"]["background_removal"]["id"] == "ZhengPeng7/BiRefNet"
    assert metadata["output_settings"]["background_removal_resolution"] == 1024
    assert metadata["output_settings"]["box_uv"] is False
    assert metadata["output_settings"]["texture_simplification"] is True
    assert "max_triangles" not in metadata["output_settings"]
    assert metadata["output_settings"]["texture_resolution"] == 512
    assert metadata["geometry"] == {
        "source_triangles": 120_000,
        "triangles": 18_750,
        "simplified": True,
        "simplification_error": 0.0001,
        "simplifier": "glTF-Transform",
        "simplifier_version": "4.4.2",
        "lock_border": True,
        "source_texture_bytes": 4_672_122,
        "texture_bytes": 437_615,
        "textures_resized": True,
        "max_texture_size": 512,
        "texture_format": "png",
    }
    assert metadata["artifacts"] == {
        "original_glb": {
            "filename": f"{asset_id}.original.glb",
            "bytes": original_glb.stat().st_size,
        },
        "network_glb": {
            "filename": f"{asset_id}.glb",
            "bytes": glb.stat().st_size,
        },
    }
    assert "memory" not in metadata


def test_complete_cache_skips_native_processes(
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    first = service.generate("cached object")
    runner.calls.clear()
    second = service.generate(" cached   object ")

    assert second[0] == first[0]
    assert second[1] is True
    assert second[2] == {
        "text_to_image_ms": 0,
        "reconstruction_ms": 0,
        "simplification_ms": 0,
        "total_ms": 0,
    }
    assert runner.calls == []


def test_partial_cache_is_removed_and_regenerated(
    settings: Settings,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    asset_id, _, _ = service.generate("partial object")
    (settings.asset_output_dir / f"{asset_id}.original.glb").unlink()
    runner.calls.clear()

    regenerated = service.generate("partial object")

    assert regenerated[1] is False
    assert [call[3] for call in runner.calls] == [
        "FLUX.2 Klein",
        "TRELLIS.2",
        "glTF-Transform simplification",
    ]
    assert (settings.asset_output_dir / f"{asset_id}.original.glb").is_file()


def test_failure_is_wrapped_and_cleans_artifacts(
    settings: Settings,
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    runner.fail = True

    with pytest.raises(GenerationError):
        service.generate("failed object")

    assert list(settings.asset_output_dir.iterdir()) == []


def test_busy_rejects_uncached_but_allows_cached(
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, _ = generator
    service.generate("cached object")
    assert service._lock.acquire(blocking=False)
    try:
        with pytest.raises(BusyError):
            service.generate("new object")
        assert service.generate("cached object")[1] is True
    finally:
        service._lock.release()


def test_concurrent_generation_holds_lock(
    generator: tuple[AssetGenerator, FakeRunner],
) -> None:
    service, runner = generator
    runner.started = threading.Event()
    runner.release = threading.Event()
    result: dict[str, object] = {}
    thread = threading.Thread(
        target=lambda: result.setdefault("value", service.generate("first object"))
    )
    thread.start()
    assert runner.started.wait(timeout=2)
    with pytest.raises(BusyError):
        service.generate("second object")
    runner.release.set()
    thread.join(timeout=2)
    assert not thread.is_alive()
    assert "value" in result


def test_invalid_native_artifact_is_rejected_and_cleaned(
    settings: Settings,
) -> None:
    def invalid_runner(command, environment, timeout, label):
        if label == "FLUX.2 Klein":
            Path(command[command.index("--output") + 1]).write_bytes(b"not-png")

    service = AssetGenerator(
        settings,
        runner=invalid_runner,
        background_remover=FakeBackgroundRemover(),
    )
    service.ready = True
    with pytest.raises(GenerationError):
        service.generate("invalid object")
    assert list(settings.asset_output_dir.iterdir()) == []


def test_native_timeout_is_sanitized(monkeypatch) -> None:
    def timeout(*args, **kwargs):
        raise subprocess.TimeoutExpired(args[0], 5)

    monkeypatch.setattr("app.pipeline.subprocess.run", timeout)
    with pytest.raises(RuntimeError, match="FLUX.2 Klein timed out after 5 seconds"):
        _run_native(["sd-cli"], {}, 5, "FLUX.2 Klein")


def test_native_failure_retains_only_diagnostic_tail(monkeypatch) -> None:
    completed = subprocess.CompletedProcess(
        ["trellis-cli"], returncode=7, stdout="native diagnostic"
    )
    monkeypatch.setattr(
        "app.pipeline.subprocess.run", lambda *args, **kwargs: completed
    )
    with pytest.raises(RuntimeError, match="native diagnostic"):
        _run_native(["trellis-cli"], {}, 10, "TRELLIS.2")
