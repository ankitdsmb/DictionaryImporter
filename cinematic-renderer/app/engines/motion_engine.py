from __future__ import annotations

from pathlib import Path

from moviepy.editor import ImageClip

from app.models.schemas import SceneInput
from app.utils.deterministic import rng_for


class MotionEngine:
    def build_scene_clip(
        self,
        scene: SceneInput,
        seed: int,
        namespace: str,
        width: int,
        height: int,
        duration: float,
    ) -> ImageClip:
        rng = rng_for(seed, namespace)
        zoom_start = rng.uniform(1.00, 1.06)
        zoom_end = rng.uniform(1.04, 1.15)
        pan_x = rng.uniform(-0.04, 0.04)
        pan_y = rng.uniform(-0.03, 0.03)

        base = ImageClip(Path(scene.image_path).as_posix()).set_duration(duration)
        base = base.resize(height=height)

        def dynamic_resize(t: float) -> float:
            progress = 0.0 if duration <= 0 else min(1.0, t / duration)
            return zoom_start + (zoom_end - zoom_start) * progress

        def dynamic_position(t: float) -> tuple[float, float]:
            progress = 0.0 if duration <= 0 else min(1.0, t / duration)
            dx = int(width * pan_x * progress)
            dy = int(height * pan_y * progress)
            return ("center", "center") if dx == 0 and dy == 0 else (dx, dy)

        return base.resize(dynamic_resize).set_position(dynamic_position)
