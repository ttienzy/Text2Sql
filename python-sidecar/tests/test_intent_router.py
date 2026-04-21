import json
import os
import sys
import types
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

try:
    from app.routers import intent
    IMPORT_ERROR = None
except ModuleNotFoundError as ex:
    if ex.name in {"joblib", "numpy"}:
        sys.modules["joblib"] = types.SimpleNamespace(load=lambda *args, **kwargs: None)
        sys.modules["numpy"] = types.SimpleNamespace(max=max)
        try:
            from app.routers import intent
            IMPORT_ERROR = None
        except ModuleNotFoundError as inner_ex:
            intent = None
            IMPORT_ERROR = inner_ex
    else:
        intent = None
        IMPORT_ERROR = ex


@unittest.skipIf(intent is None, f"sidecar runtime dependencies unavailable: {IMPORT_ERROR}")
class IntentRouterTests(unittest.TestCase):
    @staticmethod
    def _workspace_temp_dir() -> Path:
        temp_root = Path(__file__).resolve().parents[2] / ".tmp-tests"
        temp_root.mkdir(parents=True, exist_ok=True)
        return temp_root

    def test_resolve_advisory_only_defaults_to_true_without_release_proof(self):
        with patch.dict(os.environ, {}, clear=False):
            advisory_only, reason = intent.resolve_advisory_only(False)

        self.assertTrue(advisory_only)
        self.assertEqual(reason, "release_gates_missing_or_failed")

    def test_resolve_advisory_only_turns_off_when_release_gates_pass(self):
        with patch.dict(os.environ, {}, clear=False):
            advisory_only, reason = intent.resolve_advisory_only(True)

        self.assertFalse(advisory_only)
        self.assertEqual(reason, "release_gates_passed")

    def test_resolve_advisory_only_honors_env_override(self):
        with patch.dict(os.environ, {"SIDECAR_ADVISORY_ONLY": "true"}, clear=False):
            advisory_only, reason = intent.resolve_advisory_only(True)

        self.assertTrue(advisory_only)
        self.assertEqual(reason, "env_override")

    def test_load_release_status_requires_matching_model_version(self):
        temp_dir = self._workspace_temp_dir() / f"intent-router-{uuid.uuid4().hex}"
        temp_dir.mkdir(parents=True, exist_ok=True)
        release_status_path = temp_dir / "release_status.json"
        release_status_path.write_text(
            json.dumps(
                {
                    "model_version": "v9.9",
                    "release_gates": {"passed": True},
                }
            ),
            encoding="utf-8",
        )

        try:
            with patch.object(intent, "RELEASE_STATUS_PATH", str(release_status_path)):
                release_status, passed = intent.load_release_status("v1.2")
        finally:
            release_status_path.unlink(missing_ok=True)
            temp_dir.rmdir()

        self.assertFalse(passed)
        self.assertTrue(release_status.get("mismatch"))


if __name__ == "__main__":
    unittest.main()
