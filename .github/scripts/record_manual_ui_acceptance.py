#!/usr/bin/env python3
"""Record explicitly confirmed, manual LOCAL browser UI acceptance.

Never calls browser, Docker or remote APIs; never marks checks as passed without
the operator supplying an affirmative, explicit confirmation flag.
"""
import argparse
import datetime as dt
import json
from pathlib import Path

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


def record(manifest_path):
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    from local_mvp_acceptance import validate_metadata
    validate_metadata(manifest)
    root = manifest_path.parent
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    for scenario, checks in CHECKS.items():
        ref = "evidence/" + scenario.replace("_", "-") + "-manual.json"
        destination = root / ref
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
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n",
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
    if not args.manifest.is_file():
        parser.error("Existing acceptance manifest required")
    record(args.manifest)
    print("Recorded two operator-confirmed UI scenarios in local acceptance manifest.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
