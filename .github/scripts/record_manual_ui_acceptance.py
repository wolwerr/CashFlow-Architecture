#!/usr/bin/env python3
"""Record explicitly confirmed, manual LOCAL browser UI acceptance.

Never calls browser, Docker or remote APIs; never marks checks as passed without
the operator supplying an affirmative, explicit confirmation flag.
"""
import argparse
import datetime as dt
import json
from pathlib import Path

MANIFEST_NAME = "local-acceptance.json"
MANIFEST_FILE = Path(MANIFEST_NAME)


def validate_manifest_argument(raw):
    """Fail closed: never use a CLI-derived file path for reads or writes."""
    if str(raw) != MANIFEST_NAME:
        raise ValueError("Use only local-acceptance.json in the current working directory")
    if MANIFEST_FILE.is_symlink() or not MANIFEST_FILE.is_file():
        raise ValueError("Expected a regular local acceptance manifest")
    evidence_dir = Path("evidence")
    if evidence_dir.is_symlink():
        raise ValueError("Evidence directory must not be a symlink")


CHECKS = {
    "browser_oidc": [
        "keycloak_login_redirect",
        "correct_user_and_role",
        "logout_requires_authentication",
    ],
    "merchant_management": [
        "merchant_listing",
        "disposable_merchant_creation",
        "disposable_merchant_update",
        "soft_delete_disables_normal_access",
    ],
}


def evidence_json(scenario, checks, timestamp):
    """Generate report content only. No untrusted data determines file destinations."""
    return json.dumps({
        "scenario": scenario,
        "environment": "LOCAL",
        "confirmed_by": "operator",
        "confirmation_timestamp_utc": timestamp,
        "source": "Manual seven-step browser checklist reported passed on 2026-11-17",
        "checks": {name: True for name in checks},
        "outcome": "PASS",
        "independently_automated": False,
    }, indent=2) + "\n"


def record():
    manifest = json.loads(MANIFEST_FILE.read_text(encoding="utf-8-sig"))
    from local_mvp_acceptance import validate_metadata
    validate_metadata(manifest)
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    evidence_dir = Path("evidence")
    evidence_dir.mkdir(parents=True, exist_ok=True)
    browser_file = Path("evidence/browser-oidc-manual.json")
    merchant_file = Path("evidence/merchant-management-manual.json")
    if browser_file.is_symlink() or merchant_file.is_symlink():
        raise ValueError("Evidence files must not be symlinks")
    browser_file.write_text(
        evidence_json("browser_oidc", CHECKS["browser_oidc"], now), encoding="utf-8")
    merchant_file.write_text(
        evidence_json("merchant_management", CHECKS["merchant_management"], now),
        encoding="utf-8")
    manifest["scenarios"]["browser_oidc"] = {
        "status": "PASS", "evidence": ["evidence/browser-oidc-manual.json"], "metrics": {}
    }
    manifest["scenarios"]["merchant_management"] = {
        "status": "PASS", "evidence": ["evidence/merchant-management-manual.json"],
        "metrics": {}
    }
    MANIFEST_FILE.write_text(json.dumps(manifest, indent=2) + "\n",
                             encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=MANIFEST_FILE)
    parser.add_argument("--confirm-seven-checks", action="store_true",
                        help="Affirm all seven UI checks were actually completed successfully")
    args = parser.parse_args(argv)
    if not args.confirm_seven_checks:
        print("No manifest changes. Use --confirm-seven-checks only after successful manual UI test.")
        return 0
    try:
        validate_manifest_argument(args.manifest)
    except ValueError as error:
        parser.error(str(error))
    try:
        record()
    except ValueError as error:
        parser.error(str(error))
    print("Recorded two operator-confirmed UI scenarios in local acceptance manifest.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
