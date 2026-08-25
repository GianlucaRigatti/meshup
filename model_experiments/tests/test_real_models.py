from __future__ import annotations

import os

import pytest


@pytest.mark.real_models
@pytest.mark.skipif(
    os.getenv("RUN_REAL_MODEL_TESTS") != "1",
    reason="Set RUN_REAL_MODEL_TESTS=1 after the model download.",
)
def test_real_model_pipeline() -> None:
    pytest.importorskip("trimesh")
    from app.config import Settings
    from app.generator import AssetGenerator

    generator = AssetGenerator(Settings())
    generator.load()
    assert generator.ready, generator.load_error
    asset_id, _, _ = generator.generate("a simple wooden chair")
    path = generator.output_dir / f"{asset_id}.glb"
    assert path.read_bytes()[:4] == b"glTF"

    import trimesh

    scene = trimesh.load(path)
    geometries = (
        list(scene.geometry.values()) if hasattr(scene, "geometry") else [scene]
    )
    assert geometries
    assert all(len(mesh.vertices) > 0 and len(mesh.faces) > 0 for mesh in geometries)
    if generator.output_mode == "vertex_color":
        assert all(mesh.visual.kind == "vertex" for mesh in geometries)
    else:
        assert all(mesh.visual.kind == "texture" for mesh in geometries)
        assert all(getattr(mesh.visual, "uv", None) is not None for mesh in geometries)
