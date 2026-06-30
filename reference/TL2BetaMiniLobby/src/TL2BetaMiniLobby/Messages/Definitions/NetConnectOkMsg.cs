using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.NetConnectOkMsg)]
    public class NetConnectOkMsg : LobbyMessage
    {
        public ulong Field0 { get; set; }
        public uint Field1 { get; set; }
        public byte Field2 { get; set; }
        public uint Field3 { get; set; }
        public uint ClientKey { get; set; }

        public NetConnectOkMsg() { }
        public NetConnectOkMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            Field0 = reader.ReadUInt64();
            Field1 = reader.ReadUInt32();
            Field2 = reader.ReadByte();
            Field3 = reader.ReadUInt32();
            ClientKey = reader.ReadUInt32();
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.Write(Field0);
            writer.Write(Field1);
            writer.Write(Field2);
            writer.Write(Field3);
            writer.Write(ClientKey);
        }

        protected override void BuildString(StringBuilder sb)
        {
            base.BuildString(sb);
            sb.AppendLine($"{nameof(Field0)}: 0x{Field0:X}");
            sb.AppendLine($"{nameof(Field1)}: 0x{Field1:X}");
            sb.AppendLine($"{nameof(Field2)}: 0x{Field2:X}");
            sb.AppendLine($"{nameof(Field3)}: 0x{Field3:X}");
            sb.AppendLine($"{nameof(ClientKey)}: 0x{ClientKey:X}");
        }
    }
}
