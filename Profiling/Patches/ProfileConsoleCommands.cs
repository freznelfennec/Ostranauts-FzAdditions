using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Patches
{
    //Runs the profiler from the console. Nothing is patched until 'fz profile start'.
    public static class ProfileConsoleCommands
    {
        private const int DefaultRows = 15;

        public static void Register()
        {
            ConsolePatch.AddCommand("profile", KeywordProfile,
                "times the game's per-frame methods and simulation steps",
                "'fz profile start' patches them and starts timing, 'fz profile stop' removes the patches and reports",
                "'fz profile report [rows]' reports so far without stopping, 'fz profile reset' clears what has been timed",
                "the full report is written to BepInEx/FzProfile.txt");
        }

        private static bool KeywordProfile(ref string strInput, string[] args)
        {
            string action = args.Length > 1 ? args[1].ToLower() : "";
            switch (action)
            {
                case "start":
                    if (FzProfiler.Running)
                    {
                        strInput += "\nThe profiler is already running.";
                        return false;
                    }
                    int patched = FzProfiler.Start(out List<string> failed);
                    strInput += $"\nProfiler timing {patched} methods.";
                    if (failed.Count > 0) strInput += $" {failed.Count} could not be patched, they are listed in the log.";
                    foreach (string line in failed) FzAdditions.Logger.LogWarning("Profiler could not patch " + line);
                    return true;

                case "stop":
                    if (!FzProfiler.Running)
                    {
                        strInput += "\nThe profiler isn't running.";
                        return false;
                    }
                    FzProfiler.Stop();
                    strInput += "\nProfiler stopped, its patches are removed.";
                    AppendReport(ref strInput, DefaultRows);
                    return true;

                case "report":
                    int rows = args.Length > 2 && int.TryParse(args[2], out int parsed) && parsed > 0 ? parsed : DefaultRows;
                    AppendReport(ref strInput, rows);
                    return true;

                case "reset":
                    FzProfiler.Reset();
                    strInput += "\nProfile cleared.";
                    return true;

                default:
                    strInput += "\nUsage: fz profile start, fz profile stop, fz profile report [rows] or fz profile reset";
                    return false;
            }
        }

        private static void AppendReport(ref string strInput, int rows)
        {
            string report = FzProfiler.Report(rows, out string path);
            strInput += "\n" + report;
            strInput += path != null ? "\nFull report written to " + path : "\nThe full report could not be written, see the log.";
            FzAdditions.Logger.LogInfo("Profile:\n" + report);
        }
    }
}
