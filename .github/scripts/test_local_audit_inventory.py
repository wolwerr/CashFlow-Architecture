"""Safety and policy tests. No Docker calls or database access during test execution."""
import pathlib
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import local_audit_inventory as audit


class Result:
    def __init__(self, stdout="", returncode=0):
        self.stdout = stdout
        self.returncode = returncode


SERVICES = "postgres\ntransactions\nconsolidation\nkeycloak\nrabbitmq\nweb\n"


class AuditInventoryTests(unittest.TestCase):
    def test_preview_requires_no_docker(self):
        with patch.object(audit, "command") as call:
            result = audit.audit_inventory()
        call.assert_not_called()
        self.assertEqual(result["status"], "NOT_EXECUTED")

    def test_unexpected_project_is_rejected(self):
        with patch.object(audit, "command", return_value=Result("postgres\n")) as call:
            with self.assertRaisesRegex(RuntimeError, "Unexpected"):
                audit.audit_inventory(True)
        self.assertEqual(call.call_count, 1)

    def test_healthy_audits_pass(self):
        with patch.object(audit, "command", side_effect=[
            Result(SERVICES), Result("10"), Result("0"), Result("0"),
            Result("0"), Result("0"),
        ]):
            result = audit.audit_inventory(True)
        self.assertEqual(result["status"], "INTEGRITY_CHECK_PASSED")
        self.assertEqual(result["audit_total"], 10)

    def test_old_audits_require_review_not_deletion(self):
        with patch.object(audit, "command", side_effect=[
            Result(SERVICES), Result("10"), Result("1"), Result("0"),
            Result("0"), Result("0"),
        ]):
            result = audit.audit_inventory(True)
        self.assertEqual(result["status"], "RETENTION_REVIEW_REQUIRED")

    def test_missing_audit_fails(self):
        with patch.object(audit, "command", side_effect=[
            Result(SERVICES), Result("10"), Result("0"), Result("1"),
            Result("0"), Result("0"),
        ]):
            result = audit.audit_inventory(True)
        self.assertEqual(result["status"], "INTEGRITY_FAILED")

    def test_incomplete_query_fails_closed(self):
        with patch.object(audit, "command", side_effect=[
            Result(SERVICES), Result("10"), Result(returncode=1),
        ]):
            with self.assertRaisesRegex(RuntimeError, "query failed"):
                audit.audit_inventory(True)

    def test_commands_cannot_change_data(self):
        for arguments in audit.COMMANDS.values():
            self.assertNotIn("DELETE", " ".join(arguments).upper())
            self.assertNotIn("DROP", " ".join(arguments).upper())
            self.assertNotIn("UPDATE", " ".join(arguments).upper())


if __name__ == "__main__":
    unittest.main()
