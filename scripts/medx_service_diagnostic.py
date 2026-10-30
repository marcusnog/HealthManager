"""Read-only service and public-access diagnostics; never print credentials or patient data."""
import json
import subprocess
import socket


def command(args):
    try:
        result = subprocess.run(args, capture_output=True, text=True, timeout=20)
        return {"exitCode": result.returncode, "output": result.stdout.strip()}
    except subprocess.TimeoutExpired:
        return {"timeout": True}
    except FileNotFoundError:
        return {"unavailable": args[0]}


state = command(["tailscale", "status", "--json"])
public_host = "healthmanager.tailafcfbf.ts.net"
addresses = sorted({item[4][0] for item in socket.getaddrinfo(public_host, 443, socket.AF_INET, socket.SOCK_STREAM)})
print(json.dumps({"publicIPv4": addresses}))
for address in addresses:
    print(json.dumps({"publicRelay": address, "probe": command([
        "curl", "-sS", "-o", "/dev/null", "--resolve", f"{public_host}:443:{address}",
        "--connect-timeout", "4", "--max-time", "8", "-w",
        "http=%{http_code} connect=%{time_connect} tls=%{time_appconnect} total=%{time_total}",
        f"https://{public_host}/backend/health"])}))
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
            if namespace != "healthmanager":
                continue
            execute = base[:-1]
            print(json.dumps({"proxyHealth": command(execute + ["wget", "-q", "-O", "/dev/null", "-T", "5", "http://127.0.0.1:8081/backend/health"])}))
            print(json.dumps({"tailscaleDeployment": {
                "volumes": pod["spec"].get("volumes"), "mounts": container.get("volumeMounts"),
                "safeEnvironment": [e for e in container.get("env", []) if e["name"] in
                    ("TS_STATE_DIR", "TS_KUBE_SECRET", "TS_USERSPACE", "TS_SERVE_CONFIG")],
                "containerNames": [c["name"] for c in pod["spec"]["containers"]]}}))
            netcheck = command(base + ["netcheck", "--format=json"])
            if netcheck.get("exitCode") == 0:
                values = json.loads(netcheck["output"])
                netcheck = {k: values.get(k) for k in ("UDP", "IPv4", "IPv6", "PreferredDERP", "DERPLatency")}
            print(json.dumps({"network": netcheck}))
