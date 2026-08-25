from __future__ import annotations

from app import cli


def test_public_cli_exposes_only_composable_wsl_models() -> None:
    help_text = cli.build_parser().format_help()

    assert "--image-generator" in help_text
    assert "--model-3d" in help_text
    assert "zimage-q4" in help_text
    assert "instantmesh-fast" in help_text
    assert "macos-mlx" not in help_text
    assert "windows-cuda" not in help_text
    assert "--profile" not in help_text


def test_cli_starts_selected_composed_pipeline(monkeypatch) -> None:
    captured = {}
    monkeypatch.setattr(cli, "_is_wsl", lambda: True)
    monkeypatch.setattr(
        cli,
        "create_app",
        lambda settings: captured.update(settings=settings) or object(),
    )
    monkeypatch.setattr(
        cli.uvicorn,
        "run",
        lambda application, **kwargs: captured.update(app=application, kwargs=kwargs),
    )

    cli.main(
        [
            "--image-generator",
            "zimage-q6",
            "--model-3d",
            "instantmesh-fast",
            "--port",
            "8123",
        ]
    )

    assert captured["settings"].image_generator == "zimage-q6"
    assert captured["settings"].model_3d == "instantmesh-fast"
    assert captured["kwargs"]["port"] == 8123


def test_list_models_does_not_require_wsl(monkeypatch, capsys) -> None:
    monkeypatch.setattr(cli, "_is_wsl", lambda: False)

    cli.main(["--list-models"])

    output = capsys.readouterr().out
    assert "Image generators:" in output
    assert "zimage-q4" in output
    assert "3D models:" in output
    assert "trellis2-fast" in output
