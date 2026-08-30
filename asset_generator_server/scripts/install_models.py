from __future__ import annotations

import argparse
import os
import platform
import shutil
import site
import subprocess
import sys
from pathlib import Path

from huggingface_hub import hf_hub_download, snapshot_download

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from app.config import (
    ASR_MODEL_ID,
    ASR_MODEL_REVISION,
    BIREFNET_MODEL_ID,
    BIREFNET_MODEL_REVISION,
    FLUX_MODEL_FILENAME,
    FLUX_MODEL_ID,
    FLUX_MODEL_REVISION,
    FLUX_VAE_FILENAME,
    FLUX_VAE_MODEL_ID,
    FLUX_VAE_REVISION,
    PROMPT_ENHANCER_MODEL_ID,
    PROMPT_ENHANCER_MODEL_REVISION,
    PROMPT_ENHANCER_TRANSFORMERS_VERSION,
    QWEN_MODEL_FILENAME,
    QWEN_MODEL_ID,
    QWEN_MODEL_REVISION,
    STABLE_DIFFUSION_CPP_REPOSITORY,
    STABLE_DIFFUSION_CPP_REVISION,
    TRELLIS_CPP_REPOSITORY,
    TRELLIS_CPP_REVISION,
    TRELLIS_MODEL_FILENAMES,
    TRELLIS_MODEL_ID,
    TRELLIS_MODEL_REVISION,
    Settings,
    is_wsl,
)

CUDA_HOME = Path("/usr/local/cuda-12.8")


def run(
    command: list[str],
    *,
    cwd: Path | None = None,
    env: dict[str, str] | None = None,
) -> str:
    try:
        completed = subprocess.run(
            command,
            cwd=cwd,
            env=env,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
        )
    except subprocess.CalledProcessError as exc:
        output = (exc.stdout or "").strip()
        message = f"Command failed with exit code {exc.returncode}: {command[0]}"
        if output:
            message += f"\n{output[-4000:]}"
        raise RuntimeError(message) from exc
    return completed.stdout.strip()


def validate_host() -> str:
    if not is_wsl() or platform.machine().lower() not in {"amd64", "x86_64"}:
        raise RuntimeError("The installer requires x86-64 WSL 2.")
    required = (
        "git",
        "cmake",
        "ninja",
        "nvcc",
        "nvidia-smi",
        "ffmpeg",
        "ffprobe",
        "node",
        "npm",
        "uv",
    )
    missing = [tool for tool in required if shutil.which(tool) is None]
    if missing:
        raise RuntimeError("Missing required tools: " + ", ".join(missing))
    try:
        node_major = int(run(["node", "--version"]).lstrip("v").split(".", 1)[0])
    except ValueError as exc:
        raise RuntimeError("Could not determine the Node.js version.") from exc
    if node_major < 20:
        raise RuntimeError("Node.js 20 or newer is required for glTF-Transform.")
    if "release 12.8" not in run(["nvcc", "--version"]):
        raise RuntimeError("CUDA Toolkit 12.8 is required.")

    gpu_lines = [
        line.strip()
        for line in run(
            [
                "nvidia-smi",
                "--query-gpu=compute_cap,memory.total",
                "--format=csv,noheader,nounits",
            ]
        ).splitlines()
        if line.strip()
    ]
    if len(gpu_lines) != 1:
        raise RuntimeError("Exactly one NVIDIA GPU is required.")
    try:
        capability_text, memory_text = (
            part.strip() for part in gpu_lines[0].split(",", 1)
        )
        major, minor = (int(part) for part in capability_text.split(".", 1))
        memory_mib = int(float(memory_text))
    except (TypeError, ValueError) as exc:
        raise RuntimeError("Could not parse NVIDIA GPU capabilities.") from exc
    if major < 8:
        raise RuntimeError("An Ampere-or-newer NVIDIA GPU is required.")
    if memory_mib < 10 * 1024:
        raise RuntimeError("At least 10 GiB of NVIDIA VRAM is required.")
    return f"{major}{minor}"


def select_compilers() -> tuple[str, str]:
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
        "CUDA Toolkit 12.8 requires a matching GCC/G++ pair from version 10 to 14."
    )


def install_source(
    destination: Path,
    repository: str,
    revision: str,
    *,
    force: bool,
) -> None:
    if force and destination.exists():
        shutil.rmtree(destination)
    if not destination.exists():
        destination.parent.mkdir(parents=True, exist_ok=True)
        run(["git", "clone", "--recursive", repository, str(destination)])
    elif not (destination / ".git").is_dir():
        raise RuntimeError(f"Existing source path is not a Git checkout: {destination}")
    current = run(["git", "-C", str(destination), "rev-parse", "HEAD"])
    if current != revision:
        run(["git", "-C", str(destination), "fetch", "origin", revision])
        run(["git", "-C", str(destination), "checkout", "--detach", revision])
    run(
        [
            "git",
            "-C",
            str(destination),
            "submodule",
            "update",
            "--init",
            "--recursive",
        ]
    )


