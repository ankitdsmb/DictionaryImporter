from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field, model_validator


class ColorGradeConfig(BaseModel):
    profile: Literal["none", "devotional_glow"] = "none"
    intensity: float = Field(default=0.0, ge=0.0, le=1.0)


class FilmGrainConfig(BaseModel):
    enabled: bool = False
    strength: float = Field(default=0.0, ge=0.0, le=1.0)


class VideoConfig(BaseModel):
    width: int = Field(default=1920, ge=320, le=3840)
    height: int = Field(default=1080, ge=240, le=2160)
    fps: int = Field(default=30, ge=12, le=60)
    letterbox_ratio: float = Field(default=0.12, ge=0.0, le=0.3)
    color_grade: ColorGradeConfig = Field(default_factory=ColorGradeConfig)
    film_grain: FilmGrainConfig = Field(default_factory=FilmGrainConfig)


class CameraConfig(BaseModel):
    type: Literal["static", "kenburns"] = "kenburns"
    intensity: float = Field(default=0.45, ge=0.0, le=1.0)


class TransitionConfig(BaseModel):
    type: Literal["none", "crossfade"] = "crossfade"
    duration: float = Field(default=0.5, ge=0.0, le=3.0)


class CaptionConfig(BaseModel):
    text: str = Field(..., min_length=1, max_length=240)
    style: Literal["cinematic_serif"] = "cinematic_serif"


class SceneConfig(BaseModel):
    image_path: str = Field(..., description="Filesystem path to a scene image")
    duration_seconds: float = Field(default=6.0, gt=0.25, le=60.0)
    camera: CameraConfig = Field(default_factory=CameraConfig)
    transition: TransitionConfig = Field(default_factory=TransitionConfig)
    caption: CaptionConfig | None = None


class AudioTrackConfig(BaseModel):
    path: str
    volume: float = Field(default=1.0, gt=0.0, le=2.0)


class AudioMixConfig(BaseModel):
    duck_music_under_narration: bool = True
    fade_in_seconds: float = Field(default=2.0, ge=0.0, le=15.0)
    fade_out_seconds: float = Field(default=3.0, ge=0.0, le=15.0)


class AudioConfig(BaseModel):
    narration: AudioTrackConfig | None = None
    music: AudioTrackConfig | None = None
    mix: AudioMixConfig = Field(default_factory=AudioMixConfig)


class RenderRequest(BaseModel):
    request_id: str = Field(..., min_length=3, max_length=128)
    seed: int = Field(..., ge=0, le=2_147_483_647)
    video: VideoConfig = Field(default_factory=VideoConfig)
    scenes: list[SceneConfig] = Field(..., min_length=1, max_length=24)
    audio: AudioConfig = Field(default_factory=AudioConfig)

    @model_validator(mode="before")
    @classmethod
    def _upgrade_legacy_contract(cls, data: Any) -> Any:
        if not isinstance(data, dict):
            return data

        if "video" in data and "audio" in data:
            return data

        upgraded = dict(data)
        upgraded["video"] = {
            "width": data.get("width", 1920),
            "height": data.get("height", 1080),
            "fps": data.get("fps", 30),
            "letterbox_ratio": data.get("letterbox_ratio", 0.12),
            "color_grade": {
                "profile": "devotional_glow",
                "intensity": 0.6,
            },
            "film_grain": {
                "enabled": bool(data.get("apply_film_grain", False)),
                "strength": 0.25 if data.get("apply_film_grain", False) else 0.0,
            },
        }

        upgraded_scenes: list[dict[str, Any]] = []
        for scene in data.get("scenes", []):
            scene_dict = dict(scene)
            caption = scene_dict.pop("caption", None)
            upgraded_scenes.append(
                {
                    "image_path": scene_dict.get("image_path"),
                    "duration_seconds": scene_dict.get("duration_seconds", 6.0),
                    "camera": {
                        "type": "kenburns",
                        "intensity": 0.45,
                    },
                    "transition": {
                        "type": "crossfade",
                        "duration": data.get("transition_seconds", 0.5),
                    },
                    "caption": {"text": caption, "style": "cinematic_serif"} if caption else None,
                }
            )
        upgraded["scenes"] = upgraded_scenes

        upgraded["audio"] = {
            "narration": data.get("narration"),
            "music": data.get("music"),
            "mix": {
                "duck_music_under_narration": True,
                "fade_in_seconds": 2.0,
                "fade_out_seconds": 3.0,
            },
        }
        return upgraded

    @model_validator(mode="after")
    def validate_audio_presence(self) -> "RenderRequest":
        if self.audio.narration is None and self.audio.music is None:
            raise ValueError("At least one audio track (narration or music) must be provided")
        return self


class RenderMetrics(BaseModel):
    render_seconds: float
    timeline_seconds: float
    scene_count: int


class RenderResponse(BaseModel):
    request_id: str
    status: Literal["success", "failed"]
    output_video_path: str | None = None
    seed: int
    rendered_at: datetime
    metrics: RenderMetrics | None = None
    message: str | None = None
