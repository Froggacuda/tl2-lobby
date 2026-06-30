// tl2-lobby — community Torchlight 2 replacement lobby server.
// Copyright (C) 2026 Froggacuda. Licensed under AGPL-3.0 (see LICENSE / NOTICE).
// Derived from TL2BetaMiniLobby (c) 2023 Crypto137, MIT-licensed
// (https://github.com/Crypto137/TL2BetaMiniLobby) — see THIRD-PARTY-NOTICES.txt.

using System.Net.Sockets;
using System.Reflection;
using TL2BetaMiniLobby.Messages;
using TL2BetaMiniLobby.Messages.Definitions;

namespace TL2BetaMiniLobby
{
    /// <summary>
    /// Represents a client connected to a lobby server.
    /// </summary>
    public class LobbyClient
    {
        private readonly byte[] _receiveBuffer = new byte[4096];
        private readonly LobbyServer _server;

        // Bytes received but not yet consumed as complete frames (TCP is a stream: a single
        // logical message can span multiple reads, and one read can hold several messages).
        private byte[] _pending = Array.Empty<byte>();

        public TcpClient TcpClient { get; }
        public string Username { get; set; } = string.Empty;

        /// <summary>The lobby server this client is connected to (game registry, broadcast, etc.).</summary>
        public LobbyServer Server => _server;

        /// <summary>The client's mod hash (from SetModHash / op49); used to filter the game list.</summary>
        public uint ModHash { get; set; }

        /// <summary>Session addrToken assigned in op1 (= client's IP as uint32 LE). Used in op32/op33
        /// NAT handshake and op22 P2P brokering. Peers identify each other by this token.</summary>
        public uint AddrToken { get; set; }

        /// <summary>6-byte session key from op8 Field2+Field3 (bytes 10-15 of the login message).
        /// The real lobby puts the OTHER peer's LoginKey into op22's 12B prefix so the client can
        /// verify the peer's identity and extract connection info. Confirmed from proxy capture 2026-06-24.
        /// </summary>
        public byte[] LoginKey { get; set; } = new byte[6];

        /// <summary>This client's dynamic P2P UDP port, extracted from its op27 endpoint registration.
        /// BOTH host and joiner send op27 (confirmed in real-lobby capture 2026-06-24); the P2P port is
        /// per-session and is NOT the configured UDPORT/Field1. 0 until the client has sent op27.</summary>
        public ushort P2PUdpPort { get; set; }

        /// <summary>Game this client is currently trying to join (set on op21, cleared after op22).</summary>
        public GameServer PendingJoinGame { get; set; }

        /// <summary>The raw addrToken bytes the joiner sent in op21 (= host's IP LE on our LAN).</summary>
        public byte[] PendingJoinAddrToken { get; set; }

        public LobbyClient(LobbyServer server, TcpClient tcpClient)
        {
            _server = server;
            TcpClient = tcpClient;
        }

        /// <summary>
        /// Receives and handles data from the connected client asynchronously.
        /// </summary>
        /// <returns></returns>
        public async Task ReceiveDataAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    if (TcpClient.Client == null)
                        break;

                    // Receive data asynchronously
                    int received = await TcpClient.Client.ReceiveAsync(_receiveBuffer, SocketFlags.None).WaitAsync(cancellationToken);
                    if (received <= 0) break;

                    // Append to whatever partial frame was left over from previous reads.
                    byte[] data = new byte[_pending.Length + received];
                    Buffer.BlockCopy(_pending, 0, data, 0, _pending.Length);
                    Buffer.BlockCopy(_receiveBuffer, 0, data, _pending.Length, received);

