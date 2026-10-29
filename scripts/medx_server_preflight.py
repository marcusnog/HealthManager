"""Read only: report HealthManager topology, never credentials or patient data."""
import json
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
