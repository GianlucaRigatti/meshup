# Local 3D Asset Generator

This FastAPI server converts a text prompt into a textured GLB using an NVIDIA
GPU under WSL 2. The image generator and image-to-3D model are selected
independently when installing models and starting the server.

The supported public environment is WSL 2, Ubuntu 24.04, x86-64, one
Ampere-or-newer NVIDIA GPU, and CUDA Toolkit 12.8. The existing native Windows
and Apple Silicon implementations remain in the source tree as deprecated
compatibility code, but they are no longer exposed by the public CLI.

## Model choices

Image generators:

| CLI value | Model and settings |
| --- | --- |
| `flux2-klein-4b-fp8` | FLUX.2 Klein 4B Distilled FP8, 4 steps, 1024px |
| `flux2-klein-9b-q4-k-m-fast` | FLUX.2 Klein 9B Distilled GGUF Q4_K_M, 4 steps, 768px, balanced latency |
| `flux2-klein-9b-q5-k-m` | FLUX.2 Klein 9B Distilled GGUF Q5_K_M, 4 steps, 1024px |
| `zimage-q4` | Z-Image Turbo GGUF Q4, 8 steps, 1024px; default |
| `zimage-q6` | Z-Image Turbo GGUF Q6, 8 steps, 1024px |
| `zimage-q3-turbo` | Z-Image Turbo GGUF Q3, 6 steps, 768px |
| `sd35-medium-nf4` | Stable Diffusion 3.5 Medium NF4, 28 steps, 1024px |
| `sana-sprint` | Sana-Sprint 1.6B, 2 steps, 1024px |
| `sdxl-turbo-fast` | SDXL-Turbo, 1 step, 512px |
| `sdxl-turbo-quality` | SDXL-Turbo, 4 steps, 512px |

3D models:

| CLI value | Model and settings |
| --- | --- |
| `trellis2-fast` | TRELLIS.2 GGUF Q4, 512 cascade, 1024px atlas; default |
| `trellis2-turbo` | TRELLIS.2 GGUF Q4, 512 cascade, 1024px box UV |
| `trellis2-q4` | TRELLIS.2 GGUF Q4, 1024 cascade, 2048px atlas |
| `trellis2-q8` | TRELLIS.2 GGUF Q8, 1024 cascade, 2048px atlas |
| `instantmesh-fast` | InstantMesh Base, 4 views, 96³ grid, 512px texture |
| `pixal3d` | Pixal3D low-VRAM 1024 cascade, 4096px texture |
| `stable-fast-3d-fast` | Stable Fast 3D, 1024px texture |
| `stable-fast-3d-quality` | Stable Fast 3D, 2048px texture |

Every image choice can be paired with every 3D choice. Z-Image runs in a
short-lived native process, as do both FLUX.2 Klein choices. SD 3.5 also runs
in a disposable process. TRELLIS, InstantMesh, and Pixal3D have isolated
runtimes so image and reconstruction allocations do not overlap.

For a 12 GB GPU, start with `zimage-q4` and `trellis2-fast`. InstantMesh is the
lower-quality/lower-latency experiment. Pixal3D and the TRELLIS 1024 variants
are the most memory-intensive options.

List the exact choices at any time:

```bash
uv run python -m app.cli --list-models
```

## WSL installation

Install a current NVIDIA Windows driver, then run in Administrator PowerShell:

```powershell
wsl --install -d Ubuntu-24.04
wsl --update
wsl --set-default-version 2
wsl --set-version Ubuntu-24.04 2
wsl --list --verbose
```

