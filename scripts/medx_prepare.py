"""Prepare a private MedX migration package; never writes to HealthManager."""
import argparse
from collections import Counter, defaultdict
from datetime import date
import fnmatch
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import zipfile


def text(value):
    return "" if value is None else str(value).strip()


def digits(value):
    return re.sub(r"[^0-9]", "", text(value))


def valid_cpf(value):
    if len(value) != 11 or len(set(value)) == 1:
        return False
    for length in (9, 10):
        remainder = sum(int(value[i]) * (length + 1 - i) for i in range(length)) % 11
        if int(value[length]) != (0 if remainder < 2 else 11 - remainder):
            return False
    return True


def source_id(value):
    # MedX uses signed integer keys. Zero must not be mistaken for a missing key.
    return str(value) if isinstance(value, int) and not isinstance(value, bool) else None


def patient_candidate(row, today):
    issues = []
    source = source_id(row.get("Id do Cliente"))
    name = text(row.get("Nome"))
    cpf_raw = text(row.get("CPF/CGC"))
    cpf = digits(cpf_raw)
    if source is None:
        issues.append("missing_source_id")
    if not name or len(name) > 160:
        issues.append("invalid_name")
    if cpf_raw and not valid_cpf(cpf):
        issues.append("invalid_cpf")
    birth = None
    if text(row.get("Nascimento")):
        try:
            # A birth date is a calendar date, not an appointment timestamp.
            parsed = date.fromisoformat(text(row["Nascimento"])[:10])
            if parsed > today or parsed.year < 1900:
                issues.append("birth_date_requires_review")
            birth = parsed.isoformat()
        except ValueError:
            issues.append("invalid_birth_date")
    mobile = digits(row.get("Celular"))
    landline = digits(row.get("Telefone Residencial"))
    for key, value in (("Celular", mobile), ("Telefone Residencial", landline)):
        if text(row.get(key)) and (not value or len(value) > 20):
            issues.append("invalid_phone")
    sex = {"M": "Masculino", "F": "Feminino", "": None}.get(text(row.get("Sexo")))
    if text(row.get("Sexo")) not in ("M", "F", ""):
        issues.append("unknown_sex")
    payload = {
        "name": name, "cpf": cpf, "birthDate": birth, "phone": mobile or landline,
        "email": text(row.get("Email")) or None,
        "healthInsurance": None, "healthInsuranceId": None, "notes": text(row.get("Observações")) or None,
        "details": {
            "sex": sex, "secondaryPhone": landline if mobile and landline != mobile else None,
            "zipCode": text(row.get("Cep Residencial")) or None,
            "isVip": row.get("VIP") is True,
            "excludeFromMarketing": row.get("Exclui_Mkt") is True,
            "receiveDirectMail": row.get("Mala Direta") is True,
            "tags": text(row.get("Tags")) or None,
            "referredBy": None,
        },
    }
    for target, origin in {
        "socialName": "Nome Social", "rg": "RG", "contactName": "Contato",
        "street": "Endereço Residencial", "neighborhood": "Bairro Residencial",
        "city": "Cidade Residencial", "state": "Estado Residencial", "region": "Região",
        "company": "Empresa", "reference": "Referências", "acquisitionSource": "Como conheceu",
        "maritalStatus": "Estado Civil", "profession": "Profissão", "religion": "Religião",
        "fatherName": "Pai", "motherName": "Mãe", "companionName": "Acompanhante",
        "spouseName": "Cônjugue", "healthInsuranceNumber": "Número da Matrícula",
    }.items():
        payload["details"][target] = text(row.get(origin)) or None
    commercial = digits(row.get("Telefone Comercial"))
    if text(row.get("Telefone Comercial")) and (not commercial or len(commercial) > 20):
        issues.append("invalid_commercial_phone")
    payload["details"]["commercialPhone"] = commercial or None
    children = text(row.get("Filhos"))
    if children:
        if not children.isascii() or not children.isdigit() or int(children) > 100:
            issues.append("invalid_children_count")
        else:
            payload["details"]["childrenCount"] = int(children)
    return {"sourceId": source, "issues": issues, "payload": payload}


