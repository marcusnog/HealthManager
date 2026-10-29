import unittest
from datetime import date

from medx_prepare import document_link, patient_candidate, prepare_patients, source_id, valid_cpf


class MedxPreparationTests(unittest.TestCase):
    def contact(self, **changes):
        row = {"Id do Cliente": -10, "Nome": "Paciente teste", "CPF/CGC": "529.982.247-25",
               "Nascimento": "1980-02-03T02:00:00", "Sexo": "F", "Celular": "(11) 99999-1111"}
        return row | changes

    def test_normalizes_patient_without_changing_birth_calendar_date(self):
        result = patient_candidate(self.contact(), date(2026, 10, 29))
        self.assertEqual([], result["issues"])
        self.assertEqual("-10", result["sourceId"])
        self.assertEqual("1980-02-03", result["payload"]["birthDate"])
        self.assertEqual("52998224725", result["payload"]["cpf"])
        self.assertEqual("11999991111", result["payload"]["phone"])
        self.assertEqual("Feminino", result["payload"]["details"]["sex"])

    def test_invalid_cpf_is_held_not_silently_removed(self):
        result = patient_candidate(self.contact(**{"CPF/CGC": "111.111.111-11"}), date(2026, 10, 29))
        self.assertIn("invalid_cpf", result["issues"])
        self.assertEqual("11111111111", result["payload"]["cpf"])
        self.assertFalse(valid_cpf("52998224724"))

    def test_missing_cpf_and_phone_are_allowed(self):
        result = patient_candidate(self.contact(**{"CPF/CGC": None, "Celular": None}), date(2026, 10, 29))
        self.assertEqual([], result["issues"])
        self.assertEqual("", result["payload"]["cpf"])
        self.assertEqual("", result["payload"]["phone"])

    def test_duplicate_cpfs_hold_both_patients_without_merging(self):
        rows = [self.contact(), self.contact(**{"Id do Cliente": 20})]
        candidates = prepare_patients(rows, date(2026, 10, 29))
        self.assertEqual(["held", "held"], [p["status"] for p in candidates])
        self.assertTrue(all("duplicate_cpf" in p["issues"] for p in candidates))

    def test_preserves_signed_and_zero_source_ids(self):
        self.assertEqual("0", source_id(0))
        self.assertEqual("-1", source_id(-1))
        self.assertIsNone(source_id(None))
        self.assertIsNone(source_id(True))

    def test_maps_contact_details_and_resolves_referral_id(self):
        rows = [self.contact(**{"Endereço Residencial": "Rua de teste, 10", "Filhos": "2",
                 "Observações": "Observação de teste", "Indicado por": 20}),
                self.contact(**{"Id do Cliente": 20, "Nome": "Referência", "CPF/CGC": None})]
        result = prepare_patients(rows, date(2026, 10, 29))[0]
        self.assertEqual("Rua de teste, 10", result["payload"]["details"]["street"])
        self.assertEqual(2, result["payload"]["details"]["childrenCount"])
        self.assertEqual("Referência", result["payload"]["details"]["referredBy"])
        self.assertEqual("Observação de teste", result["payload"]["notes"])

    def test_attachment_requires_exact_name_and_patient(self):
        row = {"Classe": "original.pdf", "Id do Cliente": -10, "Id do Histórico": 3}
        files = {"original.pdf": [{"size": 100}]}
        self.assertEqual([], document_link(row, files, {"-10": "source_validated"})["issues"])
        self.assertIn("unknown_patient", document_link(row, files, {})["issues"])
        self.assertIn("missing_attachment", document_link(row, {"copy.pdf": [{"size": 100}]}, {})["issues"])

    def test_empty_oversized_and_ambiguous_files_are_held(self):
        row = {"Classe": "file.pdf", "Id do Cliente": -10}
        for entries, expected in [([{"size": 0}], "empty_attachment"),
                                  ([{"size": 10 * 1024 * 1024 + 1}], "attachment_over_10_mib"),
                                  ([{"size": 1}, {"size": 1}], "ambiguous_attachment")]:
            self.assertIn(expected, document_link(row, {"file.pdf": entries}, {"-10": "source_validated"})["issues"])


if __name__ == "__main__":
    unittest.main()
