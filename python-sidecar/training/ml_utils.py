"""
Shared utilities for intent-classification training and evaluation.
"""
from __future__ import annotations

import collections
import hashlib
import json
import os
from pathlib import Path
from typing import Any

VALID_INTENTS = [
    "SELECT",
    "AGGREGATE",
    "SCHEMA_QUERY",
    "WRITE_INSERT",
    "WRITE_UPDATE",
    "WRITE_DELETE",
    "DDL",
    "AMBIGUOUS",
]


def normalize_query_text(query: str) -> str:
    return " ".join(str(query).split()).strip().lower()


def normalize_optional_text(value: Any) -> str | None:
    if value is None:
        return None

    normalized = normalize_query_text(str(value))
    return normalized or None


def normalize_optional_int(value: Any) -> int | None:
    if value is None or value == "":
        return None

    normalized = int(value)
    if normalized < 1:
        raise ValueError(f"context_turn must be >= 1, received {normalized}")
    return normalized


def normalize_optional_intent(value: Any) -> str | None:
    if value is None or value == "":
        return None

    intent = str(value).strip().upper()
    if intent not in VALID_INTENTS:
        raise ValueError(f"Unsupported previous_intent '{intent}'")
    return intent


def build_feature_text(
    query: str,
    *,
    conversation_context: str | None = None,
    database_context: str | None = None,
    previous_intent: str | None = None,
    context_turn: int | None = None,
) -> str:
    parts = [f"[query] {normalize_query_text(query)}"]

    if conversation_context:
        parts.append(f"[conversation] {normalize_query_text(conversation_context)}")
    if database_context:
        parts.append(f"[database] {normalize_query_text(database_context)}")
    if previous_intent:
        parts.append(f"[previous_intent] {str(previous_intent).strip().upper()}")
    if context_turn:
        parts.append(f"[turn] {context_turn}")

    return "\n".join(parts)


def build_record_signature(record: dict[str, Any]) -> tuple[str, str, str, str, str]:
    normalized_query = record.get("normalized_query") or normalize_query_text(record.get("query", ""))
    normalized_conversation_context = record.get("normalized_conversation_context") or ""
    normalized_database_context = record.get("normalized_database_context") or ""
    previous_intent = record.get("previous_intent") or ""
    context_turn = str(record.get("context_turn") or "")

    return (
        normalized_query,
        normalized_conversation_context,
        normalized_database_context,
        str(previous_intent).strip().upper(),
        context_turn,
    )


def load_jsonl_records(filepath: str | Path) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    path = Path(filepath)

    with path.open("r", encoding="utf-8") as f:
        for line_number, raw_line in enumerate(f, start=1):
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue

            item = json.loads(line)
            query = str(item["query"]).strip()
            intent = str(item["intent"]).strip()
            conversation_context = normalize_optional_text(item.get("conversation_context"))
            database_context = normalize_optional_text(item.get("database_context"))
            previous_intent = normalize_optional_intent(item.get("previous_intent"))
            context_turn = normalize_optional_int(item.get("context_turn"))

            if not query:
                raise ValueError(f"Empty query at line {line_number} in {path}")
            if intent not in VALID_INTENTS:
                raise ValueError(f"Unsupported intent '{intent}' at line {line_number} in {path}")

            normalized = {
                **item,
                "query": query,
                "intent": intent,
                "normalized_query": normalize_query_text(query),
                "conversation_context": str(item["conversation_context"]).strip() if item.get("conversation_context") is not None else None,
                "database_context": str(item["database_context"]).strip() if item.get("database_context") is not None else None,
                "previous_intent": previous_intent,
                "context_turn": context_turn,
                "normalized_conversation_context": conversation_context,
                "normalized_database_context": database_context,
            }
            records.append(normalized)

    return records


def extract_texts_and_labels(records: list[dict[str, Any]]) -> tuple[list[str], list[str]]:
    texts = [
        build_feature_text(
            record["query"],
            conversation_context=record.get("conversation_context"),
            database_context=record.get("database_context"),
            previous_intent=record.get("previous_intent"),
            context_turn=record.get("context_turn"),
        )
        for record in records
    ]
    labels = [record["intent"] for record in records]
    return texts, labels


def compute_dataset_fingerprint(records: list[dict[str, Any]]) -> str:
    normalized_lines = sorted(
        "\t".join(build_record_signature(record)) + f"\t{record['intent']}"
        for record in records
    )
    payload = "\n".join(normalized_lines).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def resolve_training_dataset_path(
    default_dataset: str | Path,
    promoted_dataset: str | Path | None = None,
    env_var: str = "INTENT_TRAINING_DATASET",
) -> Path:
    env_override = Path(env_override_raw) if (env_override_raw := os.environ.get(env_var)) else None
    candidates = [env_override, Path(promoted_dataset) if promoted_dataset else None, Path(default_dataset)]
    for candidate in candidates:
        if candidate and candidate.exists():
            return candidate
    return Path(default_dataset)


