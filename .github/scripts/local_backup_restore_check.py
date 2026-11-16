#!/usr/bin/env python3
"""Verify a PostgreSQL backup by restoring into a disposable local database.

Explicit opt-in only. Never modifies or drops a CashFlow application database.
Backups are held in a temporary anonymous file and deleted on process exit.
"""
import argparse
import datetime as dt
import json
import subprocess
import sys
import tempfile
import time

# Literal commands only: no external string enters subprocess argument arrays.
COMMANDS = {
    "services": ("docker", "compose", "config", "--services"),
    "transactions_exists": ("docker", "compose", "exec", "-T", "postgres", "psql", "-U", "cashflow", "-d", "postgres", "-tAc", "SELECT 1 FROM pg_database WHERE datname = 'cashflow_transactions_restore_check'"),
    "consolidation_exists": ("docker", "compose", "exec", "-T", "postgres", "psql", "-U", "cashflow", "-d", "postgres", "-tAc", "SELECT 1 FROM pg_database WHERE datname = 'cashflow_consolidation_restore_check'"),
    "transactions_dump": ("docker", "compose", "exec", "-T", "postgres", "pg_dump", "-U", "cashflow", "-Fc", "-d", "cashflow_transactions"),
    "consolidation_dump": ("docker", "compose", "exec", "-T", "postgres", "pg_dump", "-U", "cashflow", "-Fc", "-d", "cashflow_consolidation"),
    "transactions_create": ("docker", "compose", "exec", "-T", "postgres", "createdb", "-U", "cashflow", "cashflow_transactions_restore_check"),
    "consolidation_create": ("docker", "compose", "exec", "-T", "postgres", "createdb", "-U", "cashflow", "cashflow_consolidation_restore_check"),
    "transactions_restore": ("docker", "compose", "exec", "-T", "postgres", "pg_restore", "-U", "cashflow", "--exit-on-error", "--no-owner", "--no-privileges", "-d", "cashflow_transactions_restore_check"),
    "consolidation_restore": ("docker", "compose", "exec", "-T", "postgres", "pg_restore", "-U", "cashflow", "--exit-on-error", "--no-owner", "--no-privileges", "-d", "cashflow_consolidation_restore_check"),
    "transactions_tables": ("docker", "compose", "exec", "-T", "postgres", "psql", "-U", "cashflow", "-d", "cashflow_transactions_restore_check", "-tAc", "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'"),
    "consolidation_tables": ("docker", "compose", "exec", "-T", "postgres", "psql", "-U", "cashflow", "-d", "cashflow_consolidation_restore_check", "-tAc", "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'"),
    "transactions_drop": ("docker", "compose", "exec", "-T", "postgres", "dropdb", "-U", "cashflow", "cashflow_transactions_restore_check"),
    "consolidation_drop": ("docker", "compose", "exec", "-T", "postgres", "dropdb", "-U", "cashflow", "cashflow_consolidation_restore_check"),
}
ALLOWED_DATABASES = ("transactions", "consolidation")


def command(action, **kwargs):
    return subprocess.run(
        COMMANDS[action], check=False, timeout=120, capture_output=True, **kwargs
    )


def ensure_local_compose():
    result = command("services")
    required = {b"postgres", b"transactions", b"consolidation", b"web", b"rabbitmq", b"keycloak"}
    if result.returncode or not required.issubset(set(result.stdout.splitlines())):
        raise RuntimeError("Unexpected Docker Compose project; no changes made")


def verify_backup(database, execute=False):
    if database not in ALLOWED_DATABASES:
        raise ValueError("Only the two financial databases can be checked")
    evidence = {
        "database": database, "executed": execute, "status": "NOT_EXECUTED",
        "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "backup_size_bytes": None, "restored_table_count": None,
        "restoration_seconds": None, "cleanup_ok": None,
    }
    if not execute:
        return evidence
    ensure_local_compose()
    # Refuse to touch a previous or user-owned database with this name.
    exists = command(database + "_exists")
    if exists.returncode or exists.stdout.strip():
        raise RuntimeError("Scratch database exists or cannot be checked: refusing to overwrite/drop")
    created = False
    started = time.monotonic()
    with tempfile.TemporaryFile(mode="w+b") as archive:
        # Keep the dump off disk paths and out of logs. stdout is binary pg_dump archive.
        with subprocess.Popen(COMMANDS[database + "_dump"], stdout=archive, stderr=subprocess.PIPE) as process:
            _, errors = process.communicate(timeout=120)
            if process.returncode:
                raise RuntimeError("pg_dump failed (details suppressed to avoid sensitive output)")
        evidence["backup_size_bytes"] = archive.tell()
        if not evidence["backup_size_bytes"]:
            raise RuntimeError("pg_dump returned an empty archive")
        try:
            creation = command(database + "_create")
            if creation.returncode:
                raise RuntimeError("Could not create disposable scratch database")
            created = True
            archive.seek(0)
            with subprocess.Popen(COMMANDS[database + "_restore"], stdin=archive, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE) as process:
                _, errors = process.communicate(timeout=120)
                if process.returncode:
                    raise RuntimeError("pg_restore failed (details suppressed to avoid sensitive output)")
            tables = command(database + "_tables")
            if tables.returncode:
                raise RuntimeError("Could not check restored tables")
            count = int(tables.stdout.strip())
            evidence["restored_table_count"] = count
            if count == 0:
                raise RuntimeError("Restoration did not contain any public tables")
            evidence["status"] = "RESTORE_VERIFIED"
        finally:
            if created:
                cleanup = command(database + "_drop")
                evidence["cleanup_ok"] = cleanup.returncode == 0
                if not evidence["cleanup_ok"]:
                    evidence["status"] = "CLEANUP_FAILED"
                    raise RuntimeError("Scratch database cleanup failed; inspect manually")
    evidence["restoration_seconds"] = round(time.monotonic() - started, 3)
    return evidence


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", choices=ALLOWED_DATABASES, required=True)
    parser.add_argument("--execute", action="store_true",
                        help="Opt in to dump/restore and scratch database lifecycle")
    args = parser.parse_args(argv)
    try:
        report = verify_backup(args.database, args.execute)
    except (RuntimeError, subprocess.TimeoutExpired, OSError, ValueError) as exc:
        print("Backup verification failed: " + str(exc), file=sys.stderr)
        return 1
    print(json.dumps(report, indent=2))
    return 0 if report["status"] in ("NOT_EXECUTED", "RESTORE_VERIFIED") else 1


if __name__ == "__main__":
    sys.exit(main())
