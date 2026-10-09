<p align="center">
  <img src="docs/banner.svg" alt="MeshUp Asset Generator — text and voice to textured 3D assets" width="100%">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Python-3.11-3776AB?style=flat" alt="Python 3.11">
  <img src="https://img.shields.io/badge/Docker-Compose-2496ED?style=flat&amp;logo=docker&amp;logoColor=white" alt="Docker Compose">
  <img src="https://img.shields.io/badge/CUDA-12.8-76B900?style=flat" alt="CUDA 12.8">
</p>

<p align="center">
  A local API that turns written or spoken descriptions into textured GLB models.<br>
  Powered by <strong>FLUX.2 Klein</strong> and <strong>TRELLIS.2</strong>.
</p>

<p align="center">
  <a href="#docker-compose">Get started</a> ·
  <a href="#generate">Generate an asset</a> ·
  <a href=".env.example">Configuration</a> ·
  <a href="THIRD_PARTY_NOTICES.md">Model licenses</a>
</p>

---

## Docker Compose

| Requirement | Supported setup |
| --- | --- |
| Host | x86-64 Linux or Windows with WSL 2 |
| GPU | Ampere-or-newer NVIDIA GPU with **10 GiB+ VRAM** |
| Docker | Compose **2.24+** with GPU access |

Use the NVIDIA Container Toolkit on Linux, or Docker Desktop with WSL 2 on
Windows. macOS is unsupported.

Run all Docker commands from the **repository root**.

### 1. Configure

```bash
cp asset_generator_server/.env.example asset_generator_server/.env
```

Set `PUBLIC_BASE_URL` in that file to the server's LAN URL
(e.g. `http://192.168.1.10:8000`) for Quest or remote clients.

### 2. Build

```bash
docker compose build asset-generator
```

### 3. Install the models

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

### 4. Start

```bash
docker compose up -d asset-generator
curl --fail http://127.0.0.1:8000/readyz
```

## Generate

### From text

```bash
curl --fail http://127.0.0.1:8000/generate_asset \
  -H 'Content-Type: application/json' \
  -d '{"prompt":"A small red medieval treasure chest"}'
```

### From audio

```bash
curl --fail http://127.0.0.1:8000/generate_asset_from_audio \
  -F 'audio=@description.m4a'
```

The response includes the GLB download `url`. Audio accepts WAV, MP3, FLAC,
OGG/Vorbis, or M4A/AAC, up to **10 MiB / 60 seconds**.

## Manage the service

Follow logs:

```bash
docker compose logs -f asset-generator
```

Stop the service:

```bash
docker compose stop asset-generator
```

Show CLI options:

```bash
docker compose run --rm asset-generator python -m app.cli --help
```

See [.env.example](.env.example) for all settings and
[third-party notices](THIRD_PARTY_NOTICES.md) for model licenses.
