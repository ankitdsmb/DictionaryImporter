from __future__ import annotations

import numpy as np

from app.models.render_contract import ColorGradeConfig


class ColorGradingService:
    def apply(self, frame: np.ndarray, config: ColorGradeConfig) -> np.ndarray:
        if config.profile == "none" or config.intensity <= 0:
            return frame

        if config.profile == "devotional_glow":
            return self._devotional_glow(frame, config.intensity)

        return frame

    @staticmethod
    def _devotional_glow(frame: np.ndarray, intensity: float) -> np.ndarray:
        f = frame.astype(np.float32)
        warmth = np.array([1.06, 1.02, 0.94], dtype=np.float32)
        cooled_shadow = np.array([0.97, 0.99, 1.03], dtype=np.float32)

        lifted = (f - 128.0) * (1.0 + 0.08 * intensity) + 128.0
        highlights = np.clip(lifted / 255.0, 0.0, 1.0)
        shadow_mix = 1.0 - highlights

        graded = lifted * (warmth * highlights + cooled_shadow * shadow_mix)
        bloom = np.clip((graded - 180.0) / 75.0, 0.0, 1.0)
        glow = graded + bloom * (14.0 * intensity)
        return np.clip(glow, 0, 255).astype(np.uint8)
