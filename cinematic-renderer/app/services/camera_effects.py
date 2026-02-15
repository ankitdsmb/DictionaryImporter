from __future__ import annotations

from pathlib import Path

from moviepy.editor import ImageClip

from app.models.render_contract import CameraConfig
from app.utils.deterministic import rng_for


class CameraEffectsService:
    def build_clip(
        self,
        image_path: str,
        duration: float,
        width: int,
        height: int,
        seed: int,
        namespace: str,
        camera: CameraConfig,
    ) -> ImageClip:
        clip = ImageClip(Path(image_path).as_posix()).set_duration(duration).resize(height=height)
        if camera.type != "kenburns" or camera.intensity <= 0:
            return clip.set_position("center")

        rng = rng_for(seed, namespace)
        intensity = camera.intensity
        zoom_start = 1.0 + rng.uniform(0.00, 0.03) * intensity
        zoom_end = zoom_start + rng.uniform(0.05, 0.16) * intensity
        pan_x = rng.uniform(-0.07, 0.07) * intensity
        pan_y = rng.uniform(-0.05, 0.05) * intensity

        def dynamic_resize(t: float) -> float:
            progress = 0.0 if duration <= 0 else min(1.0, t / duration)
            return zoom_start + (zoom_end - zoom_start) * progress

        def dynamic_position(t: float) -> tuple[float, float]:
            progress = 0.0 if duration <= 0 else min(1.0, t / duration)
            dx = int(width * pan_x * progress)
            dy = int(height * pan_y * progress)
            return (dx, dy)

        return clip.resize(dynamic_resize).set_position(dynamic_position)
