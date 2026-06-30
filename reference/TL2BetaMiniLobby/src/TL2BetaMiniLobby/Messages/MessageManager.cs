using System.Reflection;

namespace TL2BetaMiniLobby.Messages
{
    public static class MessageManager
    {
        private static readonly Dictionary<LobbyOpcode, Type> _opcodeToTypeDict = new();
        private static readonly Dictionary<Type, LobbyOpcode> _typeToOpcodeDict = new();

        static MessageManager()
        {
            // Get types for all defined messages
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.IsSubclassOf(typeof(LobbyMessage)) == false) continue;

                var attribute = type.GetCustomAttribute<LobbyMessageAttribute>();
                _opcodeToTypeDict.Add(attribute.Opcode, type);
                _typeToOpcodeDict.Add(type, attribute.Opcode);
            }
        }

        /// <summary>
        /// Parses a lobby message using definitions if possible.
        /// </summary>
        public static LobbyMessage ParseMessage(LobbyOpcode opcode, byte[] rawData)
        {
            // See if we have a definition for this opcode
            if (_opcodeToTypeDict.TryGetValue(opcode, out Type type))
                return (LobbyMessage)Activator.CreateInstance(type, new object[] { rawData });

            // Default to generic lobby message if not
            return new(rawData);
        }

        /// <summary>
        /// Tries to get opcode of a defined message.
        /// </summary>
        public static bool TryGetMessageOpcode(LobbyMessage message, out LobbyOpcode opcode)
        {
            return _typeToOpcodeDict.TryGetValue(message.GetType(), out opcode);
        }
    }
}
