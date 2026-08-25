from __future__ import annotations

import os
from pathlib import Path

import pytest

from app.config import Settings
from app.pipeline import AssetGenerator


@pytest.mark.real_models
@pytest.mark.skipif(
    os.getenv("RUN_REAL_MODEL_TESTS") != "1" or not os.getenv("AUDIO_SAMPLE_PATH"),
    reason=(
        "Set RUN_REAL_MODEL_TESTS=1 and AUDIO_SAMPLE_PATH after installing models."
    ),
)
def test_real_audio_pipeline() -> None:
    audio_path = Path(os.environ["AUDIO_SAMPLE_PATH"])
    assert audio_path.is_file()
    generator = AssetGenerator(Settings())
    generator.load()
    assert generator.ready, generator.load_error

    result = generator.generate_from_audio(audio_path)

    assert result.transcript
    assert result.enhanced_prompt
    assert (generator.output_dir / f"{result.asset_id}.glb").read_bytes()[:4] == b"glTF"
