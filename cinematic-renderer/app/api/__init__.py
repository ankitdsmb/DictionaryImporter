from fastapi import APIRouter

from app.api.render_routes import router as render_router
from app.api.ui_routes import router as ui_router

router = APIRouter()
router.include_router(render_router)
router.include_router(ui_router)

__all__ = ["router"]
