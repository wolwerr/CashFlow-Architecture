#!/usr/bin/env python3
"""Opt-in local-only end-to-end Consolidation outage and financial recovery test.

WARNING: stops the LOCAL Consolidation container and writes one real CREDIT
to a disposable merchant. Financial records remain append-only. No tokens,
merchant IDs or transaction IDs are stored in the report.
"""
import argparse
import datetime as dt
from decimal import Decimal
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = "http://127.0.0.1:8080"
EXPECTED = {"postgres", "rabbitmq", "keycloak", "transactions", "consolidation", "web"}
AMOUNT = Decimal("1.00")
REPORT = Path("reports/resilience-local.json")


class VerificationError(Exception):
    pass


def docker(*args):
    return subprocess.run(("docker", "compose", *args), capture_output=True, text=True,
                          timeout=90, check=False)


def require(condition, reason):
    if not condition:
        raise VerificationError(reason)


def verify_compose():
    configured = docker("config", "--services")
    require(configured.returncode == 0 and EXPECTED.issubset(set(configured.stdout.splitlines())),
            "Not the expected local CashFlow Docker Compose project")
    running = docker("ps", "--status", "running", "--services")
    require(running.returncode == 0 and EXPECTED.issubset(set(running.stdout.splitlines())),
            "All six local services must be running before the drill")


def is_running(service):
    result = docker("ps", "--status", "running", "--services", service)
    require(result.returncode == 0, "Cannot inspect Compose service state")
    return service in result.stdout.splitlines()


