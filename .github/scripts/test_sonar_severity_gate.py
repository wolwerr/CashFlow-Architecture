import unittest

from sonar_severity_gate import is_blocking, failing_non_rating_conditions


class SonarSeverityGateTests(unittest.TestCase):
    def test_high_impact_blocks_even_if_legacy_severity_differs(self):
        self.assertTrue(is_blocking({"severity": "MAJOR", "impacts": [{"softwareQuality": "SECURITY", "severity": "HIGH"}]}))

    def test_blocker_impact_blocks(self):
        self.assertTrue(is_blocking({"impacts": [{"severity": "BLOCKER"}]}))

    def test_medium_low_info_do_not_block(self):
        for severity in ("MEDIUM", "LOW", "INFO"):
            with self.subTest(severity=severity):
                self.assertFalse(is_blocking({"impacts": [{"severity": severity}], "severity": "CRITICAL"}))

    def test_legacy_critical_and_blocker_block(self):
        self.assertTrue(is_blocking({"severity": "CRITICAL"}))
        self.assertTrue(is_blocking({"severity": "BLOCKER"}))
        self.assertFalse(is_blocking({"severity": "MAJOR"}))

    def test_preserve_coverage_duplication_hotspots(self):
        data = {"projectStatus": {"conditions": [
            {"metricKey": "new_security_rating", "status": "ERROR"},
            {"metricKey": "new_maintainability_rating", "status": "ERROR"},
            {"metricKey": "new_reliability_rating", "status": "ERROR"},
            {"metricKey": "new_coverage", "status": "ERROR"},
            {"metricKey": "new_duplicated_lines_density", "status": "ERROR"},
            {"metricKey": "new_security_hotspots_reviewed", "status": "ERROR"},
            {"metricKey": "new_violations", "status": "ERROR"},
        ]}}
        failures = failing_non_rating_conditions(data)
        self.assertEqual(
            ["new_coverage", "new_duplicated_lines_density", "new_security_hotspots_reviewed"],
            [c["metricKey"] for c in failures],
        )

    def test_invalid_gate_response_fails_closed(self):
        with self.assertRaises(RuntimeError):
            failing_non_rating_conditions({"projectStatus": {}})


if __name__ == "__main__":
    unittest.main()
