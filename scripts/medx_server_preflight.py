"""Back up the confirmed HealthManager database; expose only aggregate metadata."""
from datetime import datetime, timezone
import hashlib
import base64
import json
import os
from pathlib import Path
import subprocess


def command(*args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError("Preflight command failed; output withheld to protect secrets.")
    return result.stdout


objects = json.loads(command("k3s", "kubectl", "-n", "healthmanager", "get",
                             "deployments,statefulsets,pods", "-o", "json"))
report = []
for obj in objects["items"]:
    spec = obj["spec"] if obj["kind"] == "Pod" else obj["spec"]["template"]["spec"]
    report.append({"kind": obj["kind"], "name": obj["metadata"]["name"],
        "containers": [{"name": c["name"], "image": c["image"],
                        "envNames": [e["name"] for e in c.get("env", [])],
                        "mounts": c.get("volumeMounts", [])} for c in spec["containers"]],
        "volumes": [{k: v for k, v in volume.items() if k in ("name", "hostPath", "persistentVolumeClaim", "emptyDir")}
                    for volume in spec.get("volumes", [])]})
print(json.dumps(report))
raw = command("k3s", "kubectl", "-n", "healthmanager", "exec", "deployment/api", "--", "printenv", "DATABASE_URL")
connection = dict(part.split("=", 1) for part in raw.strip().split(";") if "=" in part)
print(json.dumps({"database": {k: connection.get(k) for k in ("Host", "Port", "Database")}}))
print(command("df", "-h", "/opt"))

assert connection.get("Host") == "postgres.healthmanager.svc.cluster.local"
assert connection.get("Database") == "healthmanager"
clinic_id = "11111111-1111-1111-1111-111111111111"
sql = '''SELECT json_build_object(
 'clinicId', c."Id", 'clinicName', c."Name",
 'activePatients', (SELECT count(*) FROM "Patients" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NULL),
 'deletedPatients', (SELECT count(*) FROM "Patients" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NOT NULL),
 'deletedDoctors', (SELECT count(*) FROM "Doctors" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NOT NULL),
 'deletedProducts', (SELECT count(*) FROM "Products" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NOT NULL))
 FROM "Clinics" c WHERE c."Id"='11111111-1111-1111-1111-111111111111';'''
query = subprocess.run(["k3s", "kubectl", "-n", "healthmanager", "exec", "-i", "postgres-0", "--",
    "sh", "-c", 'exec psql -X -v ON_ERROR_STOP=1 -At -U "$POSTGRES_USER" -d healthmanager'],
    input=sql, capture_output=True, text=True)
if query.returncode:
    raise RuntimeError("Tenant aggregate check failed; no backup or import performed.")
counts = json.loads(query.stdout)
assert counts["clinicName"] == "Clinica Piloto" and counts["activePatients"] >= 2034
os.umask(0o077)
directory = Path("/opt/healthmanager-backups/medx-20261029")
directory.mkdir(parents=True, exist_ok=True)
stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
backup = directory / (stamp + ".dump")
with backup.open("xb") as output:
    result = subprocess.run(["k3s", "kubectl", "-n", "healthmanager", "exec", "postgres-0", "--",
        "sh", "-c", 'exec pg_dump -Fc -U "$POSTGRES_USER" -d healthmanager'], stdout=output, stderr=subprocess.PIPE)
if result.returncode or backup.stat().st_size == 0:
    raise RuntimeError("Database backup failed.")
with backup.open("rb") as source:
    result = subprocess.run(["k3s", "kubectl", "-n", "healthmanager", "exec", "-i", "postgres-0", "--",
        "pg_restore", "--file=/dev/null"], stdin=source, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
if result.returncode:
    raise RuntimeError("Database archive verification failed.")
with backup.open("rb") as source:
    digest = hashlib.file_digest(source, "sha256").hexdigest()
receipt = {"checkedAtUtc": datetime.now(timezone.utc).isoformat(), "database": "healthmanager",
           "counts": counts, "backupPath": str(backup), "backupSha256": digest,
           "backupBytes": backup.stat().st_size, "archiveVerified": True,
           "restoreTestPerformed": False}
backup.with_suffix(".json").write_text(json.dumps(receipt, indent=2))
print("MEDX_BACKUP_RECEIPT " + json.dumps(receipt))
print(command("k3s", "kubectl", "-n", "healthmanager", "exec", "deployment/api", "--",
              "df", "-Pk", "/var/lib/healthmanager/documents"))

sql = '''SELECT json_build_object('clinicId','11111111-1111-1111-1111-111111111111',
 'patients',(SELECT coalesce(json_agg(json_build_object('id',"Id",'name',"Name",'cpf',"Cpf",'birthDate',"BirthDate")),'[]'::json)
 FROM "Patients" WHERE "ClinicId"='11111111-1111-1111-1111-111111111111' AND "DeletedAt" IS NOT NULL),
 'doctors',(SELECT coalesce(json_agg(json_build_object('id',"Id",'name',"Name",'crm',"Crm")),'[]'::json)
 FROM "Doctors" WHERE "ClinicId"='11111111-1111-1111-1111-111111111111' AND "DeletedAt" IS NOT NULL),
 'products',(SELECT coalesce(json_agg(json_build_object('id',"Id",'name',"Name")),'[]'::json)
 FROM "Products" WHERE "ClinicId"='11111111-1111-1111-1111-111111111111' AND "DeletedAt" IS NOT NULL));'''
query = subprocess.run(["k3s", "kubectl", "-n", "healthmanager", "exec", "-i", "postgres-0", "--",
    "sh", "-c", 'exec psql -X -v ON_ERROR_STOP=1 -At -U "$POSTGRES_USER" -d healthmanager'],
    input=sql.encode(), capture_output=True)
if query.returncode:
    raise RuntimeError("Deleted-identity check failed.")
json.loads(query.stdout)
encrypted = subprocess.run(["openssl", "cms", "-encrypt", "-binary", "-aes256", "-outform", "DER",
    str(directory / "recipient.pem")], input=query.stdout, capture_output=True)
if encrypted.returncode:
    raise RuntimeError("Identity encryption failed; no identity data released.")
print("MEDX_ENCRYPTED_IDENTITIES " + base64.b64encode(encrypted.stdout).decode())
