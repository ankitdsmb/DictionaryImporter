from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field, model_validator


class SceneInput(BaseModel):
    image_path: str = Field(..., description="Filesystem path to a scene image")
    duration_seconds: float = Field(default=6.0, gt=0.25, le=60.0)
    caption: str | None = Field(default=None)


class AudioTrackInput(BaseModel):
    path: str
    volume: float = Field(default=1.0, gt=0.0, le=2.0)


class RenderRequest(BaseModel):
    request_id: str = Field(..., min_length=3, max_length=128)
    seed: int = Field(..., ge=0, le=2_147_483_647)
    width: int = Field(default=1920, ge=320, le=3840)
    height: int = Field(default=1080, ge=240, le=2160)
    fps: int = Field(default=30, ge=12, le=60)
    transition_seconds: float = Field(default=0.5, ge=0.0, le=2.0)
    letterbox_ratio: float = Field(default=0.12, ge=0.0, le=0.3)
    apply_film_grain: bool = Field(default=False)
    scenes: list[SceneInput] = Field(..., min_length=1, max_length=24)
    narration: AudioTrackInput | None = None
    music: AudioTrackInput | None = None

    @model_validator(mode="after")
    def validate_audio_presence(self) -> "RenderRequest":
        if self.narration is None and self.music is None:
            raise ValueError("At least one audio track (narration or music) must be provided")
        return self


class RenderMetrics(BaseModel):
    render_seconds: float
    timeline_seconds: float
    beat_count: int
    scene_count: int


class RenderResponse(BaseModel):
    request_id: str
    status: Literal["success", "failed"]
    output_video_path: str | None = None
    seed: int
    rendered_at: datetime
    metrics: RenderMetrics | None = None
    message: str | None = None
