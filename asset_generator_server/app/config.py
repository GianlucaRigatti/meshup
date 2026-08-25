from __future__ import annotations

import hashlib
import json
import os
import platform
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path

IMAGE_GENERATOR = "flux2-klein-9b-q4-k-m-fast"
MODEL_3D = "trellis2-turbo"
BACKGROUND_REMOVAL_MODEL = "birefnet-general"
DEVICE = "cuda:0"
OUTPUT_MODE = "pbr_texture"

STABLE_DIFFUSION_CPP_REPOSITORY = "https://github.com/leejet/stable-diffusion.cpp.git"
STABLE_DIFFUSION_CPP_REVISION = "97d2990807fe6d558e395f8764198d7c7e7b411c"
TRELLIS_CPP_REPOSITORY = "https://github.com/pwilkin/trellis.cpp.git"
TRELLIS_CPP_REVISION = "06fc9000719c912ddc4929d21db075972c26ac3e"

FLUX_MODEL_ID = "unsloth/FLUX.2-klein-9B-GGUF"
FLUX_MODEL_REVISION = "fde8634245fe6b749a221c25b34672b5b8fbd079"
FLUX_MODEL_FILENAME = "flux-2-klein-9b-Q4_K_M.gguf"
QWEN_MODEL_ID = "Qwen/Qwen3-8B-GGUF"
QWEN_MODEL_REVISION = "7c41481f57cb95916b40956ab2f0b139b296d974"
QWEN_MODEL_FILENAME = "Qwen3-8B-Q4_K_M.gguf"
FLUX_VAE_MODEL_ID = "Comfy-Org/flux2-klein-4B"
FLUX_VAE_REVISION = "5f526678002e43af5551dadb73ce2e8c91b43afe"
FLUX_VAE_FILENAME = "split_files/vae/flux2-vae.safetensors"

BIREFNET_MODEL_ID = "ZhengPeng7/BiRefNet"
BIREFNET_MODEL_REVISION = "b7d7f31fed203ab364ac756d62053ee467502434"

TRELLIS_MODEL_ID = "ilintar/trellis2-gguf"
TRELLIS_MODEL_REVISION = "a57397bd3d351599d9729fc144b3f87c3f87d65b"
TRELLIS_MODEL_FILENAMES = (
    "dinov3.gguf",
    "ss_flow.gguf",
    "ss_dec.gguf",
    "shape_flow_512.gguf",
    "shape_dec.gguf",
    "tex_flow_512.gguf",
    "tex_dec.gguf",
)

PIPELINE_SCHEMA_VERSION = 2
PROMPT_SUFFIX = ", one isolated subject, complete subject fully visible, centered, three-quarter front view, camera near subject height, faithful subject-specific anatomy, characteristic colors and materials, natural coherent shape, strong clean silhouette, limbs and appendages clearly visible and separated where applicable, balanced proportions, soft diffuse studio lighting, shadowless presentation, sharp focus, weak-perspective product view, solid white background, no floor, no pedestal, no environment, no text, no extra objects, no cropping, no occlusion"

_PIPELINE_IDENTITY = {
    "schema": PIPELINE_SCHEMA_VERSION,
    "image_generator": IMAGE_GENERATOR,
    "model_3d": MODEL_3D,
    "stable_diffusion_cpp": STABLE_DIFFUSION_CPP_REVISION,
    "trellis_cpp": TRELLIS_CPP_REVISION,
    "flux": FLUX_MODEL_REVISION,
    "qwen": QWEN_MODEL_REVISION,
    "vae": FLUX_VAE_REVISION,
    "background_removal": BIREFNET_MODEL_REVISION,
    "trellis": TRELLIS_MODEL_REVISION,
    "prompt_suffix": PROMPT_SUFFIX,
    "image": {
        "width": 768,
        "height": 768,
        "steps": 4,
        "cfg": 1.0,
        "max_vram_gib": 11,
    },
    "asset": {
        "resolution": 512,
        "max_tokens": 49152,
        "atlas": 1024,
        "box_uv": True,
        "background_removal": "external-birefnet-general-fp16-1024",
        "foreground_ratio": 435 / 512,
    },
}
_PIPELINE_DIGEST = hashlib.sha256(
    json.dumps(_PIPELINE_IDENTITY, sort_keys=True).encode()
).hexdigest()[:16]
PIPELINE_VERSION = f"flux2-klein-9b-trellis2-turbo-{_PIPELINE_DIGEST}"


