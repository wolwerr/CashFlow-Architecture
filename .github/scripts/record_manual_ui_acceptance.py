#!/usr/bin/env python3
"""Record explicitly confirmed, manual LOCAL browser UI acceptance.

Never calls browser, Docker or remote APIs; never marks checks as passed without
the operator supplying an affirmative, explicit confirmation flag.
"""
import argparse
import datetime as dt
import json
from pathlib import Path

MANIFEST_FILE = Path("local-acceptance.json")
EVIDENCE_FILES = {
    "browser_oidc": Path("evidence/browser-oidc-manual.json"),
    "merchant_management": Path("evidence/merchant-management-manual.json"),
}


def validate_manifest_argument(raw):
    """Fail closed: never use a CLI-derived file path for reads or writes."""
    if str(raw) != "local-acceptance.json":
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


def record():
    manifest = json.loads(MANIFEST_FILE.read_text(encoding="utf-8-sig"))
    from local_mvp_acceptance import validate_metadata
    validate_metadata(manifest)
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    for scenario, checks in CHECKS.items():
        destination = EVIDENCE_FILES[scenario]
        ref = destination.as_posix()
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(json.dumps({
            "scenario": scenario,
            "environment": "LOCAL",
            "confirmed_by": "operator",
            "confirmation_timestamp_utc": now,
            "source": "Manual seven-step browser checklist reported passed on 2026-11-17",
            "checks": {name: True for name in checks},
            "outcome": "PASS",
            "independently_automated": False,
        }, indent=2) + "\n", encoding="utf-8")
        manifest["scenarios"][scenario] = {
            "status": "PASS", "evidence": [ref], "metrics": {}
        }
    MANIFEST_FILE.write_text(json.dumps(manifest, indent=2) + "\n",
                             encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=Path("local-acceptance.json"))
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
    record()
    print("Recorded two operator-confirmed UI scenarios in local acceptance manifest.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
