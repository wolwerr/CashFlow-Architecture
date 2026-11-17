#!/usr/bin/env python3
"""Read-only LOCAL observability inventory: no health mutation, no secrets in output.

Counts service log lines and safe, coarse patterns. This is NOT an ADR-008
OpenTelemetry end-to-end validation; no automatic PASS of observability.
"""
import argparse
import datetime as dt
import json
import re
import subprocess
import sys
from pathlib import Path

SERVICES = ("transactions", "consolidation", "web")
EXPECTED = {"postgres", "rabbitmq", "keycloak", *SERVICES}
PATTERNS = {
    "correlation_mentions": re.compile(r"correlation.?id|x-correlation-id", re.I),
    "trace_mentions": re.compile(r"trace.?id|traceparent", re.I),
    "error_mentions": re.compile(r"\\b(error|exception|fatal)\\b", re.I),
}


def docker(*args):
    return subprocess.run(["docker", "compose", *args], capture_output=True,
                          text=True, check=False, timeout=30)


def inspect(since="10m"):
    config = docker("config", "--services")
    if config.returncode or not EXPECTED.issubset(set(config.stdout.splitlines())):
        raise RuntimeError("Unexpected local Compose project")
    running = docker("ps", "--status", "running", "--services")
    if running.returncode:
        raise RuntimeError("Cannot inspect local services")
    active = set(running.stdout.splitlines())
    checks = {}
    for service in SERVICES:
        result = docker("logs", "--no-color", "--since=" + since, service)
        if result.returncode:
            raise RuntimeError("Cannot inspect local service logs: " + service)
        # Docker may use stderr for log transport. Never print individual lines.
        lines = (result.stdout + "\\n" + result.stderr).splitlines()
        checks[service] = {
            "running": service in active,
            "log_lines": len([line for line in lines if line.strip()]),
            **{key: sum(bool(pattern.search(line)) for line in lines)
               for key, pattern in PATTERNS.items()},
        }
    return {
        "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "environment": "LOCAL",
        "executed": True,
        "checks": checks,
        "adr008_otlp_collector_verified": False,
        "adr008_distributed_traces_verified": False,
        "adr008_metrics_logs_traces_backends_verified": False,
        "outcome": "PARTIAL",
        "note": "Log pattern counts are diagnostic only. No distributed trace or OTLP pipeline verified.",
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args(argv)
    if not args.execute:
        print(json.dumps({"executed": False, "outcome": "NOT_RUN",
                          "note": "Use --execute for read-only local log counts"}))
        return 0
    try:
        report = inspect()
    except (RuntimeError, OSError, subprocess.TimeoutExpired):
        print("Observability inventory failed; check Docker Compose", file=sys.stderr)
        return 1
    path = Path("evidence/observability-inventory.json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2) + "\\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
