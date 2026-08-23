# Third-party notices

This project downloads and uses the following model software and weights.

## Hunyuan3D 2 Mini for MLX

- MLX-Swift source: <https://github.com/ZimengXiong/Hunyuan3D-Swift>
- Source revision: `292331f4d26ddb80b9dcea6bcb5629ff82f12b82`
- Original model: `tencent/Hunyuan3D-2mini`
- Repacked MLX checkpoint: `zimengxiong/hunyuan3d-mlx-shape-small`
- Source license: MIT
- Weight license: Tencent Hunyuan3D 2 Community License

The model weights are not MIT-licensed. Read and accept the linked model license
before downloading or using them.

## SD-Turbo

- Model: `stabilityai/sd-turbo`
- Model card: <https://huggingface.co/stabilityai/sd-turbo>

Read and accept the model license before you download or use the model.

## SDXL-Turbo

- Model: `stabilityai/sdxl-turbo`
- Pinned revision: `71153311d3dbb46851df1931d3ca6e939de83304`
- Model card: <https://huggingface.co/stabilityai/sdxl-turbo>

Commercial use may require a Stability AI license. Review the current model
license and acceptable-use terms before downloading or using the weights.

## Sana-Sprint 1.6B

- Model: `Efficient-Large-Model/Sana_Sprint_1.6B_1024px_diffusers`
- Pinned revision: `19683c58b7ea290e55cedd8950ae1d86ada7ef96`
- Model card: <https://huggingface.co/Efficient-Large-Model/Sana_Sprint_1.6B_1024px_diffusers>
- License: Apache 2.0

The optional Windows and Linux/WSL Sana presets use the BF16 1024px Diffusers
checkpoint with its documented two-step inference configuration.

## Stable Fast 3D

- Source: <https://github.com/Stability-AI/stable-fast-3d>
- Pinned source revision: `ff21fc491b4dc5314bf6734c7c0dabd86b5f5bb2`
- Weights: `stabilityai/stable-fast-3d`
- Pinned weight revision: `f0c9a8ffd62cb1bbc8a7a53c9f87a0be1b6be778`

Stable Fast 3D includes the `texture_baker` and `uv_unwrapper` native
extensions. The weights are gated and governed by the Stability AI Community
License. Commercial users above the license's revenue threshold must obtain an
enterprise license. The native Windows CUDA installer applies a local
compatibility patch to the pinned source; Linux and WSL use the pinned upstream
source directly. Neither installer changes the model weights.

## DINOv2

Stable Fast 3D uses `facebook/dinov2-large` as an image encoder. Its files are
downloaded locally by the Windows and Linux/WSL CUDA installers. Review its
Hugging Face model card and upstream license before use.
