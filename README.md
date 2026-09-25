# helm-chart-practice

A minimal, self-contained project for learning Docker, Kubernetes, and Helm
hands-on: a .NET 10 minimal API with health endpoints, containerized, and
deployed to a local [kind](https://kind.sigs.k8s.io/) cluster by a Helm chart
with separate dev and prod values.

Deliberately small — no CI, no ingress, no service mesh, no database.

## Layout

| Path | What it is |
|---|---|
| [src/HealthApi](src/HealthApi) | The application and its `Dockerfile` |
| [charts/health-api](charts/health-api) | The Helm chart |
| [docs/walkthrough.md](docs/walkthrough.md) | **Start here** — concepts, key learnings, and step-by-step instructions |

## Quick start

Full detail, including troubleshooting, is in
[docs/walkthrough.md](docs/walkthrough.md).

```powershell
docker build -t health-api:dev src/HealthApi
```

```powershell
kind create cluster --name helm-practice
```

```powershell
kind load docker-image health-api:dev --name helm-practice
```

```powershell
helm install health-api-dev ./charts/health-api -f ./charts/health-api/values-dev.yaml
```

```powershell
kubectl port-forward -n health-api-dev svc/health-api-dev 8080:80
```

Then `curl.exe http://localhost:8080/`.

## Endpoints

| Route | Purpose |
|---|---|
| `GET /` | Reports which pod answered |
| `GET /healthz/live` | Liveness probe target — always 200 |
| `GET /healthz/ready` | Readiness probe target — 200 or 503 |
| `POST /admin/ready/{true or false}` | Flips readiness so you can watch Kubernetes react |

The `/admin/ready` route is an unauthenticated learning aid, not a production
pattern.
