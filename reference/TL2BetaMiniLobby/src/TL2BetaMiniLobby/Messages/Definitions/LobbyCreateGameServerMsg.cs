using System.Text;

namespace TL2BetaMiniLobby.Messages.Definitions
{
    [LobbyMessage(LobbyOpcode.LobbyCreateGameServerMsg)]
    public class LobbyCreateGameServerMsg : LobbyMessage
    {
        public ulong GameServerId { get; private set; }
        public ushort Field1 { get; private set; }          // Changes after relaunching the game?
        public ushort LevelRangeMin { get; private set; }
        public ushort LevelRangeMax { get; private set; }
        public ushort Flags { get; private set; }           // Is this actually two separate 8-bit flag fields?
        public byte MaxPlayers { get; private set; }
        //public byte NewGamePlusLevel { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public byte[] Blob { get; private set; }            // Compressed? Also it appears it must be <= 1024 bytes

        public LobbyCreateGameServerMsg(byte[] rawData) : base(rawData) { }

        public override void Parse(BinaryReader reader)
        {
            GameServerId = reader.ReadUInt64();
            Field1 = reader.ReadUInt16();
            LevelRangeMin = reader.ReadUInt16();
            LevelRangeMax = reader.ReadUInt16();
            Flags = reader.ReadUInt16();
            MaxPlayers = reader.ReadByte();
            //NewGamePlusLevel = reader.ReadByte();
            Name = reader.ReadFixedString8();
            Description = reader.ReadFixedString8();
            Blob = reader.ReadBytes(reader.ReadUInt16());
        }

        protected override void BuildString(StringBuilder sb)
        {
            base.BuildString(sb);
            sb.AppendLine($"{nameof(GameServerId)}: 0x{GameServerId:X}");
            sb.AppendLine($"{nameof(Field1)}: 0x{Field1:X}");
            sb.AppendLine($"{nameof(LevelRangeMin)}: {LevelRangeMin}");
            sb.AppendLine($"{nameof(LevelRangeMax)}: {LevelRangeMax}");
            sb.AppendLine($"{nameof(Flags)}: 0x{Flags:X}");
            sb.AppendLine($"{nameof(MaxPlayers)}: {MaxPlayers}");
            //sb.AppendLine($"{nameof(NewGamePlusLevel)}: {NewGamePlusLevel}");
            sb.AppendLine($"{nameof(Name)}: {Name}");
            sb.AppendLine($"{nameof(Description)}: {Description}");
            sb.AppendLine($"{nameof(Blob)}: {Blob.ToHexString()}");
        }
    }
}
