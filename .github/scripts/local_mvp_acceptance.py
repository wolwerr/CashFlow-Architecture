#!/usr/bin/env python3
"""CashFlow Trilha 08: generate and validate a LOCAL acceptance report.

Offline and read-only. Never calls Docker or a network endpoint and never
creates a PASS on behalf of an unexecuted scenario.
"""
import argparse
import datetime as dt
import json
from pathlib import Path
import re
import sys

SCENARIOS = (
    "compose_readiness", "browser_oidc", "merchant_management",
    "merchant_isolation", "financial_idempotency", "outbox_projection",
    "keycloak_fail_closed", "rabbitmq_recovery", "consumer_recovery",
    "postgres_restore", "audit_integrity", "observability",
    "load_50rps_300s", "projection_slo",
)
STATUSES = frozenset({"NOT_RUN", "PASS", "FAIL", "BLOCKED"})
REF_PATTERN = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9_./-]{2,160}$")
METRIC_RULES = {
    "load_50rps_300s": {
        "duration_seconds": (lambda n: n >= 300),
        "request_rate_rps": (lambda n: n >= 50),
        "p95_ms": (lambda n: n < 500),
        "failure_rate_pct": (lambda n: n <= 5),
    },
    "projection_slo": {
        "sample_count": (lambda n: n >= 20),
        "within_30s_pct": (lambda n: n >= 95),
    },
}


class InvalidEvidence(ValueError):
    pass


def initial_manifest():
    return {
        "schema_version": 1,
        "environment": "LOCAL",
        "commit": "NOT_RECORDED",
        "scenarios": {
            name: {"status": "NOT_RUN", "evidence": [], "metrics": {}}
            for name in SCENARIOS
        },
    }


def validate_metadata(manifest):
    if not isinstance(manifest, dict) or manifest.get("schema_version") != 1:
        raise InvalidEvidence("Unsupported acceptance manifest schema")
    if manifest.get("environment") != "LOCAL":
        raise InvalidEvidence("Only LOCAL acceptance is supported")
    commit = manifest.get("commit")
    if commit != "NOT_RECORDED" and not (
        isinstance(commit, str) and re.fullmatch(r"[0-9a-f]{40}", commit)):
        raise InvalidEvidence("Commit must be a full Git SHA or NOT_RECORDED")
    data = manifest.get("scenarios")
    if not isinstance(data, dict) or set(data) != set(SCENARIOS):
        raise InvalidEvidence("Acceptance scenarios are missing or unexpected")
    return data


def validate_scenario(name, record):
    if not isinstance(record, dict) or set(record) != {"status", "evidence", "metrics"}:
        raise InvalidEvidence(f"{name}: invalid evidence record")
    status, evidence, metrics = record["status"], record["evidence"], record["metrics"]
    if status not in STATUSES or not isinstance(evidence, list) or not isinstance(metrics, dict):
        raise InvalidEvidence(f"{name}: invalid status or evidence")
    if not all(isinstance(ref, str) and REF_PATTERN.fullmatch(ref) and
               not ref.startswith("/") and ".." not in ref.split("/") for ref in evidence):
        raise InvalidEvidence(f"{name}: evidence must use relative, sanitized filenames")
    if status == "PASS" and not evidence:
        raise InvalidEvidence(f"{name}: PASS requires an evidence file reference")
    if status == "PASS" and name in METRIC_RULES:
        validate_metrics(name, metrics)
    if status == "PASS" and not metrics and name in METRIC_RULES:
        raise InvalidEvidence(f"{name}: PASS requires measured metrics")


def validate_metrics(name, metrics):
    rules = METRIC_RULES[name]
    if set(metrics) != set(rules):
        raise InvalidEvidence(f"{name}: measured metric fields are incomplete")
    for key, check in rules.items():
        value = metrics[key]
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not check(value):
            raise InvalidEvidence(f"{name}: {key} fails acceptance threshold")


def report(manifest, evidence_root=None):
    data = validate_metadata(manifest)
    for name, record in data.items():
        validate_scenario(name, record)
        if evidence_root is not None and record["status"] == "PASS":
            for ref in record["evidence"]:
                target = (evidence_root / ref).resolve()
                if not target.is_relative_to(evidence_root.resolve()) or not target.is_file():
                    raise InvalidEvidence(f"{name}: evidence file missing or outside evidence root")
    counts = {status: sum(item["status"] == status for item in data.values())
              for status in sorted(STATUSES)}
    ready = counts["PASS"] == len(SCENARIOS) and manifest["commit"] != "NOT_RECORDED"
    return {
        "schema_version": 1,
        "environment": "LOCAL",
        "commit": manifest["commit"],
        "generated_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "ready_for_mvp_acceptance": ready,
        "outcome": "ACCEPTED" if ready else "NOT_ACCEPTED",
        "counts": counts,
        "scenarios": {name: item["status"] for name, item in data.items()},
        "note": "Evidence references are existing evidence filenames and operator declarations; content is not independently validated.",
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--init", metavar="PATH", type=Path,
                        help="Create a NOT_RUN template; refuses to overwrite existing files")
    parser.add_argument("--manifest", type=Path,
                        help="Read operator-supplied JSON without executing any live probe")
    args = parser.parse_args(argv)
    if bool(args.init) == bool(args.manifest):
        parser.error("Choose exactly one of --init or --manifest")
    try:
        if args.init:
            with args.init.open("x", encoding="utf-8") as output:
                json.dump(initial_manifest(), output, indent=2)
                output.write("\n")
            print("Created NOT_RUN acceptance template")
            return 0
        with args.manifest.open(encoding="utf-8") as source:
            result = report(json.load(source), args.manifest.parent)
    except (OSError, json.JSONDecodeError, InvalidEvidence) as error:
        print(f"Acceptance report failed: {type(error).__name__}", file=sys.stderr)
        return 2
    print(json.dumps(result, indent=2))
    return 0 if result["ready_for_mvp_acceptance"] else 1


if __name__ == "__main__":
    sys.exit(main())
