# health-api: a first Docker + Kubernetes + Helm project

A deliberately tiny end-to-end project: a .NET minimal API, packaged as a
container image, deployed to a local Kubernetes cluster by a Helm chart with
separate dev and prod configuration.

No CI, no ingress, no service mesh, no database. Everything here exists to
teach one idea.

---

## 1. What's in the repo

```
helm-chart-practice/
├─ src/HealthApi/              the application
│  ├─ Program.cs               ~85 lines: the entire app
│  ├─ Dockerfile               multi-stage build -> runtime image
│  └─ .dockerignore
├─ charts/health-api/          the Helm chart
│  ├─ Chart.yaml               chart name, version, appVersion
│  ├─ values.yaml              defaults
│  ├─ values-dev.yaml          dev overrides
│  ├─ values-prod.yaml         prod overrides
│  └─ templates/               the Kubernetes objects, as templates
│     ├─ _helpers.tpl          shared naming/label snippets
│     ├─ namespace.yaml
│     ├─ serviceaccount.yaml
│     ├─ role.yaml             least-privilege RBAC
│     ├─ rolebinding.yaml
│     ├─ deployment.yaml       the pods
│     ├─ service.yaml          stable in-cluster address
│     └─ NOTES.txt             printed after `helm install`
└─ docs/walkthrough.md         this file
```

---

## 2. The application

An ASP.NET Core minimal API with four routes:

| Route | Purpose |
|---|---|
| `GET /` | Returns which pod answered — name, namespace, node, environment |
| `GET /healthz/live` | **Liveness.** Always 200. Runs no checks. |
| `GET /healthz/ready` | **Readiness.** 200 normally, 503 when marked not ready. |
| `POST /admin/ready/{true or false}` | Flips readiness on demand |

That last route is a learning aid, not a production pattern — it's
unauthenticated and mutates global state. It exists so you can *watch*
Kubernetes react to a pod going unready.

The app listens on **port 8080**. Pod identity arrives through environment
variables that Kubernetes injects (the "downward API").

---

## 3. The container

`src/HealthApi/Dockerfile` is a **multi-stage build**:

1. **build stage** — starts from the ~800 MB .NET SDK image, restores NuGet
   packages, compiles, publishes
2. **runtime stage** — starts from the ~220 MB ASP.NET runtime image and copies
   in *only* the published output

The SDK, your source code, and the NuGet cache never reach the final image.
That's the whole point of multi-stage.

Three details worth remembering:

**Copy the `.csproj` and restore *before* copying the source.** Docker caches
each instruction as a layer and reuses it if nothing it depends on changed.
Restore is slow and its inputs rarely change, so isolating it means edits to
`Program.cs` rebuild in seconds instead of half a minute.

**`USER $APP_UID` — the number, not the name.** The Microsoft base image
creates a user `app` at uid 1654 but does *not* switch to it, so without this
line the container runs as root. And it must be numeric: the kubelet cannot
resolve a username, so `USER app` would make the pod fail to start once we set
`runAsNonRoot: true`.

**`.dockerignore` matters.** Without it, your local `bin/` and `obj/` folders
get shipped into the build context and can overwrite what the build produced.

---

## 4. The Helm chart

A Helm chart is a set of **templated** Kubernetes YAML files plus a **values**
file that fills in the blanks. One chart, many environments.

### What each object does

| Object | Why it's here |
|---|---|
| **Namespace** | A folder for the release's objects. Keeps dev and prod isolated. |
| **ServiceAccount** | The identity the pods run as, from Kubernetes' point of view. |
| **Role** | A namespace-scoped list of permissions. |
| **RoleBinding** | Attaches the Role to the ServiceAccount. A Role alone grants nothing. |
| **Deployment** | Declares "I want N copies of this container" and keeps it true. |
| **Service** | One stable DNS name and IP in front of whichever pods are ready. |

### dev vs prod

| | dev | prod |
|---|---|---|
| Namespace | `health-api-dev` | `health-api-prod` |
| Replicas | 1 | 3 |
| Image tag | `dev` | `1.0.0` (pinned) |
| `ASPNETCORE_ENVIRONMENT` | Development | Production |
| Log level | Debug | Information |
| Resources | requests only | requests **and** limits |

Both can run in the same cluster at the same time. Installing them side by side
is the single most useful thing you can do with this project.

### Security posture

The chart applies things a real production chart should:

- runs as non-root uid 1654, never root
- `readOnlyRootFilesystem: true`, with a writable `emptyDir` at `/tmp`
  because .NET needs one
- all Linux capabilities dropped
- `allowPrivilegeEscalation: false`
- no ServiceAccount token mounted (the app never calls the Kubernetes API)

---

## 5. Key concepts, in plain terms

**Image vs container.** An image is a filesystem snapshot — a template. A
container is one running instance of it. `docker build` makes an image;
`docker run` makes a container from it.

**Containers you cannot see.** `docker run --rm` deletes the container the
moment it exits, so short-lived test runs leave nothing in Docker Desktop's
Containers list. The *image* still persists under Images.

