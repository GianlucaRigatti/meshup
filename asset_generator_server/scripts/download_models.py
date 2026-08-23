from __future__ import annotations

import argparse
import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path

from huggingface_hub import snapshot_download

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from app.config import Settings
from app.presets import (
    DINOV2_LARGE_REVISION,
    HUNYUAN_SWIFT_REVISION,
    SF3D_SOURCE_REVISION,
    PipelinePreset,
)

HUNYUAN_REPOSITORY = "https://github.com/ZimengXiong/Hunyuan3D-Swift.git"
SF3D_REPOSITORY = "https://github.com/Stability-AI/stable-fast-3d.git"
WINDOWS_PATCH = PROJECT_ROOT / "scripts" / "patches" / "sf3d-windows.patch"


def run(
    command: list[str], cwd: Path | None = None, env: dict[str, str] | None = None
) -> str:
    completed = subprocess.run(
        command,
        cwd=cwd,
        env=env,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    return completed.stdout.strip()


def install_source(
    destination: Path, repository: str, revision: str, label: str, force: bool
) -> None:
    if destination.exists() and force:
        shutil.rmtree(destination)
    if destination.exists():
        installed = run(["git", "rev-parse", "HEAD"], cwd=destination)
        if installed != revision:
            raise RuntimeError(
                f"{destination} contains {label} revision {installed}; use --force "
                "to replace that profile's source checkout."
            )
        print(f"{label} source is present at {destination}")
        return

    destination.parent.mkdir(parents=True, exist_ok=True)
    run(["git", "clone", "--no-checkout", repository, str(destination)])
    run(["git", "checkout", "--detach", revision], cwd=destination)
    print(f"Installed {label} source at {destination}")


def download_image_model(settings: Settings, preset: PipelinePreset) -> None:
    destination = settings.image_model_path
    destination.parent.mkdir(parents=True, exist_ok=True)
    snapshot_download(
        repo_id=preset.image.model_id,
        revision=preset.image.revision,
        local_dir=destination,
        allow_patterns=[
            "LICENSE.md",
            "README.md",
            "model_index.json",
            "scheduler/*",
            "tokenizer/*",
            "tokenizer_2/*",
            "text_encoder/config.json",
            "text_encoder/model.fp16.safetensors",
            "text_encoder_2/config.json",
            "text_encoder_2/model.fp16.safetensors",
            "unet/config.json",
            "unet/diffusion_pytorch_model.fp16.safetensors",
            "vae/config.json",
            "vae/diffusion_pytorch_model.fp16.safetensors",
        ],
    )
    (destination / ".model-revision").write_text(
        preset.image.revision + "\n", encoding="utf-8"
    )
    print(f"Installed {preset.image.model_id} at {destination}")


def download_rembg(settings: Settings) -> None:
    rembg_dir = settings.model_cache_dir / "models" / "rembg"
    rembg_dir.mkdir(parents=True, exist_ok=True)
    os.environ["U2NET_HOME"] = str(rembg_dir.resolve())
    import rembg

    rembg.new_session("u2netp")
    print(f"Installed rembg weights at {rembg_dir}")


def install_macos(settings: Settings, preset: PipelinePreset, force: bool) -> None:
    if sys.platform != "darwin" or platform.machine().lower() not in {
        "arm64",
        "aarch64",
    }:
        raise RuntimeError("The macos-mlx profile requires Apple Silicon macOS.")
    install_source(
        settings.hunyuan_source_path,
        HUNYUAN_REPOSITORY,
        HUNYUAN_SWIFT_REVISION,
        "Hunyuan3D-Swift",
        force,
    )
    snapshot_download(
        repo_id=preset.asset.model_id,
        revision=preset.asset.revision,
        local_dir=settings.asset_model_path,
        allow_patterns=["model.fp16.safetensors", "config.yaml", "README.md"],
    )
    (settings.asset_model_path / ".model-revision").write_text(
        preset.asset.revision + "\n", encoding="utf-8"
    )

    runtime = settings.hunyuan_runtime_path
    executable = runtime / "hy3d"
    metallib = (
        runtime
        / "mlx-swift_Cmlx.bundle"
        / "Contents"
        / "Resources"
        / "default.metallib"
    )
    if executable.is_file() and metallib.is_file() and not force:
        print(f"Hunyuan MLX runtime is present at {runtime}")
        return

    build_root = settings.hunyuan_source_path / ".build" / "xcode"
    run(
        [
            "xcodebuild",
            "-scheme",
            "hy3d",
            "-configuration",
            "Release",
            "-destination",
            "platform=macOS",
            "-derivedDataPath",
            ".build/xcode",
            "build",
        ],
        cwd=settings.hunyuan_source_path,
    )
    products = build_root / "Build" / "Products" / "Release"
    if runtime.exists():
        shutil.rmtree(runtime)
    runtime.mkdir(parents=True)
    shutil.copy2(products / "hy3d", executable)
    shutil.copytree(
        products / "mlx-swift_Cmlx.bundle", runtime / "mlx-swift_Cmlx.bundle"
    )
    shutil.rmtree(build_root)
    print(f"Installed the Hunyuan MLX runtime at {runtime}")


def install_windows(settings: Settings, preset: PipelinePreset, force: bool) -> None:
    if sys.platform != "win32" or platform.machine().lower() not in {"amd64", "x86_64"}:
        raise RuntimeError("Windows CUDA profiles require native 64-bit Windows.")
    _validate_windows_cuda()
    vcvars = _find_vcvars64()
    install_source(
        settings.sf3d_source_path,
        SF3D_REPOSITORY,
        SF3D_SOURCE_REVISION,
        "Stable Fast 3D",
        force,
    )
    _apply_windows_patch(settings.sf3d_source_path)
    env = {
        **os.environ,
        "DISTUTILS_USE_SDK": "1",
        "USE_CUDA": "1",
        "USE_NATIVE_ARCH": "0",
        "TORCH_CUDA_ARCH_LIST": "12.0",
    }
    for extension in ("texture_baker", "uv_unwrapper"):
        extension_path = (settings.sf3d_source_path / extension).resolve()
        command = [
            "uv",
            "pip",
            "install",
            "--python",
            sys.executable,
            "--no-build-isolation",
            str(extension_path),
        ]
        _run_in_vs_environment(command, vcvars, settings.sf3d_source_path, env)

    token = os.getenv("HF_TOKEN") or os.getenv("HUGGING_FACE_HUB_TOKEN")
    if not token:
        raise RuntimeError(
            "Set HF_TOKEN to a read token after accepting the Stable Fast 3D "
            "model terms on Hugging Face."
        )
    snapshot_download(
        repo_id=preset.asset.model_id,
        revision=preset.asset.revision,
        token=token,
        local_dir=settings.asset_model_path,
        allow_patterns=["LICENSE.md", "README.md", "config.yaml", "model.safetensors"],
    )
    (settings.asset_model_path / ".model-revision").write_text(
        preset.asset.revision + "\n", encoding="utf-8"
    )
    snapshot_download(
        repo_id="facebook/dinov2-large",
        revision=DINOV2_LARGE_REVISION,
        token=token,
        local_dir=settings.dinov2_model_path,
    )
    (settings.dinov2_model_path / ".model-revision").write_text(
        DINOV2_LARGE_REVISION + "\n", encoding="utf-8"
    )
    config_path = settings.asset_model_path / "config.yaml"
    config = config_path.read_text(encoding="utf-8")
    if "facebook/dinov2-large" not in config:
        raise RuntimeError(
            "The pinned SF3D config no longer contains its expected DINOv2 reference."
        )
    config_path.write_text(
        config.replace(
            "facebook/dinov2-large", settings.dinov2_model_path.resolve().as_posix()
        ),
        encoding="utf-8",
    )
    run(
        [
            sys.executable,
            "-c",
            (
                "import torch, texture_baker, uv_unwrapper; "
                "assert torch.cuda.is_available(); torch.zeros(1, device='cuda')"
            ),
        ],
        env=env,
    )
    print(f"Installed and verified Stable Fast 3D at {settings.asset_model_path}")


def _validate_windows_cuda() -> None:
    if shutil.which("nvcc") is None:
        raise RuntimeError("CUDA Toolkit 12.8 and nvcc must be installed and on PATH.")
    nvcc_version = run(["nvcc", "--version"])
    if "release 12.8" not in nvcc_version:
        raise RuntimeError("The Windows Blackwell profile requires CUDA Toolkit 12.8.")
    import torch

    if not torch.cuda.is_available():
        raise RuntimeError("The installed PyTorch build cannot access CUDA.")
    if torch.cuda.device_count() != 1:
        raise RuntimeError("The Windows profiles require exactly one NVIDIA GPU.")
    if torch.version.cuda is None or tuple(
        map(int, torch.version.cuda.split(".")[:2])
    ) < (12, 8):
        raise RuntimeError("Use a PyTorch build compiled for CUDA 12.8 or newer.")
    properties = torch.cuda.get_device_properties(0)
    if properties.total_memory < 10 * 1024**3:
        raise RuntimeError("The Windows quality presets require at least 10 GiB VRAM.")
    if properties.major < 8:
        raise RuntimeError("The Windows quality presets require Ampere or newer.")
    if not torch.cuda.is_bf16_supported():
        raise RuntimeError("The Windows profiles require CUDA BF16 support.")


def _find_vcvars64() -> Path:
    program_files = os.getenv("ProgramFiles(x86)", r"C:\Program Files (x86)")
    vswhere = (
        Path(program_files) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    )
    if not vswhere.is_file():
        raise RuntimeError(
            "Visual Studio 2022 Build Tools and vswhere.exe are required."
        )
    installation = run(
        [
            str(vswhere),
            "-latest",
            "-products",
            "*",
            "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property",
            "installationPath",
        ]
    )
    vcvars = Path(installation) / "VC" / "Auxiliary" / "Build" / "vcvars64.bat"
    if not vcvars.is_file():
        raise RuntimeError("Visual Studio's vcvars64.bat was not found.")
    return vcvars


def _run_in_vs_environment(
    command: list[str], vcvars: Path, cwd: Path, env: dict[str, str]
) -> None:
    quoted_command = subprocess.list2cmdline(command)
    shell_command = f'call "{vcvars}" && {quoted_command}'
    run(["cmd.exe", "/d", "/s", "/c", shell_command], cwd=cwd, env=env)


def _apply_windows_patch(source: Path) -> None:
    check = subprocess.run(
        ["git", "apply", "--check", str(WINDOWS_PATCH)], cwd=source, check=False
    )
    if check.returncode == 0:
        run(["git", "apply", str(WINDOWS_PATCH)], cwd=source)
        return
    reverse = subprocess.run(
        ["git", "apply", "--reverse", "--check", str(WINDOWS_PATCH)],
        cwd=source,
        check=False,
    )
    if reverse.returncode != 0:
        raise RuntimeError("The Stable Fast 3D Windows patch does not apply cleanly.")


def main() -> None:
    parser = argparse.ArgumentParser(description="Install a pipeline preset's models.")
    parser.add_argument(
        "--profile",
        default="auto",
        choices=["auto", "macos-mlx", "windows-cuda-quality", "windows-cuda-fast"],
    )
    parser.add_argument(
        "--accept-licenses",
        action="store_true",
        help="Confirm acceptance of all source and model license terms.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Replace source/runtime artifacts for the selected profile.",
    )
    args = parser.parse_args()
    if not args.accept_licenses:
        parser.error("--accept-licenses is required before model downloads")

    settings = Settings(PIPELINE_PROFILE=args.profile)
    preset = settings.preset
    print(
        "Installing models governed by Stability AI and/or Tencent community "
        "licenses. See THIRD_PARTY_NOTICES.md."
    )
    if preset.platform == "windows":
        _validate_windows_cuda()
        _find_vcvars64()
        if not (os.getenv("HF_TOKEN") or os.getenv("HUGGING_FACE_HUB_TOKEN")):
            raise RuntimeError(
                "Set HF_TOKEN after accepting the Stable Fast 3D model terms."
            )
    download_image_model(settings, preset)
    download_rembg(settings)
    if preset.platform == "macos":
        install_macos(settings, preset, args.force)
    else:
        install_windows(settings, preset, args.force)


if __name__ == "__main__":
    main()
