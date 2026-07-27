Plan and implement M12 for Craftwar (a Warcraft 2 remake in Unity 6.5,
`C:\Users\mattc\UnityProjects\Craftwar`): a Battle.net-style social layer —
chat channels, whispers, friends with online presence, and clans/guilds with
tags and rosters — built on top of the relay server M11 just shipped.

Read `CLAUDE.md` in the repo root first (the three hard rules — Sim purity,
view-is-a-projection, everything through lockstep — plus licensing
guardrails). Then read `PROGRESS.md`'s M11 section for what already exists:
`Server/Craftwar.NetServer` (plain .NET, not a Unity project — accounts,
sessions, rooms, chat, Glicko-2 ratings, all over TCP+TLS with a 4-byte
length-prefixed frame protocol defined in `Assets/Scripts/Net/RelayProtocol.cs`
and handled server-side in `Server/Craftwar.NetServer/Transport/
ClientConnection.cs`), plus `Server/README.md` for how to run/test it.
`Server/Craftwar.NetServer.Tests` is the existing test project — note its
established style: real sockets against a real in-process `RelayServerHost`
instance, not mocks (see `RelayIntegrationTests.cs`).

**This is not a green field** — it extends the existing relay server and
account system, not a new service. Relevant existing pieces to build on
top of rather than duplicate:
- `AccountService`/`AccountRepository`/`Db/Database.cs` — accounts and
  sessions already exist.
- `RelayProtocol.cs`'s `ControlProtocol` — the existing wire format
  (`ChatMessage`/`ChatBroadcast`) is **room-scoped only** (see
  `ClientConnection.BroadcastChatAsync`) — it only reaches whoever is
  currently in the same room as you. A global channel/whisper/friend system
  needs a directory of logged-in connections independent of room membership
  (something in the spirit of `ConnectionRegistry`, keyed by account rather
  than by connection) — this almost certainly does NOT reuse the room chat
  path as-is.
- `RelayPeerSocket.cs` (client-side `IPacketPeer` over TCP+TLS,
  `Assets/Scripts/Net/`) already has a control-plane channel alongside the
  game-traffic relay path (see how it separates `SendChat`/`TryReceiveChat`
  from `Send`/`TryReceive`) — new social messages likely ride the same
  control-plane connection, not the game-relay path.
- `MainMenuController.Online.cs` is where the client-side menu UI for this
  would go (same partial-class pattern as the existing LAN/Online split).

**Explicitly out of scope / do not touch**: `Craftwar.Sim` (this feature is
entirely social/meta-game — no gameplay determinism concerns, no lockstep
interaction), and the game-traffic relay path itself (`TurnRelay`,
`HostTurnExchange`/`ClientTurnExchange`, `TurnLockstepDriver` — all proven,
unrelated to this work).

**Process** (this repo's established, successful pattern from M11 — follow
it, don't skip steps): go into plan mode before writing any code. Ask the
user clarifying questions rather than guessing scope — in particular:
- Which of the features below are in v1 vs later.
- Whether channels are ephemeral (exist only while someone's in them, like
  original Battle.net) or persistent/named.
- Clan data model: ranks (leader/officer/member)? Invite-only or
  request-to-join? Does a clan tag actually rename the player in rooms/chat,
  or just show alongside?
- Whether presence needs to be pushed proactively to friends (a "friend
  came online" notification) or just polled on demand.
Once scope is settled, **run an adversarial review of the design against
the actual codebase** before committing to it (find the ways "just extend
the existing wire format" is and isn't true, same as M11's plan did — see
its "Mechanism notes" section in `PROGRESS.md` for the shape of that
exercise) — this project has specifically asked for that step before and
found real design flaws each time.

**Feature checklist to scope with the user** (real WC2-era Battle.net
conventions — use as a starting menu, not a mandate):
- Chat channels: join/leave/create by name, a default home channel, member
  list, channel-scoped chat (no persistent history needed).
- Whispers: direct private messages between two logged-in accounts,
  independent of what channel/room either is in.
- Friends list: add/remove by username, online/offline/in-game status,
  optionally a "friend came online" push notification.
- Clans/guilds: a named clan with a short tag shown next to the username,
  a roster with ranks, invite/kick/leave/disband, and a clan-only channel.
- Ignore/block list: suppress whispers/chat from a specific account.
- Minimal moderation: channel-scoped kick, not server-wide bans (server-wide
  moderation is a further-out concern, don't over-build it here).

**Conventions to follow** (see `CLAUDE.md` and recent `PROGRESS.md` entries):
commit straight to main, no feature branches. Only commit when the user
explicitly asks. Update `PROGRESS.md` per phase, same level of detail as
the M11 entries (what was built, what was verified and how, what's a known
gap). New server-side logic gets real tests in `Craftwar.NetServer.Tests`
against actual sockets, not mocks — that's this project's bar, not a
suggestion. Any new Unity-side script under `Assets/` needs a hand-written
`.meta` file if the editor isn't reachable to generate one (`fileFormatVersion:
2`, a fresh 32-hex-char `guid`).
