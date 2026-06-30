using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.NetConnectMsg)]
    public class NetConnectMsg : LobbyMessage
    {
        public ulong Field0 { get; set; }       // Protocol information?
        public uint ClientKey { get; set; }     // Essentially a session id. According to logs, it should be 0x00000000 here
        public uint Field2 { get; set; }

        public NetConnectMsg() { }
        public NetConnectMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            Field0 = reader.ReadUInt64();
            ClientKey = reader.ReadUInt32();
            Field2 = reader.ReadUInt32();
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.Write(Field0);
            writer.Write(ClientKey);
            writer.Write(Field2);
        }

        protected override void BuildString(StringBuilder sb)
        {
            base.BuildString(sb);
            sb.AppendLine($"{nameof(Field0)}: 0x{Field0:X}");
            sb.AppendLine($"{nameof(ClientKey)}: 0x{ClientKey:X}");
            sb.AppendLine($"{nameof(Field2)}: 0x{Field2:X}");
        }
    }
}
