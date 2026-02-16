from __future__ import annotations

from moviepy.editor import CompositeVideoClip

from app.models.render_contract import SceneConfig


class TransitionService:
    def compose(self, clips: list, scenes: list[SceneConfig], width: int, height: int):
        if not clips:
            raise ValueError("No clips supplied for composition")

        started = []
        current_start = 0.0
        for idx, clip in enumerate(clips):
            placed_clip = clip
            if idx > 0:
                transition = scenes[idx].transition
                if transition.type == "crossfade" and transition.duration > 0:
                    fade_duration = min(transition.duration, clip.duration * 0.8)
                    placed_clip = clip.crossfadein(fade_duration)
            started.append(placed_clip.set_start(current_start))
            current_start += clip.duration
            if idx < len(clips) - 1:
                transition = scenes[idx + 1].transition
                if transition.type == "crossfade" and transition.duration > 0:
                    current_start -= min(transition.duration, clip.duration * 0.8)

        total_duration = max(item.start + item.duration for item in started)
        return CompositeVideoClip(started, size=(width, height)).set_duration(total_duration)
