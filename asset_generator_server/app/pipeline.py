from __future__ import annotations

import hashlib
import json
import logging
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

from PIL import Image

from app.background_removal import TorchBiRefNet, prepare_foreground
from app.config import (
    ASR_MODEL_ID,
    ASR_MODEL_REVISION,
    AUDIO_PIPELINE_SCHEMA_VERSION,
    AUDIO_PIPELINE_VERSION,
    BACKGROUND_REMOVAL_MODEL,
    BIREFNET_MODEL_ID,
    BIREFNET_MODEL_REVISION,
    DEVICE,
    FLUX_MODEL_ID,
    FLUX_MODEL_REVISION,
    FLUX_VAE_MODEL_ID,
    FLUX_VAE_REVISION,
    GENERATION_SEED_VERSION,
    GLTF_TRANSFORM_VERSION,
    IMAGE_GENERATOR,
    MODEL_3D,
    MAX_ASSET_TRIANGLES,
    MAX_ASSET_TEXTURE_SIZE,
    OUTPUT_MODE,
    PIPELINE_SCHEMA_VERSION,
    PIPELINE_VERSION,
    PROMPT_ENHANCER_MODEL_ID,
    PROMPT_ENHANCER_MODEL_REVISION,
    PROMPT_ENHANCER_TRANSFORMERS_VERSION,
    PROMPT_SUFFIX,
    QWEN_MODEL_ID,
    QWEN_MODEL_REVISION,
    SIMPLIFICATION_ERROR,
    STABLE_DIFFUSION_CPP_REVISION,
    TRELLIS_CPP_REVISION,
    TRELLIS_MODEL_ID,
    TRELLIS_MODEL_REVISION,
    Settings,
    is_wsl,
)
from app.run_prompt_enhancer import sanitize_subject_prompt

LOGGER = logging.getLogger(__name__)
PROJECT_ROOT = Path(__file__).resolve().parents[1]
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
CommandRunner = Callable[[list[str], dict[str, str], int, str], None]
CaptureRunner = Callable[[list[str], int, str], str]


class BusyError(RuntimeError):
    pass


class GenerationError(RuntimeError):
    pass


class InvalidAudioError(ValueError):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


class EmptyTranscriptError(ValueError):
    pass


@dataclass(frozen=True)
class GenerationResult:
    asset_id: str
    cached: bool
    timings: dict[str, int]

    def __iter__(self):
        return iter((self.asset_id, self.cached, self.timings))

    def __getitem__(self, index: int):
        return (self.asset_id, self.cached, self.timings)[index]


@dataclass(frozen=True)
class AudioGenerationResult(GenerationResult):
    transcript: str
    transcript_language: str
    enhanced_prompt: str


