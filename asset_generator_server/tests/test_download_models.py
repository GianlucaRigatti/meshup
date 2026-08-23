from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

from app.presets import DINOV2_LARGE_REVISION, PRESETS
from scripts import download_models


def _settings(tmp_path: Path):
    return SimpleNamespace(
        sf3d_source_path=tmp_path / "sources" / "stable-fast-3d",
        asset_model_path=tmp_path / "models" / "stable-fast-3d",
        dinov2_model_path=tmp_path / "models" / "dinov2-large",
    )


def test_run_preserves_failed_command_output(monkeypatch) -> None:
    def fail(*_args, **_kwargs):
        raise subprocess.CalledProcessError(2, ["compiler"], output="compiler exploded")

    monkeypatch.setattr(download_models.subprocess, "run", fail)
    with pytest.raises(RuntimeError, match="compiler exploded"):
        download_models.run(["compiler"])


def test_cuda_build_environment_uses_detected_architecture() -> None:
    env = download_models._cuda_build_environment("8.6")
    assert env["USE_CUDA"] == "1"
    assert env["USE_NATIVE_ARCH"] == "0"
    assert env["TORCH_CUDA_ARCH_LIST"] == "8.6"


def test_linux_extension_install_uses_absolute_paths(tmp_path, monkeypatch) -> None:
    settings = _settings(tmp_path)
    calls = []
    monkeypatch.setattr(
        download_models,
        "run",
        lambda command, cwd=None, env=None: calls.append((command, cwd, env)) or "",
    )

    download_models._install_cuda_extensions(settings, {"USE_CUDA": "1"})

    assert len(calls) == 2
    assert all(Path(command[-1]).is_absolute() for command, _, _ in calls)
    assert all(cwd == settings.sf3d_source_path for _, cwd, _ in calls)


def test_linux_install_does_not_use_visual_studio_or_windows_patch(
    tmp_path, monkeypatch
) -> None:
    settings = _settings(tmp_path)
    events = []
    monkeypatch.setattr(download_models.sys, "platform", "linux")
    monkeypatch.setattr(download_models.platform, "machine", lambda: "x86_64")
    monkeypatch.setattr(
        download_models, "install_source", lambda *_args: events.append("source")
    )
    monkeypatch.setattr(
        download_models,
        "_install_cuda_extensions",
        lambda *_args: events.append("extensions"),
    )
    monkeypatch.setattr(
        download_models, "_install_cuda_models", lambda *_args: events.append("models")
    )
    monkeypatch.setattr(
        download_models,
        "_verify_cuda_extensions",
        lambda *_args: events.append("verify"),
    )
    monkeypatch.setattr(
        download_models,
        "_apply_windows_patch",
        lambda *_args: pytest.fail("Windows patch used on Linux"),
    )
    monkeypatch.setattr(
        download_models,
        "_find_vcvars64",
        lambda: pytest.fail("Visual Studio used on Linux"),
    )

    download_models.install_linux(
        settings,
        PRESETS["linux-cuda-quality"],
        False,
        token="hf_test",
        architecture="8.9",
    )

    assert events == ["source", "extensions", "models", "verify"]


def test_linux_pixal3d_install_dispatches_without_hugging_face_token(
    tmp_path, monkeypatch
) -> None:
    settings = _settings(tmp_path)
    captured = {}
    monkeypatch.setattr(download_models.sys, "platform", "linux")
    monkeypatch.setattr(download_models.platform, "machine", lambda: "x86_64")
    monkeypatch.setattr(
        download_models,
        "install_pixal3d",
        lambda *args: captured.update(args=args),
    )
    monkeypatch.setattr(
        download_models,
        "_require_hugging_face_token",
        lambda: pytest.fail("Pixal3D unexpectedly required a gated-model token"),
    )

    preset = PRESETS["wsl-cuda-pixal3d"]
    download_models.install_linux(
        settings,
        preset,
        False,
        architecture="8.9",
    )

    assert captured["args"] == (settings, preset, False, "8.9")


