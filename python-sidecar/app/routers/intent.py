"""
Intent Classification Router
POST /api/classify — ML-based intent classification for SQL queries
Replaces 1007 lines of C# Regex patterns with a trained ML model
"""
import json
import os
import re
import logging
from typing import Optional

import joblib
import numpy as np
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

logger = logging.getLogger("sidecar.intent")

router = APIRouter()

# ============================================================
# Models & Schemas
# ============================================================

class ClassifyRequest(BaseModel):
    """Input schema for intent classification."""
    question: str = Field(..., min_length=1, max_length=2000, description="User query text")
    language: str = Field(default="auto", description="Language hint: 'vi', 'en', or 'auto'")


class ClassifyResponse(BaseModel):
    """Output schema — maps 1:1 to C# IntentClassificationResult."""
    intent: str = Field(..., description="Classified intent: SELECT, AGGREGATE, SCHEMA_QUERY, WRITE_INSERT, WRITE_UPDATE, WRITE_DELETE, DDL, AMBIGUOUS")
    confidence: float = Field(..., ge=0.0, le=1.0, description="Classification confidence score")
    is_write_operation: bool = Field(default=False, description="True if query modifies data")
    requires_confirmation: bool = Field(default=False, description="True if user must confirm before execution")
    sub_intent: Optional[str] = Field(default=None, description="Sub-classification detail")
    detected_language: str = Field(default="en", description="Detected input language")
    classifier_mode: str = Field(default="rule_fallback", description="Classification mode: ml, safety_override, or rule_fallback")
    service_state: str = Field(default="not_ready", description="Service state: ready, degraded, model_missing, rule_fallback, not_ready")
    model_version: str = Field(default="not_loaded", description="Loaded model version")
    advisory_only: bool = Field(default=True, description="True when the service should be treated as advisory by orchestrators")
    fallback_reason: Optional[str] = Field(default=None, description="Reason for degraded or fallback behavior")


# ============================================================
# Intent Definitions & Safety Rules
# ============================================================

INTENT_LABELS = [
    "SELECT", "AGGREGATE", "SCHEMA_QUERY",
    "WRITE_INSERT", "WRITE_UPDATE", "WRITE_DELETE",
    "DDL", "AMBIGUOUS"
]

WRITE_INTENTS = {"WRITE_INSERT", "WRITE_UPDATE", "WRITE_DELETE"}
CONFIRM_INTENTS = {"WRITE_INSERT", "WRITE_UPDATE", "WRITE_DELETE", "DDL"}

# Safety-critical patterns — these ALWAYS override the ML model
# These are the absolute minimum regex guards for dangerous operations
DDL_PATTERNS = [
    r"\b(DROP|ALTER|TRUNCATE|CREATE)\s+(TABLE|DATABASE|INDEX|VIEW|SCHEMA|COLUMN)\b",
    r"\bDROP\s+ALL\b",
    r"\bTRUNCATE\b",
]

DELETE_ALL_PATTERNS = [
    r"\b(DELETE|XÓA|XOA)\s+(ALL|TẤT\s*CẢ|TAT\s*CA|TOÀN\s*BỘ|TOAN\s*BO|HẾT|HET)\b",
    r"\bDELETE\s+FROM\s+\w+\s*$",  # DELETE FROM table (no WHERE)
]

# ============================================================
# Model Management
# ============================================================

_model = None
_vectorizer = None
_label_encoder = None
_model_version = "not_loaded"
_model_metadata: dict = {}
_service_state = "not_ready"
_fallback_reason: Optional[str] = "startup_not_completed"

SERVICE_STATE_READY = "ready"
SERVICE_STATE_DEGRADED = "degraded"
SERVICE_STATE_MODEL_MISSING = "model_missing"
SERVICE_STATE_RULE_FALLBACK = "rule_fallback"
SERVICE_STATE_NOT_READY = "not_ready"

CLASSIFIER_MODE_ML = "ml"
CLASSIFIER_MODE_SAFETY_OVERRIDE = "safety_override"
CLASSIFIER_MODE_RULE_FALLBACK = "rule_fallback"

ADVISORY_ONLY = os.environ.get("SIDECAR_ADVISORY_ONLY", "true").strip().lower() not in {"false", "0", "no"}

