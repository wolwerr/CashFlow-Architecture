"""Offline safety tests for acceptance UI recording and observability inventory."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import local_mvp_acceptance as acceptance
import local_observability_inventory as inventory
import record_manual_ui_acceptance as manual


class Result:
    returncode = 0
    stdout = ""
    stderr = ""


class ManualUiTests(unittest.TestCase):
    def test_preview_writes_nothing(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.chdir(directory):
            path = Path(directory) / "local-acceptance.json"
            path.write_text(json.dumps(acceptance.initial_manifest()), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertIsNone(manual.main(["--manifest", "local-acceptance.json"]))
            self.assertEqual(0, acceptance.report(json.loads(path.read_text()))["counts"]["PASS"])

    def test_explicit_confirmation_writes_two_evidences(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.chdir(directory):
            path = Path(directory) / "local-acceptance.json"
            path.write_text(json.dumps(acceptance.initial_manifest()), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertIsNone(manual.main(
                    ["--manifest", "local-acceptance.json", "--confirm-seven-checks"]))
            result = acceptance.report(json.loads(path.read_text()), path.parent)
            self.assertEqual(2, result["counts"]["PASS"])
            self.assertEqual("NOT_RUN", result["scenarios"]["observability"])

    def test_rejects_manifest_outside_workspace(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.chdir(directory):
            Path("local-acceptance.json").write_text("{}", encoding="utf-8")
            with self.assertRaises(ValueError):
                manual.validate_manifest_argument("../local-acceptance.json")

    def test_rejects_wrong_manifest_filename(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.chdir(directory):
            Path("local-acceptance.json").write_text("{}", encoding="utf-8")
            with self.assertRaises(ValueError):
                manual.validate_manifest_argument("arbitrary.json")

    def test_rejects_symlink_manifest(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.chdir(directory):
            original = Path("original.json")
            original.write_text("{}", encoding="utf-8")
            Path("local-acceptance.json").symlink_to(original)
            with self.assertRaises(ValueError):
                manual.validate_manifest_argument("local-acceptance.json")

    def test_observability_preview_has_no_docker_calls(self):
        with patch.object(inventory, "docker") as docker:
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, inventory.main([]))
        docker.assert_not_called()

    def test_observability_inventory_does_not_claim_full_adr(self):
        def docker(*args):
            result = Result()
            if args == ("config", "--services") or args == (
                    "ps", "--status", "running", "--services"):
                result.stdout = "\n".join(sorted(inventory.EXPECTED))
            else:
                result.stdout = "correlation_id traceparent error"
            return result
        with patch.object(inventory, "docker", side_effect=docker):
            report = inventory.inspect()
        self.assertEqual("PARTIAL", report["outcome"])
        self.assertFalse(report["adr008_otlp_collector_verified"])
        self.assertEqual(1, report["checks"]["transactions"]["correlation_mentions"])


if __name__ == "__main__":
    unittest.main()
