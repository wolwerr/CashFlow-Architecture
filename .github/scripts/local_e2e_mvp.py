#!/usr/bin/env python3
"""CashFlow Trilha 05: opt-in end-to-end verification via the local NGINX proxy.

Requires disposable merchant accounts with distinct Keycloak memberships.
Never log bearer tokens or customer-identifying data. No automatic cleanup of
financial transactions is attempted, because the financial audit is append-only.
"""
import argparse
import datetime as dt
from decimal import Decimal
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = "http://127.0.0.1:8080"


class VerificationError(Exception):
    pass


def request(method, path, token=None, body=None, headers=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    request_headers = {"Accept": "application/json"}
    if token:
        request_headers["Authorization"] = "Bearer " + token
    if data is not None:
        request_headers["Content-Type"] = "application/json"
    if headers:
        request_headers.update(headers)
    req = urllib.request.Request(BASE + path, data=data, headers=request_headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=8) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        error.read()
        return error.code, None


def require(condition, message):
    if not condition:
        raise VerificationError(message)


def query(path, **params):
    return path + "?" + urllib.parse.urlencode(params)


def current_balance(transport, token, merchant, day):
    status, body = transport("GET",
        query("/api/v1/consolidation/daily", merchant_id=merchant, date=day), token)
    require(status == 200 and isinstance(body, dict), "Consolidation unavailable or unauthorized")
    return {key: Decimal(body[key]) for key in
            ("credit_total", "debit_total", "net_amount", "closing_balance")}


def create(transport, token, merchant, day, kind, amount, key):
    payload = {"merchant_id": merchant, "reference_date": day,
               "type": kind, "amount": amount}
    return transport("POST", "/api/v1/transactions", token, payload,
                     {"Idempotency-Key": key, "X-Correlation-ID": str(uuid.uuid4())})


def history(transport, token, merchant):
    """Collect all pages without assuming the new transactions are the first records."""
    entries = []
    for page in range(1, 21):
        status, body = transport("GET",
            query("/api/v1/transactions", merchant_id=merchant, page=page, page_size=100), token)
        require(status == 200 and isinstance(body, dict), "Merchant history unavailable")
        entries.extend(body["items"])
        if not body["has_more"]:
            return entries
    raise VerificationError("History exceeds 20 pages; use a fresh disposable merchant")


def run_flow(transport, token, other_token, merchant, other_merchant, day,
             sleep=time.sleep, timeout=30):
    """Run the full local test; only call after explicit opt-in and token checks."""
    steps = []

    def passed(name):
        steps.append(name)

    status, _ = transport("GET", "/api/v1/merchants/")
    require(status == 401, "Unauthenticated merchant directory must return 401")
    passed("unauthenticated_denied")

    status, merchants = transport("GET", "/api/v1/merchants/", token)
    require(status == 200 and isinstance(merchants, list) and
            any(item["id"].lower() == merchant for item in merchants),
            "Primary token must have current membership in primary merchant")
    status, other_merchants = transport("GET", "/api/v1/merchants/", other_token)
    require(status == 200 and isinstance(other_merchants, list) and
            any(item["id"].lower() == other_merchant for item in other_merchants),
            "Second token must have membership in other merchant")
    require(not any(item["id"].lower() == merchant for item in other_merchants),
            "Second token is also a member of primary merchant; isolation is untestable")
    passed("two_distinct_merchants")

    status, _ = transport("GET",
        query("/api/v1/transactions", merchant_id=merchant), other_token)
    require(status == 403, "Cross-merchant history must return 403")
    status, _ = transport("GET",
        query("/api/v1/consolidation/daily", merchant_id=merchant, date=day), other_token)
    require(status == 403, "Cross-merchant balance must return 403")
    status, _ = create(transport, other_token, merchant, day, "CREDIT", "1.00", str(uuid.uuid4()))
    require(status == 403, "Cross-merchant transaction must return 403")
    passed("cross_merchant_denied")

    baseline = current_balance(transport, token, merchant, day)
    old_history = history(transport, token, merchant)
    old_ids = {item["id"] for item in old_history}
    passed("baseline_recorded")

    key = str(uuid.uuid4())
    status, credit = create(transport, token, merchant, day, "CREDIT", "100.00", key)
    require(status == 201 and isinstance(credit, dict) and credit.get("id"),
            "CREDIT creation must return 201 with transaction ID; stop on ambiguous write")
    status, replay = create(transport, token, merchant, day, "CREDIT", "100.00", key)
    require(status == 201 and isinstance(replay, dict) and replay.get("id") == credit["id"],
            "Idempotent replay must return the original transaction")
    status, _ = create(transport, token, merchant, day, "DEBIT", "100.00", key)
    require(status == 409, "Reused key with a different payload must return 409")
    passed("credit_and_http_idempotency")

    status, debit = create(transport, token, merchant, day, "DEBIT", "25.00", str(uuid.uuid4()))
    require(status == 201 and isinstance(debit, dict) and debit.get("id"),
            "DEBIT creation must return 201; stop on ambiguous write")
    passed("debit_created")

    expected_ids = {credit["id"], debit["id"]}
    deadline = time.monotonic() + timeout
    while True:
        rows = history(transport, token, merchant)
        added = [row for row in rows if row["id"] in expected_ids]
        if len(added) == 2 and len({row["id"] for row in added}) == 2:
            require(expected_ids.isdisjoint(old_ids), "Transactions already existed in baseline")
            require({(row["type"], row["amount"]) for row in added} ==
                    {("CREDIT", "100.00"), ("DEBIT", "25.00")},
                    "History data does not match confirmed transactions")
            passed("history_has_two_unique_transactions")
            break
        if time.monotonic() >= deadline:
            raise VerificationError("New transactions are missing from the authorized history")
        sleep(0.5)

    while True:
        after = current_balance(transport, token, merchant, day)
        if (after["credit_total"] == baseline["credit_total"] + Decimal("100.00") and
                after["debit_total"] == baseline["debit_total"] + Decimal("25.00") and
                after["net_amount"] == baseline["net_amount"] + Decimal("75.00") and
                after["closing_balance"] == baseline["closing_balance"] + Decimal("75.00")):
            passed("rabbitmq_consolidation_and_balance")
            break
        if time.monotonic() >= deadline:
            raise VerificationError("Consolidation did not reflect CREDIT=100 and DEBIT=25 within timeout")
        sleep(0.5)
    return steps


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true",
                        help="Create two real test transactions in a disposable local merchant")
    parser.add_argument("--merchant-id", required=True)
    parser.add_argument("--other-merchant-id", required=True)
    parser.add_argument("--date", required=True, help="Past/current business date YYYY-MM-DD")
    parser.add_argument("--timeout-seconds", type=int, default=30)
    args = parser.parse_args(argv)
    try:
        merchant = str(uuid.UUID(args.merchant_id))
        other = str(uuid.UUID(args.other_merchant_id))
        day = dt.date.fromisoformat(args.date)
    except ValueError:
        parser.error("Merchant IDs must be UUIDs; date must be YYYY-MM-DD")
    if merchant == other:
        parser.error("Use two distinct disposable merchants")
    if day > (dt.datetime.now(dt.timezone(dt.timedelta(hours=-3))).date()):
        parser.error("Date must not be in the future in Sao Paulo")
    if not 5 <= args.timeout_seconds <= 120:
        parser.error("Timeout must be between 5 and 120 seconds")
    if not args.execute:
        print(json.dumps({"executed": False, "mode": "preview",
                          "checks": ["JWT", "membership", "cross-merchant 403",
                                     "CREDIT/DEBIT", "idempotency", "history", "consolidation"]}))
        return 0
    token = os.getenv("CASHFLOW_E2E_TOKEN")
    other_token = os.getenv("CASHFLOW_E2E_OTHER_TOKEN")
    if not token or not other_token:
        parser.error("Set CASHFLOW_E2E_TOKEN and CASHFLOW_E2E_OTHER_TOKEN locally")
    if token == other_token:
        parser.error("Provide different account tokens for isolation checks")
    try:
        steps = run_flow(request, token, other_token, merchant, other, day.isoformat(),
                         timeout=args.timeout_seconds)
    except (VerificationError, urllib.error.URLError, TimeoutError, ValueError, KeyError) as error:
        # Avoid logging response bodies, JWTs, UUIDs or exception strings from HTTP libraries.
        print(json.dumps({"executed": True, "passed": False,
                          "failure_type": type(error).__name__,
                          "message": str(error) if isinstance(error, VerificationError)
                                     else "Local request or response failed"}), file=sys.stderr)
        return 1
    print(json.dumps({"executed": True, "passed": True, "checks": steps}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
