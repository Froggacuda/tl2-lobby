using System.Collections.Concurrent;
using System.Net;
using Mono.Nat;

namespace TL2BetaMiniLobby
{
    /// <summary>
    /// Best-effort automatic router port-forwarding (UPnP-IGD + NAT-PMP, via Mono.Nat) for the
    /// lobby's own listen port. This replaces the manual "port-forward 4549 on your router" step in
    /// the README for operators whose router speaks UPnP/NAT-PMP (most consumer routers, often on by
    /// default). It is PURELY a setup convenience: it opens the lobby's TCP+UDP <see cref="Port"/> so
    /// remote friends can reach it, and learns the router's external IP as a side effect.
    ///
    /// SCOPE / what this does NOT do: it runs only where the lobby runs (the operator's box), so it
    /// cannot open a remote joining-player's game-host NAT — that is still the (parked) relay's job.
    ///
    /// SAFETY: entirely best-effort and OFF the critical path. If no IGD answers, mapping fails, or
    /// the router lies, we log and move on — the proven direct-punch flow is untouched. Disable
    /// outright with TL2_UPNP_OFF=1.
    /// </summary>
    public static class NatForward
    {
        /// <summary>Disable the whole feature with TL2_UPNP_OFF=1 (escape hatch for routers that
        /// misbehave or operators who forward manually).</summary>
        public static bool Enabled => Environment.GetEnvironmentVariable("TL2_UPNP_OFF") != "1";

        /// <summary>The external (public) IP reported by the router's IGD, once discovered. Null until
        /// a device answers. A later step can use this to auto-fill TL2_RELAY_IP.</summary>
        public static IPAddress ExternalIP { get; private set; }

        // Every (device, mapping) we create, so we can tear them down cleanly on shutdown. Discovery
        // can surface the same gateway on multiple interfaces, hence a collection keyed per mapping.
        private static readonly ConcurrentBag<(INatDevice Device, Mapping Mapping)> _active = new();
        // DeviceFound can fire several times for the same router (one event per interface). Guard so we
        // map each protocol exactly once and log the external IP once — otherwise the redundant attempts
        // collide with our own fresh mapping and surface an alarming (but harmless) conflict.
        private static readonly HashSet<Protocol> _mapped = new();
        private static readonly object _lock = new();
        private static bool _loggedExternal;
        private static int _port;
        private static bool _started;

        /// <summary>Begin UPnP/NAT-PMP discovery and, on the first device found, map TCP+UDP
        /// <paramref name="port"/>. Returns immediately; mapping happens asynchronously as devices
        /// reply (a router may take a few seconds). Safe to call once at startup.</summary>
        public static void Start(int port)
        {
            if (!Enabled)
            {
                Logger.Log("UPnP: disabled (TL2_UPNP_OFF=1); operator must port-forward manually.");
                return;
            }
            if (_started) return;
            _started = true;
            _port = port;

            try
            {
                NatUtility.DeviceFound += OnDeviceFound;
                NatUtility.StartDiscovery();
                Logger.Log($"UPnP: discovering routers to auto-forward {port}/tcp+udp "
                         + "(best-effort; set TL2_UPNP_OFF=1 to skip)...");
            }
            catch (Exception ex)
            {
                Logger.Log($"UPnP: discovery failed to start ({ex.Message}); continuing without it.");
            }
        }

        private static async void OnDeviceFound(object sender, DeviceEventArgs e)
        {
            INatDevice device = e.Device;

            // External IP first — useful even if a mapping later fails, and it confirms the IGD is real.
            try
            {
                IPAddress ip = await device.GetExternalIPAsync();
                ExternalIP = ip;
                lock (_lock)
                {
                    if (!_loggedExternal) { _loggedExternal = true; Logger.Log($"UPnP: router found, external IP {ip}."); }
                }
                // Let a self-hosting operator skip setting TL2_RELAY_IP by hand (env still wins; see #5).
                LobbyServer.AdoptDetectedPublicIP(ip);
            }
            catch (Exception ex)
            {
                Logger.Log($"UPnP: router found but external IP query failed ({ex.Message}).");
            }

            await MapAsync(device, Protocol.Tcp);
            await MapAsync(device, Protocol.Udp);
        }

        private static async Task MapAsync(INatDevice device, Protocol proto)
        {
            // DeviceFound fires once per interface; only the first attempt per protocol should run, else
            // the duplicate collides with the mapping we just made (ConflictInMappingEntry).
            lock (_lock)
            {
                if (!_mapped.Add(proto)) return;
            }

            try
            {
                // Same private+public port; a long lifetime (0 = permanent where supported) so it survives
                // the session, and we still actively remove it on clean shutdown.
                Mapping mapping = new(proto, _port, _port, 0, "TL2 Community Lobby");
                await device.CreatePortMapAsync(mapping);
                _active.Add((device, mapping));
                Logger.Log($"UPnP: mapped {_port}/{proto.ToString().ToLower()} -> this machine.");
            }
            catch (MappingException ex) when (ex.ErrorCode == ErrorCode.ConflictInMappingEntry)
            {   // The port is already forwarded by something we didn't create — a manual router rule, or a
                // co-located service (e.g. an already-running lobby). The forward we need exists, so we're
                // done; but we must NOT register it for deletion, or our shutdown would tear down a rule we
                // don't own. Leave it exactly as found.
                Logger.Log($"UPnP: {_port}/{proto.ToString().ToLower()} already forwarded by another rule — leaving it untouched.");
            }
            catch (Exception ex)
            {
                lock (_lock) { _mapped.Remove(proto); }   // let a later device retry this protocol
                Logger.Log($"UPnP: mapping {_port}/{proto.ToString().ToLower()} on {SafeName(device)} failed "
                         + $"({ex.Message}); remote friends may need a manual port-forward.");
            }
        }

        /// <summary>Remove every mapping we created. Best-effort and quick — never blocks shutdown for
        /// long if the router has gone away.</summary>
        public static void Stop()
        {
            if (!_started) return;
            try { NatUtility.DeviceFound -= OnDeviceFound; } catch { /* ignore */ }
            try { NatUtility.StopDiscovery(); } catch { /* ignore */ }

            foreach ((INatDevice device, Mapping mapping) in _active)
            {
                try
                {
                    device.DeletePortMapAsync(mapping).Wait(TimeSpan.FromSeconds(2));
                    Logger.Log($"UPnP: removed mapping {mapping.PublicPort}/{mapping.Protocol.ToString().ToLower()}.");
                }
                catch { /* router gone or already cleared; nothing to do */ }
            }
        }

        private static string SafeName(INatDevice device)
        {
            try { return device.DeviceEndpoint?.ToString() ?? "router"; }
            catch { return "router"; }
        }
    }
}
