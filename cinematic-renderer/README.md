# Cinematic Renderer Backend (CPU-Optimized)

Production-ready FastAPI backend for deterministic cinematic video rendering intended to be orchestrated by a .NET AI workflow.

## Features

- FastAPI API with `POST /api/render`
- Deterministic seed-based motion per scene
- Beat detection and beat-aligned scene timing with `librosa`
- Ken Burns style zoom/pan motion with `moviepy`
- Crossfade scene transitions
- Cinematic look: letterbox, subtle vignette, mild color grading, optional film grain
- Audio mix: narration + looping music with peak normalization
- FFmpeg export (`libx264` + `aac`, yuv420p)
- Clean temporary workspace creation + cleanup
- Docker-ready runtime

## Project Structure

```text
cinematic-renderer/
├── app/
│   ├── main.py
│   ├── api.py
│   ├── config.py
│   ├── models/
│   │   ├── __init__.py
│   │   └── schemas.py
│   ├── services/
│   │   ├── __init__.py
│   │   ├── media_validation_service.py
│   │   └── storage_service.py
│   ├── engines/
│   │   ├── __init__.py
│   │   ├── audio_engine.py
│   │   ├── beat_engine.py
│   │   ├── export_engine.py
│   │   ├── motion_engine.py
│   │   └── style_engine.py
│   ├── utils/
│   │   ├── __init__.py
│   │   ├── deterministic.py
│   │   └── filesystem.py
│   └── pipeline/
│       ├── __init__.py
│       └── render_pipeline.py
├── storage/
│   ├── temp/
│   └── output/
├── requirements.txt
├── Dockerfile
└── README.md
```

## Requirements

- Python 3.10+
- FFmpeg installed and available in PATH
- 12 GB RAM target
- 4 CPU cores target
- Windows or Linux
- No GPU required

## Performance & Capacity Expectations

- **RAM:** Typical 1080p render with 6-10 scenes uses ~2-6 GB peak memory.
- **CPU:** Uses CPU-only encoding (`libx264`) and configurable thread count (`RENDERER_MOVIEPY_THREADS`, default 4).
- **Render Time:** Usually 0.5x to 2.5x real-time depending on FPS, resolution, and filter complexity.
- **Disk:** Keep at least 2-5 GB free for temp data and output media.
- **Limitations:** No deep learning models, no GPU acceleration, intended for image + audio composition workloads.

## Installation

```bash
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt
```

## Run API

```bash
uvicorn app.main:app --host 0.0.0.0 --port 8000
```

Health check:

```bash
curl http://localhost:8000/health
```

## Render Request Example

```json
{
  "request_id": "devotional-001",
  "seed": 12345,
  "width": 1920,
  "height": 1080,
  "fps": 30,
  "transition_seconds": 0.5,
  "letterbox_ratio": 0.12,
  "apply_film_grain": true,
  "scenes": [
    { "image_path": "assets/scene1.jpg", "duration_seconds": 6.0 },
    { "image_path": "assets/scene2.jpg", "duration_seconds": 6.0 }
  ],
  "narration": { "path": "assets/narration.wav", "volume": 1.0 },
  "music": { "path": "assets/music.mp3", "volume": 0.4 }
}
```

```bash
curl -X POST http://localhost:8000/api/render \
  -H "Content-Type: application/json" \
  -d @request.json
```

## Docker

```bash
docker build -t cinematic-renderer .
docker run --rm -p 8000:8000 -v $(pwd)/storage:/app/storage cinematic-renderer
```

## Determinism Notes

- Motion parameters are generated from request seed + deterministic scene namespace.
- Beat alignment and ordering are deterministic for identical media inputs.
- No global random state is used.

## ZIP Packaging

Create distributable ZIP from repository root:

```bash
cd ..
zip -r cinematic-renderer.zip cinematic-renderer
```

Or run:

```bash
bash cinematic-renderer/package_zip.sh
```
