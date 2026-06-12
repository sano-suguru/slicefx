# Build from repository root:
#   docker build -f samples/SliceFx.AotSample/Dockerfile -t slicefx-aot .
#
# Build context must be the repository root because the sample uses ProjectReferences
# into src/. Force linux/amd64 on Apple Silicon to produce an x64 binary:
#   docker build --platform linux/amd64 -f samples/SliceFx.AotSample/Dockerfile -t slicefx-aot .
#
# Run:
#   docker run --rm -p 8080:8080 slicefx-aot
#   curl http://localhost:8080/health

# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# NativeAOT on Linux requires clang and zlib — the SDK image does not include them.
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .

RUN dotnet publish samples/SliceFx.AotSample/SliceFx.AotSample.csproj \
    -c Release \
    -o /app

# ── Runtime stage ─────────────────────────────────────────────────────────────
# runtime-deps:10.0-noble-chiseled is a minimal Ubuntu Noble (24.04) distroless image.
# It runs as non-root by default and is ~13 MB, giving a total image size of ~25-35 MB.
# Fallback if unavailable: mcr.microsoft.com/dotnet/runtime-deps:10.0-azurelinux3.0-distroless
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled

WORKDIR /app
COPY --from=build /app/SliceFx.AotSample .

# Override the localhost URL that appsettings.json sets — inside the container
# we want to listen on all interfaces so port mapping works.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["./SliceFx.AotSample"]
