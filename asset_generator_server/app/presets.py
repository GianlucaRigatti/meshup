from __future__ import annotations

import platform
import sys
from dataclasses import dataclass
from typing import Literal, TypeAlias

ProfileName: TypeAlias = Literal[
    "auto",
    "macos-mlx",
    "windows-cuda-quality",
    "windows-cuda-fast",
    "windows-cuda-sana",
    "linux-cuda-quality",
    "linux-cuda-fast",
    "linux-cuda-sana",
    "wsl-cuda-pixal3d",
    "wsl-cuda-sd35-pixal3d",
]


@dataclass(frozen=True)
class ImagePreset:
    model_id: str
    revision: str
    directory_name: str
    steps: int
    backend: Literal["auto", "sana-sprint", "stable-diffusion-3.5"] = "auto"
    guidance: float = 0.0
    width: int = 512
    height: int = 512
    dtype: str = "float16"
    variant: str | None = "fp16"
    quantization: Literal["nf4"] | None = None


@dataclass(frozen=True)
class AssetPreset:
    backend: Literal["hunyuan-mlx", "stable-fast-3d", "pixal3d"]
    model_id: str
    revision: str
    directory_name: str
    source_revision: str
    output_mode: Literal["vertex_color", "pbr_texture"]
    steps: int | None = None
    octree_resolution: int | None = None
    quantization: int | None = None
    texture_resolution: int | None = None
    foreground_ratio: float = 0.85
    remesh: str = "none"
    pipeline_resolution: int | None = None
    camera_fov: float | None = None


@dataclass(frozen=True)
class PipelinePreset:
    name: str
    schema_version: int
    platform: Literal["macos", "windows", "linux"]
    device: str
    image: ImagePreset
    asset: AssetPreset


SD_TURBO_REVISION = "b261bac6fd2cf515557d5d0707481eafa0485ec2"
SDXL_TURBO_REVISION = "71153311d3dbb46851df1931d3ca6e939de83304"
SANA_SPRINT_REVISION = "19683c58b7ea290e55cedd8950ae1d86ada7ef96"
HUNYUAN_SWIFT_REVISION = "292331f4d26ddb80b9dcea6bcb5629ff82f12b82"
HUNYUAN_MLX_REVISION = "b7536809d38ad13fe6a9b7769a41fd5d42e520df"
SF3D_SOURCE_REVISION = "ff21fc491b4dc5314bf6734c7c0dabd86b5f5bb2"
SF3D_MODEL_REVISION = "f0c9a8ffd62cb1bbc8a7a53c9f87a0be1b6be778"
DINOV2_LARGE_REVISION = "47b73eefe95e8d44ec3623f8890bd894b6ea2d6c"
PIXAL3D_SOURCE_REVISION = "cdbb2bbffbf4e6f298b5f2af3d1d76a8d823d2af"
PIXAL3D_MODEL_REVISION = "0b31f9160aa400719af409098bff7936a932f726"
SD35_MEDIUM_REVISION = "b940f670f0eda2d07fbb75229e779da1ad11eb80"