class AssetGenerator:
    def __init__(
        self,
        settings: Settings,
        *,
        runner: CommandRunner | None = None,
        capture_runner: CaptureRunner | None = None,
        background_remover: TorchBiRefNet | None = None,
        pipeline_version: str = PIPELINE_VERSION,
    ) -> None:
        self.settings = settings
        self.output_dir = settings.asset_output_dir
        self.pipeline_version = pipeline_version
        self._runner = runner or _run_native
        self._capture_runner = capture_runner or _run_capture
        self._background_remover = background_remover
        self._lock = threading.Lock()
        self.ready = False
        self.load_error: str | None = None

    @property
    def busy(self) -> bool:
        return self._lock.locked()

    @property
    def output_mode(self) -> str:
        return OUTPUT_MODE

    def load(self) -> None:
        self.output_dir.mkdir(parents=True, exist_ok=True)
        try:
            self._validate_installation()
            if self._background_remover is None:
                self._background_remover = TorchBiRefNet(
                    self.settings.background_removal_model_path
                )
            self._background_remover.load()
        except Exception as exc:
            self.ready = False
            self.load_error = str(exc)
            LOGGER.exception("The asset generator is not ready.")
            return
        self.ready = True
        self.load_error = None
        LOGGER.info("Asset generator ready: image=%s 3d=%s", IMAGE_GENERATOR, MODEL_3D)

    def generate(self, prompt: str) -> GenerationResult:
        if not self.ready:
            raise RuntimeError("The asset generator is not ready.")

        normalized_prompt = " ".join(prompt.split())
        asset_id = self.asset_id(normalized_prompt)
        if self._cache_exists(asset_id):
            return GenerationResult(asset_id, True, _cached_timings())
        if not self._lock.acquire(blocking=False):
            if self._cache_exists(asset_id):
                return GenerationResult(asset_id, True, _cached_timings())
            raise BusyError

        started = time.perf_counter()
        try:
            if self._cache_exists(asset_id):
                return GenerationResult(asset_id, True, _cached_timings())
            return self._generate_locked(
                normalized_prompt,
                asset_id=asset_id,
                pipeline_version=self.pipeline_version,
                schema_version=PIPELINE_SCHEMA_VERSION,
                started=started,
                timings={},
            )
        except Exception as exc:
            LOGGER.exception("Asset generation failed for asset %s", asset_id)
            self._remove_artifacts(asset_id)
            raise GenerationError from exc
        finally:
            self._lock.release()

    def generate_from_audio(self, audio_path: Path) -> AudioGenerationResult:
        if not self.ready:
            raise RuntimeError("The asset generator is not ready.")
        if not self._lock.acquire(blocking=False):
            raise BusyError

        started = time.perf_counter()
        asset_id: str | None = None
        try:
            if audio_path.stat().st_size > self.settings.audio_max_bytes:
                raise InvalidAudioError(
                    "audio_too_large",
                    f"Audio must be at most {self.settings.audio_max_bytes} bytes.",
                )
            timings: dict[str, int] = {}
            with tempfile.TemporaryDirectory(
                dir=self.output_dir.parent, prefix="asset-generator-audio-"
            ) as temporary_dir:
                temporary = Path(temporary_dir)
                normalized_audio = temporary / "audio.wav"
                transcript_path = temporary / "transcript.json"
                enhanced_prompt_path = temporary / "enhanced-prompt.txt"

                stage = time.perf_counter()
                self._prepare_audio(audio_path, normalized_audio)
                timings["audio_preprocess_ms"] = _elapsed_ms(stage)

                stage = time.perf_counter()
                self._runner(
                    self._asr_command(normalized_audio, transcript_path),
                    _python_environment(),
                    self.settings.asr_timeout_seconds,
                    "Qwen3-ASR-1.7B",
                )
                transcript, language = _read_transcript(transcript_path)
                timings["transcription_ms"] = _elapsed_ms(stage)

                stage = time.perf_counter()
                self._runner(
                    self._prompt_enhancement_command(
                        transcript_path, enhanced_prompt_path
                    ),
                    _python_environment(),
                    self.settings.prompt_enhancement_timeout_seconds,
                    "Qwen3.5-4B",
                )
                enhanced_prompt = _read_enhanced_prompt(
                    enhanced_prompt_path, transcript
                )
                timings["prompt_enhancement_ms"] = _elapsed_ms(stage)

                asset_id = self.asset_id(
                    enhanced_prompt, pipeline_version=AUDIO_PIPELINE_VERSION
                )
                metadata_extra = {
                    "input_type": "audio",
                    "transcript": transcript,
                    "transcript_hash": hashlib.sha256(transcript.encode()).hexdigest(),
                    "enhanced_prompt": enhanced_prompt,
                    "enhanced_prompt_hash": hashlib.sha256(
                        enhanced_prompt.encode()
                    ).hexdigest(),
                    "transcript_language": language,
                    "audio_settings": {
                        "sample_rate": 16000,
                        "channels": 1,
                        "sample_format": "s16",
                        "max_duration_seconds": self.settings.audio_max_duration_seconds,
                    },
                    "audio_models": {
                        "speech_to_text": {
                            "id": ASR_MODEL_ID,
                            "revision": ASR_MODEL_REVISION,
                        },
                        "prompt_enhancer": {
                            "id": PROMPT_ENHANCER_MODEL_ID,
                            "revision": PROMPT_ENHANCER_MODEL_REVISION,
                        },
                    },
                }
                if self._cache_exists(asset_id):
                    timings.update(_cached_downstream_timings())
                    timings["total_ms"] = _elapsed_ms(started)
                    generated = GenerationResult(asset_id, True, timings)
                else:
                    generated = self._generate_locked(
                        enhanced_prompt,
                        asset_id=asset_id,
                        pipeline_version=AUDIO_PIPELINE_VERSION,
                        schema_version=AUDIO_PIPELINE_SCHEMA_VERSION,
                        started=started,
                        timings=timings,
                        metadata_extra=metadata_extra,
                    )
            return AudioGenerationResult(
                asset_id=generated.asset_id,
                cached=generated.cached,
                timings=generated.timings,
                transcript=transcript,
                transcript_language=language,
                enhanced_prompt=enhanced_prompt,
            )
        except (InvalidAudioError, EmptyTranscriptError):
            raise
        except Exception as exc:
            LOGGER.exception("Audio asset generation failed.")
            if asset_id is not None:
                self._remove_artifacts(asset_id)
            raise GenerationError from exc
        finally:
            self._lock.release()

    def _generate_locked(
        self,
        prompt: str,
        *,
        asset_id: str,
        pipeline_version: str,
        schema_version: int,
        started: float,
        timings: dict[str, int],
        metadata_extra: dict | None = None,
    ) -> GenerationResult:
        self._remove_artifacts(asset_id)
        seed = self.generation_seed(prompt)
        with tempfile.TemporaryDirectory(
            dir=self.output_dir.parent, prefix="asset-generator-"
        ) as temporary_dir:
            temporary = Path(temporary_dir)
            prompt_path = temporary / "prompt.txt"
            source_image = temporary / "source.png"
            temporary_glb = temporary / "asset.glb"
            simplified_glb = temporary / "asset-simplified.glb"
            simplification_stats = temporary / "simplification.json"
            cutout_image = temporary / "input.png"
            prompt_path.write_text(prompt + PROMPT_SUFFIX, encoding="utf-8")

            stage = time.perf_counter()
            self._runner(
                self._flux_command(prompt_path, source_image, seed),
                _runtime_environment(
                    self.settings.stable_diffusion_executable_path.parent
                ),
                self.settings.image_timeout_seconds,
                "FLUX.2 Klein",
            )
            _require_signature(source_image, PNG_SIGNATURE, "FLUX output PNG")
            timings["text_to_image_ms"] = _elapsed_ms(stage)

            stage = time.perf_counter()
            if self._background_remover is None:
                raise RuntimeError("BiRefNet has not been loaded.")
            with Image.open(source_image) as generated:
                cutout = prepare_foreground(
                    generated.convert("RGB"), self._background_remover
                )
            cutout.save(cutout_image, format="PNG")
            _require_signature(cutout_image, PNG_SIGNATURE, "BiRefNet cutout PNG")
            timings["preprocess_ms"] = _elapsed_ms(stage)
            timings.update(self._background_remover.last_timings)

            stage = time.perf_counter()
            self._runner(
                self._trellis_command(cutout_image, temporary_glb, seed),
                _runtime_environment(self.settings.trellis_build_path),
                self.settings.trellis_timeout_seconds,
                "TRELLIS.2",
            )
            _require_signature(temporary_glb, b"glTF", "TRELLIS output GLB")
            timings["reconstruction_ms"] = _elapsed_ms(stage)

            stage = time.perf_counter()
            self._runner(
                self._simplification_command(
                    temporary_glb, simplified_glb, simplification_stats
                ),
                dict(os.environ),
                self.settings.gltf_transform_timeout_seconds,
                "glTF-Transform simplification",
            )
            _require_signature(
                simplified_glb, b"glTF", "glTF-Transform simplified GLB"
            )
            mesh_stats = _read_simplification_stats(simplification_stats)
            timings["simplification_ms"] = _elapsed_ms(stage)
            timings["total_ms"] = _elapsed_ms(started)

            artifact_stats = {
                "original_glb": {
                    "filename": f"{asset_id}.original.glb",
                    "bytes": temporary_glb.stat().st_size,
                },
                "network_glb": {
                    "filename": f"{asset_id}.glb",
                    "bytes": simplified_glb.stat().st_size,
                },
            }

            os.replace(temporary_glb, self._original_asset_path(asset_id))
            os.replace(simplified_glb, self._asset_path(asset_id))
            os.replace(cutout_image, self._image_path(asset_id))
            self._write_metadata(
                self._metadata_path(asset_id),
                self._metadata(
                    asset_id,
                    prompt,
                    seed,
                    timings,
                    pipeline_version=pipeline_version,
                    schema_version=schema_version,
                    extra=metadata_extra,
                    mesh_stats=mesh_stats,
                    artifact_stats=artifact_stats,
                ),
            )
        return GenerationResult(asset_id, False, timings)

    def asset_id(self, prompt: str, *, pipeline_version: str | None = None) -> str:
        normalized = " ".join(prompt.split())
        return hashlib.sha256(
            f"{pipeline_version or self.pipeline_version}\0{normalized}".encode()
        ).hexdigest()[:32]

    @staticmethod
    def generation_seed(prompt: str) -> int:
        normalized = " ".join(prompt.split())
        legacy_asset_id = hashlib.sha256(
            f"{GENERATION_SEED_VERSION}\0{normalized}".encode()
        ).hexdigest()[:32]
        return int(legacy_asset_id[:16], 16) % (2**31)

    def _validate_installation(self) -> None:
        if not is_wsl():
            raise RuntimeError("The asset generator requires WSL 2.")
        missing_tools = [
            tool for tool in ("ffmpeg", "ffprobe") if shutil.which(tool) is None
        ]
        if missing_tools:
            raise RuntimeError(
                "Missing required audio tools: " + ", ".join(missing_tools)
            )
        missing = [
            path.resolve()
            for path in self.settings.required_files
            if not path.is_file()
        ]
        if missing:
            raise FileNotFoundError(
                "The fixed model installation is incomplete. Missing files:\n- "
                + "\n- ".join(str(path) for path in missing)
                + "\nRun `uv run python scripts/install_models.py --accept-licenses`."
            )
        if not self.settings.background_removal_model_path.is_dir():
            raise FileNotFoundError(
                "The BiRefNet checkpoint is missing. Run the model installer."
            )
        _require_git_revision(
            self.settings.stable_diffusion_source_path,
            STABLE_DIFFUSION_CPP_REVISION,
        )
        _require_git_revision(self.settings.trellis_source_path, TRELLIS_CPP_REVISION)
        _require_marker(
            self.settings.flux_model_path / ".model-revision", FLUX_MODEL_REVISION
        )
        _require_marker(
            self.settings.flux_model_path / ".text-encoder-revision",
            QWEN_MODEL_REVISION,
        )
        _require_marker(
            self.settings.flux_model_path / ".vae-revision", FLUX_VAE_REVISION
        )
        _require_marker(
            self.settings.trellis_model_root / ".model-revision",
            TRELLIS_MODEL_REVISION,
        )
        _require_marker(
            self.settings.background_removal_model_path / ".model-revision",
            BIREFNET_MODEL_REVISION,
        )
        _require_marker(
            self.settings.asr_model_path / ".model-revision", ASR_MODEL_REVISION
        )
        _require_marker(
            self.settings.prompt_enhancer_model_path / ".model-revision",
            PROMPT_ENHANCER_MODEL_REVISION,
        )
        _require_marker(
            self.settings.prompt_enhancer_runtime_path / ".transformers-version",
            PROMPT_ENHANCER_TRANSFORMERS_VERSION,
        )
        _check_help(
            self.settings.stable_diffusion_executable_path,
            _runtime_environment(self.settings.stable_diffusion_executable_path.parent),
        )
        _check_help(
            self.settings.trellis_executable_path,
            _runtime_environment(self.settings.trellis_build_path),
        )
        _run_native(
            [
                "node",
                str((PROJECT_ROOT / "scripts" / "simplify_glb.mjs").resolve()),
                "--help",
            ],
            dict(os.environ),
            60,
            "glTF-Transform",
        )
        _check_prompt_runtime(self.settings.prompt_enhancer_python_path)

    def _prepare_audio(self, source: Path, output: Path) -> None:
        try:
            probe = self._capture_runner(
                [
                    "ffprobe",
                    "-v",
                    "error",
                    "-select_streams",
                    "a:0",
                    "-show_entries",
                    "stream=codec_name,duration:format=duration,format_name",
                    "-of",
                    "json",
                    str(source.resolve()),
                ],
                self.settings.audio_decode_timeout_seconds,
                "audio probe",
            )
        except RuntimeError as exc:
            raise InvalidAudioError(
                "invalid_audio", "The uploaded file is not valid audio."
            ) from exc
        codec, container, duration = _parse_audio_probe(probe)
        if not _supported_audio_format(codec, container):
            raise InvalidAudioError(
                "unsupported_audio_type", "The uploaded audio type is not supported."
            )
        if duration > self.settings.audio_max_duration_seconds:
            raise InvalidAudioError(
                "audio_too_long",
                f"Audio must be at most {self.settings.audio_max_duration_seconds} seconds.",
            )
        try:
            self._runner(
                [
                    "ffmpeg",
                    "-v",
                    "error",
                    "-nostdin",
                    "-y",
                    "-i",
                    str(source.resolve()),
                    "-map",
                    "0:a:0",
                    "-ac",
                    "1",
                    "-ar",
                    "16000",
                    "-c:a",
                    "pcm_s16le",
                    str(output.resolve()),
                ],
                dict(os.environ),
                self.settings.audio_decode_timeout_seconds,
                "Audio preprocessing",
            )
            _require_signature(output, b"RIFF", "normalized audio WAV")
        except RuntimeError as exc:
            raise InvalidAudioError(
                "invalid_audio", "The uploaded file is not valid audio."
            ) from exc

    def _asr_command(self, audio: Path, output: Path) -> list[str]:
        return [
            sys.executable,
            "-m",
            "app.run_asr",
            "--model",
            str(self.settings.asr_model_path.resolve()),
            "--audio",
            str(audio.resolve()),
            "--output",
            str(output.resolve()),
        ]

    def _prompt_enhancement_command(self, transcript: Path, output: Path) -> list[str]:
        return [
            str(self.settings.prompt_enhancer_python_path.absolute()),
            "-m",
            "app.run_prompt_enhancer",
            "--model",
            str(self.settings.prompt_enhancer_model_path.resolve()),
            "--transcript",
            str(transcript.resolve()),
            "--output",
            str(output.resolve()),
        ]

    def _flux_command(self, prompt: Path, output: Path, seed: int) -> list[str]:
        return [
            str(self.settings.stable_diffusion_executable_path.resolve()),
            "--diffusion-model",
            str(self.settings.flux_diffusion_path.resolve()),
            "--llm",
            str(self.settings.flux_text_encoder_path.resolve()),
            "--vae",
            str(self.settings.flux_vae_path.resolve()),
            "--prompt-file",
            str(prompt.resolve()),
            "--output",
            str(output.resolve()),
            "--steps",
            "4",
            "--cfg-scale",
            "1.0",
            "--sampling-method",
            "euler",
            "--width",
            "768",
            "--height",
            "768",
            "--seed",
            str(seed),
            "--rng",
            "cpu",
            "--diffusion-fa",
            "--offload-to-cpu",
            "--auto-fit",
            "--max-vram",
            "cuda0=11",
        ]

    def _trellis_command(self, image: Path, output: Path, seed: int) -> list[str]:
        return [
            str(self.settings.trellis_executable_path.resolve()),
            str(image.resolve()),
            str(output.resolve()),
            "--models",
            str(self.settings.trellis_model_path.resolve()),
            "--gpu",
            "0",
            "--seed",
            str(seed),
            "--res",
            "512",
            "--max-tokens",
            "49152",
            "--atlas",
            "1024",
            "--webp",
            "off",
            "--require-gpu",
        ]

    def _simplification_command(
        self, source: Path, output: Path, stats: Path
    ) -> list[str]:
        return [
            "node",
            str((PROJECT_ROOT / "scripts" / "simplify_glb.mjs").resolve()),
            "--input",
            str(source.resolve()),
            "--output",
            str(output.resolve()),
            "--stats",
            str(stats.resolve()),
            "--max-triangles",
            str(MAX_ASSET_TRIANGLES),
            "--max-texture-size",
            str(MAX_ASSET_TEXTURE_SIZE),
            "--error",
            str(SIMPLIFICATION_ERROR),
        ]

    def _cache_exists(self, asset_id: str) -> bool:
        return all(
            path.is_file()
            for path in (
                self._asset_path(asset_id),
                self._original_asset_path(asset_id),
                self._image_path(asset_id),
                self._metadata_path(asset_id),
            )
        )

    def _remove_artifacts(self, asset_id: str) -> None:
        for path in (
            self._asset_path(asset_id),
            self._original_asset_path(asset_id),
            self._image_path(asset_id),
            self._metadata_path(asset_id),
            self._metadata_path(asset_id).with_suffix(".json.tmp"),
        ):
            path.unlink(missing_ok=True)

    def _asset_path(self, asset_id: str) -> Path:
        return self.output_dir / f"{asset_id}.glb"

    def _original_asset_path(self, asset_id: str) -> Path:
        return self.output_dir / f"{asset_id}.original.glb"

    def _image_path(self, asset_id: str) -> Path:
        return self.output_dir / f"{asset_id}.png"

    def _metadata_path(self, asset_id: str) -> Path:
        return self.output_dir / f"{asset_id}.json"

    def _metadata(
        self,
        asset_id: str,
        prompt: str,
        seed: int,
        timings: dict[str, int],
        *,
        pipeline_version: str,
        schema_version: int,
        extra: dict | None = None,
        mesh_stats: dict | None = None,
        artifact_stats: dict | None = None,
    ) -> dict:
        metadata = {
            "schema_version": schema_version,
            "pipeline_version": pipeline_version,
            "asset_id": asset_id,
            "prompt_hash": hashlib.sha256(prompt.encode()).hexdigest(),
            "seed": seed,
            "generation_seed_version": GENERATION_SEED_VERSION,
            "image_generator": IMAGE_GENERATOR,
            "model_3d": MODEL_3D,
            "background_removal_model": BACKGROUND_REMOVAL_MODEL,
            "models": {
                "image": {"id": FLUX_MODEL_ID, "revision": FLUX_MODEL_REVISION},
                "text_encoder": {
                    "id": QWEN_MODEL_ID,
                    "revision": QWEN_MODEL_REVISION,
                },
                "vae": {
                    "id": FLUX_VAE_MODEL_ID,
                    "revision": FLUX_VAE_REVISION,
                },
                "asset": {
                    "id": TRELLIS_MODEL_ID,
                    "revision": TRELLIS_MODEL_REVISION,
                },
                "background_removal": {
                    "id": BIREFNET_MODEL_ID,
                    "revision": BIREFNET_MODEL_REVISION,
                },
            },
            "runtimes": {
                "stable_diffusion_cpp": STABLE_DIFFUSION_CPP_REVISION,
                "trellis_cpp": TRELLIS_CPP_REVISION,
            },
            "device": DEVICE,
            "output_mode": OUTPUT_MODE,
            "output_settings": {
                "image_resolution": [768, 768],
                "background_removal_resolution": 1024,
                "foreground_ratio": 435 / 512,
                "geometry_resolution": 512,
                "reconstruction_texture_resolution": 1024,
                "texture_resolution": MAX_ASSET_TEXTURE_SIZE,
                "texture_format": "png",
                "box_uv": False,
                "max_triangles": MAX_ASSET_TRIANGLES,
                "simplification_error": SIMPLIFICATION_ERROR,
            },
            "geometry": mesh_stats,
            "artifacts": artifact_stats,
            "created_at": datetime.now(UTC).isoformat(),
            "timings": timings,
        }
        if extra:
            metadata.update(extra)
        return metadata

    @staticmethod
    def _write_metadata(path: Path, metadata: dict) -> None:
        temporary = path.with_suffix(".json.tmp")
        temporary.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        os.replace(temporary, path)


