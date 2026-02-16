from __future__ import annotations

import json
import math
import threading
import urllib.error
import urllib.request
import uuid
import wave
from pathlib import Path
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, Field, model_validator
from PIL import Image, ImageDraw, ImageFont
from fastapi.responses import HTMLResponse
from fastapi.templating import Jinja2Templates

from app.config import get_settings
from app.models.render_contract import (
    AudioConfig,
    AudioTrackConfig,
    CameraConfig,
    CaptionConfig,
    ColorGradeConfig,
    FilmGrainConfig,
    RenderRequest,
    SceneConfig,
    TransitionConfig,
    VideoConfig,
)
from app.pipeline.render_pipeline import RenderPipeline

router = APIRouter(tags=["ui"])
templates = Jinja2Templates(directory=Path(__file__).resolve().parents[1] / "templates")

_REQUESTS_LOCK = threading.Lock()
_REQUESTS: dict[str, dict[str, object]] = {}


class GenerateVideoPayload(BaseModel):
    topic: str = Field(default="Cinematic devotional video", min_length=3, max_length=280)
    style: str = Field(default="Devotional")
    resolution: str = Field(default="1080p")
    film_grain: bool = Field(default=False)
    ken_burns: bool = Field(default=True)
    audio_path: str | None = Field(default=None, description="Legacy field accepted for backwards compatibility")

    @model_validator(mode="before")
    @classmethod
    def _upgrade_legacy_payload(cls, data: Any) -> Any:
        if not isinstance(data, dict):
            return data

        upgraded = dict(data)
        provided_topic = str(upgraded.get("topic", "")).strip()
        if provided_topic:
            return upgraded

        audio_path = upgraded.get("audio_path")
        if isinstance(audio_path, str) and audio_path.strip():
            topic_from_audio = Path(audio_path).stem.replace("_", " ").replace("-", " ").strip()
            upgraded["topic"] = topic_from_audio or "Cinematic devotional video"
        else:
            upgraded["topic"] = "Cinematic devotional video"

        return upgraded


@router.get("/", response_class=HTMLResponse)
def home_page(request: Request):
    return templates.TemplateResponse("index.html", {"request": request})


@router.post("/generate-video")
def generate_video(payload: GenerateVideoPayload) -> dict[str, str]:
    request_id = uuid.uuid4().hex[:12]
    with _REQUESTS_LOCK:
        _REQUESTS[request_id] = {
            "status": "processing",
            "progress": 5,
            "message": "Request accepted",
        }

    worker = threading.Thread(target=_run_generation, args=(request_id, payload), daemon=True)
    worker.start()
    return {"status": "processing", "request_id": request_id}


@router.get("/status/{request_id}")
def generation_status(request_id: str) -> dict[str, object]:
    with _REQUESTS_LOCK:
        status = _REQUESTS.get(request_id)
    if status is None:
        raise HTTPException(status_code=404, detail="request_id not found")
    return status


def _run_generation(request_id: str, payload: GenerateVideoPayload) -> None:
    settings = get_settings()
    workdir = settings.temp_root / f"ui-{request_id}"
    workdir.mkdir(parents=True, exist_ok=True)

    try:
        _update_status(request_id, progress=10, message="Generating scenes with local Ollama")
        scenes = _generate_scenes(payload.topic, payload.style)

        _update_status(request_id, progress=35, message="Generating narration audio")
        narration_path = workdir / "narration.wav"
        _generate_narration(payload.topic, scenes, narration_path)

        _update_status(request_id, progress=50, message="Preparing cinematic assets")
        image_paths = _generate_scene_images(scenes, payload.style, workdir)
        music_path = _ensure_music_track()

        width, height = (1920, 1080) if payload.resolution == "1080p" else (1280, 720)
        camera_type = "kenburns" if payload.ken_burns else "static"

        render_seed = int(request_id[:8], 16) % (2_147_483_647 + 1)
        render_request = RenderRequest(
            request_id=request_id,
            seed=render_seed,
            video=VideoConfig(
                width=width,
                height=height,
                fps=30,
                letterbox_ratio=0.1,
                color_grade=ColorGradeConfig(profile="devotional_glow", intensity=0.65),
                film_grain=FilmGrainConfig(enabled=payload.film_grain, strength=0.25 if payload.film_grain else 0.0),
            ),
            scenes=[
                SceneConfig(
                    image_path=image_paths[idx].as_posix(),
                    duration_seconds=6.0,
                    camera=CameraConfig(type=camera_type, intensity=0.5),
                    transition=TransitionConfig(type="crossfade", duration=0.6),
                    caption=CaptionConfig(text=scene, style="cinematic_serif"),
                )
                for idx, scene in enumerate(scenes)
            ],
            audio=AudioConfig(
                narration=AudioTrackConfig(path=narration_path.as_posix(), volume=1.0),
                music=AudioTrackConfig(path=music_path.as_posix(), volume=0.35),
            ),
        )

        _update_status(request_id, progress=70, message="Composing cinematic video")
        result = RenderPipeline().run(render_request)
        video_name = Path(result.output_video_path or "").name

        _update_status(
            request_id,
            status="completed",
            progress=100,
            message="Video generated successfully",
            video_url=f"/videos/{video_name}",
        )
    except Exception as exc:  # pragma: no cover
        _update_status(request_id, status="failed", progress=100, message=str(exc))


