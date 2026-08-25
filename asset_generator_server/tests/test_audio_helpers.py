from __future__ import annotations

import sys
import types
from pathlib import Path

from app import run_asr, run_prompt_enhancer


def test_asr_helper_uses_fixed_local_inference_settings(
    tmp_path: Path, monkeypatch
) -> None:
    calls: dict[str, object] = {}

    class FakeASR:
        @classmethod
        def from_pretrained(cls, model, **kwargs):
            calls["model"] = model
            calls["load"] = kwargs
            return cls()

        def transcribe(self, **kwargs):
            calls["transcribe"] = kwargs
            return [types.SimpleNamespace(text="a chair", language="English")]

    monkeypatch.setitem(
        sys.modules, "torch", types.SimpleNamespace(bfloat16="bfloat16")
    )
    monkeypatch.setitem(
        sys.modules, "qwen_asr", types.SimpleNamespace(Qwen3ASRModel=FakeASR)
    )

    result = run_asr.transcribe(tmp_path / "model", tmp_path / "sample.wav")

    assert result == {"text": "a chair", "language": "English"}
    assert calls["load"] == {
        "dtype": "bfloat16",
        "device_map": "cuda:0",
        "max_inference_batch_size": 1,
        "max_new_tokens": 512,
    }
    assert calls["transcribe"]["language"] is None


def test_prompt_enhancer_is_non_thinking_and_deterministic(
    tmp_path: Path, monkeypatch
) -> None:
    calls: dict[str, object] = {}

    class FakeInputs(dict):
        def to(self, device):
            calls["inputs_device"] = device
            return self

    class FakeProcessor:
        @classmethod
        def from_pretrained(cls, model, **kwargs):
            calls["processor_load"] = kwargs
            return cls()

        def apply_chat_template(self, messages, **kwargs):
            calls["messages"] = messages
            calls["template"] = kwargs
            return FakeInputs({"input_ids": types.SimpleNamespace(shape=(1, 3))})

        def decode(self, tokens, **kwargs):
            calls["decode"] = kwargs
            return "A detailed oak chair"

    class FakeModel:
        device = "cuda:0"

        @classmethod
        def from_pretrained(cls, model, **kwargs):
            calls["model_load"] = kwargs
            return cls()

        def generate(self, **kwargs):
            calls["generate"] = kwargs
            return [[0, 1, 2, 3]]

    monkeypatch.setitem(
        sys.modules, "torch", types.SimpleNamespace(bfloat16="bfloat16")
    )
    monkeypatch.setitem(
        sys.modules,
        "transformers",
        types.SimpleNamespace(
            AutoModelForMultimodalLM=FakeModel,
            AutoProcessor=FakeProcessor,
        ),
    )

    result = run_prompt_enhancer.enhance(tmp_path / "model", "a chair")

    assert result == "A detailed oak chair"
    assert calls["template"]["enable_thinking"] is False
    assert calls["generate"]["do_sample"] is False
    assert calls["generate"]["max_new_tokens"] == 160
    assert calls["model_load"]["dtype"] == "bfloat16"
    assert calls["model_load"]["device_map"] == "cuda:0"
