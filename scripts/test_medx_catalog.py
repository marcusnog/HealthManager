from datetime import datetime, timezone
import json
from pathlib import Path
import tempfile
import unittest

from medx_catalog import doctor_candidate, procedure_candidate, exam_candidate, run
from medx_prepare import sha256, write_json


class CatalogTests(unittest.TestCase):
    def doctor(self):
        return {"Id do Usuário": -1, "Nome Completo": "Maria Clara Silva", "Número do Conselho": "22061",
                "Conselho Profissional": "CRM", "Ativo": True, "Agenda": True, "Email": "source@example.invalid"}

    def procedure(self):
        return {"Id do Procedimento": 1, "Procedimento": "Aplicação teste", "Preço Base": 70, "Sessões": 2}

    def exam(self):
        return {"Id do exame": -2, "Exame": "Exame teste", "Peso": "mg/dL", "Homem_de": 1,
                "Homem_até": 2, "Mulher_de": 3, "Mulher_até": 4}

    def test_doctor_never_copies_email_or_creates_default_password_account(self):
        result = doctor_candidate(self.doctor(), [], [], "1")
        self.assertEqual("create", result["status"])
        self.assertIsNone(result["payload"]["email"])

    def test_conflicting_and_deleted_registration_are_held(self):
        result = doctor_candidate(self.doctor(), [{"id": "other", "name": "Other Person", "crm": "22061"}], [], "1")
        self.assertEqual("held", result["status"])
        result = doctor_candidate(self.doctor(), [], [{"id": "deleted", "name": "Maria Clara Silva", "crm": "22061"}], "1")
        self.assertEqual("deleted_registration", result["reason"])

    def test_matching_abbreviated_professional_requires_same_registration(self):
        result = doctor_candidate(self.doctor(), [{"id": "existing", "name": "Maria Silva", "crm": "22061"}], [], "1")
        self.assertEqual("linked_existing", result["status"])
        result = doctor_candidate(self.doctor(), [{"id": "existing", "name": "Maria Silva", "crm": "99999"}], [], "1")
        self.assertEqual("name_with_different_registration", result["reason"])

    def test_procedure_keeps_price_and_sessions_and_rejects_name_collision(self):
        result = procedure_candidate(self.procedure(), [], [], "1")
        self.assertEqual(70, result["payload"]["price"])
        self.assertEqual(2, result["payload"]["applicationCount"])
        self.assertEqual("held", procedure_candidate(self.procedure(), [{"name": "APLICACAO TESTE"}], [], "1")["status"])

    def test_exam_preserves_separate_reference_ranges_without_automatic_thresholds(self):
        result = exam_candidate(self.exam(), "1")
        self.assertEqual("create", result["status"])
        self.assertIn("masculina: 1 a 2", result["payload"]["content"])
        self.assertIn("feminina: 3 a 4", result["payload"]["content"])
        self.assertEqual("Text", result["payload"]["fields"][0]["type"])
        self.assertNotIn("min", result["payload"]["fields"][0])

    def test_catalog_import_verifies_and_reruns_without_duplicate_writes(self):
        class FakeApi:
            base, clinic_id, clinic_name = "https://example.invalid", "clinic", "Clinic"
            def __init__(self):
                self.data = {"doctors": [], "products": [], "clinical-catalog": []}
                self.posts = 0
            def request(self, path, payload=None):
                resource = path.split("?", 1)[0].strip("/")
                if payload is not None:
                    self.posts += 1
                    result = payload | {"id": str(self.posts)}
                    self.data[resource].append(result)
                    return result
                records = list(self.data[resource])
                if resource == "clinical-catalog" and "kind=" in path:
                    kind = path.split("kind=", 1)[1]
                    records = [r for r in records if r["kind"] == kind]
                return records if resource == "clinical-catalog" else {"items": records, "total": len(records)}
        with tempfile.TemporaryDirectory() as folder:
            stage = Path(folder)
            tables = []
            for name, rows in [("Usuários.json", [self.doctor()]), ("Procedimentos.json", [self.procedure()]), ("Exames_modelo.json", [self.exam()])]:
                write_json(stage / name, rows)
                tables.append({"name": name, "path": name, "sha256": sha256(stage / name)})
            write_json(stage / "manifest.json", {"subscription": "1", "tables": tables})
            write_json(stage / "server-backup-receipt.json", {"counts": {"clinicId": "clinic"},
                "archiveVerified": True, "checkedAtUtc": datetime.now(timezone.utc).isoformat()})
            write_json(stage / "deleted-identities.json", {"clinicId": "clinic", "doctors": [], "products": []})
            api = FakeApi()
            self.assertEqual(3, sum(run(stage, api, True).values()))
            summary = run(stage, api, True)
            self.assertEqual(3, api.posts)
            self.assertTrue(all(key.endswith(":matched") for key in summary))


if __name__ == "__main__":
    unittest.main()