def prepare_patients(rows, today):
    patients = [patient_candidate(row, today) for row in rows]
    ids = Counter(p["sourceId"] for p in patients if p["sourceId"] is not None)
    cpfs = Counter(p["payload"]["cpf"] for p in patients if p["payload"]["cpf"])
    names = {p["sourceId"]: p["payload"]["name"] for p in patients if ids[p["sourceId"]] == 1}
    for index, patient in enumerate(patients):
        patient["sourceRow"] = index
        referral = source_id(rows[index].get("Indicado por"))
        if referral not in (None, "0"):
            patient["payload"]["details"]["referredBy"] = names.get(referral)
        if ids[patient["sourceId"]] > 1:
            patient["issues"].append("duplicate_source_id")
        if patient["payload"]["cpf"] and cpfs[patient["payload"]["cpf"]] > 1:
            patient["issues"].append("duplicate_cpf")
        patient["status"] = "held" if patient["issues"] else "source_validated"
    return patients


def decoded_name(info):
    if not info.flag_bits & 0x800:
        try:
            return info.filename.encode("cp437").decode("cp1252")
        except UnicodeError:
            pass
    return info.filename


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def write_lines(path, values):
    with path.open("w", encoding="utf-8") as stream:
        for value in values:
            stream.write(json.dumps(value, ensure_ascii=False) + "\n")


def document_link(row, files, patients):
    # Classe is the explicit attachment filename in this export; never guess from prose.
    name = text(row.get("Classe"))
    if not name:
        return None
    patient_id = source_id(row.get("Id do Cliente"))
    matches = files.get(name, [])
    issues = []
    if len(matches) != 1:
        issues.append("missing_attachment" if not matches else "ambiguous_attachment")
    else:
        if matches[0]["size"] == 0:
            issues.append("empty_attachment")
        if matches[0]["size"] > 10 * 1024 * 1024:
            issues.append("attachment_over_10_mib")
    if patient_id not in patients:
        issues.append("unknown_patient")
    elif patients[patient_id] != "source_validated":
        issues.append("patient_held")
    return {"sourceHistoryId": source_id(row.get("Id do Histórico")),
            "sourcePatientId": patient_id, "fileName": name,
            "sourceFiles": matches, "issues": issues}


