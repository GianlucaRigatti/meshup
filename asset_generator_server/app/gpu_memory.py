from __future__ import annotations

import shutil
import subprocess
import sys
import threading

MIB = 1024 * 1024


class NvidiaMemorySampler:
    """Sample whole-device VRAM while an isolated native process runs."""

    def __init__(self, prefix: str, interval_seconds: float = 0.1) -> None:
        self.prefix = prefix
        self.interval_seconds = interval_seconds
        self.baseline_mib: int | None = None
        self.peak_mib: int | None = None
        self.post_mib: int | None = None
        self.total_mib: int | None = None
        self._enabled = sys.platform.startswith("linux") and bool(
            shutil.which("nvidia-smi")
        )
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def __enter__(self):
        if not self._enabled:
            return self
        reading = self._read()
        if reading is None:
            self._enabled = False
            return self
        self.baseline_mib, self.total_mib = reading
        self.peak_mib = self.baseline_mib
        self._thread = threading.Thread(target=self._sample, daemon=True)
        self._thread.start()
        return self

    def __exit__(self, *_args) -> None:
        if not self._enabled:
            return
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=3)
        reading = self._read()
        if reading is not None:
            self.post_mib, self.total_mib = reading

    @property
    def metadata(self) -> dict[str, int]:
        if self.baseline_mib is None or self.peak_mib is None:
            return {}
        prefix = self.prefix
        metadata = {
            f"{prefix}_baseline_nvidia_used_bytes": self.baseline_mib * MIB,
            f"{prefix}_peak_nvidia_used_bytes": self.peak_mib * MIB,
            f"{prefix}_peak_delta_bytes": max(0, self.peak_mib - self.baseline_mib)
            * MIB,
        }
        if self.post_mib is not None:
            metadata[f"{prefix}_post_nvidia_used_bytes"] = self.post_mib * MIB
        if self.total_mib is not None:
            metadata["nvidia_total_memory_bytes"] = self.total_mib * MIB
        return metadata

    def _sample(self) -> None:
        while not self._stop.wait(self.interval_seconds):
            reading = self._read()
            if reading is not None:
                used_mib, total_mib = reading
                self.peak_mib = max(self.peak_mib or 0, used_mib)
                self.total_mib = total_mib

    @staticmethod
    def _read() -> tuple[int, int] | None:
        try:
            completed = subprocess.run(
                [
                    "nvidia-smi",
                    "--query-gpu=memory.used,memory.total",
                    "--format=csv,noheader,nounits",
                    "--id=0",
                ],
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL,
                text=True,
                timeout=2,
            )
        except (OSError, subprocess.SubprocessError):
            return None
        if completed.returncode != 0:
            return None
        try:
            used, total = completed.stdout.splitlines()[0].split(",")[:2]
            return int(used.strip()), int(total.strip())
        except (IndexError, ValueError):
            return None
