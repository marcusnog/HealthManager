"""Copy verified MedX attachments to mapped patients, preserving original bytes."""
import argparse
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
import getpass
import hashlib
import json
import os
from pathlib import Path
import subprocess
import threading
import time
import uuid
import zlib

from medx_import_patients import Api
from medx_journal import DurableJournal
from medx_prepare import sha256, write_json


def document_plan(links, mappings, subscription):
    owners = defaultdict(set)
    for link in links:
        owners[link["fileName"]].add(link["sourcePatientId"])
    unique, held = {}, Counter()
    for link in links:
        name, patient = link["fileName"], link["sourcePatientId"]
        if link["issues"] or patient not in mappings:
            held["unresolved_source_or_patient"] += 1
            continue
        if len(owners[name]) != 1:
            held["shared_file_across_patients"] += 1
            continue
        if Path(name).name != name or any(c in name for c in '/\\\r\n"') or not name.isascii() or len(name) > 180:
            held["unsafe_or_unsupported_filename"] += 1
            continue
        if Path(name).suffix.lower() not in (".pdf", ".jpg", ".jpeg", ".png"):
            held["unsupported_format"] += 1
            continue
        source = link["sourceFiles"][0]
        key = f"medx:{subscription}:Document:{patient}:" + hashlib.sha256(name.encode()).hexdigest()
        if key not in unique:
            unique[key] = {"sourceKey": key, "sourcePatientId": patient, "targetPatientId": mappings[patient],
                "originalFileName": name, "fileName": f"medx{subscription}-" + name.lower().replace(" ", "-"),
                "source": source, "sourceHistoryIds": []}
        unique[key]["sourceHistoryIds"].append(link["sourceHistoryId"])
    return list(unique.values()), dict(held)


def content_type(name, data):
    extension = Path(name).suffix.lower()
    if extension == ".pdf" and data.startswith(b"%PDF-"):
        return "application/pdf"
    if extension in (".jpg", ".jpeg") and data.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if extension == ".png" and data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "image/png"
    raise ValueError("File signature does not match the expected supported format.")


def multipart(name, data, kind):
    boundary = "medx" + uuid.uuid4().hex
    head = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\n'
            f"Content-Type: {kind}\r\n\r\n").encode()
    body = head + data + f"\r\n--{boundary}--\r\n".encode()
    if len(body) > 10 * 1024 * 1024:
        raise ValueError("Multipart upload exceeds request limit.")
    return body, "multipart/form-data; boundary=" + boundary


def extract(candidate, source, seven_zip, password):
    member = candidate["source"]
    archive = source / member["archive"]
    if archive.parent.resolve() != source.resolve():
        raise ValueError("Archive path escapes source directory.")
    result = subprocess.run([str(seven_zip), "e", "-so", "-bd", f"-p{password}",
                             str(archive), candidate["originalFileName"]], capture_output=True)
    data = result.stdout
    if result.returncode or len(data) != member["size"] or zlib.crc32(data) != member["crc32"]:
        raise ValueError("Attachment extraction failed integrity checks.")
    return data


def copy_one(api, candidate, data, documents, prior, event, verify_on_server=False):
    digest = hashlib.sha256(data).hexdigest()
    kind = content_type(candidate["originalFileName"], data)
    path = "/patients/" + candidate["targetPatientId"] + "/documents"
    found = [d for d in documents if d["fileName"].lower() == candidate["fileName"].lower()]
    if len(found) > 1 or found and not prior:
        raise ValueError("Unexpected filename collision; no upload attempted.")
    if prior and prior["sha256"] != digest:
        raise ValueError("Source file changed since previous attempt.")
    if found:
        document = found[0]
        if prior.get("documentId") and prior["documentId"] != document["id"]:
            raise ValueError("Document mapping changed.")
        status = "matched"
    elif prior:
        raise ValueError("Previous upload is not visible; reconcile before retrying.")
    else:
        body, mime = multipart(candidate["fileName"], data, kind)
        event(candidate | {"status": "intent", "sha256": digest})
        document = json.loads(api.request_bytes(path + "/upload", body, mime))
        status = "created"
    prefix = f"clinics/{api.clinic_id}/patients/{candidate['targetPatientId']}/"
    if (document["fileName"] != candidate["fileName"] or document["sizeInBytes"] != len(data)
        or document["contentType"] != kind or document["storagePath"] != prefix + candidate["fileName"]):
        raise ValueError("Uploaded document metadata does not match expected patient or file.")
    if verify_on_server:
        status = "uploaded"
    else:
        downloaded = api.request_bytes(path + "/" + document["id"] + "/download")
        if hashlib.sha256(downloaded).hexdigest() != digest:
            raise ValueError("Downloaded file differs from source; stop for investigation.")
    result = candidate | {"status": status, "sha256": digest, "documentId": document["id"], "bytes": len(data)}
    event(result)
    if not found:
        documents.append(document)
    return result


