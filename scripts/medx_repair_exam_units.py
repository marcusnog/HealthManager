"""Correct journal-owned MedX exam units without overwriting independent edits."""
import argparse
from collections import Counter
from copy import deepcopy
from datetime import datetime, timezone
import getpass
import json
import os
from pathlib import Path

from medx_catalog import exam_candidate, matches, table
from medx_import_patients import Api, append_event
from medx_prepare import text, write_json


def latest(path):
    return {r["sourceKey"]: r for r in (json.loads(line) for line in path.read_text(encoding="utf-8").splitlines())} if path.exists() else {}


def without_revision(snapshot):
    return {k: v for k, v in snapshot.items() if k != "revision"}


def correction(row, subscription, owned, before, current, previous=None):
    candidate = exam_candidate(row, subscription)
    result = {"sourceKey": candidate["sourceKey"], "status": "held"}
    if candidate["status"] != "create":
        return result | {"reason": candidate["reason"]}
    if not owned or owned.get("status") not in ("created", "matched") or not before or not current:
        return result | {"reason": "missing_owned_snapshot"}
    if owned.get("resource") != "clinical-catalog" or owned.get("targetId") != before["id"] or current["id"] != before["id"]:
        raise ValueError("Exam ownership changed.")
    if matches(before, candidate):
        return result | {"status": "not_needed"}
    old_payload = deepcopy(candidate["payload"])
    old_unit = text(row.get("Peso"))
    old_payload["content"] = old_payload["content"].replace(
        "Unidade: " + text(row.get("Peso")) + "/" + text(row.get("Volume")) + "\n", "Unidade: " + old_unit + "\n", 1)
    old_payload["fields"][0]["unit"] = old_unit if len(old_unit) <= 30 else None
    if not matches(before, candidate | {"payload": old_payload}):
        return result | {"reason": "source_baseline_mismatch"}
    expected = deepcopy(before)
    expected["content"] = candidate["payload"]["content"]
    expected["fields"][0]["unit"] = candidate["payload"]["fields"][0]["unit"]
    payload = {k: expected[k] for k in ("kind", "folder", "title", "content", "fields")} | {"personal": False}
    result.update(targetId=before["id"], before=before, expected=expected, payload=payload)
    if previous:
        if any(previous.get(k) != result[k] for k in ("targetId", "before", "expected", "payload")):
            raise ValueError("Correction source or journal changed.")
        if previous["status"] == "verified" and current == previous["after"]:
            return result | {"status": "matched"}
        if previous["status"] == "intent" and without_revision(current) == without_revision(expected) and current["revision"] != before["revision"]:
            return result | {"status": "reconcile"}
        return result | {"reason": "previous_update_requires_review"}
    if current != before or not matches(before, owned):
        return result | {"reason": "independent_edit"}
    return result | {"status": "update"}


def run(stage, api, apply=False):
    directory = stage / "catalog-import"
    identity = json.loads((directory / "destination.json").read_text(encoding="utf-8"))
    if identity != {"baseUrl": api.base, "clinicId": api.clinic_id, "clinicName": api.clinic_name}:
        raise ValueError("Catalog destination changed.")
    receipt = json.loads((stage / "server-backup-receipt.json").read_text(encoding="utf-8"))
    if (receipt["counts"]["clinicId"] != api.clinic_id or not receipt["archiveVerified"]
        or not 0 <= (datetime.now(timezone.utc) - datetime.fromisoformat(receipt["checkedAtUtc"])).total_seconds() < 86400):
        raise ValueError("Verified recent server backup required.")
    lock = directory / "running.lock"
    with lock.open("x") as stream:
        stream.write(str(os.getpid()))
    try:
        manifest = json.loads((stage / "manifest.json").read_text(encoding="utf-8"))
        source = table(stage, manifest, "Exames_modelo.json")
        catalog_journal = directory / "journal.ndjson"
        repair_journal = directory / "exam-unit-corrections.ndjson"
        owned, previous, first = latest(catalog_journal), latest(repair_journal), {}
        for path in sorted(directory.glob("after-*.json")):
            for item in json.loads(path.read_text(encoding="utf-8"))["clinical-catalog"]:
                first.setdefault(item["id"], item)
        items = api.request("/clinical-catalog?kind=Exam")
        if len(items) >= 1000 or len({x["id"] for x in items}) != len(items):
            raise ValueError("Incomplete or duplicate exam snapshot.")
        current = {x["id"]: x for x in items}
        plans = []
        for row in source:
            key = exam_candidate(row, manifest["subscription"])["sourceKey"]
            target = owned.get(key, {}).get("targetId")
            plans.append(correction(row, manifest["subscription"], owned.get(key), first.get(target), current.get(target), previous.get(key)))
        if len({p["sourceKey"] for p in plans}) != len(plans):
            raise ValueError("Duplicate source exam identities.")
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
        write_json(directory / ("unit-repair-plan-" + stamp + ".json"), plans)
        if not apply:
            return dict(Counter(p["status"] for p in plans))
        verified = {}
        for plan in plans:
            if plan["status"] not in ("update", "reconcile"):
                continue
            if plan["status"] == "update":
                append_event(repair_journal, plan | {"status": "intent"})
                after = api.put("/clinical-catalog/" + plan["targetId"], plan["payload"] | {"revision": plan["before"]["revision"]})
            else:
                after = current[plan["targetId"]]
            if without_revision(after) != without_revision(plan["expected"]) or after["revision"] == plan["before"]["revision"]:
                raise ValueError("Corrected exam differs from expected snapshot.")
            append_event(catalog_journal, {"sourceKey": plan["sourceKey"], "resource": "clinical-catalog", "status": "created",
                "targetId": plan["targetId"], "payload": plan["payload"], "unitCorrected": True})
            append_event(repair_journal, plan | {"status": "verified", "after": after})
            verified[after["id"]] = after
            if len(verified) % 25 == 0:
                print(json.dumps({"unitsVerified": len(verified)}), flush=True)
        final = {x["id"]: x for x in api.request("/clinical-catalog?kind=Exam")}
        if set(final) != set(current) or any(final[key] != verified.get(key, value) for key, value in current.items()):
            raise ValueError("Final exam snapshot changed unexpectedly.")
        summary = dict(Counter(p["status"] for p in plans)) | {"newlyVerified": len(verified)}
        write_json(directory / ("unit-repair-after-" + stamp + ".json"), list(final.values()))
        write_json(directory / "exam-unit-correction-summary.json", summary)
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
    args = parser.parse_args()
    password = os.environ.get("HEALTHMANAGER_IMPORT_PASSWORD") or getpass.getpass("HealthManager password: ")
    api = Api(args.base_url, args.email, password, args.clinic_id, args.clinic_name)
    print(json.dumps(run(args.stage, api, args.apply)), flush=True)
