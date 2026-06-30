# Architecture

## How TL2 multiplayer actually works
TL2 "online" is **peer-to-peer gameplay** brokered by a lightweight lobby. Runic ran
**no game servers** — only the lobby. The lobby does three jobs:

1. **Auth** — login (the part throwing "duplicate login" lockouts).
2. **Game browser** — lists open games / online players.
3. **Connection brokering** — introduces peers (hands out IP/port) so they punch
   through NAT and connect directly. One player **hosts**; others connect to the host.

Gameplay traffic never touches the lobby — it's host↔peers P2P. This is why the
problem splits cleanly: the lobby is small; the pain is the P2P/NAT step.

## Why the lockout happens
The lobby tracks sessions by **display name** and never cleanly expires ghost sessions
after a crash/disconnect — so re-login looks like a "duplicate." A sanely written
replacement makes this bug disappear by design.

## Our design
```
[TL2 client] --LOBBYHOST--> [Our Lobby Server]
   |  login (accept all)        |
   |  game browser list  <------|
   |  join request       ------>|  brokers peer addresses
   v                            v
[Host peer] <===== P2P gameplay =====> [Joining peer]
                  (relay fallback through server if strict-NAT punch fails)
```

- **Lobby server:** accepts any login (we don't validate against Runic's dead DB),
  serves the game browser, brokers joins. Likely a custom binary protocol — fork/learn
  from TL2BetaMiniLobby (C#/.NET 8); MHServerEmu is a good architectural template.
- **Relay (only if needed):** when P2P punchthrough fails, route gameplay through the
  server (TURN-style). This is what kills BOTH the lockout AND the Hamachi ritual.
  Relay = real bandwidth = the only thing ever worth hosting-cost discussion (but this
  project stays free/non-commercial).

## Redirect mechanism
No DNS/hosts hack required — TL2 exposes `LOBBYHOST` in `local_settings.txt`. Point it
at our server's IP/domain. The game is *designed* to allow this, which also strengthens
the legal posture (see LEGAL.md).
