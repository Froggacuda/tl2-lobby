using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.NetDisconnectMsg)]
    public class NetDisconnectMsg : LobbyMessage
    {
        public byte Field0 { get; set; }

        public NetDisconnectMsg() { }
        public NetDisconnectMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            Field0 = reader.ReadByte();
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.Write(Field0);
        }

        protected override void BuildString(StringBuilder sb)
        {
            sb.AppendLine($"{nameof(Field0)}: 0x{Field0:X}");
        }
    }
}
