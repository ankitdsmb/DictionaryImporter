import os
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path


@dataclass(frozen=True)
class Settings:
    environment: str = "development"
    storage_root: Path = Path("storage")
    temp_dir_name: str = "temp"
    output_dir_name: str = "output"
    ffmpeg_binary: str = "ffmpeg"
    ffprobe_binary: str = "ffprobe"
    max_scenes: int = 24
    max_total_duration_seconds: float = 900.0
    moviepy_threads: int = 4
    default_resolution: tuple[int, int] = (1920, 1080)
    default_fps: int = 30

    @classmethod
    def from_env(cls) -> "Settings":
        return cls(**_load_prefixed_env("RENDERER_", env_file=".env"))

    @property
    def temp_root(self) -> Path:
        return self.storage_root / self.temp_dir_name

    @property
    def output_root(self) -> Path:
        return self.storage_root / self.output_dir_name


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings.from_env()


def _load_prefixed_env(prefix: str, env_file: str) -> dict[str, object]:
    values: dict[str, object] = {}

    env_path = Path(env_file)
    if env_path.exists():
        for raw_line in env_path.read_text(encoding="utf-8").splitlines():
            line = raw_line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, raw_value = line.split("=", 1)
            key = key.strip()
            if not key.startswith(prefix):
                continue
            values[_to_field_name(key, prefix)] = _coerce_value(raw_value.strip())

    for key, raw_value in os.environ.items():
        if key.startswith(prefix):
            values[_to_field_name(key, prefix)] = _coerce_value(raw_value)

    return values


def _to_field_name(env_key: str, prefix: str) -> str:
    return env_key[len(prefix) :].lower()


def _coerce_value(raw_value: str) -> object:
    cleaned = raw_value.strip().strip('"').strip("'")
    if "," in cleaned:
        parts = [part.strip() for part in cleaned.split(",") if part.strip()]
        if parts and all(part.lstrip("+-").isdigit() for part in parts):
            return tuple(int(part) for part in parts)

    lowered = cleaned.lower()
    if lowered in {"true", "false"}:
        return lowered == "true"

    if cleaned.lstrip("+-").isdigit():
        return int(cleaned)

    try:
        return float(cleaned)
    except ValueError:
        return cleaned
