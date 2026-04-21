"""
Python Sidecar for TextToSqlAgent
Provides ML-based Intent Classification and Data Visualization
"""
import os
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, Response, status
from fastapi.middleware.cors import CORSMiddleware

from app.routers import intent, visualize

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s"
)
logger = logging.getLogger("sidecar")


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Load ML models on startup, cleanup on shutdown."""
    logger.info("🚀 Loading ML models...")
    
    # Load intent classifier model
    from app.routers.intent import load_model
    load_model()
    
    logger.info("✅ All models loaded successfully")
    yield
    logger.info("🛑 Shutting down sidecar...")


app = FastAPI(
    title="TextToSqlAgent Python Sidecar",
    version="1.0.0",
    description="ML-based Intent Classifier + Data Visualization for TextToSqlAgent",
    lifespan=lifespan,
)

# CORS — internal network only
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5000", "http://localhost:3000"],
    allow_methods=["POST", "GET"],
    allow_headers=["*"],
)

# Register routers
app.include_router(intent.router, prefix="/api", tags=["Intent Classification"])
app.include_router(visualize.router, prefix="/api", tags=["Data Visualization"])


@app.get("/health")
async def health_check():
    """Service health summary for operators and dependent services."""
    from app.routers.intent import get_model_info, get_service_status
    model_info = get_model_info()
    service_status = get_service_status()
    
    return {
        "status": "healthy" if service_status["routing_ready"] else "degraded",
        "service": "python-sidecar",
        "version": "1.0.0",
        "service_state": service_status["service_state"],
        "advisory_only": service_status["advisory_only"],
        "models": {
            "intent_classifier": model_info
        }
    }


@app.get("/health/live")
async def liveness_check():
    """Liveness probe: process is up and serving requests."""
    return {
        "status": "alive",
        "service": "python-sidecar",
        "version": "1.0.0",
    }


@app.get("/health/ready")
async def readiness_check(response: Response):
    """Readiness probe: classifier is ready for enterprise ML-assisted routing."""
    from app.routers.intent import get_service_status

    service_status = get_service_status()
    if not service_status["routing_ready"]:
        response.status_code = status.HTTP_503_SERVICE_UNAVAILABLE

    return {
        "status": "ready" if service_status["routing_ready"] else "not_ready",
        "service": "python-sidecar",
        "version": "1.0.0",
        **service_status,
    }
