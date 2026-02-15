from fastapi import APIRouter, HTTPException

from app.models.schemas import RenderRequest, RenderResponse
from app.pipeline.render_pipeline import RenderPipeline

router = APIRouter(prefix="/api", tags=["render"])


@router.post("/render", response_model=RenderResponse)
def render_video(payload: RenderRequest) -> RenderResponse:
    pipeline = RenderPipeline()
    try:
        return pipeline.run(payload)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:  # pragma: no cover
        raise HTTPException(status_code=500, detail=f"Render failed: {exc}") from exc
