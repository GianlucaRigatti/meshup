from __future__ import annotations

import gc
import time
from pathlib import Path

import numpy as np
from PIL import Image


class TorchBiRefNet:
    """Keep BiRefNet in CPU RAM and borrow CUDA only for one inference."""

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
        self.last_memory: dict[str, int] = {}

    def load(self) -> None:
        import torch
        from transformers import AutoModelForImageSegmentation

        if self.device.startswith("cuda") and not torch.cuda.is_available():
            raise RuntimeError("CUDA is required for native BiRefNet inference.")
        if not self.model_path.is_dir():
            raise FileNotFoundError(
                "The native BiRefNet checkpoint is missing; rerun the model installer."
            )

        torch.set_float32_matmul_precision("high")
        self.model = AutoModelForImageSegmentation.from_pretrained(
            str(self.model_path.resolve()),
            trust_remote_code=True,
            local_files_only=True,
        )
        self.model.eval().requires_grad_(False)
        # The official inference path uses FP16. Keeping the idle CPU copy in
        # FP16 also halves its system-memory footprint.
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
        memory: dict[str, int] = {}

        if cuda:
            torch.cuda.empty_cache()
            torch.cuda.reset_peak_memory_stats()

        stage = time.perf_counter()
        self.model.to(device=self.device, dtype=dtype)
        if cuda:
            torch.cuda.synchronize()
            memory["birefnet_model_allocated_bytes"] = torch.cuda.memory_allocated()
        timings["background_to_gpu_ms"] = self._elapsed_ms(stage)

        try:
            stage = time.perf_counter()
            with torch.inference_mode():
                prediction = self.model(tensor.to(self.device, dtype=dtype))[-1]
                prediction = prediction.sigmoid().float().cpu()
            timings["background_inference_ms"] = self._elapsed_ms(stage)
            if cuda:
                memory["birefnet_peak_allocated_bytes"] = (
                    torch.cuda.max_memory_allocated()
                )
                memory["birefnet_peak_reserved_bytes"] = (
                    torch.cuda.max_memory_reserved()
                )
            mask = self._mask_from_prediction(prediction.numpy(), source.size)
        finally:
            stage = time.perf_counter()
            self.model.to(device="cpu", dtype=torch.float16)
            if cuda:
                torch.cuda.synchronize()
                torch.cuda.empty_cache()
                torch.cuda.ipc_collect()
                memory["birefnet_post_release_allocated_bytes"] = (
                    torch.cuda.memory_allocated()
                )
            gc.collect()
            timings["background_release_ms"] = self._elapsed_ms(stage)
            self.last_timings = timings
            self.last_memory = memory

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

    @staticmethod
    def _elapsed_ms(started: float) -> int:
        return round((time.perf_counter() - started) * 1000)
