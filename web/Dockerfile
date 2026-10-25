# The web app's production image: the SvelteKit static build served by Caddy on :8080 (SPA fallback, and the headers the
# build's CSP <meta> tag cannot carry), and the viewer's hosted build (the admin Replays tool) under /viewer/. Build from the
# repo root:
#   docker build -f web/Dockerfile --build-arg TABLETOP_BLOB_ORIGIN=https://files.example.org -t tabletop-web .
# TABLETOP_BLOB_ORIGIN is TABLETOP_S3_PUBLIC_ENDPOINT's origin: the CSP's connect-src and img-src name it (svelte.config.js).
FROM node:24-alpine AS build
WORKDIR /src/web
COPY web/package.json web/package-lock.json web/.npmrc ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
ARG TABLETOP_BLOB_ORIGIN
RUN test -n "$TABLETOP_BLOB_ORIGIN" || { echo "the TABLETOP_BLOB_ORIGIN build arg is required" >&2; exit 1; }; \
    TABLETOP_BLOB_ORIGIN="$TABLETOP_BLOB_ORIGIN" npm run build

# Tabletop.Geometry as WebAssembly (libs/Tabletop.Geometry.Wasm): the viewer's geometry runtime, fetched by the page on first use.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS geometry
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY libs/Tabletop.Geometry/ libs/Tabletop.Geometry/
COPY libs/Tabletop.Geometry.Wasm/ libs/Tabletop.Geometry.Wasm/
COPY scripts/geometry-wasm scripts/
RUN ./scripts/geometry-wasm --out /out

# The viewer (viewer/), Vite mode hosted: base /viewer/, the website's session, the socket on the page's own host.
FROM node:24-alpine AS viewer
WORKDIR /src/viewer
COPY viewer/package.json viewer/package-lock.json viewer/.npmrc ./
RUN npm ci --no-audit --no-fund
COPY viewer/ ./
COPY client/content/builtin/catalog.json /src/client/content/builtin/catalog.json
COPY --from=geometry /out/_framework public/geometry/_framework
RUN npm run build:hosted

FROM caddy:2-alpine
# The image grants the binary cap_net_bind_service as a file capability, which no-new-privileges refuses to exec; :8080
# needs none.
RUN setcap -r /usr/bin/caddy
COPY web/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /src/web/build /srv
COPY --from=viewer /src/viewer/dist /srv/viewer
USER 65534:65534
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=3 CMD ["wget", "-q", "-O", "/dev/null", "http://127.0.0.1:8080/"]
