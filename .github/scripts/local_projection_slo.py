#!/usr/bin/env python3
"""Opt-in local projection latency experiment with an isolated test merchant.

Creates real CREDIT transactions; use exclusively against disposable local data.
No bearer tokens or financial identifiers are written to a report file.
"""
import argparse
import datetime as dt
from decimal import Decimal
import json
import math
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = "http://127.0.0.1:8080"
AMOUNT = Decimal("0.01")
TARGET_SECONDS = 30


def http_json(method, path, token, payload=None, headers=None):
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    request_headers = {"Authorization": "Bearer " + token, "Accept": "application/json"}
    if data is not None:
        request_headers["Content-Type"] = "application/json"
    if headers:
        request_headers.update(headers)
    request = urllib.request.Request(
        BASE + path, data=data, headers=request_headers, method=method
    )
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        error.close()
        return error.code, None


def balance(token, merchant, day):
    query = urllib.parse.urlencode({"merchant_id": merchant, "date": day})
    status, response = http_json("GET", "/api/v1/consolidation/daily?" + query, token)
    if status != 200 or not isinstance(response, dict):
        raise RuntimeError("Consolidation query did not return an authorized success response")
    return Decimal(response["credit_total"])


def create_credit(token, merchant, day):
    payload = {
        "merchant_id": merchant, "type": "CREDIT", "amount": "0.01",
        "reference_date": day,
    }
    status, response = http_json("POST", "/api/v1/transactions", token, payload, {
        "Idempotency-Key": str(uuid.uuid4()),
        "X-Correlation-ID": str(uuid.uuid4()),
    })
    if status != 201 or not isinstance(response, dict):
        raise RuntimeError("Transaction POST did not return 201; stop without retrying ambiguous write")
    return response.get("id")


def measure_one(token, merchant, day, poll_interval, timeout):
    before = balance(token, merchant, day)
    started = time.monotonic()
    transaction_id = create_credit(token, merchant, day)
    created_at = time.monotonic()
    deadline = created_at + timeout
    expected = before + AMOUNT
    while time.monotonic() < deadline:
        # An isolated merchant with no concurrent writers is required for this inference.
        if balance(token, merchant, day) >= expected:
            return {"projected": True, "seconds": round(time.monotonic() - created_at, 3)}
        time.sleep(min(poll_interval, max(0.0, deadline - time.monotonic())))
    return {"projected": False, "seconds": None}


def summarize(samples):
    observed = [sample["seconds"] for sample in samples if sample["projected"]]
    total = len(samples)
    timely = sum(1 for value in observed if value <= TARGET_SECONDS)
    return {
        "total_samples": total,
        "projected_within_30s": timely,
        "not_projected_within_30s": total - timely,
        "success_ratio": round(timely / total, 4) if total else 0,
        "p95_observed_seconds": (
            sorted(observed)[max(0, math.ceil(len(observed) * 0.95) - 1)]
            if observed else None
        ),
        "pass": total >= 20 and timely / total >= 0.95,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--merchant-id", required=True, help="Dedicated disposable test merchant UUID")
    parser.add_argument("--date", required=True, help="Test reference date YYYY-MM-DD")
    parser.add_argument("--samples", type=int, default=20)
    parser.add_argument("--poll-seconds", type=float, default=0.5)
    parser.add_argument("--execute", action="store_true", help="Explicitly authorize real test credits")
    args = parser.parse_args(argv)
    try:
        merchant = str(uuid.UUID(args.merchant_id))
        day = dt.date.fromisoformat(args.date).isoformat()
    except ValueError as exc:
        parser.error("Invalid merchant UUID or reference date: " + str(exc))
    if not 20 <= args.samples <= 100 or not 0.1 <= args.poll_seconds <= 5:
        parser.error("Require 20..100 samples and poll interval 0.1..5 seconds")
    if not args.execute:
        print(json.dumps({"executed": False, "samples": args.samples, "target_seconds": 30}))
        return 0
    token = os.environ.get("CASHFLOW_SLO_TOKEN")
    if not token:
        parser.error("CASHFLOW_SLO_TOKEN must be supplied in the local environment")
    samples = []
    try:
        for _ in range(args.samples):
            samples.append(measure_one(token, merchant, day, args.poll_seconds, TARGET_SECONDS))
    except (RuntimeError, urllib.error.URLError, TimeoutError, ValueError, KeyError) as error:
        print("SLO test interrupted: " + type(error).__name__ + "; partial samples: " +
              str(len(samples)), file=sys.stderr)
        return 1
    summary = summarize(samples)
    # No JWT, merchant ID, transaction ID or per-user data in report output.
    print(json.dumps({"executed": True, "reference": "local-only",
                      "target_seconds": TARGET_SECONDS, **summary}, indent=2))
    return 0 if summary["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