def _run_native(
    command: list[str], environment: dict[str, str], timeout: int, label: str
) -> None:
    try:
        completed = subprocess.run(
            command,
            env=environment,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=timeout,
        )
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"{label} timed out after {timeout} seconds.") from exc
    if completed.returncode != 0:
        tail = completed.stdout[-4000:].strip()
        message = f"{label} failed with exit code {completed.returncode}."
        if tail:
            message += f"\n{tail}"
        raise RuntimeError(message)


def _run_capture(command: list[str], timeout: int, label: str) -> str:
    try:
        completed = subprocess.run(
            command,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=timeout,
        )
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"{label} timed out after {timeout} seconds.") from exc
    if completed.returncode != 0:
        raise RuntimeError(f"{label} failed with exit code {completed.returncode}.")
    return completed.stdout


def _runtime_environment(binary_dir: Path) -> dict[str, str]:
    binary_dir = binary_dir.resolve()
    existing = os.environ.get("LD_LIBRARY_PATH")
    library_path = str(binary_dir) if not existing else f"{binary_dir}:{existing}"
    return {**os.environ, "LD_LIBRARY_PATH": library_path}


def _python_environment() -> dict[str, str]:
    existing_pythonpath = os.environ.get("PYTHONPATH")
    pythonpath = str(PROJECT_ROOT)
    if existing_pythonpath:
        pythonpath += f":{existing_pythonpath}"
    return {
        **os.environ,
        "HF_HUB_OFFLINE": "1",
        "TRANSFORMERS_OFFLINE": "1",
        "PYTHONPATH": pythonpath,
    }


