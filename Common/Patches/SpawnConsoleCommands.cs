using Freznel.FzAdditions.Util;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Patches
{
    //Console commands for what the mod has spawned into the system (FzSpawns)
    public static class SpawnConsoleCommands
    {
        public static void Register()
        {
            ConsolePatch.AddCommand("spawns", KeywordSpawns,
                "lists the stations and ships the mod has spawned, and clears away orphans",
                "'fz spawns' lists each spawn with its kind and whether it's an orphan",
                "'fz spawns sweep' removes orphans now, as happens after each load and before each save",
                "An orphan is a spawn nothing needs: one whose kind says it's done, one that failed partway, or a station whose orbit is gone.");
        }

        private static bool KeywordSpawns(ref string strInput, string[] args)
        {
            string sub = args.Length > 1 ? args[1].ToLower() : "list";
            if (sub == "sweep")
            {
                List<string> report = FzSpawns.Sweep();
                strInput += report.Count == 0 ? "\nNo orphans." : "\n" + string.Join("\n", report);
                return true;
            }
            if (sub != "list")
            {
                strInput += "\nUsage: fz spawns, fz spawns sweep";
                return false;
            }

            var spawns = CrewSim.system?.dictShips?.Values.Where(FzSpawns.IsSpawn).OrderBy(s => s.strRegID).ToList() ?? new List<Ship>();
            if (spawns.Count == 0)
            {
                strInput += "\nThe mod has nothing spawned. Kinds: " + string.Join(", ", FzSpawns.KindNames);
                return true;
            }
            foreach (Ship ship in spawns)
            {
                string reason = FzSpawns.OrphanReason(ship);
                string state = reason == null ? "wanted" : "ORPHAN: " + reason;
                if (FzSpawns.IsInUse(ship)) state += ", in use";
                string name = string.IsNullOrEmpty(ship.publicName) ? "" : $" ({ship.publicName})";
                strInput += $"\n{ship.strRegID}{name}: {FzSpawns.KindOf(ship) ?? "unknown kind"}, {state}";
            }
            return true;
        }
    }
}
