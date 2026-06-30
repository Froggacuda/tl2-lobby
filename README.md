# TL2 Community Lobby

A **free, open-source, non-commercial** replacement for the defunct/flaky
`lobby.runicgames.com` matchmaking service — so people can keep playing
**Torchlight 2** (2012) multiplayer 15 years on.

You host a small lobby server, your friends point their game clients at it with a
single config line, and you play together through the normal in-game server browser —
no Hamachi, no mesh VPN, no LAN-mode workaround.

By **Froggacuda** (froggacuda@froggacuda.com). A labor of love. Not our IP, not for sale.

---

## Why this exists
- `lobby.runicgames.com` is unmaintained (Runic was sold several times) and still throws
  the perennial **"duplicate login" lockout** on perfectly valid credentials.
- TL2's old, strict-NAT P2P networking makes direct connections painful.
- The usual workaround (Hamachi / mesh VPN + LAN mode) works but is a hassle and pokes
  security holes in personal PCs.

TL2 has a built-in lobby redirect (`LOBBYHOST` in `local_settings.txt`) — no DRM hack
needed. This project is the replacement lobby you redirect *to*. It handles the game
browser and brokers the peer-to-peer connection between host and joiner.

## What works today
- ✅ **Standalone lobby** — no dependency on Runic's servers at all. The lobby does no
  authentication and stores no credentials; it just lists games and brokers connections.
- ✅ **In-game server browser** — hosted games show up in the normal TL2 browser.
- ✅ **Direct P2P play, proven on a real LAN and over the open internet** between friends
  on ordinary home connections.
- ✅ **Self-hosting** — you can run the lobby on the same PC/LAN you host games from
  (set `TL2_RELAY_IP`, see below).

### Known limitation
Connections use **direct NAT punch-through**. Players behind **strict / symmetric NAT**
(notably cellular / CGNAT) may fail to connect. A server-side **relay fallback** for
those cases is in progress but **not finished yet** — see [`docs/`](docs/). Most
home-broadband players are unaffected.

---

## Host a lobby

**Requirements:** Windows, a router you can port-forward, and (to build) the
[.NET 8 SDK](https://dotnet.microsoft.com/download).

1. **Build** the server (or use a release binary when available):
   ```
   cd reference/TL2BetaMiniLobby/src/TL2BetaMiniLobby
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   ```
2. **Run it.** A sample launcher is provided in [`run.bat`](run.bat) — rename the
   published exe to `tl2lobby.exe` next to it, or edit the path. It logs cleanly to
   `server.log` by default; add `-debug` for verbose diagnostics.
3. **Port-forward `4549` (both TCP and UDP)** to the lobby machine on your router.
4. **Share your address** with friends — your public IP, or a domain / dynamic-DNS name.
5. **Self-hosting:** if you host *games* on the same PC or LAN as the lobby, set
   `TL2_RELAY_IP=<your public IP>` (uncomment the line in `run.bat`) so remote friends can
   reach your games. The lobby otherwise only knows your private address. Remote-only
   hosting doesn't need it.

> **Heads-up:** the lobby is an unsigned binary, so the first time you run it Windows
> SmartScreen may say *"Windows protected your PC."* Click **More info → Run anyway**.
> Some antivirus may also flag a brand-new unsigned exe; the full source is in this repo
> if you'd rather build it yourself.

See [`docs/DEPLOY.md`](docs/DEPLOY.md) for running it as an always-on service.

## Join a lobby

1. In Torchlight 2's `local_settings.txt`, set:
   ```
   LOBBYHOST :<the host's public IP or domain>
   LOBBYPORT :4549
   ```
   (The file is under `Documents\My Games\runic games\torchlight 2\`. Edit it with the
   game **closed**, and keep its existing text encoding.)
2. Launch TL2, open the multiplayer server browser, and join your friend's game.

No account setup is required by the lobby — it doesn't authenticate you. If you have a
Runic/TL2 login you can still use it; nothing is sent to Runic.

---

## Docs
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — how TL2 multiplayer works + this design
- [`docs/DEPLOY.md`](docs/DEPLOY.md) — running the lobby as a service
- [`docs/PROTOCOL.md`](docs/PROTOCOL.md) / [`docs/RE-FINDINGS.md`](docs/RE-FINDINGS.md) — the lobby protocol, reverse-engineered
- [`docs/LEGAL.md`](docs/LEGAL.md) — legal posture
- [`docs/FEASIBILITY.md`](docs/FEASIBILITY.md) · [`docs/ALPHA_PLAN.md`](docs/ALPHA_PLAN.md) — background / project history

## Credits
This project stands on the shoulders of
**[TL2BetaMiniLobby](https://github.com/Crypto137/TL2BetaMiniLobby) by Crypto137**, the
original open-source Torchlight 2 lobby implementation. tl2-lobby is a modified derivative
of that work — huge thanks to Crypto137. The upstream code is MIT-licensed; see
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

Torchlight 2 is a trademark of its respective owners. This is an unofficial, fan-made,
non-commercial project with no affiliation to or endorsement by Runic Games or any
rights-holder.

## License
Licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)** — see
[`LICENSE`](LICENSE) and [`NOTICE`](NOTICE). AGPL is chosen deliberately: improvements,
**including those deployed as a network service**, must be shared back with the community
under the same terms. Upstream MIT portions retain their original license.
