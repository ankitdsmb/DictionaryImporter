from __future__ import annotations

from pathlib import Path

import numpy as np
from moviepy.editor import ColorClip, CompositeVideoClip, ImageClip
from PIL import Image, ImageDraw, ImageFont

from app.models.render_contract import RenderRequest, SceneConfig
from app.services.camera_effects import CameraEffectsService
from app.services.color_grading import ColorGradingService
from app.services.film_grain import FilmGrainService
from app.services.transitions import TransitionService


class VideoComposerService:
    def __init__(self) -> None:
        self.camera = CameraEffectsService()
        self.transitions = TransitionService()
        self.color = ColorGradingService()
        self.grain = FilmGrainService()

    def compose(self, request: RenderRequest):
        width = request.video.width
        height = request.video.height

        scene_clips = [
            self._build_scene_clip(scene, idx, request.seed, width, height)
            for idx, scene in enumerate(request.scenes)
        ]
        timeline = self.transitions.compose(scene_clips, request.scenes, width, height)
        timeline = timeline.set_fps(request.video.fps)

        graded = timeline.fl(lambda gf, t: self._style_frame(gf, t, request), apply_to=[])
        letterboxed = self._apply_letterbox(graded, width, height, request.video.letterbox_ratio)
        return letterboxed.set_duration(timeline.duration).set_fps(request.video.fps)

    def _build_scene_clip(self, scene: SceneConfig, idx: int, seed: int, width: int, height: int):
        base = self.camera.build_clip(
            image_path=scene.image_path,
            duration=scene.duration_seconds,
            width=width,
            height=height,
            seed=seed,
            namespace=f"scene-{idx}",
            camera=scene.camera,
        )

        overlays = [base]
        caption = self._build_caption(scene, width, height)
        if caption is not None:
            overlays.append(caption.set_duration(scene.duration_seconds))

        return CompositeVideoClip(overlays, size=(width, height)).set_duration(scene.duration_seconds)

    @staticmethod
    def _build_caption(scene: SceneConfig, width: int, height: int):
        if scene.caption is None:
            return None

        txt = scene.caption.text
        font_size = max(30, int(height * 0.045))
        font = _load_font(font_size)

        image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        draw = ImageDraw.Draw(image)
        text_box_width = int(width * 0.85)
        wrapped = _wrap_text(draw, txt, font, text_box_width)
        bbox = draw.multiline_textbbox((0, 0), wrapped, font=font, spacing=8)
        tw = bbox[2] - bbox[0]
        th = bbox[3] - bbox[1]
        x = (width - tw) // 2
        y = int(height * 0.78) - th

        draw.multiline_text((x + 2, y + 2), wrapped, font=font, fill=(0, 0, 0, 180), align="center", spacing=8)
        draw.multiline_text((x, y), wrapped, font=font, fill=(245, 238, 216, 240), align="center", spacing=8)

        return ImageClip(np.array(image), ismask=False).set_position((0, 0))

    def _style_frame(self, get_frame, t: float, request: RenderRequest):
        frame = get_frame(t)
        frame = self.color.apply(frame, request.video.color_grade)
        frame = self.grain.apply(frame, t, request.video.fps, request.seed, request.video.film_grain)
        return frame

    @staticmethod
    def _apply_letterbox(clip, width: int, height: int, ratio: float):
        if ratio <= 0:
            return clip
        bar_height = int(height * ratio)
        top_bar = ColorClip((width, bar_height), color=(0, 0, 0)).set_duration(clip.duration).set_position((0, 0))
        bottom_bar = ColorClip((width, bar_height), color=(0, 0, 0)).set_duration(clip.duration).set_position(
            (0, height - bar_height)
        )
        return CompositeVideoClip([clip, top_bar, bottom_bar], size=(width, height)).set_duration(clip.duration)


def _load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for candidate in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSerif-Regular.ttf",
    ):
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size=size)
    return ImageFont.load_default()


def _wrap_text(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.ImageFont, max_width: int) -> str:
    words = text.split()
    lines: list[str] = []
    current = ""
    for word in words:
        proposal = f"{current} {word}".strip()
        width = draw.textbbox((0, 0), proposal, font=font)[2]
        if width <= max_width or not current:
            current = proposal
        else:
            lines.append(current)
            current = word
    if current:
        lines.append(current)
    return "\n".join(lines)
