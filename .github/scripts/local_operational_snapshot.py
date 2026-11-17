#!/usr/bin/env python3
"""Opt-in, read-only CashFlow local operational snapshot.

Never starts/stops containers, never publishes/consumes messages or writes
to any database. By default outputs preview without contacting Docker.
"""
import argparse
import datetime as dt
import json
import subprocess
import sys

EXPECTED = frozenset({"postgres", "rabbitmq", "keycloak", "transactions", "consolidation", "web"})
COMMANDS = {
    "configured": ("docker", "compose", "config", "--services"),
    "running": ("docker", "compose", "ps", "--status", "running", "--services"),
    "queues": ("docker", "compose", "exec", "-T", "rabbitmq", "rabbitmqctl",
               "-q", "list_queues", "name", "messages_ready", "messages_unacknowledged"),
}


class SnapshotError(Exception):
    pass


def command(name):
    try:
        return subprocess.run(COMMANDS[name], capture_output=True,
                              text=True, shell=False, check=False, timeout=30)
    except (FileNotFoundError, subprocess.TimeoutExpired) as error:
        raise SnapshotError("Local Docker Compose command unavailable") from error


def checked_lines(name):
    result = command(name)
    if result.returncode != 0:
        raise SnapshotError(f"Read-only {name} command failed")
    return result.stdout.splitlines()


def queue_totals(lines):
    ready = unacknowledged = count = 0
    for line in lines:
        parts = line.strip().split()
        if not parts or (len(parts) == 3 and parts[0] == "name"):
            continue
        if len(parts) != 3 or not parts[1].isdigit() or not parts[2].isdigit():
            raise SnapshotError("RabbitMQ queue metrics are incomplete")
        count += 1
        ready += int(parts[1])
        unacknowledged += int(parts[2])
    return {"queue_count": count, "messages_ready": ready,
            "messages_unacknowledged": unacknowledged}


def snapshot():
    configured = set(checked_lines("configured"))
    if not EXPECTED.issubset(configured):
        raise SnapshotError("Not the expected CashFlow local Compose project")
    running = set(checked_lines("running"))
    status = {name: name in running for name in sorted(EXPECTED)}
    if not status["rabbitmq"]:
        raise SnapshotError("RabbitMQ is not running; cannot read queue depths")
    totals = queue_totals(checked_lines("queues"))
    return {
        "executed": True,
        "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "services_running": status,
        **totals,
        "outcome": "OBSERVED" if all(status.values()) and totals["queue_count"] > 0 else "DEGRADED",
        "limits": "Container running does not prove readiness; queue depth does not prove financial consistency."
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true", help="Query local Compose without changing it")
    args = parser.parse_args(argv)
    if not args.execute:
        print(json.dumps({"executed": False, "mode": "preview",
                          "checks": ["configured services", "running services", "RabbitMQ queue depth"]}))
        return 0
    try:
        report = snapshot()
    except SnapshotError as error:
        print(json.dumps({"executed": True, "outcome": "ERROR",
                          "error": str(error)}), file=sys.stderr)
        return 1
    print(json.dumps(report, indent=2))
    return 0 if report["outcome"] == "OBSERVED" else 1


if __name__ == "__main__":
    sys.exit(main())
