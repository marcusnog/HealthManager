import unittest

from medx_templates import document_candidates, plain_text


class TemplateTests(unittest.TestCase):
    def test_inline_text_entities_and_paragraphs_keep_order(self):
        self.assertEqual("Primeiro & segundo.\n\nOutra linha.", plain_text("<p>Primeiro <strong>&amp; segundo.</strong></p><p>Outra linha.</p>"))

    def test_numbered_lists_keep_explicit_start_and_item_values(self):
        self.assertEqual("3. Um\n4. Dois\n8. Oito", plain_text('<ol start="3"><li>Um</li><li>Dois</li><li value="8">Oito</li></ol>'))

    def test_hidden_rich_content_and_placeholders_are_not_silently_stripped(self):
        for html in ['<p style="display:none">Oculto</p>', '<p>Texto<img src="data:..."></p>',
                     '<table><tr><td>Texto</td></tr></table>', '<p>{{paciente}}</p>', r'{\rtf1 Texto}',
                     '<ol reversed><li>Texto</li></ol>', '<ol type="A"><li>Texto</li></ol>']:
            with self.assertRaises(ValueError):
                plain_text(html)

    def test_private_parent_blocks_otherwise_shared_child(self):
        rows = [{"Id do Texto": 1, "Pai": 0, "Biblioteca": "Pessoal", "Texto": "", "Bloqueio": 123},
                {"Id do Texto": 2, "Pai": 1, "Biblioteca": "Modelo", "Texto": "<p>Texto</p>", "Bloqueio": 0}]
        self.assertEqual("private_or_unknown_parent", document_candidates(rows, "1")[1]["reason"])
        rows[0]["Bloqueio"] = 0
        result = document_candidates(rows, "1")[1]
        self.assertEqual("create", result["status"])
        self.assertEqual("MedX - Modelos / Pessoal", result["payload"]["folder"])


if __name__ == "__main__":
    unittest.main()
