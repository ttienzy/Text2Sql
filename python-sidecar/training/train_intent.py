"""
Intent Classifier Training Script

Enterprise-oriented training entrypoint:
- prefers a fixed benchmark split when present
- saves richer evaluation metadata
- keeps output artifacts compatible with the running sidecar

Usage:
    python training/train_intent.py
"""
from __future__ import annotations

import collections
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

import joblib
from sklearn.model_selection import cross_val_score
from sklearn.preprocessing import LabelEncoder

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from training.prepare_intent_benchmark import write_benchmark_artifacts  # noqa: E402
from training.ml_utils import (  # noqa: E402
    VALID_INTENTS,
    build_model,
    build_vectorizer,
    compute_dataset_fingerprint,
    evaluate_predictions,
    extract_texts_and_labels,
    load_jsonl_records,
    resolve_training_dataset_path,
    write_json,
)

ROOT_DIR = os.path.dirname(os.path.dirname(__file__))
MODEL_DIR = os.path.join(ROOT_DIR, "models", "intent_classifier")
TRAINING_DIR = os.path.dirname(__file__)
DATA_DIR = os.path.join(TRAINING_DIR, "data")
BENCHMARK_DIR = os.path.join(DATA_DIR, "benchmark")
DEFAULT_DATASET = os.path.join(DATA_DIR, "labeled_queries.jsonl")
PROMOTED_DATASET = os.path.join(DATA_DIR, "review", "generated", "labeled_queries.promoted.jsonl")
BENCHMARK_TRAIN = os.path.join(BENCHMARK_DIR, "train.jsonl")
BENCHMARK_EVAL = os.path.join(BENCHMARK_DIR, "eval.jsonl")
BENCHMARK_MANIFEST = os.path.join(BENCHMARK_DIR, "manifest.json")
EVALUATION_ARTIFACT = os.path.join(MODEL_DIR, "evaluation.json")


def load_json(path: str) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def benchmark_matches_dataset(manifest: dict, dataset_path: Path, records: list[dict]) -> bool:
    return (
        manifest.get("source_dataset") == os.path.relpath(dataset_path, ROOT_DIR).replace("\\", "/")
        and manifest.get("source_dataset_fingerprint") == compute_dataset_fingerprint(records)
    )


def main() -> None:
    print("=" * 60)
    print("Intent Classifier Training")
    print("=" * 60)

    dataset_path = resolve_training_dataset_path(DEFAULT_DATASET, PROMOTED_DATASET)
    if not dataset_path.exists():
        raise FileNotFoundError(f"Training data not found: {dataset_path}")

    records = load_jsonl_records(dataset_path)
    benchmark_manifest = load_json(BENCHMARK_MANIFEST) if os.path.exists(BENCHMARK_MANIFEST) else {}

    benchmark_is_current = (
        os.path.exists(BENCHMARK_TRAIN)
        and os.path.exists(BENCHMARK_EVAL)
        and benchmark_matches_dataset(benchmark_manifest, dataset_path, records)
    )

    if not benchmark_is_current:
        benchmark_manifest = write_benchmark_artifacts(dataset_path, records)
        print(f"\nRefreshed benchmark split from: {dataset_path}")
    else:
        print(f"\nUsing fixed benchmark split from: {BENCHMARK_DIR}")

    train_records = load_jsonl_records(BENCHMARK_TRAIN)
    eval_records = load_jsonl_records(BENCHMARK_EVAL)
    dataset_mode = "fixed-benchmark"

    train_texts, train_labels = extract_texts_and_labels(train_records)
    eval_texts, eval_labels = extract_texts_and_labels(eval_records)

    print(f"\nTraining samples: {len(train_texts)}")
    print(f"Evaluation samples: {len(eval_texts)}")

    label_distribution = collections.Counter(train_labels)
    print("\nTraining label distribution:")
    for label, count in sorted(label_distribution.items()):
        print(f"  {label}: {count} ({count / len(train_labels) * 100:.1f}%)")

    label_encoder = LabelEncoder()
    y_train = label_encoder.fit_transform(train_labels)

    vectorizer = build_vectorizer()
    X_train = vectorizer.fit_transform(train_texts)
    print(f"\nFeature matrix: {X_train.shape}")

    model = build_model()
    model.fit(X_train, y_train)

    eval_features = vectorizer.transform(eval_texts)
    y_pred_encoded = model.predict(eval_features)
    y_pred = label_encoder.inverse_transform(y_pred_encoded)

    metrics = evaluate_predictions(eval_labels, list(y_pred), VALID_INTENTS)
    cv_scores = cross_val_score(model, X_train, y_train, cv=5, scoring="accuracy")

    print(f"\nEvaluation accuracy: {metrics['accuracy']:.4f}")
    print(f"Cross-validation accuracy: {cv_scores.mean():.4f} (+/-{cv_scores.std():.4f})")

    os.makedirs(MODEL_DIR, exist_ok=True)
    joblib.dump(model, os.path.join(MODEL_DIR, "model.pkl"))
    joblib.dump(vectorizer, os.path.join(MODEL_DIR, "vectorizer.pkl"))
    joblib.dump(label_encoder, os.path.join(MODEL_DIR, "label_encoder.pkl"))

    metadata = {
        "version": "v1.2-enterprise-hardened",
        "trained_at_utc": datetime.now(timezone.utc).isoformat(),
        "dataset_mode": dataset_mode,
        "source_dataset": os.path.relpath(dataset_path, ROOT_DIR).replace("\\", "/"),
        "source_dataset_fingerprint": compute_dataset_fingerprint(records),
        "benchmark_manifest": benchmark_manifest,
        "accuracy": metrics["accuracy"],
        "macro_f1": metrics["macro_avg"]["f1_score"],
        "cv_accuracy": float(round(cv_scores.mean(), 4)),
        "cv_std": float(round(cv_scores.std(), 4)),
        "num_samples": len(train_texts),
        "num_eval_samples": len(eval_texts),
        "num_features": int(X_train.shape[1]),
        "labels": list(label_encoder.classes_),
        "label_distribution": {k: v for k, v in sorted(label_distribution.items())},
    }

    write_json(os.path.join(MODEL_DIR, "config.json"), metadata)
    write_json(
        EVALUATION_ARTIFACT,
        {
            "generated_at_utc": datetime.now(timezone.utc).isoformat(),
            "dataset_mode": dataset_mode,
            "benchmark_manifest": benchmark_manifest,
            "metrics": metrics,
            "cross_validation": {
                "accuracy_mean": float(round(cv_scores.mean(), 4)),
                "accuracy_std": float(round(cv_scores.std(), 4)),
            },
        },
    )

    print(f"\nSaved model artifacts to {MODEL_DIR}")
    print(f"  - source dataset: {dataset_path}")
    print(f"  - model.pkl ({os.path.getsize(os.path.join(MODEL_DIR, 'model.pkl')) / 1024:.1f} KB)")
    print(f"  - vectorizer.pkl ({os.path.getsize(os.path.join(MODEL_DIR, 'vectorizer.pkl')) / 1024:.1f} KB)")
    print("  - label_encoder.pkl")
    print("  - config.json")
    print("  - evaluation.json")


if __name__ == "__main__":
    main()
