from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))


def parse_args() -> argparse.Namespace:
    from app.presets import IMAGE_GENERATORS, MODELS_3D

    parser = argparse.ArgumentParser(
        description="Generate one image in an isolated model process."
    )
    parser.add_argument("--model-cache", type=Path, required=True)
    parser.add_argument("--image-generator", choices=IMAGE_GENERATORS)
    parser.add_argument("--model-3d", choices=MODELS_3D)
    parser.add_argument("--profile", help=argparse.SUPPRESS)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def read_request() -> tuple[str, int]:
    request = json.load(sys.stdin)
    prompt = request.get("prompt")
    seed = request.get("seed")
    if not isinstance(prompt, str) or not prompt:
        raise ValueError("The image request requires a non-empty prompt.")
    if not isinstance(seed, int) or isinstance(seed, bool) or seed < 0:
        raise ValueError("The image request requires a non-negative integer seed.")
    return prompt, seed


def main() -> None:
    args = parse_args()
    prompt, seed = read_request()

    from app.backends import DiffusersImageBackend
    from app.config import Settings

    if args.profile:
        settings = Settings(
            MODEL_CACHE_DIR=args.model_cache,
            PIPELINE_PROFILE=args.profile,
        )
    else:
        if args.image_generator is None or args.model_3d is None:
            raise ValueError("Both model selections are required.")
        settings = Settings(
            MODEL_CACHE_DIR=args.model_cache,
            IMAGE_GENERATOR=args.image_generator,
            MODEL_3D=args.model_3d,
            PIPELINE_PROFILE=None,
        )
    preset = settings.preset
    if preset.image.backend != "stable-diffusion-3.5":
        raise ValueError("The isolated image runner only supports SD 3.5 profiles.")

    backend = DiffusersImageBackend(settings, preset)
    image = backend.generate(prompt, seed)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    image.save(args.output, format="PNG")


if __name__ == "__main__":
    main()
