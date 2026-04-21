"""
Validate review-queue datasets for enterprise intent-classification workflow.

Usage:
    python training/validate_review_dataset.py
"""
from __future__ import annotations

import collections
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from training.ml_utils import (  # noqa: E402
    VALID_INTENTS,
    detect_internal_query_conflicts,
    detect_label_conflicts,
    load_jsonl_records,
    normalize_query_text,
    write_json,
)

TRAINING_DIR = Path(__file__).resolve().parent
REVIEW_DIR = TRAINING_DIR / "data" / "review"
CANDIDATES_DIR = REVIEW_DIR / "candidates"
GENERATED_DIR = REVIEW_DIR / "generated"
SUMMARY_PATH = GENERATED_DIR / "review_validation_summary.json"
CANONICAL_DATASET = TRAINING_DIR / "data" / "labeled_queries.jsonl"

VALID_REVIEW_STATUSES = {"pending_review", "approved", "rejected"}
VALID_LANGUAGES = {"vi", "en", "mixed", "auto"}
REQUIRED_FIELDS = {"query", "intent", "language", "review_status", "source", "batch_id", "risk_class"}


def validate_file(path: Path, canonical_records: list[dict]) -> dict:
    counts_by_intent = collections.Counter()
    counts_by_status = collections.Counter()
    duplicates = []
    errors = []
    seen = set()
    total = 0
    parsed_records: list[dict] = []

    with path.open("r", encoding="utf-8") as f:
        for line_number, raw_line in enumerate(f, start=1):
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue

            total += 1
            try:
                item = json.loads(line)
            except json.JSONDecodeError as ex:
                errors.Add if False else None
                errors.append(f"{path.name}:{line_number} invalid JSON: {ex}")
                continue

            missing = sorted(REQUIRED_FIELDS - set(item.keys()))
            if missing:
                errors.append(f"{path.name}:{line_number} missing fields: {', '.join(missing)}")
                continue

            query = str(item["query"]).strip()
            intent = str(item["intent"]).strip()
            language = str(item["language"]).strip()
            review_status = str(item["review_status"]).strip()

            if not query:
                errors.append(f"{path.name}:{line_number} query must not be empty")
            if intent not in VALID_INTENTS:
                errors.append(f"{path.name}:{line_number} invalid intent: {intent}")
            if language not in VALID_LANGUAGES:
                errors.append(f"{path.name}:{line_number} invalid language: {language}")
            if review_status not in VALID_REVIEW_STATUSES:
                errors.append(f"{path.name}:{line_number} invalid review_status: {review_status}")

            duplicate_key = (query.lower(), intent)
            if duplicate_key in seen:
                duplicates.append(f"{path.name}:{line_number} duplicate query+intent: {query} / {intent}")
            else:
                seen.add(duplicate_key)

            counts_by_intent[intent] += 1
            counts_by_status[review_status] += 1
            parsed_records.append({
                **item,
                "query": query,
                "intent": intent,
                "normalized_query": normalize_query_text(query),
            })

    canonical_conflicts = detect_label_conflicts(parsed_records, canonical_records)
    internal_conflicts = detect_internal_query_conflicts(parsed_records)

    return {
        "file": path.name,
        "total_records": total,
        "counts_by_intent": dict(sorted(counts_by_intent.items())),
        "counts_by_status": dict(sorted(counts_by_status.items())),
        "duplicates": duplicates,
        "canonical_conflicts": canonical_conflicts,
        "internal_conflicts": internal_conflicts,
        "errors": errors,
        "valid": not errors and not duplicates and not canonical_conflicts and not internal_conflicts,
    }


def main() -> None:
    if not CANDIDATES_DIR.exists():
        raise FileNotFoundError(f"Candidates directory not found: {CANDIDATES_DIR}")

    files = sorted(CANDIDATES_DIR.glob("*.jsonl"))
    if not files:
        raise FileNotFoundError(f"No review candidate files found under: {CANDIDATES_DIR}")

    canonical_records = load_jsonl_records(CANONICAL_DATASET)
    results = [validate_file(path, canonical_records) for path in files]
    overall_valid = all(result["valid"] for result in results)

    summary = {
        "review_dir": str(REVIEW_DIR),
        "files_checked": len(results),
        "overall_valid": overall_valid,
        "results": results,
    }

    write_json(SUMMARY_PATH, summary)

    print("Review dataset validation complete")
    print(f"- files checked: {len(results)}")
    print(f"- overall valid: {overall_valid}")
    print(f"- summary: {SUMMARY_PATH}")


if __name__ == "__main__":
    main()
