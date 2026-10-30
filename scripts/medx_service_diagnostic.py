"""Read-only service and public-access diagnostics; never print credentials or patient data."""
import json
import subprocess


def command(args):
    try:
        result = subprocess.run(args, capture_output=True, text=True, timeout=20)
        return {"exitCode": result.returncode, "output": result.stdout.strip()}
    except subprocess.TimeoutExpired:
        return {"timeout": True}


state = command(["tailscale", "status", "--json"])
if state.get("exitCode") == 0:
    value = json.loads(state["output"])
    state = {"backendState": value.get("BackendState"), "online": value.get("Self", {}).get("Online"),
             "healthWarningCount": len(value.get("Health") or [])}
print(json.dumps({"tailscale": state}))
for name, args in {
    "tailscaledService": ["systemctl", "is-active", "tailscaled"],
    "funnel": ["tailscale", "funnel", "status"],
    "serve": ["tailscale", "serve", "status"],
    "load": ["uptime"],
    "memory": ["free", "-m"],
    "pods": ["k3s", "kubectl", "-n", "healthmanager", "get", "pods"],
    "metrics": ["k3s", "kubectl", "-n", "healthmanager", "top", "pods"],
    "publicAccess": ["curl", "-sS", "-o", "/dev/null", "--connect-timeout", "5", "--max-time", "10",
                     "-w", "http=%{http_code} connect=%{time_connect} total=%{time_total}",
                     "https://healthmanager.tailafcfbf.ts.net/"]
}.items():
    print(json.dumps({name: command(args)}))
