#!/usr/bin/env python3
"""Opt-in LOCAL RabbitMQ/consumer failure recovery with financial audit checks.

Creates two permanent test entries on an isolated disposable merchant. Stops
RabbitMQ and Consolidation sequentially, ALWAYS attempts to restore services.
Never retry an ambiguous financial write or run against production.
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

from local_load_probe import Tokens

BASE = "http://127.0.0.1:8080"
SERVICES = {"postgres", "rabbitmq", "keycloak", "transactions", "consolidation", "web"}
REPORT = Path("reports/integrated-recovery-local.json")
AMOUNT = Decimal("1.00")
FIELDS = ("credit_total", "debit_total", "net_amount", "closing_balance")


class VerificationError(Exception):
    pass


def require(condition, reason):
    if not condition:
        raise VerificationError(reason)


def docker(*args):
    return subprocess.run(["docker", "compose", *args], capture_output=True,
                          text=True, check=False, timeout=90)


def running(service):
    result = docker("ps", "--status", "running", "--services", service)
    require(result.returncode == 0, "Cannot inspect Compose service state")
    return service in result.stdout.splitlines()


def verify_local():
    configured = docker("config", "--services")
    require(configured.returncode == 0 and
            SERVICES.issubset(set(configured.stdout.splitlines())),
            "Unexpected Compose project")
    active = docker("ps", "--status", "running", "--services")
    require(active.returncode == 0 and
            SERVICES.issubset(set(active.stdout.splitlines())),
            "All six services must be running before outage")


def http(method, path, token, body=None, key=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    headers = {"Authorization": "Bearer " + token, "Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if key:
        headers.update({"Idempotency-Key": key, "X-Correlation-ID": str(uuid.uuid4())})
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        with opener.open(req, timeout=8) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        error.close()
        return error.code, None


def query(path, **params):
    return path + "?" + urllib.parse.urlencode(params)


def balance(tokens, merchant, day, request=http):
    status, body = request("GET", query("/api/v1/consolidation/daily",
                                       merchant_id=merchant, date=day), tokens.get())
    require(status == 200 and isinstance(body, dict), "Balance baseline/verification unavailable")
    return {name: Decimal(str(body[name])) for name in FIELDS}


def history(tokens, merchant, request=http):
    rows = []
    for page in range(1, 21):
        status, body = request("GET", query("/api/v1/transactions",
                          merchant_id=merchant, page=page, page_size=100), tokens.get())
        require(status == 200 and isinstance(body, dict), "History unavailable")
        rows.extend(body["items"])
        if not body["has_more"]:
            return rows
    raise VerificationError("History exceeds 2000 records; use a fresh test merchant")


def member(tokens, merchant, request=http):
    status, rows = request("GET", "/api/v1/merchants/", tokens.get())
    require(status == 200 and isinstance(rows, list) and any(
        str(row.get("id", "")).lower() == merchant for row in rows),
        "Token lacks merchant membership")


def post(tokens, merchant, day, kind, key, request=http):
    return request("POST", "/api/v1/transactions", tokens.get(),
                   {"merchant_id": merchant, "reference_date": day,
                    "type": kind, "amount": "1.00"}, key)


def create(tokens, merchant, day, kind, request=http):
    key = str(uuid.uuid4())
    status, body = post(tokens, merchant, day, kind, key, request)
    require(status == 201 and isinstance(body, dict) and body.get("id"),
            "Ambiguous financial POST; never retry automatically")
    transaction_id = body["id"]
    replay_status, replay = post(tokens, merchant, day, kind, key, request)
    require(replay_status == 201 and isinstance(replay, dict)
            and replay.get("id") == transaction_id,
            "Idempotent replay did not return original transaction")
    conflict = "CREDIT" if kind == "DEBIT" else "DEBIT"
    conflict_status, _ = post(tokens, merchant, day, conflict, key, request)
    require(conflict_status == 409, "Same key with different payload must return 409")
    return transaction_id


def expected_projection(before, after):
    changes = {"credit_total": AMOUNT, "debit_total": AMOUNT,
               "net_amount": Decimal("0"), "closing_balance": Decimal("0")}
    return all(after[name] == before[name] + delta for name, delta in changes.items())


def unique_financial_history(before_ids, rows, credit_id, debit_id):
    expected = {credit_id: "CREDIT", debit_id: "DEBIT"}
    if len(expected) != 2 or set(expected).intersection(before_ids):
        return False
    matching = [row for row in rows if row["id"] in expected]
    return len(matching) == 2 and all(
        row["type"] == expected[row["id"]] and Decimal(str(row["amount"])) == AMOUNT
        for row in matching)


def write_report(report):
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def outage(service, action, sleep):
    """Restore even when stop fails or write raises; do not mask failed restoration."""
    stop_attempted = False
    try:
        stop_attempted = True
        stopped = docker("stop", service)
        require(stopped.returncode == 0 and not running(service),
                service + " did not stop")
        result = action()
        sleep(2)
        return result
    finally:
        if stop_attempted:
            restored = docker("up", "-d", service)
            require(restored.returncode == 0 and running(service),
                    "CRITICAL: " + service + " restoration failed; inspect Docker immediately")


def execute(tokens, merchant, day, outage_seconds=15, timeout=120,
            request=http, sleep=time.sleep):
    verify_local()
    member(tokens, merchant, request)
    before = balance(tokens, merchant, day, request)
    old_ids = {row["id"] for row in history(tokens, merchant, request)}
    report = {
        "scenario": "local-integrated-rabbitmq-consumer-recovery",
        "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "executed": True, "outage_seconds_requested": outage_seconds,
        "rabbitmq_restored": False, "consumer_restored": False,
        "transactions_during_broker_outage": False,
        "transactions_during_consumer_outage": False,
        "unique_financial_history": False, "projection_exact_delta": False,
        "idempotency_replay_and_conflict": False, "outcome": "FAIL"
    }
    try:
        # One credit while broker is unavailable: Transactions should persist via Outbox.
        def broker_write():
            require(running("transactions"), "Transactions stopped with RabbitMQ")
            credit = create(tokens, merchant, day, "CREDIT", request)
            report["transactions_during_broker_outage"] = True
            sleep(outage_seconds)
            return credit
        credit_id = outage("rabbitmq", broker_write, sleep)
        report["rabbitmq_restored"] = True

        # Wait until RabbitMQ is serving connections before subsequent consumer drill.
        # Consumer is explicitly stopped, so debit must queue until it comes back.
        def consumer_write():
            require(running("transactions"), "Transactions stopped with consumer")
            debit = create(tokens, merchant, day, "DEBIT", request)
            report["transactions_during_consumer_outage"] = True
            sleep(outage_seconds)
            return debit
        debit_id = outage("consolidation", consumer_write, sleep)
        report["consumer_restored"] = True
        report["idempotency_replay_and_conflict"] = True

        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                rows = history(tokens, merchant, request)
                report["unique_financial_history"] = unique_financial_history(
                    old_ids, rows, credit_id, debit_id)
                if (report["unique_financial_history"] and
                        expected_projection(before, balance(tokens, merchant, day, request))):
                    sleep(2)
                    report["projection_exact_delta"] = expected_projection(
                        before, balance(tokens, merchant, day, request))
                    if report["projection_exact_delta"]:
                        report["outcome"] = "PASS"
                        break
            except (VerificationError, urllib.error.URLError, TimeoutError, ValueError, KeyError):
                pass
            sleep(1)
        require(report["outcome"] == "PASS",
                "History or projection not confirmed; inspect merchant before repeating")
        return report
    finally:
        write_report(report)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--merchant-id", required=True)
    parser.add_argument("--date", required=True)
    parser.add_argument("--outage-seconds", type=int, default=15)
    parser.add_argument("--recovery-timeout", type=int, default=120)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args(argv)
    try:
        merchant = str(uuid.UUID(args.merchant_id))
        day = dt.date.fromisoformat(args.date)
    except ValueError:
        parser.error("Invalid merchant UUID or date")
    if day > dt.datetime.now(dt.timezone(dt.timedelta(hours=-3))).date():
        parser.error("Date cannot be future in Sao Paulo")
    if not 1 <= args.outage_seconds <= 120 or not 10 <= args.recovery_timeout <= 300:
        parser.error("Outage 1..120s and recovery 10..300s")
    if not args.execute:
        print(json.dumps({"executed": False, "mode": "preview",
                          "disrupts": ["rabbitmq", "consolidation"],
                          "permanent_financial_writes": 2,
                          "checks": ["Transactions HTTP 201 while RabbitMQ down",
                                     "Transactions HTTP 201 while consumer down",
                                     "idempotent replay and conflict",
                                     "unique history", "exact final projection"]}, indent=2))
        return 0
    token = os.getenv("CASHFLOW_RECOVERY_TOKEN")
    if not token:
        parser.error("Set CASHFLOW_RECOVERY_TOKEN locally, never share it")
    try:
        tokens = Tokens(token, os.getenv("CASHFLOW_RECOVERY_REFRESH_TOKEN"))
        if not tokens.refresh and tokens.exp - time.time() < 2*args.outage_seconds + args.recovery_timeout + 90:
            parser.error("Token validity insufficient; provide refresh token")
        result = execute(tokens, merchant, day.isoformat(),
                         args.outage_seconds, args.recovery_timeout)
        print(json.dumps(result, indent=2))
        return 0
    except (VerificationError, subprocess.TimeoutExpired, OSError, ValueError,
            KeyError, urllib.error.URLError, TimeoutError) as error:
        print("Recovery scenario failed: " +
              (str(error) if isinstance(error, VerificationError)
               else type(error).__name__), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
