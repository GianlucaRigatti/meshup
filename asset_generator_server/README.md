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

### Build the image

```bash
docker compose build asset-generator
```

### Install the models

Read the [model licenses](THIRD_PARTY_NOTICES.md), including FLUX.2 Klein's
non-commercial terms, before installing. Replace `<n_cpu_threads>` with the
number of CPU threads to use for compilation (for example, `2`):

```bash
docker compose run --rm -e MAX_JOBS=<n_cpu_threads> asset-generator python scripts/install_models.py --accept-licenses
```

The first install downloads large checkpoints and compiles native runtimes.
Reduce `MAX_JOBS` to `1` if compilation runs out of memory.
Models persist in the `asset-models` volume; outputs are saved in
`asset_generator_server/generated_assets`.

### Start the service

```bash
docker compose up -d asset-generator
curl --fail http://127.0.0.1:8000/readyz
```

View logs or stop the service from the repository root:

```bash
docker compose logs -f asset-generator
docker compose down
```

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

Settings are in [.env.example](.env.example). For CLI options, run from the
repository root:

```bash
docker compose run --rm asset-generator python -m app.cli --help
```
