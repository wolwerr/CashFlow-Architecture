"""Tests for the local read-only load probe, including a real loopback HTTP server."""
import http.server
import base64
import json
import time
import pathlib
import sys
import threading
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from local_load_probe import Tokens, jwt_exp, percentile, run


def make_token(lifetime=300):
    body = base64.urlsafe_b64encode(
        json.dumps({"exp": int(time.time()) + lifetime}).encode()
    ).decode().rstrip("=")
    return "header." + body + ".signature"


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path.startswith("/api/v1/consolidation/daily?") and self.headers.get("Authorization", "").startswith("Bearer header."):
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b'{"ok":true}')
        else:
            self.send_response(401)
            self.end_headers()

    def log_message(self, *args):
        # Intentionally suppress request logging to avoid writing bearer tokens or test noise.
        pass


class LoadProbeTests(unittest.TestCase):
    def test_percentile_nearest_rank_and_empty(self):
        self.assertIsNone(percentile([], 0.95))
        self.assertEqual(percentile([50, 10, 40, 20, 30], 0.95), 50)
        self.assertEqual(percentile([50, 10, 40, 20, 30], 0.5), 30)

    def test_expiration_and_refresh_requirement(self):
        self.assertGreater(jwt_exp(make_token()), time.time())
        with self.assertRaises(ValueError):
            Tokens(make_token(0)).get()

    def test_new_metrics_present(self):
        self.assertIsNone(percentile([], .99))

    def test_read_only_probe_with_authenticated_loopback(self):
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            url = f"http://127.0.0.1:{server.server_port}/api/v1/consolidation/daily?merchant_id=test"
            report = run(url, Tokens(make_token()), rate=12, duration=0.35, workers=4, timeout=2)
            self.assertGreater(report["successful_requests"], 0)
            self.assertEqual(report["failed_requests"], 0)
            self.assertEqual(report["missed_scheduled_requests"], 0)
            self.assertEqual(report["status_counts"].get("200"), report["submitted_requests"])
            self.assertIsNotNone(report["successful_p95_ms"])
            self.assertIn("successful_p50_ms", report)
            self.assertIn("successful_p99_ms", report)
            self.assertIn("successful_throughput_rps", report)
            self.assertIn("completed_requests_per_second", report)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)

    def test_metrics_at_low_volume(self):
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            url = f"http://127.0.0.1:{server.server_port}/api/v1/consolidation/daily?merchant_id=test"
            report = run(url, Tokens(make_token()), rate=10, duration=0.25, workers=4, timeout=2)
            self.assertGreater(report["successful_requests"], 0)
            self.assertEqual(report["failed_requests"], 0)
            self.assertIsNotNone(report["successful_p95_ms"])
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
