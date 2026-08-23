# Local 3D Asset Generator

This FastAPI server converts a text prompt into a Unity-ready GLB on Apple
Silicon macOS, native Windows with NVIDIA CUDA, or x86-64 Linux/WSL 2 with
NVIDIA CUDA. A named pipeline preset keeps the platform-specific models and
inference settings reproducible.

## Presets

| Preset | Platform | Pipeline | Output |
| --- | --- | --- | --- |
| `macos-mlx` | Apple Silicon | SD-Turbo → Hunyuan3D 2 Mini MLX | Vertex-colored GLB |
| `windows-cuda-quality` | Windows/NVIDIA | SDXL-Turbo (4 steps) → Stable Fast 3D | 2048px UV/PBR GLB |
| `windows-cuda-fast` | Windows/NVIDIA | SDXL-Turbo (1 step) → Stable Fast 3D | 1024px UV/PBR GLB |
| `linux-cuda-quality` | Linux or WSL 2/NVIDIA | SDXL-Turbo (4 steps) → Stable Fast 3D | 2048px UV/PBR GLB |
| `linux-cuda-fast` | Linux or WSL 2/NVIDIA | SDXL-Turbo (1 step) → Stable Fast 3D | 1024px UV/PBR GLB |

`PIPELINE_PROFILE=auto` selects `macos-mlx` on Apple Silicon and
`windows-cuda-quality` on supported Windows systems, or `linux-cuda-quality`
on x86-64 Linux and WSL 2. Selecting a profile for the wrong platform is an
error. ARM Linux, DirectML, ROCm, and CPU fallback are not supported.

The CUDA profiles target exactly one Ampere-or-newer NVIDIA GPU with at least
10 GiB VRAM and 32 GB system RAM. RTX 50-series GPUs require the pinned PyTorch
CUDA 12.8 build; CUDA 12.4 builds do not contain Blackwell `sm_120` support.

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

## WSL 2 installation (recommended for Windows/NVIDIA)

WSL uses Stable Fast 3D's Linux build and does not require Visual Studio. These
instructions target WSL 2 with Ubuntu 24.04 on x86-64 Windows 10 or 11.

### 1. Prepare Windows and WSL