def prepare(source, output, subscription, executable, password, today):
    repo = Path(__file__).resolve().parents[1]
    source, output = source.resolve(), output.resolve()
    if any(output.is_relative_to(root) for root in (repo, repo.parent / "healthmanager-web")) or output.exists():
        raise ValueError("Output must be a NEW private directory outside the repository.")
    archive = source / f"{subscription}_JSONdata.zip"
    if not archive.is_file():
        raise ValueError("JSONdata archive was not found.")
    output.mkdir(parents=True)
    (output / "tables").mkdir()
    manifest = {"version": 1, "subscription": subscription, "status": "preparing",
                "destination": None, "databaseWrites": 0, "archives": [], "tables": []}
    write_json(output / "manifest.json", manifest)
    tables = {}
    with zipfile.ZipFile(archive) as zipped:
        members = [i for i in zipped.infolist() if not i.is_dir()]
        for index, member in enumerate(members):
            name = decoded_name(member)
            if not name.endswith(".json"):
                raise ValueError("Unexpected member in JSONdata archive.")
            pattern = "".join(c if c.isascii() else "?" for c in member.filename)
            if sum(fnmatch.fnmatchcase(i.filename, pattern) for i in members) != 1:
                raise ValueError("Ambiguous archive member selection.")
            if member.file_size:
                result = subprocess.run([str(executable), "e", "-so", "-bd", f"-p{password}",
                                         str(archive), pattern], capture_output=True)
                if result.returncode or len(result.stdout) != member.file_size:
                    raise ValueError(f"Could not read table {index}; check archive/password.")
                raw = result.stdout
                rows = json.loads(raw.decode("utf-8-sig"))
                if not isinstance(rows, list) or any(not isinstance(r, dict) for r in rows):
                    raise ValueError(f"Unexpected table structure: {index}.")
            else:
                raw, rows = b"", []
            target = f"tables/{index:02}.json"
            (output / target).write_bytes(raw)
            tables[name] = target
            manifest["tables"].append({"name": name, "path": target, "rows": len(rows),
                                       "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()})
    def table(name):
        content = (output / tables[name]).read_bytes()
        return json.loads(content.decode("utf-8-sig")) if content else []

    patients = prepare_patients(table("Contatos.json"), today)
    for patient in patients:
        patient["sourceKey"] = f"medx:{subscription}:Contatos:{patient['sourceId']}"
    write_lines(output / "patients.ndjson", patients)
    patient_states = {p["sourceId"]: p["status"] for p in patients if p["sourceId"] is not None}
    files = defaultdict(list)
    hashes = {}
    archives = sorted(source.glob(f"{subscription}_*.zip"))
    for path in archives:
        digest = sha256(path)
        duplicate = hashes.get(digest)
        manifest["archives"].append({"name": path.name, "bytes": path.stat().st_size,
                                     "sha256": digest, "duplicateOf": duplicate})
        if duplicate:
            continue
        hashes[digest] = path.name
        if path == archive:
            continue
        with zipfile.ZipFile(path) as zipped:
            for member in zipped.infolist():
                if not member.is_dir():
                    files[decoded_name(member)].append({"archive": path.name, "member": member.filename,
                                                        "size": member.file_size, "crc32": member.CRC})
    history = table("Histórico de Clientes.json")
    history_ids = Counter(source_id(r.get("Id do Histórico")) for r in history)
    links, history_summary = [], Counter()
    with (output / "history.ndjson").open("w", encoding="utf-8") as stream:
        for index, row in enumerate(history):
            source = source_id(row.get("Id do Histórico"))
            patient_id = source_id(row.get("Id do Cliente"))
            issues = []
            if source is None or history_ids[source] > 1:
                issues.append("invalid_or_duplicate_history_id")
            if patient_id not in patient_states:
                issues.append("unknown_patient")
            elif patient_states[patient_id] != "source_validated":
                issues.append("patient_held")
            content = text(row.get("Histórico"))
            content_format = "rtf" if content.startswith("{\\rtf") else "html" if content.startswith("<") else "text"
            history_summary[content_format] += 1
            history_summary.update(issues)
            stream.write(json.dumps({"sourceId": source, "sourcePatientId": patient_id,
                "sourceAuthorId": source_id(row.get("Id do Usuário")), "sourceDate": row.get("Data"),
                "sourceTable": tables["Histórico de Clientes.json"], "sourceRow": index,
                "format": content_format, "issues": issues,
                "status": "awaiting_encounter_author_mapping"}, ensure_ascii=False) + "\n")
            link = document_link(row, files, patient_states)
            if link:
                links.append(link)
    write_lines(output / "document-links.ndjson", links)
    write_lines(output / "files.ndjson", ({"fileName": name, "sources": values} for name, values in files.items()))
    referenced = {link["fileName"] for link in links}
    summary = {
        "patients": {"total": len(patients), "sourceValidated": sum(not p["issues"] for p in patients),
                     "held": sum(bool(p["issues"]) for p in patients),
                     "issues": Counter(issue for p in patients for issue in p["issues"])},
        "history": {"total": len(history), "details": history_summary},
        "documents": {"files": sum(map(len, files.values())), "links": len(links),
                      "sourceValidatedLinks": sum(not link["issues"] for link in links),
                      "unreferencedFileNames": len(set(files) - referenced),
                      "issues": Counter(issue for link in links for issue in link["issues"])},
        "databaseWrites": 0,
        "pending": ["destination clinic and access", "destination identity reconciliation",
                    "doctor/encounter mapping", "legacy rich text conversion", "agenda and financial mappings"],
    }
    write_json(output / "summary.json", summary)
    manifest["status"] = "prepared_not_imported"
    manifest["outputs"] = {p.name: sha256(p) for p in output.glob("*.ndjson")}
    write_json(output / "manifest.json", manifest)
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--subscription", required=True)
    parser.add_argument("--seven-zip", type=Path, default=Path(r"C:\Program Files\7-Zip\7z.exe"))
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9]+", args.subscription):
        parser.error("Subscription must contain only digits.")
    password = os.environ.get("MEDX_BACKUP_PASSWORD") or getpass.getpass("Backup password: ")
    print(json.dumps(prepare(args.source, args.output, args.subscription, args.seven_zip,
                             password, date.today()), ensure_ascii=False, indent=2))