PRESETS: dict[str, PipelinePreset] = {
    "macos-mlx": PipelinePreset(
        name="macos-mlx",
        schema_version=1,
        platform="macos",
        device="mps+mlx-metal",
        image=ImagePreset(
            model_id="stabilityai/sd-turbo",
            revision=SD_TURBO_REVISION,
            directory_name="sd-turbo",
            steps=1,
        ),
        asset=AssetPreset(
            backend="hunyuan-mlx",
            model_id="zimengxiong/hunyuan3d-mlx-shape-small",
            revision=HUNYUAN_MLX_REVISION,
            directory_name="hunyuan3d-mlx-shape-small",
            source_revision=HUNYUAN_SWIFT_REVISION,
            output_mode="vertex_color",
            steps=6,
            octree_resolution=48,
            quantization=4,
        ),
    ),
    "windows-cuda-quality": PipelinePreset(
        name="windows-cuda-quality",
        schema_version=1,
        platform="windows",
        device="cuda:0",
        image=ImagePreset(
            model_id="stabilityai/sdxl-turbo",
            revision=SDXL_TURBO_REVISION,
            directory_name="sdxl-turbo",
            steps=4,
        ),
        asset=AssetPreset(
            backend="stable-fast-3d",
            model_id="stabilityai/stable-fast-3d",
            revision=SF3D_MODEL_REVISION,
            directory_name="stable-fast-3d",
            source_revision=SF3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=2048,
        ),
    ),
    "windows-cuda-fast": PipelinePreset(
        name="windows-cuda-fast",
        schema_version=1,
        platform="windows",
        device="cuda:0",
        image=ImagePreset(
            model_id="stabilityai/sdxl-turbo",
            revision=SDXL_TURBO_REVISION,
            directory_name="sdxl-turbo",
            steps=1,
        ),
        asset=AssetPreset(
            backend="stable-fast-3d",
            model_id="stabilityai/stable-fast-3d",
            revision=SF3D_MODEL_REVISION,
            directory_name="stable-fast-3d",
            source_revision=SF3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=1024,
        ),
    ),
    "windows-cuda-sana": PipelinePreset(
        name="windows-cuda-sana",
        schema_version=1,
        platform="windows",
        device="cuda:0",
        image=ImagePreset(
            backend="sana-sprint",
            model_id="Efficient-Large-Model/Sana_Sprint_1.6B_1024px_diffusers",
            revision=SANA_SPRINT_REVISION,
            directory_name="sana-sprint-1.6b-1024px",
            steps=2,
            guidance=4.5,
            width=1024,
            height=1024,
            dtype="bfloat16",
            variant=None,
        ),
        asset=AssetPreset(
            backend="stable-fast-3d",
            model_id="stabilityai/stable-fast-3d",
            revision=SF3D_MODEL_REVISION,
            directory_name="stable-fast-3d",
            source_revision=SF3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=2048,
        ),
    ),
    "linux-cuda-quality": PipelinePreset(
        name="linux-cuda-quality",
        schema_version=1,
        platform="linux",
        device="cuda:0",
        image=ImagePreset(
            model_id="stabilityai/sdxl-turbo",
            revision=SDXL_TURBO_REVISION,
            directory_name="sdxl-turbo",
            steps=4,
        ),
        asset=AssetPreset(
            backend="stable-fast-3d",
            model_id="stabilityai/stable-fast-3d",
            revision=SF3D_MODEL_REVISION,
            directory_name="stable-fast-3d",
            source_revision=SF3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=2048,
        ),
    ),
    "linux-cuda-fast": PipelinePreset(
        name="linux-cuda-fast",
        schema_version=1,
        platform="linux",
        device="cuda:0",
        image=ImagePreset(
            model_id="stabilityai/sdxl-turbo",
            revision=SDXL_TURBO_REVISION,
            directory_name="sdxl-turbo",
            steps=1,
        ),
        asset=AssetPreset(
            backend="stable-fast-3d",
            model_id="stabilityai/stable-fast-3d",
            revision=SF3D_MODEL_REVISION,
            directory_name="stable-fast-3d",
            source_revision=SF3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=1024,
        ),
    ),
    "linux-cuda-sana": PipelinePreset(
        name="linux-cuda-sana",
        schema_version=1,
        platform="linux",
        device="cuda:0",
        image=ImagePreset(
            backend="sana-sprint",
            model_id="Efficient-Large-Model/Sana_Sprint_1.6B_1024px_diffusers",
            revision=SANA_SPRINT_REVISION,
            directory_name="sana-sprint-1.6b-1024px",
            steps=2,
            guidance=4.5,
            width=1024,
            height=1024,
            dtype="bfloat16",
            variant=None,
        ),
        asset=AssetPreset(
            backend="stable-fast-3d",
            model_id="stabilityai/stable-fast-3d",
            revision=SF3D_MODEL_REVISION,
            directory_name="stable-fast-3d",
            source_revision=SF3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=2048,
        ),
    ),
    "wsl-cuda-pixal3d": PipelinePreset(
        name="wsl-cuda-pixal3d",
        schema_version=1,
        platform="linux",
        device="cuda:0",
        image=ImagePreset(
            backend="sana-sprint",
            model_id="Efficient-Large-Model/Sana_Sprint_1.6B_1024px_diffusers",
            revision=SANA_SPRINT_REVISION,
            directory_name="sana-sprint-1.6b-1024px",
            steps=2,
            guidance=4.5,
            width=1024,
            height=1024,
            dtype="bfloat16",
            variant=None,
        ),
        asset=AssetPreset(
            backend="pixal3d",
            model_id="TencentARC/Pixal3D",
            revision=PIXAL3D_MODEL_REVISION,
            directory_name="pixal3d",
            source_revision=PIXAL3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=4096,
            pipeline_resolution=1024,
            camera_fov=0.2,
        ),
    ),
    "wsl-cuda-sd35-pixal3d": PipelinePreset(
        name="wsl-cuda-sd35-pixal3d",
        schema_version=1,
        platform="linux",
        device="cuda:0",
        image=ImagePreset(
            backend="stable-diffusion-3.5",
            model_id="stabilityai/stable-diffusion-3.5-medium",
            revision=SD35_MEDIUM_REVISION,
            directory_name="stable-diffusion-3.5-medium-nf4",
            steps=28,
            guidance=7.0,
            width=1024,
            height=1024,
            dtype="bfloat16",
            variant=None,
            quantization="nf4",
        ),
        asset=AssetPreset(
            backend="pixal3d",
            model_id="TencentARC/Pixal3D",
            revision=PIXAL3D_MODEL_REVISION,
            directory_name="pixal3d",
            source_revision=PIXAL3D_SOURCE_REVISION,
            output_mode="pbr_texture",
            texture_resolution=4096,
            pipeline_resolution=1024,
            camera_fov=0.2,
        ),
    ),
}


