from __future__ import annotations

from app.gpu_memory import MIB, NvidiaMemorySampler


def test_sampler_reports_baseline_peak_delta_and_release() -> None:
    sampler = NvidiaMemorySampler("image_model")
    sampler.baseline_mib = 300
    sampler.peak_mib = 10_800
    sampler.post_mib = 420
    sampler.total_mib = 12_288

    assert sampler.metadata == {
        "image_model_baseline_nvidia_used_bytes": 300 * MIB,
        "image_model_peak_nvidia_used_bytes": 10_800 * MIB,
        "image_model_peak_delta_bytes": 10_500 * MIB,
        "image_model_post_nvidia_used_bytes": 420 * MIB,
        "nvidia_total_memory_bytes": 12_288 * MIB,
    }


def test_sampler_without_nvidia_reading_has_no_metadata() -> None:
    assert NvidiaMemorySampler("model_3d").metadata == {}
