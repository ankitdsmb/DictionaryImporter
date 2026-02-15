from __future__ import annotations

import numpy as np

from app.models.render_contract import FilmGrainConfig


class FilmGrainService:
    def apply(self, frame: np.ndarray, t: float, fps: int, seed: int, config: FilmGrainConfig) -> np.ndarray:
        if not config.enabled or config.strength <= 0:
            return frame

        frame_idx = max(0, int(round(t * fps)))
        rng = np.random.default_rng(seed + frame_idx * 9973)
        sigma = 12.0 * config.strength
        grain = rng.normal(loc=0.0, scale=sigma, size=frame.shape)
        return np.clip(frame.astype(np.float32) + grain, 0, 255).astype(np.uint8)
