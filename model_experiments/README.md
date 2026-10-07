# Model experiments

Archived text-to-3D server for comparing image and reconstruction models.
For the game, use [asset_generator_server](../asset_generator_server/README.md).

## Install

Requires WSL 2 with Ubuntu 24.04 on x86-64, an Ampere-or-newer NVIDIA GPU
(12 GB VRAM for the default pair), CUDA Toolkit **12.8** with `nvcc` on `PATH`,
and [uv](https://docs.astral.sh/uv/). Install the NVIDIA display driver on Windows
and the CUDA toolkit inside WSL. Keep the checkout in WSL's Linux filesystem.

```bash
sudo apt-get update
sudo apt-get install -y build-essential cmake git ninja-build pkg-config libgl1 libglib2.0-0
cd model_experiments
uv python install 3.11
uv sync
```

Read the [model licenses](THIRD_PARTY_NOTICES.md), then install a pair:

```bash
MAX_JOBS=2 uv run python scripts/download_models.py \
  --image-generator zimage-q4 --model-3d trellis2-fast --accept-licenses
```

Use `MAX_JOBS=1` if compilation runs out of memory. Gated Stability AI models
require accepting their Hugging Face terms and setting `HF_TOKEN`.

## Run

Use the same model selections for installation and startup:

```bash
uv run python -m app.cli --image-generator zimage-q4 --model-3d trellis2-fast
curl --fail http://127.0.0.1:8000/readyz
curl --fail http://127.0.0.1:8000/generate_asset \
  -H 'Content-Type: application/json' \
  -d '{"prompt":"A small red medieval treasure chest"}'
```

The response includes the GLB download `url`; files are saved in `generated_assets`.
List available models with `uv run python -m app.cli --list-models`.
Optional settings are in [.env.example](.env.example).

## Tests and benchmarks

```bash
uv run pytest
# Requires installed models:
RUN_REAL_MODEL_TESTS=1 uv run pytest -m real_models
# Requires a running server:
uv run python scripts/benchmark.py --runs 5
```
