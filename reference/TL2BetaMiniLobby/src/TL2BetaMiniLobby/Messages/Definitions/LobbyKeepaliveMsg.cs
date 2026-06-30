using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.LobbyKeepaliveMsg)]
    public class LobbyKeepaliveMsg : LobbyMessage
    {
        // Empty message
        public LobbyKeepaliveMsg() { }
        public LobbyKeepaliveMsg(byte[] rawData) : base(rawData) { }
        protected override void BuildString(StringBuilder sb) { }
    }
}
