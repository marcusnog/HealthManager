from copy import deepcopy
from datetime import datetime, timezone
import json
from pathlib import Path
import tempfile
import unittest

from medx_catalog import exam_candidate
from medx_prepare import sha256, write_json
from medx_repair_exam_units import correction, latest, run


class ExamUnitRepairTests(unittest.TestCase):
    def setUp(self):
        self.row = {"Id do exame": 1, "Exame": "Teste", "Peso": "mg", "Volume": "dl", "Homem_de": 1, "Homem_até": 2}
        candidate = exam_candidate(self.row, "1")
        old = deepcopy(candidate["payload"])
        old["content"] = old["content"].replace("Unidade: mg/dl", "Unidade: mg")
        old["fields"][0]["unit"] = "mg"
        self.owned = candidate | {"status": "created", "targetId": "exam", "payload": old}
        self.before = {k: old[k] for k in ("kind", "folder", "title", "content", "fields")}
        self.before = deepcopy(self.before) | {"id": "exam", "ownerUserId": None, "revision": "original"}
        self.before["fields"][0].update(required=False, min=1.25, max=None, defaultValue=None)

    def test_correction_changes_only_unit_and_its_description(self):
        plan = correction(self.row, "1", self.owned, self.before, deepcopy(self.before))
        self.assertEqual("update", plan["status"])
        self.assertEqual("mg/dl", plan["payload"]["fields"][0]["unit"])
        self.assertEqual(1.25, plan["payload"]["fields"][0]["min"])
        expected = deepcopy(self.before)
        expected["content"] = expected["content"].replace("Unidade: mg", "Unidade: mg/dl")
        expected["fields"][0]["unit"] = "mg/dl"
        self.assertEqual(expected, plan["expected"])

    def test_independent_field_or_revision_edits_are_held(self):
        changed = deepcopy(self.before)
        changed["fields"][0]["min"] = 99
        for current in (changed, self.before | {"revision": "edited"}):
            self.assertEqual("independent_edit", correction(self.row, "1", self.owned, self.before, current)["reason"])

    def test_uncertain_update_is_reconciled_only_when_full_expected_snapshot_exists(self):
        plan = correction(self.row, "1", self.owned, self.before, self.before)
        intent = plan | {"status": "intent"}
        self.assertEqual("held", correction(self.row, "1", self.owned, self.before, self.before, intent)["status"])
        changed = plan["expected"] | {"revision": "new"}
        self.assertEqual("reconcile", correction(self.row, "1", self.owned, self.before, changed, intent)["status"])
        self.assertEqual("held", correction(self.row, "1", self.owned, self.before, changed | {"title": "Edited"}, intent)["status"])

    def test_apply_recovers_lost_response_and_does_not_repeat_update(self):
        class FakeApi:
            base, clinic_id, clinic_name = "https://example.invalid", "clinic", "Clinic"
            def request(api, path):
                return [deepcopy(api.item)]
            def put(api, path, payload):
                self.assertEqual(api.item["revision"], payload["revision"])
                api.puts += 1
                api.item = api.item | {k: payload[k] for k in ("content", "fields")} | {"revision": "new"}
                raise TimeoutError("Response lost after update")
        with tempfile.TemporaryDirectory() as directory:
            stage = Path(directory)
            catalog = stage / "catalog-import"
            catalog.mkdir()
            write_json(stage / "exams.json", [self.row])
            write_json(stage / "manifest.json", {"subscription": "1", "tables": [
                {"name": "Exames_modelo.json", "path": "exams.json", "sha256": sha256(stage / "exams.json")}]})
            write_json(stage / "server-backup-receipt.json", {"counts": {"clinicId": "clinic"}, "archiveVerified": True,
                "checkedAtUtc": datetime.now(timezone.utc).isoformat()})
            write_json(catalog / "destination.json", {"baseUrl": FakeApi.base, "clinicId": "clinic", "clinicName": "Clinic"})
            write_json(catalog / "after-original.json", {"clinical-catalog": [self.before]})
            (catalog / "journal.ndjson").write_text(json.dumps(self.owned) + "\n", encoding="utf-8")
            api = FakeApi()
            api.item, api.puts = deepcopy(self.before), 0
            self.assertEqual({"update": 1}, run(stage, api))
            self.assertEqual(0, api.puts)
            with self.assertRaises(TimeoutError):
                run(stage, api, True)
            self.assertFalse((catalog / "running.lock").exists())
            self.assertEqual(1, run(stage, api, True)["newlyVerified"])
            self.assertEqual(1, run(stage, api, True)["matched"])
            self.assertEqual(1, api.puts)
            saved = latest(catalog / "journal.ndjson")[self.owned["sourceKey"]]
            self.assertEqual("mg/dl", saved["payload"]["fields"][0]["unit"])


if __name__ == "__main__":
    unittest.main()
