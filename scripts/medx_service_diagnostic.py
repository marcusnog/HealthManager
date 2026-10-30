"""Read-only service and public-access diagnostics; never print credentials or patient data."""
import json
import subprocess


def command(args):
    try:
        result = subprocess.run(args, capture_output=True, text=True, timeout=20)
        return {"exitCode": result.returncode, "output": result.stdout.strip()}
    except subprocess.TimeoutExpired:
        return {"timeout": True}
    except FileNotFoundError:
        return {"unavailable": args[0]}


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

inventory = command(["docker", "ps", "--format", '{{.ID}} {{.Names}} {{.Image}}'])
print(json.dumps({"containers": inventory}))
if inventory.get("exitCode") == 0:
    for line in inventory["output"].splitlines():
        if "tailscale" in line.lower():
            identifier = line.split()[0]
            for name, args in {"state": ["status", "--json"], "funnel": ["funnel", "status"]}.items():
                result = command(["docker", "exec", identifier, "tailscale", *args])
                if name == "state" and result.get("exitCode") == 0:
                    value = json.loads(result["output"])
                    result = {"backendState": value.get("BackendState"), "online": value.get("Self", {}).get("Online"),
                              "healthWarningCount": len(value.get("Health") or [])}
                print(json.dumps({"container": identifier, name: result}))
pods = command(["k3s", "kubectl", "get", "pods", "-A", "-o", "json"])
if pods.get("exitCode") == 0:
    for pod in json.loads(pods["output"])["items"]:
        for container in pod["spec"]["containers"]:
            if "tailscale" not in container["image"].lower():
                continue
            namespace, name = pod["metadata"]["namespace"], pod["metadata"]["name"]
            base = ["k3s", "kubectl", "-n", namespace, "exec", name, "-c", container["name"], "--", "tailscale"]
            state = command(base + ["status", "--json"])
            if state.get("exitCode") == 0:
                value = json.loads(state["output"])
                state = {"backendState": value.get("BackendState"), "online": value.get("Self", {}).get("Online"),
                         "healthWarningCount": len(value.get("Health") or [])}
            print(json.dumps({"pod": name, "namespace": namespace, "state": state,
                              "funnel": command(base + ["funnel", "status"])}))
