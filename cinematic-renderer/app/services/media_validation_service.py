from pathlib import Path

from app.models.schemas import RenderRequest


class MediaValidationService:
    @staticmethod
    def validate(request: RenderRequest) -> None:
        for scene in request.scenes:
            path = Path(scene.image_path)
            if not path.exists() or not path.is_file():
                raise ValueError(f"Scene image not found: {scene.image_path}")

        if request.music is not None:
            music_path = Path(request.music.path)
            if not music_path.exists() or not music_path.is_file():
                raise ValueError(f"Music track not found: {request.music.path}")

        if request.narration is not None:
            narration_path = Path(request.narration.path)
            if not narration_path.exists() or not narration_path.is_file():
                raise ValueError(f"Narration track not found: {request.narration.path}")
