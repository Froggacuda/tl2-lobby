using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    public enum LoginResult : byte
    {
        Success = 0,
        InvalidUsernameOrPassword = 1,
        DuplicateLogin = 3
    }

    [LobbyMessage(LobbyOpcode.LobbyLoginResultMsg)]
    public class LobbyLoginResultMsg : LobbyMessage
    {
        public LoginResult Result { get; set; }
        public uint Field1 { get; set; }
        public ushort Field2 { get; set; }
        public string Field3 { get; set; } = string.Empty;  // This is structured like a string, but it's empty in all of our dumped messages

        public LobbyLoginResultMsg() { }
        public LobbyLoginResultMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            Result = (LoginResult)reader.ReadByte();
            Field1 = reader.ReadUInt32();
            Field2 = reader.ReadUInt16();
            Field3 = reader.ReadFixedString8();
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.Write((byte)Result);
            writer.Write(Field1);
            writer.Write(Field2);
            writer.Write(Field3);
        }

        protected override void BuildString(StringBuilder sb)
        {
            sb.AppendLine($"{nameof(Result)}: {Result}");
            sb.AppendLine($"{nameof(Field1)}: 0x{Field1}");
            sb.AppendLine($"{nameof(Field2)}: 0x{Field2}");
            sb.AppendLine($"{nameof(Field3)}: {Field3}");
        }
    }
}
