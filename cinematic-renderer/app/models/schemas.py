from app.models.render_contract import (  # backward-compatible import surface
    AudioConfig,
    AudioMixConfig,
    AudioTrackConfig,
    CameraConfig,
    CaptionConfig,
    ColorGradeConfig,
    FilmGrainConfig,
    RenderMetrics,
    RenderRequest,
    RenderResponse,
    SceneConfig,
    TransitionConfig,
    VideoConfig,
)

# Legacy aliases
SceneInput = SceneConfig
AudioTrackInput = AudioTrackConfig

__all__ = [
    "AudioConfig",
    "AudioMixConfig",
    "AudioTrackConfig",
    "AudioTrackInput",
    "CameraConfig",
    "CaptionConfig",
    "ColorGradeConfig",
    "FilmGrainConfig",
    "RenderMetrics",
    "RenderRequest",
    "RenderResponse",
    "SceneConfig",
    "SceneInput",
    "TransitionConfig",
    "VideoConfig",
]
