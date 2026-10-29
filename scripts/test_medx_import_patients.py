import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
from urllib.error import HTTPError

from medx_import_patients import Api, classify, marker, run
from medx_prepare import sha256, write_json


def candidate():
    return {"sourceKey": "medx:1:Contatos:-1", "status": "source_validated", "payload": {
        "name": "Paciente Teste", "cpf": "52998224725", "birthDate": "1980-01-01", "notes": None}}


class FakeApi:
    base, clinic_id, clinic_name = "https://example.invalid/backend", "test-clinic", "Test Clinic"

    def __init__(self, failure=None):
        self.rows, self.posts, self.failure = [], 0, failure

    def patients(self):
        return list(self.rows)

    def login(self):
        pass

    def request(self, path, payload):
        self.posts += 1
        if self.failure:
            raise self.failure
        row = payload | {"id": "new-id"}
        self.rows.append(row)
        return row


class ImportTests(unittest.TestCase):
    def stage(self, root):
        source = root / "patients.ndjson"
        source.write_text(json.dumps(candidate()) + "\n", encoding="utf-8")
        write_json(root / "manifest.json", {"status": "prepared_not_imported",
                   "outputs": {source.name: sha256(source)}})

    def test_conflicts_are_held_without_merging(self):
        c = candidate()
        self.assertEqual("existing_cpf", classify(c, [c["payload"]], None)[0])
        self.assertEqual("existing_name_requires_review", classify(c,
            [{"name": " PACIÉNTE  TESTE ", "cpf": ""}], None)[0])
        c["payload"]["cpf"] = ""
        self.assertEqual("missing_cpf_requires_deleted_identity_check", classify(c, [], None)[0])

    def test_uncertain_outcome_never_blindly_retries(self):
        with self.assertRaises(ValueError):
            classify(candidate(), [], {"status": "intent"})

    def test_missing_cpf_requires_preflight_and_still_checks_deleted_names(self):
        c = candidate()
        c["payload"]["cpf"] = ""
        self.assertEqual("create", classify(c, [{"id": "other", "name": "Other", "cpf": ""}], None, True)[0])
        self.assertEqual("existing_name_requires_review", classify(c,
            [{"id": "deleted", "name": c["payload"]["name"], "cpf": ""}], None, True)[0])

    def test_marker_without_journal_is_not_assumed_to_be_ours(self):
        c = candidate()
        with self.assertRaises(ValueError):
            classify(c, [c["payload"] | {"id": "id", "notes": marker(c)}], None)

    def test_creation_and_rerun_are_idempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            root, api = Path(directory), FakeApi()
            self.stage(root)
            self.assertEqual({"created": 1}, run(root, api, 10))
            self.assertEqual({"matched": 1}, run(root, api, 10))
            self.assertEqual(1, api.posts)
            self.assertEqual(2, len(list((root / "import-test-clinic").glob("patients-before-*.manifest.json"))))

    def test_network_failure_leaves_intent_and_blocks_resume(self):
        with tempfile.TemporaryDirectory() as directory:
            root, api = Path(directory), FakeApi(TimeoutError())
            self.stage(root)
            with self.assertRaises(TimeoutError):
                run(root, api, 10)
            api.failure = None
            with self.assertRaises(ValueError):
                run(root, api, 10)
            self.assertEqual(1, api.posts)

    def test_database_conflict_is_recorded_and_not_retried(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            api = FakeApi(HTTPError("https://example.invalid", 409, "Conflict", {}, None))
            self.stage(root)
            self.assertEqual({"rejected": 1}, run(root, api, 10))
            self.assertEqual({"previously_rejected": 1}, run(root, api, 10))
            self.assertEqual(1, api.posts)

    def test_modified_stage_is_rejected_before_any_write(self):
        with tempfile.TemporaryDirectory() as directory:
            root, api = Path(directory), FakeApi()
            self.stage(root)
            (root / "patients.ndjson").write_text("{}", encoding="utf-8")
            with self.assertRaises(ValueError):
                run(root, api, 10)
            self.assertEqual(0, api.posts)

    def test_preserves_original_notes_after_source_marker(self):
        with tempfile.TemporaryDirectory() as directory:
            root, api = Path(directory), FakeApi()
            self.stage(root)
            c = candidate()
            c["payload"]["notes"] = "Original observation"
            source = root / "patients.ndjson"
            source.write_text(json.dumps(c) + "\n", encoding="utf-8")
            write_json(root / "manifest.json", {"status": "prepared_not_imported",
                       "outputs": {source.name: sha256(source)}})
            run(root, api, 1)
            self.assertEqual(marker(c) + "\nOriginal observation", api.rows[0]["notes"])

    def test_api_checks_tenant_and_reuses_connection_without_following_redirects(self):
        response = Mock(status=200)
        response.read.side_effect = [json.dumps({"accessToken": "test-token",
            "user": {"clinicId": "clinic", "role": "Admin"}}).encode(),
            json.dumps({"id": "clinic", "name": "Clinic"}).encode(), b"{}"]
        connection = Mock()
        connection.getresponse.return_value = response
        with patch("medx_import_patients.http.client.HTTPSConnection", return_value=connection) as constructor:
            api = Api("https://example.invalid/backend", "email", "password", "clinic", "Clinic")
            constructor.assert_called_once()
            response.status = 302
            with self.assertRaises(HTTPError):
                api.request("/patients", {})
            self.assertEqual(3, connection.request.call_count)

    def test_wrong_tenant_stops_at_login(self):
        response = Mock(status=200)
        response.read.return_value = json.dumps({"accessToken": "test-token",
            "user": {"clinicId": "wrong", "role": "Admin"}}).encode()
        connection = Mock()
        connection.getresponse.return_value = response
        with patch("medx_import_patients.http.client.HTTPSConnection", return_value=connection):
            with self.assertRaises(ValueError):
                Api("https://example.invalid/backend", "email", "password", "clinic", "Clinic")
            self.assertEqual(1, connection.request.call_count)

    def test_large_upload_timeout_is_bounded_and_reset_for_reads(self):
        api = object.__new__(Api)
        api.connection = Mock()
        api.connection.getresponse.return_value = Mock(status=200, read=Mock(return_value=b"{}"))
        api.base_path, api.clinic_id, api.token = "/backend", "clinic", "token"
        api.host_header = "example.invalid"
        api.request_bytes("/upload", b"file", "multipart/form-data; boundary=test")
        api.connection.sock.settimeout.assert_called_with(120)
        api.request_bytes("/read")
        api.connection.sock.settimeout.assert_called_with(45)

    def test_explicit_ssh_tunnel_uses_loopback_and_keeps_canonical_identity(self):
        response = Mock(status=200)
        response.read.side_effect = [json.dumps({"accessToken": "token", "user": {"clinicId": "clinic", "role": "Admin"}}).encode(),
                                     json.dumps({"id": "clinic", "name": "Clinic"}).encode()]
        connection = Mock()
        connection.getresponse.return_value = response
        with patch.dict("os.environ", {"HEALTHMANAGER_SSH_TUNNEL_PORT": "38443"}), patch("medx_import_patients.http.client.HTTPConnection", return_value=connection) as constructor:
            api = Api("https://example.invalid/backend", "email", "password", "clinic", "Clinic")
            constructor.assert_called_once_with("127.0.0.1", 38443, timeout=45)
            self.assertEqual("https://example.invalid/backend", api.base)
            self.assertEqual("example.invalid", connection.request.call_args.kwargs["headers"]["Host"])

    def test_invalid_tunnel_ports_are_rejected_before_connection(self):
        for port in ("0", "65536", "remote:80"):
            with patch.dict("os.environ", {"HEALTHMANAGER_SSH_TUNNEL_PORT": port}), self.assertRaises(ValueError):
                Api("https://example.invalid/backend", "email", "password", "clinic", "Clinic")


if __name__ == "__main__":
    unittest.main()
