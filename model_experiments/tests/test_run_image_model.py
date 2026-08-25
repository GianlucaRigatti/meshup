from __future__ import annotations

import io

import pytest

from scripts import run_image_model


def test_image_runner_reads_request_from_stdin(monkeypatch) -> None:
    monkeypatch.setattr(
        run_image_model.sys,
        "stdin",
        io.StringIO('{"prompt": "a chair", "seed": 42}'),
    )

    assert run_image_model.read_request() == ("a chair", 42)


@pytest.mark.parametrize(
    "payload",
    [
        '{}',
        '{"prompt": "", "seed": 42}',
        '{"prompt": "a chair", "seed": true}',
        '{"prompt": "a chair", "seed": -1}',
    ],
)
def test_image_runner_rejects_invalid_requests(monkeypatch, payload: str) -> None:
    monkeypatch.setattr(run_image_model.sys, "stdin", io.StringIO(payload))

    with pytest.raises(ValueError):
        run_image_model.read_request()
