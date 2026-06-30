using System.Reflection;

namespace TL2BetaMiniLobby
{
    public class Program
    {
        /// <summary>Debug mode (-dump / -debug). Enables the per-message hex firehose, the
        /// BLOBDUMP/OUTDUMP RE traces, and honoring the debug-only env toggles
        /// (TL2_BREAK_PUNCH). OFF for a normal release run — keeps server.log readable and
        /// makes it impossible to accidentally sabotage connections in the field.</summary>
        public static bool MessageDumpMode { get; private set; } = false;

        /// <summary>
        /// The op53 ModGameServersList reply (the in-client game browser). ON by default —
        /// proven working on a real WAN join 2026-06-29. Opt out with "-no-op53" or
        /// TL2_OP53_OFF=1 (kept as an escape hatch only).
        /// </summary>
        public static bool Op53Enabled { get; private set; } =
            Environment.GetEnvironmentVariable("TL2_OP53_OFF") != "1";

        /// <summary>Seed a synthetic game so a single browsing client receives an op53 entry (testing).</summary>
        public static bool SeedPhantomGame { get; private set; } = false;

        public static LobbyServer LobbyServer { get; private set; }

        static void Main(string[] args)
        {
            Logger.Log($"TL2BetaMiniLobby {Assembly.GetExecutingAssembly().GetName().Version} starting...");

            foreach (string arg in args)
            {
                if (arg.ToLower() == "-dump" || arg.ToLower() == "-debug")
                {
                    MessageDumpMode = true;
                    Console.WriteLine("Debug mode enabled (verbose logging + debug toggles honored)");
                }
                else if (arg.ToLower() == "-op53")
                {
                    Op53Enabled = true;    // back-compat no-op (op53 is on by default now)
                }
                else if (arg.ToLower() == "-no-op53")
                {
                    Op53Enabled = false;
                }
                else if (arg.ToLower() == "-seedgame")
                {
                    SeedPhantomGame = true;
                }
            }

            // Create and start the lobby server
            LobbyServer = new();
            LobbyServer.Start();

            if (SeedPhantomGame)
                LobbyServer.Games.Add(GameServer.CreatePhantom("PHANTOM TEST", "op53 test entry"));

            // Process input
            while (true)
            {
                string input = Console.ReadLine();
                if (input == null)
                {
                    // No interactive console (headless/background service mode):
                    // keep the process alive so the listener task keeps running.
                    Logger.Log("No console input available; running headless. Listener stays up.");
                    Thread.Sleep(Timeout.Infinite);
                }
                switch (input.ToLower())
                {
                    case "commands":
                        Console.WriteLine("Available commands: status, stop");
                        break;

                    case "status":
                        Console.WriteLine(LobbyServer.GetStatus());
                        break;

                    case "stop":
                        LobbyServer.Shutdown();
                        Console.WriteLine("Server shut down. Press any key to exit");
                        Console.ReadKey();
                        return;

                    default:
                        Console.WriteLine($"Invalid command '{input}'. Type 'commands' for a list of available commands.");
                        break;
                }
            }
        }
    }
}
