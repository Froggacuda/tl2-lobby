// tl2-lobby — community Torchlight 2 replacement lobby server.
// Copyright (C) 2026 Froggacuda. Licensed under AGPL-3.0 (see LICENSE / NOTICE).
// Derived from TL2BetaMiniLobby (c) 2023 Crypto137, MIT-licensed
// (https://github.com/Crypto137/TL2BetaMiniLobby) — see THIRD-PARTY-NOTICES.txt.

using TL2BetaMiniLobby.Messages.Definitions;

namespace TL2BetaMiniLobby.Messages
{
    public static class MessageHandler
    {
        /// <summary>
        /// Handles a message received from a client.
        /// </summary>
        public static bool HandleMessage(LobbyClient client, LobbyMessage message)
        {
            switch (message)
            {
                case NetConnectMsg netConnectMsg:                   OnNetConnectMsg(client, netConnectMsg); break;
                case NetDisconnectMsg netDisconnectMsg:             OnNetDisconnectMsg(client, netDisconnectMsg); break;
                case LobbyStartLoginMsg startLoginMsg:              OnStartLoginMsg(client, startLoginMsg); break;
                case LobbyLoginResponseMsg loginResponseMsg:        OnLoginResponseMsg(client, loginResponseMsg); break;
                case LobbyCreateGameServerMsg createGameServerMsg:  OnCreateGameServerMsg(client, createGameServerMsg); break;
                case LobbyKeepaliveMsg keepaliveMsg:                OnKeepaliveMsg(client, keepaliveMsg); break;

                default: return false;
            }

            return true;
        }

        private static void OnNetConnectMsg(LobbyClient client, NetConnectMsg message)
        {
            // Assign a SESSION-RANDOM, opaque addrToken (ClientKey in op1) — matching the real
            // lobby, which never hands the client its own IP. Suspected fix for the login-phase
            // "client 100: network name is invalid" spam: the client may format the token into a
            // facilitator/self address string, and an IP-valued token breaks it. The token is an
            // opaque key (op32/op33, op22, op53, op37); peers echo it back, so any unique value works.
            uint token = unchecked((uint)System.Random.Shared.Next(1, int.MaxValue));
            client.AddrToken = token;
            client.Send(new NetConnectOkMsg() { Field1 = 0x1, Field2 = 0x1, ClientKey = token });
        }

        private static void OnNetDisconnectMsg(LobbyClient client, NetDisconnectMsg message)
        {
            client.Disconnect();
        }

        private static void OnStartLoginMsg(LobbyClient client, LobbyStartLoginMsg message)
        {
            client.Username = message.Username;
            // Save Field2+Field3 (op8 bytes 10-15) as the session LoginKey.
            // The real lobby puts the OTHER peer's LoginKey into op22's 12B prefix.
            byte[] f2 = BitConverter.GetBytes(message.Field2);
            byte[] f3 = BitConverter.GetBytes(message.Field3);
            client.LoginKey = new byte[6];
            Array.Copy(f2, 0, client.LoginKey, 0, 4);
            Array.Copy(f3, 0, client.LoginKey, 4, 2);
            client.Send(new LobbyLoginChallengeMsg());
        }

        private static void OnLoginResponseMsg(LobbyClient client, LobbyLoginResponseMsg message)
        {
            Logger.Log($"{client.Username} logged in");
            // op11 Field2 = 0xC511 → writes LE as bytes [0x11,0xC5] = port 4549 (big-endian), matching
            // the real lobby's op11 (000000000011c500) byte-for-byte across all captures. STRONG suspect
            // for the login-phase "client 100: network name is invalid" spam: we were sending Field2=0,
            // giving the client a port-0 facilitator address. 0x11C5 = 4549 = the lobby/facilitator port.
            client.Send(new LobbyLoginResultMsg() { Field2 = 0xC511 });
        }

        private static void OnCreateGameServerMsg(LobbyClient client, LobbyCreateGameServerMsg message)
        {
            // Register the game (vanilla create path). Modded creates arrive as raw opcode 51
            // and are registered in LobbyClient.RespondRaw; both share GameServer.TryParseCreate.
            if (GameServer.TryParseCreate(client, message.RawData, isModded: false, out GameServer game))
                client.Server.Games.Add(game);
            else
                Logger.Log($"{client.Username} created a game server: {message.Name} (id 0x{message.GameServerId:X})");

            client.Send(new LobbyCreateGameResponseMsg() { Response = CreateGameResponse.Success });
        }

        private static void OnKeepaliveMsg(LobbyClient client, LobbyKeepaliveMsg message)
        {
            client.Send(new LobbyKeepaliveMsg());
        }
    }
}