                    // The client packs multiple messages into one TCP segment, and a single
                    // message can be split across segments. Consume every complete framed
                    // message: [1B opcode][2B BE len][payload]; keep the remainder for next read.
                    int pos = 0;
                    while (pos + 3 <= data.Length)
                    {
                        byte opcode = data[pos];
                        int payloadLen = (data[pos + 1] << 8) | data[pos + 2];
                        int total = 3 + payloadLen;
                        if (pos + total > data.Length) break; // incomplete; wait for more bytes

                        byte[] msgBuf = new byte[total];
                        Buffer.BlockCopy(data, pos, msgBuf, 0, total);
                        pos += total;

                        // A single malformed/truncated message must not tear down the whole
                        // connection — log it and move on to the next framed message.
                        try
                        {
                            Packet packet = new(msgBuf);
                            if (MessageHandler.HandleMessage(this, packet.Message) == false)
                            {
                                // Not a beta-defined message — handle the full/modded opcodes by raw value.
                                // op44 is the client's ~15s keepalive (benign, nothing to do) — don't spam
                                // the log with it; surface every other unknown opcode for field diagnosis.
                                if (RespondRaw(opcode, packet.Message.RawData) == false && opcode != 44)
                                    Logger.Log($"Unhandled message: opcode {opcode}");
                            }

                            if (Program.MessageDumpMode)
                                Logger.Log($"op {opcode}: {packet.Message.RawData.ToHexString()}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Skipping malformed message (opcode {opcode}, {total} bytes): {ex.Message}");
                        }
                    }

