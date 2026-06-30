namespace TL2BetaMiniLobby.Messages
{
    [AttributeUsage(AttributeTargets.Class)]
    public class LobbyMessageAttribute : Attribute
    {
        public LobbyOpcode Opcode { get; }

        public LobbyMessageAttribute(LobbyOpcode opcode)
        {
            Opcode = opcode;
        }
    }
}
