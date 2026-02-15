from __future__ import annotations

import shutil
import time
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from moviepy.editor import CompositeVideoClip, concatenate_videoclips

from app.config import get_settings
from app.engines.audio_engine import AudioEngine
from app.engines.beat_engine import BeatEngine
from app.engines.export_engine import ExportEngine
from app.engines.motion_engine import MotionEngine
from app.engines.style_engine import StyleEngine
from app.models.schemas import RenderMetrics, RenderRequest, RenderResponse
from app.services.media_validation_service import MediaValidationService
from app.services.storage_service import StorageService


class RenderPipeline:
    def __init__(self) -> None:
        self.settings = get_settings()
        self.validator = MediaValidationService()
        self.storage = StorageService()
        self.beats = BeatEngine()
        self.motion = MotionEngine()
        self.audio = AudioEngine()
        self.style = StyleEngine()
        self.exporter = ExportEngine()

    def run(self, request: RenderRequest) -> RenderResponse:
        started = time.perf_counter()
        self.validator.validate(request)
        if len(request.scenes) > self.settings.max_scenes:
            raise ValueError(f"Scene count exceeds limit ({self.settings.max_scenes})")

        workdir = self.storage.create_workdir(request.request_id)
        output_path = self.storage.output_path(request.request_id)

        beat_times: list[float] = []
        if request.music is not None:
            beat_times = self.beats.detect_beats(Path(request.music.path)).tolist()

        durations = [scene.duration_seconds for scene in request.scenes]
        aligned_durations = self.beats.align_durations_to_beats(durations, np.array(beat_times)) if beat_times else durations

        total_duration = sum(aligned_durations)
        if total_duration > self.settings.max_total_duration_seconds:
            raise ValueError(
                f"Timeline duration {total_duration:.2f}s exceeds limit {self.settings.max_total_duration_seconds:.2f}s"
            )

        clips = []
        width, height = request.width, request.height
        for idx, (scene, duration) in enumerate(zip(request.scenes, aligned_durations)):
            clip = self.motion.build_scene_clip(scene, request.seed, f"scene-{idx}", width, height, duration)
            clips.append(clip)

        transition = request.transition_seconds
        if transition > 0:
            clips = [clip.crossfadein(transition) if idx > 0 else clip for idx, clip in enumerate(clips)]
            video = concatenate_videoclips(clips, method="compose", padding=-transition)
        else:
            video = concatenate_videoclips(clips, method="compose")

        video = CompositeVideoClip([video.set_position("center")], size=(width, height)).set_duration(video.duration)
        audio_mix = self.audio.mix_audio(video.duration, request.narration, request.music)
        video = video.set_audio(audio_mix)
        styled = self.style.apply_cinematic_style(
            video, request.seed, width, height, request.letterbox_ratio, request.apply_film_grain
        )

        self.exporter.export(styled, output_path, request.fps)

        render_seconds = round(time.perf_counter() - started, 3)
        metrics = RenderMetrics(
            render_seconds=render_seconds,
            timeline_seconds=round(video.duration, 3),
            beat_count=len(beat_times),
            scene_count=len(request.scenes),
        )

        self._cleanup(workdir)
        return RenderResponse(
            request_id=request.request_id,
            status="success",
            output_video_path=output_path.as_posix(),
            seed=request.seed,
            rendered_at=datetime.now(timezone.utc),
            metrics=metrics,
            message="Render completed successfully",
        )

    @staticmethod
    def _cleanup(workdir: Path) -> None:
        shutil.rmtree(workdir, ignore_errors=True)
