from __future__ import annotations

import pytest

from scripts.run_pixal3d import PreprocessedRgbaOnly


def test_preprocessed_rgba_placeholder_rejects_background_removal() -> None:
    placeholder = PreprocessedRgbaOnly(model_name="unused")

    assert placeholder.to("cuda") is placeholder
    assert placeholder.cpu() is placeholder
    with pytest.raises(RuntimeError, match="without transparency"):
        placeholder(object())
