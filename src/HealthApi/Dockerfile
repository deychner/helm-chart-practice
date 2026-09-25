# syntax=docker/dockerfile:1

# Build context is this directory (src/HealthApi), not the repo root:
#   docker build -t health-api:dev src/HealthApi

ARG DOTNET_VERSION=10.0

# ---- build -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy the project file alone and restore first. Restore is the slow step and
# its dependencies rarely change, so this layer stays cached across edits to
# Program.cs. Copying everything up front would invalidate it on every build.
COPY HealthApi.csproj .
RUN dotnet restore HealthApi.csproj

COPY . .
# UseAppHost=false skips the native launcher executable; we start the app with
# `dotnet HealthApi.dll` instead, which trims a few MB and one moving part.
RUN dotnet publish HealthApi.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false

# ---- runtime -----------------------------------------------------------
# For a smaller, shell-free image later: swap in 10.0-noble-chiseled. It is
# more secure but you lose `kubectl exec -- sh`, which is worth keeping while
# you are still learning to poke at running pods.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

WORKDIR /app
COPY --from=build /app/publish .

# The base image already sets ASPNETCORE_HTTP_PORTS=8080 and creates a user
# "app" with uid/gid 1654 -- but it does not switch to it, so without this
# line the container runs as root.
#
# Use the NUMERIC uid, not `USER app`. The kubelet cannot resolve a username
# to a uid, so with a name it cannot prove the user is non-root and a pod with
# securityContext.runAsNonRoot: true fails to start with
# "container has runAsNonRoot and image has non-numeric user".
USER $APP_UID

# Documentation only -- it publishes nothing by itself. The port that matters
# is containerPort in the Deployment.
EXPOSE 8080

# No HEALTHCHECK instruction: Kubernetes ignores it entirely and uses the
# probes defined in the pod spec instead.

LABEL org.opencontainers.image.title="health-api" \
      org.opencontainers.image.source="https://github.com/deychner/helm-chart-practice"

ENTRYPOINT ["dotnet", "HealthApi.dll"]
