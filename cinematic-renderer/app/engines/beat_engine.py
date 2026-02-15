from __future__ import annotations

from pathlib import Path

import librosa
import numpy as np


class BeatEngine:
    def detect_beats(self, audio_path: Path) -> np.ndarray:
        y, sr = librosa.load(audio_path.as_posix(), sr=None, mono=True)
        if len(y) == 0:
            return np.array([], dtype=float)
        _, beat_frames = librosa.beat.beat_track(y=y, sr=sr, units="frames")
        beat_times = librosa.frames_to_time(beat_frames, sr=sr)
        return beat_times.astype(float)

    def align_durations_to_beats(self, durations: list[float], beat_times: np.ndarray) -> list[float]:
        if beat_times.size < 2:
            return durations

        intervals = np.diff(beat_times)
        beat_unit = float(np.median(intervals)) if intervals.size else 0.5
        beat_unit = max(0.3, min(2.0, beat_unit))

        aligned: list[float] = []
        for duration in durations:
            beats = max(1, round(duration / beat_unit))
            aligned_duration = round(beats * beat_unit, 3)
            aligned.append(max(0.5, aligned_duration))
        return aligned
