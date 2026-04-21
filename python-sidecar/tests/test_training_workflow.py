import sys
import unittest
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from training.ml_utils import (
    detect_internal_query_conflicts,
    detect_label_conflicts,
    normalize_query_text,
)
from training.validate_review_dataset import validate_file


class TrainingWorkflowTests(unittest.TestCase):
    @staticmethod
    def _workspace_temp_dir() -> Path:
        temp_root = Path(__file__).resolve().parents[2] / ".tmp-tests"
        temp_root.mkdir(parents=True, exist_ok=True)
        return temp_root

    def test_normalize_query_text_collapses_whitespace_and_case(self):
        normalized = normalize_query_text("  Doanh   Thu   Theo   Thang  ")

        self.assertEqual(normalized, "doanh thu theo thang")

    def test_detect_label_conflicts_flags_existing_query_with_new_intent(self):
        canonical_records = [
            {
                "query": "doanh thu theo thang",
                "intent": "AGGREGATE",
                "normalized_query": "doanh thu theo thang",
            }
        ]
        candidate_records = [
            {
                "query": "doanh thu theo thang",
                "intent": "AMBIGUOUS",
                "normalized_query": "doanh thu theo thang",
            }
        ]

        conflicts = detect_label_conflicts(candidate_records, canonical_records)

        self.assertEqual(len(conflicts), 1)
        self.assertEqual(conflicts[0]["existing_intents"], ["AGGREGATE"])

    def test_detect_internal_query_conflicts_flags_multiple_labels_for_same_query(self):
        conflicts = detect_internal_query_conflicts([
            {"query": "top customers", "intent": "AGGREGATE", "normalized_query": "top customers"},
            {"query": "top customers", "intent": "AMBIGUOUS", "normalized_query": "top customers"},
        ])

        self.assertEqual(len(conflicts), 1)
        self.assertEqual(conflicts[0]["intents"], ["AGGREGATE", "AMBIGUOUS"])

    def test_validate_file_marks_canonical_conflict_as_invalid(self):
        canonical_records = [
            {
                "query": "doanh thu theo thang",
                "intent": "AGGREGATE",
                "normalized_query": "doanh thu theo thang",
            }
        ]

        temp_dir = self._workspace_temp_dir() / f"validate-file-{uuid.uuid4().hex}"
        temp_dir.mkdir(parents=True, exist_ok=True)
        candidate_path = temp_dir / "candidate.jsonl"
        try:
            candidate_path.write_text(
                (
                    '{"query":"doanh thu theo thang","intent":"AMBIGUOUS","language":"vi",'
                    '"review_status":"approved","source":"seed","batch_id":"b1","risk_class":"medium"}\n'
                ),
                encoding="utf-8",
            )

            result = validate_file(candidate_path, canonical_records)
        finally:
            if candidate_path.exists():
                candidate_path.unlink()
            temp_dir.rmdir()

        self.assertFalse(result["valid"])
        self.assertEqual(len(result["canonical_conflicts"]), 1)


if __name__ == "__main__":
    unittest.main()
