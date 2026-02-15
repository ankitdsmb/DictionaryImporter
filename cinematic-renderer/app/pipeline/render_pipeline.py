from __future__ import annotations

import shutil
import time
from datetime import datetime, timezone
from pathlib import Path

from app.config import get_settings
from app.engines.export_engine import ExportEngine
from app.models.render_contract import RenderMetrics, RenderRequest, RenderResponse
from app.services.audio_mixer import AudioMixerService
from app.services.media_validation_service import MediaValidationService
from app.services.storage_service import StorageService
from app.services.video_composer import VideoComposerService


class RenderPipeline:
    def __init__(self) -> None:
        self.settings = get_settings()
        self.validator = MediaValidationService()
        self.storage = StorageService()
        self.video_composer = VideoComposerService()
        self.audio_mixer = AudioMixerService()
        self.exporter = ExportEngine()

    def run(self, request: RenderRequest) -> RenderResponse:
        started = time.perf_counter()
        self.validator.validate(request)

        if len(request.scenes) > self.settings.max_scenes:
            raise ValueError(f"Scene count exceeds limit ({self.settings.max_scenes})")

        timeline_seconds = sum(scene.duration_seconds for scene in request.scenes)
        if timeline_seconds > self.settings.max_total_duration_seconds:
            raise ValueError(
                f"Timeline duration {timeline_seconds:.2f}s exceeds limit {self.settings.max_total_duration_seconds:.2f}s"
            )

        workdir = self.storage.create_workdir(request.request_id)
        output_path = self.storage.output_path(request.request_id)

        clip = self.video_composer.compose(request)
        mixed_audio = self.audio_mixer.mix(clip.duration, request.audio)
        final_clip = clip.set_audio(mixed_audio)

        self.exporter.export(final_clip, output_path, request.video.fps)

        render_seconds = round(time.perf_counter() - started, 3)
        metrics = RenderMetrics(
            render_seconds=render_seconds,
            timeline_seconds=round(final_clip.duration, 3),
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
