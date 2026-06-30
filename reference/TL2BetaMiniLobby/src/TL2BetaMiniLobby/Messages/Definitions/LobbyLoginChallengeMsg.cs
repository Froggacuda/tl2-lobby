using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.LobbyLoginChallengeMsg)]
    public class LobbyLoginChallengeMsg : LobbyMessage
    {
        public string Salt { get; set; } = string.Empty;
        public string Field1 { get; set; } = string.Empty;

        public LobbyLoginChallengeMsg() { }
        public LobbyLoginChallengeMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            Salt = reader.ReadFixedString8();
            Field1 = reader.ReadFixedString8();
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.WriteFixedString8(Salt);
            writer.WriteFixedString8(Field1);
        }

        protected override void BuildString(StringBuilder sb)
        {
            sb.AppendLine($"{nameof(Salt)}: {Salt}");
            sb.AppendLine($"{nameof(Field1)}: {Field1}");
        }
    }
}
