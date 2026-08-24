from __future__ import annotations

import numpy as np
from PIL import Image

from app.background_removal import TorchBiRefNet


class FakeBiRefNet:
    def __init__(self) -> None:
        self.moves = []

    def to(self, **kwargs):
        self.moves.append(kwargs)
        return self

    def __call__(self, tensor):
        return [tensor[:, :1]]


def test_native_birefnet_returns_rgba_and_releases_model_to_cpu(tmp_path) -> None:
    remover = TorchBiRefNet(tmp_path, device="cpu", resolution=4)
    model = FakeBiRefNet()
    remover.model = model
    pixels = np.zeros((4, 4, 3), dtype=np.uint8)
    pixels[:, 2:, :] = 255

    output = remover.remove(Image.fromarray(pixels))

    assert output.mode == "RGBA"
    assert output.size == (4, 4)
    assert model.moves[-1]["device"] == "cpu"
    assert set(remover.last_timings) == {
        "background_to_gpu_ms",
        "background_inference_ms",
        "background_release_ms",
    }
    assert remover.last_memory == {}


def test_constant_prediction_produces_empty_mask() -> None:
    prediction = np.ones((1, 1, 4, 4), dtype=np.float32)

    mask = TorchBiRefNet._mask_from_prediction(prediction, (8, 6))

    assert mask.size == (8, 6)
    assert np.asarray(mask).max() == 0