def build_runtime(
    source: Path,
    build: Path,
    executable: Path,
    revision: str,
    architecture: str,
    compilers: tuple[str, str],
    cmake_flags: list[str],
    target: str,
    *,
    force: bool,
) -> None:
    marker = build / ".source-revision"
    installed_revision = (
        marker.read_text(encoding="utf-8").strip() if marker.is_file() else None
    )
    if executable.is_file() and installed_revision == revision and not force:
        return
    cc, cxx = compilers
    jobs = _build_jobs()
    environment = {
        **os.environ,
        "CC": cc,
        "CXX": cxx,
        "CUDAHOSTCXX": cxx,
        "CUDA_HOME": str(CUDA_HOME),
        "CUDACXX": str(CUDA_HOME / "bin" / "nvcc"),
    }
    run(
        [
            "cmake",
            "-S",
            str(source.resolve()),
            "-B",
            str(build.resolve()),
            "-G",
            "Ninja",
            "-DCMAKE_BUILD_TYPE=Release",
            f"-DCMAKE_CUDA_COMPILER={CUDA_HOME / 'bin' / 'nvcc'}",
            f"-DCMAKE_CUDA_HOST_COMPILER={cxx}",
            f"-DCMAKE_CUDA_ARCHITECTURES={architecture}",
            *cmake_flags,
        ],
        env=environment,
    )
    run(
        [
            "cmake",
            "--build",
            str(build.resolve()),
            "--target",
            target,
            "--parallel",
            str(jobs),
        ],
        env=environment,
    )
    if not executable.is_file():
        raise RuntimeError(f"The build did not create {executable}.")
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text(revision + "\n", encoding="utf-8")


def download_models(settings: Settings) -> None:
    settings.flux_model_path.mkdir(parents=True, exist_ok=True)
    downloads = (
        (FLUX_MODEL_ID, FLUX_MODEL_REVISION, FLUX_MODEL_FILENAME),
        (QWEN_MODEL_ID, QWEN_MODEL_REVISION, QWEN_MODEL_FILENAME),
        (FLUX_VAE_MODEL_ID, FLUX_VAE_REVISION, FLUX_VAE_FILENAME),
    )
    for repo_id, revision, filename in downloads:
        hf_hub_download(
            repo_id=repo_id,
            revision=revision,
            filename=filename,
            local_dir=settings.flux_model_path,
        )
    _write_marker(settings.flux_model_path / ".model-revision", FLUX_MODEL_REVISION)
    _write_marker(
        settings.flux_model_path / ".text-encoder-revision", QWEN_MODEL_REVISION
    )
    _write_marker(settings.flux_model_path / ".vae-revision", FLUX_VAE_REVISION)

    snapshot_download(
        repo_id=BIREFNET_MODEL_ID,
        revision=BIREFNET_MODEL_REVISION,
        local_dir=settings.background_removal_model_path,
    )
    _write_marker(
        settings.background_removal_model_path / ".model-revision",
        BIREFNET_MODEL_REVISION,
    )

    snapshot_download(
        repo_id=ASR_MODEL_ID,
        revision=ASR_MODEL_REVISION,
        local_dir=settings.asr_model_path,
    )
    _write_marker(settings.asr_model_path / ".model-revision", ASR_MODEL_REVISION)

    snapshot_download(
        repo_id=PROMPT_ENHANCER_MODEL_ID,
        revision=PROMPT_ENHANCER_MODEL_REVISION,
        local_dir=settings.prompt_enhancer_model_path,
    )
    _write_marker(
        settings.prompt_enhancer_model_path / ".model-revision",
        PROMPT_ENHANCER_MODEL_REVISION,
    )

    settings.trellis_model_root.mkdir(parents=True, exist_ok=True)
    for filename in TRELLIS_MODEL_FILENAMES:
        hf_hub_download(
            repo_id=TRELLIS_MODEL_ID,
            revision=TRELLIS_MODEL_REVISION,
            filename=f"q4/{filename}",
            local_dir=settings.trellis_model_root,
        )
    _write_marker(
        settings.trellis_model_root / ".model-revision", TRELLIS_MODEL_REVISION
    )


def install_prompt_runtime(settings: Settings) -> None:
    runtime = settings.prompt_enhancer_runtime_path
    python = settings.prompt_enhancer_python_path
    if not python.is_file():
        runtime.parent.mkdir(parents=True, exist_ok=True)
        run(
            [
                "uv",
                "venv",
                "--python",
                sys.executable,
                str(runtime.resolve()),
            ]
        )
    run(
        [
            "uv",
            "pip",
            "install",
            "--python",
            str(python.absolute()),
            f"transformers=={PROMPT_ENHANCER_TRANSFORMERS_VERSION}",
        ]
    )

    parent_site = next(
        (
            Path(path).resolve()
            for path in site.getsitepackages()
            if Path(path).joinpath("torch").is_dir()
        ),
        None,
    )
    if parent_site is None:
        raise RuntimeError("Could not locate the project CUDA PyTorch installation.")
    child_site = Path(
        run(
            [
                str(python.absolute()),
                "-c",
                "import site; print(site.getsitepackages()[0])",
            ]
        )
    )
    child_site.joinpath("asset-generator-project-runtime.pth").write_text(
        str(parent_site) + "\n", encoding="utf-8"
    )
    _write_marker(
        runtime / ".transformers-version", PROMPT_ENHANCER_TRANSFORMERS_VERSION
    )