def _check_help(executable: Path, environment: dict[str, str]) -> None:
    _run_native([str(executable.resolve()), "--help"], environment, 60, executable.name)


def _check_prompt_runtime(python: Path) -> None:
    code = (
        "import torch, transformers; "
        f"assert transformers.__version__ == '{PROMPT_ENHANCER_TRANSFORMERS_VERSION}'; "
        "from transformers import AutoModelForMultimodalLM, AutoProcessor"
    )
    _run_native(
        [str(python.absolute()), "-c", code],
        _python_environment(),
        60,
        "Qwen3.5 Python runtime",
    )


def _require_git_revision(source: Path, expected: str) -> None:
    completed = subprocess.run(
        ["git", "-C", str(source.resolve()), "rev-parse", "HEAD"],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        timeout=30,
    )
    actual = completed.stdout.strip() if completed.returncode == 0 else None
    if actual != expected:
        raise RuntimeError(
            f"Runtime revision mismatch at {source}: expected {expected}, got {actual!r}."
        )


def _require_marker(path: Path, expected: str) -> None:
    actual = path.read_text(encoding="utf-8").strip() if path.is_file() else None
    if actual != expected:
        raise RuntimeError(
            f"Model revision mismatch at {path.parent}: expected {expected}, got {actual!r}."
        )


