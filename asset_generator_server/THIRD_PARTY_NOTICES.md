# Third-Party Notices

The installer downloads third-party source code and model weights. Review the
linked license and model-card terms before running it, using the models, or
redistributing any resulting bundle. Passing `--accept-licenses` records only
your confirmation; it does not replace those terms.

## stable-diffusion.cpp

- Source: <https://github.com/leejet/stable-diffusion.cpp>
- Pinned revision: `97d2990807fe6d558e395f8764198d7c7e7b411c`

The installer builds the CUDA `sd-cli` target from this revision. Review the
repository license and the notices for its bundled dependencies.

## FLUX.2 Klein 9B Q4_K_M

- Quantized weights: <https://huggingface.co/unsloth/FLUX.2-klein-9B-GGUF>
- Pinned revision: `fde8634245fe6b749a221c25b34672b5b8fbd079`
- Original model: <https://huggingface.co/black-forest-labs/FLUX.2-klein-9B>

The original FLUX.2 Klein 9B weights are governed by the FLUX
Non-Commercial License and acceptable-use terms. Confirm that the intended use
is permitted before downloading or using the quantized derivative.

## Qwen3-8B text encoder

- Weights: <https://huggingface.co/Qwen/Qwen3-8B-GGUF>
- Pinned revision: `7c41481f57cb95916b40956ab2f0b139b296d974`
- Selected file: `Qwen3-8B-Q4_K_M.gguf`

Review the repository model card and license.

## FLUX.2 VAE

- Files: <https://huggingface.co/Comfy-Org/flux2-klein-4B>
- Pinned revision: `5f526678002e43af5551dadb73ce2e8c91b43afe`
- Selected file: `split_files/vae/flux2-vae.safetensors`

Review the repository model card and the licenses of the original components.

## trellis.cpp

- Source: <https://github.com/pwilkin/trellis.cpp>
- Pinned revision: `06fc9000719c912ddc4929d21db075972c26ac3e`
- Original TRELLIS.2 project: <https://github.com/microsoft/TRELLIS.2>

The installer builds the CUDA `trellis-cli` target from this revision. The
runtime is MIT-licensed and contains bundled third-party components with their
own notices and terms.

## BiRefNet-General background removal

- Model: <https://huggingface.co/ZhengPeng7/BiRefNet>
- Pinned revision: `b7d7f31fed203ab364ac756d62053ee467502434`

The service runs this full checkpoint in FP16 at 1024px before TRELLIS. Review
the repository model card, source, license, and terms before use or
redistribution.

## TRELLIS.2 Q4 GGUF weights

- Quantized weights: <https://huggingface.co/ilintar/trellis2-gguf>
- Pinned revision: `a57397bd3d351599d9729fc144b3f87c3f87d65b`
- Original model: <https://huggingface.co/microsoft/TRELLIS.2-4B>

Only the Q4 files needed by the 512-resolution path are downloaded. The bundle
includes DINOv3 image-conditioning weights in addition to the TRELLIS flow and
decoder weights. Its bundled quantized BiRefNet file is not downloaded because
the server supplies a prematted RGBA image. Review the quantized-weight
repository, the original Microsoft model terms, and the upstream DINOv3
license before redistribution.
