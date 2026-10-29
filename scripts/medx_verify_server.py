"""Decrypt the server integrity report locally and reconcile uploaded MedX documents."""
import argparse
import base64
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess

from cryptography import x509
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.serialization import pkcs7

from medx_prepare import write_json


def reconcile(events, files):
    by_id = {f["documentId"]: f for f in files}
    if len(by_id) != len(files):
        raise ValueError("Duplicate document IDs in server report.")
    latest = {e["sourceKey"]: e for e in events}
    pending = []
    for event in latest.values():
        if event["status"] == "intent":
            raise ValueError("Unresolved upload intent; reconcile API outcome first.")
        if event["status"] not in ("uploaded", "created", "matched"):
            continue
        actual = by_id.get(event["documentId"])
        expected = {"patientId": event["targetPatientId"], "fileName": event["fileName"],
                    "size": event["bytes"], "sha256": event["sha256"]}
        if not actual or any(actual.get(k) != v for k, v in expected.items()):
            raise ValueError("Server bytes/metadata differ or a journal document is missing.")
        if event["status"] == "uploaded":
            pending.append(event | {"status": "created", "verification": "server-filesystem-sha256"})
    return pending


def verify(stage, certificate, run_id):
    result = subprocess.run(["gh", "run", "view", str(run_id), "--log"], capture_output=True, text=True, encoding="utf-8")
    if result.returncode:
        raise RuntimeError("Could not read server verification log.")
    cert = x509.load_pem_x509_certificate(certificate.read_bytes())
    key = serialization.load_pem_private_key((stage / "recipient-private.pem").read_bytes(), password=None)
    chunks = []
    receipt = None
    declared = None
    for line in result.stdout.splitlines():
        if "MEDX_ENCRYPTED_FILE_HASHES " in line:
            blob = base64.b64decode(line.split("MEDX_ENCRYPTED_FILE_HASHES ", 1)[1], validate=True)
            chunks.append(json.loads(pkcs7.pkcs7_decrypt_der(blob, cert, key, [])))
        elif "MEDX_BACKUP_RECEIPT " in line:
            receipt = json.loads(line.split("MEDX_BACKUP_RECEIPT ", 1)[1])
        elif "MEDX_VERIFIED_FILE_COUNT " in line:
            declared = int(line.split("MEDX_VERIFIED_FILE_COUNT ", 1)[1])
    directory = stage / "document-import"
    identity = json.loads((directory / "destination.json").read_text(encoding="utf-8"))
    if declared is None or not receipt or receipt["counts"]["clinicId"] != identity["clinicId"]:
        raise ValueError("Server verification is incomplete or belongs to another clinic.")
    files = []
    for chunk in sorted(chunks, key=lambda c: c["offset"]):
        if chunk["offset"] != len(files) or chunk["total"] != declared or chunk["clinicId"] != identity["clinicId"]:
            raise ValueError("Encrypted report chunks are missing or inconsistent.")
        files.extend(chunk["files"])
    if len(files) != declared:
        raise ValueError("Server report is truncated.")
    if (directory / "running.lock").exists():
        raise ValueError("Finish the upload batch before final reconciliation.")
    journal = directory / "journal.ndjson"
    events = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
    updates = reconcile(events, files)
    write_json(directory / f"server-files-{run_id}.json", files)
    # Upload intents are already durable; batch only repeatable read-only verification results.
    with journal.open("a", encoding="utf-8") as stream:
        for event in updates:
            stream.write(json.dumps(event | {"verificationRunId": run_id}) + "\n")
        stream.flush()
        os.fsync(stream.fileno())
    receipt["workflowRunId"] = run_id
    write_json(stage / "server-backup-receipt.json", receipt)
    summary = {"verifiedAtUtc": datetime.now(timezone.utc).isoformat(), "workflowRunId": run_id,
               "serverFiles": len(files), "newlyVerified": len(updates), "verifiedBytes": sum(f["size"] for f in files)}
    write_json(directory / "server-verification-summary.json", summary)
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--certificate", type=Path, required=True)
    parser.add_argument("--run-id", type=int, required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.stage, args.certificate, args.run_id)))
