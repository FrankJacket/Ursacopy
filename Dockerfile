# Ursacopy: incremental copy of NAS folders onto an external USB disk (web interface + scheduler) in a Linux container.
# Build from the repository root:  docker build -t ursacopy .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0-dev
WORKDIR /src
# Restore first, from the project files only, so this layer stays cached while the code changes.
COPY Directory.Build.props .
COPY src/Ursacopy.Core/Ursacopy.Core.csproj src/Ursacopy.Core/
COPY src/Ursacopy.Server/Ursacopy.Server.csproj src/Ursacopy.Server/
RUN dotnet restore src/Ursacopy.Server/Ursacopy.Server.csproj
COPY src/ src/
# No --no-restore: see the note in the Ursavault Dockerfile (blazor.web.js may otherwise be missing from the publish output).
RUN dotnet publish src/Ursacopy.Server/Ursacopy.Server.csproj -c Release -o /app -p:UseAppHost=false -p:Version=${VERSION#v}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# tzdata: daily schedules use the container's time zone (TZ).
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=5090 \
    DataDir=/data \
    TZ=America/Toronto
EXPOSE 5090
VOLUME ["/data"]
# Runs as root by default: reading every file of a NAS share and writing to an ext4 / exFAT USB disk needs it.
# To run unprivileged, set "user:" in compose.yaml to an account that can read the sources and write the disk and /data.
ENTRYPOINT ["dotnet", "Ursacopy.Server.dll"]
