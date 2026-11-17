#!/usr/bin/env python3
"""Publish real .NET TRX test totals to the GitHub Actions job summary."""
from pathlib import Path
import os
import sys
import xml.etree.ElementTree as ET

PROJECTS = (
    "CashFlow.ArchitectureTests",
    "CashFlow.Transactions.UnitTests",
    "CashFlow.Transactions.ApiTests",
    "CashFlow.Transactions.IntegrationTests",
    "CashFlow.Consolidation.UnitTests",
    "CashFlow.Consolidation.IntegrationTests",
    "CashFlow.Consolidation.ApiTests",
)


def summarize(root: Path) -> tuple[str, bool]:
    rows = []
    totals = {"passed": 0, "failed": 0, "skipped": 0, "total": 0}
    missing = []
    for project in PROJECTS:
        path = root / project / "TestResults" / "ci-results.trx"
        if not path.is_file():
            missing.append(project)
            rows.append(f"| {project} | ? | ? | ? | Missing TRX |")
            continue
        document = ET.parse(path).getroot()
        counters = document.find(".//{*}Counters")
        if counters is None:
            missing.append(project)
            rows.append(f"| {project} | ? | ? | ? | Missing counters |")
            continue
        passed = int(counters.get("passed", "0"))
        failed = int(counters.get("failed", "0"))
        total = int(counters.get("total", "0"))
        skipped = total - passed - failed
        for key, value in (("passed", passed), ("failed", failed), ("skipped", skipped), ("total", total)):
            totals[key] += value
        label = "✅ Passed" if failed == 0 and skipped == 0 else "❌ Failed" if failed else "⚠️ Skipped"
        rows.append(f"| {project} | {passed} | {failed} | {skipped} | {label} |")
    lines = [
        "## Backend .NET Test Report",
        "",
        "### Summary",
        "",
        f"- **Test Projects:** {len(PROJECTS) - len(missing)}/{len(PROJECTS)} with TRX reports",
        f"- **Test Results:** ✅ **{totals['passed']} passed** · {totals['failed']} failed · {totals['skipped']} skipped · {totals['total']} total",
        "",
        "### Results by project",
        "",
        "| Project | Passed | Failed | Skipped | Status |",
        "| --- | ---: | ---: | ---: | --- |",
        *rows,
        "",
        "**Source:** TRX files produced by this GitHub Actions execution, not manually entered counts.",
    ]
    if missing:
        lines.extend(("", "**Missing or incomplete reports:** " + ", ".join(missing)))
    return "\n".join(lines) + "\n", bool(missing)


def main() -> int:
    try:
        summary, missing = summarize(Path("Backend/tests"))
    except (ET.ParseError, ValueError, OSError) as error:
        print(f"Cannot parse backend TRX reports: {error}", file=sys.stderr)
        return 1
    output = os.environ.get("GITHUB_STEP_SUMMARY")
    if output:
        with open(output, "a", encoding="utf-8") as destination:
            destination.write(summary)
    print(summary)
    return 1 if missing else 0


if __name__ == "__main__":
    raise SystemExit(main())