MODEL_DIR = os.environ.get("MODEL_CACHE_DIR", os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(__file__))),
    "models", "intent_classifier"
))


def load_model():
    """Load the trained intent classifier model from disk."""
    global _model, _vectorizer, _label_encoder, _model_version, _model_metadata, _service_state, _fallback_reason
    
    model_path = os.path.join(MODEL_DIR, "model.pkl")
    vectorizer_path = os.path.join(MODEL_DIR, "vectorizer.pkl")
    label_encoder_path = os.path.join(MODEL_DIR, "label_encoder.pkl")
    metadata_path = os.path.join(MODEL_DIR, "config.json")

    _model = None
    _vectorizer = None
    _label_encoder = None
    _model_metadata = {}
    _model_version = "not_loaded"
    _service_state = SERVICE_STATE_NOT_READY
    _fallback_reason = "model_loader_not_executed"

    if os.path.exists(metadata_path):
        try:
            with open(metadata_path, "r", encoding="utf-8") as f:
                _model_metadata = json.load(f)
        except Exception as e:
            logger.warning("Failed to load model metadata from %s: %s", metadata_path, e)
    
    if os.path.exists(model_path) and os.path.exists(vectorizer_path):
        try:
            _model = joblib.load(model_path)
            _vectorizer = joblib.load(vectorizer_path)
            _label_encoder = joblib.load(label_encoder_path)
            _model_version = _model_metadata.get("version", "v1.0-trained")

            loaded_labels = sorted(str(label) for label in getattr(_label_encoder, "classes_", []))
            expected_labels = sorted(INTENT_LABELS)
            if loaded_labels != expected_labels:
                _model = None
                _vectorizer = None
                _label_encoder = None
                _service_state = SERVICE_STATE_DEGRADED
                _fallback_reason = "label_set_mismatch"
                logger.error(
                    "Intent model label mismatch. expected=%s actual=%s. Falling back to rules.",
                    expected_labels, loaded_labels
                )
                return

            _service_state = SERVICE_STATE_READY
            _fallback_reason = None
            logger.info("Intent model loaded from %s with version=%s", MODEL_DIR, _model_version)
        except Exception as e:
            logger.warning("Failed to load trained model: %s. Using rule-based fallback.", e)
            _model = None
            _vectorizer = None
            _label_encoder = None
            _model_version = _model_metadata.get("version", "rule-based-fallback")
            _service_state = SERVICE_STATE_DEGRADED
            _fallback_reason = "model_load_failure"
    else:
        logger.info("No trained model found. Using rule-based classifier.")
        _model_version = _model_metadata.get("version", "rule-based-fallback")
        _service_state = SERVICE_STATE_MODEL_MISSING
        _fallback_reason = "trained_model_artifacts_missing"


def get_model_info() -> dict:
    """Return model metadata for health check."""
    return {
        "version": _model_version,
        "type": "scikit-learn" if _model else "rule-based",
        "labels": INTENT_LABELS,
        "model_dir": MODEL_DIR,
        "service_state": _service_state,
        "fallback_reason": _fallback_reason,
        "advisory_only": ADVISORY_ONLY,
        "metadata": _model_metadata,
    }


def get_service_status() -> dict:
    """Return runtime status used by health and readiness endpoints."""
    return {
        "service_state": _service_state,
        "ready": _service_state == SERVICE_STATE_READY,
        "classifier_mode": CLASSIFIER_MODE_ML if _service_state == SERVICE_STATE_READY else CLASSIFIER_MODE_RULE_FALLBACK,
        "model_version": _model_version,
        "fallback_reason": _fallback_reason,
        "advisory_only": ADVISORY_ONLY,
    }


def _build_response(
    intent: str,
    confidence: float,
    language: str,
    *,
    is_write_operation: bool = False,
    requires_confirmation: bool = False,
    sub_intent: Optional[str] = None,
    classifier_mode: str = CLASSIFIER_MODE_RULE_FALLBACK,
    fallback_reason: Optional[str] = None,
) -> ClassifyResponse:
    return ClassifyResponse(
        intent=intent,
        confidence=confidence,
        is_write_operation=is_write_operation,
        requires_confirmation=requires_confirmation,
        sub_intent=sub_intent,
        detected_language=language,
        classifier_mode=classifier_mode,
        service_state=_service_state,
        model_version=_model_version,
        advisory_only=ADVISORY_ONLY,
        fallback_reason=fallback_reason,
    )


