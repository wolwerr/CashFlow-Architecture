"""Offline safety and parsing tests for the read-only operational snapshot."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "local_operational_snapshot", Path(__file__).with_name("local_operational_snapshot.py"))
ops = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ops)


class Result:
    def __init__(self, stdout="", returncode=0):
        self.stdout = stdout
        self.returncode = returncode


class OperationalSnapshotTests(unittest.TestCase):
    def test_preview_performs_zero_docker_calls(self):
        with patch.object(ops, "command") as run, contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(0, ops.main([]))
        run.assert_not_called()
        self.assertFalse(json.loads(output.getvalue())["executed"])

    def test_queue_totals_hide_names_and_count_ready_unacknowledged(self):
        metrics = ops.queue_totals([
            "transaction.queue 4 2", "deadletter.queue 1 0"])
        self.assertEqual({"queue_count": 2, "messages_ready": 5,
                          "messages_unacknowledged": 2}, metrics)
        self.assertNotIn("transaction.queue", str(metrics))

    def test_header_is_ignored(self):
        self.assertEqual(0, ops.queue_totals(["name messages_ready messages_unacknowledged"])["queue_count"])

    def test_queue_malformed_metrics_fail_closed(self):
        for rows in (["myqueue four 0"], ["myqueue 2"], ["queue 1 -1"]):
            with self.subTest(rows=rows), self.assertRaises(ops.SnapshotError):
                ops.queue_totals(rows)

    def test_configured_project_must_match_expected_services(self):
        with patch.object(ops, "checked_lines", return_value=["rabbitmq"]):
            with self.assertRaisesRegex(ops.SnapshotError, "expected CashFlow"):
                ops.snapshot()

    def test_no_mutation_commands_are_supported(self):
        self.assertEqual(set(ops.COMMANDS), {"configured", "running", "queues"})
        self.assertFalse(any("stop" in command or "down" in command
                             for command in ops.COMMANDS.values()))

    def test_snapshot_summarizes_without_exposing_queue_names(self):
        names = sorted(ops.EXPECTED)
        with patch.object(ops, "checked_lines",
                          side_effect=[names, names, ["internal.jobs 2 1"]]) as calls:
            result = ops.snapshot()
        self.assertEqual("OBSERVED", result["outcome"])
        self.assertEqual(2, result["messages_ready"])
        self.assertEqual(1, result["messages_unacknowledged"])
        self.assertEqual(["configured", "running", "queues"], [c.args[0] for c in calls.call_args_list])
        self.assertNotIn("internal.jobs", str(result))

    def test_missing_service_is_degraded_not_claimed_healthy(self):
        names = sorted(ops.EXPECTED)
        with patch.object(ops, "checked_lines",
                          side_effect=[names, [n for n in names if n != "consolidation"], []]):
            result = ops.snapshot()
        self.assertFalse(result["services_running"]["consolidation"])
        self.assertEqual("DEGRADED", result["outcome"])

    def test_no_declared_queues_is_not_reported_as_healthy(self):
        names = sorted(ops.EXPECTED)
        with patch.object(ops, "checked_lines", side_effect=[names, names, []]):
            result = ops.snapshot()
        self.assertEqual("DEGRADED", result["outcome"])

    def test_rabbitmq_down_refuses_queue_lookup(self):
        names = sorted(ops.EXPECTED)
        with patch.object(ops, "checked_lines",
                          side_effect=[names, [n for n in names if n != "rabbitmq"]]) as calls:
            with self.assertRaisesRegex(ops.SnapshotError, "RabbitMQ"):
                ops.snapshot()
        self.assertEqual(2, calls.call_count)

    def test_subprocess_failure_reports_error_without_secrets(self):
        with patch.object(ops, "command", return_value=Result(returncode=1)):
            with self.assertRaises(ops.SnapshotError):
                ops.checked_lines("running")


if __name__ == "__main__":
    unittest.main()
