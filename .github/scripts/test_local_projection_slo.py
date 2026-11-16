"""Unit tests for the local projection SLO experiment, without live HTTP writes."""
import pathlib
import sys
import unittest
from unittest.mock import patch
from decimal import Decimal

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import local_projection_slo as slo


class ProjectionSloTests(unittest.TestCase):
    def test_summary_requires_at_least_twenty_samples(self):
        result = slo.summarize([{"projected": True, "seconds": 0.4}] * 19)
        self.assertFalse(result["pass"])

    def test_nineteen_of_twenty_meets_95_percent(self):
        samples = [{"projected": True, "seconds": 0.5}] * 19
        samples.append({"projected": False, "seconds": None})
        result = slo.summarize(samples)
        self.assertTrue(result["pass"])
        self.assertEqual(result["projected_within_30s"], 19)

    def test_eighteen_of_twenty_fails(self):
        samples = [{"projected": True, "seconds": 1}] * 18
        samples.extend([{"projected": False, "seconds": None}] * 2)
        self.assertFalse(slo.summarize(samples)["pass"])

    def test_slow_projection_does_not_meet_deadline(self):
        result = slo.summarize([{"projected": True, "seconds": 31}] * 20)
        self.assertFalse(result["pass"])
        self.assertEqual(result["projected_within_30s"], 0)

    def test_preview_does_not_read_token_or_call_http(self):
        with patch.object(slo, "http_json") as call, patch.dict(slo.os.environ, {}, clear=True):
            result = slo.main(["--merchant-id", "68a5baac-d97f-45b4-9119-b4c5b9de7c9b",
                               "--date", "2026-11-16"])
        self.assertEqual(result, 0)
        call.assert_not_called()

    def test_one_sample_waits_for_credit_delta(self):
        with patch.object(slo, "balance", side_effect=[Decimal("10.00"), Decimal("10.01")]), \
             patch.object(slo, "create_credit", return_value="transaction-id") as create:
            sample = slo.measure_one("token", "merchant", "2026-11-16", 0.1, 30)
        self.assertTrue(sample["projected"])
        create.assert_called_once_with("token", "merchant", "2026-11-16")

    def test_post_requires_created_status(self):
        with patch.object(slo, "http_json", return_value=(503, None)):
            with self.assertRaises(RuntimeError):
                slo.create_credit("token", "merchant", "2026-11-16")


if __name__ == "__main__":
    unittest.main()
