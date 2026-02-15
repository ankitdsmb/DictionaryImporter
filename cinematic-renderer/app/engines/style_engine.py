from __future__ import annotations

import numpy as np
from moviepy.editor import CompositeVideoClip, ColorClip, VideoClip

from app.utils.deterministic import np_rng_for


class StyleEngine:
    def apply_cinematic_style(
        self,
        clip: VideoClip,
        seed: int,
        width: int,
        height: int,
        letterbox_ratio: float,
        film_grain: bool,
    ) -> VideoClip:
        graded = clip.fl_image(self._grade_frame)
        vignette = graded.fl_image(self._vignette_frame)
        if film_grain:
            grain_rng = np_rng_for(seed, "film-grain")
            vignette = vignette.fl(lambda gf, t: self._grain_frame(gf, t, grain_rng), apply_to=["mask"])

        bar_height = int(height * letterbox_ratio)
        top_bar = ColorClip((width, bar_height), color=(0, 0, 0)).set_duration(vignette.duration).set_position((0, 0))
        bottom_bar = ColorClip((width, bar_height), color=(0, 0, 0)).set_duration(vignette.duration).set_position(
            (0, height - bar_height)
        )
        return CompositeVideoClip([vignette, top_bar, bottom_bar], size=(width, height)).set_duration(vignette.duration)

    @staticmethod
    def _grade_frame(frame: np.ndarray) -> np.ndarray:
        f = frame.astype(np.float32)
        f[..., 0] *= 1.02
        f[..., 1] *= 0.99
        f[..., 2] *= 0.94
        f = (f - 128.0) * 1.06 + 128.0
        return np.clip(f, 0, 255).astype(np.uint8)

    @staticmethod
    def _vignette_frame(frame: np.ndarray) -> np.ndarray:
        h, w = frame.shape[:2]
        y, x = np.ogrid[:h, :w]
        cy, cx = h / 2.0, w / 2.0
        dy = (y - cy) / cy
        dx = (x - cx) / cx
        dist = np.sqrt(dx * dx + dy * dy)
        mask = np.clip(1.0 - (dist**1.7) * 0.35, 0.65, 1.0)
        return np.clip(frame.astype(np.float32) * mask[..., None], 0, 255).astype(np.uint8)

    @staticmethod
    def _grain_frame(get_frame, t: float, rng: np.random.Generator) -> np.ndarray:
        frame = get_frame(t).astype(np.float32)
        noise = rng.normal(0.0, 3.0, frame.shape)
        return np.clip(frame + noise, 0, 255).astype(np.uint8)
