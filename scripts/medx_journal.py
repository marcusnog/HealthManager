"""Batch concurrent journal writes while making every caller await disk durability."""
import json
import os
import queue
import threading
import time


class DurableJournal:
    def __init__(self, path):
        self.path = path
        self.queue = queue.Queue()
        self.error = None
        self.thread = threading.Thread(target=self._write, daemon=True)
        self.thread.start()

    def append(self, value):
        done = threading.Event()
        self.queue.put((value, done))
        done.wait()
        if self.error:
            raise RuntimeError("Journal persistence failed; stop before any further upload.") from self.error

    def close(self):
        self.queue.put(None)
        self.thread.join()

    def _write(self):
        while True:
            first = self.queue.get()
            if first is None:
                return
            batch = [first]
            # A short collection window shares one disk flush across concurrent requests.
            time.sleep(0.01)
            while not self.queue.empty():
                item = self.queue.get_nowait()
                if item is None:
                    raise RuntimeError("Close journal only after all producers have finished.")
                batch.append(item)
            try:
                if self.error is None:
                    with self.path.open("a", encoding="utf-8") as stream:
                        for value, _ in batch:
                            stream.write(json.dumps(value) + "\n")
                        stream.flush()
                        os.fsync(stream.fileno())
            except Exception as error:
                self.error = error
            finally:
                for _, done in batch:
                    done.set()
