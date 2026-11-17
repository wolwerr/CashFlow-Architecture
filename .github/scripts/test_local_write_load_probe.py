"""Offline safety, arithmetic and contract tests for concurrent local writes."""
import contextlib
import io
import json
import pathlib
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import local_write_load_probe as subject


class FinancialWriteProbeTests(unittest.TestCase):
    def test_balance_deltas(self):
        before = {k: subject.Decimal("0") for k in
                  ("credit_total", "debit_total", "net_amount", "closing_balance")}
        after = {"credit_total": subject.Decimal("12"),
                 "debit_total": subject.Decimal("8"),
                 "net_amount": subject.Decimal("4"),
                 "closing_balance": subject.Decimal("4")}
        self.assertTrue(subject.matches(before, after, 12, 8))
        after["closing_balance"] = subject.Decimal("5")
        self.assertFalse(subject.matches(before, after, 12, 8))

    def test_history_rejects_duplicates(self):
        rows = [{"id": "a", "type": "CREDIT", "amount": "1.00"},
                {"id": "a", "type": "CREDIT", "amount": "1.00"}]
        with self.assertRaises(subject.VerificationError):
            subject.verify_records(set(), rows, {"a", "b"}, 1, 1)

    def test_history_accepts_correct_writes(self):
        rows = [{"id": "a", "type": "CREDIT", "amount": "1.00"},
                {"id": "b", "type": "DEBIT", "amount": "1.00"}]
        subject.verify_records(set(), rows, {"a", "b"}, 1, 1)

    def test_no_financial_write_without_execute(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            status = subject.main(["--merchant-id", str(subject.uuid.uuid4()),
                                   "--date", "2026-01-01"])
        self.assertEqual(status, 0)
        response = json.loads(output.getvalue())
        self.assertFalse(response["executed"])
        self.assertEqual(response["writes_if_executed"], 20)

    def test_requires_token_in_execute_mode(self):
        with patch.dict(subject.os.environ, {}, clear=True):
            with self.assertRaises(SystemExit):
                subject.main(["--merchant-id", str(subject.uuid.uuid4()),
                              "--date", "2026-01-01", "--execute"])


if __name__ == "__main__":
    unittest.main()
