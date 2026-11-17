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


def module_sequence_lines(module):
    """Yield unique source-line identifiers and whether each was visited."""
    name = module.findtext("ModuleName")
    if not name:
        raise ValueError("OpenCover ModuleName missing")
    files = {
        node.attrib["uid"]: node.attrib["fullPath"].replace("\\", "/")
        for node in module.findall("./Files/File")
    }
    for point in module.findall(".//SequencePoints/SequencePoint"):
        source = files.get(point.attrib.get("fileid"))
        if not source:
            raise ValueError("SequencePoint source file missing")
        line = int(point.attrib["sl"])
        visits = int(point.attrib["vc"])
        if line < 1 or visits < 0:
            raise ValueError("Invalid OpenCover sequence point")
        yield (name, source, line), visits > 0


def report_sequence_lines(path):
    """Yield source lines from one report and reject empty instrumented reports."""
    modules = ET.parse(path).getroot().findall("./Modules/Module")
    if not modules:
        raise ValueError("OpenCover report has no Modules")
    count = 0
    for module in modules:
        for entry in module_sequence_lines(module):
            count += 1
            yield entry
    if not count:
        raise ValueError("OpenCover report has no sequence points")


def merge_opencover_line_coverage(paths):
    """Union source lines across suites; count covered when any suite visits."""
    lines = {}
    for path in paths:
        for key, covered in report_sequence_lines(path):
            lines[key] = lines.get(key, False) or covered
    if not lines:
        raise ValueError("No instrumented source lines")
    return sum(lines.values()), len(lines)

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
    paths = [Path("sonar-reports/backend") / filename
             for _, filename in BACKEND_REPORTS]
    covered, total = merge_opencover_line_coverage(paths)
    return ("## Backend .NET Coverage Report\n\n"
            "| Metric | Consolidated result |\n| --- | ---: |\n"
            f"| Covered source lines | {covered} |\n"
            f"| Instrumented source lines | {total} |\n"
            f"| **Overall backend line coverage** | **{ratio(covered, total)}** |\n\n"
            "**Source:** six OpenCover reports produced by this Actions run. "
            "A source line is counted once per assembly and file, and is "
            "considered covered when any test suite executes it. "
            "**This is a union, not an average or a sum of percentages.**\n\n"
            "**Policy:** CI still enforces its existing 80% thresholds on "
            "selected backend layers separately; the overall percentage "
            "is informative and may be lower than 80%.\n")




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
