# Two builds in one image: the SvelteKit client is compiled to static files and served by the
# same ASP.NET host that exposes the API and the SignalR hub.

# --- 1. Front end -------------------------------------------------------------------------
FROM node:22-bookworm-slim AS client
WORKDIR /client

COPY app/package.json app/package-lock.json ./
RUN npm ci

COPY app/ ./
RUN npm run build

# --- 2. Back end --------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src

COPY Directory.Build.props MssqlRealtime.slnx ./
COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/MssqlRealtime.Api/MssqlRealtime.Api.csproj -c Release -o /app/publish

# --- 3. Runtime ---------------------------------------------------------------------------
# NOTE: use the full Debian-based runtime image, not -alpine or -chiseled.
# Measured 2026-08-04: Microsoft.Data.SqlClient fails with "Globalization Invariant Mode is
# not supported", so ICU must be present — the slim variants do not ship it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is for HEALTHCHECK only. Measured 2026-08-05: the runtime image ships neither curl nor
# wget, so the healthcheck below silently never passed and the container sat in "starting"
# forever — which also breaks `depends_on: condition: service_healthy`.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=server /app/publish ./
COPY --from=client /client/build ./wwwroot/

# SQLite database, data-protection key ring and logs all live here. Mount it as a volume:
# losing the key ring makes every stored SQL password unreadable.
ENV Storage__DataDirectory=/data
VOLUME /data

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/api/health || exit 1"]

ENTRYPOINT ["dotnet", "MssqlRealtime.Api.dll"]
