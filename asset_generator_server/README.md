# Asset generation server

Converts text or spoken descriptions into textured GLB models using FLUX.2 Klein
and TRELLIS.2.

## Docker Compose

Requires an x86-64 host, an Ampere-or-newer NVIDIA GPU with **10 GiB+ VRAM**,
and Docker Compose **2.24+** with GPU access. Use Docker Desktop with WSL 2 on
Windows, or the NVIDIA Container Toolkit on Linux. macOS is unsupported.

From the repository root:

```bash
cp asset_generator_server/.env.example asset_generator_server/.env
```

Set `PUBLIC_BASE_URL` in that file to the server's LAN URL
(e.g. `http://192.168.1.10:8000`) for Quest or remote clients.
Read the [model licenses](THIRD_PARTY_NOTICES.md), including FLUX.2 Klein's
non-commercial terms, before installing:

```bash
docker compose build asset-generator
docker compose run --rm asset-generator python scripts/install_models.py --accept-licenses
docker compose up -d asset-generator
curl --fail http://127.0.0.1:8000/readyz
```

The first install downloads large checkpoints and compiles native runtimes.
Models persist in the `asset-models` volume; outputs are saved in
`asset_generator_server/generated_assets`.

```bash
docker compose logs -f asset-generator
docker compose down
```

## Run directly in WSL 2

Requires Ubuntu 24.04 on x86-64, the same GPU, CUDA Toolkit **12.8**
(`nvcc` on `PATH`), Node.js **20.9+**, and [uv](https://docs.astral.sh/uv/).
Install the NVIDIA display driver on Windows and the CUDA toolkit inside WSL.

```bash
sudo apt-get update
sudo apt-get install -y build-essential cmake ffmpeg git ninja-build
cd asset_generator_server
uv python install 3.11
uv sync
cp .env.example .env
MAX_JOBS=2 uv run python scripts/install_models.py --accept-licenses
uv run python -m app.cli --host 0.0.0.0 --port 8000
```

Set `PUBLIC_BASE_URL` in `.env` for remote clients and make port 8000 reachable
through Windows/WSL networking. Use `MAX_JOBS=1` if compilation runs out of memory.
The installer also installs the JavaScript dependencies.

## Generate

```bash
curl --fail http://127.0.0.1:8000/generate_asset \
  -H 'Content-Type: application/json' \
  -d '{"prompt":"A small red medieval treasure chest"}'

curl --fail http://127.0.0.1:8000/generate_asset_from_audio \
  -F 'audio=@description.m4a'
```

The response includes the GLB download `url`. Audio accepts WAV, MP3, FLAC,
OGG/Vorbis, or M4A/AAC, up to **10 MiB / 60 seconds**.

Settings are in [.env.example](.env.example). For CLI options and tests,
run from `asset_generator_server`:

```bash
uv run python -m app.cli --help
uv run pytest
```
