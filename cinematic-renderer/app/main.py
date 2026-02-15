from fastapi import FastAPI

from app.api import router
from app.config import get_settings


def create_app() -> FastAPI:
    settings = get_settings()
    app = FastAPI(
        title="Cinematic Renderer API",
        version="1.0.0",
        description="CPU-optimized deterministic cinematic video rendering backend",
    )
    app.include_router(router)

    @app.get("/health", tags=["system"])
    def health() -> dict[str, str]:
        return {"status": "ok", "environment": settings.environment}

    return app


app = create_app()
