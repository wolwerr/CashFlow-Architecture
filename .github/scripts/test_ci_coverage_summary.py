"""Offline tests for deterministic coverage summaries, no build or network."""
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ci_coverage_summary as coverage


class CoverageSummaryTests(unittest.TestCase):
    def test_opencover_uses_measured_sequence_points(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "coverage.xml"
            report.write_text(
                '<CoverageSession><Summary numSequencePoints="20" '
                'visitedSequencePoints="17"/></CoverageSession>',
                encoding="utf-8")
            self.assertEqual((17, 20), coverage.parse_opencover(report))
            self.assertEqual("85.00%", coverage.ratio(17, 20))

    def test_lcov_aggregates_lines_across_files(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "lcov.info"
            report.write_text(
                "SF:a.ts\nLF:20\nLH:17\nend_of_record\n"
                "SF:b.ts\nLF:10\nLH:8\nend_of_record\n",
                encoding="utf-8")
            self.assertEqual((25, 30), coverage.parse_lcov(report))

    def test_empty_lcov_is_not_reported_as_zero_percent(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "lcov.info"
            report.write_text("", encoding="utf-8")
            with self.assertRaises(ValueError):
                coverage.parse_lcov(report)

    def test_missing_opencover_summary_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "coverage.xml"
            report.write_text("<CoverageSession/>", encoding="utf-8")
            with self.assertRaises(ValueError):
                coverage.parse_opencover(report)


if __name__ == "__main__":
    unittest.main()
