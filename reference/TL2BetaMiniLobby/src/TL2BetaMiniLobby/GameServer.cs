// tl2-lobby — community Torchlight 2 replacement lobby server.
// Copyright (C) 2026 Froggacuda. Licensed under AGPL-3.0 (see LICENSE / NOTICE).
// Derived from TL2BetaMiniLobby (c) 2023 Crypto137, MIT-licensed
// (https://github.com/Crypto137/TL2BetaMiniLobby) — see THIRD-PARTY-NOTICES.txt.

using System.Net;
using System.Text;

namespace TL2BetaMiniLobby
{
    /// <summary>
    /// A game hosted by a player and tracked in the lobby's <see cref="GameRegistry"/>.
    /// Built from a create message (vanilla opcode 15 or modded opcode 51) and refreshed
    /// from UpdateGameServer (opcode 17) messages. See docs/CAPTURE-FINDINGS.md for the
    /// wire layout these are parsed from.
    /// </summary>
    public class GameServer
    {
        public ulong Id { get; private set; }
        public ushort Field1 { get; private set; }
        public ushort LevelRangeMin { get; private set; }
        public ushort LevelRangeMax { get; private set; }
        public ushort Flags { get; private set; }
        public byte MaxPlayers { get; private set; }
        public byte NewGamePlusLevel { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;

        /// <summary>True if created via the modded create message (opcode 51).</summary>
        public bool IsModded { get; private set; }

        /// <summary>The host client; used to broker joins and to evict the game on disconnect.</summary>
        public LobbyClient Host { get; }

        /// <summary>Host's source address as seen by the lobby (for join brokering / NAT).</summary>
        public IPAddress HostAddress { get; }

        /// <summary>Raw create payload, kept verbatim until the modded tail is fully decoded.</summary>
        public byte[] RawCreate { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// The host's game-state blob — everything in the create payload after Name/Description
        /// (compressed game/character/mod data). Forwarded to joiners in op53/op20 so the client
        /// can populate server details and enable Join. Refreshed from op17 updates.
        /// </summary>
        public byte[] GameStateBlob { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// The create-tail split into its component FixedString16 chunks. CONFIRMED 2026-06-24:
        /// the tail is exactly [FixedString16 chunk0][FixedString16 chunk1], each an independent
        /// zlib stream (chunk0 ~20B→1416B zeros; chunk1 ~468B→1504B game/character data). The op20
        /// ServerDetails deserializer (fcn.0069ce00) reads ONE FixedString16 and inflates it, so a
        /// joiner needs a single chunk here, NOT the re-wrapped whole tail (that caused the
        /// "ERROR IN UNCOMPRESSING DATA" → no Join). See docs/HANDOFF.md.
        /// </summary>
        public IReadOnlyList<byte[]> StateChunks { get; private set; } = Array.Empty<byte[]>();

        /// <summary>zlib of a 1416-byte all-zero struct — byte-identical to what the real host/lobby
        /// sends as the create blob#1 "summary" (verified 2026-06-24). Used when the host's op51
        /// blob#1 is empty (host not yet in-game), so op20/op53 always carry a valid 1416B summary
        /// the client can inflate. The 1416B (0x588) size is the client's expected struct size.</summary>
        private static readonly byte[] EmptySummary =
            { 0x78,0x9c,0x63,0x60,0x18,0x05,0xa3,0x60,0x14,0x8c,0x82,0x51,0x30,0x90,0x00,0x00,0x05,0x88,0x00,0x01 };

        /// <summary>Create blob#1 → the ~1416B "summary" the client shows in op53 list + op20 details
        /// (CONFIRMED from live-lobby capture 2026-06-24). Falls back to a synthesized zeroed summary
        /// when the host's op51 blob#1 is empty, so Join can still appear.</summary>
        public byte[] SummaryBlob =>
            (RefreshedSummary is { Length: > 0 }) ? RefreshedSummary
            : (StateChunks.Count > 0 && StateChunks[0].Length > 0) ? StateChunks[0]
            : EmptySummary;

        /// <summary>The host's live ~1416B character "summary", captured from op17 updates. The op51
        /// blob#1 is only a canned zeroed summary (host not yet in-game); the real character data
        /// arrives later via op17 (CONFIRMED from live-lobby capture: real op20 summary == host op17
        /// blob). Once set, it takes precedence in op53/op20 so the joiner sees the character + Join.</summary>
        private byte[] RefreshedSummary = Array.Empty<byte>();

        /// <summary>Create blob#2 → the ~1504B big game/character blob the client needs via op50
        /// BEFORE it will enable Join (CONFIRMED from live-lobby capture 2026-06-24).</summary>
        public byte[] BigBlob => StateChunks.Count > 1 ? StateChunks[1] : Array.Empty<byte>();

        public DateTime CreatedUtc { get; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; private set; } = DateTime.UtcNow;

        private GameServer(LobbyClient host)
        {
            Host = host;
            // For a real client this is its source address; for a host-less phantom fall back to
            // 0.0.0.0 (NOT IPAddress.None = 255.255.255.255, whose 0xFF bytes can be misread as
            // NG+/other scalars in the op53 list entry — see docs/RE-FINDINGS.md).
            HostAddress = (host?.TcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.Any;
        }

        /// <summary>
        /// Builds a synthetic game with no real host, for testing the op53 browser list against
        /// a single client (which can't both host and browse). Enabled via Program seeding.
        /// </summary>
        public static GameServer CreatePhantom(string name, string description)
        {
            return new GameServer(null)
            {
                Id = 0xDEADBEEFCAFEF00D,
                Field1 = 0,
                LevelRangeMin = 1,
                LevelRangeMax = 100,
                Flags = 0,
                MaxPlayers = 8,
                NewGamePlusLevel = 0,
                Name = name,
                Description = description,
                IsModded = true
            };
        }

        /// <summary>
        /// Parses a create payload (the bytes after the [opcode][len] frame header). The prefix
        /// is identical for vanilla (op15) and modded (op51) create messages; the modded tail
        /// (mod list / hashes / compressed character blob) is retained raw in <see cref="RawCreate"/>.
        /// Returns false if the payload is too short to be a valid create.
        /// </summary>
        public static bool TryParseCreate(LobbyClient host, byte[] payload, bool isModded, out GameServer game)
        {
            game = null;
            try
            {
                using MemoryStream stream = new(payload);
                using BinaryReader reader = new(stream);

                GameServer g = new(host)
                {
                    IsModded = isModded,
                    RawCreate = payload
                };

                g.Id = reader.ReadUInt64();
                g.Field1 = reader.ReadUInt16();
                g.LevelRangeMin = reader.ReadUInt16();
                g.LevelRangeMax = reader.ReadUInt16();
                g.Flags = reader.ReadUInt16();
                g.MaxPlayers = reader.ReadByte();
                // The full client sends the NewGamePlusLevel byte the beta reference left commented out.
                g.NewGamePlusLevel = reader.ReadByte();
                g.Name = reader.ReadFixedString8();
                g.Description = reader.ReadFixedString8();
                // Everything after Description is the host's compressed game-state blob; keep it
                // verbatim to forward to joiners in op53/op20. (STABLE baseline. Today's two-blob
                // experiments — forwarding blob#1, then blob#2 — did not enable Join and the blob#2
                // variant desynced the stream reassembler, so both were reverted. See docs/HANDOFF.md
                // and the ReadFixedString16 helper retained in Extensions.cs for tomorrow's work.)
                long pos = stream.Position;
                g.GameStateBlob = (pos < payload.Length)
                    ? payload[(int)pos..]
                    : Array.Empty<byte>();

                // Split the create-tail into its component FixedString16 chunks (each an
                // independent zlib stream). The op20 details relay sends ONE of these, not the
                // whole tail. See StateChunks / SelectDetailBlob.
                g.StateChunks = SplitFixedString16Chunks(g.GameStateBlob);

                // DIAGNOSTIC 2026-06-24: ground-truth capture for the zlib-inflate failure on the
                // joiner ("ERROR IN UNCOMPRESSING DATA"). Dump the raw create-tail and its chunk
                // anatomy. Pure logging; remove once the relay fix is fully verified.
                LogBlobAnatomy($"op51-recv user={host?.Username} id=0x{g.Id:X}", g.GameStateBlob);

                game = g;
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        /// <summary>
        /// Builds the op53 ModGameServersList payload for this game (the bytes AFTER the
        /// [opcode][len] frame header). Layout reverse-engineered from the client serializer
        /// fcn.0069d850 — see docs/RE-FINDINGS.md. Multi-byte fields are little-endian.
        /// EXPERIMENTAL: several scalar roles are still unconfirmed; known fields
        /// (GameServerId, Name, Description, level range, players) are populated, the rest zeroed.
        /// </summary>
        public byte[] BuildModListPayload()
        {
            // Phantom (host-less) test games allow each ambiguous scalar to be overridden via
            // env vars TL2P_<field> so we can probe which wire offset is difficulty / NG+ / etc.
            // without rebuilding the exe. Real games ignore the overrides. See docs/RE-FINDINGS.md.
            bool ph = Host == null;
            uint hostIp = HostAddress.GetAddressBytes().Length == 4
                ? BitConverter.ToUInt32(HostAddress.GetAddressBytes(), 0) : 0u;

            using MemoryStream stream = new();
            using BinaryWriter w = new(stream);

            w.Write((byte)Ov(ph, "B10", 0x08));         // @0x10  byte — CONFIRMED 0x08 from live capture; Join-eligibility gate
            w.Write((byte)Ov(ph, "B11", 0x01));         // @0x11  byte — CONFIRMED 0x01 from live capture
            // CONFIRMED 2026-06-23: the client reads the LEVEL RANGE from @0x12/@0x14. We were
            // writing Field1 (0x1033=4147) at @0x12, so the browser showed "4147-1" and the joiner
            // was "out of level range" -> Join hidden. Put the real bounds here.
            w.Write((ushort)Ov(ph, "LMIN", LevelRangeMin)); // @0x12  u16 — level range MIN
            w.Write((ushort)Ov(ph, "LMAX", LevelRangeMax)); // @0x14  u16 — level range MAX
            w.Write((ushort)Ov(ph, "F1", 0x0001));      // @0x16  u16 — CONFIRMED 0x0001 from live capture (NOT Field1/0x1033)
            w.Write((byte)Ov(ph, "MAX", 0x01));         // @0x18  byte — CONFIRMED 0x01 from live capture (semantics TBD; not literal max-players)
            w.Write((byte)Ov(ph, "B19", 0x08));         // @0x19  byte — CONFIRMED 0x08 from live capture
            // 2026-06-26: runic SUCCESS capture proves this field is the host's OPAQUE TOKEN, not its IP
            // (runic op53 +10 = 55fb9d0e = host AddrToken; ours leaked host LAN IP -> joiner replayed it as
            // op21 addrToken -> "client 100 network name invalid"). Emit the opaque AddrToken; port is
            // delivered separately via op51/op17. Falls back to hostIp for phantom (host-less) test games.
            w.Write((uint)Ov(ph, "IP", Host?.AddrToken ?? hostIp));  // @0x1c u32 — host identity TOKEN (was IP)
            w.Write(Id);                                // @0x20  u64 — GameServerId (confirmed)
            w.Write((ushort)Ov(ph, "U28", 0));          // @0x28  u16 [unknown]
            w.Write((ushort)Ov(ph, "U2A", 0x0019));     // @0x2a  u16 — CONFIRMED 0x0019 from live capture (the @0x24 "flag")
            // @0x2c = NewGamePlusLevel (CONFIRMED live 2026-06-23: the client compares this to
            // the joiner's NG+ rank; a mismatch shows "different New Game Plus rank" and hides Join).
            w.Write((byte)Ov(ph, "NGP", NewGamePlusLevel)); // @0x2c byte — NewGamePlusLevel
            w.Write((uint)Ov(ph, "MH", Host?.ModHash ?? 0u)); // @0x30 u32 — mod hash [guess]

            WriteFixedString8(w, Name);
            WriteFixedString8(w, Description);
            // FixedString16 tail = the ~1416B SUMMARY blob (create blob#1), NOT the whole tail.
            // CONFIRMED from live-lobby op53 capture (2026-06-24): the list entry carries only the
            // summary; the big game/char blob goes out separately via op50.
            WriteFixedString16(w, SummaryBlob);

            byte[] p = stream.ToArray();
            if (Program.MessageDumpMode)
                Logger.Log($"[OUTDUMP op53 id=0x{Id:X}] summaryLen={SummaryBlob.Length} totalLen={p.Length} hex={Convert.ToHexString(p)}");
            return p;
        }

        /// <summary>
        /// Returns the TL2P_&lt;name&gt; env override if set, else the default. Applies to all games
        /// (phantom and real) so op53 scalar fields can be probed live via run.bat without rebuilds.
        /// The isPhantom arg is retained for call-site clarity but no longer gates the override.
        /// </summary>
        private static long Ov(bool isPhantom, string name, long dflt)
        {
            string v = Environment.GetEnvironmentVariable("TL2P_" + name);
            if (string.IsNullOrEmpty(v)) return dflt;
            v = v.Trim();
            return v.StartsWith("0x")
                ? Convert.ToInt64(v.Substring(2), 16)
                : long.Parse(v);
        }

        /// <summary>
        /// Builds the op20 ServerDetails payload (bytes after the frame header). Layout from the
        /// client serializer fcn.0069ce00: [u64 GameServerId][FixedString16 details blob].
        /// The blob (server/character detail) is sent empty for now — enough to satisfy the
        /// client's GetServerDetails fetch. See docs/RE-FINDINGS.md.
        /// </summary>
        public byte[] BuildServerDetailsPayload()
        {
            using MemoryStream stream = new();
            using BinaryWriter w = new(stream);
            w.Write(Id);                          // u64 GameServerId
            // op20 details FS16 = the ~1416B SUMMARY blob (create blob#1). CONFIRMED from live-lobby
            // op20 capture (2026-06-24): 70-byte response = [u64 id][FS16 60B zlib→1416B summary].
            WriteFixedString16(w, SummaryBlob);
            byte[] p = stream.ToArray();
            if (Program.MessageDumpMode)
                Logger.Log($"[OUTDUMP op20 id=0x{Id:X}] summaryLen={SummaryBlob.Length} totalLen={p.Length} hex={Convert.ToHexString(p)}");
            return p;
        }

        /// <summary>
        /// Builds the op50 payload that delivers the big game/character blob to a joiner. CONFIRMED
        /// from live-lobby capture (2026-06-24): [u32 modHash][u32 count=1][FS16 = big blob (create
        /// blob#2, ~1504B inflated)]. The client needs this (sent in the op52 list reply, before
        /// op53) before it will enable Join. Followed by a fixed 10-byte op50 trailer.
        /// </summary>
        public byte[] BuildBigBlobPayload(uint modHash)
        {
            using MemoryStream stream = new();
            using BinaryWriter w = new(stream);
            w.Write(modHash);          // u32 mod hash (LE)
            w.Write((uint)1);          // u32 count
            WriteFixedString16(w, BigBlob);
            byte[] p = stream.ToArray();
            if (Program.MessageDumpMode)
                Logger.Log($"[OUTDUMP op50 id=0x{Id:X}] modhash=0x{modHash:X8} bigLen={BigBlob.Length} hex={Convert.ToHexString(p)}");
            return p;
        }

        /// <summary>The fixed 10-byte op50 trailer the live lobby sends right after the big-blob op50
        /// (observed constant `00 00 00 00 5c 59 00 00 00 00`).</summary>
        public static byte[] BigBlobTrailer() => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x5c, 0x59, 0x00, 0x00, 0x00, 0x00 };

        /// <summary>Walks a buffer as sequential FixedString16 chunks ([u16 LE len][bytes]…) and
        /// returns each chunk's inner bytes. Stops at the first non-aligned length.</summary>
        private static List<byte[]> SplitFixedString16Chunks(byte[] tail)
        {
            var chunks = new List<byte[]>();
            try
            {
                using MemoryStream ms = new(tail);
                using BinaryReader r = new(ms);
                while (ms.Position + 2 <= tail.Length)
                {
                    long off = ms.Position;
                    ushort len = r.ReadUInt16();
                    if (off + 2 + len > tail.Length) break;
                    chunks.Add(r.ReadBytes(len));
                }
            }
            catch { /* return whatever parsed cleanly */ }
            return chunks;
        }

        /// <summary>
        /// DIAGNOSTIC (2026-06-24): logs a create-tail blob and walks it as sequential
        /// FixedString16 chunks ([u16 LE len][bytes]...). Lets us see the two-blob structure
        /// (blob#1 ~49B + blob#2 ~464B) and extract the compressed game-state for offline zlib
        /// testing. Pure logging; no behavior change. Remove once the relay fix is in.
        /// </summary>
        private static void LogBlobAnatomy(string tag, byte[] tail)
        {
            if (!Program.MessageDumpMode) return;   // RE diagnostic — debug mode only
            Logger.Log($"[BLOBDUMP {tag}] totalLen={tail.Length}");
            Logger.Log($"[BLOBDUMP {tag}] hex={Convert.ToHexString(tail)}");
            try
            {
                using MemoryStream ms = new(tail);
                using BinaryReader r = new(ms);
                int i = 0;
                while (ms.Position + 2 <= tail.Length)
                {
                    long off = ms.Position;
                    ushort len = r.ReadUInt16();
                    if (off + 2 + len > tail.Length)
                    {
                        Logger.Log($"[BLOBDUMP {tag}] chunk{i} @{off} declares len={len} but only " +
                                   $"{tail.Length - off - 2} bytes remain -> not FixedString16-aligned; stop");
                        break;
                    }
                    byte[] chunk = r.ReadBytes(len);
                    string head = chunk.Length > 0 ? Convert.ToHexString(chunk, 0, Math.Min(12, chunk.Length)) : "";
                    Logger.Log($"[BLOBDUMP {tag}] chunk{i} @{off} len={len} head={head}");
                    i++;
                }
                if (ms.Position != tail.Length)
                    Logger.Log($"[BLOBDUMP {tag}] {tail.Length - ms.Position} trailing byte(s) after last full chunk");
            }
            catch (Exception e) { Logger.Log($"[BLOBDUMP {tag}] walk error: {e.Message}"); }
        }

        /// <summary>Writes a blob prefixed by its length as a little-endian unsigned 16-bit integer.</summary>
        private static void WriteFixedString16(BinaryWriter w, byte[] data)
        {
            data ??= Array.Empty<byte>();
            w.Write((ushort)Math.Min(data.Length, ushort.MaxValue));
            w.Write(data, 0, Math.Min(data.Length, ushort.MaxValue));
        }

        private static void WriteFixedString8(BinaryWriter w, string s)
        {
            byte[] b = System.Text.Encoding.UTF8.GetBytes(s ?? string.Empty);
            w.Write((byte)Math.Min(b.Length, byte.MaxValue));
            w.Write(b, 0, Math.Min(b.Length, byte.MaxValue));
        }

        /// <summary>Reads the GameServerId from the head of an update/remove payload (opcodes 17/18).</summary>
        public static bool TryReadId(byte[] payload, out ulong id)
        {
            id = 0;
            if (payload.Length < 8) return false;
            id = BitConverter.ToUInt64(payload, 0);
            return true;
        }

        /// <summary>Refreshes liveness from an UpdateGameServer (opcode 17) message.</summary>
        public void ApplyUpdate(byte[] payload)
        {
            UpdatedUtc = DateTime.UtcNow;

            // op17 layout (CONFIRMED 2026-06-24 from live capture): [u64 id][8B scalar header]
            // [1B][FixedString16 summary blob]. The blob is the host's refreshed ~1416B character
            // "summary" — the same slot the client renders in the op53 list entry and op20 details.
            // The initial op51 blob#1 is a canned zeroed summary (no character yet); the real one
            // only arrives via op17. Capture it so op20/op53 carry live character data (char icon +
            // Join). Empty-blob op17s (len=19) are pure liveness heartbeats — never clobber a good
            // summary with them.
            const int blobOffset = 17; // 8 (u64 id) + 8 (scalar header) + 1
            if (payload.Length >= blobOffset + 2)
            {
                ushort len = BitConverter.ToUInt16(payload, blobOffset);
                if (len > 0 && blobOffset + 2 + len <= payload.Length)
                    RefreshedSummary = payload[(blobOffset + 2)..(blobOffset + 2 + len)];
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append($"\"{Name}\" (id 0x{Id:X}) lvl {LevelRangeMin}-{LevelRangeMax}, ");
            sb.Append($"max {MaxPlayers}, host {Host?.Username ?? "(phantom)"}@{HostAddress}");
            if (IsModded) sb.Append(" [modded]");
            return sb.ToString();
        }
    }
}
