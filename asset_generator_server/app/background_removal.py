from __future__ import annotations

import gc
import time
import warnings
from pathlib import Path

import numpy as np
from PIL import Image


class TorchBiRefNet:
    """Keep the full BiRefNet model in CPU RAM and borrow CUDA for inference."""

    def __init__(
        self,
        model_path: Path,
        *,
        device: str = "cuda:0",
        resolution: int = 1024,
    ) -> None:
        self.model_path = model_path
        self.device = device
        self.resolution = resolution
        self.model = None
        self.last_timings: dict[str, int] = {}

    def load(self) -> None:
        import torch
        from transformers import AutoModelForImageSegmentation

        if self.device.startswith("cuda") and not torch.cuda.is_available():
            raise RuntimeError("CUDA is required for BiRefNet inference.")
        if not self.model_path.is_dir():
            raise FileNotFoundError(
                "The BiRefNet checkpoint is missing; rerun the model installer."
            )

        torch.set_float32_matmul_precision("high")
        # The pinned BiRefNet remote code still uses timm's compatibility import
        # paths. Suppress only those known deprecations while loading it.
        with warnings.catch_warnings():
            warnings.filterwarnings(
                "ignore",
                message=(
                    r"Importing from timm\.models\.layers is deprecated, "
                    r"please import via timm\.layers"
                ),
                category=FutureWarning,
            )
            warnings.filterwarnings(
                "ignore",
                message=(
                    r"Importing from timm\.models\.registry is deprecated, "
                    r"please import via timm\.models"
                ),
                category=FutureWarning,
            )
            self.model = AutoModelForImageSegmentation.from_pretrained(
                str(self.model_path.resolve()),
                trust_remote_code=True,
                local_files_only=True,
            )
        self.model.eval().requires_grad_(False)
        self.model.to(device="cpu", dtype=torch.float16)

    def remove(self, image: Image.Image) -> Image.Image:
        import torch

        if self.model is None:
            raise RuntimeError("BiRefNet has not been loaded.")

        source = image.convert("RGB")
        tensor = self._input_tensor(source, torch)
        cuda = self.device.startswith("cuda")
        dtype = torch.float16 if cuda else torch.float32
        timings: dict[str, int] = {}

        if cuda:
            torch.cuda.empty_cache()

        stage = time.perf_counter()
        self.model.to(device=self.device, dtype=dtype)
        if cuda:
            torch.cuda.synchronize()
        timings["background_to_gpu_ms"] = _elapsed_ms(stage)

        try:
            stage = time.perf_counter()
            with torch.inference_mode():
                prediction = self.model(tensor.to(self.device, dtype=dtype))[-1]
                prediction = prediction.sigmoid().float().cpu()
            timings["background_inference_ms"] = _elapsed_ms(stage)
            mask = self._mask_from_prediction(prediction.numpy(), source.size)
        finally:
            stage = time.perf_counter()
            self.model.to(device="cpu", dtype=torch.float16)
            if cuda:
                torch.cuda.synchronize()
                torch.cuda.empty_cache()
                torch.cuda.ipc_collect()
            gc.collect()
            timings["background_release_ms"] = _elapsed_ms(stage)
            self.last_timings = timings

        result = source.convert("RGBA")
        result.putalpha(mask)
        return result

    def _input_tensor(self, image: Image.Image, torch):
        resized = image.resize(
            (self.resolution, self.resolution), Image.Resampling.BILINEAR
        )
        pixels = np.asarray(resized, dtype=np.float32) / 255.0
        pixels = np.transpose(pixels, (2, 0, 1))[None, ...]
        tensor = torch.from_numpy(pixels)
        mean = torch.tensor((0.485, 0.456, 0.406)).view(1, 3, 1, 1)
        std = torch.tensor((0.229, 0.224, 0.225)).view(1, 3, 1, 1)
        return (tensor - mean) / std

    @staticmethod
    def _mask_from_prediction(prediction: np.ndarray, size: tuple[int, int]):
        prediction = np.squeeze(prediction)
        minimum = float(prediction.min())
        maximum = float(prediction.max())
        if maximum > minimum:
            prediction = (prediction - minimum) / (maximum - minimum)
        else:
            prediction = np.zeros_like(prediction)
        mask = Image.fromarray((prediction * 255).astype(np.uint8))
        return mask.resize(size, Image.Resampling.LANCZOS)


def prepare_foreground(
    image: Image.Image,
    remover: TorchBiRefNet,
    *,
    canvas_size: int = 768,
) -> Image.Image:
    """Reproduce the archived crop, scale, and transparent-canvas handoff."""
    rgba = remover.remove(image)
    alpha = np.asarray(rgba.getchannel("A"))
    points = np.argwhere(alpha > 8)
    if points.size == 0:
        raise RuntimeError("The generated image does not contain a visible object.")

    y0, x0 = points.min(axis=0)
    y1, x1 = points.max(axis=0) + 1
    cropped = rgba.crop((int(x0), int(y0), int(x1), int(y1)))
    target = round(canvas_size * (435 / 512))
    scale = min(target / cropped.width, target / cropped.height)
    size = (round(cropped.width * scale), round(cropped.height * scale))
    cropped = cropped.resize(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (canvas_size, canvas_size), (255, 255, 255, 0))
    canvas.alpha_composite(
        cropped, ((canvas_size - size[0]) // 2, (canvas_size - size[1]) // 2)
    )
    return canvas


def _elapsed_ms(started: float) -> int:
    return round((time.perf_counter() - started) * 1000)
