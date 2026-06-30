using TL2BetaMiniLobby.Messages;

namespace TL2BetaMiniLobby
{
    public class Packet
    {
        public LobbyOpcode Opcode { get; }
        public LobbyMessage Message { get; }

        public Packet(byte[] buffer)
        {
            using (MemoryStream stream = new(buffer))
            using (BinaryReader reader = new(stream))
            {
                Opcode = (LobbyOpcode)reader.ReadByte();

                // Read payload and parse message
                ushort payloadSize = reader.ReadUInt16BigEndian();
                byte[] payload = reader.ReadBytes(payloadSize);
                Message = MessageManager.ParseMessage(Opcode, payload);
            }
        }

        public Packet(LobbyMessage message)
        {
            if (MessageManager.TryGetMessageOpcode(message, out LobbyOpcode opcode) == false)
                throw new("Attempted to create a packet for an undefined message.");

            Opcode = opcode;
            Message = message;
        }

        public byte[] Serialize()
        {
            Message.Serialize();
            if (Message.RawData.Length > ushort.MaxValue)
                throw new Exception($"Message size exceeded {ushort.MaxValue} bytes.");

            using (MemoryStream stream = new())
            using (BinaryWriter writer = new(stream))
            {
                writer.Write((byte)Opcode);
                writer.WriteUInt16BigEndian((ushort)Message.RawData.Length);
                writer.Write(Message.RawData);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
