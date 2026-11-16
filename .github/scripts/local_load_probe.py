#!/usr/bin/env python3
"""Read-only authenticated load probe for local CashFlow GET endpoints.

No dependencies outside the Python standard library. Never prints the bearer token.
"""
import argparse
import concurrent.futures
import datetime as dt
import json
import math
import os
import pathlib
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def probe(url, token, timeout):
    request = urllib.request.Request(
        url, headers={"Authorization": "Bearer " + token, "Accept": "application/json"},
        method="GET",
    )
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            response.read()
            status = response.status
    except urllib.error.HTTPError as error:
        status = error.code
        error.close()
    except (urllib.error.URLError, TimeoutError, OSError):
        status = 0
    return status, (time.perf_counter() - started) * 1000


def run(url, token, rate, duration, workers, timeout):
    started = time.perf_counter()
    finish = started + duration
    submitted = 0
    results = []
    pending = set()
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        while True:
            scheduled = started + submitted / rate
            if scheduled >= finish:
                break
            delay = scheduled - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            # Bound outstanding work, so overload is visible as a lower delivered rate.
            if len(pending) >= workers * 2:
                done, pending = concurrent.futures.wait(
                    pending, return_when=concurrent.futures.FIRST_COMPLETED
                )
                results.extend(task.result() for task in done)
            pending.add(pool.submit(probe, url, token, timeout))
            submitted += 1
        for task in concurrent.futures.as_completed(pending):
            results.append(task.result())
    elapsed = time.perf_counter() - started
    successful = [latency for status, latency in results if 200 <= status < 300]
    errors = len(results) - len(successful)
    expected = math.ceil(rate * duration - 1e-9)
    missed = max(0, expected - submitted)
    failure_ratio = (errors + missed) / expected if expected else 1.0
    p95 = percentile(successful, 0.95)
    return {
        "endpoint": url,
        "requested_rps": rate,
        "requested_duration_seconds": duration,
        "workers": workers,
        "timeout_seconds": timeout,
        "elapsed_seconds": round(elapsed, 3),
        "expected_requests": expected,
        "submitted_requests": submitted,
        "missed_scheduled_requests": missed,
        "successful_requests": len(successful),
        "failed_requests": errors,
        "failure_ratio": round(failure_ratio, 5),
        "successful_p95_ms": round(p95, 2) if p95 is not None else None,
        "status_counts": {str(status): sum(1 for s, _ in results if s == status)
                          for status in sorted({s for s, _ in results})},
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:8080")
    parser.add_argument("--merchant-id", required=True)
    parser.add_argument("--date", required=True, help="YYYY-MM-DD")
    parser.add_argument("--rate", type=float, default=50)
    parser.add_argument("--duration", type=float, default=300)
    parser.add_argument("--workers", type=int, default=64)
    parser.add_argument("--timeout", type=float, default=5)
    args = parser.parse_args(argv)
    token = os.environ.get("CASHFLOW_LOAD_TOKEN")
    if not token:
        parser.error("CASHFLOW_LOAD_TOKEN must be set in the environment")
    try:
        merchant = str(uuid.UUID(args.merchant_id))
        day = dt.date.fromisoformat(args.date).isoformat()
    except ValueError as error:
        parser.error("Invalid merchant UUID or date: " + str(error))
    if args.rate <= 0 or args.duration <= 0 or args.workers < 1 or args.timeout <= 0:
        parser.error("Rate, duration and timeout must be positive; workers >= 1")
    base = urllib.parse.urlsplit(args.base_url)
    if base.scheme not in ("http", "https") or not base.netloc or base.username or base.password:
        parser.error("Provide an HTTP(S) base URL without credentials")
    if base.hostname not in ("localhost", "127.0.0.1", "::1"):
        parser.error("This probe is restricted to localhost; run it only against the local MVP")
    query = urllib.parse.urlencode({"merchant_id": merchant, "date": day})
    url = args.base_url.rstrip("/") + "/api/v1/consolidation/daily?" + query
    report = run(url, token, args.rate, args.duration, args.workers, args.timeout)
    report["captured_at_utc"] = dt.datetime.now(dt.timezone.utc).isoformat()
    report["pass"] = (report["failure_ratio"] <= 0.05
                      and report["successful_p95_ms"] is not None
                      and report["successful_p95_ms"] < 500)
    # Fixed local output path: never accept a filesystem destination from CLI input.
    destination = pathlib.Path("reports/load-local.json")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
