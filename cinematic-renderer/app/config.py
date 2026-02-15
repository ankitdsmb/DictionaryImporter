from functools import lru_cache
from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    environment: str = Field(default="development")
    storage_root: Path = Field(default=Path("storage"))
    temp_dir_name: str = Field(default="temp")
    output_dir_name: str = Field(default="output")
    ffmpeg_binary: str = Field(default="ffmpeg")
    ffprobe_binary: str = Field(default="ffprobe")
    max_scenes: int = Field(default=24)
    max_total_duration_seconds: float = Field(default=900.0)
    moviepy_threads: int = Field(default=4)
    default_resolution: tuple[int, int] = Field(default=(1920, 1080))
    default_fps: int = Field(default=30)

    model_config = SettingsConfigDict(env_prefix="RENDERER_", env_file=".env", extra="ignore")

    @property
    def temp_root(self) -> Path:
        return self.storage_root / self.temp_dir_name

    @property
    def output_root(self) -> Path:
        return self.storage_root / self.output_dir_name


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings()
