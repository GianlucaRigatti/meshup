from __future__ import annotations

import argparse
import gc
import os
import random
import sys
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run InstantMesh with sequential low-VRAM model loading."
    )
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--image", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--source", type=Path, default=Path.cwd())
    parser.add_argument("--model-path", type=Path)
    parser.add_argument("--zero123-model", type=Path)
    parser.add_argument("--zero123-pipeline", type=Path)
    parser.add_argument("--dino-model", type=Path)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--diffusion-steps", type=int, default=30)
    parser.add_argument("--views", type=int, choices=(4, 6), default=4)
    parser.add_argument("--grid-resolution", type=int, default=96)
    parser.add_argument("--texture-resolution", type=int, default=512)
    return parser.parse_args()


def release_cuda(torch_module) -> None:
    gc.collect()
    torch_module.cuda.empty_cache()
    torch_module.cuda.ipc_collect()


def export_textured_glb(mesh_out, output: Path) -> None:
    import numpy as np
    import trimesh
    from PIL import Image

    vertices, faces, uvs, texture_faces, texture = mesh_out
    vertices = vertices.detach().float().cpu().numpy()
    faces = faces.detach().long().cpu().numpy()
    uvs = uvs.detach().float().cpu().numpy()
    texture_faces = texture_faces.detach().long().cpu().numpy()
    texture = texture.detach().float().permute(1, 2, 0).cpu().numpy()
    texture = (texture.clip(0, 1) * 255).astype(np.uint8)
    texture_image = Image.fromarray(np.ascontiguousarray(texture[::-1]))

    # glTF has one index for position and UV. InstantMesh's OBJ-style output has
    # separate indices, so split only the vertices that lie on UV seams.
    pairs = np.stack((faces.reshape(-1), texture_faces.reshape(-1)), axis=1)
    unique_pairs, inverse = np.unique(pairs, axis=0, return_inverse=True)
    split_vertices = vertices[unique_pairs[:, 0]]
    split_uvs = uvs[unique_pairs[:, 1]]
    split_faces = inverse.reshape(-1, 3)
    material = trimesh.visual.material.PBRMaterial(
        baseColorTexture=texture_image,
        metallicFactor=0.0,
        roughnessFactor=0.8,
    )
    visual = trimesh.visual.texture.TextureVisuals(
        uv=split_uvs,
        material=material,
    )
    mesh = trimesh.Trimesh(
        vertices=split_vertices,
        faces=split_faces,
        visual=visual,
        process=False,
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(output, file_type="glb", include_normals=True)


def validate_paths(args: argparse.Namespace) -> None:
    required = {
        "input image": args.image,
        "output path": args.output,
        "InstantMesh model directory": args.model_path,
        "Zero123++ model directory": args.zero123_model,
        "Zero123++ pipeline directory": args.zero123_pipeline,
        "DINO model directory": args.dino_model,
    }
    missing = [label for label, path in required.items() if path is None]
    if missing:
        raise ValueError("Missing required arguments: " + ", ".join(missing))
    files = (
        args.image,
        args.source / "configs" / "instant-mesh-base.yaml",
        args.model_path / "diffusion_pytorch_model.bin",
        args.model_path / "instant_mesh_base.ckpt",
        args.zero123_model / "model_index.json",
        args.zero123_model / "unet" / "diffusion_pytorch_model.safetensors",
        args.zero123_model / "vae" / "diffusion_pytorch_model.safetensors",
        args.zero123_model / "vision_encoder" / "model.safetensors",
        args.zero123_model / "text_encoder" / "model.safetensors",
        args.zero123_pipeline / "pipeline.py",
        args.dino_model / "config.json",
        args.dino_model / "preprocessor_config.json",
        args.dino_model / "pytorch_model.bin",
    )
    absent = [str(path.resolve()) for path in files if not path.is_file()]
    if absent:
        raise FileNotFoundError("Missing InstantMesh files:\n- " + "\n- ".join(absent))


def main() -> None:
    args = parse_args()
    source = args.source.resolve()
    sys.path.insert(0, str(source))

    import cv2  # noqa: F401
    import nvdiffrast.torch  # noqa: F401
    import torch
    import xatlas  # noqa: F401

    if not torch.cuda.is_available():
        raise RuntimeError("InstantMesh requires an available CUDA GPU.")
    torch.zeros(1, device="cuda")
    if args.check:
        from src.models.lrm_mesh import InstantMesh  # noqa: F401

        print("InstantMesh CUDA runtime is ready.")
        return

    validate_paths(args)
    import numpy as np
    from diffusers import DiffusionPipeline, EulerAncestralDiscreteScheduler
    from einops import rearrange
    from omegaconf import OmegaConf
    from PIL import Image
    from torchvision.transforms import v2

    from src.utils.camera_util import get_zero123plus_input_cameras
    from src.utils.train_util import instantiate_from_config

    os.chdir(source)
    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.manual_seed(args.seed)
    torch.cuda.manual_seed_all(args.seed)
    device = torch.device("cuda:0")

    print("Loading Zero123++ multiview model ...", flush=True)
    pipeline = DiffusionPipeline.from_pretrained(
        str(args.zero123_model.resolve()),
        custom_pipeline=str(args.zero123_pipeline.resolve()),
        torch_dtype=torch.float16,
        local_files_only=True,
    )
    pipeline.scheduler = EulerAncestralDiscreteScheduler.from_config(
        pipeline.scheduler.config,
        timestep_spacing="trailing",
    )
    unet_state = torch.load(
        args.model_path / "diffusion_pytorch_model.bin",
        map_location="cpu",
        weights_only=True,
    )
    pipeline.unet.load_state_dict(unet_state, strict=True)
    del unet_state
    pipeline.to(device)

    with Image.open(args.image) as opened:
        input_image = opened.convert("RGBA")
    with torch.inference_mode():
        multiview = pipeline(
            input_image,
            num_inference_steps=args.diffusion_steps,
        ).images[0]
    images = np.asarray(multiview, dtype=np.float32) / 255.0
    images = torch.from_numpy(images).permute(2, 0, 1).contiguous().float()
    images = rearrange(images, "c (n h) (m w) -> (n m) c h w", n=3, m=2)

    # The stock runner loads both stages together. Fully destroy Zero123++
    # before allocating InstantMesh so a 12 GB GPU only holds one stage.
    del pipeline, multiview, input_image
    release_cuda(torch)

    print("Loading InstantMesh Base reconstruction model ...", flush=True)
    config = OmegaConf.load(source / "configs" / "instant-mesh-base.yaml")
    config.model_config.params.encoder_model_name = str(args.dino_model.resolve())
    config.model_config.params.grid_res = args.grid_resolution
    config.infer_config.texture_resolution = args.texture_resolution
    model = instantiate_from_config(config.model_config)
    checkpoint = torch.load(
        args.model_path / "instant_mesh_base.ckpt",
        map_location="cpu",
        weights_only=True,
    )["state_dict"]
    checkpoint = {
        key[14:]: value
        for key, value in checkpoint.items()
        if key.startswith("lrm_generator.")
    }
    model.load_state_dict(checkpoint, strict=True)
    del checkpoint
    gc.collect()
    model = model.to(device).eval()
    model.init_flexicubes_geometry(device, fovy=30.0)

    cameras = get_zero123plus_input_cameras(batch_size=1, radius=4.0).to(device)
    images = images.unsqueeze(0)
    images = v2.functional.resize(
        images,
        320,
        interpolation=v2.InterpolationMode.BICUBIC,
        antialias=True,
    ).clamp(0, 1)
    if args.views == 4:
        indices = torch.tensor([0, 2, 4, 5], dtype=torch.long)
        images = images[:, indices]
        cameras = cameras[:, indices.to(device)]
    images = images.to(device)

    print(
        f"Reconstructing with {args.views} views, grid {args.grid_resolution}, "
        f"texture {args.texture_resolution} ...",
        flush=True,
    )
    with torch.inference_mode(), torch.autocast(
        device_type="cuda", dtype=torch.float16
    ):
        planes = model.forward_planes(images, cameras)
        mesh_out = model.extract_mesh(
            planes,
            use_texture_map=True,
            texture_resolution=args.texture_resolution,
        )
    export_textured_glb(mesh_out, args.output.resolve())
    print(f"Mesh saved to {args.output.resolve()}", flush=True)


if __name__ == "__main__":
    main()
