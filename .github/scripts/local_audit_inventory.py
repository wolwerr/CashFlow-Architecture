#!/usr/bin/env python3
"""Read-only, opt-in audit integrity and retention inventory for LOCAL CashFlow.

Reports aggregate counts, never identities, credentials or individual records.
Does not delete audit entries; legal retention approval is a separate decision.
"""
import argparse
import datetime as dt
import json
import subprocess
import sys

COMMANDS = {
    "services": ("docker", "compose", "config", "--services"),
    "audit_total": (
        "docker", "compose", "exec", "-T", "postgres", "psql", "-X", "-v",
        "ON_ERROR_STOP=1", "-U", "cashflow", "-d", "cashflow_transactions",
        "-tAc", "SELECT count(*) FROM transaction_audits"
    ),
    "older_than_365_days": (
        "docker", "compose", "exec", "-T", "postgres", "psql", "-X", "-v",
        "ON_ERROR_STOP=1", "-U", "cashflow", "-d", "cashflow_transactions",
        "-tAc", "SELECT count(*) FROM transaction_audits WHERE occurred_at_utc < now() - interval '365 days'"
    ),
    "missing_audits": (
        "docker", "compose", "exec", "-T", "postgres", "psql", "-X", "-v",
        "ON_ERROR_STOP=1", "-U", "cashflow", "-d", "cashflow_transactions",
        "-tAc", "SELECT count(*) FROM transactions t WHERE NOT EXISTS (SELECT 1 FROM transaction_audits a WHERE a.transaction_id=t.id AND a.merchant_id=t.merchant_id AND a.actor_id=t.actor_id AND a.action='CREATED')"
    ),
    "orphan_audits": (
        "docker", "compose", "exec", "-T", "postgres", "psql", "-X", "-v",
        "ON_ERROR_STOP=1", "-U", "cashflow", "-d", "cashflow_transactions",
        "-tAc", "SELECT count(*) FROM transaction_audits a WHERE NOT EXISTS (SELECT 1 FROM transactions t WHERE t.id=a.transaction_id AND t.merchant_id=a.merchant_id AND t.actor_id=a.actor_id)"
    ),
    "missing_correlation": (
        "docker", "compose", "exec", "-T", "postgres", "psql", "-X", "-v",
        "ON_ERROR_STOP=1", "-U", "cashflow", "-d", "cashflow_transactions",
        "-tAc", "SELECT count(*) FROM transaction_audits WHERE correlation_id IS NULL OR btrim(correlation_id)=''"
    ),
}


def command(name):
    # Only literal, allowlisted commands. No user input becomes a subprocess argument.
    return subprocess.run(
        COMMANDS[name], capture_output=True, text=True, check=False, timeout=45
    )


def audit_inventory(execute=False):
    report = {
        "executed": execute, "status": "NOT_EXECUTED",
        "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "retention_policy_days": 365,
    }
    if not execute:
        return report
    services = command("services")
    expected = {"postgres", "transactions", "consolidation", "keycloak", "rabbitmq", "web"}
    if services.returncode != 0 or not expected.issubset(set(services.stdout.splitlines())):
        raise RuntimeError("Unexpected local Docker Compose services; refusing inspection")
    values = {}
    for name in COMMANDS:
        if name == "services":
            continue
        result = command(name)
        if result.returncode:
            raise RuntimeError("PostgreSQL audit query failed: " + name)
        try:
            count = int(result.stdout.strip())
        except ValueError as error:
            raise RuntimeError("Unexpected audit count: " + name) from error
        if count < 0:
            raise RuntimeError("Negative audit count: " + name)
        values[name] = count
    report.update(values)
    if values["missing_audits"] or values["orphan_audits"] or values["missing_correlation"]:
        report["status"] = "INTEGRITY_FAILED"
    elif values["older_than_365_days"]:
        report["status"] = "RETENTION_REVIEW_REQUIRED"
    else:
        report["status"] = "INTEGRITY_CHECK_PASSED"
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true",
                        help="Explicitly authorize read-only queries against the local database")
    args = parser.parse_args(argv)
    try:
        report = audit_inventory(args.execute)
    except (RuntimeError, subprocess.TimeoutExpired, OSError) as error:
        print("Audit inventory failed: " + str(error), file=sys.stderr)
        return 1
    print(json.dumps(report, indent=2))
    return 0 if report["status"] in ("NOT_EXECUTED", "INTEGRITY_CHECK_PASSED") else 1


if __name__ == "__main__":
    sys.exit(main())
