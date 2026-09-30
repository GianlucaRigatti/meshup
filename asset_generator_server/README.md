# Local 3D Asset Generator

This FastAPI service converts a text prompt or a spoken description into a
textured GLB with a fixed local pipeline. Audio requests first run through
`qwen3-asr-1.7b` and a `qwen3.5-4b` prompt-cleanup stage; text requests keep
their original behavior and skip both stages.

- `flux2-klein-9b-q4-k-m-fast` generates a 768×768 source image.
- The full FP16 `birefnet-general` checkpoint removes the background at 1024px,
  then crops and recenters the subject on a transparent 768px canvas.
- `trellis2-fast` receives that prematted RGBA image and reconstructs a
  512-resolution, 1024px xatlas-UV textured mesh.
- glTF-Transform welds and simplifies meshes as far as a configurable geometric-
  error limit allows, with a conservative 0.01% default and locked topology
  borders, then resizes embedded textures to a configurable limit (512×512 by
  default) and encodes them as PNG. There is no triangle-count target;
  geometric error and topology determine the result.

The former cross-platform/model-comparison implementation is preserved in
[`../model_experiments`](../model_experiments). It is not part of this server.

## Run with Docker Compose

From the repository root, use Docker Compose 2.24 or newer with NVIDIA GPU
access enabled. On Windows, use Docker Desktop's WSL 2 backend and a current
NVIDIA Windows driver. On Linux, install the NVIDIA driver and configure the
[NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)
for Docker. See [Docker's GPU setup](https://docs.docker.com/compose/how-tos/gpu-support/).
The container requires an x86-64 host and one Ampere-or-newer NVIDIA GPU with
at least 10 GiB VRAM; Docker Desktop on macOS cannot run this GPU pipeline.
CUDA 12.8, Python 3.11, Node.js, and the native build tools are included in the
image, so they do not need to be installed on the host.

Build the image and optionally configure the server:

```bash
docker compose build asset-generator
cp asset_generator_server/.env.example asset_generator_server/.env
```

Read [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), including the FLUX.2 Klein
9B non-commercial terms. Then install the pinned models and compile the native
runtimes once, explicitly accepting their licenses:

```bash
docker compose run --rm asset-generator python scripts/install_models.py --accept-licenses
```

This first installation downloads large checkpoints and can take a while.
The named `asset-models` volume retains weights, native builds, and the isolated
Qwen3.5 runtime across container replacements. This volume is separate from
any existing host `.model_sources` installation, whose native builds and Python
paths may be incompatible with the image. To limit compiler memory usage, add
`-e MAX_JOBS=1` after `run --rm`. Rerun the installation command after updating
model pins or the image's Python dependencies; use `--force` after changing GPUs
or when native builds need to be rebuilt.

Start the server:

```bash
docker compose up -d asset-generator
docker compose logs -f asset-generator
curl --fail http://127.0.0.1:8000/readyz
```

The API listens on port 8000, and the container health check uses `/readyz`.
Generated files remain in `asset_generator_server/generated_assets` on the
host. Server settings are read from `asset_generator_server/.env`; Compose
fixes `MODEL_CACHE_DIR` and `ASSET_OUTPUT_DIR` to the mounted container paths.
Set `PUBLIC_BASE_URL` to the host's LAN-reachable URL for headset clients.
To publish another port or select another GPU, export `ASSET_SERVER_PORT` or
`NVIDIA_GPU_ID` before invoking Compose (defaults: `8000` and `0`).

Stop with `docker compose down`. The model volume is retained unless you run
`docker compose down --volumes`, which deletes it and requires reinstalling models.

## Supported system

The supported runtime is Ubuntu 24.04 under WSL 2 on x86-64, with exactly one
Ampere-or-newer NVIDIA GPU, at least 10 GiB VRAM, and CUDA Toolkit 12.8. Install
the NVIDIA display driver on Windows and the CUDA toolkit—not a Linux display
driver—inside WSL.

Install the build prerequisites inside Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y build-essential cmake ffmpeg git ninja-build
```

Install CUDA Toolkit 12.8 so `nvcc --version` reports release 12.8. CUDA 12.8
requires GCC/G++ 14 or older; the installer selects an installed matching pair
from versions 10 through 14.

Install Node.js 20.9 or newer and npm for the pinned glTF-Transform
postprocessor. Ubuntu 24.04 may still have Node.js 18 installed, which is too
old. If you use `nvm`, upgrade and select the default runtime with:

```bash
nvm install 20
nvm alias default 20
nvm use 20
node --version  # must print v20.9 or newer
```

After changing Node.js versions, install the locked JavaScript dependencies
from the server directory:

```bash
cd asset_generator_server
npm ci
```

Install Python and the server dependencies:

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
uv python install 3.11
cd asset_generator_server
uv sync
```

## Install the fixed models

Read [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), especially the FLUX.2
Klein 9B non-commercial terms, then run:

```bash
MAX_JOBS=2 uv run python scripts/install_models.py --accept-licenses
```

The installer builds only `sd-cli` and `trellis-cli`, downloads the fixed FLUX
Q4_K_M components, the full Qwen3-ASR-1.7B and Qwen3.5-4B checkpoints, the full
BiRefNet-General checkpoint, and the TRELLIS Q4 weights needed by the 512 path.
The two new checkpoints add roughly 13 GB to the installation. Every revision
is pinned. Because the official ASR package requires Transformers 4.57.6 while
Qwen3.5 requires Transformers 5, the installer also creates a small isolated
Qwen3.5 Python runtime under `.model_sources/runtimes/`. It reuses the project's
CUDA PyTorch installation rather than installing a second copy. It also runs
`npm ci` to install the locked glTF-Transform runtime. Use
`MAX_JOBS=1` if WSL is under memory pressure. `--force` replaces and rebuilds
only the two native source trees; downloaded weights are retained.

Weights and source builds are stored under `.model_sources/` by default. No
Hugging Face token is required for the pinned repositories.

## Run

```bash
uv run python -m app.cli --host 127.0.0.1 --port 8000
```

The native launcher requires WSL 2; the Docker image also supports Linux containers. Windows normally forwards the
WSL service to `127.0.0.1:8000`.

Check liveness and readiness:

```bash
curl --fail http://127.0.0.1:8000/healthz
curl --fail http://127.0.0.1:8000/readyz
```

The readiness response identifies the fixed pipeline:

```json
{
  "status": "ready",
  "ready": true,
  "busy": false,
  "image_generator": "flux2-klein-9b-q4-k-m-fast",
  "model_3d": "trellis2-fast",
  "background_removal_model": "birefnet-general",
  "speech_to_text_model": "qwen3-asr-1.7b",
  "prompt_enhancer_model": "qwen3.5-4b",
  "device": "cuda:0",
  "output_mode": "pbr_texture"
}
```

## Generate an asset

```bash
curl --fail \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"prompt":"A small red medieval treasure chest"}' \
  http://127.0.0.1:8000/generate_asset
```

Response:

```json
{
  "url": "http://127.0.0.1:8000/assets/<asset-id>.glb",
  "original_url": "http://127.0.0.1:8000/assets/<asset-id>.original.glb",
  "asset_id": "<asset-id>",
  "cached": false,
  "image_generation_time_ms": 1200,
  "model_generation_time_ms": 3500,
  "generation_time_ms": 4700
}
```

Prompts are whitespace-normalized and combined with the fixed subject-isolation
suffix. A deterministic asset ID and seed are derived from the normalized
prompt and complete pinned pipeline identity. The prompt is passed to the
native image runtime through a temporary file and is never placed in process
arguments or saved in metadata.

The server accepts one uncached generation at a time. Other uncached requests
receive `generator_busy`; complete cached assets remain available.

## Generate an asset from audio

Upload a complete spoken description as multipart form data:

```bash
curl --fail \
  --request POST \
  --form 'audio=@description.m4a' \
  http://127.0.0.1:8000/generate_asset_from_audio
```

The `audio` field accepts WAV, MP3, FLAC, OGG/Vorbis, and M4A/AAC files up to
10 MiB and 60 seconds. The server validates the actual media with `ffprobe`,
converts it to mono 16 kHz PCM, detects the spoken language, transcribes it,
and cleans the transcript into concise English. The cleanup model corrects only
clear recognition, grammar, punctuation, and wording errors; it is explicitly
instructed not to add or infer materials, parts, colors, proportions, finishes,
decorations, or other subject details. A deterministic sanitizer also removes
invented decorative treatments and any camera, composition, lighting, or
background clauses already supplied by the fixed downstream suffix. A
successful response includes `transcript`, `transcript_language`, the cleaned
text in the backward-compatible `enhanced_prompt` field, and timings for all
stages in addition to the normal asset fields.

ASR and prompt cleanup run in separate short-lived GPU subprocesses before
the existing asset pipeline. This keeps the 10 GiB VRAM target but adds model
loading latency to every audio request. The transcript and cleaned prompt are
returned in the response and stored in the generated asset's JSON metadata,
together with their hashes. The uploaded and normalized audio files are deleted
after every request and are never persisted as asset artifacts.

Audio assets use a separate cache identity containing both Qwen revisions and
the fixed cleanup policy. A cache lookup happens after transcription and
cleanup. On a cache hit those two stages still run, while image and mesh
generation report zero milliseconds.

## Artifacts and cache

Each successful uncached request atomically creates:

- `generated_assets/<asset-id>.glb`: textured binary glTF, simplified as far as
  the configured geometric-error limit and border-preservation constraints
  allow, with embedded textures resized to the configured limit and encoded as
  PNG.
- `generated_assets/<asset-id>.original.glb`: untouched 1024px TRELLIS output
  before geometry simplification or texture resizing.
- `generated_assets/<asset-id>.png`: the full-resolution BiRefNet RGBA cutout,
  cropped and centered exactly as in the archived preprocessing path. This is
  the image actually conditioned by TRELLIS.
- `generated_assets/<asset-id>.json`: prompt hash, seed, pinned revisions,
  fixed settings, source/output triangle and texture byte counts, timestamp,
  and stage timings. Audio-generated metadata also
  stores the transcript, cleaned prompt, detected language, and text hashes.

All four files must exist for a cache hit. Cached requests do not run generation
or postprocessing subprocesses and report zero timings. Static artifacts are
served from `/assets/`; API responses continue to point to the smaller
`<asset-id>.glb` asset.

## Configuration

Only operational settings remain:

- `PUBLIC_BASE_URL`: optional base URL returned in generation responses.
- `ASSET_OUTPUT_DIR`: artifact directory; default `generated_assets`.
- `MODEL_CACHE_DIR`: source, build, and model directory; default
  `.model_sources`.
- `IMAGE_TIMEOUT_SECONDS`: FLUX subprocess timeout; default 600.
- `TRELLIS_TIMEOUT_SECONDS`: TRELLIS subprocess timeout; default 1800.
- `GLTF_TRANSFORM_TIMEOUT_SECONDS`: mesh simplification timeout; default 300.
- `MESH_SIMPLIFICATION`: whether meshes are simplified; default `true`. Texture
  processing is controlled independently.
- `TEXTURE_SIMPLIFICATION`: whether embedded textures are resized to the
  configured limit and re-encoded as PNG; default `true`.
- `MAX_TEXTURE_SIZE`: maximum width and height for processed embedded textures;
  default `512`. Aspect ratio is preserved. This setting has no effect when
  `TEXTURE_SIMPLIFICATION=false`.
- `SIMPLIFICATION_ERROR`: maximum geometric error as a fraction of the mesh
  radius, from `0` to `1`; default `0.0001` (0.01%). The simplifier attempts
  maximum reduction without exceeding this limit.
- `AUDIO_MAX_BYTES`: maximum audio upload size; default 10485760.
- `AUDIO_MAX_DURATION_SECONDS`: maximum decoded duration; default 60.
- `AUDIO_DECODE_TIMEOUT_SECONDS`: `ffprobe`/`ffmpeg` timeout; default 30.
- `ASR_TIMEOUT_SECONDS`: Qwen3-ASR subprocess timeout; default 300.
- `PROMPT_ENHANCEMENT_TIMEOUT_SECONDS`: Qwen3.5 subprocess timeout; default 300.
- `LOG_LEVEL`: server log level; default `INFO`.

Values can be exported in the environment or written to `.env`. There are no
model selectors or pipeline profiles.

The command-line equivalents are `--mesh-simplification` /
`--no-mesh-simplification` and `--texture-simplification` /
`--no-texture-simplification`. For example:

```bash
uv run python -m app.cli --no-mesh-simplification
uv run python -m app.cli --no-texture-simplification
```

## Troubleshooting

- A 503 from `/readyz` means a required file, revision marker, Git revision, or
  native executable check failed. Run the installer again and inspect the
  server log.
- If CUDA compilation selects an unsupported host compiler, install a matching
  pair such as `gcc-14` and `g++-14`.
- If native builds exhaust WSL memory, lower `MAX_JOBS` and allocate more WSL
  memory/swap.
- If an audio upload is rejected, confirm that `ffmpeg` and `ffprobe` are
  installed and that the file contains one of the documented codecs.
- If readiness reports a missing or mismatched Qwen3.5 Python runtime, rerun
  `uv run python scripts/install_models.py --accept-licenses`. Do not install
  Transformers 5 into the main project environment; Qwen3-ASR pins 4.57.6.
- If glTF-Transform fails in `sharp` with `Unexpected token 'with'`, check
  `node --version`. Node.js 18 cannot load the pinned dependencies. Select
  Node.js 20.9 or newer, rerun `npm ci` in `asset_generator_server`, and
  restart the server.
- Native stderr/stdout tails are logged server-side on failure, while API
  errors remain intentionally generic.

## Development

```bash
uv run pytest
```

The test suite mocks native execution and downloads; it does not require WSL,
CUDA, or model weights.

After installing every model, an end-to-end audio smoke test can be run with a
short supported speech sample:

```bash
RUN_REAL_MODEL_TESTS=1 AUDIO_SAMPLE_PATH=/path/to/sample.wav \
  uv run pytest tests/test_real_audio_pipeline.py
```
