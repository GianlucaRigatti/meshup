from __future__ import annotations

from pathlib import Path

import pytest

from app.config import (
    FLUX_MODEL_FILENAME,
    FLUX_MODEL_ID,
    FLUX_VAE_FILENAME,
    QWEN_MODEL_FILENAME,
    STABLE_DIFFUSION_CPP_REPOSITORY,
    TRELLIS_CPP_REPOSITORY,
    TRELLIS_MODEL_FILENAMES,
    TRELLIS_MODEL_ID,
    Settings,
)
from scripts import install_models


def test_license_acceptance_is_required() -> None:
    with pytest.raises(SystemExit) as error:
        install_models.main([])
    assert error.value.code == 2


def test_validate_host_rejects_unsupported_system(monkeypatch) -> None:
    monkeypatch.setattr(install_models, "is_wsl", lambda: False)
    with pytest.raises(RuntimeError, match="WSL 2"):
        install_models.validate_host()


def test_validate_host_uses_nvidia_tools_without_torch(monkeypatch) -> None:
    monkeypatch.setattr(install_models, "is_wsl", lambda: True)
    monkeypatch.setattr(install_models.platform, "machine", lambda: "x86_64")
    monkeypatch.setattr(install_models.shutil, "which", lambda tool: f"/usr/bin/{tool}")

    def fake_run(command, **kwargs):
        if command[0] == "nvcc":
            return "Cuda compilation tools, release 12.8"
        if command[0] == "nvidia-smi":
            return "8.6, 12288"
        raise AssertionError(command)

    monkeypatch.setattr(install_models, "run", fake_run)
    assert install_models.validate_host() == "86"


def test_downloads_only_fixed_components(tmp_path: Path, monkeypatch) -> None:
    settings = Settings(model_cache_dir=tmp_path / "cache")
    calls: list[tuple[str, str]] = []

    def fake_download(*, repo_id, revision, filename, local_dir):
        calls.append((repo_id, filename))
        destination = Path(local_dir) / filename
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(b"model")
        return str(destination)

    monkeypatch.setattr(install_models, "hf_hub_download", fake_download)
    install_models.download_models(settings)

    assert calls[:3] == [
        (FLUX_MODEL_ID, FLUX_MODEL_FILENAME),
        ("Qwen/Qwen3-8B-GGUF", QWEN_MODEL_FILENAME),
        ("Comfy-Org/flux2-klein-4B", FLUX_VAE_FILENAME),
    ]
    assert calls[3:] == [
        (TRELLIS_MODEL_ID, f"q4/{filename}") for filename in TRELLIS_MODEL_FILENAMES
    ]
    assert len(calls) == 3 + len(TRELLIS_MODEL_FILENAMES)


def test_install_dispatches_only_two_pinned_runtimes(
    tmp_path: Path, monkeypatch
) -> None:
    settings = Settings(model_cache_dir=tmp_path / "cache")
    sources: list[tuple[str, str]] = []
    builds: list[tuple[str, tuple[str, ...]]] = []
    monkeypatch.setattr(install_models, "validate_host", lambda: "86")
    monkeypatch.setattr(
        install_models,
        "select_compilers",
        lambda: ("/usr/bin/gcc-14", "/usr/bin/g++-14"),
    )
    monkeypatch.setattr(
        install_models,
        "install_source",
        lambda destination, repository, revision, force: sources.append(
            (repository, revision)
        ),
    )

    def fake_build(
        source,
        build,
        executable,
        revision,
        architecture,
        compilers,
        flags,
        target,
        force,
    ):
        builds.append((target, tuple(flags)))

    monkeypatch.setattr(install_models, "build_runtime", fake_build)
    monkeypatch.setattr(install_models, "download_models", lambda settings: None)
    monkeypatch.setattr(install_models, "verify_installation", lambda settings: None)

    install_models.install(settings, force=False)

    assert [source[0] for source in sources] == [
        STABLE_DIFFUSION_CPP_REPOSITORY,
        TRELLIS_CPP_REPOSITORY,
    ]
    assert builds == [
        ("sd-cli", ("-DSD_CUDA=ON", "-DSD_WEBP=OFF", "-DSD_WEBM=OFF")),
        ("trellis-cli", ("-DGGML_CUDA=ON", "-DTRELLIS_WEBP=OFF")),
    ]


def test_build_reuses_matching_runtime(tmp_path: Path, monkeypatch) -> None:
    source = tmp_path / "source"
    build = source / ".build"
    executable = build / "bin"
    executable.parent.mkdir(parents=True)
    executable.write_bytes(b"binary")
    (build / ".source-revision").write_text("revision\n", encoding="utf-8")
    monkeypatch.setattr(
        install_models,
        "run",
        lambda *args, **kwargs: (_ for _ in ()).throw(
            AssertionError("unexpected build")
        ),
    )

    install_models.build_runtime(
        source,
        build,
        executable,
        "revision",
        "86",
        ("gcc-14", "g++-14"),
        [],
        "target",
        force=False,
    )


def test_readiness_detects_revision_marker_mismatch(
    tmp_path: Path, monkeypatch
) -> None:
    settings = Settings(
        asset_output_dir=tmp_path / "assets",
        model_cache_dir=tmp_path / "cache",
    )
    for path in settings.required_files:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"file")
    settings.flux_model_path.joinpath(".model-revision").write_text(
        "wrong\n", encoding="utf-8"
    )
    monkeypatch.setattr("app.pipeline.is_wsl", lambda: True)
    monkeypatch.setattr("app.pipeline._require_git_revision", lambda *args: None)
    monkeypatch.setattr("app.pipeline._check_help", lambda *args: None)

    from app.pipeline import AssetGenerator

    service = AssetGenerator(settings)
    service.load()
    assert service.ready is False
    assert "revision mismatch" in (service.load_error or "").lower()


def test_verification_detects_missing_required_weight(
    tmp_path: Path, monkeypatch
) -> None:
    settings = Settings(model_cache_dir=tmp_path / "cache")
    for path in settings.required_files[:-1]:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"file")
    monkeypatch.setattr(install_models, "_run_help", lambda *args: None)

    with pytest.raises(RuntimeError, match="required files"):
        install_models.verify_installation(settings)
