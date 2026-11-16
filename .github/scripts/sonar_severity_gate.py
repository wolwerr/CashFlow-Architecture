#!/usr/bin/env python3
"""Enforce CashFlow's CI policy from the SonarQube Cloud analysis that just finished.

The Sonar way gate stays unchanged in SonarQube Cloud. In CI, block on new
HIGH/BLOCKER impacts (or legacy CRITICAL/BLOCKER issues), plus all failing
non-issue-rating quality-gate conditions (such as new coverage / duplication).
"""
import json
import os
from pathlib import Path
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

API_BASE = "https://sonarcloud.io"
BLOCKING_IMPACTS = {"HIGH", "BLOCKER"}
BLOCKING_LEGACY = {"CRITICAL", "BLOCKER"}
IGNORED_RATING_METRICS = {
    "new_security_rating", "new_reliability_rating", "new_maintainability_rating",
    "new_sqale_rating", "new_violations",
}


def api(path, params=None):
    token = os.environ["SONAR_TOKEN"]
    url = API_BASE + path
    if params:
        url += "?" + urlencode(params)
    req = Request(url, headers={"Authorization": "Bearer " + token, "Accept": "application/json"})
    try:
        with urlopen(req, timeout=30) as response:
            return json.load(response)
    except (HTTPError, URLError, ValueError) as exc:
        raise RuntimeError(f"Sonar API request failed ({path}): {exc}") from exc


def report_task():
    candidates = list(Path(".sonarqube").rglob("report-task.txt")) if Path(".sonarqube").exists() else []
    candidates += list(Path(".scannerwork").rglob("report-task.txt")) if Path(".scannerwork").exists() else []
    if len(candidates) != 1:
        raise RuntimeError(f"Expected one Sonar report-task.txt, found {len(candidates)}")
    props = {}
    for line in candidates[0].read_text(encoding="utf-8").splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            props[key] = value
    if not props.get("ceTaskId"):
        raise RuntimeError("Sonar report-task.txt does not contain ceTaskId")
    return props


def wait_for_analysis(task_id):
    for _ in range(100):
        task = api("/api/ce/task", {"id": task_id})["task"]
        status = task["status"]
        if status == "SUCCESS":
            if not task.get("analysisId"):
                raise RuntimeError("Sonar CE task succeeded but has no analysisId")
            return task["analysisId"]
        if status in {"FAILED", "CANCELED"}:
            raise RuntimeError(f"Sonar server-side analysis {status}")
        time.sleep(3)
    raise RuntimeError("Timed out waiting for Sonar server-side analysis")


def is_blocking(issue):
    impacts = issue.get("impacts")
    if isinstance(impacts, list) and impacts:
        return any(i.get("severity") in BLOCKING_IMPACTS for i in impacts)
    # Legacy severity uses CRITICAL for what MQR calls HIGH.
    return issue.get("severity") in BLOCKING_LEGACY


def fetch_issues(project_key, pull_request, branch):
    result = []
    page = 1
    while True:
        params = {"componentKeys": project_key, "resolved": "false", "ps": "500", "p": str(page)}
        if pull_request:
            params["pullRequest"] = pull_request
        else:
            params["branch"] = branch
            params["inNewCodePeriod"] = "true"
        payload = api("/api/issues/search", params)
        issues = payload.get("issues")
        total = payload.get("total")
        if not isinstance(issues, list) or not isinstance(total, int):
            raise RuntimeError("Unexpected Sonar issues search response; refusing to pass")
        result.extend(issues)
        if len(result) >= total:
            return result
        if not issues or page >= 20:  # Sonar search APIs limit result windows to ~10k entries.
            raise RuntimeError("Sonar issues pagination incomplete; refusing to pass")
        page += 1


def failing_non_rating_conditions(payload):
    status = payload.get("projectStatus")
    if not isinstance(status, dict):
        raise RuntimeError("Unexpected Sonar quality gate response")
    conditions = status.get("conditions")
    if not isinstance(conditions, list):
        raise RuntimeError("Missing Sonar quality gate conditions")
    # Do not ignore test coverage, duplicated lines, security hotspot reviews,
    # or other non-rating checks. Only ratings and total issue count are relaxed.
    return [
        c for c in conditions
        if c.get("status") == "ERROR"
        and c.get("metricKey") not in IGNORED_RATING_METRICS
    ]


def main():
    task = report_task()
    analysis_id = wait_for_analysis(task["ceTaskId"])
    project = os.environ["SONAR_PROJECT_KEY"]
    pr_number = os.getenv("SONAR_PR_NUMBER", "").strip()
    branch = os.getenv("GITHUB_REF_NAME", "main")
    status = api("/api/qualitygates/project_status", {"analysisId": analysis_id})
    other_failures = failing_non_rating_conditions(status)
    issues = fetch_issues(project, pr_number, branch)
    blocking = [issue for issue in issues if is_blocking(issue)]
    nonblocking = len(issues) - len(blocking)

    print(f"Sonar analysis {analysis_id}: {len(blocking)} High/Blocker (Critical in legacy mode) issue(s); "
          f"{nonblocking} lower-severity issue(s) kept as non-blocking warnings.")
    for issue in blocking[:30]:
        impacts = ",".join(i.get("severity", "?") for i in issue.get("impacts", []))
        print(f"BLOCK: {issue.get('component')}:{issue.get('line', '?')} "
              f"[{impacts or issue.get('severity', '?')}] {issue.get('message')}")
    for condition in other_failures:
        print(f"BLOCK: Sonar metric {condition.get('metricKey')} = "
              f"{condition.get('actualValue', '?')} (threshold {condition.get('errorThreshold', '?')})")
    if blocking or other_failures:
        print("CashFlow CI quality policy: FAILED")
        return 1
    print("CashFlow CI quality policy: PASSED (Medium/Low/Info issues do not block)")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (KeyError, RuntimeError, OSError, ValueError) as exc:
        print(f"CashFlow CI quality policy: FAILED CLOSED: {exc}", file=sys.stderr)
        sys.exit(2)