def test_linux_preflight_reports_missing_build_tools(monkeypatch) -> None:
    monkeypatch.setattr(
        download_models.shutil,
        "which",
        lambda tool: None if tool in {"g++", "cmake"} else f"/usr/bin/{tool}",
    )
    with pytest.raises(RuntimeError, match=r"g\+\+, cmake"):
        download_models._validate_linux_cuda()


def test_cuda_host_compiler_falls_back_from_gcc_15_to_gcc_14(monkeypatch) -> None:
    available = {
        "gcc": "/usr/bin/gcc",
        "g++": "/usr/bin/g++",
        "gcc-14": "/usr/bin/gcc-14",
        "g++-14": "/usr/bin/g++-14",
    }
    monkeypatch.setattr(download_models.shutil, "which", available.get)
    monkeypatch.setattr(
        download_models,
        "run",
        lambda command: "14.2.0" if command[0].endswith("-14") else "15.1.0",
    )

    assert download_models._select_cuda_host_compilers() == (
        "/usr/bin/gcc-14",
        "/usr/bin/g++-14",
    )


def test_cuda_host_compiler_rejects_gcc_15_only(monkeypatch) -> None:
    monkeypatch.setattr(
        download_models.shutil,
        "which",
        lambda tool: f"/usr/bin/{tool}" if tool in {"gcc", "g++"} else None,
    )
    monkeypatch.setattr(download_models, "run", lambda _command: "15.1.0")

    with pytest.raises(RuntimeError, match=r"gcc-14 g\+\+-14"):
        download_models._select_cuda_host_compilers()


def test_missing_hugging_face_token_is_rejected(monkeypatch) -> None:
    monkeypatch.delenv("HF_TOKEN", raising=False)
    monkeypatch.delenv("HUGGING_FACE_HUB_TOKEN", raising=False)
    with pytest.raises(RuntimeError, match="HF_TOKEN"):
        download_models._require_hugging_face_token()


class _Properties:
    total_memory = 12 * 1024**3
    major = 8
    minor = 9


class _Cuda:
    device_count_value = 1

    @staticmethod
    def is_available() -> bool:
        return True

    @classmethod
    def device_count(cls) -> int:
        return cls.device_count_value

    @staticmethod
    def get_device_properties(_index: int):
        return _Properties()

    @staticmethod
    def is_bf16_supported() -> bool:
        return True


def _fake_torch():
    return SimpleNamespace(
        cuda=_Cuda,
        version=SimpleNamespace(cuda="12.8"),
        zeros=lambda *_args, **_kwargs: None,
    )


def test_cuda_validation_returns_gpu_capability(monkeypatch) -> None:
    monkeypatch.setattr(download_models.shutil, "which", lambda _tool: "/bin/nvcc")
    monkeypatch.setattr(download_models, "run", lambda _command: "release 12.8")
    monkeypatch.setitem(sys.modules, "torch", _fake_torch())
    _Cuda.device_count_value = 1
    assert download_models._validate_cuda("Linux") == "8.9"


def test_cuda_validation_rejects_multiple_gpus(monkeypatch) -> None:
    monkeypatch.setattr(download_models.shutil, "which", lambda _tool: "/bin/nvcc")
    monkeypatch.setattr(download_models, "run", lambda _command: "release 12.8")
    monkeypatch.setitem(sys.modules, "torch", _fake_torch())
    _Cuda.device_count_value = 2
    try:
        with pytest.raises(RuntimeError, match="exactly one"):
            download_models._validate_cuda("Linux")
    finally:
        _Cuda.device_count_value = 1


def test_cuda_validation_rejects_missing_toolkit(monkeypatch) -> None:
    monkeypatch.setattr(download_models.shutil, "which", lambda _tool: None)
    with pytest.raises(RuntimeError, match="nvcc"):
        download_models._validate_cuda("Linux")


