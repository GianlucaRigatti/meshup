from __future__ import annotations

import argparse
import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path

from huggingface_hub import hf_hub_download, snapshot_download

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from app.config import Settings
from app.presets import (
    DINOV2_LARGE_REVISION,
    HUNYUAN_SWIFT_REVISION,
    PIXAL3D_SOURCE_REVISION,
    SF3D_SOURCE_REVISION,
    PipelinePreset,
)

HUNYUAN_REPOSITORY = "https://github.com/ZimengXiong/Hunyuan3D-Swift.git"
SF3D_REPOSITORY = "https://github.com/Stability-AI/stable-fast-3d.git"
PIXAL3D_REPOSITORY = "https://github.com/TencentARC/Pixal3D.git"
TRELLIS2_REPOSITORY = "https://github.com/microsoft/TRELLIS.2.git"
CUMESH_REPOSITORY = "https://github.com/JeffreyXiang/CuMesh.git"
FLEXGEMM_REPOSITORY = "https://github.com/JeffreyXiang/FlexGEMM.git"
NVDIFFRAST_REPOSITORY = "https://github.com/NVlabs/nvdiffrast.git"
NAF_REPOSITORY = "https://github.com/valeoai/NAF.git"
TRELLIS2_SOURCE_REVISION = "75fbf0183001ed9876c8dbb35de6b68552ee08bd"
CUMESH_SOURCE_REVISION = "12289e1062f0603f2f0d0771b02e1395d247f26f"
FLEXGEMM_SOURCE_REVISION = "6dd94a859c26ee8246888502eada3dd8ad85532e"
NVDIFFRAST_SOURCE_REVISION = "253ac4fcea7de5f396371124af597e6cc957bfae"
NAF_SOURCE_REVISION = "37f2dfc180f2de53d98bd601109c0da0dd6b0f43"
PIXAL3D_DINO_REPOSITORY = "camenduru/dinov3-vitl16-pretrain-lvd1689m"
PIXAL3D_DINO_REVISION = "3c276edd87d6f6e569ff0c4400e086807d0f3881"
UTILS3D_WHEEL = (
    "https://github.com/LDYang694/Storages/releases/download/20260430/"
    "utils3d-0.0.2-py3-none-any.whl"
)
WINDOWS_PATCH = PROJECT_ROOT / "scripts" / "patches" / "sf3d-windows.patch"


def run(
    command: list[str], cwd: Path | None = None, env: dict[str, str] | None = None
) -> str:
    try:
        completed = subprocess.run(
            command,
            cwd=cwd,
            env=env,
            check=True,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
        )
    except subprocess.CalledProcessError as error:
        output = (error.stdout or "").strip()
        message = f"Command failed with exit code {error.returncode}: {command[0]}"
        if output:
            message += f"\n{output}"
        raise RuntimeError(message) from error
    return completed.stdout.strip()


