from __future__ import annotations

import argparse
import os
from pathlib import Path

from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Remove one image background in an isolated CUDA process."
    )
    parser.add_argument("--model-cache", type=Path, required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.check and (args.input is None or args.output is None):
        parser.error("--input and --output are required unless --check is used")
    return args


def create_cuda_session(model_name: str):
    # PyTorch and ONNX Runtime use compatible CUDA 12.8/cuDNN 9 libraries in
    # the WSL environment. Importing torch first makes those libraries visible
    # before ONNX Runtime creates its execution provider.
    import torch  # noqa: F401
    import onnxruntime as ort

    if "CUDAExecutionProvider" not in ort.get_available_providers():
        raise RuntimeError(
            "CUDAExecutionProvider is unavailable; install the pinned "
            "CUDA 12 ONNX Runtime GPU package."
        )
    import rembg

    session = rembg.new_session(
        model_name,
        providers=["CUDAExecutionProvider"],
    )
    active = session.inner_session.get_providers()
    if "CUDAExecutionProvider" not in active:
        raise RuntimeError(
            "Background removal silently fell back to CPU; active providers: "
            + ", ".join(active)
        )
    return rembg, session


def main() -> None:
    args = parse_args()
    rembg_dir = args.model_cache / "models" / "rembg"
    os.environ["U2NET_HOME"] = str(rembg_dir.resolve())
    rembg, session = create_cuda_session(args.model)
    if args.check:
        return

    with Image.open(args.input) as source:
        output = rembg.remove(source.convert("RGB"), session=session).convert("RGBA")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    output.save(args.output, format="PNG")


if __name__ == "__main__":
    main()
