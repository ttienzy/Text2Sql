"""
Evaluate the current intent-classification model against a fixed benchmark.

Usage:
    python training/evaluate_intent.py
"""
from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timezone

import joblib

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from training.ml_utils import (  # noqa: E402
    VALID_INTENTS,
    compute_dataset_fingerprint,
    evaluate_predictions,
    extract_texts_and_labels,
    load_jsonl_records,
    resolve_training_dataset_path,
    write_json,
)

ROOT_DIR = os.path.dirname(os.path.dirname(__file__))
TRAINING_DIR = os.path.dirname(__file__)
DATA_DIR = os.path.join(TRAINING_DIR, "data")
BENCHMARK_DIR = os.path.join(DATA_DIR, "benchmark")
MODEL_DIR = os.path.join(ROOT_DIR, "models", "intent_classifier")
REPORT_DIR = os.path.join(TRAINING_DIR, "reports")
GATES_PATH = os.path.join(TRAINING_DIR, "intent_release_gates.json")
REPORT_PATH = os.path.join(REPORT_DIR, "intent_eval_report.json")
DEFAULT_DATASET = os.path.join(DATA_DIR, "labeled_queries.jsonl")
PROMOTED_DATASET = os.path.join(DATA_DIR, "review", "generated", "labeled_queries.promoted.jsonl")
EVAL_DATA_PATH = os.path.join(BENCHMARK_DIR, "eval.jsonl")
MANIFEST_PATH = os.path.join(BENCHMARK_DIR, "manifest.json")


def load_json(path: str) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def compare_against_gates(metrics: dict, gates: dict) -> dict:
    checks: list[dict] = []

    accuracy = metrics["accuracy"]
    macro_f1 = metrics["macro_avg"]["f1_score"]

    checks.append({
        "name": "minimum_overall_accuracy",
        "passed": accuracy >= gates["minimum_overall_accuracy"],
        "actual": accuracy,
        "required": gates["minimum_overall_accuracy"],
    })
    checks.append({
        "name": "minimum_macro_f1",
        "passed": macro_f1 >= gates["minimum_macro_f1"],
        "actual": macro_f1,
        "required": gates["minimum_macro_f1"],
    })

    for intent_name, thresholds in gates.get("dangerous_intents", {}).items():
        intent_metrics = metrics["per_class"].get(intent_name, {})
        precision = intent_metrics.get("precision", 0.0)
        recall = intent_metrics.get("recall", 0.0)

        checks.append({
            "name": f"{intent_name}.precision",
            "passed": precision >= thresholds["min_precision"],
            "actual": precision,
            "required": thresholds["min_precision"],
        })
        checks.append({
            "name": f"{intent_name}.recall",
            "passed": recall >= thresholds["min_recall"],
            "actual": recall,
            "required": thresholds["min_recall"],
        })

    return {
        "passed": all(check["passed"] for check in checks),
        "checks": checks,
    }


def main() -> None:
    required_paths = [
        os.path.join(MODEL_DIR, "model.pkl"),
        os.path.join(MODEL_DIR, "vectorizer.pkl"),
        os.path.join(MODEL_DIR, "label_encoder.pkl"),
        EVAL_DATA_PATH,
        MANIFEST_PATH,
        GATES_PATH,
    ]

    missing = [path for path in required_paths if not os.path.exists(path)]
    if missing:
        missing_list = "\n".join(f"- {path}" for path in missing)
        raise FileNotFoundError(f"Missing required evaluation inputs:\n{missing_list}")

    dataset_path = resolve_training_dataset_path(DEFAULT_DATASET, PROMOTED_DATASET)
    dataset_records = load_jsonl_records(dataset_path)
    records = load_jsonl_records(EVAL_DATA_PATH)
    texts, y_true = extract_texts_and_labels(records)

    vectorizer = joblib.load(os.path.join(MODEL_DIR, "vectorizer.pkl"))
    model = joblib.load(os.path.join(MODEL_DIR, "model.pkl"))
    label_encoder = joblib.load(os.path.join(MODEL_DIR, "label_encoder.pkl"))

    features = vectorizer.transform(texts)
    y_pred_encoded = model.predict(features)
    y_pred = label_encoder.inverse_transform(y_pred_encoded)

    metrics = evaluate_predictions(y_true, list(y_pred), VALID_INTENTS)
    gates = load_json(GATES_PATH)
    benchmark_manifest = load_json(MANIFEST_PATH) if os.path.exists(MANIFEST_PATH) else {}

    expected_source_dataset = os.path.relpath(dataset_path, ROOT_DIR).replace("\\", "/")
    expected_fingerprint = compute_dataset_fingerprint(dataset_records)
    if benchmark_manifest.get("source_dataset") != expected_source_dataset:
        raise ValueError(
            "Benchmark manifest is stale for evaluation. "
            f"expected source_dataset={expected_source_dataset}, "
            f"actual={benchmark_manifest.get('source_dataset')}"
        )
    if benchmark_manifest.get("source_dataset_fingerprint") != expected_fingerprint:
        raise ValueError(
            "Benchmark manifest fingerprint does not match the current training dataset. "
            "Rebuild the benchmark split before evaluating."
        )

    gate_results = compare_against_gates(metrics, gates)

    report = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_dataset": expected_source_dataset,
        "benchmark_manifest": benchmark_manifest,
        "model_version": load_json(os.path.join(MODEL_DIR, "config.json")).get("version", "unknown")
            if os.path.exists(os.path.join(MODEL_DIR, "config.json")) else "unknown",
        "evaluation_dataset": os.path.relpath(EVAL_DATA_PATH, ROOT_DIR).replace("\\", "/"),
        "metrics": metrics,
        "release_gates": gate_results,
    }

    write_json(REPORT_PATH, report)

    print("Intent evaluation complete")
    print(f"- accuracy: {metrics['accuracy']:.4f}")
    print(f"- macro_f1: {metrics['macro_avg']['f1_score']:.4f}")
    print(f"- release gates passed: {gate_results['passed']}")
    print(f"- report: {REPORT_PATH}")


if __name__ == "__main__":
    main()
