# Craftwar.NetServer

The relay server for M11 online play: matchmaking, accounts, chat, ladder,
and packet relay. It never simulates a match — see the class docs on
`RoomManager`/`ClientConnection` and the M11 plan
(`C:\Users\mattc\.claude\plans\optimized-foraging-hearth.md`) for why.

Plain .NET, not a Unity project — it compiles `Assets/Scripts/Sim` and
`Assets/Scripts/Net` by source (both are `noEngineReferences` asmdefs, see
`CLAUDE.md`) the same way the standalone Sim/Net test harness does.

## Running it locally (the default — no setup needed)

```
cd Server/Craftwar.NetServer
dotnet run
```

First run generates and caches a self-signed cert (`craftwar-dev-cert.pfx`)
next to the working directory, and creates `craftwar.db` (SQLite) there too.
Both are gitignored — delete them to reset to a clean account/ratings state.
Listens on `0.0.0.0:27015` by default.

Point a client (or `RelayPeerSocket.Host`/`.Join` in a test) at
`127.0.0.1:27015`.

## Tests

```
cd Server/Craftwar.NetServer.Tests
dotnet test
```

Runs against real in-process server instances (`RelayServerHost`) over real
TCP+TLS on loopback — not mocks. See `RelayIntegrationTests.cs` for the
headline proofs (lockstep over the relay stays bit-identical, reconnect
survives a real dropped connection, chat/rooms/accounts/ratings all round-trip
through a real socket).

## Configuration

Every value in `ServerConfig.cs` can be set two ways, args winning on
conflict:

| Setting | Env var | Arg | Default |
|---|---|---|---|
| Bind host | `CRAFTWAR_HOST` | `--host` | `0.0.0.0` |
| Port | `CRAFTWAR_PORT` | `--port` | `27015` |
| SQLite db path | `CRAFTWAR_DB_PATH` | `--db` | `craftwar.db` |
| TLS cert (PFX) | `CRAFTWAR_CERT_PATH` | `--cert` | *(auto-generate/cache)* |
| TLS cert password | `CRAFTWAR_CERT_PASSWORD` | `--cert-password` | `craftwar` |

Env vars exist because that's what both deployment paths below actually set
(a systemd `EnvironmentFile`, a Docker `--env-file`) — neither naturally
populates argv.

## Deploying for real (once this stops being "just my machine")

The only things that change between local and real are in the table above —
no code changes. Two packaging options, pick one:

### Docker

Build from the **repo root**, not `Server/` — the Dockerfile needs
`Assets/Scripts/Sim`/`Net` in its build context (see the comment at the top
of `Server/Dockerfile`):

```
docker build -f Server/Dockerfile -t craftwar-netserver .
docker run -d --name craftwar-netserver \
  -p 27015:27015 \
  -v craftwar-data:/data \
  -e CRAFTWAR_CERT_PATH=/data/real-cert.pfx \
  -e CRAFTWAR_CERT_PASSWORD=<real password> \
  craftwar-netserver
```

The `/data` volume is where the SQLite db lives (`CRAFTWAR_DB_PATH` is
already baked in to point there) — mount a real PFX into it too if you're
using a real cert, rather than the auto-generated self-signed one.

### systemd (bare metal / a VM)

1. `dotnet publish -c Release -o /opt/craftwar-netserver` from
   `Server/Craftwar.NetServer/`.
2. `useradd --system --home /var/lib/craftwar-netserver craftwar`, then
   `mkdir -p /var/lib/craftwar-netserver && chown craftwar:craftwar
   /var/lib/craftwar-netserver`.
3. Copy `deploy/craftwar-netserver.env.example` to
   `/etc/craftwar/craftwar-netserver.env`, fill it in, `chmod 600` (it holds
   the cert password).
4. Copy `deploy/craftwar-netserver.service` to
   `/etc/systemd/system/`, then `systemctl daemon-reload && systemctl enable
   --now craftwar-netserver`.
5. `journalctl -u craftwar-netserver -f` for logs.

### TLS certificate

`CertificateProvider.Load` (see its own doc comment) transparently covers
both cases: leave `CRAFTWAR_CERT_PATH` unset and it self-generates/caches a
5-year self-signed cert next to the working directory — fine for `dotnet run`
locally or the Docker image (`WORKDIR /data` there is always writable) —
while you're the only one connecting, since `RelayPeerSocket`'s client-side
validation currently accepts any server cert (`ValidateServerCertificate` —
see the note in `RelayPeerSocket.cs`; cert pinning/CA validation is
unaddressed, deliberately deferred past this milestone). **Under the shipped
systemd unit, always set `CRAFTWAR_CERT_PATH` explicitly** (the
`.env.example` already does, pointing under `/var/lib/craftwar-netserver`):
leaving it blank falls back to a path relative to `WorkingDirectory`
(`/opt/craftwar-netserver`), which `ProtectSystem=strict` makes read-only, so
the service fails to start on first run instead of caching a cert. Once real
players connect, point `CRAFTWAR_CERT_PATH` at a real PFX (e.g. a Let's
Encrypt cert exported via `openssl pkcs12 -export -in fullchain.pem -inkey
privkey.pem -out cert.pfx`) — pure config, no rebuild.

### Shutdown

`Program.cs` handles `SIGTERM`/`SIGINT` (`docker stop`, `systemctl stop`,
Ctrl+C) by disposing `RelayServerHost` — stop accepting new connections, stop
the listener — instead of relying on the container/service manager's kill
timeout to end the process mid-connection. **Not verified against a real
SIGTERM on this codebase's dev machine** (Windows; POSIX signal delivery
isn't something `kill`/`taskkill` there actually exercises) — `dotnet`'s
`PosixSignalRegistration` is documented to work on Linux, which is what
Docker/systemd both are, but treat this specific path as unverified until
it's checked on an actual Linux deployment.

### Backing up the database

The server holds no lock open on `craftwar.db` between requests longer than
a single query, so `sqlite3 /path/to/craftwar.db ".backup /path/to/backup.db"`
(the online-safe SQLite backup command, not a raw file copy) is safe to run
against a live server.

## Known gaps (carried over from the M11 plan, not regressions)

- `ReportMatchResult` is fire-and-forget from whichever peer is the elected
  host, trusted with no server-side verification — a malicious host could
  misreport a result. Deliberately deferred (see the M11 plan's phase 5
  notes); revisit before this is a public ladder anyone competitive cares
  about.
- No ladder/profile view in the client yet — server-side ready
  (`RatingRepository`/`RatingService`), no menu screen built.
- No cert pinning on the client — see the TLS note above.
