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
- ✅ **Full multiplayer (3+ players)** — the lobby brokers the peer-to-peer *mesh* (in TL2
  co-op every player connects to every other, not just the host), so games beyond host + 1
  work. Proven with a live 3-player game; no player cap in the lobby (standard 4, or 8 with mods).
- ✅ **Automatic port-forwarding (UPnP / NAT-PMP)** — the lobby opens port 4549 on your
  router on startup if it supports UPnP, so hosting often needs zero manual router setup.
- ✅ **Self-hosting** — you can run the lobby on the same PC/LAN you host games from
  (public IP auto-detected via UPnP, or set `TL2_RELAY_IP`, see below).

### Known limitation
Every peer-to-peer link uses **direct NAT punch-through** — including the joiner-to-joiner
links in a 3+ player mesh. A player behind **strict / symmetric NAT** (notably cellular /
CGNAT) may fail to connect to some or all peers. A server-side **relay fallback** for those
cases is in progress but **not finished yet** — see [`docs/`](docs/). Most home-broadband
players are unaffected.

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
3. **Port-forwarding — automatic where possible.** On startup the lobby opens TCP + UDP
   `4549` on your router via **UPnP / NAT-PMP** (look for `UPnP: mapped 4549…` in `server.log`).
   If your router has UPnP disabled, forward **`4549` (both TCP and UDP)** manually. Disable
   the auto-attempt entirely with `TL2_UPNP_OFF=1`.
4. **Share your address** with friends — your public IP, or a domain / dynamic-DNS name.
5. **Self-hosting:** if you host *games* on the same PC or LAN as the lobby, the lobby needs
   to advertise your **public** IP to remote friends (it otherwise only knows your private
   address). If UPnP is available this is **detected automatically**; otherwise set
   `TL2_RELAY_IP=<your public IP>` (uncomment the line in `run.bat`). Remote-only hosting
   doesn't need it.

> **Heads-up:** the lobby is an unsigned binary, so the first time you run it Windows
> SmartScreen may say *"Windows protected your PC."* Click **More info → Run anyway**.
> Some antivirus may also flag a brand-new unsigned exe; the full source is in this repo
> if you'd rather build it yourself. Anything not working? See
> **[Troubleshooting & FAQ](docs/TROUBLESHOOTING.md)**.

### Verify your download

The release binary is unsigned, so verify its integrity before running it. The SHA-256 of
`tl2-lobby-v0.1.0-win-x64.zip` (v0.1.0 release) is:

```
92bd793e934a8f9ef5578b6381de7e3de3628dbcc081138f37ee6f15a213a11f
```

Check it after downloading — the output must match **exactly**:

- **Windows (PowerShell):** `Get-FileHash tl2-lobby-v0.1.0-win-x64.zip -Algorithm SHA256`
- **Windows (cmd):** `certutil -hashfile tl2-lobby-v0.1.0-win-x64.zip SHA256`
- **macOS / Linux:** `shasum -a 256 tl2-lobby-v0.1.0-win-x64.zip`

If it doesn't match, don't run it.

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

> **Can't connect or stuck?** → **[Troubleshooting & FAQ](docs/TROUBLESHOOTING.md)**. The
> [can't-connect checklist](docs/TROUBLESHOOTING.md#my-friends-cant-connect--check-your-firewall)
> fixes most problems (usually a missing **UDP** port-forward, or a cellular/CGNAT connection).

---

## Docs
- [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md) — **stuck? start here** (SmartScreen, "can't connect" / NAT, config)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — how TL2 multiplayer works + this design
- [`docs/PROTOCOL.md`](docs/PROTOCOL.md) — the lobby protocol, reverse-engineered
- [`docs/LEGAL.md`](docs/LEGAL.md) — legal posture

## Credits
This project stands on the shoulders of
**[TL2BetaMiniLobby](https://github.com/Crypto137/TL2BetaMiniLobby) by Crypto137**, the
original open-source Torchlight 2 lobby implementation. tl2-lobby is a modified derivative
of that work — huge thanks to Crypto137. The upstream code is MIT-licensed; see
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

**Special thanks** to the playtesters who helped prove the lobby, NAT brokering, and
multiplayer mesh across real home connections: **Yeggis, Sparklespanks, Goretsky, Ktinga,
and Boomclad.**

Torchlight 2 is a trademark of its respective owners. This is an unofficial, fan-made,
non-commercial project with no affiliation to or endorsement by Runic Games or any
rights-holder.

## License
Licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)** — see
[`LICENSE`](LICENSE) and [`NOTICE`](NOTICE). AGPL is chosen deliberately: improvements,
**including those deployed as a network service**, must be shared back with the community
under the same terms. Upstream MIT portions retain their original license.
