# Local 3D Asset Generator

This FastAPI server converts a text prompt into a Unity-ready GLB on Apple
Silicon macOS or native Windows with NVIDIA CUDA. A named pipeline preset keeps
the platform-specific models and inference settings reproducible.

## Presets

| Preset | Platform | Pipeline | Output |
| --- | --- | --- | --- |
| `macos-mlx` | Apple Silicon | SD-Turbo → Hunyuan3D 2 Mini MLX | Vertex-colored GLB |
| `windows-cuda-quality` | Windows/NVIDIA | SDXL-Turbo (4 steps) → Stable Fast 3D | 2048px UV/PBR GLB |
| `windows-cuda-fast` | Windows/NVIDIA | SDXL-Turbo (1 step) → Stable Fast 3D | 1024px UV/PBR GLB |

`PIPELINE_PROFILE=auto` selects `macos-mlx` on Apple Silicon and
`windows-cuda-quality` on supported Windows systems. Selecting a profile for
the wrong platform is an error. Linux, WSL, DirectML, and CPU fallback are not
supported.

The Windows profile targets one Blackwell/Ampere-or-newer NVIDIA GPU with at
least 10 GiB VRAM and 32 GB system RAM. RTX 50-series GPUs require the pinned
PyTorch CUDA 12.8 build; CUDA 12.4 builds do not contain Blackwell `sm_120`
support.

## macOS installation

Requirements:

- Apple Silicon Mac
- Xcode command-line tools
- Enough free disk space for the model cache

Install and download the automatically selected preset:

```bash
xcode-select --install
uv sync
uv run python scripts/download_models.py --profile auto --accept-licenses
```

The existing Mac pipeline uses six Hunyuan steps, octree resolution 48, and
4-bit MLX weights.

## Native Windows installation

Requirements:

- 64-bit Windows 10 or 11
- Python 3.11 x64
- NVIDIA Ampere or newer GPU with at least 10 GiB VRAM
- 32 GB system RAM and at least 25 GB free disk space
- A current NVIDIA driver
- CUDA Toolkit 12.8 with `nvcc` on `PATH`
- Visual Studio 2022 Build Tools with Desktop development with C++, MSVC v143,
  and a Windows SDK
- Windows long-path support enabled

Stable Fast 3D is gated. Accept its terms on Hugging Face and provide a
read-only token for installation:

```powershell
$env:HF_TOKEN = "hf_..."
uv sync
uv run python scripts/download_models.py --profile auto --accept-licenses
```

The installer locates Visual Studio with `vswhere.exe`, applies the pinned
Windows compatibility patch, compiles `texture_baker` and `uv_unwrapper`, and
tests both extensions on CUDA. Tokens are not logged.

Stable Fast 3D's native Windows support is experimental. If compilation fails,
confirm that `nvcc --version` reports CUDA 12.8 and that the x64 MSVC v143 tools
are installed. Re-run with `--force` only when replacing the selected preset's
source and runtime artifacts.

## Run

Copy `.env.example` to `.env`, then start exactly one worker:

```bash
uv run uvicorn app.main:app --host 127.0.0.1 --port 8000 --workers 1
```

Check readiness:

```bash
curl --fail http://127.0.0.1:8000/readyz
```

The readiness response includes both the configured and resolved profiles:

```json
{
  "status": "ready",
  "ready": true,
  "busy": false,
  "configured_profile": "auto",
  "profile": "windows-cuda-quality",
  "device": "cuda:0",
  "output_mode": "pbr_texture"
}
```

Generate an asset:

```bash
curl --fail \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"prompt":"A small red medieval treasure chest"}' \
  http://127.0.0.1:8000/generate_asset
```

The request API is unchanged:

```json
{
  "url": "http://127.0.0.1:8000/assets/<asset-id>.glb",
  "asset_id": "<asset-id>",
  "cached": false,
  "generation_time_ms": 5000
}
```

Prompts are limited to 500 characters and normalized before hashing. Model
revisions, the resolved preset, and all output-affecting settings are part of
the cache identity. The original prompt is never written to metadata.

The server accepts one uncached generation at a time. A second uncached request
receives `generator_busy`; cached requests remain available.

## Configuration

- `PIPELINE_PROFILE`: `auto`, `macos-mlx`, `windows-cuda-quality`, or
  `windows-cuda-fast`
- `PUBLIC_BASE_URL`: public URL used in responses
- `ASSET_OUTPUT_DIR`: generated GLB and metadata directory
- `MODEL_CACHE_DIR`: models, pinned sources, and runtimes
- `HUNYUAN_TIMEOUT_SECONDS`: macOS Hunyuan subprocess timeout
- `LOG_LEVEL`: server log level

The former `GENERATION_DEVICE`, `HUNYUAN_STEPS`,
`HUNYUAN_OCTREE_RESOLUTION`, and `HUNYUAN_QUANTIZATION` variables are no longer
used. Choose a validated preset instead.

## Tests and benchmark

Run the fast suite:

```bash
uv run pytest
```

Run the installed real pipeline:

```bash
RUN_REAL_MODEL_TESTS=1 uv run pytest -m real_models
```

With the server running, benchmark uncached and cached requests:

```bash
uv run python scripts/benchmark.py --runs 5
```

The report includes the resolved profile, per-stage latency, artifact
statistics, and NVIDIA memory sampling on Windows. The 2048px quality preset
must be validated on the target laptop; it never silently falls back to a lower
texture resolution.

Read [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) before downloading or
using model weights.
