import json
import tempfile
import threading
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from unittest.mock import patch

from medx_journal import DurableJournal


class JournalTests(unittest.TestCase):
    def test_concurrent_callers_wait_for_shared_disk_flush(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "journal"
            entered, release = threading.Event(), threading.Event()
            def flush(_):
                entered.set()
                self.assertTrue(release.wait(5))
            with patch("medx_journal.os.fsync", side_effect=flush) as sync:
                writer = DurableJournal(path)
                try:
                    with ThreadPoolExecutor(max_workers=8) as pool:
                        futures = [pool.submit(writer.append, {"id": i}) for i in range(8)]
                        self.assertTrue(entered.wait(5))
                        self.assertTrue(all(not f.done() for f in futures))
                        release.set()
                        for future in futures:
                            future.result(timeout=5)
                    self.assertLess(sync.call_count, 8)
                finally:
                    release.set()
                    writer.close()
            rows = [json.loads(line) for line in path.read_text().splitlines()]
            self.assertEqual(list(range(8)), sorted(r["id"] for r in rows))

    def test_flush_failure_blocks_current_and_future_writes(self):
        with tempfile.TemporaryDirectory() as folder, patch("medx_journal.os.fsync", side_effect=OSError("disk")):
            writer = DurableJournal(Path(folder) / "journal")
            try:
                for i in range(2):
                    with self.assertRaises(RuntimeError):
                        writer.append({"id": i})
            finally:
                writer.close()
