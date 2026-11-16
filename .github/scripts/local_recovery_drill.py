#!/usr/bin/env python3
"""Local-only, opt-in Docker Compose recovery drill.

The default mode prints the plan; --execute stops one allowed service and
restores it in a finally block. No credentials, customer data, or cloud calls.
"""
import argparse
import datetime as dt
import json
import subprocess
import sys
import time

ALLOWED_SERVICES = ("rabbitmq", "consolidation", "keycloak")


# Docker receives only literal commands defined here; CLI inputs are never arguments.
DOCKER_COMMANDS = {
    "services": ("docker", "compose", "config", "--services"),
    "rabbitmq_running": ("docker", "compose", "ps", "--status", "running", "--services", "rabbitmq"),
    "consolidation_running": ("docker", "compose", "ps", "--status", "running", "--services", "consolidation"),
    "keycloak_running": ("docker", "compose", "ps", "--status", "running", "--services", "keycloak"),
    "rabbitmq_stop": ("docker", "compose", "stop", "rabbitmq"),
    "consolidation_stop": ("docker", "compose", "stop", "consolidation"),
    "keycloak_stop": ("docker", "compose", "stop", "keycloak"),
    "rabbitmq_restore": ("docker", "compose", "up", "-d", "rabbitmq"),
    "consolidation_restore": ("docker", "compose", "up", "-d", "consolidation"),
    "keycloak_restore": ("docker", "compose", "up", "-d", "keycloak"),
}


def compose(action):
    return subprocess.run(
        DOCKER_COMMANDS[action], shell=False,
        capture_output=True, text=True, check=False, timeout=90,
    )

def verify_local_project():
    config = compose("services")
    if config.returncode != 0:
        raise RuntimeError("Docker Compose config is unavailable")
    services = set(config.stdout.splitlines())
    expected = {"postgres", "rabbitmq", "keycloak", "transactions", "consolidation", "web"}
    if not expected.issubset(services):
        raise RuntimeError("This directory is not the expected CashFlow local Compose project")
    return services


def get_service_state(service):
    result = compose(service + "_running")
    if result.returncode != 0:
        raise RuntimeError("Could not inspect Docker Compose service")
    return service in result.stdout.splitlines()


def perform_drill(service, outage_seconds, execute):
    if service not in ALLOWED_SERVICES:
        raise ValueError("Service is not allowed for this drill")
    report = {
        "scenario": service + "-restart",
        "executed": execute,
        "outage_seconds_requested": outage_seconds,
        "captured_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "initially_running": None,
        "stop_succeeded": False,
        "restore_succeeded": False,
        "running_after_restore": False,
        "outcome": "NOT_EXECUTED",
    }
    if not execute:
        return report

    verify_local_project()
    report["initially_running"] = get_service_state(service)
    if not report["initially_running"]:
        raise RuntimeError("Target service is not running; refusing to modify its state")

    stop_attempted = False
    try:
        # Mark before invoking stop: recovery must be attempted even on timeout.
        stop_attempted = True
        stopped = compose(service + "_stop")
        report["stop_succeeded"] = stopped.returncode == 0
        if not report["stop_succeeded"]:
            raise RuntimeError("Docker Compose stop failed")
        time.sleep(outage_seconds)
    finally:
        if stop_attempted:
            restored = compose(service + "_restore")
            report["restore_succeeded"] = restored.returncode == 0
            if report["restore_succeeded"]:
                report["running_after_restore"] = get_service_state(service)

    report["outcome"] = (
        "RESTORED" if report["stop_succeeded"] and report["running_after_restore"]
        else "RESTORATION_INCOMPLETE"
    )
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--service", choices=ALLOWED_SERVICES, required=True)
    parser.add_argument("--outage-seconds", type=int, default=15)
    parser.add_argument("--execute", action="store_true",
                        help="Explicitly authorize disruption of the selected local service")
    args = parser.parse_args(argv)
    if not 1 <= args.outage_seconds <= 120:
        parser.error("Outage must be between 1 and 120 seconds")
    try:
        result = perform_drill(args.service, args.outage_seconds, args.execute)
    except (RuntimeError, subprocess.TimeoutExpired) as error:
        print("Recovery drill failed: " + str(error), file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2))
    return 0 if not args.execute or result["outcome"] == "RESTORED" else 1


if __name__ == "__main__":
    sys.exit(main())
