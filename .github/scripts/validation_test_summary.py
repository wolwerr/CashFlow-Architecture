#!/usr/bin/env python3
"""Run offline contract suites and publish the real results in GitHub Step Summary.

These are simulated/offline validation tests, NOT live end-to-end tests.
No customer credentials, Docker stop operations or financial writes are required.
"""
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

SUITES = (
    ("MVP E2E script contract", "test_local_e2e_mvp.py"),
    ("Load probe", "test_local_load_probe.py"),
    ("Concurrent writes", "test_local_write_load_probe.py"),
    ("Acceptance manifest", "test_local_mvp_acceptance.py"),
    ("Manual UI + observability inventory", "test_local_manual_ui_and_observability.py"),
    ("Operational snapshot", "test_local_operational_snapshot.py"),
    ("Recovery drill", "test_local_recovery_drill.py"),
    ("Resilience probe", "test_local_resilience_e2e.py"),
    ("RabbitMQ + consumer recovery", "test_local_integrated_recovery.py"),
    ("PostgreSQL backup and restore", "test_local_backup_restore_check.py"),
    ("Projection SLO", "test_local_projection_slo.py"),
    ("Audit inventory", "test_local_audit_inventory.py"),
    ("Sonar severity policy", "test_sonar_severity_gate.py"),
)

RUN_RE = re.compile(r"Ran (\\d+) tests? in ([0-9.]+)s")
FAIL_RE = re.compile(r"(failures|errors|skipped)=(\\d+)")
SKIP_OK_RE = re.compile(r"OK \\(skipped=(\\d+)\\)")
SUITE_TIMEOUT = 120


def one_suite(label, filename, runner=subprocess.run):
    cmd = [sys.executable, "-m", "unittest", "discover", "-s", ".github/scripts",
           "-p", filename]
    start = time.monotonic()
    try:
        execution = runner(cmd, capture_output=True, text=True, check=False,
                           timeout=SUITE_TIMEOUT)
    except subprocess.TimeoutExpired:
        return dict(label=label, status="FAIL", total=None, passed=None,
                    failed=None, skipped=None, seconds=round(time.monotonic()-start, 3),
                    detail="Timed out")
    except OSError:
        return dict(label=label, status="FAIL", total=None, passed=None,
                    failed=None, skipped=None, seconds=round(time.monotonic()-start, 3),
                    detail="Runner unavailable")

    # Do not copy raw tracebacks into the published summary or artifact.
    output = execution.stdout + "\n" + execution.stderr
    match = list(RUN_RE.finditer(output))
    if not match:
        return dict(label=label, status="FAIL", total=None, passed=None,
                    failed=None, skipped=None, seconds=round(time.monotonic()-start, 3),
                    detail="Missing unittest result counters")
    total = int(match[-1].group(1))
    duration = float(match[-1].group(2))
    final = output[match[-1].end():]
    count = {key: int(value) for key, value in FAIL_RE.findall(final)}
    skipped = count.get("skipped", 0)
    skip_ok = SKIP_OK_RE.search(final)
    if skip_ok:
        skipped = int(skip_ok.group(1))
    failures = count.get("failures", 0) + count.get("errors", 0)
    # A suite is successful only when unittest explicitly prints OK and exits 0.
    succeeded = execution.returncode == 0 and re.search(r"(?m)^OK(?: \\(skipped=\\d+\\))?\\s*$", final)
    status = "PASS" if succeeded else "FAIL"
    return dict(label=label, status=status, total=total,
                passed=max(0, total-failures-skipped), failed=failures,
                skipped=skipped, seconds=duration,
                detail="Passed" if succeeded else "Unittest failed; see job logs")


def compose_check(runner=subprocess.run):
    start = time.monotonic()
    try:
        result = runner(["docker", "compose", "config", "--quiet"],
                        capture_output=True, text=True, check=False,
                        timeout=SUITE_TIMEOUT,
                        env={**os.environ, "CASHFLOW_GRAFANA_PASSWORD": "ci-local-only-not-a-secret"})
        success = result.returncode == 0
    except (subprocess.TimeoutExpired, OSError):
        success = False
    return dict(label="Docker Compose configuration", status="PASS" if success else "FAIL",
                total=None, passed=None, failed=None, skipped=None,
                seconds=round(time.monotonic()-start, 3),
                detail="Valid schema" if success else "Compose validation failed; see job logs")


def render(rows):
    tests = [r for r in rows if r["total"] is not None]
    total = sum(r["total"] for r in tests)
    passed = sum(r["passed"] for r in tests)
    failed = sum(r["failed"] for r in tests)
    skipped = sum(r["skipped"] for r in tests)
    failures = sum(r["status"] != "PASS" for r in rows)
    lines = [
        "## Validation: Offline Test Report",
        "",
        "### Summary",
        "",
        f"- **Checks:** {len(rows)-failures}/{len(rows)} passed",
        f"- **Test Results:** ✅ **{passed} passed** · {failed} failed · {skipped} skipped · {total} total",
        f"- **Failed checks:** {failures}",
        "",
        "### Results by suite",
        "",
        "| Suite | Passed | Failed | Skipped | Status |",
        "| --- | ---: | ---: | ---: | --- |",
    ]
    for r in rows:
        fmt = lambda n: str(n) if n is not None else "n/a"
        status = "✅ Passed" if r["status"] == "PASS" else "❌ Failed"
        lines.append(f"| {r['label']} | {fmt(r['passed'])} | {fmt(r['failed'])} | {fmt(r['skipped'])} | {status} |")
    lines += ["", "**Scope:** Offline test contracts and Compose syntax. These are not live CashFlow E2E executions.",
              "**Source:** Actual unittest and Compose execution results for this GitHub Actions run.",
              "**Note:** View the corresponding job logs for failed assertions; raw tracebacks are excluded from the report."]
    return "\n".join(lines)+"\n"


def main():
    rows = []
    for label, filename in SUITES:
        row = one_suite(label, filename)
        rows.append(row)
        print(f"{row['status']}: {label} ({row['total']} tests)", flush=True)
    rows.append(compose_check())
    report = render(rows)
    print(report)
    path = Path("reports/validation-summary.json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"category": "OFFLINE_VALIDATION", "rows": rows},
                               indent=2)+"\n", encoding="utf-8")
    summary = os.getenv("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as output:
            output.write(report)
    return int(any(r["status"] != "PASS" for r in rows))


if __name__ == "__main__":
    sys.exit(main())
