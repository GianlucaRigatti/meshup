from __future__ import annotations

import argparse
import json
from pathlib import Path

from app.config import PROMPT_ENHANCEMENT_INSTRUCTION


def enhance(model_path: Path, transcript: str) -> str:
    import torch
    from transformers import AutoModelForMultimodalLM, AutoProcessor

    processor = AutoProcessor.from_pretrained(
        str(model_path.resolve()), local_files_only=True
    )
    model = AutoModelForMultimodalLM.from_pretrained(
        str(model_path.resolve()),
        local_files_only=True,
        dtype=torch.bfloat16,
        device_map="cuda:0",
        attn_implementation="sdpa",
    )
    messages = [
        {
            "role": "system",
            "content": [{"type": "text", "text": PROMPT_ENHANCEMENT_INSTRUCTION}],
        },
        {"role": "user", "content": [{"type": "text", "text": transcript}]},
    ]
    inputs = processor.apply_chat_template(
        messages,
        add_generation_prompt=True,
        tokenize=True,
        return_dict=True,
        return_tensors="pt",
        enable_thinking=False,
    ).to(model.device)
    outputs = model.generate(**inputs, max_new_tokens=160, do_sample=False)
    generated = outputs[0][inputs["input_ids"].shape[-1] :]
    return processor.decode(generated, skip_special_tokens=True).strip()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run the pinned local Qwen3.5 prompt-enrichment stage."
    )
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--transcript", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: list[str] | None = None) -> None:
    args = build_parser().parse_args(argv)
    payload = json.loads(args.transcript.read_text(encoding="utf-8"))
    transcript = " ".join(str(payload["text"]).split())
    prompt = enhance(args.model, transcript)
    args.output.write_text(prompt, encoding="utf-8")


if __name__ == "__main__":
    main()
