#!/usr/bin/env python3
"""Read-only local CashFlow load probe. Python standard library only.

Optional OIDC refresh uses CASHFLOW_LOAD_REFRESH_TOKEN held in memory.
No tokens are written to reports, printed, or included in exceptions.
"""
import argparse
import base64
import collections
import concurrent.futures
import datetime as dt
import json
import math
import os
import pathlib
import threading
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


def jwt_exp(token):
    """Read expiry for scheduling only; this does not validate JWT signature."""
    try:
        part = token.split(".")[1]
        padded = part + "=" * (-len(part) % 4)
        claims = json.loads(base64.urlsafe_b64decode(padded))
        return int(claims["exp"])
    except (IndexError, ValueError, KeyError, TypeError) as error:
        raise ValueError("Access token must be a JWT with an exp claim") from error


class Tokens:
    def __init__(self, access, refresh=None, client_id="cashflow-spa",
                 auth_url="http://127.0.0.1:8081"):
        self.access = access
        self.refresh = refresh
        self.client_id = client_id
        self.exp = jwt_exp(access)
        self.lock = threading.Lock()
        self.auth_url = auth_url.rstrip("/")
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        parsed = urllib.parse.urlsplit(self.auth_url)
        if parsed.scheme != "http" or parsed.hostname not in ("127.0.0.1", "localhost") or parsed.username or parsed.password:
            raise ValueError("Token endpoint must use local HTTP without credentials")

    def get(self):
        if time.time() < self.exp - 30:
            return self.access
        with self.lock:
            if time.time() < self.exp - 30:
                return self.access
            if not self.refresh:
                raise ValueError("Access token expires soon; provide CASHFLOW_LOAD_REFRESH_TOKEN")
            body = urllib.parse.urlencode({
                "grant_type": "refresh_token",
                "client_id": self.client_id,
                "refresh_token": self.refresh,
            }).encode("ascii")
            request = urllib.request.Request(
                self.auth_url + "/realms/cashflow/protocol/openid-connect/token",
                data=body, headers={"Content-Type": "application/x-www-form-urlencoded"},
                method="POST",
            )
            try:
                with self.opener.open(request, timeout=5) as response:
                    tokens = json.load(response)
                new_access = tokens["access_token"]
                new_exp = jwt_exp(new_access)
                if new_exp <= time.time() + 30:
                    raise ValueError("Refreshed access token has insufficient validity")
                self.access = new_access
                self.refresh = tokens.get("refresh_token", self.refresh)
                self.exp = new_exp
            except (urllib.error.URLError, OSError, KeyError, ValueError) as error:
                raise ValueError("Local token refresh failed; check Keycloak session and refresh token") from None
            return self.access


def probe(url, tokens, timeout, scheduled, origin, opener):
    started = time.perf_counter()
    queue_ms = (started - scheduled) * 1000
    try:
        token = tokens.get()
        request = urllib.request.Request(
            url, headers={"Authorization": "Bearer " + token, "Accept": "application/json"},
            method="GET",
        )
        with opener.open(request, timeout=timeout) as response:
            response.read()
            status = response.status
    except urllib.error.HTTPError as error:
        status = error.code
        error.close()
    except (urllib.error.URLError, TimeoutError, OSError, ValueError):
        status = 0
    finished = time.perf_counter()
    return status, (finished - started) * 1000, queue_ms, finished - origin


def run(url, tokens, rate, duration, workers, timeout):
    origin = time.perf_counter()
    finish = origin + duration
    expected = math.ceil(rate * duration - 1e-9)
    submitted = 0
    results = []
    pending = set()
    # Bypass system proxies independently of environment variables.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        while submitted < expected:
            scheduled = origin + submitted / rate
            if scheduled >= finish:
                break
            delay = scheduled - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            if len(pending) >= workers * 2:
                done, pending = concurrent.futures.wait(
                    pending, return_when=concurrent.futures.FIRST_COMPLETED
                )
                results.extend(task.result() for task in done)
            pending.add(pool.submit(probe, url, tokens, timeout, scheduled, origin, opener))
            submitted += 1
        schedule_end = time.perf_counter()
        for task in concurrent.futures.as_completed(pending):
            results.append(task.result())
    elapsed = time.perf_counter() - origin
    successful = [lat for status, lat, _, _ in results if 200 <= status < 300]
    errors = len(results) - len(successful)
    missed = max(0, expected - submitted)
    counts = collections.Counter(status for status, _, _, _ in results)
    buckets = collections.Counter(int(math.floor(finished)) for _, _, _, finished in results)
    # Throughput is measured over the full observation interval, including drain.
    observed_rps = len(successful) / elapsed if elapsed else 0
    queue = [q for _, _, q, _ in results]
    return {
        "endpoint": url,
        "requested_rps": rate,
        "requested_duration_seconds": duration,
        "workers": workers,
        "timeout_seconds": timeout,
        "elapsed_seconds": round(elapsed, 3),
        "scheduling_elapsed_seconds": round(schedule_end - origin, 3),
        "drain_seconds": round(elapsed - (schedule_end - origin), 3),
        "expected_requests": expected,
        "submitted_requests": submitted,
        "missed_scheduled_requests": missed,
        "successful_requests": len(successful),
        "failed_requests": errors,
        "failure_ratio": round((errors + missed) / expected, 5) if expected else 1,
        "successful_p50_ms": round(percentile(successful, .50), 2) if successful else None,
        "successful_p95_ms": round(percentile(successful, .95), 2) if successful else None,
        "successful_p99_ms": round(percentile(successful, .99), 2) if successful else None,
        "scheduling_delay_p95_ms": round(percentile(queue, .95), 2) if queue else None,
        "successful_throughput_rps": round(observed_rps, 2),
        "completed_requests_per_second": {
            str(k): buckets[k] for k in sorted(buckets)
        },
        "status_counts": {str(k): counts[k] for k in sorted(counts)},
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://127.0.0.1:8080")
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
    if not all(map(math.isfinite, (args.rate, args.duration, args.timeout))) or (
        args.rate <= 0 or args.duration <= 0 or args.workers < 1 or args.timeout <= 0
    ):
        parser.error("Rate, duration and timeout must be finite and positive; workers >= 1")
    base = urllib.parse.urlsplit(args.base_url)
    if (base.scheme != "http" or base.hostname not in ("localhost", "127.0.0.1")
            or base.username or base.password or base.query or base.fragment
            or base.path not in ("", "/")):
        parser.error("Use http://127.0.0.1:8080 or another local HTTP host and port")
    refresh = os.environ.get("CASHFLOW_LOAD_REFRESH_TOKEN")
    try:
        tokens = Tokens(token, refresh=refresh)
    except ValueError as error:
        parser.error(str(error))
    if not refresh and tokens.exp - time.time() <= args.duration + 30:
        parser.error("JWT validity is too short for the requested duration. "
                     "Provide CASHFLOW_LOAD_REFRESH_TOKEN or use a shorter test.")
    query = urllib.parse.urlencode({"merchant_id": merchant, "date": day})
    url = args.base_url.rstrip("/") + "/api/v1/consolidation/daily?" + query
    report = run(url, tokens, args.rate, args.duration, args.workers, args.timeout)
    report["captured_at_utc"] = dt.datetime.now(dt.timezone.utc).isoformat()
    report["pass"] = (report["failure_ratio"] <= 0.05
                      and report["successful_p95_ms"] is not None
                      and report["successful_p95_ms"] < 500
                      and report["submitted_requests"] == report["expected_requests"]
                      and report["scheduling_elapsed_seconds"] <= args.duration + 1)
    destination = pathlib.Path("reports/load-local.json")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
