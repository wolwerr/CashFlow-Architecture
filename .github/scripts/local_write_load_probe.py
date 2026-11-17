#!/usr/bin/env python3
"""Opt-in LOCAL concurrent financial write verification on a disposable merchant.

Writes are append-only. Never run against production or an important merchant.
No implicit retries of ambiguous financial POSTs. No secrets/IDs in the report.
"""
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed
import datetime as dt
from decimal import Decimal
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from local_load_probe import Tokens

BASE = "http://127.0.0.1:8080"
REPORT = Path("reports/write-load-local.json")
AMOUNT = Decimal("1.00")


class VerificationError(Exception):
    pass


def require(ok, message):
    if not ok:
        raise VerificationError(message)


def transport(method, path, token, body=None, key=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    headers = {"Authorization": "Bearer " + token, "Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if key:
        headers["Idempotency-Key"] = key
        headers["X-Correlation-ID"] = str(uuid.uuid4())
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    start = time.perf_counter()
    try:
        with opener.open(req, timeout=8) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None, (time.perf_counter()-start)*1000
    except urllib.error.HTTPError as error:
        error.close()
        return error.code, None, (time.perf_counter()-start)*1000


def query(path, **params):
    return path + "?" + urllib.parse.urlencode(params)


def snapshot(tokens, merchant, day, call=transport):
    status, body, _ = call("GET", query("/api/v1/consolidation/daily",
                                       merchant_id=merchant, date=day), tokens.get())
    require(status == 200 and isinstance(body, dict), "Authorized balance unavailable")
    return {name: Decimal(str(body[name])) for name in
            ("credit_total", "debit_total", "net_amount", "closing_balance")}


def history(tokens, merchant, call=transport):
    rows = []
    for page in range(1, 21):
        status, body, _ = call("GET", query("/api/v1/transactions",
                                           merchant_id=merchant, page=page,
                                           page_size=100), tokens.get())
        require(status == 200 and isinstance(body, dict), "History unavailable")
        rows.extend(body["items"])
        if not body["has_more"]:
            return rows
    raise VerificationError("Merchant exceeds 2000 records; use a new disposable merchant")


def membership(tokens, merchant, call=transport):
    status, body, _ = call("GET", "/api/v1/merchants/", tokens.get())
    require(status == 200 and isinstance(body, list) and
            any(str(row.get("id", "")).lower() == merchant for row in body),
            "User is not a member of the disposable merchant")


def payload(merchant, day, kind):
    return {"merchant_id": merchant, "reference_date": day,
            "type": kind, "amount": "1.00"}


def write(tokens, merchant, day, kind, key, call=transport):
    return call("POST", "/api/v1/transactions", tokens.get(),
                payload(merchant, day, kind), key)


def expected_delta(credits, debits):
    credit = AMOUNT * credits
    debit = AMOUNT * debits
    return {"credit_total": credit, "debit_total": debit,
            "net_amount": credit-debit, "closing_balance": credit-debit}


def matches(before, after, credits, debits):
    return all(after[name] == before[name] + delta
               for name, delta in expected_delta(credits, debits).items())


def verify_records(before_ids, rows, expected_ids, credits, debits):
    matches_by_id = [r for r in rows if r["id"] in expected_ids]
    require(expected_ids.isdisjoint(before_ids), "Baseline includes generated transaction")
    require(len(matches_by_id) == len(expected_ids) == credits + debits,
            "New transaction IDs not unique in history")
    types = Counter(r["type"] for r in matches_by_id)
    require(types == Counter({"CREDIT": credits, "DEBIT": debits}),
            "Incorrect transaction types in history")
    require(all(Decimal(str(r["amount"])) == AMOUNT for r in matches_by_id),
            "Incorrect amounts in history")


def execute(tokens, merchant, day, credits, debits, workers, timeout, call=transport):
    membership(tokens, merchant, call)
    before = snapshot(tokens, merchant, day, call)
    old_ids = {r["id"] for r in history(tokens, merchant, call)}
    jobs = [(kind, str(uuid.uuid4())) for kind, count in
            (("CREDIT", credits), ("DEBIT", debits)) for _ in range(count)]
    results = []
    start = time.perf_counter()
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futures = [pool.submit(write, tokens, merchant, day, kind, key, call)
                   for kind, key in jobs]
        for future in as_completed(futures):
            try:
                results.append(future.result())
            except (OSError, ValueError, KeyError):
                raise VerificationError("Ambiguous POST: stop and inspect history; never retry automatically") from None
    elapsed = time.perf_counter() - start
    # Do not retry a POST if any response is unexpected.
    require(all(status == 201 and isinstance(body, dict) and body.get("id")
                for status, body, _ in results),
            "Unexpected write response; inspect history before any new attempt")
    ids = [body["id"] for _, body, _ in results]
    require(len(ids) == len(set(ids)), "Different keys created duplicate transaction IDs")
    require(set(ids).isdisjoint(old_ids), "Generated transaction overlaps baseline")
    # Exercise idempotent replay using only confirmed 201 operations.
    replay_kind, replay_key = jobs[0]
    # Map submitted idempotency keys to the corresponding responses via a separate
    # deterministic phase, since futures finish out of order: check the original
    # result by reusing keys and looking for a known ID.
    replay_status, replay_body, _ = write(tokens, merchant, day, replay_kind, replay_key, call)
    require(replay_status == 201 and isinstance(replay_body, dict)
            and replay_body.get("id") in ids,
            "Idempotency replay did not return an existing ID")
    conflict_kind = "DEBIT" if replay_kind == "CREDIT" else "CREDIT"
    conflict_status, _, _ = write(tokens, merchant, day, conflict_kind, replay_key, call)
    require(conflict_status == 409, "Idempotency conflict did not return HTTP 409")
    deadline = time.monotonic() + timeout
    checked_history = False
    checked_projection = False
    while time.monotonic() < deadline:
        rows = history(tokens, merchant, call)
        try:
            verify_records(old_ids, rows, set(ids), credits, debits)
            checked_history = True
        except VerificationError:
            pass
        if checked_history and matches(before, snapshot(tokens, merchant, day, call), credits, debits):
            time.sleep(1)
            checked_projection = matches(before, snapshot(tokens, merchant, day, call), credits, debits)
            if checked_projection:
                break
        time.sleep(.5)
    require(checked_history and checked_projection,
            "History or eventual projection did not converge within timeout")
    latencies = sorted(lat for _, _, lat in results)
    p95 = latencies[max(0, __import__("math").ceil(len(latencies)*.95)-1)]
    return {"scenario": "local-concurrent-financial-writes",
            "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "outcome": "PASS", "credits": credits, "debits": debits,
            "unique_writes": len(ids), "successful_writes": len(results),
            "write_elapsed_seconds": round(elapsed, 3),
            "write_throughput_rps": round(len(results)/elapsed, 2),
            "write_p95_ms": round(p95, 2),
            "idempotent_replay": True, "idempotency_conflict_409": True,
            "unique_history": True, "projection_exactly_once": True}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--merchant-id", required=True)
    parser.add_argument("--date", required=True)
    parser.add_argument("--credits", type=int, default=12)
    parser.add_argument("--debits", type=int, default=8)
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--recovery-timeout", type=int, default=90)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args(argv)
    try:
        merchant = str(uuid.UUID(args.merchant_id))
        day = dt.date.fromisoformat(args.date)
    except ValueError:
        parser.error("Invalid merchant UUID or date")
    today = dt.datetime.now(dt.timezone(dt.timedelta(hours=-3))).date()
    if day > today or not 1 <= args.credits <= 100 or not 1 <= args.debits <= 100:
        parser.error("Use a current/past date and 1..100 credits and debits")
    if not 1 <= args.workers <= 32 or not 5 <= args.recovery_timeout <= 300:
        parser.error("Workers 1..32 and timeout 5..300 are required")
    if not args.execute:
        print(json.dumps({"executed": False, "mode": "preview",
                          "writes_if_executed": args.credits + args.debits,
                          "notice": "Append-only local financial writes; disposable merchant only"}))
        return 0
    access = os.environ.get("CASHFLOW_WRITE_TOKEN")
    if not access:
        parser.error("Provide CASHFLOW_WRITE_TOKEN locally; never paste credentials")
    try:
        tokens = Tokens(access, os.environ.get("CASHFLOW_WRITE_REFRESH_TOKEN"))
        if not tokens.refresh and tokens.exp - time.time() < args.recovery_timeout + 90:
            parser.error("Token validity insufficient; use refresh token or shorter timeout")
        report = execute(tokens, merchant, day.isoformat(), args.credits, args.debits,
                         args.workers, args.recovery_timeout)
    except (VerificationError, ValueError, OSError, KeyError) as error:
        print(json.dumps({"outcome": "FAIL", "failure": str(error) if
                          isinstance(error, VerificationError) else type(error).__name__}))
        return 1
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
