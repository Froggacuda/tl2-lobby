using System.Diagnostics;

namespace TL2BetaMiniLobby
{
    public static class Logger
    {
        private const string TimeFormat = "yyyy.MM.dd HH:mm:ss.fff";

        private static readonly object Lock = new();

        private static readonly DateTime LogTimeBase;
        private static readonly Stopwatch LogTimeStopwatch;

        static Logger()
        {
            LogTimeBase = DateTime.Now;
            LogTimeStopwatch = Stopwatch.StartNew();
        }

        public static void Log(string message)
        {
            DateTime time = LogTimeNow();

            lock (Lock)
                Console.WriteLine($"[{time.ToString(TimeFormat)}] {message}");
        }

        private static DateTime LogTimeNow()
        {
            return LogTimeBase.Add(LogTimeStopwatch.Elapsed);
        }
    }
}
