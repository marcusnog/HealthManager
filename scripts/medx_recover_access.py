"""Restart only the existing persistent HealthManager Funnel proxy and check public access."""
import json
import subprocess
import time

base = ["k3s", "kubectl", "-n", "healthmanager"]
deployment = json.loads(subprocess.check_output(base + ["get", "deployment", "healthmanager-tailscale", "-o", "json"]))
spec = deployment["spec"]["template"]["spec"]
assert any(v.get("persistentVolumeClaim", {}).get("claimName") == "healthmanager-tailscale-state" for v in spec["volumes"])
assert len(spec["containers"]) == 1 and "tailscale" in spec["containers"][0]["image"]
assert any(m["name"] == "state" and m["mountPath"] == "/var/lib/tailscale" for m in spec["containers"][0]["volumeMounts"])
subprocess.run(base + ["rollout", "restart", "deployment/healthmanager-tailscale"], check=True, timeout=30)
subprocess.run(base + ["rollout", "status", "deployment/healthmanager-tailscale", "--timeout=120s"], check=True, timeout=135)
for attempt in range(3):
    result = subprocess.run(["curl", "-sS", "-o", "/dev/null", "--connect-timeout", "5", "--max-time", "10",
                             "-w", "%{http_code}", "https://healthmanager.tailafcfbf.ts.net/backend/health"],
                            capture_output=True, text=True, timeout=15)
    print(json.dumps({"attempt": attempt + 1, "exitCode": result.returncode, "httpStatus": result.stdout}), flush=True)
    if result.returncode == 0 and result.stdout == "200":
        break
    time.sleep(5)
else:
    raise RuntimeError("Funnel proxy restarted but public access is still unavailable; do not retry restarts blindly")
