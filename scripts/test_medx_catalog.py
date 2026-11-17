from datetime import datetime, timezone
import json
from pathlib import Path
import tempfile
import unittest

from medx_catalog import doctor_candidate, procedure_candidate, exam_candidate, appointment_type_candidate, expense_category_candidates, run
from medx_prepare import sha256, write_json


class CatalogTests(unittest.TestCase):
    def test_exam_unit_keeps_explicit_denominator_without_converting_values(self):
        row = self.exam() | {"Peso": "mg", "Volume": "dl"}
        result = exam_candidate(row, "1")
        self.assertEqual("mg/dl", result["payload"]["fields"][0]["unit"])
        self.assertIn("Unidade: mg/dl", result["payload"]["content"])
        self.assertIn("masculina: 1 a 2", result["payload"]["content"])
        self.assertEqual("held", exam_candidate(row | {"Peso": ""}, "1")["status"])

    def test_expense_categories_require_explicit_parent_type_and_preserve_group(self):
        groups = [{"Id do Grupo": 0, "Grupo": "Administrativo", "Tipo": "Despesas"},
                  {"Id do Grupo": 1, "Grupo": "Consultas", "Tipo": "Receitas"}]
        rows = [{"Id do subgrupo": -1, "Id do grupo": 0, "Subgrupo": "Material"},
                {"Id do subgrupo": 2, "Id do grupo": 1, "Subgrupo": "Consulta"},
                {"Id do subgrupo": 3, "Id do grupo": 99, "Subgrupo": "Sem grupo"}]
        result = expense_category_candidates(rows, groups, [], [], "1")
        self.assertEqual({"name": "Material"}, result[0]["payload"])
        self.assertEqual("Administrativo", result[0]["sourceGroupName"])
        self.assertEqual(["create", "held", "held"], [r["status"] for r in result])

    def test_expense_categories_hold_duplicate_and_deleted_names(self):
        groups = [{"Id do Grupo": 1, "Tipo": "Despesas"}]
        row = {"Id do subgrupo": 1, "Id do grupo": 1, "Subgrupo": "Material"}
        existing = [{"id": "category", "name": "MATERIAL"}]
        self.assertEqual("linked_existing", expense_category_candidates([row], groups, existing, [], "1")[0]["status"])
        self.assertEqual("deleted_expense_category", expense_category_candidates([row], groups, existing, existing, "1")[0]["reason"])
        duplicate = row | {"Id do subgrupo": 2, "Subgrupo": " MATERIAL "}
        self.assertTrue(all(r["status"] == "held" for r in expense_category_candidates([row, duplicate], groups, [], [], "1")))
        self.assertTrue(all(r["status"] == "held" for r in expense_category_candidates([row, row], groups, [], [], "1")))
        with self.assertRaisesRegex(ValueError, "duplicate financial group"):
            expense_category_candidates([row], groups + groups, [], [], "1")

    def test_appointment_types_preserve_duration_without_changing_target_schema(self):
        row = {"Id do Diagnóstico-QP": -10, "Diagnóstico-QP": "Retorno", "Tempo": 40}
        created = appointment_type_candidate(row, [], [], "1")
        self.assertEqual({"name": "Retorno"}, created["payload"])
        self.assertEqual(40, created["sourceDurationMinutes"])
        existing = [{"id": "type", "name": "RETORNO"}]
        self.assertEqual("linked_existing", appointment_type_candidate(row, existing, [], "1")["status"])
        self.assertEqual("deleted_appointment_type", appointment_type_candidate(row, [], existing, "1")["reason"])

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
                self.data = {"doctors": [], "products": [], "clinical-catalog": [], "expense-categories": []}
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
            for name, rows in [("Usuários.json", [self.doctor()]), ("Procedimentos.json", [self.procedure()]), ("Exames_modelo.json", [self.exam()]),
                               ("Grupos.json", [{"Id do Grupo": 1, "Tipo": "Despesas"}]),
                               ("Subgrupos.json", [{"Id do subgrupo": 1, "Id do grupo": 1, "Subgrupo": "Material"}])]:
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
            with self.assertRaisesRegex(ValueError, "Deleted expense categories"):
                run(stage, api, True, include_expense_categories=True)
            write_json(stage / "deleted-identities.json", {"clinicId": "clinic", "doctors": [], "products": [], "expenseCategories": []})
            self.assertEqual(1, run(stage, api, True, include_expense_categories=True)["expense-categories:created"])
            self.assertEqual(1, run(stage, api, True, include_expense_categories=True)["expense-categories:matched"])
            self.assertEqual(4, api.posts)


if __name__ == "__main__":
    unittest.main()