# ============================================================
# Text Preprocessing
# ============================================================

def detect_language(text: str) -> str:
    """Simple language detection: Vietnamese vs English."""
    # Vietnamese-specific characters
    vi_chars = set("àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ")
    text_lower = text.lower()
    vi_count = sum(1 for c in text_lower if c in vi_chars)
    return "vi" if vi_count >= 2 else "en"


def preprocess_text(text: str, language: str) -> str:
    """Normalize and preprocess query text for classification."""
    # Normalize whitespace
    text = re.sub(r'\s+', ' ', text.strip())
    
    # Lowercase
    text = text.lower()
    
    # Remove excessive punctuation but keep meaningful ones
    text = re.sub(r'[!?]{2,}', '?', text)
    
    return text


# ============================================================
# Rule-Based Classifier (Fallback)
# ============================================================

def rule_based_classify(text: str, language: str) -> ClassifyResponse:
    """
    Lightweight rule-based classifier. 
    Used when ML model is not available or as safety override.
    Much simpler than the C# 1007-line version — covers only critical cases.
    """
    text_upper = text.upper().strip()
    text_lower = text.lower().strip()
    
    # ===== PRIORITY 1: DDL (Safety critical) =====
    for pattern in DDL_PATTERNS:
        if re.search(pattern, text_upper, re.IGNORECASE):
            return _build_response(
                intent="DDL",
                confidence=0.99,
                is_write_operation=True,
                requires_confirmation=True,
                language=language,
                classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
                fallback_reason=_fallback_reason or "rule_based_classifier",
            )
    
    # ===== PRIORITY 2: Dangerous DELETE =====
    for pattern in DELETE_ALL_PATTERNS:
        if re.search(pattern, text_upper, re.IGNORECASE):
            return _build_response(
                intent="WRITE_DELETE",
                confidence=0.98,
                is_write_operation=True,
                requires_confirmation=True,
                language=language,
                classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
                fallback_reason=_fallback_reason or "rule_based_classifier",
            )
    
    # ===== PRIORITY 3: Write operations =====
    # Vietnamese
    vi_insert = re.search(r"\b(thêm|thêm mới|chèn|tạo mới|insert|thêm vào)\b", text_lower)
    vi_update = re.search(r"\b(cập nhật|sửa|thay đổi|update|chỉnh sửa|đổi)\b", text_lower)
    vi_delete = re.search(r"\b(xóa|xoá|delete|remove|loại bỏ)\b", text_lower)
    
    # English
    en_insert = re.search(r"\b(insert|add|create|new)\b", text_lower)
    en_update = re.search(r"\b(update|modify|change|set|edit|alter)\b", text_lower)
    en_delete = re.search(r"\b(delete|remove|drop|erase)\b", text_lower)
    
    if vi_delete or en_delete:
        return _build_response(
            intent="WRITE_DELETE",
            confidence=0.85,
            is_write_operation=True,
            requires_confirmation=True,
            language=language,
            classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
            fallback_reason=_fallback_reason or "rule_based_classifier",
        )
    
    if vi_update or en_update:
        # Careful: "update" can also appear in SELECT queries as a table name
        # Only classify as WRITE_UPDATE if clearly imperative
        imperative_signals = re.search(r"(hãy|giúp|please|set\s+\w+\s*=)", text_lower)
        if imperative_signals or not re.search(r"\b(bao nhiêu|có|liệt kê|list|show|how many|what)\b", text_lower):
            return _build_response(
                intent="WRITE_UPDATE",
                confidence=0.80,
                is_write_operation=True,
                requires_confirmation=True,
                language=language,
                classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
                fallback_reason=_fallback_reason or "rule_based_classifier",
            )
    
    if vi_insert or en_insert:
        return _build_response(
            intent="WRITE_INSERT",
            confidence=0.82,
            is_write_operation=True,
            requires_confirmation=True,
            language=language,
            classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
            fallback_reason=_fallback_reason or "rule_based_classifier",
        )
    
    # ===== PRIORITY 4: Schema queries =====
    schema_keywords = r"\b(bảng nào|bảng gì|table|column|cột|schema|danh sách bảng|structure|cấu trúc|describe|mô tả)\b"
    if re.search(schema_keywords, text_lower):
        return _build_response(
            intent="SCHEMA_QUERY",
            confidence=0.80,
            is_write_operation=False,
            requires_confirmation=False,
            language=language,
            classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
            fallback_reason=_fallback_reason or "rule_based_classifier",
        )
    
    # ===== PRIORITY 5: Aggregate =====
    agg_keywords = r"\b(tổng|trung bình|đếm|count|sum|avg|average|max|min|total|bao nhiêu|how many|tổng cộng|thống kê|group by|theo tháng|theo năm|theo ngày)\b"
    if re.search(agg_keywords, text_lower):
        return _build_response(
            intent="AGGREGATE",
            confidence=0.80,
            is_write_operation=False,
            requires_confirmation=False,
            language=language,
            classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
            fallback_reason=_fallback_reason or "rule_based_classifier",
        )
    
    # ===== DEFAULT: SELECT =====
    return _build_response(
        intent="SELECT",
        confidence=0.60,
        is_write_operation=False,
        requires_confirmation=False,
        language=language,
        classifier_mode=CLASSIFIER_MODE_RULE_FALLBACK,
        fallback_reason=_fallback_reason or "rule_based_classifier",
    )


