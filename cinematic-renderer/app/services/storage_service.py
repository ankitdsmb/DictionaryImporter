from __future__ import annotations

import uuid
from pathlib import Path

from app.config import get_settings
from app.utils.filesystem import ensure_dir, reset_dir


class StorageService:
    def __init__(self) -> None:
        self.settings = get_settings()
        ensure_dir(self.settings.temp_root)
        ensure_dir(self.settings.output_root)

    def create_workdir(self, request_id: str) -> Path:
        safe_id = "".join(ch for ch in request_id if ch.isalnum() or ch in {"-", "_"})
        unique = uuid.uuid4().hex[:8]
        workdir = self.settings.temp_root / f"{safe_id}-{unique}"
        return reset_dir(workdir)

    def output_path(self, request_id: str) -> Path:
        safe_id = "".join(ch for ch in request_id if ch.isalnum() or ch in {"-", "_"})
        return self.settings.output_root / f"{safe_id}.mp4"
