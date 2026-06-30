# Legal Posture (not legal advice)

Froggacuda has decided to ship a free community fix and absorb any legal ramifications
later. This documents the *actual* cover so the README/release can lean on the strong
arguments, not the weak one.

## The weak argument (don't rely on it)
The **"Stop Killing Games"** European Citizens' Initiative (2024–2025, crossed 1M
signatures) pushes publishers to leave games playable after shutdown. It is **only a
request for the EU Commission to consider legislation — NOT enforceable law.** Use it
as goodwill/momentum, never as a legal basis.

## The strong cover (rely on these)
1. **EU interoperability right — Software Directive 2009/24/EC, Art. 6.** EU law
   explicitly permits reverse-engineering/decompiling to achieve **interoperability**
   with an independently created program. "Make the client talk to a replacement lobby"
   is textbook interoperability. Strongest single point.
2. **Reimplementing an interface, not copying code.** Clean-room reimplementation of a
   network protocol is broadly defensible (US: *Sega v. Accolade*; GameSpy-revival
   precedent). Ship none of Runic's code.
3. **Non-commercial + bring-your-own-copy.** Free, no IP resale, every user owns a
   legit copy — removes the lost-sales / trademark motives to sue.
4. **Redistribute nothing of theirs.** No game files, art, or binaries hosted — just a
   server that speaks the protocol.

## The one thing to watch
**Anti-circumvention** (US DMCA §1201; EU EUCD equivalents). Mitigation: we **replace
the endpoint** and accept our own logins rather than defeating their crypto — that's
interoperability, not circumvention. Reinforced by the fact the game *invites*
redirection via its own `LOBBYHOST` setting.

## Practical guardrails for release
- Brand it clearly: unofficial, fan-made, not affiliated with/endorsed by the rights
  holders; "Torchlight" trademarks belong to their owners.
- "Use with a legally obtained copy" (mirrors MHServerEmu's framing).
- No assets, no game binaries in the repo.
- Credit Crypto137 for the prior-art protocol work.
