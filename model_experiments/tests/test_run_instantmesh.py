from __future__ import annotations

import numpy as np
import torch
import trimesh

from scripts.run_instantmesh import export_textured_glb, extract_mesh_float32


def test_extract_mesh_promotes_autocast_planes_to_float32() -> None:
    class Model:
        def extract_mesh(self, planes, **kwargs):
            assert planes.dtype == torch.float32
            assert kwargs == {"use_texture_map": True, "texture_resolution": 512}
            return "mesh"

    planes = torch.ones((1, 3, 4, 4), dtype=torch.float16)

    assert extract_mesh_float32(Model(), planes, 512) == "mesh"


def test_export_textured_glb_embeds_texture_and_splits_uv_seams(tmp_path) -> None:
    vertices = torch.tensor(
        [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [1.0, 1.0, 0.0], [0.0, 1.0, 0.0]]
    )
    faces = torch.tensor([[0, 1, 2], [0, 2, 3]])
    uvs = torch.tensor(
        [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0], [0.5, 0.5]]
    )
    texture_faces = torch.tensor([[0, 1, 2], [4, 2, 3]])
    texture_array = np.zeros((3, 8, 8), dtype=np.uint8)
    texture_array[0], texture_array[1], texture_array[2] = 255, 80, 20
    texture = torch.from_numpy(texture_array).float() / 255
    output = tmp_path / "textured.glb"

    export_textured_glb(
        (vertices, faces, uvs, texture_faces, texture),
        output,
    )

    scene = trimesh.load(output, force="scene")
    mesh = next(iter(scene.geometry.values()))
    assert len(mesh.vertices) == 5
    assert mesh.visual.kind == "texture"
    assert mesh.visual.material.baseColorTexture is not None
