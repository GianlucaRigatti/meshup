from __future__ import annotations

import asyncio
import logging
import mimetypes
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, ConfigDict, Field, field_validator

from app.config import Settings
from app.generator import AssetGenerator, BusyError, GenerationError


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
    settings = settings or Settings()
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
        service = request.app.state.generator
        content = {
            "status": "ready" if service.ready else "not_ready",
            "ready": service.ready,
            "busy": service.busy,
            "configured_profile": settings.pipeline_profile,
            "profile": service.preset.name,
            "device": service.preset.device,
            "output_mode": service.output_mode,
        }
        return content if service.ready else JSONResponse(content, status_code=503)

    @application.post("/generate_asset")
    async def generate_asset(payload: GenerateRequest, request: Request):
        service = request.app.state.generator
        if not service.ready:
            return _error(
                503, "generator_not_ready", "The asset generator is not ready."
            )
        try:
            asset_id, cached, timings = await asyncio.to_thread(
                service.generate, payload.prompt
            )
        except BusyError:
            return _error(
                503, "generator_busy", "The asset generator is busy. Try again later."
            )
        except GenerationError:
            return _error(500, "generation_failed", "The asset generation failed.")

        base_url = settings.public_base_url or str(request.base_url)
        return {
            "url": f"{base_url.rstrip('/')}/assets/{asset_id}.glb",
            "asset_id": asset_id,
            "cached": cached,
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
