import hashlib
import json
import unittest
import threading
from concurrent.futures import ThreadPoolExecutor

from medx_documents import UploadBudget, content_type, copy_one, document_plan, multipart


class DocumentTests(unittest.TestCase):
    def test_large_uploads_wait_for_byte_budget_and_failures_release_it(self):
        budget = UploadBudget(10)
        entered = threading.Event()
        def upload():
            with budget.hold(4):
                entered.set()
        with ThreadPoolExecutor(max_workers=1) as pool:
            with budget.hold(7):
                future = pool.submit(upload)
                self.assertFalse(entered.wait(0.05))
            future.result(timeout=5)
        self.assertTrue(entered.is_set())
        with self.assertRaises(RuntimeError), budget.hold(20):
            self.assertEqual(10, budget.used)
            raise RuntimeError("upload failed")
        self.assertEqual(0, budget.used)

    def link(self, patient="1", history="2"):
        return {"sourcePatientId": patient, "sourceHistoryId": history, "fileName": "original.pdf",
                "issues": [], "sourceFiles": [{"archive": "backup.zip", "size": 10, "crc32": 1}]}

    def test_same_file_is_deduplicated_only_within_one_patient(self):
        plan, held = document_plan([self.link(), self.link(history="3")], {"1": "target"}, "1")
        self.assertEqual(1, len(plan))
        self.assertEqual(["2", "3"], plan[0]["sourceHistoryIds"])
        self.assertEqual({}, held)
        plan, held = document_plan([self.link(), self.link(patient="2")], {"1": "a", "2": "b"}, "1")
        self.assertEqual([], plan)
        self.assertEqual(2, held["shared_file_across_patients"])

    def test_unsupported_signature_and_multipart_overhead_are_rejected(self):
        with self.assertRaises(ValueError):
            content_type("a.pdf", b"not a PDF")
        with self.assertRaises(ValueError):
            multipart("a.pdf", b"x" * (10 * 1024 * 1024), "application/pdf")

    def test_unknown_patient_and_path_traversal_are_held(self):
        self.assertEqual([], document_plan([self.link()], {}, "1")[0])
        link = self.link() | {"fileName": "../original.pdf"}
        self.assertEqual([], document_plan([link], {"1": "target"}, "1")[0])

    def test_upload_is_verified_by_download_and_resume_does_not_duplicate(self):
        data = b"%PDF-test fixture"
        candidate = document_plan([self.link()], {"1": "target"}, "1")[0][0]
        class FakeApi:
            clinic_id = "clinic"
            posts = 0
            corrupt = False
            def request_bytes(self, path, body=None, content_type=None):
                if body is None:
                    return b"corrupt" if self.corrupt else data
                self.posts += 1
                return json.dumps({"id": "doc", "fileName": candidate["fileName"],
                    "sizeInBytes": len(data), "contentType": "application/pdf",
                    "storagePath": "clinics/clinic/patients/target/" + candidate["fileName"]}).encode()
        api, documents, events = FakeApi(), [], []
        result = copy_one(api, candidate, data, documents, None, events.append)
        self.assertEqual("created", result["status"])
        self.assertEqual("intent", events[0]["status"])
        self.assertEqual(hashlib.sha256(data).hexdigest(), result["sha256"])
        self.assertEqual("matched", copy_one(api, candidate, data, documents, result, events.append)["status"])
        self.assertEqual(1, api.posts)
        deferred_events = []
        deferred = copy_one(api, candidate, data, documents, result, deferred_events.append, verify_on_server=True)
        self.assertEqual("uploaded", deferred["status"])
        self.assertEqual(1, api.posts)
        api.corrupt = True
        with self.assertRaises(ValueError):
            copy_one(api, candidate, data, documents, result, events.append)

    def test_uncertain_upload_is_not_blindly_retried(self):
        data = b"%PDF-test"
        candidate = document_plan([self.link()], {"1": "target"}, "1")[0][0]
        with self.assertRaises(ValueError):
            copy_one(None, candidate, data, [], {"sha256": hashlib.sha256(data).hexdigest(), "status": "intent"}, lambda _: None)


if __name__ == "__main__":
    unittest.main()