Install a current
[NVIDIA Windows driver](https://www.nvidia.com/Download/index.aspx), then open
an Administrator PowerShell:

```powershell
wsl --install -d Ubuntu-24.04
```

Restart Windows if requested, launch Ubuntu once to create its Linux username
and password, and then update and verify WSL from PowerShell:

```powershell
wsl --update
wsl --set-default-version 2
wsl --set-version Ubuntu-24.04 2
wsl --list --verbose
```

The Ubuntu entry must show version `2`. Inside Ubuntu, verify that the Windows
driver exposes the GPU:

```bash
nvidia-smi
```

Only install the NVIDIA display driver on Windows. Do **not** install a Linux
NVIDIA driver inside WSL. See NVIDIA's
[CUDA on WSL guide](https://docs.nvidia.com/cuda/wsl-user-guide/).

### 2. Install Linux build tools and CUDA 12.8

Run inside Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y \
  build-essential cmake git curl wget ninja-build pkg-config \
  libgl1 libglib2.0-0

wget https://developer.download.nvidia.com/compute/cuda/repos/wsl-ubuntu/x86_64/cuda-keyring_1.1-1_all.deb
sudo dpkg -i cuda-keyring_1.1-1_all.deb
sudo apt-get update
sudo apt-get install -y cuda-toolkit-12-8
```

Use `cuda-toolkit-12-8` exactly. Do not install the `cuda`, `cuda-12-8`,
`cuda-drivers`, or other driver-bearing metapackages inside WSL.

Add CUDA to the shell environment:

```bash
echo 'export CUDA_HOME=/usr/local/cuda-12.8' >> ~/.bashrc
echo 'export PATH=/usr/local/cuda-12.8/bin:$PATH' >> ~/.bashrc
source ~/.bashrc
nvcc --version
```

`nvcc` must report release 12.8.

### 3. Install the project in WSL

Keep the checkout and model cache in WSL's Linux filesystem, not under
`/mnt/c`; native Linux files are substantially faster for compilation and model
loading.

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
source ~/.bashrc
uv python install 3.11

mkdir -p ~/projects
cd ~/projects
git clone --filter=blob:none --sparse https://github.com/GianlucaRigatti/meshup.git
cd meshup
git sparse-checkout set asset_generator_server
cd asset_generator_server
```

Open the gated
[Stable Fast 3D model page](https://huggingface.co/stabilityai/stable-fast-3d),
accept its terms, and create a read token. Export it only in the current shell:

```bash
export HF_TOKEN="hf_..."
uv sync
uv run python scripts/download_models.py --profile auto --accept-licenses
```

`auto` selects `linux-cuda-quality`. To install explicitly, use
`--profile linux-cuda-quality` or `--profile linux-cuda-fast`. Both profiles
share the same downloaded weights; only their generation settings differ.

### 4. Run WSL server and connect Unity

Inside WSL:

```bash
cp .env.example .env
uv run uvicorn app.main:app --host 127.0.0.1 --port 8000 --workers 1
```

In another WSL terminal:

```bash
curl --fail http://127.0.0.1:8000/readyz
```

From Windows PowerShell, verify the forwarded port:

```powershell
curl.exe --fail http://127.0.0.1:8000/readyz
```

Unity on Windows can continue using `http://127.0.0.1:8000`. WSL normally
forwards Linux services to Windows localhost automatically; see Microsoft's
[WSL networking documentation](https://learn.microsoft.com/en-us/windows/wsl/networking/)
if localhost forwarding is disabled on the machine.

### 5. Optional WSL memory tuning

If model loading is killed for lack of memory, create or edit
`%UserProfile%\.wslconfig` on Windows. For a machine with at least 32 GB RAM, a
starting point is:

```ini
[wsl2]
memory=24GB
swap=16GB
localhostForwarding=true
```

Adjust these limits for the host, then apply them from PowerShell:

```powershell
wsl --shutdown
```

Restart Ubuntu and the server afterward.

### 6. WSL troubleshooting

Run these checks inside Ubuntu:

```bash
nvidia-smi
nvcc --version
command -v gcc g++ git cmake ninja
uv run python -c "import torch; print(torch.__version__, torch.version.cuda, torch.cuda.is_available(), torch.cuda.get_device_name(0))"
curl --fail http://127.0.0.1:8000/readyz
```

Expected PyTorch CUDA version: `12.8`. If Hugging Face returns `401` or `403`,
confirm the model terms were accepted by the same account that created
`HF_TOKEN`. If Windows cannot reach the service, first test it inside WSL, then
run `wsl --shutdown`, restart Ubuntu, and review the WSL networking link above.

## Native Ubuntu Linux installation

Native x86-64 Ubuntu uses the same `linux-cuda-quality` and
`linux-cuda-fast` profiles. Install a supported NVIDIA Linux driver and CUDA
Toolkit 12.8 using NVIDIA's
[Linux installation guide](https://docs.nvidia.com/cuda/archive/12.8.0/cuda-installation-guide-linux/),
then install the build packages and project as shown in the WSL sections above.
Unlike WSL, native Linux requires its own NVIDIA Linux driver. Verify
`nvidia-smi`, `nvcc --version`, and the PyTorch diagnostic before downloading
models.

## Run

Copy `.env.example` to `.env`, then start exactly one worker:

```bash
uv run uvicorn app.main:app --host 127.0.0.1 --port 8000 --workers 1
```

Check readiness:

```bash
curl --fail http://127.0.0.1:8000/readyz
```

The readiness response includes both the configured and resolved profiles.

The following example is from the Linux quality profile:

```json
{
  "status": "ready",
  "ready": true,
  "busy": false,
  "configured_profile": "auto",
  "profile": "linux-cuda-quality",
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

- `PIPELINE_PROFILE`: `auto`, `macos-mlx`, `windows-cuda-quality`,
  `windows-cuda-fast`, `linux-cuda-quality`, or `linux-cuda-fast`
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
statistics, and NVIDIA memory sampling on CUDA platforms. The 2048px quality
preset must be validated on the target laptop; it never silently falls back to
a lower texture resolution.

Read [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) before downloading or
using model weights.
