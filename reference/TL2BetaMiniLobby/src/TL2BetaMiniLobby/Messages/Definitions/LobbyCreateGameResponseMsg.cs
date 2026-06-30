using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    public enum CreateGameResponse : byte
    {
        Success,
        GameNameExists
    }

    [LobbyMessage(LobbyOpcode.LobbyCreateGameResponseMsg)]
    public class LobbyCreateGameResponseMsg : LobbyMessage
    {
        public CreateGameResponse Response { get; set; }

        public LobbyCreateGameResponseMsg() { }
        public LobbyCreateGameResponseMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            Response = (CreateGameResponse)reader.ReadByte();
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.Write((byte)Response);
        }

        protected override void BuildString(StringBuilder sb)
        {
            sb.AppendLine($"{nameof(Response)}: {Response}");
        }
    }
}