def detect_label_conflicts(
    candidate_records: list[dict[str, Any]],
    reference_records: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    reference_by_query: dict[tuple[str, str, str, str, str], set[str]] = collections.defaultdict(set)
    for record in reference_records:
        reference_by_query[build_record_signature(record)].add(record["intent"])

    conflicts: list[dict[str, Any]] = []
    for record in candidate_records:
        record_signature = build_record_signature(record)
        intent = str(record["intent"]).strip()
        existing_intents = sorted(reference_by_query.get(record_signature, set()))
        if existing_intents and intent not in existing_intents:
            conflicts.append({
                "query": str(record["query"]).strip(),
                "candidate_intent": intent,
                "existing_intents": existing_intents,
                "conversation_context": record.get("conversation_context"),
                "database_context": record.get("database_context"),
                "previous_intent": record.get("previous_intent"),
                "context_turn": record.get("context_turn"),
            })

    return conflicts


def detect_internal_query_conflicts(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    query_to_intents: dict[tuple[str, str, str, str, str], set[str]] = collections.defaultdict(set)
    query_to_examples: dict[tuple[str, str, str, str, str], dict[str, Any]] = {}

    for record in records:
        signature = build_record_signature(record)
        query_to_intents[signature].add(str(record["intent"]).strip())
        query_to_examples.setdefault(signature, {
            "query": str(record["query"]).strip(),
            "conversation_context": record.get("conversation_context"),
            "database_context": record.get("database_context"),
            "previous_intent": record.get("previous_intent"),
            "context_turn": record.get("context_turn"),
        })

    conflicts = []
    for signature, intents in sorted(query_to_intents.items()):
        if len(intents) <= 1:
            continue
        example = query_to_examples[signature]
        conflicts.append({
            "query": example["query"],
            "normalized_query": signature[0],
            "intents": sorted(intents),
            "conversation_context": example["conversation_context"],
            "database_context": example["database_context"],
            "previous_intent": example["previous_intent"],
            "context_turn": example["context_turn"],
        })

    return conflicts


def build_vectorizer() -> TfidfVectorizer:
    from sklearn.feature_extraction.text import TfidfVectorizer

    return TfidfVectorizer(
        analyzer="char_wb",
        ngram_range=(2, 5),
        max_features=15000,
        sublinear_tf=True,
        strip_accents=None,
    )


def build_model() -> LogisticRegression:
    from sklearn.linear_model import LogisticRegression

    return LogisticRegression(
        C=5.0,
        max_iter=1000,
        class_weight="balanced",
        solver="lbfgs",
        multi_class="multinomial",
        random_state=42,
    )


def evaluate_predictions(
    y_true: list[str],
    y_pred: list[str],
    labels: list[str],
) -> dict[str, Any]:
    from sklearn.metrics import accuracy_score, classification_report, confusion_matrix

    report = classification_report(
        y_true,
        y_pred,
        labels=labels,
        target_names=labels,
        digits=3,
        output_dict=True,
        zero_division=0,
    )

    matrix = confusion_matrix(y_true, y_pred, labels=labels)

    per_class = {
        label: {
            "precision": round(float(report[label]["precision"]), 4),
            "recall": round(float(report[label]["recall"]), 4),
            "f1_score": round(float(report[label]["f1-score"]), 4),
            "support": int(report[label]["support"]),
        }
        for label in labels
    }

    return {
        "accuracy": round(float(accuracy_score(y_true, y_pred)), 4),
        "macro_avg": {
            "precision": round(float(report["macro avg"]["precision"]), 4),
            "recall": round(float(report["macro avg"]["recall"]), 4),
            "f1_score": round(float(report["macro avg"]["f1-score"]), 4),
        },
        "weighted_avg": {
            "precision": round(float(report["weighted avg"]["precision"]), 4),
            "recall": round(float(report["weighted avg"]["recall"]), 4),
            "f1_score": round(float(report["weighted avg"]["f1-score"]), 4),
        },
        "per_class": per_class,
        "confusion_matrix": {
            "labels": labels,
            "values": matrix.tolist(),
        },
    }


def write_json(path: str | Path, payload: dict[str, Any]) -> None:
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with Path(path).open("w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2, ensure_ascii=False)


def write_jsonl(path: str | Path, records: list[dict[str, Any]]) -> None:
    output_path = Path(path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as f:
        for record in records:
            serializable = {
                k: v
                for k, v in record.items()
                if not k.startswith("normalized_") and not k.startswith("_")
            }
            f.write(json.dumps(serializable, ensure_ascii=False) + "\n")
