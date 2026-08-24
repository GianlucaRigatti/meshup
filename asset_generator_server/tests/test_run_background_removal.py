from __future__ import annotations

import sys
from types import SimpleNamespace

import pytest

from scripts import run_background_removal


def test_cuda_background_removal_rejects_missing_provider(monkeypatch) -> None:
    fake_ort = SimpleNamespace(get_available_providers=lambda: ["CPUExecutionProvider"])
    monkeypatch.setitem(sys.modules, "torch", SimpleNamespace())
    monkeypatch.setitem(sys.modules, "onnxruntime", fake_ort)

    with pytest.raises(RuntimeError, match="CUDAExecutionProvider is unavailable"):
        run_background_removal.create_cuda_session("birefnet-general")


def test_cuda_background_removal_requests_only_cuda(monkeypatch) -> None:
    active = ["CUDAExecutionProvider"]
    calls = []
    fake_ort = SimpleNamespace(get_available_providers=lambda: active)
    fake_session = SimpleNamespace(
        inner_session=SimpleNamespace(get_providers=lambda: active)
    )
    fake_rembg = SimpleNamespace(
        new_session=lambda model, **kwargs: calls.append((model, kwargs))
        or fake_session
    )
    monkeypatch.setitem(sys.modules, "torch", SimpleNamespace())
    monkeypatch.setitem(sys.modules, "onnxruntime", fake_ort)
    monkeypatch.setitem(sys.modules, "rembg", fake_rembg)

    _, session = run_background_removal.create_cuda_session("birefnet-general")

    assert session is fake_session
    assert calls == [("birefnet-general", {"providers": ["CUDAExecutionProvider"]})]
