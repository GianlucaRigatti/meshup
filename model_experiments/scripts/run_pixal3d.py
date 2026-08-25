from __future__ import annotations

import runpy
import sys
from pathlib import Path


class PreprocessedRgbaOnly:
    """Placeholder for Pixal3D's unused gated background-removal model."""

    def __init__(self, **_kwargs) -> None:
        pass

    def to(self, _device):
        return self

    def cpu(self):
        return self

    def __call__(self, _image):
        raise RuntimeError(
            "Pixal3D received an image without transparency. The asset server "
            "must remove its background before reconstruction."
        )


def main() -> None:
    source = Path.cwd()
    inference = source / "inference.py"
    if not inference.is_file():
        raise FileNotFoundError(f"Pixal3D inference script is missing at {inference}.")
    sys.path.insert(0, str(source))
    from pixal3d.pipelines import rembg

    rembg.BiRefNet = PreprocessedRgbaOnly
    runpy.run_path(str(inference), run_name="__main__")


if __name__ == "__main__":
    main()
