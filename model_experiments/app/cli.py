from __future__ import annotations

import argparse
import platform
from pathlib import Path

import uvicorn

from app.config import Settings
from app.main import create_app
from app.presets import IMAGE_GENERATORS, MODELS_3D


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="asset-generator-server",
        description="Run the WSL CUDA text-to-3D server with independently selected models.",
    )
    parser.add_argument(
        "--image-generator",
        choices=IMAGE_GENERATORS,
        help="Text-to-image model (default: IMAGE_GENERATOR or zimage-q4).",
    )
    parser.add_argument(
        "--model-3d",
        choices=MODELS_3D,
        help="Image-to-3D model (default: MODEL_3D or trellis2-fast).",
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--log-level", default=None)
    parser.add_argument(
        "--list-models",
        action="store_true",
        help="List selectable WSL models and exit.",
    )
    return parser


def _is_wsl() -> bool:
    if platform.system() != "Linux":
        return False
    release = Path("/proc/sys/kernel/osrelease")
    try:
        return "microsoft" in release.read_text(encoding="utf-8").lower()
    except OSError:
        return False


def _print_models() -> None:
    print("Image generators:")
    for name in IMAGE_GENERATORS:
        print(f"  {name}")
    print("3D models:")
    for name in MODELS_3D:
        print(f"  {name}")


def main(argv: list[str] | None = None) -> None:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.list_models:
        _print_models()
        return
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")
    if not _is_wsl():
        parser.error("the public server launcher requires WSL 2")

    environment = Settings(PIPELINE_PROFILE=None)
    settings = Settings(
        IMAGE_GENERATOR=args.image_generator or environment.image_generator,
        MODEL_3D=args.model_3d or environment.model_3d,
        PIPELINE_PROFILE=None,
        LOG_LEVEL=args.log_level or environment.log_level,
    )
    # Resolve before starting Uvicorn so invalid environment selections produce
    # a direct CLI error rather than a delayed application startup failure.
    try:
        preset = settings.preset
    except ValueError as exc:
        parser.error(str(exc))

    print(
        f"Starting {preset.name} "
        f"(image={settings.image_generator}, 3d={settings.model_3d})"
    )
    uvicorn.run(
        create_app(settings),
        host=args.host,
        port=args.port,
        log_level=settings.log_level.lower(),
    )


if __name__ == "__main__":
    main()
