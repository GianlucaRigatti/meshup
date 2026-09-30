FROM node:22-bookworm-slim AS node
FROM ghcr.io/astral-sh/uv:0.8.15 AS uv
FROM nvidia/cuda:12.8.1-devel-ubuntu24.04

ENV DEBIAN_FRONTEND=noninteractive \
    UV_PYTHON_INSTALL_DIR=/opt/python \
    UV_LINK_MODE=copy \
    PYTHONUNBUFFERED=1 \
    PATH="/app/.venv/bin:${PATH}"

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential ca-certificates cmake ffmpeg git ninja-build pkg-config \
    libcurl4-openssl-dev libssl-dev libgl1 libglib2.0-0 libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx
COPY --from=uv /uv /uvx /usr/local/bin/

WORKDIR /app
COPY pyproject.toml uv.lock .python-version ./
RUN uv sync --locked --no-dev --python 3.11
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
COPY app ./app
COPY scripts ./scripts
COPY README.md THIRD_PARTY_NOTICES.md ./

EXPOSE 8000
CMD ["python", "-m", "app.cli", "--host", "0.0.0.0", "--port", "8000"]
