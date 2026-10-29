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
        with self.assertRaises(ValueError):
            reconcile([event | {"status": "intent"}], [file])


if __name__ == "__main__":
    unittest.main()
