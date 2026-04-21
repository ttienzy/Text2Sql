"""
Promote approved review samples into a generated candidate training dataset.

Usage:
    python training/promote_reviewed_samples.py
"""
from __future__ import annotations

import collections
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from training.ml_utils import (  # noqa: E402
    detect_internal_query_conflicts,
    detect_label_conflicts,
    load_jsonl_records,
    normalize_query_text,
    write_json,
    write_jsonl,
)

TRAINING_DIR = Path(__file__).resolve().parent
DATA_DIR = TRAINING_DIR / "data"
REVIEW_DIR = DATA_DIR / "review"
CANDIDATES_DIR = REVIEW_DIR / "candidates"
GENERATED_DIR = REVIEW_DIR / "generated"
CANONICAL_DATASET = DATA_DIR / "labeled_queries.jsonl"
PROMOTED_DATASET = GENERATED_DIR / "labeled_queries.promoted.jsonl"
PROMOTION_REPORT = GENERATED_DIR / "promotion_report.json"


def load_candidate_records() -> list[dict]:
    records: list[dict] = []
    for path in sorted(CANDIDATES_DIR.glob("*.jsonl")):
        with path.open("r", encoding="utf-8") as f:
            for raw_line in f:
                line = raw_line.strip()
                if not line or line.startswith("#"):
                    continue
                item = json.loads(line)
                item["_source_file"] = path.name
                records.append(item)
    return records


def main() -> None:
    if not CANONICAL_DATASET.exists():
        raise FileNotFoundError(f"Canonical dataset not found: {CANONICAL_DATASET}")

    canonical_records = load_jsonl_records(CANONICAL_DATASET)
    canonical_keys = {(record["normalized_query"], record["intent"]) for record in canonical_records}
    candidate_records = load_candidate_records()

    approved = [record for record in candidate_records if record.get("review_status") == "approved"]
    approved_conflicts = detect_label_conflicts(approved, canonical_records)
    internal_conflicts = detect_internal_query_conflicts(approved)
    promoted: list[dict] = []
    skipped_duplicates: list[dict] = []

    if approved_conflicts or internal_conflicts:
        write_json(
            PROMOTION_REPORT,
            {
                "canonical_dataset": str(CANONICAL_DATASET),
                "generated_dataset": str(PROMOTED_DATASET),
                "candidate_files": [path.name for path in sorted(CANDIDATES_DIR.glob("*.jsonl"))],
                "approved_records": len(approved),
                "promoted_records": 0,
                "skipped_duplicates": 0,
                "promotion_blocked": True,
                "canonical_conflicts": approved_conflicts,
                "internal_conflicts": internal_conflicts,
            },
        )
        raise ValueError(
            "Approved review samples contain label conflicts. "
            "Resolve canonical or intra-batch conflicts before promotion."
        )

    for record in approved:
        key = (normalize_query_text(record["query"]), str(record["intent"]).strip())
        normalized_record = {
            "query": str(record["query"]).strip(),
            "intent": str(record["intent"]).strip(),
        }

        if key in canonical_keys:
            skipped_duplicates.append({
                "query": normalized_record["query"],
                "intent": normalized_record["intent"],
                "source_file": record.get("_source_file"),
            })
            continue

        promoted.append(normalized_record)
        canonical_keys.add(key)

    merged_records = canonical_records + promoted
    merged_records = sorted(merged_records, key=lambda item: (item["intent"], item["query"].lower()))
    write_jsonl(PROMOTED_DATASET, merged_records)

    promoted_distribution = collections.Counter(item["intent"] for item in promoted)
    write_json(
        PROMOTION_REPORT,
        {
            "canonical_dataset": str(CANONICAL_DATASET),
            "generated_dataset": str(PROMOTED_DATASET),
            "candidate_files": [path.name for path in sorted(CANDIDATES_DIR.glob("*.jsonl"))],
            "approved_records": len(approved),
            "promoted_records": len(promoted),
            "skipped_duplicates": len(skipped_duplicates),
            "promotion_blocked": False,
            "promoted_distribution": dict(sorted(promoted_distribution.items())),
            "duplicate_examples": skipped_duplicates[:20],
        },
    )

    print("Promotion complete")
    print(f"- approved records: {len(approved)}")
    print(f"- promoted records: {len(promoted)}")
    print(f"- skipped duplicates: {len(skipped_duplicates)}")
    print(f"- generated dataset: {PROMOTED_DATASET}")
    print(f"- promotion report: {PROMOTION_REPORT}")


if __name__ == "__main__":
    main()
