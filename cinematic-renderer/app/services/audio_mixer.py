from __future__ import annotations

import numpy as np
from moviepy.editor import AudioFileClip, CompositeAudioClip, afx

from app.models.render_contract import AudioConfig


class AudioMixerService:
    def mix(self, timeline_seconds: float, audio: AudioConfig):
        clips = []
        narration_clip = None

        if audio.narration is not None:
            narration_clip = AudioFileClip(audio.narration.path).volumex(audio.narration.volume).set_duration(timeline_seconds)
            clips.append(narration_clip)

        if audio.music is not None:
            music_clip = AudioFileClip(audio.music.path).audio_loop(duration=timeline_seconds)
            music_clip = music_clip.volumex(audio.music.volume)
            if audio.mix.duck_music_under_narration and narration_clip is not None:
                envelope = self._build_ducking_envelope(narration_clip, timeline_seconds)
                music_clip = music_clip.volumex(lambda t: float(envelope[min(len(envelope) - 1, int(t * 25))]))
            clips.append(music_clip)

        if not clips:
            raise ValueError("No audio clips available for mix")

        mixed = CompositeAudioClip(clips).set_duration(timeline_seconds)

        if audio.mix.fade_in_seconds > 0:
            mixed = mixed.fx(afx.audio_fadein, audio.mix.fade_in_seconds)
        if audio.mix.fade_out_seconds > 0:
            mixed = mixed.fx(afx.audio_fadeout, audio.mix.fade_out_seconds)

        return self._normalize(mixed)

    @staticmethod
    def _build_ducking_envelope(narration_clip, timeline_seconds: float) -> np.ndarray:
        sample_rate = 2000
        samples = narration_clip.to_soundarray(fps=sample_rate)
        mono = samples.mean(axis=1) if samples.ndim > 1 else samples
        window = max(1, int(sample_rate / 25))

        level = np.sqrt(np.convolve(mono**2, np.ones(window) / window, mode="same"))
        threshold = max(0.01, np.percentile(level, 60))

        envelope = np.ones(max(1, int(timeline_seconds * 25)))
        for i in range(envelope.size):
            idx = min(level.size - 1, i * window)
            if level[idx] >= threshold:
                envelope[i] = 0.45
        return np.clip(envelope, 0.35, 1.0)

    @staticmethod
    def _normalize(audio_clip):
        sample = audio_clip.to_soundarray(fps=22050)
        peak = float(np.max(np.abs(sample))) if sample.size else 1.0
        if peak <= 0:
            return audio_clip
        gain = min(2.0, 0.92 / peak)
        return audio_clip.volumex(gain)
