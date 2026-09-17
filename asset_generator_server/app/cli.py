from __future__ import annotations

import argparse
from dataclasses import replace

import uvicorn

from app.config import (
    IMAGE_GENERATOR,
    MODEL_3D,
    PROMPT_ENHANCER_MODEL,
    SPEECH_TO_TEXT_MODEL,
    Settings,
    is_wsl,
)
from app.main import create_app


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="asset-generator-server",
        description=(
            f"Run the fixed WSL CUDA {SPEECH_TO_TEXT_MODEL} / "
            f"{PROMPT_ENHANCER_MODEL} / {IMAGE_GENERATOR} / {MODEL_3D} server."
        ),
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--log-level", default=None)
    parser.add_argument(
        "--mesh-simplification",
        action=argparse.BooleanOptionalAction,
        default=None,
        help="simplify generated meshes toward the triangle budget (default: enabled)",
    )
    return parser


def main(argv: list[str] | None = None) -> None:
    parser = build_parser()
    args = parser.parse_args(argv)
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")
    if not is_wsl():
        parser.error("the asset generator server requires WSL 2")
    try:
        settings = Settings.from_env()
    except ValueError as exc:
        parser.error(str(exc))
    if args.log_level:
        settings = replace(settings, log_level=args.log_level)
    if args.mesh_simplification is not None:
        settings = replace(settings, mesh_simplification=args.mesh_simplification)

    print(
        f"Starting speech={SPEECH_TO_TEXT_MODEL} "
        f"prompt={PROMPT_ENHANCER_MODEL} image={IMAGE_GENERATOR} 3d={MODEL_3D}"
    )
    uvicorn.run(
        create_app(settings),
        host=args.host,
        port=args.port,
        log_level=settings.log_level.lower(),
    )


if __name__ == "__main__":
    main()
