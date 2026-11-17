"""Offline tests for deterministic coverage summaries, no build or network."""
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ci_coverage_summary as coverage


class CoverageSummaryTests(unittest.TestCase):
    @staticmethod
    def opencover_xml(points):
        sequence_points = "".join(
            f'<SequencePoint vc="{visits}" sl="{line}" fileid="1" />'
            for line, visits in points
        )
        return (
            '<CoverageSession><Modules><Module>'
            '<ModuleName>CashFlow.Transactions.Domain</ModuleName>'
            '<Files><File uid="1" fullPath="/repo/Domain/Entry.cs" /></Files>'
            '<Classes><Class><Methods><Method><SequencePoints>'
            + sequence_points +
            '</SequencePoints></Method></Methods></Class></Classes>'
            '</Module></Modules></CoverageSession>'
        )

    def test_opencover_merges_overlapping_lines_without_double_count(self):
        with tempfile.TemporaryDirectory() as directory:
            first = Path(directory) / "unit.xml"
            second = Path(directory) / "integration.xml"
            first.write_text(self.opencover_xml([(10, 1), (11, 0)]), encoding="utf-8")
            second.write_text(self.opencover_xml([(10, 0), (11, 2), (12, 0)]),
                              encoding="utf-8")
            self.assertEqual((2, 3),
                             coverage.merge_opencover_line_coverage([first, second]))
            self.assertEqual("66.67%", coverage.ratio(2, 3))

    def test_merge_counts_same_source_line_only_once(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "coverage.xml"
            report.write_text(self.opencover_xml([(10, 0), (10, 1), (11, 0)]),
                              encoding="utf-8")
            self.assertEqual((1, 2),
                             coverage.merge_opencover_line_coverage([report]))

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

    def test_missing_opencover_modules_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "coverage.xml"
            report.write_text("<CoverageSession/>", encoding="utf-8")
            with self.assertRaises(ValueError):
                coverage.merge_opencover_line_coverage([report])

    def test_missing_source_mapping_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "coverage.xml"
            report.write_text(
                '<CoverageSession><Modules><Module><ModuleName>X</ModuleName>'
                '<Classes><Class><Methods><Method><SequencePoints>'
                '<SequencePoint vc="1" sl="10" fileid="1"/>'
                '</SequencePoints></Method></Methods></Class></Classes>'
                '</Module></Modules></CoverageSession>', encoding="utf-8")
            with self.assertRaises(ValueError):
                coverage.merge_opencover_line_coverage([report])


if __name__ == "__main__":
    unittest.main()
