"""Safety tests for local PostgreSQL backup/restore verification.

Docker and PostgreSQL are mocked. These tests do not access live data.
"""
import pathlib
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import local_backup_restore_check as check


class Result:
    def __init__(self, stdout=b"", returncode=0):
        self.stdout = stdout
        self.returncode = returncode


class BackupRestoreSafetyTests(unittest.TestCase):
    def test_preview_never_calls_compose_or_pg_dump(self):
        with patch.object(check, "command") as command:
            report = check.verify_backup("transactions", False)
        command.assert_not_called()
        self.assertEqual(report["status"], "NOT_EXECUTED")
        self.assertFalse(report["executed"])

    def test_only_financial_databases_are_accepted(self):
        with self.assertRaises(ValueError):
            check.verify_backup("keycloak", True)

    def test_rejects_unexpected_compose_project(self):
        with patch.object(check, "command", return_value=Result(stdout=b"postgres\n")):
            with self.assertRaisesRegex(RuntimeError, "Unexpected"):
                check.verify_backup("consolidation", True)

    def test_refuses_to_drop_existing_scratch_db(self):
        services = b"postgres\ntransactions\nconsolidation\nweb\nrabbitmq\nkeycloak\n"
        with patch.object(check, "command", side_effect=[
            Result(stdout=services), Result(stdout=b"1\n"),
        ]) as command:
            with self.assertRaisesRegex(RuntimeError, "Scratch database exists"):
                check.verify_backup("transactions", True)
        self.assertEqual([c.args[0] for c in command.call_args_list], [
            "services", "transactions_exists"
        ])

    def test_refuses_to_create_when_existence_query_fails(self):
        services = b"postgres\ntransactions\nconsolidation\nweb\nrabbitmq\nkeycloak\n"
        with patch.object(check, "command", side_effect=[
            Result(stdout=services), Result(returncode=1),
        ]) as command:
            with self.assertRaisesRegex(RuntimeError, "Scratch database exists"):
                check.verify_backup("transactions", True)
        self.assertEqual(command.call_count, 2)

    def test_command_allowlist_is_literal_and_excludes_destructive_live_db(self):
        for arguments in check.COMMANDS.values():
            self.assertEqual(arguments[:3], ("docker", "compose", "exec") if arguments != check.COMMANDS["services"] else ("docker", "compose", "config"))
            self.assertNotIn("--force", arguments)
            self.assertNotIn("cashflow_transactions", arguments[-1:] if "dropdb" in arguments else ())
            self.assertNotIn("cashflow_consolidation", arguments[-1:] if "dropdb" in arguments else ())


if __name__ == "__main__":
    unittest.main()
