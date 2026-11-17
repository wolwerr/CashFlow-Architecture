"""Offline tests for local financial resilience probe; no Docker or HTTP calls."""
import contextlib
from decimal import Decimal
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "local_resilience_e2e", Path(__file__).with_name("local_resilience_e2e.py"))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)

M = "68a5baac-d97f-45b4-9119-b4c5b9de7c9b"
DAY = "2026-11-16"


class Result:
    def __init__(self, returncode=0):
        self.returncode = returncode
        self.stdout = ""


class ResilienceTests(unittest.TestCase):
    def test_preview_does_not_touch_docker_or_http(self):
        with patch.object(probe, "docker") as docker, patch.object(probe, "request") as http:
            with contextlib.redirect_stdout(io.StringIO()):
                code = probe.main(["--merchant-id", M, "--date", DAY])
        self.assertEqual(code, 0)
        docker.assert_not_called()
        http.assert_not_called()

    def test_requires_token_only_on_execute(self):
        with patch.dict("os.environ", {}, clear=True):
            with self.assertRaises(SystemExit):
                probe.main(["--merchant-id", M, "--date", DAY, "--execute"])

    def test_exact_balance_delta_rejects_double_counting(self):
        before = {key: Decimal("0") for key in
                  ("credit_total", "debit_total", "net_amount", "closing_balance")}
        after = {key: Decimal("1") for key in
                 ("credit_total", "net_amount", "closing_balance")}
        after["debit_total"] = Decimal("0")
        self.assertTrue(probe.projected(before, after))
        after["credit_total"] = Decimal("2")
        self.assertFalse(probe.projected(before, after))

    def test_successful_outage_write_restores_and_checks_projection(self):
        baseline = {key: Decimal("0") for key in
                    ("credit_total", "debit_total", "net_amount", "closing_balance")}
        updated = {key: Decimal("1") for key in
                   ("credit_total", "net_amount", "closing_balance")}
        updated["debit_total"] = Decimal("0")
        state = {"stopped": False, "written": False}
        def docker(*args):
            if args == ("stop", "consolidation"):
                state["stopped"] = True
            if args == ("up", "-d", "consolidation"):
                state["stopped"] = False
            return Result()
        def create(*args):
            self.assertTrue(state["stopped"])
            state["written"] = True
            return "test-txn"
        with patch.object(probe, "verify_compose"), patch.object(probe, "check_membership"), \
             patch.object(probe, "balance", side_effect=[baseline, updated, updated]), \
             patch.object(probe, "history", side_effect=[[], ["test-txn"]]), \
             patch.object(probe, "docker", side_effect=docker), \
             patch.object(probe, "is_running", side_effect=lambda _: not state["stopped"]), \
             patch.object(probe, "create_credit", side_effect=create), \
             patch.object(probe, "write_report"), patch.object(probe.time, "monotonic", return_value=1):
            outcome = probe.execute("test-token", M, DAY, 1, 30, sleep=lambda _: None)
        self.assertTrue(state["written"])
        self.assertFalse(state["stopped"])
        self.assertEqual(outcome["outcome"], "PASS")

    def test_failed_write_always_restores(self):
        commands = []
        def docker(*args):
            commands.append(args)
            return Result()
        with patch.object(probe, "verify_compose"), patch.object(probe, "check_membership"), \
             patch.object(probe, "balance", return_value={}), \
             patch.object(probe, "history", return_value=[]), \
             patch.object(probe, "docker", side_effect=docker), \
             patch.object(probe, "is_running", side_effect=[False, True]), \
             patch.object(probe, "create_credit", side_effect=probe.VerificationError("HTTP 201 missing")), \
             patch.object(probe, "write_report"):
            with self.assertRaises(probe.VerificationError):
                probe.execute("token", M, DAY, 1, 30, sleep=lambda _: None)
        self.assertIn(("up", "-d", "consolidation"), commands)

    def test_invalid_or_future_date_rejected(self):
        with self.assertRaises(SystemExit):
            probe.main(["--merchant-id", M, "--date", "2099-01-01"])


if __name__ == "__main__":
    unittest.main()
