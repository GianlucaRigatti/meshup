from __future__ import annotations

from types import SimpleNamespace

from scripts import benchmark


def test_linux_benchmark_includes_nvidia_metadata(monkeypatch) -> None:
    monkeypatch.setattr(benchmark.platform, "system", lambda: "Linux")
    monkeypatch.setattr(benchmark.platform, "platform", lambda: "Linux-WSL2")
    monkeypatch.setattr(benchmark.platform, "machine", lambda: "x86_64")
    monkeypatch.setattr(
        benchmark,
        "hardware_value",
        lambda command, fallback="unknown": (
            "RTX Test" if "name" in command[1] else "600.1"
        ),
    )
    monkeypatch.setattr(
        benchmark,
        "torch",
        SimpleNamespace(
            __version__="2.7.1+cu128", version=SimpleNamespace(cuda="12.8")
        ),
    )

    metadata = benchmark.system_metadata()

    assert metadata["gpu"] == "RTX Test"
    assert metadata["driver"] == "600.1"
    assert metadata["cuda"] == "12.8"
