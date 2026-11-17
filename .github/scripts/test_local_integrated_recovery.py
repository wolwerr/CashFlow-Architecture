"""Offline tests: integrated recovery safety and financial invariants, no live IO."""
import contextlib
import io
import pathlib
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import local_integrated_recovery as probe


class Token:
    def get(self):
        return "test-only-token"


class Result:
    def __init__(self, code=0):
        self.returncode = code


class IntegratedRecoveryTests(unittest.TestCase):
    def test_preview_never_stops_services_or_writes(self):
        with patch.object(probe, "docker") as docker, patch.object(probe, "http") as http:
            with contextlib.redirect_stdout(io.StringIO()):
                code = probe.main(["--merchant-id", str(probe.uuid.uuid4()),
                                   "--date", "2026-01-01"])
        self.assertEqual(code, 0)
        docker.assert_not_called()
        http.assert_not_called()

    def test_execute_requires_token(self):
        with patch.dict(probe.os.environ, {}, clear=True):
            with self.assertRaises(SystemExit):
                probe.main(["--merchant-id", str(probe.uuid.uuid4()),
                            "--date", "2026-01-01", "--execute"])

    def test_exact_projection_rejects_double_credit(self):
        before = {name: probe.Decimal("0") for name in probe.FIELDS}
        after = {name: probe.Decimal("0") for name in probe.FIELDS}
        after["credit_total"] = probe.Decimal("1")
        after["debit_total"] = probe.Decimal("1")
        self.assertTrue(probe.expected_projection(before, after))
        after["credit_total"] = probe.Decimal("2")
        self.assertFalse(probe.expected_projection(before, after))

    def test_unique_history_rejects_duplicate_and_wrong_amount(self):
        rows = [{"id": "c", "type": "CREDIT", "amount": "1.00"},
                {"id": "d", "type": "DEBIT", "amount": "1.00"}]
        self.assertTrue(probe.unique_financial_history(set(), rows, "c", "d"))
        self.assertFalse(probe.unique_financial_history(set(), rows + rows[:1], "c", "d"))
        self.assertFalse(probe.unique_financial_history({"c"}, rows, "c", "d"))
        rows[1]["amount"] = "2.00"
        self.assertFalse(probe.unique_financial_history(set(), rows, "c", "d"))

    def test_stop_failure_still_attempts_restore(self):
        calls = []
        def docker(*args):
            calls.append(args)
            if args == ("stop", "rabbitmq"):
                return Result(1)
            return Result()
        with patch.object(probe, "docker", side_effect=docker), \
             patch.object(probe, "running", return_value=True):
            with self.assertRaises(probe.VerificationError):
                probe.outage("rabbitmq", lambda: None, lambda _: None)
        self.assertIn(("up", "-d", "rabbitmq"), calls)

    def test_failed_action_restores_service(self):
        calls = []
        def docker(*args):
            calls.append(args)
            return Result()
        with patch.object(probe, "docker", side_effect=docker), \
             patch.object(probe, "running", return_value=False):
            # Simulate down at stop, up after restoration.
            with self.assertRaises(probe.VerificationError):
                probe.outage("consolidation", lambda: (_ for _ in ()).throw(
                    probe.VerificationError("write failed")), lambda _: None)
        self.assertIn(("up", "-d", "consolidation"), calls)

    def test_idempotency_replay_and_conflict(self):
        result = iter([(201, {"id": "test-1"}), (201, {"id": "test-1"}), (409, None)])
        with patch.object(probe.uuid, "uuid4", return_value="key"):
            outcome = probe.create(Token(), "merchant", "2026-01-01", "CREDIT",
                                   request=lambda *args: next(result))
        self.assertEqual(outcome, "test-1")

    def test_failed_idempotency_does_not_retry_financial_post(self):
        calls = []
        def request(*args):
            calls.append(args)
            return 503, None
        with self.assertRaises(probe.VerificationError):
            probe.create(Token(), "merchant", "2026-01-01", "CREDIT", request)
        self.assertEqual(len(calls), 1)


if __name__ == "__main__":
    unittest.main()
