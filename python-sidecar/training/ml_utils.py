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

            if not query:
                raise ValueError(f"Empty query at line {line_number} in {path}")
            if intent not in VALID_INTENTS:
                raise ValueError(f"Unsupported intent '{intent}' at line {line_number} in {path}")

            normalized = {
                **item,
                "query": query,
                "intent": intent,
                "normalized_query": normalize_query_text(query),
            }
            records.append(normalized)

    return records


def extract_texts_and_labels(records: list[dict[str, Any]]) -> tuple[list[str], list[str]]:
    texts = [record["normalized_query"] for record in records]
    labels = [record["intent"] for record in records]
    return texts, labels


def compute_dataset_fingerprint(records: list[dict[str, Any]]) -> str:
    normalized_lines = sorted(
        f"{record['normalized_query']}\t{record['intent']}"
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
    reference_by_query: dict[str, set[str]] = collections.defaultdict(set)
    for record in reference_records:
        reference_by_query[record["normalized_query"]].add(record["intent"])

    conflicts: list[dict[str, Any]] = []
    for record in candidate_records:
        normalized_query = record.get("normalized_query") or normalize_query_text(record.get("query", ""))
        intent = str(record["intent"]).strip()
        existing_intents = sorted(reference_by_query.get(normalized_query, set()))
        if existing_intents and intent not in existing_intents:
            conflicts.append({
                "query": str(record["query"]).strip(),
                "candidate_intent": intent,
                "existing_intents": existing_intents,
            })

    return conflicts


def detect_internal_query_conflicts(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    query_to_intents: dict[str, set[str]] = collections.defaultdict(set)
    query_to_examples: dict[str, str] = {}

    for record in records:
        normalized_query = record.get("normalized_query") or normalize_query_text(record.get("query", ""))
        query_to_intents[normalized_query].add(str(record["intent"]).strip())
        query_to_examples.setdefault(normalized_query, str(record["query"]).strip())

    conflicts = []
    for normalized_query, intents in sorted(query_to_intents.items()):
        if len(intents) <= 1:
            continue
        conflicts.append({
            "query": query_to_examples[normalized_query],
            "normalized_query": normalized_query,
            "intents": sorted(intents),
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
            serializable = {k: v for k, v in record.items() if k != "normalized_query"}
            f.write(json.dumps(serializable, ensure_ascii=False) + "\n")
