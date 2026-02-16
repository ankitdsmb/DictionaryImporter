from fastapi import APIRouter, HTTPException
from starlette.concurrency import run_in_threadpool

from app.models.render_contract import RenderRequest, RenderResponse
from app.pipeline.render_pipeline import RenderPipeline

router = APIRouter(prefix="/api", tags=["render"])


@router.post("/render", response_model=RenderResponse)
async def render_video(payload: RenderRequest) -> RenderResponse:
    pipeline = RenderPipeline()
    try:
        return await run_in_threadpool(pipeline.run, payload)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:  # pragma: no cover
        raise HTTPException(status_code=500, detail=f"Render failed: {exc}") from exc
