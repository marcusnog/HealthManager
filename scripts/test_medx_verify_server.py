import unittest

from medx_verify_server import reconcile


class ServerVerificationTests(unittest.TestCase):
    def test_uploaded_is_verified_only_when_patient_name_size_and_hash_match(self):
        event = {"sourceKey": "source", "status": "uploaded", "documentId": "doc",
                 "targetPatientId": "patient", "fileName": "file.pdf", "bytes": 10, "sha256": "digest"}
        file = {"documentId": "doc", "patientId": "patient", "fileName": "file.pdf", "size": 10, "sha256": "digest"}
        self.assertEqual("created", reconcile([event], [file])[0]["status"])
        for changed in [file | {"patientId": "other"}, file | {"sha256": "changed"}, file | {"size": 9}]:
            with self.assertRaises(ValueError):
                reconcile([event], [changed])
        with self.assertRaises(ValueError):
            reconcile([event], [])
        with self.assertRaises(ValueError):
            reconcile([event], [file, file])
        self.assertEqual("created", reconcile([event | {"status": "intent"}], [file])[0]["status"])

    def test_uncertain_upload_requires_unique_source_bytes_and_patient_match(self):
        event = {"sourceKey": "source", "status": "intent", "source": {"size": 10},
                 "targetPatientId": "patient", "fileName": "file.pdf", "sha256": "digest"}
        file = {"documentId": "doc", "patientId": "patient", "fileName": "file.pdf", "size": 10, "sha256": "digest"}
        self.assertEqual("doc", reconcile([event], [file])[0]["documentId"])
        for files in ([], [file | {"sha256": "different"}], [file | {"patientId": "other"}],
                      [file, file | {"documentId": "duplicate"}]):
            with self.assertRaises(ValueError):
                reconcile([event], files)


if __name__ == "__main__":
    unittest.main()