# ============================================================
# ML-Based Classifier
# ============================================================

def ml_classify(text: str, language: str) -> ClassifyResponse:
    """
    Classify intent using trained ML model (scikit-learn).
    Falls back to rule-based if model not loaded.
    """
    if _model is None or _vectorizer is None or _label_encoder is None:
        return rule_based_classify(text, language)
    
    try:
        processed = preprocess_text(text, language)
        features = _vectorizer.transform([processed])
        
        # Get prediction and confidence
        prediction = _model.predict(features)[0]
        probabilities = _model.predict_proba(features)[0]
        confidence = float(np.max(probabilities))
        
        # Decode label
        intent = _label_encoder.inverse_transform([prediction])[0]
        
        # If confidence too low, fallback to rule-based
        if confidence < 0.5:
            logger.info("ML confidence too low (%.2f), falling back to rules", confidence)
            return rule_based_classify(text, language)
        
        return _build_response(
            intent=intent,
            confidence=round(confidence, 4),
            is_write_operation=intent in WRITE_INTENTS,
            requires_confirmation=intent in CONFIRM_INTENTS,
            language=language,
            classifier_mode=CLASSIFIER_MODE_ML,
        )
    except Exception as e:
        logger.error("ML classification failed: %s, falling back to rules", e)
        return rule_based_classify(text, language)


# ============================================================
# API Endpoint
# ============================================================

@router.post("/classify", response_model=ClassifyResponse)
async def classify_intent(request: ClassifyRequest):
    """
    Classify the intent of a natural language query.
    
    Returns intent type, confidence, and safety flags.
    Maps 1:1 to C# IntentClassificationResult for seamless integration.
    """
    if not request.question.strip():
        raise HTTPException(status_code=400, detail="Question cannot be empty")
    
    # Detect language if auto
    language = request.language
    if language == "auto":
        language = detect_language(request.question)
    
    text = preprocess_text(request.question, language)
    
    # Safety check: DDL patterns ALWAYS override ML model
    text_upper = request.question.upper()
    for pattern in DDL_PATTERNS:
        if re.search(pattern, text_upper, re.IGNORECASE):
            logger.warning("DDL safety override triggered for: %s", request.question[:50])
            return _build_response(
                intent="DDL",
                confidence=0.99,
                is_write_operation=True,
                requires_confirmation=True,
                language=language,
                classifier_mode=CLASSIFIER_MODE_SAFETY_OVERRIDE,
                fallback_reason="ddl_safety_override",
            )
    
    # Use ML model if available, else rule-based
    result = ml_classify(text, language)
    
    logger.info(
        "Classified '%s' -> %s (conf=%.2f, lang=%s, mode=%s, state=%s)",
        request.question[:40] + ("..." if len(request.question) > 40 else ""),
        result.intent,
        result.confidence,
        result.detected_language,
        result.classifier_mode,
        result.service_state,
    )
    
    return result
