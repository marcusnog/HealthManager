"""Convert only shared, simple MedX text models; hold private or rich content."""
from html.parser import HTMLParser
import re

from medx_prepare import source_id, text


class PlainTemplate(HTMLParser):
    allowed = {"html", "body", "head", "title", "meta", "p", "div", "span", "br", "b", "strong", "em", "i", "u", "font",
               "ul", "ol", "li", "hr", "h1", "h2", "h3", "h4", "h5", "h6"}
    blocks = {"p", "div", "hr", "h1", "h2", "h3", "h4", "h5", "h6"}

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts, self.lists, self.skip, self.unsupported = [], [], 0, False

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag not in self.allowed or any(k == "hidden" or k.startswith("on") for k in attrs):
            self.unsupported = True
        style = attrs.get("style") or ""
        if re.search(r"display\s*:\s*none|visibility|opacity|line-through|mso-hide|color\s*:\s*(?:white|#fff(?:fff)?\b|rgb\(\s*255\s*,\s*255\s*,\s*255)", style, re.I):
            self.unsupported = True
        if tag in ("head", "title"):
            self.skip += 1
        if self.skip:
            return
        if tag in self.blocks or tag == "br":
            self.parts.append("\n")
        if tag in ("ol", "ul"):
            if self.lists or "reversed" in attrs or attrs.get("type", "1") != "1" or "list-style" in style:
                self.unsupported = True
            try:
                start = int(attrs.get("start", "1"))
            except (ValueError, TypeError):
                self.unsupported, start = True, 1
            self.lists.append([tag, start - 1])
        elif tag == "li":
            if self.parts and self.parts[-1] == "\n":
                self.parts.pop()
            if self.lists and self.lists[-1][0] == "ol":
                try:
                    self.lists[-1][1] = int(attrs["value"]) if "value" in attrs else self.lists[-1][1] + 1
                except (ValueError, TypeError):
                    self.unsupported = True
                self.parts.append(f"\n{self.lists[-1][1]}. ")
            else:
                self.parts.append("\n• ")

    def handle_endtag(self, tag):
        if tag in ("head", "title"):
            self.skip = max(0, self.skip - 1)
        if self.skip:
            return
        if tag in self.blocks or tag == "li":
            self.parts.append("\n")
        if tag in ("ol", "ul") and self.lists:
            self.lists.pop()

    def handle_data(self, data):
        if not self.skip:
            self.parts.append(data)


def plain_text(content):
    if content.lstrip().startswith("{\\rtf"):
        raise ValueError("rtf_requires_faithful_conversion")
    parser = PlainTemplate()
    parser.feed(content)
    parser.close()
    if parser.unsupported:
        raise ValueError("unsupported_or_hidden_markup")
    result = "\n".join(re.sub(r"[ \t\u00a0]+", " ", line).strip() for line in "".join(parser.parts).splitlines())
    result = re.sub(r"\n{3,}", "\n\n", result).strip()
    if re.search(r"\{\{|\$\{|<%|%[A-Za-z_][A-Za-z_ ]{0,60}%|\[[^\]\n]{1,80}\]|##[^#\n]{1,80}##", result):
        raise ValueError("legacy_placeholder_requires_review")
    return result


def document_candidates(rows, subscription):
    by_id = {source_id(r.get("Id do Texto")): r for r in rows}
    if None in by_id or len(by_id) != len(rows):
        raise ValueError("Invalid or duplicate template IDs.")
    candidates = []
    for row in rows:
        identifier = source_id(row["Id do Texto"])
        key = f"medx:{subscription}:Autodocs:{identifier}"
        candidate = {"sourceKey": key, "resource": "clinical-catalog", "kind": "Document", "status": "held"}
        try:
            if row.get("Bloqueio") != 0:
                raise ValueError("private_or_unknown_owner")
            if not text(row.get("Texto")):
                raise ValueError("folder_or_empty")
            parents, seen, parent = [], {identifier}, source_id(row.get("Pai"))
            while parent != "0":
                if parent is None or parent in seen or parent not in by_id:
                    raise ValueError("invalid_folder_hierarchy")
                seen.add(parent)
                ancestor = by_id[parent]
                if ancestor.get("Bloqueio") != 0:
                    raise ValueError("private_or_unknown_parent")
                parents.append(text(ancestor.get("Biblioteca")))
                parent = source_id(ancestor.get("Pai"))
            folder = "MedX - Modelos" + (" / " + " / ".join(reversed(parents)) if parents else "")
            title = text(row.get("Biblioteca"))
            content = plain_text(row["Texto"])
            if not content:
                raise ValueError("empty_visible_content")
            content += f"\n\nOrigem: [{key}]. Revisar a formatação antes de usar."
            if not title or len(title) > 200 or len(folder) > 120 or len(content) > 30000:
                raise ValueError("catalog_length_limit")
            candidate.update(status="create", payload={"kind": "Document", "folder": folder, "title": title,
                "content": content, "personal": False, "fields": []})
        except ValueError as error:
            candidate["reason"] = str(error)
        candidates.append(candidate)
    return candidates