def _generate_scenes(topic: str, style: str) -> list[str]:
    prompt = (
        "Create 4 short cinematic scene lines for a video. "
        f"Topic: {topic}. Style: {style}. "
        "Each line should be under 12 words and spiritually evocative."
    )
    ollama_response = _call_ollama(prompt)
    if ollama_response:
        lines = [line.strip(" -•\t") for line in ollama_response.splitlines() if line.strip()]
        cleaned = [line for line in lines if len(line.split()) >= 3]
        if len(cleaned) >= 4:
            return cleaned[:4]

    return [
        f"Dawn rises over a sacred horizon for {topic}",
        f"Devotees gather as {topic} chants fill the air",
        f"Golden light and incense reveal timeless {style.lower()} emotion",
        "A serene closing prayer leaves a cinematic spiritual calm",
    ]


def _call_ollama(prompt: str) -> str | None:
    body = json.dumps({"model": "llama3", "prompt": prompt, "stream": False}).encode("utf-8")
    request = urllib.request.Request(
        "http://127.0.0.1:11434/api/generate",
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=8) as response:
            payload = json.loads(response.read().decode("utf-8"))
            return str(payload.get("response", "")).strip() or None
    except (urllib.error.URLError, TimeoutError, ValueError, json.JSONDecodeError):
        return None


def _generate_narration(topic: str, scenes: list[str], output_path: Path) -> None:
    narration_text = f"{topic}. " + ". ".join(scenes)
    if _try_pyttsx3(narration_text, output_path):
        return
    _synthesize_voice_placeholder(output_path, duration_seconds=max(10, len(narration_text) // 12))


def _try_pyttsx3(text: str, output_path: Path) -> bool:
    try:
        import pyttsx3  # type: ignore

        engine = pyttsx3.init()
        engine.setProperty("rate", 155)
        engine.save_to_file(text, output_path.as_posix())
        engine.runAndWait()
        return output_path.exists() and output_path.stat().st_size > 0
    except Exception:
        return False


def _synthesize_voice_placeholder(path: Path, duration_seconds: int) -> None:
    sample_rate = 22050
    t = np.linspace(0, duration_seconds, sample_rate * duration_seconds, endpoint=False)
    envelope = 0.3 + 0.2 * np.sin(2 * math.pi * t / 4)
    wave_data = (np.sin(2 * math.pi * 170 * t) * envelope * 0.25).astype(np.float32)
    _write_wav(path, wave_data, sample_rate)


def _ensure_music_track() -> Path:
    settings = get_settings()
    assets_dir = settings.storage_root / "assets"
    assets_dir.mkdir(parents=True, exist_ok=True)
    music_path = assets_dir / "royalty_free_bg.wav"
    if music_path.exists() and music_path.stat().st_size > 0:
        return music_path

    sample_rate = 22050
    duration_seconds = 24
    t = np.linspace(0, duration_seconds, sample_rate * duration_seconds, endpoint=False)
    pad = 0.5 * (1 - np.cos(2 * math.pi * np.minimum(t, duration_seconds - t) / duration_seconds))
    chord = (
        np.sin(2 * math.pi * 130.81 * t)
        + 0.7 * np.sin(2 * math.pi * 164.81 * t)
        + 0.6 * np.sin(2 * math.pi * 196.00 * t)
    )
    audio = (chord * pad * 0.12).astype(np.float32)
    _write_wav(music_path, audio, sample_rate)
    return music_path


def _write_wav(path: Path, data: np.ndarray, sample_rate: int) -> None:
    clipped = np.clip(data, -1.0, 1.0)
    pcm = (clipped * 32767).astype(np.int16)
    with wave.open(path.as_posix(), "wb") as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(sample_rate)
        wav_file.writeframes(pcm.tobytes())


def _generate_scene_images(scenes: list[str], style: str, workdir: Path) -> list[Path]:
    style_palette = {
        "devotional": ((98, 69, 188), (238, 173, 79)),
        "mythological": ((36, 88, 139), (213, 107, 73)),
        "motivational": ((23, 126, 137), (246, 174, 45)),
    }
    key = style.lower().strip()
    start, end = style_palette.get(key, style_palette["devotional"])

    font = _load_font(54)
    outputs: list[Path] = []
    for idx, text in enumerate(scenes, start=1):
        image = Image.new("RGB", (1920, 1080), color=(12, 12, 18))
        draw = ImageDraw.Draw(image)

        for y in range(1080):
            mix = y / 1080
            color = (
                int(start[0] * (1 - mix) + end[0] * mix),
                int(start[1] * (1 - mix) + end[1] * mix),
                int(start[2] * (1 - mix) + end[2] * mix),
            )
            draw.line([(0, y), (1920, y)], fill=color)

        draw.rectangle((120, 740, 1800, 980), fill=(0, 0, 0, 120))
        draw.text((160, 790), f"Scene {idx}", font=font, fill=(255, 236, 190))
        draw.text((160, 860), text[:70], font=_load_font(42), fill=(240, 240, 240))

        output = workdir / f"scene_{idx}.png"
        image.save(output)
        outputs.append(output)
    return outputs


def _load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for candidate in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    ):
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size=size)
    return ImageFont.load_default()


def _update_status(
    request_id: str,
    *,
    status: str | None = None,
    progress: int | None = None,
    message: str | None = None,
    video_url: str | None = None,
) -> None:
    with _REQUESTS_LOCK:
        current = _REQUESTS.setdefault(request_id, {})
        if status is not None:
            current["status"] = status
        if progress is not None:
            current["progress"] = progress
        if message is not None:
            current["message"] = message
        if video_url is not None:
            current["video_url"] = video_url
