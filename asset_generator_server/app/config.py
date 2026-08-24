from __future__ import annotations

from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

from app.presets import (
    DEFAULT_IMAGE_GENERATOR,
    DEFAULT_MODEL_3D,
    PipelinePreset,
    resolve_models,
    resolve_profile,
)


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
    image_generator: str = Field(
        default=DEFAULT_IMAGE_GENERATOR, alias="IMAGE_GENERATOR"
    )
    model_3d: str = Field(default=DEFAULT_MODEL_3D, alias="MODEL_3D")
    # Deprecated internal compatibility switch. New launch/install CLIs do not
    # expose legacy platform profiles.
    pipeline_profile: str | None = Field(default=None, alias="PIPELINE_PROFILE")
    hunyuan_timeout_seconds: int = Field(default=300, alias="HUNYUAN_TIMEOUT_SECONDS")
    image_timeout_seconds: int = Field(default=600, alias="IMAGE_TIMEOUT_SECONDS")
    pixal3d_timeout_seconds: int = Field(default=1800, alias="PIXAL3D_TIMEOUT_SECONDS")
    trellis_timeout_seconds: int = Field(default=1800, alias="TRELLIS_TIMEOUT_SECONDS")
    instantmesh_timeout_seconds: int = Field(
        default=1800, alias="INSTANTMESH_TIMEOUT_SECONDS"
    )
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")

    @property
    def preset(self) -> PipelinePreset:
        if self.pipeline_profile is not None:
            return resolve_profile(self.pipeline_profile)
        return resolve_models(self.image_generator, self.model_3d)

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
    def trellis_cpp_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "trellis.cpp"

    @property
    def trellis_cpp_build_path(self) -> Path:
        return self.trellis_cpp_source_path / ".build"

    @property
    def trellis_cpp_executable_path(self) -> Path:
        return self.trellis_cpp_build_path / "trellis-cli"

    @property
    def trellis_cpp_model_path(self) -> Path:
        quantization = self.preset.asset.quantization
        if quantization not in {"q4", "q8"}:
            raise ValueError("The selected preset does not use TRELLIS.2 GGUF weights.")
        return self.asset_model_path / quantization

    @property
    def trellis_cpp_required_files(self) -> tuple[Path, ...]:
        models = (
            "birefnet.gguf",
            "dinov3.gguf",
            "ss_flow.gguf",
            "ss_dec.gguf",
            "shape_flow_512.gguf",
            "shape_flow_1024.gguf",
            "shape_dec.gguf",
            "tex_flow_512.gguf",
            "tex_flow_1024.gguf",
            "tex_dec.gguf",
        )
        return (
            self.trellis_cpp_executable_path,
            *(self.trellis_cpp_model_path / name for name in models),
        )

    @property
    def stable_diffusion_cpp_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "stable-diffusion.cpp"

    @property
    def stable_diffusion_cpp_build_path(self) -> Path:
        return self.stable_diffusion_cpp_source_path / ".build"

    @property
    def stable_diffusion_cpp_executable_path(self) -> Path:
        return self.stable_diffusion_cpp_build_path / "bin" / "sd-cli"

    @property
    def z_image_diffusion_path(self) -> Path:
        filenames = {
            "q3": "z_image_turbo-Q3_K.gguf",
            "q4": "z_image_turbo-Q4_K.gguf",
            "q6": "z_image_turbo-Q6_K.gguf",
        }
        try:
            filename = filenames[self.preset.image.quantization]
        except KeyError as exc:
            raise ValueError("The selected preset does not use Z-Image GGUF.") from exc
        return self.image_model_path / filename

    @property
    def z_image_components_path(self) -> Path:
        return self.model_cache_dir / "models" / "z-image-components"

    @property
    def z_image_text_encoder_path(self) -> Path:
        return (
            self.z_image_components_path
            / "Qwen3-4B-Instruct-2507-Q4_K_M.gguf"
        )

    @property
    def z_image_vae_path(self) -> Path:
        return self.z_image_components_path / "split_files" / "vae" / "ae.safetensors"

    @property
    def z_image_required_files(self) -> tuple[Path, ...]:
        return (
            self.stable_diffusion_cpp_executable_path,
            self.z_image_diffusion_path,
            self.z_image_text_encoder_path,
            self.z_image_vae_path,
        )

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

    @property
    def instantmesh_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "InstantMesh"

    @property
    def instantmesh_runtime_path(self) -> Path:
        return self.model_cache_dir / "runtime" / "instantmesh"

    @property
    def instantmesh_python_path(self) -> Path:
        return self.instantmesh_runtime_path / ".venv" / "bin" / "python"

    @property
    def zero123_model_path(self) -> Path:
        return self.model_cache_dir / "models" / "zero123plus-v1.2"

    @property
    def zero123_pipeline_path(self) -> Path:
        return self.model_cache_dir / "models" / "zero123plus-pipeline"

    @property
    def instantmesh_dino_path(self) -> Path:
        return self.model_cache_dir / "models" / "dino-vitb16"

    @property
    def instantmesh_required_files(self) -> tuple[Path, ...]:
        return (
            self.instantmesh_python_path,
            self.instantmesh_source_path / "configs" / "instant-mesh-base.yaml",
            self.asset_model_path / "diffusion_pytorch_model.bin",
            self.asset_model_path / "instant_mesh_base.ckpt",
            self.zero123_model_path / "model_index.json",
            self.zero123_model_path
            / "unet"
            / "diffusion_pytorch_model.safetensors",
            self.zero123_model_path
            / "vae"
            / "diffusion_pytorch_model.safetensors",
            self.zero123_model_path / "vision_encoder" / "model.safetensors",
            self.zero123_model_path / "text_encoder" / "model.safetensors",
            self.zero123_pipeline_path / "pipeline.py",
            self.instantmesh_dino_path / "config.json",
            self.instantmesh_dino_path / "preprocessor_config.json",
            self.instantmesh_dino_path / "pytorch_model.bin",
        )
