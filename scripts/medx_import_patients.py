"""Append MedX patients with CPF through the tenant API; never update existing records."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import getpass
import http.client
import json
import os
from pathlib import Path
import unicodedata
import urllib.error
from urllib.parse import urlsplit

from medx_prepare import sha256, valid_cpf, write_json


def normalized_name(value):
    return " ".join("".join(c for c in unicodedata.normalize("NFKD", value.casefold())
                            if not unicodedata.combining(c)).split())


def marker(candidate):
    return "[" + candidate["sourceKey"] + "]"


def classify(candidate, patients, prior, allow_missing_cpf=False):
    payload = candidate["payload"]
    matches = [p for p in patients if marker(candidate) in (p.get("notes") or "").splitlines()]
    if matches:
        if len(matches) != 1 or not prior:
            raise ValueError("Unexpected or duplicate source marker; manual reconciliation required.")
        found = matches[0]
        if any(found.get(k) != payload.get(k) for k in ("name", "cpf", "birthDate")):
            raise ValueError("Previously imported identity changed; manual reconciliation required.")
        if prior.get("targetId") and prior["targetId"] != found["id"]:
            raise ValueError("Journal target differs from source marker.")
        return "matched", found["id"]
    if prior and prior["status"] in ("intent", "created", "matched"):
        raise ValueError("Unresolved previous write; do not retry automatically.")
    if prior and prior["status"] == "rejected":
        return "previously_rejected", None
    if candidate["status"] != "source_validated":
        return "source_held", None
    if not valid_cpf(payload["cpf"]) and not (allow_missing_cpf and not payload["cpf"]):
        return "missing_cpf_requires_deleted_identity_check", None
    if payload["cpf"] and any(p.get("cpf") == payload["cpf"] for p in patients):
        return "existing_cpf", None
    if any(normalized_name(p["name"]) == normalized_name(payload["name"]) for p in patients):
        return "existing_name_requires_review", None
    return "create", None


class Api:
    def __init__(self, base, email, password, clinic_id, clinic_name):
        if not base.startswith("https://"):
            raise ValueError("HTTPS is required.")
        self.base, self.token = base.rstrip("/"), None
        url = urlsplit(self.base)
        if not url.hostname or url.username or url.password or url.query or url.fragment:
            raise ValueError("Invalid API URL.")
        self.connection = http.client.HTTPSConnection(url.hostname, url.port or 443, timeout=45)
        self.base_path = url.path
        self.credentials = {"email": email, "password": password}
        self.clinic_id, self.clinic_name = clinic_id, clinic_name
        self.login()

    def request(self, path, data=None):
        return json.loads(self.request_bytes(path, json.dumps(data).encode() if data is not None else None))

    def request_bytes(self, path, body=None, content_type="application/json"):
        self.connection.timeout = 120 if content_type.startswith("multipart/") else 45
        if self.connection.sock:
            self.connection.sock.settimeout(self.connection.timeout)
        headers = {"Content-Type": content_type, "X-Clinic-Id": self.clinic_id}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        self.connection.request("POST" if body is not None else "GET", self.base_path + path,
            body=body, headers=headers)
        response = self.connection.getresponse()
        body = response.read()
        if not 200 <= response.status < 300:
            raise urllib.error.HTTPError(self.base + path, response.status, "API request failed", {}, None)
        return body

    def login(self):
        auth = self.request("/auth/login", self.credentials)
        if auth["user"]["clinicId"] != self.clinic_id or auth["user"]["role"] != "Admin":
            raise ValueError("Wrong clinic or role.")
        self.token = auth["accessToken"]
        clinic = self.request("/tenant/settings")
        if clinic["id"] != self.clinic_id or clinic["name"] != self.clinic_name:
            raise ValueError("Destination clinic does not match.")

    def patients(self):
        result, page, total = [], 1, None
        while True:
            response = self.request(f"/patients?Page={page}&PageSize=100&SortBy=createdat")
            if total is not None and total != response["total"]:
                raise ValueError("Patient count changed during snapshot; restart preflight.")
            total = response["total"]
            result.extend(response["items"])
            if len(result) >= total:
                break
            if not response["items"]:
                raise ValueError("Incomplete patient snapshot.")
            page += 1
        if len(result) != total or len({p["id"] for p in result}) != total:
            raise ValueError("Incomplete or unstable patient snapshot.")
        return result


def append_event(path, event):
    with path.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(event) + "\n")
        stream.flush()
        os.fsync(stream.fileno())


def run(stage, api, limit, deleted=None):
    manifest = json.loads((stage / "manifest.json").read_text(encoding="utf-8"))
    source = stage / "patients.ndjson"
    if manifest["status"] != "prepared_not_imported" or sha256(source) != manifest["outputs"][source.name]:
        raise ValueError("Source preparation is incomplete or changed.")
    candidates = [json.loads(line) for line in source.read_text(encoding="utf-8").splitlines()]
    directory = stage / ("import-" + api.clinic_id)
    directory.mkdir(exist_ok=True)
    lock = directory / "running.lock"
    # A surviving lock after a crash requires explicit process/journal inspection.
    with lock.open("x") as stream:
        stream.write(str(os.getpid()))
    try:
        identity = {"baseUrl": api.base, "clinicId": api.clinic_id, "clinicName": api.clinic_name,
                    "sourceSha256": sha256(source)}
        identity_file = directory / "destination.json"
        if identity_file.exists():
            if json.loads(identity_file.read_text(encoding="utf-8")) != identity:
                raise ValueError("Destination or source changed since first run.")
        else:
            write_json(identity_file, identity)
        journal = directory / "journal.ndjson"
        events = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()] if journal.exists() else []
        prior = {e["sourceKey"]: e for e in events}
        patients = api.patients()
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
        snapshot = directory / ("patients-before-" + stamp + ".json")
        write_json(snapshot, patients)
        if json.loads(snapshot.read_text(encoding="utf-8")) != patients:
            raise ValueError("Snapshot verification failed.")
        write_json(snapshot.with_suffix(".manifest.json"), identity | {
            "sha256": sha256(snapshot), "count": len(patients), "scope": "active patient API snapshot"})
        counts, results = Counter(), []
        for candidate in candidates:
            key = candidate["sourceKey"]
            status, target = classify(candidate, patients + (deleted or []), prior.get(key), deleted is not None)
            if status == "create" and counts["created"] >= limit:
                status = "pending_batch_limit"
            if status == "create":
                if counts["created"] % 50 == 0:
                    api.login()
                notes = candidate["payload"].get("notes")
                payload = candidate["payload"] | {"notes": marker(candidate) + ("\n" + notes if notes else "")}
                append_event(journal, {"sourceKey": key, "status": "intent"})
                try:
                    created = api.request("/patients", payload)
                except urllib.error.HTTPError as error:
                    if error.code not in (400, 409, 422):
                        raise RuntimeError("Creation outcome uncertain; reconcile journal before resuming.") from None
                    status = "rejected"
                    append_event(journal, {"sourceKey": key, "status": status, "httpStatus": error.code})
                else:
                    if any(created.get(k) != payload.get(k) for k in ("name", "cpf", "birthDate", "notes")):
                        raise ValueError("Created patient differs from candidate; stop for reconciliation.")
                    target, status = created["id"], "created"
                    append_event(journal, {"sourceKey": key, "status": status, "targetId": target})
                    patients.append(created)
            counts[status] += 1
            results.append({"sourceKey": key, "status": status, "targetId": target})
            if status == "created" and counts["created"] % 50 == 0:
                print(json.dumps(dict(counts)), flush=True)
        current = {p["id"]: p for p in api.patients()}
        expected = {c["sourceKey"]: c for c in candidates}
        for result in results:
            if result["status"] in ("created", "matched"):
                patient = current.get(result["targetId"])
                if not patient or "[" + result["sourceKey"] + "]" not in (patient.get("notes") or "").splitlines():
                    raise ValueError("Final verification failed.")
                payload = expected[result["sourceKey"]]["payload"]
                for field, value in payload.items():
                    if field == "details":
                        if any(patient.get("details", {}).get(k) != v for k, v in value.items()):
                            raise ValueError("Final contact details differ from source candidate.")
                    elif field != "notes" and patient.get(field) != value:
                        raise ValueError("Final patient fields differ from source candidate.")
                notes = marker(expected[result["sourceKey"]]) + ("\n" + payload["notes"] if payload.get("notes") else "")
                if patient.get("notes") != notes:
                    raise ValueError("Final notes differ from source candidate.")
        write_json(directory / ("results-" + stamp + ".json"), results)
        write_json(directory / ("summary-" + stamp + ".json"), dict(counts))
        return dict(counts)
    finally:
        lock.unlink()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--clinic-id", required=True)
    parser.add_argument("--clinic-name", required=True)
    parser.add_argument("--email", required=True)
    parser.add_argument("--limit", type=int, required=True)
    parser.add_argument("--deleted-identities", type=Path)
    args = parser.parse_args()
    if args.limit < 1:
        parser.error("Limit must be positive.")
    password = os.environ.get("HEALTHMANAGER_IMPORT_PASSWORD") or getpass.getpass("HealthManager password: ")
    api = Api(args.base_url, args.email, password, args.clinic_id, args.clinic_name)
    deleted = None
    if args.deleted_identities:
        identities = json.loads(args.deleted_identities.read_text(encoding="utf-8"))
        receipt = json.loads((args.stage / "server-backup-receipt.json").read_text(encoding="utf-8"))
        if (identities["clinicId"] != args.clinic_id or receipt["counts"]["clinicId"] != args.clinic_id
            or not receipt["archiveVerified"] or len(identities["patients"]) != receipt["counts"]["deletedPatients"]
            or not 0 <= (datetime.now(timezone.utc) - datetime.fromisoformat(receipt["checkedAtUtc"])).total_seconds() < 86400):
            raise ValueError("Deleted identity preflight is inconsistent or expired.")
        deleted = identities["patients"]
    print(json.dumps(run(args.stage, api, args.limit, deleted)), flush=True)
