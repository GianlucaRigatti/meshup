from __future__ import annotations

from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

from app.presets import PipelinePreset, resolve_profile


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    public_base_url: str | None = Field(default=None, alias="PUBLIC_BASE_URL")
    asset_output_dir: Path = Field(
        default=Path("generated_assets"), alias="ASSET_OUTPUT_DIR"
    )
    model_cache_dir: Path = Field(
        default=Path(".model_sources"), alias="MODEL_CACHE_DIR"
    )
    pipeline_profile: str = Field(default="auto", alias="PIPELINE_PROFILE")
    hunyuan_timeout_seconds: int = Field(default=300, alias="HUNYUAN_TIMEOUT_SECONDS")
    pixal3d_timeout_seconds: int = Field(default=1800, alias="PIXAL3D_TIMEOUT_SECONDS")
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")

    @property
    def preset(self) -> PipelinePreset:
        return resolve_profile(self.pipeline_profile)

    @property
    def image_model_path(self) -> Path:
        return self.model_cache_dir / "models" / self.preset.image.directory_name

    @property
    def asset_model_path(self) -> Path:
        return self.model_cache_dir / "models" / self.preset.asset.directory_name

    @property
    def sd_turbo_path(self) -> Path:
        return self.model_cache_dir / "models" / "sd-turbo"

    @property
    def hunyuan_model_path(self) -> Path:
        return self.model_cache_dir / "models" / "hunyuan3d-mlx-shape-small"

    @property
    def hunyuan_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "Hunyuan3D-Swift"

    @property
    def hunyuan_runtime_path(self) -> Path:
        return self.model_cache_dir / "runtime" / "hunyuan"

    @property
    def sf3d_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "stable-fast-3d"

    @property
    def dinov2_model_path(self) -> Path:
        return self.model_cache_dir / "models" / "dinov2-large"

    @property
    def pixal3d_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "Pixal3D"

    @property
    def trellis2_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "TRELLIS.2"

    @property
    def pixal3d_runtime_path(self) -> Path:
        return self.model_cache_dir / "runtime" / "pixal3d"

    @property
    def pixal3d_python_path(self) -> Path:
        return self.pixal3d_runtime_path / ".venv" / "bin" / "python"

    @property
    def pixal3d_required_files(self) -> tuple[Path, ...]:
        return (
            self.pixal3d_python_path,
            self.pixal3d_source_path / "inference.py",
            self.asset_model_path / "pipeline.json",
        )