def resolve_profile(
    configured: str,
    *,
    platform_name: str | None = None,
    machine: str | None = None,
) -> PipelinePreset:
    platform_name = platform_name or sys.platform
    machine = (machine or platform.machine()).lower()
    if configured == "auto":
        if platform_name == "darwin" and machine in {"arm64", "aarch64"}:
            configured = "macos-mlx"
        elif platform_name == "win32" and machine in {"amd64", "x86_64"}:
            configured = "windows-cuda-sana"
        elif platform_name == "linux" and machine in {"amd64", "x86_64"}:
            configured = "linux-cuda-sana"
        else:
            raise ValueError(
                f"PIPELINE_PROFILE=auto does not support {platform_name}/{machine}."
            )

    try:
        preset = PRESETS[configured]
    except KeyError as exc:
        choices = ", ".join(["auto", *PRESETS])
        raise ValueError(
            f"Unknown PIPELINE_PROFILE={configured!r}; choose {choices}."
        ) from exc

    expected = {"macos": "darwin", "windows": "win32", "linux": "linux"}[
        preset.platform
    ]
    if platform_name != expected:
        raise ValueError(
            f"Pipeline profile {preset.name!r} requires {preset.platform}, "
            f"but this host is {platform_name}."
        )
    if preset.platform == "macos" and machine not in {"arm64", "aarch64"}:
        raise ValueError("The macos-mlx profile requires Apple Silicon.")
    if preset.platform == "windows" and machine not in {"amd64", "x86_64"}:
        raise ValueError("Windows CUDA profiles require 64-bit x86 Windows.")
    if preset.platform == "linux" and machine not in {"amd64", "x86_64"}:
        raise ValueError("Linux CUDA profiles require 64-bit x86 Linux.")
    return preset