@dataclass(frozen=True)
class Settings:
    public_base_url: str | None = None
    asset_output_dir: Path = Path("generated_assets")
    model_cache_dir: Path = Path(".model_sources")
    image_timeout_seconds: int = 600
    trellis_timeout_seconds: int = 1800
    log_level: str = "INFO"

    @classmethod
    def from_env(
        cls,
        environment: Mapping[str, str] | None = None,
        env_file: Path = Path(".env"),
    ) -> Settings:
        values = _read_env_file(env_file)
        values.update(os.environ if environment is None else environment)
        return cls(
            public_base_url=values.get("PUBLIC_BASE_URL") or None,
            asset_output_dir=Path(values.get("ASSET_OUTPUT_DIR", "generated_assets")),
            model_cache_dir=Path(values.get("MODEL_CACHE_DIR", ".model_sources")),
            image_timeout_seconds=_positive_int(
                values.get("IMAGE_TIMEOUT_SECONDS", "600"),
                "IMAGE_TIMEOUT_SECONDS",
            ),
            trellis_timeout_seconds=_positive_int(
                values.get("TRELLIS_TIMEOUT_SECONDS", "1800"),
                "TRELLIS_TIMEOUT_SECONDS",
            ),
            log_level=values.get("LOG_LEVEL", "INFO"),
        )

    @property
    def stable_diffusion_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "stable-diffusion.cpp"

    @property
    def stable_diffusion_build_path(self) -> Path:
        return self.stable_diffusion_source_path / ".build"

    @property
    def stable_diffusion_executable_path(self) -> Path:
        return self.stable_diffusion_build_path / "bin" / "sd-cli"

    @property
    def trellis_source_path(self) -> Path:
        return self.model_cache_dir / "sources" / "trellis.cpp"

    @property
    def trellis_build_path(self) -> Path:
        return self.trellis_source_path / ".build"

    @property
    def trellis_executable_path(self) -> Path:
        return self.trellis_build_path / "trellis-cli"

    @property
    def flux_model_path(self) -> Path:
        return self.model_cache_dir / "models" / IMAGE_GENERATOR

    @property
    def flux_diffusion_path(self) -> Path:
        return self.flux_model_path / FLUX_MODEL_FILENAME

    @property
    def flux_text_encoder_path(self) -> Path:
        return self.flux_model_path / QWEN_MODEL_FILENAME

    @property
    def flux_vae_path(self) -> Path:
        return self.flux_model_path / FLUX_VAE_FILENAME

    @property
    def trellis_model_root(self) -> Path:
        return self.model_cache_dir / "models" / MODEL_3D

    @property
    def trellis_model_path(self) -> Path:
        return self.trellis_model_root / "q4"

    @property
    def background_removal_model_path(self) -> Path:
        return self.model_cache_dir / "models" / BACKGROUND_REMOVAL_MODEL

    @property
    def required_files(self) -> tuple[Path, ...]:
        return (
            self.stable_diffusion_executable_path,
            self.trellis_executable_path,
            self.flux_diffusion_path,
            self.flux_text_encoder_path,
            self.flux_vae_path,
            *(self.trellis_model_path / name for name in TRELLIS_MODEL_FILENAMES),
        )


def is_wsl() -> bool:
    if platform.system() != "Linux":
        return False
    try:
        release = Path("/proc/sys/kernel/osrelease").read_text(encoding="utf-8")
    except OSError:
        return False
    return "microsoft" in release.lower()


def _read_env_file(path: Path) -> dict[str, str]:
    if not path.is_file():
        return {}
    values: dict[str, str] = {}
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
            value = value[1:-1]
        values[key] = value
    return values


def _positive_int(value: str, name: str) -> int:
    try:
        parsed = int(value)
    except ValueError as exc:
        raise ValueError(f"{name} must be a positive integer.") from exc
    if parsed <= 0:
        raise ValueError(f"{name} must be a positive integer.")
    return parsed