def _require_signature(path: Path, signature: bytes, label: str) -> None:
    if not path.is_file():
        raise RuntimeError(f"{label} is missing or invalid.")
    with path.open("rb") as artifact:
        actual = artifact.read(len(signature))
    if actual != signature:
        raise RuntimeError(f"{label} is missing or invalid.")


def _cached_timings() -> dict[str, int]:
    return {
        "text_to_image_ms": 0,
        "reconstruction_ms": 0,
        "simplification_ms": 0,
        "total_ms": 0,
    }


def _cached_downstream_timings() -> dict[str, int]:
    return {
        "text_to_image_ms": 0,
        "reconstruction_ms": 0,
        "simplification_ms": 0,
    }


def _read_simplification_stats(path: Path) -> dict:
    try:
        stats = json.loads(path.read_text(encoding="utf-8"))
        source = int(stats["source_triangles"])
        output = int(stats["output_triangles"])
        simplified = stats["simplified"]
        source_texture_bytes = int(stats["source_texture_bytes"])
        output_texture_bytes = int(stats["output_texture_bytes"])
        textures_resized = stats["textures_resized"]
    except (OSError, KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
        raise RuntimeError("glTF-Transform produced invalid statistics.") from exc
    if (
        source < 0
        or output < 0
        or output > source
        or not isinstance(simplified, bool)
        or simplified != (output < source)
        or source_texture_bytes < 0
        or output_texture_bytes < 0
        or not isinstance(textures_resized, bool)
    ):
        raise RuntimeError("glTF-Transform produced invalid statistics.")
    return {
        "source_triangles": source,
        "triangles": output,
        "simplified": simplified,
        "max_triangles": MAX_ASSET_TRIANGLES,
        "simplifier": "glTF-Transform",
        "simplifier_version": GLTF_TRANSFORM_VERSION,
        "source_texture_bytes": source_texture_bytes,
        "texture_bytes": output_texture_bytes,
        "textures_resized": textures_resized,
        "max_texture_size": MAX_ASSET_TEXTURE_SIZE,
        "texture_format": "png",
    }


def _parse_audio_probe(payload: str) -> tuple[str, str, float]:
    try:
        data = json.loads(payload)
        streams = data.get("streams") or []
        stream = streams[0]
        codec = str(stream["codec_name"]).lower()
        format_data = data.get("format") or {}
        container = str(format_data["format_name"]).lower()
        duration_value = stream.get("duration") or format_data.get("duration")
        duration = float(duration_value)
    except (IndexError, KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
        raise InvalidAudioError(
            "invalid_audio", "The uploaded file is not valid audio."
        ) from exc
    if not codec or not container or duration <= 0:
        raise InvalidAudioError(
            "invalid_audio", "The uploaded file is not valid audio."
        )
    return codec, container, duration


def _supported_audio_format(codec: str, container: str) -> bool:
    containers = set(container.split(","))
    return any(
        (
            codec.startswith("pcm_") and "wav" in containers,
            codec == "mp3" and "mp3" in containers,
            codec == "flac" and "flac" in containers,
            codec == "vorbis" and "ogg" in containers,
            codec == "aac" and bool(containers & {"mov", "mp4", "m4a", "aac"}),
        )
    )


def _read_transcript(path: Path) -> tuple[str, str]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
        transcript = " ".join(str(payload["text"]).split())
        language = " ".join(str(payload["language"]).split()) or "Unknown"
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as exc:
        raise RuntimeError("Qwen3-ASR produced invalid output.") from exc
    if not transcript:
        raise EmptyTranscriptError("No speech was recognized in the audio sample.")
    if len(transcript) > 4000:
        raise RuntimeError("Qwen3-ASR transcript exceeded the safety limit.")
    return transcript, language


def _read_enhanced_prompt(path: Path, transcript: str) -> str:
    try:
        prompt = sanitize_subject_prompt(path.read_text(encoding="utf-8"), transcript)
    except (OSError, ValueError) as exc:
        raise RuntimeError("Qwen3.5 produced invalid output.") from exc
    if not prompt or len(prompt) > 500:
        raise RuntimeError("Qwen3.5 produced an invalid prompt.")
    return prompt


def _elapsed_ms(started: float) -> int:
    return max(0, round((time.perf_counter() - started) * 1000))
