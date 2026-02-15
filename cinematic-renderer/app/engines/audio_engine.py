from __future__ import annotations

from pathlib import Path

import numpy as np
from moviepy.editor import AudioFileClip, CompositeAudioClip

from app.models.schemas import AudioTrackInput


class AudioEngine:
    def mix_audio(
        self,
        timeline_seconds: float,
        narration: AudioTrackInput | None,
        music: AudioTrackInput | None,
    ):
        clips = []
        if narration is not None:
            narr = AudioFileClip(Path(narration.path).as_posix()).volumex(narration.volume)
            clips.append(narr.set_duration(timeline_seconds))

        if music is not None:
            mus = AudioFileClip(Path(music.path).as_posix()).audio_loop(duration=timeline_seconds).volumex(music.volume)
            clips.append(mus)

        if not clips:
            raise ValueError("No audio clips available for mix")

        mixed = CompositeAudioClip(clips).set_duration(timeline_seconds)
        return self._normalize(mixed)

    @staticmethod
    def _normalize(audio_clip):
        sample = audio_clip.to_soundarray(fps=22050)
        peak = float(np.max(np.abs(sample))) if sample.size else 1.0
        if peak <= 0:
            return audio_clip
        target_peak = 0.92
        gain = min(2.0, target_peak / peak)
        return audio_clip.volumex(gain)