Inside Ubuntu, `nvidia-smi` must see the GPU. Install the display driver only on
Windows; do not install a Linux NVIDIA driver inside WSL. See NVIDIA's
[CUDA on WSL guide](https://docs.nvidia.com/cuda/wsl-user-guide/).

Install build tools and CUDA Toolkit 12.8 inside Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y \
  build-essential cmake git curl wget ninja-build pkg-config \
  libgl1 libglib2.0-0

wget https://developer.download.nvidia.com/compute/cuda/repos/wsl-ubuntu/x86_64/cuda-keyring_1.1-1_all.deb
sudo dpkg -i cuda-keyring_1.1-1_all.deb
sudo apt-get update
sudo apt-get install -y cuda-toolkit-12-8

echo 'export CUDA_HOME=/usr/local/cuda-12.8' >> ~/.bashrc
echo 'export PATH=/usr/local/cuda-12.8/bin:$PATH' >> ~/.bashrc
source ~/.bashrc
nvcc --version
```

Do not install the `cuda`, `cuda-12-8`, or `cuda-drivers` metapackages inside
WSL. `nvcc` must report release 12.8. CUDA 12.8 does not support GCC 15; on a
system that defaults to GCC 15, install `gcc-14 g++-14` and the installer will
select them automatically.

Keep the checkout and model cache in WSL's Linux filesystem rather than
`/mnt/c`:

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
source ~/.bashrc
uv python install 3.11

git clone --filter=blob:none --sparse https://github.com/GianlucaRigatti/meshup.git
cd meshup
git sparse-checkout set asset_generator_server
cd asset_generator_server
uv sync
```

## Install a selected pair

Install the default low-latency pair:

```bash
MAX_JOBS=2 uv run python scripts/download_models.py \
  --image-generator zimage-q4 \
  --model-3d trellis2-fast \
  --accept-licenses
```

For example, install Z-Image Q4 with InstantMesh instead:

```bash
MAX_JOBS=2 uv run python scripts/download_models.py \
  --image-generator zimage-q4 \
  --model-3d instantmesh-fast \
  --accept-licenses
```

Install either FLUX.2 Klein choice the same way. The installer reuses the
existing `stable-diffusion.cpp` CUDA build:

```bash
MAX_JOBS=2 uv run python scripts/download_models.py \
  --image-generator flux2-klein-4b-fp8 \
  --model-3d trellis2-fast \
  --accept-licenses
```

For the larger 9B model, use `flux2-klein-9b-q4-k-m-fast` for the balanced
768px path or `flux2-klein-9b-q5-k-m` for the 1024px quality path. The balanced
selection uses an 11 GiB graph budget and direct VAE decoding; the quality
selection retains a 10.5 GiB ceiling and tiled VAE decoding. Both use four
distilled steps, native Flash Attention, CPU offload, and the same Qwen3-8B Q4
encoder. The 9B weights are governed by the FLUX non-commercial license; review
the notices before use.

For the measured low-latency pair on a 12 GB GPU:

```bash
MAX_JOBS=2 uv run python scripts/download_models.py \
  --image-generator flux2-klein-9b-q4-k-m-fast \
  --model-3d trellis2-turbo \
  --accept-licenses
```

Already installed runtimes and weights are reused when switching either side.
`MAX_JOBS` defaults to two for native builds; use `MAX_JOBS=1` if WSL is under
memory pressure. Pixal3D also respects `NATTEN_N_WORKERS`.

`sd35-medium-nf4` and both Stable Fast 3D choices use gated Stability AI
weights. Accept the relevant Hugging Face model terms and export a read token
before installing a pair that contains either one:

```bash
export HF_TOKEN="hf_..."
```

Review [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) before downloading or
redistributing any model.

## Run the server

Start the server with the same independent selections:

```bash
uv run python -m app.cli \
  --image-generator zimage-q4 \
  --model-3d trellis2-fast \
  --host 127.0.0.1 \
  --port 8000
```

Only one worker is started because all requests share one GPU. The launcher
rejects non-WSL hosts and does not expose deprecated platform profiles.

Selections may instead be stored in `.env`:

```bash
cp .env.example .env
```

```dotenv
IMAGE_GENERATOR=zimage-q4
MODEL_3D=trellis2-fast
BACKGROUND_REMOVAL_MODEL=birefnet-general
```

Then run:

```bash
uv run python -m app.cli
```

CLI arguments override `.env`. `PIPELINE_PROFILE` is deprecated and is not
part of the public launcher or installer interface.

Check readiness inside WSL:

```bash
curl --fail http://127.0.0.1:8000/readyz
```

The response includes the independently selected models:

```json
{
  "status": "ready",
  "ready": true,
  "busy": false,
  "image_generator": "zimage-q4",
  "model_3d": "trellis2-fast",
  "device": "cuda:0",
  "output_mode": "pbr_texture"
}
```

Windows normally forwards WSL services to localhost, so Unity can use
`http://127.0.0.1:8000`. See Microsoft's
[WSL networking documentation](https://learn.microsoft.com/en-us/windows/wsl/networking/)
if forwarding is disabled.

## API

Generate an asset:

```bash
curl --fail \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"prompt":"A small red medieval treasure chest"}' \
  http://127.0.0.1:8000/generate_asset
```

Example response:

```json
{
  "url": "http://127.0.0.1:8000/assets/<asset-id>.glb",
  "asset_id": "<asset-id>",
  "cached": false,
  "image_generation_time_ms": 1200,
  "model_generation_time_ms": 3500,
  "generation_time_ms": 5000
}
```

Image time covers text-to-image inference. Model time covers image-to-3D
reconstruction. Total time also includes model release, background removal,
preprocessing, validation, and GLB export. All values are zero on a cached
request because no generation stages ran.

Each uncached request creates:

- `generated_assets/<asset-id>.glb`
- `generated_assets/<asset-id>.png`, the preprocessed 3D-model input
- `generated_assets/<asset-id>.json`, revisions, settings, timings, and memory

Native BiRefNet runs additionally record `background_to_gpu_ms`,
`background_inference_ms`, and `background_release_ms`. Its memory section
reports model allocation, inference peak, and post-release CUDA allocation so
12 GB cards can be checked for real headroom instead of relying on estimates.
Native FLUX and TRELLIS subprocesses also record whole-device NVIDIA baseline,
peak, peak delta, and post-process usage under `image_model_*` and `model_3d_*`
keys. These include VRAM used by other processes, while the delta is the useful
per-stage estimate.

Prompts are normalized and hashed but never stored in metadata. The server
accepts one uncached request at a time; concurrent uncached requests receive
`generator_busy`, while cached results remain available.

## Configuration

- `IMAGE_GENERATOR`: public image selection; default `zimage-q4`
- `MODEL_3D`: public 3D selection; default `trellis2-fast`
- `BACKGROUND_REMOVAL_MODEL`: foreground segmentation model; default
  `birefnet-general`. Set `u2netp` only to restore the faster legacy model.
- `PUBLIC_BASE_URL`: base URL returned by the API
- `ASSET_OUTPUT_DIR`: generated asset directory
- `MODEL_CACHE_DIR`: models, pinned sources, and isolated runtimes
- `IMAGE_TIMEOUT_SECONDS`: isolated image subprocess timeout; default 600
- `PIXAL3D_TIMEOUT_SECONDS`: Pixal3D timeout; default 1800
- `TRELLIS_TIMEOUT_SECONDS`: TRELLIS timeout; default 1800
- `INSTANTMESH_TIMEOUT_SECONDS`: InstantMesh timeout; default 1800
- `LOG_LEVEL`: server log level

## WSL memory tuning

For a Windows machine with 32 GB RAM, a reasonable `%UserProfile%\.wslconfig`
starting point is:

```ini
[wsl2]
memory=24GB
swap=16GB
localhostForwarding=true
```

Apply changes with `wsl --shutdown` in PowerShell. Close other GPU applications
before running Pixal3D or a quality TRELLIS variant.

On WSL, BiRefNet-General stays resident in FP16 system RAM. After the image
generator exits, it moves temporarily to CUDA for segmentation, then returns to
CPU and clears the CUDA allocator before TRELLIS starts. This avoids rebuilding
an ONNX session for every request without permanently reserving VRAM.

## Validation and benchmarking

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

The report includes both selected models, per-stage latency, artifact
statistics, and NVIDIA memory use.
