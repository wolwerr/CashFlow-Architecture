"""Offline unit tests for validation test report parsing and safety."""
import contextlib
import io
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import validation_test_summary as report


def execute(stdout="", stderr="", code=0):
    return Mock(stdout=stdout, stderr=stderr, returncode=code)


class ValidationSummaryTests(unittest.TestCase):
    def test_success_unittest_with_skips(self):
        result = report.one_suite("ok", "suite.py",
            runner=lambda *a, **k: execute(stderr="Ran 7 tests in 0.024s\n\nOK (skipped=2)\n"))
        self.assertEqual(result["status"], "PASS")
        self.assertEqual((result["passed"], result["failed"], result["skipped"]), (5, 0, 2))

    def test_failed_unittest_reports_correct_counts(self):
        result = report.one_suite("failed", "suite.py",
            runner=lambda *a, **k: execute(
                stderr="Ran 8 tests in 0.032s\n\nFAILED (failures=1, errors=2)\n", code=1))
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual((result["passed"], result["failed"]), (5, 3))

    def test_missing_counts_fails_closed(self):
        result = report.one_suite("unknown", "suite.py",
            runner=lambda *a, **k: execute(stderr="unittest failed", code=1))
        self.assertEqual(result["status"], "FAIL")
        self.assertIsNone(result["total"])

    def test_compose_failure_is_not_success(self):
        result = report.compose_check(runner=lambda *a, **k: execute(code=1))
        self.assertEqual(result["status"], "FAIL")

    def test_report_marks_scope_as_offline_not_live_e2e(self):
        data = report.render([
            dict(label="Contract", status="PASS", total=2, passed=2,
                 failed=0, skipped=0, seconds=0.1, detail="Passed"),
            dict(label="Docker Compose configuration", status="PASS",
                 total=None, passed=None, failed=None, skipped=None,
                 seconds=0.2, detail="Valid schema")])
        self.assertIn("2 passed", data)
        self.assertIn("not live CashFlow E2E", data)
        self.assertIn("2/2 passed", data)


if __name__ == "__main__":
    unittest.main()
