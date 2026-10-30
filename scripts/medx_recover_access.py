"""Try a temporary HTTPS listener for the same application; remove it if either ingress fails."""
import json
import os
from pathlib import Path
import socket
import subprocess
import time

base = ["k3s", "kubectl", "-n", "healthmanager"]
deployment = json.loads(subprocess.check_output(base + ["get", "deployment", "healthmanager-tailscale", "-o", "json"]))
spec = deployment["spec"]["template"]["spec"]
assert any(v.get("persistentVolumeClaim", {}).get("claimName") == "healthmanager-tailscale-state" for v in spec["volumes"])
assert len(spec["containers"]) == 1 and "tailscale" in spec["containers"][0]["image"]
assert any(m["name"] == "state" and m["mountPath"] == "/var/lib/tailscale" for m in spec["containers"][0]["volumeMounts"])
selector = ",".join(f"{key}={value}" for key, value in deployment["spec"]["selector"]["matchLabels"].items())
pods = json.loads(subprocess.check_output(base + ["get", "pods", "-l", selector, "-o", "json"]))["items"]
pods = [pod for pod in pods if not pod["metadata"].get("deletionTimestamp") and pod["status"]["phase"] == "Running"]
assert len(pods) == 1
execute = base + ["exec", pods[0]["metadata"]["name"], "--", "tailscale"]
before = json.loads(subprocess.check_output(execute + ["funnel", "status", "--json"]))
host = "healthmanager.tailafcfbf.ts.net"
assert set(before["TCP"]) == {"443"} and before["TCP"]["443"] == {"HTTPS": True}
assert before["Web"] == {host + ":443": {"Handlers": {"/": {"Proxy": "http://127.0.0.1:8081"}}}}
assert before["AllowFunnel"] == {host + ":443": True} and not before.get("Foreground")
os.umask(0o077)
with Path("/opt/healthmanager-backups/medx-20261029/funnel-config-before-alternate-port.json").open("x") as output:
    json.dump(before, output)
healthy = False
try:
    subprocess.run(execute + ["funnel", "--bg", "--https=8443", "http://127.0.0.1:8081"], check=True, timeout=30)
    time.sleep(5)
    after = json.loads(subprocess.check_output(execute + ["funnel", "status", "--json"]))
    assert after["Web"][host + ":443"] == before["Web"][host + ":443"]
    assert after["TCP"]["443"] == before["TCP"]["443"] and after["AllowFunnel"][host + ":443"] is True
    addresses = sorted({item[4][0] for item in socket.getaddrinfo(host, 443, socket.AF_INET, socket.SOCK_STREAM)})
    healthy = True
    for address in addresses:
        result = subprocess.run(["curl", "-sS", "-o", "/dev/null", "--resolve", f"{host}:8443:{address}",
                                 "--connect-timeout", "4", "--max-time", "8", "-w", "%{http_code}",
                                 f"https://{host}:8443/backend/health"], capture_output=True, text=True, timeout=12)
        print(json.dumps({"address": address, "port": 8443, "exitCode": result.returncode, "httpStatus": result.stdout}), flush=True)
        healthy = healthy and result.returncode == 0 and result.stdout == "200"
finally:
    if not healthy:
        subprocess.run(execute + ["funnel", "--bg", "--https=8443", "off"], check=True, timeout=20)
        restored = json.loads(subprocess.check_output(execute + ["funnel", "status", "--json"]))
        assert before == restored, 'Original Funnel configuration was not restored'
        print('ALTERNATE_LISTENER_REMOVED')
if not healthy:
    raise RuntimeError("Alternate listener did not restore both ingress paths; original configuration preserved")
print('ALTERNATE_LISTENER_HEALTHY https://' + host + ':8443/')
