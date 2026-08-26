from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

from app.config import PROMPT_ENHANCEMENT_INSTRUCTION

_PRESENTATION_CLAUSE_PATTERNS = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\b(?:background|backdrop)\b",
        r"\blighting\b",
        r"\b(?:camera\s+(?:angle|view|height|position)|viewed?\s+from)\b",
        r"\b(?:orthographic|isometric|weak[- ]perspective)\b",
        r"\b(?:front|rear|side|top|three[- ]quarter|product)\s+view\b",
        r"\b(?:product\s+shot|sharp\s+focus|depth\s+of\s+field)\b",
        r"\b(?:composition|framing)\b",
        r"\b(?:no|without)\s+(?:floor|pedestal|environment|text|extra objects?)\b",
        r"\b(?:on|against)\s+(?:an?\s+)?(?:floor|pedestal|background|backdrop)\b",
        r"^(?:fully|completely)\s+visible$",
        r"^(?:perfectly\s+)?cent(?:er|re)d$",
        r"^(?:one\s+)?isolated\s+subject$",
    )
)

_UNSUPPORTED_DETAIL_PATTERNS = (
    (
        re.compile(r"\b(?:ornate|intricate|elaborate)\b", re.IGNORECASE),
        ("ornate", "intricat", "elaborat"),
    ),
    (
        re.compile(r"\b(?:glossy|matte|polished)\s+finish\b", re.IGNORECASE),
        ("gloss", "matte", "polish"),
    ),
    (
        re.compile(r"\b(?:weathered|aged|worn|damaged|distressed)\b", re.IGNORECASE),
        ("weather", "aged", "worn", "damage", "distress"),
    ),
    (
        re.compile(
            r"\b(?:intricate\s+)?carv(?:ing|ings|ed\s+details?)\b", re.IGNORECASE
        ),
        ("carv",),
    ),
    (
        re.compile(r"\b(?:engraved|engravings?|embossed|embossing)\b", re.IGNORECASE),
        ("engrav", "emboss"),
    ),
    (re.compile(r"\b(?:inlaid|inlay)\b", re.IGNORECASE), ("inlay", "inlaid")),
)


def sanitize_subject_prompt(prompt: str, transcript: str) -> str:
    """Remove presentation instructions and unsupported decorative invention."""
    normalized = " ".join(prompt.strip().strip("\"'").split())
    transcript_lower = transcript.lower()
    clauses: list[str] = []
    for raw_clause in re.split(r"[,;]+", normalized):
        clause = raw_clause.strip(" .")
        if not clause:
            continue
        clause = re.sub(
            r"^(?:(?:an?|one)\s+)?(?:isolated\s+)?(?:3d\s+)?"
            r"(?:asset|model|render(?:ing)?|product\s+shot)\s+of\s+",
            "",
            clause,
            flags=re.IGNORECASE,
        )
        clause = re.sub(
            r"^(?:(?:an?|one)\s+)?isolated(?:\s+single)?\s+",
            "",
            clause,
            flags=re.IGNORECASE,
        )
        if not re.search(r"\b(?:3d|three[- ]dimensional)\b", transcript_lower):
            clause = re.sub(r"^3d\s+", "", clause, flags=re.IGNORECASE)
        if any(pattern.search(clause) for pattern in _PRESENTATION_CLAUSE_PATTERNS):
            continue

        for pattern, evidence in _UNSUPPORTED_DETAIL_PATTERNS:
            if not any(term in transcript_lower for term in evidence):
                clause = pattern.sub("", clause)
        clause = re.sub(r"\s+", " ", clause).strip(" .")
        clause = re.sub(
            r"\b(?:with|and|featuring)\s*$", "", clause, flags=re.IGNORECASE
        ).strip()
        if clause:
            clauses.append(clause)

    result = ", ".join(clauses).strip(" ,.")
    if not result:
        raise ValueError("Prompt cleanup produced no usable subject description.")
    return result[0].upper() + result[1:]


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
    raw_prompt = processor.decode(generated, skip_special_tokens=True).strip()
    return sanitize_subject_prompt(raw_prompt, transcript)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run the pinned local Qwen3.5 prompt-cleanup stage."
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
