"""Offline tests for CashFlow acceptance evidence control."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "local_mvp_acceptance", Path(__file__).with_name("local_mvp_acceptance.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class AcceptanceTests(unittest.TestCase):
    def test_initial_manifest_never_claims_success(self):
        manifest = gate.initial_manifest()
        result = gate.report(manifest)
        self.assertEqual("NOT_ACCEPTED", result["outcome"])
        self.assertEqual(len(gate.SCENARIOS), result["counts"]["NOT_RUN"])
        self.assertEqual(0, result["counts"]["PASS"])

    def test_missing_evidence_blocks_pass(self):
        m = gate.initial_manifest()
        m["scenarios"]["browser_oidc"]["status"] = "PASS"
        with self.assertRaisesRegex(gate.InvalidEvidence, "PASS requires"):
            gate.report(m)

    def test_unknown_or_missing_scenario_rejected(self):
        m = gate.initial_manifest()
        del m["scenarios"]["browser_oidc"]
        with self.assertRaises(gate.InvalidEvidence):
            gate.report(m)
        m = gate.initial_manifest()
        m["scenarios"]["extra"] = {"status": "PASS", "evidence": [], "metrics": {}}
        with self.assertRaises(gate.InvalidEvidence):
            gate.report(m)

    def test_untrusted_paths_are_rejected(self):
        for ref in ("/etc/passwd", "../secret", "data/../secret", "https://example.com/evidence"):
            with self.subTest(ref=ref):
                m = gate.initial_manifest()
                m["scenarios"]["browser_oidc"] = {
                    "status": "PASS", "evidence": [ref], "metrics": {}}
                with self.assertRaises(gate.InvalidEvidence):
                    gate.report(m)

    def test_no_evidence_file_cannot_pass_even_if_named(self):
        m = gate.initial_manifest()
        m["scenarios"]["browser_oidc"] = {
            "status": "PASS", "evidence": ["local/browser-evidence.json"], "metrics": {}}
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(gate.InvalidEvidence, "evidence file missing"):
                gate.report(m, Path(directory))

    def test_load_and_projection_thresholds_are_enforced(self):
        m = gate.initial_manifest()
        m["scenarios"]["load_50rps_300s"] = {
            "status": "PASS", "evidence": ["metrics/load.json"],
            "metrics": {"duration_seconds": 300, "request_rate_rps": 50,
                        "p95_ms": 499, "failure_rate_pct": 5}}
        m["scenarios"]["projection_slo"] = {
            "status": "PASS", "evidence": ["metrics/projection.json"],
            "metrics": {"sample_count": 20, "within_30s_pct": 95}}
        self.assertEqual(2, gate.report(m)["counts"]["PASS"])
        m["scenarios"]["load_50rps_300s"]["metrics"]["p95_ms"] = 500
        with self.assertRaisesRegex(gate.InvalidEvidence, "p95_ms"):
            gate.report(m)

    def test_boolean_metrics_are_not_accepted_as_numbers(self):
        m = gate.initial_manifest()
        m["scenarios"]["projection_slo"] = {
            "status": "PASS", "evidence": ["metrics/projection.json"],
            "metrics": {"sample_count": True, "within_30s_pct": 100}}
        with self.assertRaises(gate.InvalidEvidence):
            gate.report(m)

    def test_full_acceptance_requires_commit_and_actual_files(self):
        m = gate.initial_manifest()
        m["commit"] = "a" * 40
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "evidence").mkdir()
            (root / "evidence" / "checked.json").write_text("{}", encoding="utf-8")
            for name in gate.SCENARIOS:
                metrics = {}
                if name == "load_50rps_300s":
                    metrics = {"duration_seconds": 300, "request_rate_rps": 50,
                               "p95_ms": 450, "failure_rate_pct": 1}
                elif name == "projection_slo":
                    metrics = {"sample_count": 20, "within_30s_pct": 100}
                m["scenarios"][name] = {"status": "PASS",
                                        "evidence": ["evidence/checked.json"], "metrics": metrics}
            self.assertTrue(gate.report(m, root)["ready_for_mvp_acceptance"])
            m["commit"] = "NOT_RECORDED"
            self.assertFalse(gate.report(m, root)["ready_for_mvp_acceptance"])

    def test_init_refuses_overwrite_and_uses_not_run(self):
        with tempfile.TemporaryDirectory() as directory:
            filename = Path(directory) / "acceptance.json"
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, gate.main(["--init", str(filename)]))
            self.assertEqual("NOT_RUN", json.loads(filename.read_text())["scenarios"]["browser_oidc"]["status"])
            with contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(2, gate.main(["--init", str(filename)]))

    def test_not_run_manifest_exits_nonzero_without_live_traffic(self):
        with tempfile.TemporaryDirectory() as directory:
            filename = Path(directory) / "acceptance.json"
            filename.write_text(json.dumps(gate.initial_manifest()), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(1, gate.main(["--manifest", str(filename)]))
            self.assertEqual("NOT_ACCEPTED", json.loads(output.getvalue())["outcome"])


if __name__ == "__main__":
    unittest.main()
