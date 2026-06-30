// tl2-lobby — community Torchlight 2 replacement lobby server.
// Copyright (C) 2026 Froggacuda. Licensed under AGPL-3.0 (see LICENSE / NOTICE).
// Derived from TL2BetaMiniLobby (c) 2023 Crypto137, MIT-licensed
// (https://github.com/Crypto137/TL2BetaMiniLobby) — see THIRD-PARTY-NOTICES.txt.

using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    // Server -> client: tells the client its firewall/NAT test passed so it will host.
    // Opcode 33 (vanilla, matches beta). Empty payload (sufficient to satisfy the client).
    [LobbyMessage(LobbyOpcode.LobbyFirewallTestSuccessMsg)]
    public class LobbyFirewallTestSuccessMsg : LobbyMessage
    {
        public LobbyFirewallTestSuccessMsg() { }
        public LobbyFirewallTestSuccessMsg(byte[] rawData) : base(rawData) { }

        public override void Encode(BinaryWriter writer) { /* empty payload */ }

        protected override void BuildString(StringBuilder sb)
        {
            sb.AppendLine(nameof(LobbyFirewallTestSuccessMsg));
        }
    }
}
