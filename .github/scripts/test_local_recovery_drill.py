"""Non-disruptive unit tests for the opt-in CashFlow local recovery drill."""
import pathlib
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import local_recovery_drill as drill


class Result:
    def __init__(self, stdout="", returncode=0):
        self.stdout = stdout
        self.returncode = returncode


class LocalRecoveryDrillTests(unittest.TestCase):
    def test_preview_does_not_call_docker(self):
        with patch.object(drill, "compose") as compose:
            result = drill.perform_drill("rabbitmq", 10, False)
        compose.assert_not_called()
        self.assertFalse(result["executed"])
        self.assertEqual(result["outcome"], "NOT_EXECUTED")

    def test_invalid_service_cannot_be_selected(self):
        with self.assertRaises(ValueError):
            drill.perform_drill("postgres", 10, True)

    def test_unexpected_project_refused_before_stop(self):
        with patch.object(drill, "compose", return_value=Result(stdout="postgres\n")):
            with self.assertRaisesRegex(RuntimeError, "not the expected"):
                drill.perform_drill("rabbitmq", 1, True)

    def test_stopped_service_refused_before_mutation(self):
        services = "postgres\nrabbitmq\nkeycloak\ntransactions\nconsolidation\nweb\n"
        with patch.object(drill, "compose", side_effect=[
            Result(stdout=services), Result(stdout=""),
        ]) as compose:
            with self.assertRaisesRegex(RuntimeError, "not running"):
                drill.perform_drill("keycloak", 1, True)
        self.assertEqual(compose.call_count, 2)

    def test_success_stops_and_restores(self):
        services = "postgres\nrabbitmq\nkeycloak\ntransactions\nconsolidation\nweb\n"
        with patch.object(drill, "compose", side_effect=[
            Result(stdout=services), Result(stdout="rabbitmq\n"),
            Result(), Result(), Result(stdout="rabbitmq\n"),
        ]) as compose, patch.object(drill.time, "sleep") as sleep:
            report = drill.perform_drill("rabbitmq", 2, True)
        self.assertEqual(report["outcome"], "RESTORED")
        self.assertEqual(compose.call_args_list[2].args, ("rabbitmq_stop",))
        self.assertEqual(compose.call_args_list[3].args, ("rabbitmq_restore",))
        sleep.assert_called_once_with(2)

    def test_stop_failure_still_attempts_restart(self):
        services = "postgres\nrabbitmq\nkeycloak\ntransactions\nconsolidation\nweb\n"
        with patch.object(drill, "compose", side_effect=[
            Result(stdout=services), Result(stdout="consolidation\n"),
            Result(returncode=1), Result(),
            Result(stdout="consolidation\n"),
        ]) as compose:
            with self.assertRaisesRegex(RuntimeError, "stop failed"):
                drill.perform_drill("consolidation", 1, True)
        self.assertEqual(compose.call_args_list[-2].args, ("consolidation_restore",))


if __name__ == "__main__":
    unittest.main()
