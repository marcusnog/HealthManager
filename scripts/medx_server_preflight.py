"""Back up the confirmed HealthManager database; expose only aggregate metadata."""
from datetime import datetime, timezone
import hashlib
import base64
import json
import os
import ipaddress
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
print(command("uptime"))
metrics = subprocess.run(["k3s", "kubectl", "-n", "healthmanager", "top", "pods"], capture_output=True, text=True)
if metrics.returncode == 0:
    print(metrics.stdout)


assert connection.get("Host") == "postgres.healthmanager.svc.cluster.local"
assert connection.get("Database") == "healthmanager"
clinic_id = "11111111-1111-1111-1111-111111111111"
sql = '''SELECT json_build_object(
 'clinicId', c."Id", 'clinicName', c."Name",
 'activePatients', (SELECT count(*) FROM "Patients" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NULL),
 'deletedPatients', (SELECT count(*) FROM "Patients" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NOT NULL),
 'deletedDoctors', (SELECT count(*) FROM "Doctors" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NOT NULL),
 'deletedProducts', (SELECT count(*) FROM "Products" WHERE "ClinicId"=c."Id" AND "DeletedAt" IS NOT NULL),
 'clinicalExamEntriesSinceMigration', (SELECT count(*) FROM "ClinicalEntries" WHERE "ClinicId"=c."Id"
 AND "Kind" IN ('ExamOrder','ExamResult') AND "CreatedAt">='2026-10-29T00:00:00Z'::timestamptz))
 FROM "Clinics" c WHERE c."Id"='11111111-1111-1111-1111-111111111111';'''
query = subprocess.run(["k3s", "kubectl", "-n", "healthmanager", "exec", "-i", "postgres-0", "--",
    "sh", "-c", 'exec psql -X -v ON_ERROR_STOP=1 -At -U "$POSTGRES_USER" -d healthmanager'],
    input=sql, capture_output=True, text=True)
if query.returncode:
    raise RuntimeError("Tenant aggregate check failed; no backup or import performed.")
counts = json.loads(query.stdout)
assert counts["clinicName"] == "Clinica Piloto" and counts["activePatients"] >= 2034

# Temporary key permits only forwarding to the existing web/API service, never a shell.
service = json.loads(command("k3s", "kubectl", "-n", "healthmanager", "get", "service", "web", "-o", "json"))
service_ip = service["spec"]["clusterIP"]
ports = service["spec"]["ports"]
assert ipaddress.ip_address(service_ip).is_private and len(ports) == 1 and ports[0]["protocol"] == "TCP"
service_port = ports[0]["port"]
key = Path("/opt/healthmanager-backups/medx-20261029/tunnel.pub").read_text().strip()
assert key.startswith("ssh-ed25519 ") and " medx-import-" in key and "\n" not in key
authorized = Path("/root/.ssh/authorized_keys")
lines = authorized.read_text().splitlines() if authorized.exists() else []
lines = [line for line in lines if key.split()[1] not in line.split()]
lines.append(f'restrict,port-forwarding,permitopen="{service_ip}:{service_port}",command="false" ' + key)
authorized.write_text("\n".join(lines) + "\n")
authorized.chmod(0o600)
print("MEDX_TUNNEL " + json.dumps({"host": "157.254.207.208", "serviceIp": service_ip, "servicePort": service_port,
    "hostKey": Path("/etc/ssh/ssh_host_ed25519_key.pub").read_text().strip()}))

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
 FROM "Products" WHERE "ClinicId"='11111111-1111-1111-1111-111111111111' AND "DeletedAt" IS NOT NULL),
 'appointmentTypes',(SELECT coalesce(json_agg(json_build_object('id',"Id",'name',"Name")),'[]'::json)
 FROM "AppointmentTypes" WHERE "ClinicId"='11111111-1111-1111-1111-111111111111' AND "DeletedAt" IS NOT NULL),
 'expenseCategories',(SELECT coalesce(json_agg(json_build_object('id',"Id",'name',"Name")),'[]'::json)
 FROM "ExpenseCategories" WHERE "ClinicId"='11111111-1111-1111-1111-111111111111' AND "DeletedAt" IS NOT NULL));'''
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

# Resolve the existing documents PVC; read only this tenant's migration-prefixed files.
pvc = json.loads(command("k3s", "kubectl", "-n", "healthmanager", "get", "pvc", "documents", "-o", "json"))
pv = json.loads(command("k3s", "kubectl", "get", "pv", pvc["spec"]["volumeName"], "-o", "json"))
volume = pv["spec"].get("hostPath") or pv["spec"].get("local")
if not volume:
    raise RuntimeError("Documents volume is not a local filesystem; use download verification.")
root = Path(volume["path"]).resolve()
sql = '''SELECT coalesce(json_agg(json_build_object('documentId',"Id",'patientId',"PatientId",'fileName',"FileName",'size',"SizeInBytes",'path',"StoragePath")),'[]'::json)
FROM "PatientDocuments" WHERE "ClinicId"='11111111-1111-1111-1111-111111111111'
AND "DeletedAt" IS NULL AND "FileName" LIKE 'medx16925-%';'''
query = subprocess.run(["k3s", "kubectl", "-n", "healthmanager", "exec", "-i", "postgres-0", "--",
    "sh", "-c", 'exec psql -X -v ON_ERROR_STOP=1 -At -U "$POSTGRES_USER" -d healthmanager'],
    input=sql.encode(), capture_output=True)
if query.returncode:
    raise RuntimeError("Document inventory query failed.")
inventory = json.loads(query.stdout)
verified = []
for row in inventory:
    expected = f"clinics/{clinic_id}/patients/{row['patientId']}/{row['fileName']}"
    path = (root / row["path"]).resolve()
    if row["path"] != expected or not path.is_relative_to(root) or not path.is_file():
        raise RuntimeError("Document storage path or file is inconsistent.")
    if path.stat().st_size != row["size"]:
        raise RuntimeError("Document byte count differs from database.")
    with path.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    verified.append({k: v for k, v in row.items() if k != "path"} | {"sha256": digest})
for offset in range(0, len(verified), 100):
    payload = json.dumps({"clinicId": clinic_id, "offset": offset, "total": len(verified),
                          "files": verified[offset:offset + 100]}).encode()
    encrypted = subprocess.run(["openssl", "cms", "-encrypt", "-binary", "-aes256", "-outform", "DER",
        str(directory / "recipient.pem")], input=payload, capture_output=True)
    if encrypted.returncode:
        raise RuntimeError("Integrity report encryption failed.")
    print("MEDX_ENCRYPTED_FILE_HASHES " + base64.b64encode(encrypted.stdout).decode())
print("MEDX_VERIFIED_FILE_COUNT " + str(len(verified)))
