from __future__ import annotations

import asyncio
import logging
import mimetypes
import tempfile
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Annotated

from fastapi import FastAPI, File, Request, UploadFile
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, ConfigDict, Field, field_validator

from app.config import (
    BACKGROUND_REMOVAL_MODEL,
    DEVICE,
    IMAGE_GENERATOR,
    MODEL_3D,
    OUTPUT_MODE,
    PROMPT_ENHANCER_MODEL,
    SPEECH_TO_TEXT_MODEL,
    Settings,
)
from app.pipeline import (
    AssetGenerator,
    BusyError,
    EmptyTranscriptError,
    GenerationError,
    InvalidAudioError,
)


class GenerateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    prompt: str = Field(strict=True, max_length=500)

    @field_validator("prompt")
    @classmethod
    def normalize(cls, prompt: str) -> str:
        prompt = " ".join(prompt.split())
        if not prompt:
            raise ValueError("The prompt must contain text.")
        return prompt


def create_app(
    settings: Settings | None = None,
    generator: AssetGenerator | None = None,
) -> FastAPI:
    settings = settings or Settings.from_env()
    settings.asset_output_dir.mkdir(parents=True, exist_ok=True)
    provided_generator = generator

    logging.basicConfig(
        level=getattr(logging, settings.log_level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    mimetypes.add_type("model/gltf-binary", ".glb")

    @asynccontextmanager
    async def lifespan(application: FastAPI) -> AsyncIterator[None]:
        service = provided_generator or AssetGenerator(settings)
        application.state.generator = service
        if provided_generator is None:
            await asyncio.to_thread(service.load)
        yield

    application = FastAPI(title="Local 3D Asset Generator", lifespan=lifespan)
    application.mount(
        "/assets", StaticFiles(directory=settings.asset_output_dir), name="assets"
    )

    @application.get("/healthz")
    async def healthz():
        return {"status": "ok"}

    @application.get("/readyz")
    async def readyz(request: Request):
        service: AssetGenerator = request.app.state.generator
        content = {
            "status": "ready" if service.ready else "not_ready",
            "ready": service.ready,
            "busy": service.busy,
            "image_generator": IMAGE_GENERATOR,
            "model_3d": MODEL_3D,
            "background_removal_model": BACKGROUND_REMOVAL_MODEL,
            "speech_to_text_model": SPEECH_TO_TEXT_MODEL,
            "prompt_enhancer_model": PROMPT_ENHANCER_MODEL,
            "device": DEVICE,
            "output_mode": OUTPUT_MODE,
        }
        return content if service.ready else JSONResponse(content, status_code=503)

    @application.post("/generate_asset")
    async def generate_asset(payload: GenerateRequest, request: Request):
        service: AssetGenerator = request.app.state.generator
        if not service.ready:
            return _error(
                503, "generator_not_ready", "The asset generator is not ready."
            )
        try:
            result = await asyncio.to_thread(service.generate, payload.prompt)
        except BusyError:
            return _error(
                503, "generator_busy", "The asset generator is busy. Try again later."
            )
        except GenerationError:
            return _error(500, "generation_failed", "The asset generation failed.")

        base_url = settings.public_base_url or str(request.base_url)
        return {
            "url": f"{base_url.rstrip('/')}/assets/{result.asset_id}.glb",
            "asset_id": result.asset_id,
            "cached": result.cached,
            "image_generation_time_ms": result.timings["text_to_image_ms"],
            "model_generation_time_ms": result.timings["reconstruction_ms"],
            "generation_time_ms": result.timings["total_ms"],
        }

    @application.post("/generate_asset_from_audio")
    async def generate_asset_from_audio(
        request: Request, audio: Annotated[UploadFile, File()]
    ):
        service: AssetGenerator = request.app.state.generator
        if not service.ready:
            return _error(
                503, "generator_not_ready", "The asset generator is not ready."
            )
        if service.busy:
            return _error(
                503, "generator_busy", "The asset generator is busy. Try again later."
            )

        with tempfile.TemporaryDirectory(
            dir=settings.asset_output_dir.parent, prefix="asset-upload-"
        ) as temporary_dir:
            upload_path = Path(temporary_dir) / "upload.bin"
            size = 0
            try:
                with upload_path.open("wb") as destination:
                    while chunk := await audio.read(1024 * 1024):
                        size += len(chunk)
                        if size > settings.audio_max_bytes:
                            return _error(
                                413,
                                "audio_too_large",
                                f"Audio must be at most {settings.audio_max_bytes} bytes.",
                            )
                        destination.write(chunk)
            finally:
                await audio.close()
            if size == 0:
                return _error(422, "invalid_audio", "The audio upload is empty.")

            try:
                result = await asyncio.to_thread(
                    service.generate_from_audio, upload_path
                )
            except BusyError:
                return _error(
                    503,
                    "generator_busy",
                    "The asset generator is busy. Try again later.",
                )
            except InvalidAudioError as exc:
                status = {
                    "audio_too_large": 413,
                    "unsupported_audio_type": 415,
                }.get(exc.code, 422)
                return _error(status, exc.code, str(exc))
            except EmptyTranscriptError as exc:
                return _error(422, "empty_transcript", str(exc))
            except GenerationError:
                return _error(500, "generation_failed", "The asset generation failed.")

        base_url = settings.public_base_url or str(request.base_url)
        timings = result.timings
        return {
            "url": f"{base_url.rstrip('/')}/assets/{result.asset_id}.glb",
            "asset_id": result.asset_id,
            "cached": result.cached,
            "transcript": result.transcript,
            "transcript_language": result.transcript_language,
            "enhanced_prompt": result.enhanced_prompt,
            "audio_preprocessing_time_ms": timings["audio_preprocess_ms"],
            "transcription_time_ms": timings["transcription_ms"],
            "prompt_enhancement_time_ms": timings["prompt_enhancement_ms"],
            "image_generation_time_ms": timings["text_to_image_ms"],
            "model_generation_time_ms": timings["reconstruction_ms"],
            "generation_time_ms": timings["total_ms"],
        }

    return application


def _error(status: int, code: str, message: str) -> JSONResponse:
    return JSONResponse(
        status_code=status,
        content={"error": {"code": code, "message": message}},
    )


app = create_app()