**kind has its own image store.** A kind "node" is itself a container with a
private image store. An image sitting in Docker Desktop is **not** visible to
the cluster. You must run `kind load docker-image` after *every* rebuild, even
if the tag did not change. This is the single most common source of
`ErrImagePull` on a beginner's first cluster.

**Liveness vs readiness.** Two different questions:

- *Liveness* — "is this process wedged?" Failing it **restarts the container.**
- *Readiness* — "should this pod get traffic right now?" Failing it **removes
  the pod from the Service**, with no restart.

**Never check dependencies in a liveness probe.** If liveness pings your
database and the database blips, every pod fails liveness simultaneously and
Kubernetes restarts your entire fleet — turning a recoverable dependency hiccup
into a self-inflicted outage. Our `/healthz/live` deliberately checks nothing.

**Probes are not monitoring.** They are control signals telling Kubernetes what
to do. Real production monitoring is a separate layer — metrics, traces, alerts.
Nobody gets paged because a probe failed.

**A Service is a stable front door.** Pods come and go with changing IPs. The
Service keeps one address and routes to whichever pods are currently *ready*.

**Role + RoleBinding always come as a pair.** A Role is just a list of
permissions sitting on a shelf. The RoleBinding hands it to someone.

**Chart version vs appVersion.** `version` is the version of the chart's
templates. `appVersion` is the version of the software it deploys. They move
independently.

---

## 6. Running it on your other machine

### Prerequisites

Docker Desktop (running), `kind`, `kubectl`, `helm`, .NET 10 SDK.

Confirm all four:

```powershell
docker version; kind version; kubectl version --client; helm version
```

> **PowerShell note:** in Windows PowerShell 5.1, `curl` is an alias for
> `Invoke-WebRequest` and will choke on flags like `-X`. Use **`curl.exe`**
> explicitly — the examples below do.

### Step 1 — Build the images

From the repo root. Two tags, because the prod values pin `1.0.0`:

```powershell
docker build -t health-api:dev src/HealthApi
```

```powershell
docker build -t health-api:1.0.0 src/HealthApi
```

Confirm the container runs as a non-root user — this must print
`uid=1654(app)`:

```powershell
docker run --rm --entrypoint id health-api:dev
```

### Step 2 — Smoke test outside Kubernetes

```powershell
docker run -d --name health-api-test -p 8080:8080 -e POD_NAME=in-container health-api:dev
```

```powershell
curl.exe -s http://localhost:8080/
```

```powershell
docker rm -f health-api-test
```

### Step 3 — Create the cluster and load the images

```powershell
kind create cluster --name helm-practice
```

```powershell
kind load docker-image health-api:dev --name helm-practice
```

```powershell
kind load docker-image health-api:1.0.0 --name helm-practice
```

```powershell
kubectl get nodes
```

### Step 4 — Inspect the chart before installing anything

Catches syntax errors:

```powershell
helm lint ./charts/health-api -f ./charts/health-api/values-dev.yaml
```

Renders the final YAML **without touching the cluster**. Read this output — it
is the best way to understand what a chart actually does:

```powershell
helm template health-api-dev ./charts/health-api -f ./charts/health-api/values-dev.yaml
```

Compare the two environments and see exactly what differs:

```powershell
helm template health-api-prod ./charts/health-api -f ./charts/health-api/values-prod.yaml
```

### Step 5 — Install dev

```powershell
helm install health-api-dev ./charts/health-api -f ./charts/health-api/values-dev.yaml
```

```powershell
kubectl get all -n health-api-dev
```

Wait until the pod shows `1/1 Running`. If it does not, jump to Troubleshooting.

> **A quirk to expect:** because the chart creates its own namespace rather
> than being installed with `--namespace`, Helm records the *release* in the
> `default` namespace while the *objects* live in `health-api-dev`. So
> `helm list` shows it under `default`, and `helm uninstall health-api-dev`
> needs no `-n` flag. Use `helm list --all-namespaces` to avoid confusion.
> The usual convention — `helm install --namespace X --create-namespace` —
> keeps the two aligned, at the cost of the namespace not being part of the
> chart. Both patterns are in use; this project does it the less common way
> on purpose, so you see the trade-off.

### Step 6 — Exercise it

Leave this running in one terminal:

```powershell
kubectl port-forward -n health-api-dev svc/health-api-dev 8080:80
```

In a second terminal:

```powershell
curl.exe -s http://localhost:8080/
```

```powershell
curl.exe -s -o NUL -w "live=%{http_code}\n" http://localhost:8080/healthz/live
```

```powershell
curl.exe -s -o NUL -w "ready=%{http_code}\n" http://localhost:8080/healthz/ready
```

Look at the logs and the pod's full spec:

```powershell
kubectl logs -n health-api-dev -l app.kubernetes.io/instance=health-api-dev
```

```powershell
kubectl describe pod -n health-api-dev -l app.kubernetes.io/instance=health-api-dev
```

### Step 7 — Install prod alongside it

