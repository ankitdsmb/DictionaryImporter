# Cinematic Renderer Backend (CPU-Optimized)

Production-ready FastAPI backend for deterministic cinematic video rendering intended to be orchestrated by a .NET AI workflow.

## Features

- FastAPI API with `POST /api/render`
- Advanced render contract with nested `video`, `scenes`, and `audio` settings
- Backward compatibility with the legacy flat request format
- Deterministic seed-based Ken Burns motion, film grain, and scene timing
- Crossfade transitions and cinematic serif captions
- Color grading profile `devotional_glow`
- Audio mixing with narration/music ducking, fades, and normalization
- FFmpeg export (`libx264` + `aac`, yuv420p)
- CPU-only and memory-aware processing

## Project Structure

```text
cinematic-renderer/
├── app/
│   ├── main.py
│   ├── api.py
│   ├── config.py
│   ├── models/
│   │   ├── __init__.py
│   │   ├── render_contract.py
│   │   └── schemas.py
│   ├── services/
│   │   ├── __init__.py
│   │   ├── audio_mixer.py
│   │   ├── camera_effects.py
│   │   ├── color_grading.py
│   │   ├── film_grain.py
│   │   ├── media_validation_service.py
│   │   ├── storage_service.py
│   │   ├── transitions.py
│   │   └── video_composer.py
│   ├── engines/
│   │   └── export_engine.py
│   └── pipeline/
│       ├── __init__.py
│       └── render_pipeline.py
├── storage/
│   ├── temp/
│   └── output/
└── Dockerfile
```

## Rendering Flow

1. Request model validation upgrades legacy payloads into the new nested contract.
2. File paths for image/audio are validated before rendering starts.
3. Scene clips are built with deterministic camera motion and caption overlays.
4. Scene timeline is composed with crossfade transitions and fixed FPS.
5. Per-frame color grade + deterministic film grain is applied.
6. Letterbox bars are composited.
7. Narration and music are mixed with optional ducking/fades, then normalized.
8. Final video is encoded by FFmpeg through MoviePy using `libx264`.

## Advanced Request Example

```json
{
  "request_id": "devotional-001",
  "seed": 12345,
  "video": {
    "width": 1920,
    "height": 1080,
    "fps": 30,
    "letterbox_ratio": 0.12,
    "color_grade": {
      "profile": "devotional_glow",
      "intensity": 0.8
    },
    "film_grain": {
      "enabled": true,
      "strength": 0.3
    }
  },
  "scenes": [
    {
      "image_path": "storage/frames/scene_01.png",
      "duration_seconds": 6,
      "camera": {
        "type": "kenburns",
        "intensity": 0.5
      },
      "transition": {
        "type": "crossfade",
        "duration": 0.6
      },
      "caption": {
        "text": "In the land of Vrindavan...",
        "style": "cinematic_serif"
      }
    }
  ],
  "audio": {
    "narration": {
      "path": "storage/audio/narration.wav",
      "volume": 1.0
    },
    "music": {
      "path": "storage/audio/music.mp3",
      "volume": 0.6
    },
    "mix": {
      "duck_music_under_narration": true,
      "fade_in_seconds": 2,
      "fade_out_seconds": 3
    }
  }
}
```

## Response Example

```json
{
  "request_id": "devotional-001",
  "status": "success",
  "output_video_path": "storage/output/devotional-001.mp4",
  "seed": 12345,
  "rendered_at": "2026-01-03T12:33:11.345678+00:00",
  "metrics": {
    "render_seconds": 19.721,
    "timeline_seconds": 5.4,
    "scene_count": 1
  },
  "message": "Render completed successfully"
}
```
