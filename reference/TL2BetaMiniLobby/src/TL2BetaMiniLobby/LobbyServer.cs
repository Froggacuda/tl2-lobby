// tl2-lobby — community Torchlight 2 replacement lobby server.
// Copyright (C) 2026 Froggacuda. Licensed under AGPL-3.0 (see LICENSE / NOTICE).
// Derived from TL2BetaMiniLobby (c) 2023 Crypto137, MIT-licensed
// (https://github.com/Crypto137/TL2BetaMiniLobby) — see THIRD-PARTY-NOTICES.txt.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TL2BetaMiniLobby
{
    public class LobbyServer
    {
        private const string BindIP = "0.0.0.0";
        private const int Port = 4549;   // clients connect here for both TCP lobby control and the UDP facilitator

        private readonly CancellationTokenSource _cts = new();

        private readonly HashSet<LobbyClient> _clients = new();

        private readonly TcpListener _listener;

        /// <summary>The games currently hosted in this lobby.</summary>
        public GameRegistry Games { get; } = new();

        // ── UDP RELAY (strict-NAT fallback; the project's "no Hamachi" wedge) ──────────────────────
        // Built 2026-06-27 after live WAN tests PROVED NAT punching can't complete here: the lobby never
        // learns a peer's public UDP port, and even both-external + port-preserving got total silence.
        // The relay sidesteps NAT entirely — both clients send gameplay UDP to us, we forward by pair.
        //
        // TRIGGER (proven live 2026-06-27): the client AUTO-asks for relay after its punch fails — it
        // sends op23 RequestRelayConnect [4B relayKey = its own AddrToken] (RE string "asking for
        // relay..."). We answer AttemptRelayConnect (op24 / 0x18 — the 0x17/0x18 serializer pair, with
        // 0x17=op23 now confirmed) to BOTH peers, each carrying [relay endpoint][peer key][flag]. Data
        // packets are [1B flag][4B key][payload]; we forward to the OTHER endpoint in the pair. Routing is
        // endpoint-based (the key only locates the pair), so it is robust to whether the client tags a
        // packet with its own key or the peer's — the exact opcode + wire bytes get pinned live.
        public const byte AttemptRelayOpcode = 24;   // 0x18 candidate — pin via client LOG CONSOLE
        /// <summary>This lobby's own public IP, set by the operator via TL2_RELAY_IP. Used to (a) rewrite a
        /// co-located/private host's op22 public ep (#5) and (b) advertise the relay endpoint to WAN peers.
        /// EMPTY by default — a public release must NOT bake in any one operator's IP. Self-hosters set it to
        /// their own public IP; if unset, the #5 substitution is skipped (co-located hosting won't reach
        /// remote joiners until it's configured).</summary>
        public static string RelayPublicIP => Environment.GetEnvironmentVariable("TL2_RELAY_IP") ?? "";
        /// <summary>The lobby's own LAN IP, advertised to LAN peers on the (parked) relay path. Auto-detected
        /// from the primary outbound interface so no operator value is baked in; override with TL2_RELAY_LAN_IP.</summary>
        private static string RelayLanIP =>
            Environment.GetEnvironmentVariable("TL2_RELAY_LAN_IP") ?? DetectLanIPv4();

        /// <summary>Best-effort local IPv4 of the primary interface. The UDP "connect" sends no packet — it just
        /// makes the OS pick the source interface for the default route. Falls back to loopback on failure.</summary>
        private static string DetectLanIPv4()
        {
            try
            {
                using Socket s = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("203.0.113.1", 65530);   // RFC 5737 documentation address; no traffic is sent
                return (s.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }

        private sealed class RelaySession   // one host<->joiner pairing; routes purely by source endpoint
        {
            public IPEndPoint EpA, EpB;
            // Datagrams received before BOTH endpoints are known are buffered here (NOT dropped) and flushed
            // the instant the partner registers. The punch's ONE-SHOT opening packets (0x90/0x11) arrive
            // microseconds before the partner's first datagram; dropping them stalled the handshake to a 6s
            // giveup (proven 2026-06-27: host's 0x90/0x11 were "held"/lost, joiner never saw connection-open).
            public readonly List<(IPEndPoint From, byte[] Data)> Pending = new();
            public void See(IPEndPoint ep)
            {
                if (ep.Equals(EpA) || ep.Equals(EpB)) return;
                if (EpA == null) EpA = ep; else if (EpB == null) EpB = ep;
            }
            public IPEndPoint Other(IPEndPoint ep) => ep.Equals(EpA) ? EpB : (ep.Equals(EpB) ? EpA : null);
            public void FlushTo(UdpClient sock)
            {
                foreach ((IPEndPoint from, byte[] data) in Pending)
                {
                    IPEndPoint d = Other(from);
                    if (d != null) sock.Send(data, data.Length, d);
                }
                Pending.Clear();
            }
        }
        private readonly ConcurrentDictionary<uint, RelaySession> _relaySessions = new();

        /// <summary>Pairs two peers (by AddrToken) so a relay datagram from one forwards to the other.
        /// Called at broker time, so the pairing exists before the client's op23 arrives.</summary>
        public void RegisterRelayPair(uint a, uint b)
        {
            RelaySession s = new();
            _relaySessions[a] = s;
            _relaySessions[b] = s;
        }

        /// <summary>Relay endpoint to advertise to a peer: LAN IP for a LAN peer, public IP for a WAN peer.</summary>
        public IPEndPoint RelayEndpointFor(LobbyClient c)
        {
            IPAddress ip = (c.TcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;
            bool lan = ip != null && IsPrivate(ip);
            return new IPEndPoint(IPAddress.Parse(lan ? RelayLanIP : RelayPublicIP), Port);
        }

        internal static bool IsPrivate(IPAddress a)   // internal: reused by LobbyClient op22 builder (#5 fix)
        {
            byte[] b = a.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                                     || (b[0] == 192 && b[1] == 168) || b[0] == 127);
        }

        /// <summary>Builds an AttemptRelayConnect (op24/0x18) payload. Wire format PINNED by static RE
        /// 2026-06-27 from the client DESERIALIZER (fcn.0069f690, AttemptRelay vtable 0x218e774 slot1) +
        /// handler (fcn.006a4c25). The read path consumes exactly 11 bytes:
        ///   [4B IP -> +0x10] [2B port -> +0x14] [4B peerKey -> +0x18] [1B flag -> +0x1c].
        /// (The serializer's leading htons(size-3) is the MESSAGE LENGTH, which our SendRaw framing
        /// [opcode][2B BE len][payload] already writes — prepending it shifts the payload, the bug that
        /// silenced the previous build.) The IP/port (+0x10/+0x14) are XOR-MASKED by the peerKey (+0x18):
        /// the handler feeds them to makeSockaddr @0x461e30, the SAME unmask as op22, so we reuse op22's
        /// proven cipher (BrokerConnect.MaskedPublicEp): masked IP big-endian, masked port big-endian.</summary>
        public static byte[] BuildAttemptRelay(IPEndPoint relay, uint peerKey, byte flag)
        {
            byte[] ip = relay.Address.GetAddressBytes();                        // network order a.b.c.d
            ushort port = (ushort)relay.Port;
            uint ipBE = ((uint)ip[0] << 24) | ((uint)ip[1] << 16) | ((uint)ip[2] << 8) | ip[3];
            uint mIP = ipBE ^ peerKey;                                          // XOR-mask IP (op22 cipher)
            ushort mPort = (ushort)(port ^ (ushort)(peerKey & 0xFFFF));         // XOR-mask port
            using MemoryStream ms = new();
            ms.WriteByte((byte)(mIP >> 24)); ms.WriteByte((byte)(mIP >> 16));   // [0..3] masked IP, big-endian
            ms.WriteByte((byte)(mIP >> 8));  ms.WriteByte((byte)mIP);
            ms.WriteByte((byte)(mPort >> 8)); ms.WriteByte((byte)mPort);        // [4..5] masked port, big-endian
            ms.Write(BitConverter.GetBytes(peerKey), 0, 4);                     // [6..9] peerKey (the XOR key)
            ms.WriteByte(flag);                                                 // [10] flag (host=02 / joiner=03)
            return ms.ToArray();
        }

        /// <summary>op23 handler: a peer's punch failed and it asked for relay. Answer AttemptRelayConnect
        /// to BOTH peers in the pair, each told to relay-connect to the OTHER's key via our relay ep.</summary>
        public void OfferRelay(LobbyClient requester, uint reqKey)
        {
            LobbyClient peer = FindRelayPeer(requester.AddrToken);
            if (peer == null)
            {
                Logger.Log($"[RELAY] op23 from {requester.Username} key={reqKey:X8} — no connected pair; ignoring");
                return;
            }
            IPEndPoint reqEp = RelayEndpointFor(requester);
            IPEndPoint peerEp = RelayEndpointFor(peer);
            requester.SendRaw(AttemptRelayOpcode, BuildAttemptRelay(reqEp,  peer.AddrToken,      0x03));
            peer.SendRaw(AttemptRelayOpcode,      BuildAttemptRelay(peerEp, requester.AddrToken, 0x02));
            Logger.Log($"[RELAY] op23({reqKey:X8}) from {requester.Username} -> AttemptRelayConnect op{AttemptRelayOpcode}: " +
                       $"{requester.Username} relay-to {peer.AddrToken:X8} via {reqEp}; {peer.Username} relay-to {requester.AddrToken:X8} via {peerEp}");
        }

        /// <summary>Finds the connected peer paired with this token via a registered relay session.</summary>
        private LobbyClient FindRelayPeer(uint token)
        {
            if (!_relaySessions.TryGetValue(token, out RelaySession s)) return null;
            lock (_clients)
                foreach (LobbyClient c in _clients)
                    if (c.AddrToken != token && _relaySessions.TryGetValue(c.AddrToken, out RelaySession s2)
                        && ReferenceEquals(s, s2))
                        return c;
            return null;
        }

        /// <summary>Relay data path: forward [1B flag][4B key][payload] to the OTHER endpoint in the pair.
        /// Endpoint-based routing (the key only locates the pair) tolerates either tagging convention.</summary>
        private void HandleRelayDatagram(UdpClient sock, UdpReceiveResult r)
        {
            byte[] b = r.Buffer;
            if (b.Length < 1) return;
            IPEndPoint src = r.RemoteEndPoint;

            // PRIMARY routing = by SOURCE ENDPOINT. Once a peer's UDP endpoint is known to a pair, every
            // datagram from it forwards to the partner — INCLUDING tokenless ones (proven 2026-06-27: the
            // joiner's 13B all-zero RakNet keepalives/acks `00..01..` carry no AddrToken; dropping them
            // stalled the handshake to an op46 giveup even though token-tagged packets relayed fine).
            RelaySession s = null;
            foreach (RelaySession cand in _relaySessions.Values)
                if (src.Equals(cand.EpA) || src.Equals(cand.EpB)) { s = cand; break; }

            // FALLBACK = locate the pair by a registered token (first datagram, endpoint not yet seen).
            // The 4B key's offset shifts with the RakNet relay-wrapper header (observed at +13); scan the
            // first 48B, testing BOTH byte orders (token rides the wire big-endian, AddrToken sessions are
            // keyed host/LE — datagram 0x65DB2F72 LE == registered 0x722FDB65 BE).
            if (s == null && b.Length >= 5)
            {
                int limit = Math.Min(b.Length - 4, 48);
                for (int off = 0; off <= limit; off++)
                {
                    uint le = BitConverter.ToUInt32(b, off);
                    if (le == 0) continue;
                    uint be = (le << 24) | ((le & 0xFF00) << 8) | ((le >> 8) & 0xFF00) | (le >> 24);
                    if (_relaySessions.TryGetValue(le, out s)) break;
                    if (_relaySessions.TryGetValue(be, out s)) break;
                }
            }
            if (s == null)
            {
                Logger.Log($"[RELAY] datagram from {src} len={b.Length} — no matching session; dropped");
                return;
            }
            s.See(src);
            IPEndPoint dest = s.Other(src);
            if (dest != null)
            {
                s.FlushTo(sock);   // deliver any opening packets buffered before this partner was known
                sock.Send(b, b.Length, dest);
                Logger.Log($"[RELAY] fwd {b.Length}B {src} -> {dest}");
            }
            else
            {
                if (s.Pending.Count < 16) s.Pending.Add((src, b));   // buffer (don't drop) until partner appears
                Logger.Log($"[RELAY] from {src} len={b.Length} — partner not seen; buffered ({s.Pending.Count})");
            }
        }

        /// <summary>Finds a connected client by their session addrToken (assigned in op1).</summary>
        public LobbyClient FindClientByToken(uint token)
        {
            lock (_clients)
                return _clients.FirstOrDefault(c => c.AddrToken == token);
        }

        public LobbyServer()
        {
            IPEndPoint endpoint = new(IPAddress.Parse(BindIP), Port);
            _listener = new(endpoint);
            _listener.Server.NoDelay = true;
            _listener.Server.LingerState = new(false, 0);
        }

        /// <summary>
        /// Starts listening and accepting client connections.
        /// </summary>
        public void Start()
        {
            _listener.Start();
            Task.Run(async () => await AcceptConnectionsAsync());
            Logger.Log($"LobbyServer is listening on {BindIP}:{Port}...");
            StartUdpProbes();
        }

        /// <summary>
        /// Diagnostic-only: binds UDP listeners on the TL2 NAT/gameplay ports + the lobby port and logs
        /// every datagram (source IP:port, dest port, hex). Pure instrumentation; the lobby is TCP-only
        /// and gameplay/NAT-punch is peer-to-peer (out of band). Safe to remove for a public release.
        /// </summary>
        private void StartUdpProbes()
        {
            int[] ports = { 4549, 4171, 4175, 4179 };
            foreach (int p in ports)
            {
                try
                {
                    UdpClient udp = new(new IPEndPoint(IPAddress.Parse(BindIP), p));
                    Task.Run(async () =>
                    {
                        Logger.Log($"UDP probe listening on {BindIP}:{p}...");
                        while (!_cts.IsCancellationRequested)
                        {
                            try
                            {
                                UdpReceiveResult r = await udp.ReceiveAsync(_cts.Token);
                                string hex = Convert.ToHexString(r.Buffer.Length > 64 ? r.Buffer[..64] : r.Buffer);
                                Logger.Log($"[UDP :{p}] <- {r.RemoteEndPoint} len={r.Buffer.Length} hex={hex}");
                                if (p == 4549) HandleRelayDatagram(udp, r);   // strict-NAT relay forward-by-pair
                            }
                            catch (OperationCanceledException) { break; }
                            catch (Exception ex) { Logger.Log($"[UDP :{p}] recv error: {ex.Message}"); }
                        }
                    });
                }
                catch (Exception ex) { Logger.Log($"UDP probe bind :{p} failed: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Disconnects a client.
        /// </summary>
        /// <param name="client"></param>
        public void DisconnectClient(LobbyClient client)
        {
            client.TcpClient.Close();
            RemoveClient(client);
            Games.RemoveByHost(client);     // Evict any games this client was hosting
            Logger.Log($"{client.Username} disconnected");
        }

        /// <summary>
        /// Disconnects all clients.
        /// </summary>
        public void DisconnectAllClients()
        {
            lock (_clients)
            {
                foreach (LobbyClient client in _clients)
                {
                    if (client.TcpClient.Connected == false) continue;
                    client.TcpClient.Close();
                }
            }

            _clients.Clear();
        }

        /// <summary>
        /// Shuts the server down.
        /// </summary>
        public void Shutdown()
        {
            _cts.Cancel();              // Cancel async tasks
            _listener?.Stop();          // Stop listening
            DisconnectAllClients();     // Disconnect all clients
        }

        /// <summary>
        /// Retrieves server status.
        /// </summary>
        public string GetStatus()
        {
            StringBuilder sb = new();

            sb.Append($"{_clients.Count} client(s) online");

            // Add client usernames if anyone's online
            if (_clients.Count > 0)
            {
                sb.Append(": ");
                foreach (LobbyClient client in _clients)
                    sb.Append(client.Username).Append(", ");
                sb.Length -= 2; // Remove last comma and space
            }

            return sb.ToString();
        }

        /// <summary>
        /// Removes a connected client.
        /// </summary>
        private void RemoveClient(LobbyClient client)
        {
            lock (_clients)
                _clients.Remove(client);
        }

        /// <summary>
        /// Accepts client connections asynchronously.
        /// </summary>
        private async Task AcceptConnectionsAsync()
        {
            while (true)
            {
                try
                {
                    // Wait for a client connection
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync().WaitAsync(_cts.Token);

                    // Accept the client
                    Logger.Log($"Accepting connection from {tcpClient.Client.RemoteEndPoint}...");
                    LobbyClient lobbyClient = new(this, tcpClient);
                    lock (_clients)
                        _clients.Add(lobbyClient);

                    // Start receiving data from the client
                    _ = Task.Run(async () => await lobbyClient.ReceiveDataAsync(_cts.Token));
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Logger.Log(e.Message);
                }
            }
        }
    }
}
