"""Import explicit MedX master data without changing existing clinical or financial records."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import getpass
import json
import os
from pathlib import Path

from medx_import_patients import Api, append_event, normalized_name
from medx_prepare import digits, sha256, source_id, text, write_json
from medx_templates import document_candidates


def same_professional(source, target):
    first = normalized_name(source).split()
    second = [w for w in normalized_name(target).split() if w not in ("dr", "dr.", "dra", "dra.")]
    return len(second) >= 2 and first[:1] == second[:1] and all(w in first for w in second)


def doctor_candidate(row, active, deleted, subscription):
    key = f"medx:{subscription}:Usuarios:{source_id(row.get('Id do Usuário'))}"
    name, crm = text(row.get("Nome Completo")), digits(row.get("Número do Conselho"))
    result = {"sourceKey": key, "resource": "doctors", "status": "held"}
    if source_id(row.get("Id do Usuário")) is None:
        return result | {"reason": "invalid_source_id"}
    if any(digits(d["crm"]) == crm and crm for d in deleted):
        return result | {"reason": "deleted_registration"}
    matches = [d for d in active if digits(d["crm"]) == crm and crm]
    if len(matches) == 1 and same_professional(name, matches[0]["name"]):
        return result | {"status": "linked_existing", "targetId": matches[0]["id"]}
    if matches:
        return result | {"reason": "registration_with_different_identity"}
    council = normalized_name(text(row.get("Conselho Profissional")))
    if not row.get("Ativo") or not row.get("Agenda") or council not in ("crm", "crm-ce", "cremec"):
        return result | {"reason": "not_unambiguous_active_doctor"}
    if not crm or not 2 <= len(name.split()) or len(name) > 160:
        return result | {"reason": "invalid_identity"}
    if any(same_professional(name, d["name"]) for d in active + deleted):
        return result | {"reason": "name_with_different_registration"}
    return result | {"status": "create", "payload": {"name": name, "crm": crm,
        "phone": None, "email": None, "clinicSharePercentage": 100, "specialtyIds": []}}


def procedure_candidate(row, active, deleted, subscription):
    key = f"medx:{subscription}:Procedimentos:{source_id(row.get('Id do Procedimento'))}"
    result = {"sourceKey": key, "resource": "products", "status": "held"}
    if source_id(row.get("Id do Procedimento")) is None:
        return result | {"reason": "invalid_source_id"}
    name = text(row.get("Procedimento"))
    price, sessions = row.get("Preço Base"), row.get("Sessões")
    if row.get("Ocultar") or not name or len(name) > 100 or not isinstance(price, (int, float)) or price <= 0:
        return result | {"reason": "invalid_or_hidden_procedure"}
    if not isinstance(sessions, int) or isinstance(sessions, bool) or sessions < 1:
        return result | {"reason": "invalid_session_count"}
    matches = [p for p in active + deleted if normalized_name(p["name"]) == normalized_name(name)]
    if matches:
        return result | {"reason": "existing_or_deleted_product_name"}
    return result | {"status": "create", "payload": {
        "name": name, "price": price, "applicationCount": sessions, "isActive": True}}


def exam_candidate(row, subscription):
    key = f"medx:{subscription}:Exames_modelo:{source_id(row.get('Id do exame'))}"
    title = text(row.get("Exame"))
    folder = "MedX - Exames / " + (text(row.get("Grupo")) or "Geral")
    unit = text(row.get("Peso"))
    content = ["Referências originais do MedX, pendentes de revisão clínica.",
        f"Unidade: {unit or 'não informada'}",
        f"Referência masculina: {row.get('Homem_de')} a {row.get('Homem_até')}",
        f"Referência feminina: {row.get('Mulher_de')} a {row.get('Mulher_até')}"]
    for field in ("Volume", "Similares"):
        if text(row.get(field)):
            content.append(f"{field} (origem): {text(row[field])}")
    content.append(f"Origem: [{key}]")
    result = {"sourceKey": key, "resource": "clinical-catalog", "status": "held"}
    if source_id(row.get("Id do exame")) is None:
        return result | {"reason": "invalid_source_id"}
    if row.get("Ocultar") or not title or len(title) > 200 or len(folder) > 120 or len("\n".join(content)) > 30000:
        return result | {"reason": "invalid_or_hidden_exam"}
    return result | {"status": "create", "payload": {"kind": "Exam", "folder": folder,
        "title": title, "content": "\n".join(content), "personal": False,
        "fields": [{"label": "Resultado", "type": "Text", "unit": unit if len(unit) <= 30 else None}]}}


def appointment_type_candidate(row, active, deleted, subscription):
    identifier = source_id(row.get("Id do Diagnóstico-QP"))
    name = text(row.get("Diagnóstico-QP"))
    result = {"sourceKey": f"medx:{subscription}:Diagnostico-QP:{identifier}", "resource": "appointment-types",
              "status": "held", "sourceDurationMinutes": row.get("Tempo")}
    if identifier is None or not name or len(name) > 100:
        return result | {"reason": "invalid_appointment_type"}
    normalized = normalized_name(name)
    if any(normalized_name(r["name"]) == normalized for r in deleted):
        return result | {"reason": "deleted_appointment_type"}
    found = [r for r in active if normalized_name(r["name"]) == normalized]
    if len(found) == 1:
        return result | {"status": "linked_existing", "targetId": found[0]["id"]}
    if found:
        return result | {"reason": "ambiguous_appointment_type"}
    return result | {"status": "create", "payload": {"name": name}}


def table(stage, manifest, name):
    info = next(t for t in manifest["tables"] if t["name"] == name)
    path = stage / info["path"]
    if sha256(path) != info["sha256"]:
        raise ValueError("Source table changed.")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def rows(api, resource):
    if resource == "clinical-catalog":
        result = []
        for kind in ("Exam", "Document"):
            items = api.request("/clinical-catalog?kind=" + kind)
            if len(items) >= 1000:
                raise ValueError("Catalog pagination required before importing more entries.")
            result.extend(items)
        return result
    result = api.request(f"/{resource}?Page=1&PageSize=5000")
    if len(result["items"]) != result["total"]:
        raise ValueError("Incomplete catalog snapshot.")
    return result["items"]


def matches(item, candidate):
    payload = candidate["payload"]
    if candidate["resource"] == "appointment-types":
        return item["name"] == payload["name"]
    if candidate["resource"] == "clinical-catalog":
        return (all(item.get(k) == payload[k] for k in ("kind", "content", "title", "folder"))
                and item.get("ownerUserId") is None and len(item.get("fields", [])) == len(payload["fields"])
                and all(all(actual.get(k) == v for k, v in expected.items())
                        for actual, expected in zip(item["fields"], payload["fields"])))
    keys = ("name", "crm", "phone", "email", "clinicSharePercentage") if candidate["resource"] == "doctors" else ("name", "price", "applicationCount", "isActive")
    return all(item.get(k) == payload[k] for k in keys)


def run(stage, api, apply, include_documents=False, include_appointment_types=False):
    manifest = json.loads((stage / "manifest.json").read_text(encoding="utf-8"))
    receipt = json.loads((stage / "server-backup-receipt.json").read_text(encoding="utf-8"))
    deleted = json.loads((stage / "deleted-identities.json").read_text(encoding="utf-8"))
    if (receipt["counts"]["clinicId"] != api.clinic_id or deleted["clinicId"] != api.clinic_id
        or not receipt["archiveVerified"] or not 0 <= (datetime.now(timezone.utc) - datetime.fromisoformat(receipt["checkedAtUtc"])).total_seconds() < 86400):
        raise ValueError("Verified recent server backup required.")
    directory = stage / "catalog-import"
    directory.mkdir(exist_ok=True)
    identity = {"baseUrl": api.base, "clinicId": api.clinic_id, "clinicName": api.clinic_name}
    path = directory / "destination.json"
    if path.exists() and json.loads(path.read_text(encoding="utf-8")) != identity:
        raise ValueError("Catalog destination changed.")
    write_json(path, identity)
    current = {resource: rows(api, resource) for resource in ("doctors", "products", "clinical-catalog")}
    if include_appointment_types:
        if "appointmentTypes" not in deleted:
            raise ValueError("Deleted appointment types must be checked first.")
        current["appointment-types"] = rows(api, "appointment-types")
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    write_json(directory / ("before-" + stamp + ".json"), current)
    subscription = manifest["subscription"]
    candidates = [doctor_candidate(r, current["doctors"], deleted["doctors"], subscription)
                  for r in table(stage, manifest, "Usuários.json")]
    candidates += [procedure_candidate(r, current["products"], deleted["products"], subscription)
                   for r in table(stage, manifest, "Procedimentos.json")]
    candidates += [exam_candidate(r, subscription) for r in table(stage, manifest, "Exames_modelo.json")]
    if include_documents:
        candidates += document_candidates(table(stage, manifest, "Autodocs.json"), subscription)
    if include_appointment_types:
        candidates += [appointment_type_candidate(r, current["appointment-types"], deleted["appointmentTypes"], subscription)
                       for r in table(stage, manifest, "Diagnóstico-QP.json")]
    journal = directory / "journal.ndjson"
    events = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()] if journal.exists() else []
    previous = {e["sourceKey"]: e for e in events}
    # Preserve the original creation payload so a rerun never treats our product as a new conflict.
    for candidate in candidates:
        prior = previous.get(candidate["sourceKey"])
        if prior and prior.get("payload"):
            candidate.update(status="create", payload=prior["payload"])
    write_json(directory / ("plan-" + stamp + ".json"), candidates)
    if not apply:
        return dict(Counter(c["resource"] + ":" + c["status"] for c in candidates))
    for kind in ("Exam", "Document"):
        new = sum(c["resource"] == "clinical-catalog" and c["status"] == "create" and c["payload"]["kind"] == kind and c["sourceKey"] not in previous for c in candidates)
        if sum(r["kind"] == kind for r in current["clinical-catalog"]) + new >= 1000:
            raise ValueError("Catalog exceeds readable limit; partition the import before writing.")
    lock = directory / "running.lock"
    with lock.open("x") as stream:
        stream.write(str(os.getpid()))
    try:
        results = []
        for candidate in candidates:
            key, resource = candidate["sourceKey"], candidate["resource"]
            if candidate["status"] != "create":
                results.append(candidate)
                continue
            prior = previous.get(key)
            found = [r for r in current[resource] if matches(r, candidate)]
            if len(found) > 1 or found and not prior:
                raise ValueError("Unexpected existing match; review before continuing.")
            if found:
                if prior.get("targetId") and prior["targetId"] != found[0]["id"]:
                    raise ValueError("Target mapping changed.")
                result = candidate | {"status": "matched", "targetId": found[0]["id"]}
            elif prior:
                raise ValueError("Prior catalog write requires reconciliation.")
            else:
                append_event(journal, candidate | {"status": "intent"})
                created = api.request("/" + resource, candidate["payload"])
                if not matches(created, candidate):
                    raise ValueError("Created catalog entry differs from prepared payload.")
                result = candidate | {"status": "created", "targetId": created["id"]}
                append_event(journal, result)
                current[resource].append(created)
            results.append(result)
        final = {resource: rows(api, resource) for resource in current}
        for result in results:
            if result["status"] in ("created", "matched"):
                found = [r for r in final[result["resource"]] if r["id"] == result["targetId"]]
                if len(found) != 1 or not matches(found[0], result):
                    raise ValueError("Catalog final verification failed.")
        before = json.loads((directory / ("before-" + stamp + ".json")).read_text(encoding="utf-8"))
        for resource, originals in before.items():
            if any(item not in final[resource] for item in originals):
                raise ValueError("Existing catalog item changed during import.")
        write_json(directory / ("results-" + stamp + ".json"), results)
        write_json(directory / ("after-" + stamp + ".json"), final)
        summary = dict(Counter(r["resource"] + ":" + r["status"] for r in results))
        write_json(directory / "summary.json", summary)
        return summary
    finally:
        lock.unlink()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--clinic-id", required=True)
    parser.add_argument("--clinic-name", required=True)
    parser.add_argument("--email", required=True)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--include-document-templates", action="store_true")
    parser.add_argument("--include-appointment-types", action="store_true")
    args = parser.parse_args()
    password = os.environ.get("HEALTHMANAGER_IMPORT_PASSWORD") or getpass.getpass("HealthManager password: ")
    api = Api(args.base_url, args.email, password, args.clinic_id, args.clinic_name)
    print(json.dumps(run(args.stage, api, args.apply, args.include_document_templates, args.include_appointment_types)), flush=True)
