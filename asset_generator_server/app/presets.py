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
]


@dataclass(frozen=True)
class ImagePreset:
    model_id: str
    revision: str
    directory_name: str
    steps: int
    guidance: float = 0.0
    width: int = 512
    height: int = 512
    dtype: str = "float16"
    variant: str = "fp16"


@dataclass(frozen=True)
class AssetPreset:
    backend: Literal["hunyuan-mlx", "stable-fast-3d"]
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


@dataclass(frozen=True)
class PipelinePreset:
    name: str
    schema_version: int
    platform: Literal["macos", "windows"]
    device: str
    image: ImagePreset
    asset: AssetPreset


SD_TURBO_REVISION = "b261bac6fd2cf515557d5d0707481eafa0485ec2"
SDXL_TURBO_REVISION = "71153311d3dbb46851df1931d3ca6e939de83304"
HUNYUAN_SWIFT_REVISION = "292331f4d26ddb80b9dcea6bcb5629ff82f12b82"
HUNYUAN_MLX_REVISION = "b7536809d38ad13fe6a9b7769a41fd5d42e520df"
SF3D_SOURCE_REVISION = "ff21fc491b4dc5314bf6734c7c0dabd86b5f5bb2"
SF3D_MODEL_REVISION = "f0c9a8ffd62cb1bbc8a7a53c9f87a0be1b6be778"
DINOV2_LARGE_REVISION = "47b73eefe95e8d44ec3623f8890bd894b6ea2d6c"


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
            configured = "windows-cuda-quality"
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

    expected = "darwin" if preset.platform == "macos" else "win32"
    if platform_name != expected:
        raise ValueError(
            f"Pipeline profile {preset.name!r} requires {preset.platform}, "
            f"but this host is {platform_name}."
        )
    if preset.platform == "macos" and machine not in {"arm64", "aarch64"}:
        raise ValueError("The macos-mlx profile requires Apple Silicon.")
    if preset.platform == "windows" and machine not in {"amd64", "x86_64"}:
        raise ValueError("Windows CUDA profiles require 64-bit x86 Windows.")
    return preset
