from pathlib import Path

import pytest

from app import config


@pytest.mark.parametrize(
    ("system", "machine", "wsl", "docker", "supported"),
    [
        ("Linux", "x86_64", True, False, True),
        ("Linux", "x86_64", False, True, True),
        ("Linux", "AMD64", False, True, True),
        ("Linux", "x86_64", False, False, False),
        ("Linux", "aarch64", False, True, False),
        ("Darwin", "x86_64", False, True, False),
        ("Windows", "AMD64", False, True, False),
    ],
)
def test_supported_host(monkeypatch, system, machine, wsl, docker, supported):
    monkeypatch.setattr(config.platform, "system", lambda: system)
    monkeypatch.setattr(config.platform, "machine", lambda: machine)
    monkeypatch.setattr(config, "is_wsl", lambda: wsl)
    monkeypatch.setattr(
        Path, "is_file", lambda path: docker if str(path) == "/.dockerenv" else False
    )
    assert config.is_supported_host() is supported