def request(method, path, token, body=None, headers=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    all_headers = {"Authorization": "Bearer " + token, "Accept": "application/json"}
    if data is not None:
        all_headers["Content-Type"] = "application/json"
    all_headers.update(headers or {})
    req = urllib.request.Request(BASE + path, data=data, headers=all_headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=8) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        error.close()
        return error.code, None


def balance(token, merchant, day):
    query = urllib.parse.urlencode({"merchant_id": merchant, "date": day})
    status, body = request("GET", "/api/v1/consolidation/daily?" + query, token)
    require(status == 200 and isinstance(body, dict), "Cannot read authorized baseline balance")
    return {key: Decimal(body[key]) for key in
            ("credit_total", "debit_total", "net_amount", "closing_balance")}


def history(token, merchant):
    ids = []
    for page in range(1, 21):
        query = urllib.parse.urlencode({"merchant_id": merchant, "page": page, "page_size": 100})
        status, body = request("GET", "/api/v1/transactions?" + query, token)
        require(status == 200 and isinstance(body, dict), "Cannot read authorized transaction history")
        ids.extend(item["id"] for item in body["items"])
        if not body["has_more"]:
            return ids
    raise VerificationError("Test merchant has too much history; use a fresh disposable merchant")


def check_membership(token, merchant):
    status, body = request("GET", "/api/v1/merchants/", token)
    require(status == 200 and isinstance(body, list) and
            any(item.get("id", "").lower() == merchant for item in body),
            "Token must be an authorized member of the disposable merchant")


def create_credit(token, merchant, day, key):
    status, body = request("POST", "/api/v1/transactions", token,
                           {"merchant_id": merchant, "type": "CREDIT",
                            "amount": "1.00", "reference_date": day},
                           {"Idempotency-Key": key, "X-Correlation-ID": str(uuid.uuid4())})
    require(status == 201 and isinstance(body, dict) and body.get("id"),
            "Transaction did not return HTTP 201 during outage; do not retry ambiguous write")
    return body["id"]


def projected(before, after):
    return (after["credit_total"] == before["credit_total"] + AMOUNT and
            after["debit_total"] == before["debit_total"] and
            after["net_amount"] == before["net_amount"] + AMOUNT and
            after["closing_balance"] == before["closing_balance"] + AMOUNT)


def execute(token, merchant, day, outage_seconds, recovery_timeout, sleep=time.sleep):
    verify_compose()
    check_membership(token, merchant)
    before = balance(token, merchant, day)
    old_ids = set(history(token, merchant))
    report = {"executed": True, "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
              "scenario": "transactions-during-consolidation-outage",
              "outage_seconds_requested": outage_seconds,
              "baseline_checked": True, "consolidation_stopped": False,
              "transaction_http_201_during_outage": False, "consolidation_restored": False,
              "transaction_unique_in_history": False, "projection_exactly_once": False,
              "outcome": "FAIL"}
    stop_attempted = False
    transaction_id = None
    try:
        stop_attempted = True
        stopped = docker("stop", "consolidation")
        require(stopped.returncode == 0 and not is_running("consolidation"),
                "Failed to stop local Consolidation")
        report["consolidation_stopped"] = True
        # No balance query while service is stopped; Transactions must remain writable.
        key = str(uuid.uuid4())
        transaction_id = create_credit(token, merchant, day, key)
        report["transaction_http_201_during_outage"] = True
        sleep(outage_seconds)
    finally:
        if stop_attempted:
            restored = docker("up", "-d", "consolidation")
            report["consolidation_restored"] = restored.returncode == 0 and is_running("consolidation")
            write_report(report)
    require(report["consolidation_restored"], "Consolidation could not be restored")
    require(transaction_id not in old_ids, "New transaction was already in baseline history")
    deadline = time.monotonic() + recovery_timeout
    while time.monotonic() < deadline:
        try:
            ids = history(token, merchant)
            report["transaction_unique_in_history"] = ids.count(transaction_id) == 1
            if report["transaction_unique_in_history"] and projected(before, balance(token, merchant, day)):
                # A second observation helps detect immediate duplicate projection.
                sleep(1)
                report["projection_exactly_once"] = projected(before, balance(token, merchant, day))
                if report["projection_exactly_once"]:
                    report["outcome"] = "PASS"
                    break
        except (VerificationError, urllib.error.URLError, TimeoutError, ValueError, KeyError):
            pass  # Service may be restarting; keep polling until deadline.
        sleep(1)
    write_report(report)
    require(report["outcome"] == "PASS",
            "Projection or unique transaction history not confirmed before timeout")
    return report


def write_report(report):
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--merchant-id", required=True, help="Disposable LOCAL merchant UUID")
    parser.add_argument("--date", required=True, help="YYYY-MM-DD, not in the future")
    parser.add_argument("--outage-seconds", type=int, default=15)
    parser.add_argument("--recovery-timeout", type=int, default=90)
    parser.add_argument("--execute", action="store_true", help="Explicitly authorize LOCAL stop/start and financial write")
    args = parser.parse_args(argv)
    try:
        merchant = str(uuid.UUID(args.merchant_id))
        day = dt.date.fromisoformat(args.date)
    except ValueError:
        parser.error("Invalid merchant UUID or ISO date")
    if day > dt.datetime.now(dt.timezone(dt.timedelta(hours=-3))).date():
        parser.error("Date must not be in the future in Sao Paulo")
    if not 1 <= args.outage_seconds <= 120 or not 5 <= args.recovery_timeout <= 300:
        parser.error("Outage must be 1..120 seconds and recovery timeout 5..300 seconds")
    if not args.execute:
        print(json.dumps({"executed": False, "mode": "preview", "steps": [
            "authorized baseline", "stop consolidation", "POST CREDIT HTTP 201",
            "restore consolidation", "unique history and exact balance delta"]}, indent=2))
        return 0
    token = os.environ.get("CASHFLOW_RESILIENCE_TOKEN")
    if not token:
        parser.error("Set CASHFLOW_RESILIENCE_TOKEN locally; never commit or paste it")
    try:
        result = execute(token, merchant, day.isoformat(), args.outage_seconds,
                         args.recovery_timeout)
    except (VerificationError, subprocess.TimeoutExpired, OSError, ValueError, KeyError,
            urllib.error.URLError, TimeoutError) as error:
        # No potentially sensitive exception details from HTTP libraries.
        print("Resilience test failed: " +
              (str(error) if isinstance(error, VerificationError)
               else type(error).__name__), file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