def run(stage, source, api, password, seven_zip, limit, workers, verify_on_server=False):
    manifest = json.loads((stage / "manifest.json").read_text(encoding="utf-8"))
    receipt = json.loads((stage / "server-backup-receipt.json").read_text(encoding="utf-8"))
    if not receipt["archiveVerified"] or receipt["counts"]["clinicId"] != api.clinic_id:
        raise ValueError("Verified destination backup required.")
    if not 0 <= (datetime.now(timezone.utc) - datetime.fromisoformat(receipt["checkedAtUtc"])).total_seconds() < 86400:
        raise ValueError("Refresh destination backup before uploading documents.")
    links_path = stage / "document-links.ndjson"
    if sha256(links_path) != manifest["outputs"][links_path.name]:
        raise ValueError("Prepared document links changed.")
    patient_journal = stage / ("import-" + api.clinic_id) / "journal.ndjson"
    events = [json.loads(line) for line in patient_journal.read_text(encoding="utf-8").splitlines()]
    patient_states = {e["sourceKey"]: e for e in events}
    mappings = {k.rsplit(":", 1)[1]: e["targetId"] for k, e in patient_states.items() if e["status"] in ("created", "matched")}
    response = api.request("/patients?Page=1&PageSize=10000&SortBy=createdat")
    if len(response["items"]) != response["total"]:
        raise ValueError("Incomplete patient snapshot.")
    current = {p["id"]: p for p in response["items"]}
    for source_id, target in mappings.items():
        marker = f"[medx:{manifest['subscription']}:Contatos:{source_id}]"
        if target not in current or marker not in (current[target].get("notes") or "").splitlines():
            raise ValueError("Patient mapping is not confirmed by destination.")
    links = [json.loads(line) for line in links_path.read_text(encoding="utf-8").splitlines()]
    plan, held = document_plan(links, mappings, manifest["subscription"])
    directory = stage / "document-import"
    directory.mkdir(exist_ok=True)
    identity = {"baseUrl": api.base, "clinicId": api.clinic_id, "linksSha256": sha256(links_path)}
    path = directory / "destination.json"
    if path.exists() and json.loads(path.read_text(encoding="utf-8")) != identity:
        raise ValueError("Document destination or source changed.")
    write_json(path, identity)
    journal = directory / "journal.ndjson"
    previous = {e["sourceKey"]: e for e in (json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines())} if journal.exists() else {}
    selected = [c for c in sorted(plan, key=lambda c: c["source"]["size"], reverse=True)
                if c["sourceKey"] not in previous or previous[c["sourceKey"]]["status"] == "intent"][:limit]
    write_json(directory / "plan-summary.json", {"eligibleUniqueFiles": len(plan), "selected": len(selected), "heldLinks": held})
    archive_hashes = {a["name"]: a["sha256"] for a in manifest["archives"]}
    for archive in {c["source"]["archive"] for c in selected}:
        if sha256(source / archive) != archive_hashes[archive]:
            raise ValueError("Source archive changed.")
    groups = defaultdict(list)
    for candidate in selected:
        groups[candidate["targetPatientId"]].append(candidate)
    lock = directory / "running.lock"
    with lock.open("x") as stream:
        stream.write(str(os.getpid()))
    mutex, stop = threading.Lock(), threading.Event()
    local, clients = threading.local(), []
    totals = Counter()
    writer = DurableJournal(journal)
    def event(value):
        writer.append(value)
        with mutex:
            if value["status"] != "intent":
                totals[value["status"]] += 1
                if value["status"] in ("uploaded", "created", "matched"):
                    totals["uploadedBytes" if value["status"] == "uploaded" else "verifiedBytes"] += value["bytes"]
                if (totals["created"] + totals["matched"] + totals["uploaded"]) % 100 == 0:
                    print(json.dumps(dict(totals)), flush=True)
    def group(candidates):
        if stop.is_set():
            return
        try:
            if not hasattr(local, "client"):
                local.client = Api(api.base, api.credentials["email"], api.credentials["password"], api.clinic_id, api.clinic_name)
                local.last_login = time.monotonic()
                with mutex:
                    clients.append(local.client)
            client = local.client
            documents = client.request("/patients/" + candidates[0]["targetPatientId"] + "/documents")
            for candidate in candidates:
                if stop.is_set() or (directory / "pause.request").exists():
                    stop.set()
                    return
                if time.monotonic() - local.last_login > 900:
                    client.login()
                    local.last_login = time.monotonic()
                data = extract(candidate, source, seven_zip, password)
                try:
                    content_type(candidate["originalFileName"], data)
                except ValueError:
                    event(candidate | {"status": "held", "reason": "file_signature_mismatch",
                        "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)})
                    continue
                copy_one(client, candidate, data, documents, previous.get(candidate["sourceKey"]), event, verify_on_server)
        except Exception:
            stop.set()
            raise
    try:
        with ThreadPoolExecutor(max_workers=workers) as executor:
            futures = [executor.submit(group, candidates) for candidates in groups.values()]
            for future in as_completed(futures):
                future.result()
        write_json(directory / "last-run-summary.json", dict(totals))
        return dict(totals)
    finally:
        writer.close()
        for client in clients:
            client.connection.close()
        lock.unlink()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--clinic-id", required=True)
    parser.add_argument("--clinic-name", required=True)
    parser.add_argument("--email", required=True)
    parser.add_argument("--limit", type=int, required=True)
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--verify-on-server", action="store_true")
    parser.add_argument("--seven-zip", type=Path, default=Path(r"C:\Program Files\7-Zip\7z.exe"))
    args = parser.parse_args()
    if args.limit < 1 or not 1 <= args.workers <= 32:
        parser.error("Positive limit and 1..32 workers required.")
    credential = os.environ.get("HEALTHMANAGER_IMPORT_PASSWORD") or getpass.getpass("HealthManager password: ")
    password = os.environ.get("MEDX_BACKUP_PASSWORD") or getpass.getpass("Backup password: ")
    api = Api(args.base_url, args.email, credential, args.clinic_id, args.clinic_name)
    print(json.dumps(run(args.stage, args.source, api, password, args.seven_zip, args.limit, args.workers, args.verify_on_server)), flush=True)
