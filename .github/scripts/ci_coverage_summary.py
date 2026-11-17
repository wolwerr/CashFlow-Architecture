#!/usr/bin/env python3
"""Report measured CI line coverage. Never infer coverage from passing tests."""
import argparse
import os
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

BACKEND_REPORTS = (
    ("Transactions unit", "coverage.unit.opencover.xml"),
    ("Transactions integration", "coverage.integration.opencover.xml"),
    ("Transactions API", "coverage.api.opencover.xml"),
    ("Consolidation unit", "coverage.consolidation-unit.opencover.xml"),
    ("Consolidation integration", "coverage.consolidation-integration.opencover.xml"),
    ("Consolidation API", "coverage.consolidation-api.opencover.xml"),
)


def ratio(hit, total):
    return f"{100 * hit / total:.2f}%" if total else "n/a"


def parse_opencover(path):
    summary = ET.parse(path).getroot().find("Summary")
    if summary is None:
        raise ValueError("OpenCover Summary not found")
    covered = int(summary.attrib["visitedSequencePoints"])
    total = int(summary.attrib["numSequencePoints"])
    if covered > total or total < 0:
        raise ValueError("Invalid OpenCover line counts")
    return covered, total


def parse_lcov(path):
    covered = total = 0
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("LF:"):
            total += int(line[3:])
        elif line.startswith("LH:"):
            covered += int(line[3:])
    if covered > total or total <= 0:
        raise ValueError("Invalid or empty LCOV line counts")
    return covered, total


def backend_summary():
    lines = ["## Backend .NET Coverage Report", "",
             "| OpenCover report | Covered sequence points | Total sequence points | Coverage |",
             "| --- | ---: | ---: | ---: |"]
    for label, filename in BACKEND_REPORTS:
        covered, total = parse_opencover(
            Path("sonar-reports/backend") / filename)
        lines.append(f"| {label} | {covered} | {total} | {ratio(covered, total)} |")
    lines.extend(["",
        "**Source:** six actual OpenCover reports from this run. Percentages reflect "
        "sequence-point coverage **within each individual report**. Reports can "
        "contain overlapping assemblies and must not be added to produce an "
        "application-wide coverage percentage.",
        "**Quality gates:** minimum 80% line coverage is checked separately for "
        "selected backend layers; this table does not imply every project or file "
        "has at least 80%.",
        ""])
    return "\n".join(lines)


def frontend_summary():
    covered, total = parse_lcov(Path("Frontend/coverage/lcov.info"))
    return ("## Frontend Coverage Report\n\n"
            "| Metric | Measured value |\n| --- | ---: |\n"
            f"| Lines covered | {covered} |\n"
            f"| Lines instrumented | {total} |\n"
            f"| Line coverage | **{ratio(covered, total)}** |\n\n"
            "**Source:** actual Vitest LCOV file from this run; not manually "
            "entered README figures. Other coverage measures (branches/functions) "
            "are not included in this line summary.\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("section", choices=("backend", "frontend"))
    args = parser.parse_args()
    try:
        report = backend_summary() if args.section == "backend" else frontend_summary()
    except (OSError, ValueError, ET.ParseError, KeyError) as error:
        print(f"Coverage report unavailable: {type(error).__name__}", file=sys.stderr)
        return 1
    print(report)
    summary_file = os.getenv("GITHUB_STEP_SUMMARY")
    if summary_file:
        with open(summary_file, "a", encoding="utf-8") as destination:
            destination.write(report + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
