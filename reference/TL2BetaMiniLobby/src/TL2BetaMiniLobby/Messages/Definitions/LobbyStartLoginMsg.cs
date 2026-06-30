using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.LobbyStartLoginMsg)]
    public class LobbyStartLoginMsg : LobbyMessage
    {
        public ushort CharacterLevel { get; set; }
        public ulong Field1 { get; set; }       // Same as character data
        public uint Field2 { get; set; }
        public ushort Field3 { get; set; }
        public string Username { get; set; }
        public string CharacterName { get; set; }

        public LobbyStartLoginMsg() { }
        public LobbyStartLoginMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            CharacterLevel = reader.ReadUInt16();
            Field1 = reader.ReadUInt64();
            Field2 = reader.ReadUInt32();
            Field3 = reader.ReadUInt16();
            Username = reader.ReadFixedString8();
            CharacterName = reader.ReadFixedString8();
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.Write(CharacterLevel);
            writer.Write(Field1);
            writer.Write(Field2);
            writer.Write(Field3);
            writer.WriteFixedString8(Username);
            writer.WriteFixedString8(CharacterName);
        }

        protected override void BuildString(StringBuilder sb)
        {
            sb.AppendLine($"{nameof(CharacterLevel)}: {CharacterLevel}");
            sb.AppendLine($"{nameof(Field1)}: 0x{Field1:X}");
            sb.AppendLine($"{nameof(Field2)}: 0x{Field2:X}");
            sb.AppendLine($"{nameof(Field3)}: 0x{Field3:X}");
            sb.AppendLine($"{nameof(Username)}: {Username}");
            sb.AppendLine($"{nameof(CharacterName)}: {CharacterName}");
        }
    }
}
