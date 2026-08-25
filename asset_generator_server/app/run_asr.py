from __future__ import annotations

import argparse
import json
from pathlib import Path


def transcribe(model_path: Path, audio_path: Path) -> dict[str, str]:
    import torch
    from qwen_asr import Qwen3ASRModel

    model = Qwen3ASRModel.from_pretrained(
        str(model_path.resolve()),
        dtype=torch.bfloat16,
        device_map="cuda:0",
        max_inference_batch_size=1,
        max_new_tokens=512,
    )
    result = model.transcribe(audio=str(audio_path.resolve()), language=None)[0]
    return {
        "text": str(result.text),
        "language": str(result.language or "Unknown"),
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run the pinned local Qwen3 ASR stage."
    )
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--audio", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: list[str] | None = None) -> None:
    args = build_parser().parse_args(argv)
    payload = transcribe(args.model, args.audio)
    args.output.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    main()