def test_cuda_validation_rejects_unsupported_gpu(monkeypatch) -> None:
    class _OldProperties(_Properties):
        major = 7

    fake = _fake_torch()
    fake.cuda = type(
        "OldCuda",
        (),
        {
            "is_available": staticmethod(lambda: True),
            "device_count": staticmethod(lambda: 1),
            "get_device_properties": staticmethod(lambda _index: _OldProperties()),
            "is_bf16_supported": staticmethod(lambda: True),
        },
    )
    monkeypatch.setattr(download_models.shutil, "which", lambda _tool: "/bin/nvcc")
    monkeypatch.setattr(download_models, "run", lambda _command: "release 12.8")
    monkeypatch.setitem(sys.modules, "torch", fake)
    with pytest.raises(RuntimeError, match="Ampere"):
        download_models._validate_cuda("Linux")


def test_cuda_model_config_rewrite_is_idempotent(tmp_path, monkeypatch) -> None:
    settings = _settings(tmp_path)

    def snapshot(**kwargs):
        Path(kwargs["local_dir"]).mkdir(parents=True, exist_ok=True)

    def download(**kwargs):
        destination = Path(kwargs["local_dir"]) / kwargs["filename"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text("encoder: facebook/dinov2-large\n", encoding="utf-8")
        return str(destination)

    monkeypatch.setattr(download_models, "snapshot_download", snapshot)
    monkeypatch.setattr(download_models, "hf_hub_download", download)

    for _ in range(2):
        download_models._install_cuda_models(
            settings, PRESETS["linux-cuda-quality"], "hf_test"
        )

    config = (settings.asset_model_path / "config.yaml").read_text(encoding="utf-8")
    assert "facebook/dinov2-large" not in config
    assert settings.dinov2_model_path.resolve().as_posix() in config
    assert (
        settings.dinov2_model_path / ".model-revision"
    ).read_text().strip() == DINOV2_LARGE_REVISION


def test_sana_download_includes_sharded_encoder_and_transformer(
    tmp_path, monkeypatch
) -> None:
    preset = PRESETS["linux-cuda-sana"]
    settings = SimpleNamespace(
        image_model_path=tmp_path / "models" / preset.image.directory_name
    )
    captured = {}

    def snapshot(**kwargs):
        captured.update(kwargs)
        Path(kwargs["local_dir"]).mkdir(parents=True, exist_ok=True)

    monkeypatch.setattr(download_models, "snapshot_download", snapshot)

    download_models.download_image_model(settings, preset)

    assert captured["repo_id"] == preset.image.model_id
    assert captured["revision"] == preset.image.revision
    assert "text_encoder/model-*.safetensors" in captured["allow_patterns"]
    assert (
        "transformer/diffusion_pytorch_model.safetensors"
        in captured["allow_patterns"]
    )
    assert (
        settings.image_model_path / ".model-revision"
    ).read_text().strip() == preset.image.revision


def test_missing_required_snapshot_file_is_force_downloaded(
    tmp_path, monkeypatch
) -> None:
    captured = {}

    def download(**kwargs):
        captured.update(kwargs)
        destination = Path(kwargs["local_dir"]) / kwargs["filename"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text("{}")

    monkeypatch.setattr(download_models, "hf_hub_download", download)

    destination = download_models.ensure_snapshot_file(
        "example/model", "revision", tmp_path, "pipeline.json"
    )

    assert destination == tmp_path / "pipeline.json"
    assert captured["force_download"] is True
    assert captured["revision"] == "revision"


def test_sd35_download_includes_full_t5_and_uses_token(tmp_path, monkeypatch) -> None:
    preset = PRESETS["wsl-cuda-sd35-pixal3d"]
    settings = SimpleNamespace(
        image_model_path=tmp_path / "models" / preset.image.directory_name
    )
    captured = {}

    def snapshot(**kwargs):
        captured.update(kwargs)
        Path(kwargs["local_dir"]).mkdir(parents=True, exist_ok=True)

    monkeypatch.setattr(download_models, "snapshot_download", snapshot)

    download_models.download_image_model(settings, preset, "hf_test")

    assert captured["repo_id"] == "stabilityai/stable-diffusion-3.5-medium"
    assert captured["revision"] == preset.image.revision
    assert captured["token"] == "hf_test"
    assert "text_encoder_3/model-*.safetensors" in captured["allow_patterns"]
    assert "transformer/diffusion_pytorch_model.safetensors" in captured[
        "allow_patterns"
    ]
