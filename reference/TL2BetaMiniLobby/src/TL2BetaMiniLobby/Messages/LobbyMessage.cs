using System.Text;

namespace TL2BetaMiniLobby.Messages
{
    public class LobbyMessage
    {
        public byte[] RawData { get; private set; } = Array.Empty<byte>();

        public LobbyMessage() { }

        public LobbyMessage(byte[] rawData)
        {
            RawData = rawData;

            using (MemoryStream stream = new(rawData))
            using (BinaryReader reader = new(stream))
            {
                Parse(reader);
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            BuildString(sb);
            return sb.ToString();
        }

        public virtual void Parse(BinaryReader reader) { }

        public virtual void Encode(BinaryWriter writer) => writer.Write(RawData);

        /// <summary>
        /// Serializes this message and updates the RawData property.
        /// </summary>
        public byte[] Serialize()
        {
            using (MemoryStream stream = new())
            using (BinaryWriter writer = new(stream))
            {
                Encode(writer);
                writer.Flush();
                RawData = stream.ToArray();
                return RawData;
            }
        }

        protected virtual void BuildString(StringBuilder sb)
        {
            sb.AppendLine($"Raw Data: {RawData.ToHexString()}");
        }
    }
}