def install_source(
    destination: Path,
    repository: str,
    revision: str,
    label: str,
    force: bool,
    *,
    recursive: bool = False,
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
        if recursive:
            run(["git", "submodule", "update", "--init", "--recursive"], cwd=destination)
        print(f"{label} source is present at {destination}")
        return

    destination.parent.mkdir(parents=True, exist_ok=True)
    run(["git", "clone", "--no-checkout", repository, str(destination)])
    run(["git", "checkout", "--detach", revision], cwd=destination)
    if recursive:
        run(["git", "submodule", "update", "--init", "--recursive"], cwd=destination)
    print(f"Installed {label} source at {destination}")


def download_image_model(
    settings: Settings, preset: PipelinePreset, token: str | None = None
) -> None:
    destination = settings.image_model_path
    destination.parent.mkdir(parents=True, exist_ok=True)
    if preset.image.backend == "stable-diffusion-3.5":
        allow_patterns = [
            "LICENSE.md",
            "README.md",
            "model_index.json",
            "scheduler/*",
            "tokenizer/*",
            "tokenizer_2/*",
            "tokenizer_3/*",
            "text_encoder/config.json",
            "text_encoder/model.safetensors",
            "text_encoder_2/config.json",
            "text_encoder_2/model.safetensors",
            "text_encoder_3/config.json",
            "text_encoder_3/model-*.safetensors",
            "text_encoder_3/model.safetensors.index.json",
            "transformer/config.json",
            "transformer/diffusion_pytorch_model.safetensors",
            "vae/config.json",
            "vae/diffusion_pytorch_model.safetensors",
        ]
    elif preset.image.backend == "sana-sprint":
        allow_patterns = [
            "LICENSE",
            "README.md",
            "model_index.json",
            "scheduler/*",
            "tokenizer/*",
            "text_encoder/config.json",
            "text_encoder/model-*.safetensors",
            "text_encoder/model.safetensors.index.json",
            "transformer/config.json",
            "transformer/diffusion_pytorch_model.safetensors",
            "vae/config.json",
            "vae/diffusion_pytorch_model.safetensors",
        ]
    else:
        allow_patterns = [
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
        ]
    snapshot_download(
        repo_id=preset.image.model_id,
        revision=preset.image.revision,
        token=token,
        local_dir=destination,
        allow_patterns=allow_patterns,
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


def _install_cuda_models(
    settings: Settings, preset: PipelinePreset, token: str
) -> None:
    snapshot_download(
        repo_id=preset.asset.model_id,
        revision=preset.asset.revision,
        token=token,
        local_dir=settings.asset_model_path,
        allow_patterns=["LICENSE.md", "README.md", "config.yaml", "model.safetensors"],
    )
    # Always restore the tiny upstream config before making its DINOv2 reference
    # local. This keeps reruns and cache moves between Windows and WSL idempotent.
    hf_hub_download(
        repo_id=preset.asset.model_id,
        filename="config.yaml",
        revision=preset.asset.revision,
        token=token,
        local_dir=settings.asset_model_path,
        force_download=True,
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


def _install_cuda_extensions(
    settings: Settings,
    env: dict[str, str],
    vcvars: Path | None = None,
) -> None:
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
        if vcvars is None:
            run(command, cwd=settings.sf3d_source_path, env=env)
        else:
            _run_in_vs_environment(command, vcvars, settings.sf3d_source_path, env)


def _verify_cuda_extensions(settings: Settings, env: dict[str, str]) -> None:
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


def _cuda_build_environment(architecture: str) -> dict[str, str]:
    return {
        **os.environ,
        "USE_CUDA": "1",
        "USE_NATIVE_ARCH": "0",
        "TORCH_CUDA_ARCH_LIST": architecture,
    }


def install_windows(
    settings: Settings,
    preset: PipelinePreset,
    force: bool,
    *,
    token: str | None = None,
    architecture: str | None = None,
    vcvars: Path | None = None,
) -> None:
    if sys.platform != "win32" or platform.machine().lower() not in {"amd64", "x86_64"}:
        raise RuntimeError("Windows CUDA profiles require native 64-bit Windows.")
    architecture = architecture or _validate_windows_cuda()
    vcvars = vcvars or _find_vcvars64()
    token = token or _require_hugging_face_token()
    install_source(
        settings.sf3d_source_path,
        SF3D_REPOSITORY,
        SF3D_SOURCE_REVISION,
        "Stable Fast 3D",
        force,
    )
    _apply_windows_patch(settings.sf3d_source_path)
    env = {**_cuda_build_environment(architecture), "DISTUTILS_USE_SDK": "1"}
    _install_cuda_extensions(settings, env, vcvars)
    _install_cuda_models(settings, preset, token)
    _verify_cuda_extensions(settings, env)


def _install_pixal3d_python_runtime(
    settings: Settings, architecture: str, force: bool
) -> tuple[Path, dict[str, str]]:
    runtime = settings.pixal3d_runtime_path
    if runtime.exists() and force:
        shutil.rmtree(runtime)
    python = settings.pixal3d_python_path
    if not python.is_file():
        runtime.mkdir(parents=True, exist_ok=True)
        run(["uv", "venv", "--python", "3.11", str(runtime / ".venv")])

    cc, cxx = _select_cuda_host_compilers()
    build_env = {
        **_cuda_build_environment(architecture),
        "CUDA_HOME": "/usr/local/cuda-12.8",
        "CC": cc,
        "CXX": cxx,
        "CUDAHOSTCXX": cxx,
        "NVCC_CCBIN": cc,
        "MAX_JOBS": str(max(1, (os.cpu_count() or 2) // 2)),
        "NATTEN_CUDA_ARCH": architecture,
        "NATTEN_N_WORKERS": str(max(1, (os.cpu_count() or 2) // 2)),
        "TORCH_HOME": str((runtime / "torch").resolve()),
        "HF_HOME": str((runtime / "huggingface").resolve()),
    }
    run(
        [
            "uv",
            "pip",
            "install",
            "--python",
            str(python),
            "--index-url",
            "https://download.pytorch.org/whl/cu128",
            "torch==2.7.1",
            "torchvision==0.22.1",
        ],
        env=build_env,
    )
    run(
        [
            "uv",
            "pip",
            "install",
            "--python",
            str(python),
            "setuptools==69.5.1",
            "wheel==0.45.1",
            "ninja==1.13.0",
            "packaging==25.0",
            "numpy==2.2.6",
            "pillow==12.0.0",
            "imageio==2.37.2",
            "imageio-ffmpeg==0.6.0",
            "tqdm==4.67.1",
            "easydict==1.13",
            "opencv-python-headless==4.12.0.88",
            "trimesh==4.10.1",
            "transformers==4.57.3",
            "zstandard==0.25.0",
            "kornia==0.8.2",
            "timm==1.0.22",
            "diffusers==0.37.1",
            "accelerate==1.13.0",
            "plyfile==1.1.3",
            UTILS3D_WHEEL,
        ],
        env=build_env,
    )
    return python, build_env


def _install_pixal3d_extensions(
    settings: Settings, python: Path, env: dict[str, str], force: bool
) -> None:
    source_root = settings.model_cache_dir / "sources"
    extensions = (
        (
            source_root / "nvdiffrast",
            NVDIFFRAST_REPOSITORY,
            NVDIFFRAST_SOURCE_REVISION,
            "nvdiffrast",
            False,
        ),
        (
            source_root / "CuMesh",
            CUMESH_REPOSITORY,
            CUMESH_SOURCE_REVISION,
            "CuMesh",
            True,
        ),
        (
            source_root / "FlexGEMM",
            FLEXGEMM_REPOSITORY,
            FLEXGEMM_SOURCE_REVISION,
            "FlexGEMM",
            True,
        ),
    )
    for destination, repository, revision, label, recursive in extensions:
        install_source(
            destination,
            repository,
            revision,
            label,
            force,
            recursive=recursive,
        )
        run(
            [
                "uv",
                "pip",
                "install",
                "--python",
                str(python),
                "--no-deps",
                "--no-build-isolation",
                str(destination.resolve()),
            ],
            env=env,
        )

    run(
        [
            "uv",
            "pip",
            "install",
            "--python",
            str(python),
            "--no-deps",
            "--no-build-isolation",
            str((settings.trellis2_source_path / "o-voxel").resolve()),
        ],
        env=env,
    )
    run(
        [
            "uv",
            "pip",
            "install",
            "--python",
            str(python),
            "--no-deps",
            "--no-build-isolation",
            "natten==0.21.0",
        ],
        env=env,
    )


def _download_pixal3d_models(
    settings: Settings, preset: PipelinePreset, python: Path, env: dict[str, str]
) -> None:
    snapshot_download(
        repo_id=preset.asset.model_id,
        revision=preset.asset.revision,
        local_dir=settings.asset_model_path,
    )
    (settings.asset_model_path / ".model-revision").write_text(
        preset.asset.revision + "\n", encoding="utf-8"
    )
    dino_snapshot = Path(
        snapshot_download(
            repo_id=PIXAL3D_DINO_REPOSITORY,
            cache_dir=settings.pixal3d_runtime_path / "huggingface" / "hub",
        )
    )
    if dino_snapshot.name != PIXAL3D_DINO_REVISION:
        raise RuntimeError(
            "The Pixal3D DINOv3 dependency changed upstream; update and verify "
            "its pinned revision before installing."
        )

    naf_source = settings.pixal3d_runtime_path / "torch" / "hub" / "valeoai_NAF_main"
    install_source(
        naf_source,
        NAF_REPOSITORY,
        NAF_SOURCE_REVISION,
        "NAF",
        False,
    )
    run(
        [
            str(python),
            "-c",
            (
                "import torch; torch.hub.load('valeoai/NAF', 'naf', "
                "pretrained=True, device='cpu', trust_repo=True)"
            ),
        ],
        env=env,
    )


def install_pixal3d(
    settings: Settings,
    preset: PipelinePreset,
    force: bool,
    architecture: str,
) -> None:
    if not _is_wsl():
        raise RuntimeError("The wsl-cuda-pixal3d profile requires WSL 2.")
    install_source(
        settings.pixal3d_source_path,
        PIXAL3D_REPOSITORY,
        PIXAL3D_SOURCE_REVISION,
        "Pixal3D",
        force,
    )
    install_source(
        settings.trellis2_source_path,
        TRELLIS2_REPOSITORY,
        TRELLIS2_SOURCE_REVISION,
        "TRELLIS.2",
        force,
        recursive=True,
    )
    python, env = _install_pixal3d_python_runtime(settings, architecture, force)
    _install_pixal3d_extensions(settings, python, env, force)
    _download_pixal3d_models(settings, preset, python, env)
    run(
        [
            str(python),
            "-c",
            (
                "import torch, natten, flex_gemm, cumesh, o_voxel, nvdiffrast.torch; "
                "assert torch.cuda.is_available(); torch.zeros(1, device='cuda'); "
                "import pixal3d"
            ),
        ],
        cwd=settings.pixal3d_source_path,
        env=env,
    )
    print(f"Installed and verified Pixal3D at {settings.asset_model_path}")


def install_linux(
    settings: Settings,
    preset: PipelinePreset,
    force: bool,
    *,
    token: str | None = None,
    architecture: str | None = None,
) -> None:
    if sys.platform != "linux" or platform.machine().lower() not in {
        "amd64",
        "x86_64",
    }:
        raise RuntimeError("Linux CUDA profiles require 64-bit x86 Linux.")
    architecture = architecture or _validate_linux_cuda()
    if preset.asset.backend == "pixal3d":
        install_pixal3d(settings, preset, force, architecture)
        return
    token = token or _require_hugging_face_token()
    install_source(
        settings.sf3d_source_path,
        SF3D_REPOSITORY,
        SF3D_SOURCE_REVISION,
        "Stable Fast 3D",
        force,
    )
    env = _cuda_build_environment(architecture)
    _install_cuda_extensions(settings, env)
    _install_cuda_models(settings, preset, token)
    _verify_cuda_extensions(settings, env)


def _validate_cuda(platform_label: str) -> str:
    if shutil.which("nvcc") is None:
        raise RuntimeError("CUDA Toolkit 12.8 and nvcc must be installed and on PATH.")
    nvcc_version = run(["nvcc", "--version"])
    if "release 12.8" not in nvcc_version:
        raise RuntimeError(
            f"The {platform_label} CUDA profiles require CUDA Toolkit 12.8."
        )
    import torch

    if not torch.cuda.is_available():
        raise RuntimeError("The installed PyTorch build cannot access CUDA.")
    if torch.cuda.device_count() != 1:
        raise RuntimeError(
            f"The {platform_label} CUDA profiles require exactly one NVIDIA GPU."
        )
    if torch.version.cuda is None or tuple(
        map(int, torch.version.cuda.split(".")[:2])
    ) != (
        12,
        8,
    ):
        raise RuntimeError("Use the pinned PyTorch build compiled for CUDA 12.8.")
    properties = torch.cuda.get_device_properties(0)
    if properties.total_memory < 10 * 1024**3:
        raise RuntimeError(
            f"The {platform_label} CUDA profiles require at least 10 GiB VRAM."
        )
    if properties.major < 8:
        raise RuntimeError(
            f"The {platform_label} CUDA profiles require Ampere or newer."
        )
    if not torch.cuda.is_bf16_supported():
        raise RuntimeError(
            f"The {platform_label} CUDA profiles require CUDA BF16 support."
        )
    try:
        torch.zeros(1, device="cuda")
    except Exception as exc:
        raise RuntimeError(
            "The installed PyTorch build cannot allocate a CUDA tensor."
        ) from exc
    return f"{properties.major}.{properties.minor}"


def _validate_windows_cuda() -> str:
    return _validate_cuda("Windows")


def _validate_linux_cuda() -> str:
    missing = [
        tool
        for tool in ("gcc", "g++", "git", "cmake", "ninja")
        if shutil.which(tool) is None
    ]
    if missing:
        raise RuntimeError(
            "The Linux CUDA profiles require these build tools on PATH: "
            + ", ".join(missing)
        )
    return _validate_cuda("Linux")


def _select_cuda_host_compilers() -> tuple[str, str]:
    candidates = [("gcc", "g++")]
    candidates.extend((f"gcc-{major}", f"g++-{major}") for major in range(14, 9, -1))
    for cc_name, cxx_name in candidates:
        cc = shutil.which(cc_name)
        cxx = shutil.which(cxx_name)
        if cc is None or cxx is None:
            continue
        try:
            cc_major = int(run([cc, "-dumpfullversion", "-dumpversion"]).split(".")[0])
            cxx_major = int(
                run([cxx, "-dumpfullversion", "-dumpversion"]).split(".")[0]
            )
        except (RuntimeError, ValueError):
            continue
        if cc_major == cxx_major and 10 <= cc_major <= 14:
            return cc, cxx
    raise RuntimeError(
        "CUDA Toolkit 12.8 requires GCC/G++ 14 or older for native extensions. "
        "Install a matching pair such as `sudo apt-get install gcc-14 g++-14`, "
        "then rerun the installer."
    )


def _is_wsl() -> bool:
    if sys.platform != "linux":
        return False
    try:
        release = Path("/proc/sys/kernel/osrelease").read_text(encoding="utf-8")
    except OSError:
        return False
    return "microsoft" in release.lower()


def _require_hugging_face_token() -> str:
    token = os.getenv("HF_TOKEN") or os.getenv("HUGGING_FACE_HUB_TOKEN")
    if not token:
        raise RuntimeError(
            "Set HF_TOKEN to a read token after accepting the selected Stability "
            "AI model terms on Hugging Face."
        )
    return token


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
            "-version",
            "[17.0,18.0)",
            "-products",
            "*",
            "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property",
            "installationPath",
        ]
    )
    if not installation:
        raise RuntimeError(
            "Visual Studio 2022 with the MSVC v143 x64 tools is required; "
            "no matching installation was found."
        )
    vcvars = Path(installation) / "VC" / "Auxiliary" / "Build" / "vcvars64.bat"
    if not vcvars.is_file():
        raise RuntimeError("Visual Studio's vcvars64.bat was not found.")
    return vcvars


def _run_in_vs_environment(
    command: list[str], vcvars: Path, cwd: Path, env: dict[str, str]
) -> None:
    quoted_command = subprocess.list2cmdline(command)

    batch_file = (cwd / ".build_with_vs.bat").resolve()

    batch_file.write_text(
        "@echo off\n"
        f'call "{vcvars.resolve()}"\n'
        "if errorlevel 1 exit /b %errorlevel%\n"
        f"{quoted_command}\n"
        "exit /b %errorlevel%\n",
        encoding="utf-8",
    )

    try:
        run(
            ["cmd.exe", "/d", "/c", str(batch_file)],
            cwd=cwd,
            env=env,
        )
    finally:
        batch_file.unlink(missing_ok=True)


def _apply_windows_patch(source: Path) -> None:
    check = subprocess.run(
        ["git", "apply", "--check", str(WINDOWS_PATCH)],
        cwd=source,
        check=False,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if check.returncode == 0:
        run(["git", "apply", str(WINDOWS_PATCH)], cwd=source)
        return
    reverse = subprocess.run(
        ["git", "apply", "--reverse", "--check", str(WINDOWS_PATCH)],
        cwd=source,
        check=False,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if reverse.returncode != 0:
        raise RuntimeError("The Stable Fast 3D Windows patch does not apply cleanly.")


def main() -> None:
    parser = argparse.ArgumentParser(description="Install a pipeline preset's models.")
    parser.add_argument(
        "--profile",
        default="auto",
        choices=[
            "auto",
            "macos-mlx",
            "windows-cuda-quality",
            "windows-cuda-fast",
            "windows-cuda-sana",
            "linux-cuda-quality",
            "linux-cuda-fast",
            "linux-cuda-sana",
            "wsl-cuda-pixal3d",
            "wsl-cuda-sd35-pixal3d",
        ],
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
    if preset.name.startswith("wsl-") and not _is_wsl():
        raise RuntimeError(f"The {preset.name} profile requires WSL 2.")
    print(
        "Installing models governed by Stability AI and/or Tencent community "
        "licenses. See THIRD_PARTY_NOTICES.md."
    )
    token: str | None = None
    architecture: str | None = None
    vcvars: Path | None = None
    if preset.platform == "windows":
        architecture = _validate_windows_cuda()
        vcvars = _find_vcvars64()
        token = _require_hugging_face_token()
    elif preset.platform == "linux":
        architecture = _validate_linux_cuda()
        if preset.asset.backend == "pixal3d":
            _select_cuda_host_compilers()
        if (
            preset.asset.backend == "stable-fast-3d"
            or preset.image.backend == "stable-diffusion-3.5"
        ):
            token = _require_hugging_face_token()
    download_image_model(settings, preset, token)
    download_rembg(settings)
    if preset.platform == "macos":
        install_macos(settings, preset, args.force)
    elif preset.platform == "windows":
        install_windows(
            settings,
            preset,
            args.force,
            token=token,
            architecture=architecture,
            vcvars=vcvars,
        )
    else:
        install_linux(
            settings,
            preset,
            args.force,
            token=token,
            architecture=architecture,
        )


if __name__ == "__main__":
    main()
