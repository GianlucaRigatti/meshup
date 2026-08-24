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

The optional `sana-sprint` image selection uses the BF16 1024px Diffusers
checkpoint with its documented two-step inference configuration.

## Stable Diffusion 3.5 Medium

- Model: `stabilityai/stable-diffusion-3.5-medium`
- Pinned revision: `b940f670f0eda2d07fbb75229e779da1ad11eb80`
- Model card: <https://huggingface.co/stabilityai/stable-diffusion-3.5-medium>
- License: Stability AI Community License

The optional WSL SD 3.5/Pixal3D preset uses bitsandbytes NF4 quantization for
the transformer and T5-XXL encoder. The weights are gated. Accept the model
terms and review the license before downloading or using them.

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

## Pixal3D and TRELLIS.2

- Pixal3D source: <https://github.com/TencentARC/Pixal3D>
- Pinned Pixal3D source revision: `cdbb2bbffbf4e6f298b5f2af3d1d76a8d823d2af`
- Pixal3D weights: <https://huggingface.co/TencentARC/Pixal3D>
- Pinned weight revision: `0b31f9160aa400719af409098bff7936a932f726`
- TRELLIS.2 source: <https://github.com/microsoft/TRELLIS.2>
- Pinned TRELLIS.2 revision: `75fbf0183001ed9876c8dbb35de6b68552ee08bd`

The optional `pixal3d` 3D selection also downloads or builds NATTEN,
CuMesh, FlexGEMM, nvdiffrast, O-Voxel, utils3d, DINOv3, and NAF. Pixal3D and
TRELLIS.2 source are MIT-licensed. Their third-party components and model
weights remain under their own terms; review the linked repositories and model
cards before downloading or distributing them.

## TRELLIS.2 GGUF via trellis.cpp

- Runtime source: <https://github.com/pwilkin/trellis.cpp>
- Pinned runtime revision: `06fc9000719c912ddc4929d21db075972c26ac3e`
- Quantized weights: <https://huggingface.co/ilintar/trellis2-gguf>
- Pinned weight revision: `a57397bd3d351599d9729fc144b3f87c3f87d65b`
- Original model: <https://huggingface.co/microsoft/TRELLIS.2-4B>

The optional TRELLIS.2 selections build the MIT-licensed trellis.cpp runtime
against the local CUDA 12.8 toolkit and download only the selected GGUF weight
folder. The original Microsoft TRELLIS.2 model is MIT-licensed. Review the
runtime's bundled third-party components and the quantized-weight repository
before redistribution.

## Z-Image Turbo GGUF via stable-diffusion.cpp

- Runtime source: <https://github.com/leejet/stable-diffusion.cpp>
- Pinned runtime revision: `97d2990807fe6d558e395f8764198d7c7e7b411c`
- Quantized Z-Image weights: <https://huggingface.co/leejet/Z-Image-Turbo-GGUF>
- Pinned weight revision: `c61c0e422dc8b541b7548cf33a4ef8302b0f8085`
- Quantized Qwen3 text encoder: <https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF>
- Pinned text-encoder revision: `a06e946bb6b655725eafa393f4a9745d460374c9`
- VAE files: <https://huggingface.co/Comfy-Org/z_image_turbo>
- Pinned VAE revision: `08d04455279082882deaabc8d0d09fc914c071e1`
- Original model: <https://huggingface.co/Tongyi-MAI/Z-Image-Turbo>

The optional Z-Image Q3, Q4, and Q6 selections build the MIT-licensed
stable-diffusion.cpp runtime against the local CUDA 12.8 toolkit. Z-Image Turbo
and the referenced model repositories identify their weights as Apache 2.0.
Review each linked model card and the runtime's bundled third-party components
before redistribution.

## InstantMesh and Zero123++

- InstantMesh source: <https://github.com/TencentARC/InstantMesh>
- Pinned source revision: `08822c52fdc399b93ea00e4fa9e596344ed52ccc`
- InstantMesh weights: <https://huggingface.co/TencentARC/InstantMesh>
- Pinned weight revision: `b785b4ecfb6636ef34a08c748f96f6a5686244d0`
- Zero123++ v1.2: <https://huggingface.co/sudo-ai/zero123plus-v1.2>
- Zero123++ pipeline: <https://huggingface.co/sudo-ai/zero123plus-pipeline>
- DINO ViT-B/16 encoder: <https://huggingface.co/facebook/dino-vitb16>

The optional `instantmesh-fast` 3D selection installs InstantMesh
and its dependencies in an isolated runtime. InstantMesh identifies its source
and model repository as Apache 2.0. Zero123++ identifies its weights under the
CreativeML Open RAIL-M license. nvdiffrast and some bundled NVIDIA source files
have their own NVIDIA license terms. Review all linked repositories and bundled
notices before use or redistribution.