def install_gltf_transform() -> None:
    run(["npm", "ci", "--omit=dev"], cwd=PROJECT_ROOT)


def verify_installation(settings: Settings) -> None:
    missing = [path.resolve() for path in settings.required_files if not path.is_file()]
    if not settings.background_removal_model_path.is_dir():
        missing.append(settings.background_removal_model_path.resolve())
    if missing:
        raise RuntimeError(
            "Installation did not create the required files:\n- "
            + "\n- ".join(str(path) for path in missing)
        )
    _run_help(
        settings.stable_diffusion_executable_path,
        settings.stable_diffusion_executable_path.parent,
    )
    _run_help(settings.trellis_executable_path, settings.trellis_build_path)
    _verify_prompt_runtime(settings.prompt_enhancer_python_path)
    run(["node", str(PROJECT_ROOT / "scripts" / "simplify_glb.mjs"), "--help"])


def _verify_prompt_runtime(python: Path) -> None:
    run(
        [
            str(python.absolute()),
            "-c",
            (
                "import torch, transformers; "
                f"assert transformers.__version__ == "
                f"'{PROMPT_ENHANCER_TRANSFORMERS_VERSION}'; "
                "from transformers import AutoModelForMultimodalLM, AutoProcessor"
            ),
        ]
    )


def install(settings: Settings, *, force: bool) -> None:
    architecture = validate_host()
    compilers = select_compilers()
    install_source(
        settings.stable_diffusion_source_path,
        STABLE_DIFFUSION_CPP_REPOSITORY,
        STABLE_DIFFUSION_CPP_REVISION,
        force=force,
    )
    build_runtime(
        settings.stable_diffusion_source_path,
        settings.stable_diffusion_build_path,
        settings.stable_diffusion_executable_path,
        STABLE_DIFFUSION_CPP_REVISION,
        architecture,
        compilers,
        ["-DSD_CUDA=ON", "-DSD_WEBP=OFF", "-DSD_WEBM=OFF"],
        "sd-cli",
        force=force,
    )
    install_source(
        settings.trellis_source_path,
        TRELLIS_CPP_REPOSITORY,
        TRELLIS_CPP_REVISION,
        force=force,
    )
    build_runtime(
        settings.trellis_source_path,
        settings.trellis_build_path,
        settings.trellis_executable_path,
        TRELLIS_CPP_REVISION,
        architecture,
        compilers,
        ["-DGGML_CUDA=ON", "-DTRELLIS_WEBP=OFF"],
        "trellis-cli",
        force=force,
    )
    install_prompt_runtime(settings)
    install_gltf_transform()
    download_models(settings)
    verify_installation(settings)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Install the fixed Qwen speech-to-prompt, FLUX.2 Klein, and "
            "TRELLIS.2 pipeline."
        )
    )
    parser.add_argument(
        "--accept-licenses",
        action="store_true",
        help="Confirm acceptance of all source and model license terms.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Replace and rebuild only the two pinned native source trees.",
    )
    return parser


def main(argv: list[str] | None = None) -> None:
    parser = build_parser()
    args = parser.parse_args(argv)
    if not args.accept_licenses:
        parser.error("--accept-licenses is required before model downloads")
    try:
        settings = Settings.from_env()
        print(
            "Installing fixed third-party models and runtimes. See THIRD_PARTY_NOTICES.md."
        )
        install(settings, force=args.force)
    except (RuntimeError, ValueError) as exc:
        parser.exit(1, f"error: {exc}\n")
    print("Installed and verified the fixed speech-to-3D asset pipeline.")


def _run_help(executable: Path, binary_dir: Path) -> None:
    existing = os.environ.get("LD_LIBRARY_PATH")
    library_path = str(binary_dir.resolve())
    if existing:
        library_path += f":{existing}"
    run(
        [str(executable.resolve()), "--help"],
        env={**os.environ, "LD_LIBRARY_PATH": library_path},
    )


def _write_marker(path: Path, revision: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(revision + "\n", encoding="utf-8")


def _build_jobs() -> int:
    value = os.environ.get("MAX_JOBS", str(min(2, max(1, (os.cpu_count() or 2) // 2))))
    try:
        jobs = int(value)
    except ValueError as exc:
        raise RuntimeError("MAX_JOBS must be a positive integer.") from exc
    if jobs <= 0:
        raise RuntimeError("MAX_JOBS must be a positive integer.")
    return jobs


if __name__ == "__main__":
    main()
