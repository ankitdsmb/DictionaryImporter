from __future__ import annotations

from pathlib import Path

from moviepy.editor import VideoClip

from app.config import get_settings


class ExportEngine:
    def __init__(self) -> None:
        self.settings = get_settings()

    def export(self, clip: VideoClip, output_path: Path, fps: int) -> None:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        clip.write_videofile(
            output_path.as_posix(),
            fps=fps,
            codec="libx264",
            audio_codec="aac",
            preset="medium",
            threads=self.settings.moviepy_threads,
            ffmpeg_params=["-movflags", "+faststart", "-pix_fmt", "yuv420p"],
            temp_audiofile=(output_path.parent / f"{output_path.stem}.m4a").as_posix(),
            remove_temp=True,
            logger=None,
        )
