# TL2 Lobby Protocol (reverse-engineered)

Source: reading the **Crypto137/TL2BetaMiniLobby** C# implementation (beta v0.20.8.1,
May 2012). Confirmed by building + running that server locally on 2026-06-22.
**Caveat:** this is the *beta* protocol; the full Steam/Epic/GOG version may differ in
opcode ordering and message bodies — must be re-verified by capture (see "Unknowns").

## Transport
- **TCP**, server listens on **port 4549**.
- **Packet framing:** `[1 byte opcode][2 byte payload length, BIG-ENDIAN][payload]`.
- **Inside the payload:** integers are **little-endian** (default .NET `BinaryReader`),
  EXCEPT the length prefix above which is big-endian. Watch this mismatch.
- **Strings = "FixedString8":** `[1 byte length][UTF-8 bytes]` (max 255).
- Opcode is a single byte indexing the enum below (0-based).

## Opcode table (48 opcodes, 0-indexed)
```
0  NetConnectMsg              17 LobbyUpdateGameServerMsg
1  NetConnectOkMsg            18 LobbyRemoveGameServerMsg
2  NetDisconnectMsg           19 LobbyGetServerDetailsMsg
3  SrvConnectCallbackMsg      20 LobbyServerDetailsMsg
4  CltConnectCallbackMsg      21 LobbyRequestConnectMsg     <- brokering
5  DisconnectCallbackMsg      22 LobbyAttemptConnectMsg     <- brokering
6  LobbyMsg6                  23 LobbyAttemptConnectFailedMsg
7  LobbyMsg7                  24 LobbyJoinedGameMsg
8  LobbyStartLoginMsg         25 LobbyLeftGameMsg
9  LobbyLoginChallengeMsg     26 LobbyAddFriendMsg
10 LobbyLoginResponseMsg      27 LobbyRemoveFriendMsg
11 LobbyLoginResultMsg        28 LobbyAddEnemyMsg
12 LobbyGetGameServersMsg     29 LobbyRemoveEnemyMsg
13 LobbyGameServersListMsg    30 LobbyLookupUserMsg
14 LobbyGameServersListEndMsg 31 LobbyLookupUserResponseMsg
15 LobbyCreateGameServerMsg   32 LobbyTestNatRequestMsg     <- NAT
16 LobbyCreateGameResponseMsg 33 LobbyFirewallTestSuccessMsg<- NAT
   ...                        34 LobbyBeginFriendsMsg
                              35 LobbySendFriendMsg
                              36 LobbyAdminMessageMsg
                              37 LobbySetCharacterDataMsg
                              38 LobbyVerifyNewKeyMsg
                              39 LobbyVerifyNewKeyResultMsg
                              40 LobbyAddFriendByNameMsg
                              41 LobbyMsg41
                              42 LobbyKeepaliveMsg
                              43 LobbyRequestGameMsg
                              44 LobbyReportConnectionMsg
```

## ⭐ KEY FINDING: auth is trivially bypassable
The login handshake is:
1. client → **NetConnectMsg** (Field0, ClientKey=0, Field2)
2. server → **NetConnectOkMsg** { Field1=1, Field2=1, ClientKey=0xFFFFFFFF }
3. client → **LobbyStartLoginMsg** { Username (FixedString8), ... }
4. server → **LobbyLoginChallengeMsg** { Salt (FixedString8), Field1 (FixedString8) }
5. client → **LobbyLoginResponseMsg** { 32 raw bytes — a salted password hash }
6. server → **LobbyLoginResultMsg**

**The server never validates the 32-byte LoginResponse.** It logs the user in
unconditionally. So our replacement just *accepts everyone* — there is **no crypto to
crack, no anti-circumvention concern** (we replace the endpoint, we don't defeat auth).
This also kills the "duplicate login" lockout for free: we manage sessions sanely.

## Known message bodies
- **NetConnectMsg / NetConnectOkMsg:** ulong Field0, uint ClientKey, uint Field2.
- **LobbyCreateGameServerMsg** (client→server, host opens a game):
  ulong GameServerId, ushort Field1, ushort LevelRangeMin, ushort LevelRangeMax,
  ushort Flags, byte MaxPlayers, FixedString8 Name, FixedString8 Description,
  [ushort len][Blob] (Blob ≤1024, possibly compressed character/game data).
- **LobbyCreateGameResponseMsg:** { Response = Success } (server ack).
- **LobbyKeepaliveMsg:** echoed back by server.

## Unknowns — REQUIRE CAPTURE from the full client (the real work)
Bodies NOT implemented in the beta reference, needed for actual multiplayer:
- **Game browser:** LobbyGetGameServersMsg → LobbyGameServersListMsg(+End) — so joiners
  see open games.
- **Details:** LobbyGetServerDetailsMsg → LobbyServerDetailsMsg.
- **Brokering (the crux):** LobbyRequestConnectMsg / LobbyAttemptConnectMsg /
  LobbyAttemptConnectFailedMsg — how the lobby hands peers each other's IP:port for P2P.
- **NAT:** LobbyTestNatRequestMsg / LobbyFirewallTestSuccessMsg — the "firewall detected"
  probe path. Where our strict-NAT fix / relay hooks in.
- **Join state:** LobbyJoinedGameMsg / LobbyLeftGameMsg.

## Strict-NAT / UDPORT:0 strategy (the bonus)
- TL2 gameplay is P2P UDP. `UDPORT :0` = ephemeral port → unpredictable mapping →
  strict NAT fails. Mitigation 1: have clients **pin UDPORT** to a fixed value >1024 so
  it's forwardable/stable.
- Mitigation 2 (the real fix): our broker controls LobbyTestNatRequest/AttemptConnect, so
  we can implement a **UDP relay** — when direct punch fails, gameplay UDP routes through
  our server (TURN-style). This is what removes the Hamachi requirement entirely.
- Both need the brokering/NAT message bodies above, hence capture is the gate.
```