```powershell
helm install health-api-prod ./charts/health-api -f ./charts/health-api/values-prod.yaml
```

```powershell
helm list --all-namespaces
```

```powershell
kubectl get pods -n health-api-prod -o wide
```

Three pods this time, in a separate namespace, from the same chart.

### Step 8 — Watch readiness control traffic

This is the payoff. List the prod pods:

```powershell
kubectl get pods -n health-api-prod
```

All three pod IPs are currently listed as endpoints:

```powershell
kubectl get endpoints -n health-api-prod
```

Port-forward directly to **one pod** (substitute the real name), leaving it
running:

```powershell
kubectl port-forward -n health-api-prod pod/PASTE-POD-NAME-HERE 8081:8080
```

From a second terminal, mark just that pod unready:

```powershell
curl.exe -s -X POST http://localhost:8081/admin/ready/false
```

Within a few seconds that pod's IP disappears from the endpoint list, while the
other two remain:

```powershell
kubectl get endpoints -n health-api-prod
```

Crucially, the pod is **not** restarted — check that `RESTARTS` is still 0 and
`READY` now reads `0/1`:

```powershell
kubectl get pods -n health-api-prod
```

Then bring it back:

```powershell
curl.exe -s -X POST http://localhost:8081/admin/ready/true
```

That is the entire liveness/readiness distinction, visible in about 30 seconds.

### Step 9 — Verify the RBAC really is least-privilege

The Role grants read access to configmaps and nothing else. The first should
print `yes`, the second `no`:

```powershell
kubectl auth can-i get configmaps -n health-api-dev --as=system:serviceaccount:health-api-dev:health-api-dev
```

```powershell
kubectl auth can-i get secrets -n health-api-dev --as=system:serviceaccount:health-api-dev:health-api-dev
```

### Step 10 — Upgrade and roll back

Change something without reinstalling:

```powershell
helm upgrade health-api-dev ./charts/health-api -f ./charts/health-api/values-dev.yaml --set replicaCount=3
```

```powershell
kubectl get pods -n health-api-dev
```

```powershell
helm history health-api-dev
```

```powershell
helm rollback health-api-dev 1
```

### Step 11 — Clean up

```powershell
helm uninstall health-api-dev
```

```powershell
helm uninstall health-api-prod
```

```powershell
kind delete cluster --name helm-practice
```

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `ErrImagePull` / `ImagePullBackOff` | Image never loaded into kind | `kind load docker-image health-api:dev --name helm-practice` |
| Pod `CrashLoopBackOff` | App failing at startup | `kubectl logs -n <ns> <pod> --previous` |
| `container has runAsNonRoot and image has non-numeric user` | Dockerfile used `USER app` instead of `USER $APP_UID` | Use the numeric uid |
| Pod stuck `0/1 Running`, never ready | Readiness probe failing | `kubectl describe pod` and read the Events at the bottom |
| `INSTALLATION FAILED: ... already exists` | Previous release was not removed | `helm list -A` then `helm uninstall` |
| `helm lint` template errors | Typo in a template | The error names the file and line |
| Port-forward dies | Pod was replaced | Re-run it |
| Built image missing from Docker Desktop | UI filter, or a `docker-container` buildx driver | Check the Local tab; `docker buildx ls` |

**The two commands that diagnose almost everything:**

```powershell
kubectl describe pod -n <namespace> <pod-name>
```

```powershell
kubectl logs -n <namespace> <pod-name>
```

`describe` ends with an Events section that usually states the problem in plain
English.

---

## 8. Glossary

| Term | Meaning |
|---|---|
| **Image** | A packaged filesystem + startup command. A template. |
| **Container** | A running instance of an image. |
| **Pod** | Kubernetes' smallest unit — one or more containers sharing a network address. |
| **Deployment** | Declares a desired number of identical pods and maintains it. |
| **Service** | A stable address load-balancing across ready pods. |
| **Namespace** | A folder grouping objects inside a cluster. |
| **ServiceAccount** | An identity a pod runs as. |
| **Role / RoleBinding** | A permission list, and the thing that grants it to an identity. |
| **kubelet** | The agent on each node that starts containers and runs probes. |
| **Chart** | A packaged, templated set of Kubernetes manifests. |
| **Release** | One installation of a chart, with a name. |
| **Values** | The configuration fed into a chart's templates. |
| **kind** | "Kubernetes IN Docker" — a throwaway cluster running inside containers. |

---

## 9. Things deliberately left out

Worth knowing these exist, and that their absence here is a choice:

- **Ingress** — real external access. We use `port-forward` instead.
- **ConfigMap / Secret** — externalized configuration. We use env vars.
- **HorizontalPodAutoscaler** — replica count by load. Ours is fixed.
- **PodDisruptionBudget** — protects availability during node maintenance.
- **NetworkPolicy** — restricts pod-to-pod traffic. Everything is open here.
- **A container registry** — we side-load into kind rather than push/pull.
- **CI/CD** — no pipeline; every step above is manual on purpose.
