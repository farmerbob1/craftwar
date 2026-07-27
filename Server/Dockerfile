# Build from the REPO ROOT, not this directory:
#   docker build -f Server/Dockerfile -t craftwar-netserver .
#
# The repo root is required as the build context because
# Craftwar.NetServer.csproj compiles Assets/Scripts/Sim and
# Assets/Scripts/Net by source (both are noEngineReferences Unity asmdefs —
# see CLAUDE.md — so this is the same "compile the source directly" trick
# the standalone Sim/Net test harness uses), not via a project reference a
# container build could resolve on its own from within Server/.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Assets/Scripts/Sim/ Assets/Scripts/Sim/
COPY Assets/Scripts/Net/ Assets/Scripts/Net/
COPY Server/Craftwar.NetServer/ Server/Craftwar.NetServer/
WORKDIR /src/Server/Craftwar.NetServer
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The server writes its SQLite db, and (unless CRAFTWAR_CERT_PATH points
# elsewhere) its self-signed cert, next to its working directory — put both
# under a mountable volume so `docker run -v craftwar-data:/data ...`
# actually persists an account database and match history across restarts
# instead of losing them with the container.
ENV CRAFTWAR_DB_PATH=/data/craftwar.db
VOLUME /data
WORKDIR /data

EXPOSE 27015
ENTRYPOINT ["dotnet", "/app/Craftwar.NetServer.dll"]
