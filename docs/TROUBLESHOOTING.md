# Troubleshooting & FAQ

Most problems fall into a few buckets. Find your symptom below.

**Jump to:** [SmartScreen warning](#windows-protected-your-pc-smartscreen) ·
[Antivirus](#antivirus-flagged-the-exe) ·
[Friends can't connect](#my-friends-cant-connect--check-your-firewall) ·
[Red firewall indicator](#tl2-shows-a-red-firewall-indicator-in-the-bottom-right-corner) ·
[Empty server browser](#the-server-browser-is-empty-or-i-dont-see-my-friends-game) ·
[Editing local_settings.txt](#where-is-local_settingstxt--how-do-i-edit-it) ·
[Is the lobby working?](#how-do-i-know-the-lobby-is-running) ·
[Still stuck?](#still-stuck-file-an-issue)

---

## "Windows protected your PC" (SmartScreen)

**Expected, and harmless.** `tl2lobby.exe` is an unsigned binary (code-signing certificates
cost money this free project doesn't have), so Windows SmartScreen warns about it the first
time you run it.

**Fix:** on the blue dialog click **More info → Run anyway**.

Don't trust a random exe on principle? Good instinct — the full source is in this repo, so
you can **build it yourself** instead (see [Host a lobby](../README.md#host-a-lobby)) and run
your own binary.

## Antivirus flagged the exe

Brand-new, unsigned, self-contained .NET single-file executables are a common **false-positive**
trigger for heuristic AV. The lobby has no network behavior beyond listening on port 4549 and
forwarding TL2 lobby traffic.

If you'd rather not take our word for it: **build from source** (the exe is just the published
output of the code in this repo), verify the download's checksum
([Verify your download](../README.md#verify-your-download) — the release's SHA-256 is published),
or upload the exe to [VirusTotal](https://www.virustotal.com/) to see the detections for yourself.

## My friends can't connect / "check your firewall"

This is the most common issue. Work down the list **in order** — the first few fix the large
majority of cases.

1. **Port-forward BOTH TCP and UDP on port `4549`** to the lobby PC on your router. This is the
   #1 cause. Many people forward only TCP (or only UDP) — you need **both**. UDP is what carries
   the actual peer-to-peer connection.

2. **Check each friend's `local_settings.txt`** (see [below](#where-is-local_settingstxt--how-do-i-edit-it)):
   ```
   LOBBYHOST :<your public IP or domain>
   LOBBYPORT :4549
   ```
   Note the **leading colon** in the value (`LOBBYHOST :1.2.3.4`). Edit the file with the **game
   closed**, and don't change its text encoding.

3. **Confirm the lobby is reachable from the outside.** From a phone on cellular (not your
   Wi-Fi) or [a port checker](https://www.yougetsignal.com/tools/open-ports/), check that your
   public IP responds on `4549`. If it doesn't, the port-forward or a firewall is the problem,
   not the lobby.

4. **Self-hosting?** If you run the lobby **on the same PC or LAN where you host games**, set
   your public IP so remote friends can reach your games:
   ```
   set TL2_RELAY_IP=<your public IP>
   ```
   (uncomment that line in `run.bat`). Without it, a co-located game only advertises a private
   address and remote joiners can't reach it. Remote-only hosting doesn't need this.

5. **Strict / symmetric NAT or CGNAT (known limitation).** If you (or your friend) are on
   **cellular**, **Starlink**, or an ISP that uses **carrier-grade NAT**, direct connections may
   be impossible no matter what you do — there's no public port to forward. v0.1 uses direct NAT
   punch-through only; a server-side **relay fallback** for these cases is planned for v0.2.
   Tell-tale sign: home-broadband friends connect fine, but the cellular/CGNAT person always
   fails. (Not sure if you're behind CGNAT? If your router's WAN IP starts with `100.64.`–`100.127.`,
   or doesn't match your public IP from [whatismyip](https://www.whatismyip.com/), you probably are.)

## TL2 shows a red firewall indicator in the bottom-right corner

**Normal, and not something this project causes or can fix.** That red firewall/connection
indicator is **Torchlight 2's own client-side check** of your local network — it's built into
the game and reflects your PC's firewall / router / NAT, not the lobby. You'll often see it
even when multiplayer works fine: on its own it does **not** mean you can't connect, and the
lobby neither triggers it nor can clear it (the lobby is a matchmaking broker — it never
touches your local firewall).

If your connections genuinely fail, the real cause is your NAT type, not this indicator — see
[strict / symmetric NAT or CGNAT](#my-friends-cant-connect--check-your-firewall) above.

## The server browser is empty, or I don't see my friend's game

- **Everyone must run the same mods** (or everyone vanilla). Modded and unmodded clients use
  **different** browse paths in TL2 — a modded host won't appear to a vanilla joiner and vice
  versa. Match your mod setups.
- **Confirm both of you point at the same lobby** (`LOBBYHOST` matches) on port `4549`.
- Make sure the host actually created/opened a game after launching.

## Where is `local_settings.txt` / how do I edit it?

- **Location:** `Documents\My Games\runic games\torchlight 2\local_settings.txt`
- **Edit with Torchlight 2 fully closed** — the game rewrites this file on exit, so edits made
  while it's running are lost.
- **Keep the file's existing text encoding** when you save (don't convert it).
- The two lines that matter for this project:
  ```
  LOBBYHOST :<the host's public IP or domain>
  LOBBYPORT :4549
  ```

## How do I know the lobby is running?

When it starts, `server.log` (next to the exe) should show:
```
LobbyServer is listening on 0.0.0.0:4549...
```
That means it's up and listening on both TCP and UDP `4549`.

When a friend successfully joins, the log records the brokered connection; the connection-result
line **ending in `01`** means success (`00` means the connection failed). Run with **`-debug`**
(`tl2lobby.exe -debug`) for a verbose play-by-play if you're diagnosing a problem.

## Still stuck? File an issue

Open a [connection problem or bug report](https://github.com/Froggacuda/tl2-lobby/issues/new/choose)
and include:

- **Host or joiner?** (which side are you on)
- Your **OS** and whether you **built from source** or used the release binary
- Did you **port-forward TCP + UDP 4549**?
- Are you or your friend on **cellular / CGNAT / Starlink**?
- The **`server.log`** from the lobby (run with `-debug` for the most useful output)
- What you **see** vs. what you **expected**

The more of this you include up front, the faster anyone can help.
