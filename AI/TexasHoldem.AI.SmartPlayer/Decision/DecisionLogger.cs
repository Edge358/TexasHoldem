namespace TexasHoldem.AI.SmartPlayer.Decision
{
    using System;
    using System.IO;

    public static class DecisionLogger
    {
        private const string LogFileName = "ai_decisions.log";
        public static bool Enabled { get; set; }

        public static void Log(string line)
        {
            try
            {
                if (!Enabled)
                {
                    return;
                }

                File.AppendAllText(LogFileName, line + Environment.NewLine);
            }
            catch
            {
                // Swallow all exceptions to avoid impacting gameplay.
            }
        }
    }
}
