"""
Prepare a fixed benchmark split for enterprise evaluation.

Usage:
    python training/prepare_intent_benchmark.py
"""
from __future__ import annotations

import collections
import os
import random
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from training.ml_utils import (  # noqa: E402
    compute_dataset_fingerprint,
    load_jsonl_records,
    resolve_training_dataset_path,
    write_json,
    write_jsonl,
)

ROOT_DIR = os.path.dirname(os.path.dirname(__file__))
DATA_DIR = os.path.join(os.path.dirname(__file__), "data")
SOURCE_DATASET = os.path.join(DATA_DIR, "labeled_queries.jsonl")
PROMOTED_DATASET = os.path.join(DATA_DIR, "review", "generated", "labeled_queries.promoted.jsonl")
BENCHMARK_DIR = os.path.join(DATA_DIR, "benchmark")
TRAIN_SPLIT_PATH = os.path.join(BENCHMARK_DIR, "train.jsonl")
EVAL_SPLIT_PATH = os.path.join(BENCHMARK_DIR, "eval.jsonl")
MANIFEST_PATH = os.path.join(BENCHMARK_DIR, "manifest.json")

DEFAULT_SEED = 42
DEFAULT_EVAL_RATIO = 0.2


def stratified_split(
    records: list[dict],
    eval_ratio: float,
    seed: int,
) -> tuple[list[dict], list[dict]]:
    grouped: dict[str, list[dict]] = collections.defaultdict(list)
    for record in records:
        grouped[record["intent"]].append(record)

    rng = random.Random(seed)
    train_records: list[dict] = []
    eval_records: list[dict] = []

    for intent, items in grouped.items():
        bucket = items[:]
        rng.shuffle(bucket)

        eval_count = max(1, round(len(bucket) * eval_ratio))
        if eval_count >= len(bucket):
            eval_count = len(bucket) - 1

        eval_records.extend(bucket[:eval_count])
        train_records.extend(bucket[eval_count:])

    return train_records, eval_records


def build_manifest(
    source_dataset: Path,
    records: list[dict],
    train_records: list[dict],
    eval_records: list[dict],
) -> dict:
    train_distribution = collections.Counter(item["intent"] for item in train_records)
    eval_distribution = collections.Counter(item["intent"] for item in eval_records)

    return {
        "benchmark_name": "intent-classification-enterprise-baseline",
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_dataset": os.path.relpath(source_dataset, ROOT_DIR).replace("\\", "/"),
        "source_dataset_fingerprint": compute_dataset_fingerprint(records),
        "source_dataset_samples": len(records),
        "random_seed": DEFAULT_SEED,
        "eval_ratio": DEFAULT_EVAL_RATIO,
        "train_samples": len(train_records),
        "eval_samples": len(eval_records),
        "train_distribution": dict(sorted(train_distribution.items())),
        "eval_distribution": dict(sorted(eval_distribution.items())),
    }


def write_benchmark_artifacts(source_dataset: Path, records: list[dict]) -> dict:
    train_records, eval_records = stratified_split(records, DEFAULT_EVAL_RATIO, DEFAULT_SEED)

    train_records = sorted(train_records, key=lambda item: (item["intent"], item["query"]))
    eval_records = sorted(eval_records, key=lambda item: (item["intent"], item["query"]))

    write_jsonl(TRAIN_SPLIT_PATH, train_records)
    write_jsonl(EVAL_SPLIT_PATH, eval_records)

    manifest = build_manifest(source_dataset, records, train_records, eval_records)
    write_json(MANIFEST_PATH, manifest)
    return manifest


def main() -> None:
    dataset_path = resolve_training_dataset_path(SOURCE_DATASET, PROMOTED_DATASET)
    if not dataset_path.exists():
        raise FileNotFoundError(f"Training dataset not found: {dataset_path}")

    records = load_jsonl_records(dataset_path)
    write_benchmark_artifacts(dataset_path, records)

    print("Prepared benchmark split")
    print(f"- source dataset: {dataset_path}")
    print(f"- train: {TRAIN_SPLIT_PATH}")
    print(f"- eval:  {EVAL_SPLIT_PATH}")
    print(f"- manifest: {MANIFEST_PATH}")


if __name__ == "__main__":
    main()
