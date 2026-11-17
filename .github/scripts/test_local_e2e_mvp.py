"""Offline contract tests for the opt-in local CashFlow E2E probe."""
import contextlib
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

spec = importlib.util.spec_from_file_location(
    "local_e2e_mvp", Path(__file__).with_name("local_e2e_mvp.py"))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)

M1 = "68a5baac-d97f-45b4-9119-b4c5b9de7c9b"
M2 = "b5644520-1056-4234-8ffd-5a0cd16346da"
DAY = "2026-11-16"


class FakeLocalApi:
    def __init__(self, *, deny_isolation=True, project=True):
        self.posts = []
        self.created = {}
        self.deny_isolation = deny_isolation
        self.project = project
        self.rows = []
        self.requests = []
        self.replays = 0

    def merchant_directory(self, token):
        accounts = {
            "owner": [{"id": M1, "name": "Loja 1", "role": "OPERATOR"}],
            "other": [{"id": M2, "name": "Loja 2", "role": "OPERATOR"}],
        }
        return (200, accounts[token]) if token in accounts else (404, None)

    def balance(self):
        credit = sum(100 for row in self.rows if row["type"] == "CREDIT") if self.project else 0
        debit = sum(25 for row in self.rows if row["type"] == "DEBIT") if self.project else 0
        return 200, {
            "credit_total": f"{credit}.00",
            "debit_total": f"{debit}.00",
            "net_amount": f"{credit-debit}.00",
            "closing_balance": f"{credit-debit}.00",
        }

    def create_transaction(self, body, headers):
        key = headers["Idempotency-Key"]
        if key in self.created:
            prior_body, prior_record = self.created[key]
            if body != prior_body:
                return 409, None
            self.replays += 1
            return 201, prior_record
        record = {**body, "id": f"txn-{len(self.rows)+1}"}
        self.rows.append(record)
        self.created[key] = (dict(body), record)
        self.posts.append(record)
        return 201, record

    def __call__(self, method, path, token=None, body=None, headers=None):
        route = urlsplit(path)
        self.requests.append((method, route.path, token))
        if not token:
            return 401, None
        if route.path == probe.MERCHANTS_PATH:
            return self.merchant_directory(token)

        merchant = body["merchant_id"] if body else parse_qs(route.query).get("merchant_id", [None])[0]
        if token == "other" and merchant == M1 and self.deny_isolation:
            return 403, None
        if route.path == "/api/v1/consolidation/daily":
            return self.balance()
        if route.path == probe.TRANSACTIONS_PATH:
            if method == "GET":
                return 200, {"items": list(self.rows), "page": 1,
                             "page_size": 100, "has_more": False}
            if method == "POST":
                return self.create_transaction(body, headers)
        return 404, None

class LocalMvpTests(unittest.TestCase):
    def test_full_merchant_isolation_idempotency_and_projection(self):
        fake = FakeLocalApi()
        steps = probe.run_flow(fake, "owner", "other", M1, M2, DAY,
                               sleep=lambda _: None)
        self.assertIn("rabbitmq_consolidation_and_balance", steps)
        self.assertEqual(2, len(fake.posts))
        self.assertEqual(1, fake.replays)
        self.assertEqual(["CREDIT", "DEBIT"], [row["type"] for row in fake.rows])
        self.assertFalse(any(token == "other" and path == "/api/v1/merchants/"
                             and method == "POST" for method, path, token in fake.requests))

    def test_rejects_cross_merchant_data_leak_before_writing(self):
        fake = FakeLocalApi(deny_isolation=False)
        with self.assertRaisesRegex(probe.VerificationError, "Cross-merchant"):
            probe.run_flow(fake, "owner", "other", M1, M2, DAY)
        self.assertEqual([], fake.posts)

    def test_preview_never_sends_http_or_needs_tokens(self):
        output = io.StringIO()
        with patch.dict("os.environ", {}, clear=True), contextlib.redirect_stdout(output):
            status = probe.main(["--merchant-id", M1, "--other-merchant-id", M2,
                                 "--date", DAY])
        self.assertEqual(0, status)
        self.assertFalse(__import__("json").loads(output.getvalue())["executed"])

    def test_requires_opt_in_tokens(self):
        with patch.dict("os.environ", {}, clear=True):
            with self.assertRaises(SystemExit):
                probe.main(["--execute", "--merchant-id", M1, "--other-merchant-id", M2,
                            "--date", DAY])

    def test_rejects_same_merchant_id(self):
        with self.assertRaises(SystemExit):
            probe.main(["--merchant-id", M1, "--other-merchant-id", M1, "--date", DAY])

    def test_refuses_create_when_primary_token_lacks_merchant(self):
        fake = FakeLocalApi()
        with self.assertRaises(probe.VerificationError):
            probe.run_flow(fake, "other", "owner", M1, M2, DAY)
        self.assertFalse(fake.posts)


if __name__ == "__main__":
    unittest.main()
