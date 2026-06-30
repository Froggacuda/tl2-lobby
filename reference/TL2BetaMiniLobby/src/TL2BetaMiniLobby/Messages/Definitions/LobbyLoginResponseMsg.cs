using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.LobbyLoginResponseMsg)]
    public class LobbyLoginResponseMsg : LobbyMessage
    {
        public byte[] LoginResponse { get; private set; }

        public LobbyLoginResponseMsg() { }
        public LobbyLoginResponseMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            LoginResponse = reader.ReadBytes(32);
        }

        protected override void BuildString(StringBuilder sb)
        {
            sb.AppendLine($"{nameof(LoginResponse)}: {LoginResponse.ToHexString()}");
        }
    }
}