                    // Retain any trailing partial frame for the next read.
                    int remaining = data.Length - pos;
                    _pending = remaining > 0 ? data[pos..] : Array.Empty<byte>();
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Logger.Log(e.Message);
                    Disconnect();
                }
            }
        }

        /// <summary>
        /// Disconnects this client from the server.
        /// </summary>
        public void Disconnect()
        {
            _server.DisconnectClient(this);
        }

        /// <summary>
        /// Handles full-version / modded opcodes that aren't in the beta enum, by raw byte.
        /// Returns true if we sent a response. (Vanilla opcodes match beta; the full client's
        /// mod variants are appended at the high end — see docs/RE-FINDINGS.md.)
        /// </summary>
        private bool RespondRaw(byte opcode, byte[] payload)
        {
            switch (opcode)
            {
                case 15:  // LobbyCreateGameServerMsg (vanilla)
                case 51:  // LobbyCreateModGameServerMsg (modded client)
                    if (GameServer.TryParseCreate(this, payload, isModded: opcode == 51, out GameServer game))
                        Server.Games.Add(game);
                    else
                        Logger.Log($"{Username} create (opcode {opcode}) had an unparseable payload; acking anyway");
                    Send(new LobbyCreateGameResponseMsg() { Response = CreateGameResponse.Success });
                    return true;

                case 17:  // LobbyUpdateGameServerMsg — host refreshing its game's state/liveness
                    if (GameServer.TryReadId(payload, out ulong updateId))
                        Server.Games.Update(updateId, payload);
                    return true;

                case 18:  // LobbyRemoveGameServerMsg — host closed its game
                    if (GameServer.TryReadId(payload, out ulong removeId))
                        Server.Games.Remove(removeId);
                    return true;

                case 19:  // LobbyGetServerDetailsMsg — client selected a game, wants details.
                          // Reply with op20 ServerDetails (gated with the op53 experiment).
                    if (Program.Op53Enabled && GameServer.TryReadId(payload, out ulong detailsId))
                    {
                        foreach (GameServer g in Server.Games.GetAll())
                        {
                            if (g.Id != detailsId) continue;
                            SendRaw(20, g.BuildServerDetailsPayload());
                            Logger.Log($"{Username} requested details for 0x{detailsId:X} -> ServerDetails (op20)");
                            break;
                        }
                    }
                    return true;

                case 47:  // LobbySetLanguageMsg — 1-byte language id; nothing to do
                    return true;

                case 49:  // LobbySetModHashMsg — 4-byte mod hash; the client uses it to filter
                          // the game list. Remember it so we can match games in op53 later.
                    if (payload.Length >= 4)
                        ModHash = BitConverter.ToUInt32(payload, 0);
                    // Match the real lobby's login sequence: right after the client's modhash,
                    // send the roster — one op37 (LobbySetCharacterData/presence) per online host,
                    // then op36 (roster count). The joiner needs this to know the host is present.
                    // (Login-phase byte-match test 2026-06-25 — see docs/HANDOFF.md.)
                    foreach (GameServer g in Server.Games.GetAll())
                        if (g.Host != null)
                            SendRaw(37, BuildOp37(g, g.Host));
                    SendRaw(36, BitConverter.GetBytes((ushort)Server.Games.Count));
                    return true;

                case 52:  // LobbyGetModGameServersMsg — the game-browser list-refresh poll.
                          // Layout: [u32 flags][u16 requestSeq][u32 modHash][u8]. See docs/RE-FINDINGS.md.
                    if (payload.Length >= 6)
                    {
                        ushort seq = BitConverter.ToUInt16(payload, 4);
                        Logger.Log($"{Username} polled game list (GetModGameServers seq {seq}); " +
                                   $"{Server.Games.Count} game(s) available");
                    }
                    // EXPERIMENTAL (flag-gated): reply with one ModGameServersList (op53) per
                    // game, then a GameServersListEnd (op14) terminator. The op53 layout is
                    // reverse-engineered (docs/RE-FINDINGS.md) but some scalar roles are unconfirmed,
                    // so this is OFF by default — a malformed response may freeze the client
                    // (cf. the op33 NAT freeze). Enable with TL2_OP53_EXPERIMENT=1 for A/B capture.
                    if (Program.Op53Enabled)
                    {
                        // CONFIRMED from live-lobby capture (2026-06-24): the op52 reply for each game
                        // is op50(big game/char blob) + op50(10B trailer) + op53(list entry) + op14.
                        // The op50 big blob is REQUIRED before the client will enable Join.
                        foreach (GameServer g in Server.Games.GetAll())
                        {
                            SendRaw(50, g.BuildBigBlobPayload(g.Host?.ModHash ?? ModHash));
                            SendRaw(50, GameServer.BigBlobTrailer());
                            SendRaw(53, g.BuildModListPayload());
                        }
                        SendRaw(14, Array.Empty<byte>());   // GameServersListEnd (vanilla opcode, preserved)
                    }
                    return true;

                case 21:  // LobbyAttemptConnectMsg — joiner requests connect to a host.
                          // Payload: [4B addrToken = host IP LE][8B gameId(=8B session handle)].
                          // The NON-DEGENERATE runic SUCCESS brokers op22 DIRECTLY off op21 — there is
                          // no op25 ACK and no op27 wait (those were retry artifacts of the
                          // address-degenerate proxy capture, see docs/HANDOFF.md). So broker the
                          // connection immediately: op37(peer) + op22 to BOTH peers.
                    if (payload.Length >= 12)
                    {
                        byte[] addrToken = payload[0..4];
                        ulong gameId21 = BitConverter.ToUInt64(payload, 4);

                        GameServer game21 = null;
                        foreach (GameServer g in Server.Games.GetAll())
                            if (g.Id == gameId21) { game21 = g; break; }

                        Logger.Log($"{Username} op21 addrToken={BitConverter.ToUInt32(payload,0):X8} gameId=0x{gameId21:X} game={(game21?.Name ?? "NOT FOUND")} -> broker op22 (direct off op21)");
                        if (game21 != null)
                            BrokerConnect(game21, addrToken, this);
                    }
                    return true;

                case 26:  // LobbyConnectConfirmMsg — host echoes [8B session handle][4B joiner token]
                          // back to the lobby after it learns of the joiner (runic SUCCESS: host->lobby
                          // op26). Fire-and-forget host bookkeeping — runic sends nothing new in reply.
                          // Accept so it isn't logged as an "Unhandled message".
                    Logger.Log($"{Username} op26 connect-confirm: {Convert.ToHexString(payload)}");
                    return true;

                case 27:  // LobbyEndpointRegisterMsg — peer registers UDP endpoints. In the runic
                          // SUCCESS flow neither peer sends op27 (it was a degenerate-capture retry
                          // artifact), and op22 is identity-only so the port is not needed for
                          // brokering. We no longer gate on it — just record the dynamic P2P port if
                          // present and accept; op21 brokers directly.
                    if (payload.Length >= 4)
                        P2PUdpPort = BitConverter.ToUInt16(payload, 2);
                    Logger.Log($"{Username} op27 endpoint-reg: {Convert.ToHexString(payload)} (P2P udpPort={P2PUdpPort}) — recorded, no broker");
                    return true;

                case 32:  // LobbyFirewallTestRequest — "tell me about the client with this token."
                          // At login: client sends its OWN token (self-registration).
                          // After op22: each peer sends the OTHER's token to confirm connectivity.
                          // Captured format (real lobby 2026-06-24): C2S [4B token]; S2C op33 below.
                    if (payload.Length >= 4)
                    {
                        uint queriedToken = BitConverter.ToUInt32(payload, 0);
                        LobbyClient peer = Server.FindClientByToken(queriedToken);
                        if (peer != null)
                        {
                            SendRaw(33, BuildOp33(queriedToken, peer));
                            Logger.Log($"{Username} op32 token={queriedToken:X8} -> op33 for {peer.Username}");
                        }
                        else
                        {
                            Logger.Log($"{Username} op32 token={queriedToken:X8} -> no client found");
                        }
                    }
                    return true;

                case 46:  // LobbyEndpointExchangeMsg — joiner notifies lobby after op22.
                          // Captured: C2S [4B host_token][1B flag]. Real lobby relays an op37 roster
                          // update to the host; for now just ACK (don't drop as unhandled).
                    Logger.Log($"{Username} op46: {Convert.ToHexString(payload)}");
                    return true;

                case 23:  // LobbyRequestRelayConnect (0x17) — the client's AUTOMATIC relay request, sent
                          // after its P2P punch fails ("Failed all back connections to %08x, asking for
                          // relay..."). Payload = [4B relayKey]. PROVEN live 2026-06-27: TheeFroggacuda
                          // sent op23 = its own AddrToken once the cross-NAT punch went silent. Answer with
                          // AttemptRelayConnect to both peers + begin forwarding their UDP by pair.
                    if (payload.Length >= 4)
                        Server.OfferRelay(this, BitConverter.ToUInt32(payload, 0));
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Builds an op33 (LobbyFirewallTestSuccess) payload for a given client.
        /// Captured (real-lobby) format: [4B token][4B accountId][0xFF][FS8 username].
        /// accountId is a per-ACCOUNT id (a username hash), NOT the client IP — confirmed: the
        /// same id follows each account across op33 AND op37 in every captured session.
        /// (Was wrongly emitting peer.AddrToken/IP here.)
        /// </summary>
        private static byte[] BuildOp33(uint token, LobbyClient peer)
        {
            using System.IO.MemoryStream ms = new();
            ms.Write(BitConverter.GetBytes(token), 0, 4);
            ms.Write(AccountId(peer.Username), 0, 4);
            ms.WriteByte(0xFF);
            WriteFs8(ms, peer.Username);
            return ms.ToArray();
        }

        /// <summary>Per-account id for op33/op37. The real lobby derives this from the account (an
        /// un-reversed username hash); the client treats it as an opaque account handle and does not
        /// validate it, so any value that is STABLE per username (and consistent across both peers /
        /// the whole session) works. We keep the two captured ids byte-exact so the known test accounts
        /// stay identical to the reference capture, and derive a unique id for everyone else.</summary>
        private static byte[] AccountId(string username) => username switch
        {
            "froggacuda"     => new byte[] { 0xcd, 0x44, 0x40, 0x00 },
            "TheeFroggacuda" => new byte[] { 0xe1, 0x41, 0x9b, 0x00 },
            _                => DeriveAccountId(username),
        };

        /// <summary>Deterministic per-account id for accounts not in the capture. FNV-1a (32-bit) of the
        /// username, masked to 24 bits with a 0x00 high byte to match the captured id format
        /// (cd4440·00 / e1419b·00). Same username always maps to the same id on every peer.</summary>
        private static byte[] DeriveAccountId(string username)
        {
            uint h = 2166136261u;                                  // FNV-1a offset basis
            foreach (byte ch in System.Text.Encoding.UTF8.GetBytes(username ?? string.Empty))
                h = (h ^ ch) * 16777619u;                          // FNV-1a prime
            h &= 0x00FFFFFFu;                                      // 24-bit, top byte 0x00 (matches capture)
            if (h == 0) h = 1;                                     // never emit an all-zero id
            return new byte[] { (byte)h, (byte)(h >> 8), (byte)(h >> 16), 0x00 };
        }

        /// <summary>Host character name for the op37 roster entry (the second FS8). Hardcoded for
        /// the known test characters; falls back to the account name.</summary>
        private static string CharName(string username) => username switch
        {
            "froggacuda"     => "TEST",
            "TheeFroggacuda" => "test",
            _                => username ?? string.Empty,
        };

        /// <summary>Writes a FixedString8: [1B length][UTF-8 bytes].</summary>
        private static void WriteFs8(System.IO.MemoryStream ms, string s)
        {
            byte[] b = System.Text.Encoding.UTF8.GetBytes(s ?? string.Empty);
            int n = Math.Min(b.Length, 255);
            ms.WriteByte((byte)n);
            ms.Write(b, 0, n);
        }

        /// <summary>
        /// Builds an op37 (roster/presence) entry advertising one game member (host OR joiner),
        /// matching the real-lobby capture byte layout:
        ///   [4B accountId][4B member token][u16 0x0001][4B modhash][4B 0x00000000]
        ///   [1B 0x13 status][8B GameServerId(=session handle)][FS8 username][FS8 character name]
        /// At login this advertises the host to a joiner (joiner then op19's that id). At broker time
        /// the runic SUCCESS sends each peer an op37 about the OTHER (the PeerManager identity+handle
        /// binding the receiver needs) — so we now also send the host an op37 about the joiner.
        /// </summary>
        private static byte[] BuildOp37(GameServer g, LobbyClient member)
        {
            using System.IO.MemoryStream ms = new();
            ms.Write(AccountId(member?.Username ?? string.Empty), 0, 4);
            ms.Write(BitConverter.GetBytes(member?.AddrToken ?? 0u), 0, 4);
            ms.Write(BitConverter.GetBytes((ushort)0x0001), 0, 2);
            ms.Write(BitConverter.GetBytes(member?.ModHash ?? 0u), 0, 4);
            ms.Write(new byte[4], 0, 4);                       // 0x00000000
            ms.WriteByte(0x13);                                // status flag (from capture)
            ms.Write(BitConverter.GetBytes(g.Id), 0, 8);       // GameServerId = 8B session handle
            WriteFs8(ms, member?.Username ?? string.Empty);
            WriteFs8(ms, CharName(member?.Username ?? string.Empty));
            return ms.ToArray();
        }

        /// <summary>
        /// Brokers a P2P connection between a joiner and the game host by sending op22
        /// (LobbyAttemptConnectReplyMsg) to both peers.
        ///
        /// op22 layout (from live-lobby capture 2026-06-24):
        ///   [12B endpoint_of_OTHER_peer][2B FFFF][4B recipient_addrToken][1B slot]
        ///
        /// The 12-byte endpoint prefix encoding is not fully decoded; best-guess for LAN:
        ///   [4B peer_ip_LE][2B peer_udport_LE][6B zeros]
        /// The addrToken on our LAN = the peer's IP bytes (LE) from its TCP connection.
        /// Slot: host=02, joiner=03 (from live capture).
        /// </summary>
        private static void BrokerConnect(GameServer game, byte[] joinerAddrToken, LobbyClient joiner)
        {
            LobbyClient host = game.Host;
            if (host == null)
            {
                Logger.Log($"BrokerConnect: game {game.Id:X} has no host client — cannot broker");
                return;
            }

            // Derive IP bytes for each peer from their TCP connection address.
            byte[] hostIp = game.HostAddress.GetAddressBytes();   // 4 bytes LE
            byte[] joinerIp = (joiner.TcpClient.Client.RemoteEndPoint as System.Net.IPEndPoint)
                              ?.Address.GetAddressBytes() ?? new byte[4];

            // Host P2P UDP port: the host registers its own dynamic port via op27 (stored in
            // host.P2PUdpPort). Fall back to the configured UDPORT (Field1) only if the host hasn't
            // sent op27 yet. The dynamic port is per-session — NOT the configured UDPORT.
            ushort hostUdPort = host.P2PUdpPort != 0 ? host.P2PUdpPort : game.Field1;
            // Joiner P2P UDP port: recorded from an op27 if the joiner happened to send one (no longer
            // required for brokering — op22 is identity-only). For logging context only.
            ushort joinerUdPort = joiner.P2PUdpPort;

            // op22 token field = the OTHER peer's token (confirmed from real-lobby capture 2026-06-24).
            // After receiving op22, each peer sends op32 with this token to verify the other is online.
            // Host gets JOINER's token; joiner gets HOST's token.
            byte[] hostTokenBytes   = BitConverter.GetBytes(host.AddrToken);   // host IP bytes LE
            byte[] joinerTokenBytes = BitConverter.GetBytes(joiner.AddrToken); // joiner IP bytes LE

            // op22 (opcode 0x16) — 19-byte payload, FULLY CRACKED via static RE 2026-06-26 (session 7,
            // docs/RE-FINDINGS.md). It is the LobbyAttemptConnectMsg the client's NAT-punch handler
            // @0x6aa6d0 acts on (serializer 0x69ceb0 writes byte 0x16 first). The TRUE layout is TWO
            // endpoints + key + makeClient, with IP/port fields XOR-MASKED by the peer's AddrToken:
            //   [u32 privIP][u16 privPort][u32 pubIP][u16 pubPort][0xff][0xff][u32 peerToken][u8 makeClient]
            // CIPHER (makeSockaddr 0x461e30): realIP_net = htonl(ntohl(wireIP) XOR token); same XOR with
            // (token & 0xffff) for the port. ENCODE: wireIP = bigendian32(bigendian(realIP) XOR token);
            // wirePort = bigendian16(realPort XOR (token & 0xffff)). Verified byte-exact vs the
            // non-degenerate runic SUCCESS capture (captures/runic_success_broker_decode.txt).
            //
            // WHY THE OLD "identity/LoginKey" READING WORKED ON LAN: the client's op8 LoginKey (6B) IS
            // its own masked PRIVATE endpoint (privIP+privPort) — it decodes cleanly to the joiner's private
            // LAN endpoint in the runic capture. Echoing peerLoginKey verbatim therefore delivers the peer's PRIVATE ep
            // correctly (LAN-reachable). What we got WRONG was bytes 6-11 (the PUBLIC ep): we filled them
            // with [0x42][session][deriv] garbage, so cross-NAT the client's getaddrinfo on the public
            // candidate failed immediately ("network name invalid" / firewall error) — the live WAN failure.
            //
            // FIX (session 7): keep bytes 0-5 = peerLoginKey verbatim (proven private ep), and fill the
            // PUBLIC ep (bytes 6-11) properly: pubIP = the peer's real public IP (its TCP-connection source
            // address, which the lobby observes on accept), masked by the same peerToken we place in the key
            // field; pubPort = 0 for now (masks to token-low, exactly as runic's same-LAN capture left it).
            // The public UDP PORT is the one datum still missing — it needs a UDP facilitator to observe the
            // peer's NAT-mapped source port (no client->lobby UDP has ever been seen in our tests). Until
            // then: on LAN the private path still completes (unchanged); on a port-preserving NAT the public
            // IP alone may suffice; on symmetric NAT op35 relay remains the fallback.

            // Masked public endpoint for a peer: 6 bytes = [u32 pubIP BE-masked][u16 pubPort BE-masked].
            // pubIP = the peer's real public IP (its TCP-connection source addr, observed on accept).
            byte[] MaskedPublicEp(LobbyClient peer, uint token, ushort privPort)
            {
                var addr = (peer.TcpClient.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address;
                byte[] ip = addr?.GetAddressBytes() ?? new byte[4];             // network order
                // #5 fix: a co-located host (lobby on the same PC/LAN as the game host) shows up here as
                // 127.0.0.1 / 192.168.x — masking THAT into op22 makes the remote joiner punch a loopback/
                // private address (dead). Substitute the lobby's configured public IP (TL2_RELAY_IP),
                // exactly as the relay path already does (LobbyServer.RelayEndpointFor). IP only — pubPort
                // stays 0, matching the proven WAN-success capture. Remote peers are left untouched.
                if (addr != null && LobbyServer.IsPrivate(addr))
                {
                    string pub = LobbyServer.RelayPublicIP;
                    if (!string.IsNullOrEmpty(pub))
                        ip = System.Net.IPAddress.Parse(pub).GetAddressBytes();
                    else
                        Logger.Log($"[#5] co-located/private host {addr} seen but TL2_RELAY_IP is unset — " +
                                   "advertising its private ep; remote joiners will fail. Set TL2_RELAY_IP=<your public IP>.");
                }
                uint ipBE = ((uint)ip[0] << 24) | ((uint)ip[1] << 16) | ((uint)ip[2] << 8) | ip[3];
                uint m = ipBE ^ token;
                ushort pubPort = 0;                                              // 0 = client pairs pub IP w/ a port it knows (proven on WAN)
                ushort pm = (ushort)(pubPort ^ (ushort)(token & 0xFFFF));
                return new byte[] { (byte)(m >> 24), (byte)(m >> 16), (byte)(m >> 8), (byte)m,
                                    (byte)(pm >> 8), (byte)pm };
            }

            //   [6B peer LoginKey = masked PRIVATE ep][6B masked PUBLIC ep][ffff][4B peer token][1B makeClient]
            byte[] BuildOp22(LobbyClient peer, byte[] peerToken, byte flag, ushort peerPrivPort)
            {
                using System.IO.MemoryStream ms = new();
                ms.Write(peer.LoginKey, 0, 6);                                 // 6B masked private ep (verbatim)
                ms.Write(MaskedPublicEp(peer, peer.AddrToken, peerPrivPort), 0, 6); // 6B masked public ep
                ms.WriteByte(0xFF); ms.WriteByte(0xFF);                        // ffff
                ms.Write(peerToken, 0, 4);                                     // 4B peer token (= the XOR key)
                ms.WriteByte(flag);                                            // 1B makeClient (host=02, joiner=03)
                return ms.ToArray();
            }

            // To joiner: HOST's eps + host token; makeClient 0x03 (joiner is the initiator).
            byte[] op22ToJoiner = BuildOp22(host,   hostTokenBytes,   0x03, hostUdPort);
            // To host:   JOINER's eps + joiner token; makeClient 0x02.
            byte[] op22ToHost   = BuildOp22(joiner, joinerTokenBytes, 0x02, joinerUdPort);

            // Roster/presence binding (op37) FIRST — the runic SUCCESS sends each peer an op37 about
            // the OTHER right before op22: [acct][token][0001][modhash][0000][0x13][8B handle][names].
            // This is the PeerManager identity+handle entry the receiver needs. Without the host's
            // entry for the joiner, the host's incoming peer ("client 100") never gets populated ->
            // getaddrinfo on an empty address -> "network name invalid" (the loop that filled host.txt).
            host.SendRaw(37,   BuildOp37(game, joiner));   // tell HOST about the joiner
            joiner.SendRaw(37, BuildOp37(game, host));     // (re)bind JOINER to the host, handle-bound

            // DEBUG (TL2_BREAK_PUNCH=1, debug mode only): force the P2P punch to FAIL by pointing both op22
            // endpoints at a valid-looking but never-routable IP (TEST-NET-2 198.51.100.50), masked by the
            // receiver's token. The client tries, fails the back-connect, and falls back to op23
            // RequestRelayConnect — lets us exercise the RELAY path on the LAN. Gated behind MessageDumpMode
            // so a release run (no -dump/-debug) can NEVER honor it and sabotage real connections.
            if (Program.MessageDumpMode && Environment.GetEnvironmentVariable("TL2_BREAK_PUNCH") == "1")
            {
                static byte[] BrokenEp(uint token)
                {
                    uint ipBE = (198u << 24) | (51u << 16) | (100u << 8) | 50u;
                    uint m = ipBE ^ token;
                    ushort pm = (ushort)(1 ^ (ushort)(token & 0xFFFF));
                    return new byte[] { (byte)(m >> 24), (byte)(m >> 16), (byte)(m >> 8), (byte)m,
                                        (byte)(pm >> 8), (byte)pm };
                }
                byte[] bj = BrokenEp(host.AddrToken);     // joiner receives the HOST's (now broken) endpoint
                Array.Copy(bj, 0, op22ToJoiner, 0, 6); Array.Copy(bj, 0, op22ToJoiner, 6, 6);
                byte[] bh = BrokenEp(joiner.AddrToken);   // host receives the JOINER's (now broken) endpoint
                Array.Copy(bh, 0, op22ToHost, 0, 6); Array.Copy(bh, 0, op22ToHost, 6, 6);
                Logger.Log("[BREAK_PUNCH] op22 endpoints set unreachable to force the relay fallback");
            }

            joiner.SendRaw(22, op22ToJoiner);
            host.SendRaw(22, op22ToHost);

            // Pre-register the host<->joiner relay pairing so the strict-NAT fallback is armed the instant
            // either client asks for relay (op23) after its punch fails. Harmless if the punch succeeds.
            joiner.Server.RegisterRelayPair(host.AddrToken, joiner.AddrToken);

            Logger.Log($"BrokerConnect(2ep op22): joiner={joiner.Username} <- HOST " +
                       $"op22={Convert.ToHexString(op22ToJoiner)} privKey={Convert.ToHexString(host.LoginKey)} pubIP={hostIp[0]}.{hostIp[1]}.{hostIp[2]}.{hostIp[3]} privPort={hostUdPort}; " +
                       $"host={host.Username} <- JOINER op22={Convert.ToHexString(op22ToHost)} privKey={Convert.ToHexString(joiner.LoginKey)} pubIP={joinerIp[0]}.{joinerIp[1]}.{joinerIp[2]}.{joinerIp[3]} privPort={joinerUdPort}");
        }

        /// <summary>
        /// Sends a message to the server.
        /// </summary>
        public void Send(LobbyMessage message)
        {
            Packet packet = new(message);
            TcpClient.Client.Send(packet.Serialize());
        }

        /// <summary>
        /// Sends a raw framed message for opcodes not modelled by the beta enum:
        /// [1B opcode][2B big-endian length][payload]. Used for the full-version mod messages.
        /// </summary>
        public void SendRaw(byte opcode, byte[] payload)
        {
            byte[] frame = new byte[3 + payload.Length];
            frame[0] = opcode;
            frame[1] = (byte)(payload.Length >> 8);
            frame[2] = (byte)(payload.Length & 0xFF);
            Buffer.BlockCopy(payload, 0, frame, 3, payload.Length);
            TcpClient.Client.Send(frame);
        }

        /// <summary>
        /// Saves a raw message to a file.
        /// </summary>
        private void SaveMessageToFile(Packet packet)
        {
            string root = AppContext.BaseDirectory;   // works in single-file publish (Assembly.Location is empty there)
            string packetDir = Path.Combine(root, "DumpedMessages");
            if (Directory.Exists(packetDir) == false)
                Directory.CreateDirectory(packetDir);

            string filePath = $"[{DateTime.Now:yyyy-dd-MM_HH.mm.ss.fff}] {packet.Opcode}.bin";
            File.WriteAllBytes(Path.Combine(packetDir, filePath), packet.Message.RawData);
        }
    }
}
