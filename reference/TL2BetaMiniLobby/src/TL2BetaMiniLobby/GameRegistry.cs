// tl2-lobby — community Torchlight 2 replacement lobby server.
// Copyright (C) 2026 Froggacuda. Licensed under AGPL-3.0 (see LICENSE / NOTICE).
// Derived from TL2BetaMiniLobby (c) 2023 Crypto137, MIT-licensed
// (https://github.com/Crypto137/TL2BetaMiniLobby) — see THIRD-PARTY-NOTICES.txt.

namespace TL2BetaMiniLobby
{
    /// <summary>
    /// Thread-safe in-memory store of the games currently hosted in the lobby. This is the
    /// state the game browser (GetModGameServers -> ModGameServersList) and join brokering
    /// (RequestConnect -> AttemptConnect) will read from once those handlers land.
    /// </summary>
    public class GameRegistry
    {
        private readonly object _lock = new();
        private readonly Dictionary<ulong, GameServer> _games = new();

        /// <summary>Registers (or replaces) a hosted game, keyed by its GameServerId.</summary>
        public void Add(GameServer game)
        {
            lock (_lock)
                _games[game.Id] = game;

            Logger.Log($"Game registered: {game} ({CountUnsafe()} game(s) online)");
        }

        /// <summary>Refreshes an existing game from an UpdateGameServer message. No-op if unknown.</summary>
        public void Update(ulong id, byte[] payload)
        {
            lock (_lock)
            {
                if (_games.TryGetValue(id, out GameServer game))
                    game.ApplyUpdate(payload);
            }
        }

        /// <summary>Removes a game by id (e.g. on RemoveGameServer). Returns true if one was removed.</summary>
        public bool Remove(ulong id)
        {
            lock (_lock)
            {
                if (_games.Remove(id, out GameServer game))
                {
                    Logger.Log($"Game removed: {game} ({CountUnsafe()} game(s) online)");
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes every game hosted by the given client (called on disconnect).</summary>
        public void RemoveByHost(LobbyClient host)
        {
            lock (_lock)
            {
                var orphaned = _games.Where(kvp => kvp.Value.Host == host).Select(kvp => kvp.Key).ToList();
                foreach (ulong id in orphaned)
                {
                    if (_games.Remove(id, out GameServer game))
                        Logger.Log($"Game evicted (host left): {game} ({CountUnsafe()} game(s) online)");
                }
            }
        }

        /// <summary>Snapshot of all hosted games for listing.</summary>
        public IReadOnlyList<GameServer> GetAll()
        {
            lock (_lock)
                return _games.Values.ToList();
        }

        public int Count
        {
            get { lock (_lock) return _games.Count; }
        }

        // Caller already holds _lock.
        private int CountUnsafe() => _games.Count;
    }
}
