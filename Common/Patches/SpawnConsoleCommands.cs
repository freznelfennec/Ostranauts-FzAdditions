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

        //A UI dump, for building the mod's own UI in the game's style. Registered with the spawn commands, as both are
        //Common's debugging tools.
        public static void RegisterUIDump()
        {
            ConsolePatch.AddCommand("dumpui", KeywordDumpUI,
                "dumps a piece of the game's UI, with its sprites, to BepInEx/FzUIDump, to build matching UI from",
                "'fz dumpui' or 'fz dumpui pda' dumps the PDA, whether or not it's open",
                "'fz dumpui pda <app>' dumps one app's screen: home, goals, tasks, gigs, bounties, ferry, orders, socials, standings, viz, notes, timer or hotbar",
                "'fz dumpui <name>' dumps the first object in the scene with that name, hidden ones included",
                "Each dump has hierarchy.txt (every object with its rect, components, images, text and buttons), sprites.tsv (each sprite's texture, rect and slicing) and the sprites as PNGs.");
        }

        //PDA apps that draw inside the PDA, by their pda_apps.json name, to GUIPDA's field for each one's screen
        private static readonly Dictionary<string, string> PdaApps = new Dictionary<string, string>
        {
            ["home"] = "cgHome",
            ["goals"] = "cgObjectives",
            ["tasks"] = "cgTasks",
            ["gigs"] = "cgGigNexus",
            ["bounties"] = "cgBounties",
            ["ferry"] = "cgFerry",
            ["orders"] = "cgJobs",
            ["socials"] = "cgSocials",
            ["standings"] = "cgStandings",
            ["viz"] = "cgViz",
            ["notes"] = "cgNotes",
            ["timer"] = "cgTimer",
            ["hotbar"] = "cgHotbar"
        };

        private static bool KeywordDumpUI(ref string strInput, string[] args)
        {
            string target = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "pda";
            UnityEngine.GameObject root = null;
            if (args.Length > 2 && args[1].ToLower() == "pda")
            {
                //One app inside the PDA, by its name in pda_apps.json, through the PDA's own field for its screen
                string app = args[2].ToLower();
                if (!PdaApps.TryGetValue(app, out string field))
                {
                    strInput += $"\nNo app screen inside the PDA called {app}. Apps with their own screen: {string.Join(", ", PdaApps.Keys)}. The others (zones, inventory, files, navmap, power, roster, duties, vote) open the game's other windows.";
                    return false;
                }
                if (GUIPDA.instance == null)
                {
                    strInput += "\nNo PDA in this scene. Load a game first.";
                    return false;
                }
                var group = HarmonyLib.AccessTools.Field(typeof(GUIPDA), field)?.GetValue(GUIPDA.instance) as UnityEngine.Component;
                root = group != null ? group.gameObject : null;
                target = "pda_" + app;
                if (root == null)
                {
                    strInput += $"\nThe PDA has no {app} screen in this version of the game.";
                    return false;
                }
            }
            else if (target.ToLower() == "pda") root = GUIPDA.instance != null ? GUIPDA.instance.gameObject : null;
            else root = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>()
                .FirstOrDefault(g => g.name == target && g.scene.IsValid());
            if (root == null)
            {
                strInput += target.ToLower() == "pda" ? "\nNo PDA in this scene. Load a game first." : $"\nNo object named {target} in the scene.";
                return false;
            }
            string folder = UIDump.Dump(root, target, out int objects, out int sprites);
            strInput += $"\nDumped {objects} objects and {sprites} sprites from {root.name} to {folder}";
            return true;
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
