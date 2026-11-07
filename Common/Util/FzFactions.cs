using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //Factions the mod adds, such as Faure-Zhang, read from every mod's data/fz/factions folder in load order.
    //
    //The game only reads factions from star_system.json for a new game, and a save keeps its own list, so a faction
    //can't be added with data alone. Any defined here that the running game lacks is added once after each load (the
    //first time the game runs) and whenever something is about to use one (Ensure). From then on the game saves it with
    //the rest. Factions don't change as data, so the definitions are read once and kept.
    public static class FzFactions
    {
        public const string Folder = "fz/factions/";
        public const string FaureZhang = "FzFaureZhang";

        private static List<JsonFaction> defs;
        private static bool pending = true;

        public static IReadOnlyList<JsonFaction> Defs
        {
            get
            {
                if (defs != null) return defs;
                var byName = new Dictionary<string, JsonFaction>();
                foreach (string modPath in ModData.ModPathsInLoadOrder())
                {
                    foreach (string file in ModData.Files(modPath + "data/" + Folder))
                    {
                        foreach (JsonFaction faction in ModData.Read<JsonFaction>(file))
                        {
                            if (!string.IsNullOrEmpty(faction?.strName)) byName[faction.strName] = faction;
                        }
                    }
                }
                defs = byName.Values.ToList();
                return defs;
            }
        }

        //Adds every defined faction the running game lacks. Returns how many were added.
        public static int Ensure()
        {
            if (CrewSim.system == null) return 0;
            int added = 0;
            foreach (JsonFaction def in Defs)
            {
                if (CrewSim.system.GetFaction(def.strName) != null) continue;
                JsonFaction faction = def.Clone();
                if (faction.dictFactionRels == null) faction.dictFactionRels = new Dictionary<string, float>();
                if (faction.aMembers == null) faction.aMembers = new List<string>();
                faction.aReciprocals = new List<string>(faction.dictFactionRels.Keys);
                CrewSim.system.AddFaction(faction);
                added++;
                FzAdditions.Logger.LogInfo($"Added faction {faction.strName} ({faction.strNameFriendly}), which this game didn't have");
            }
            return added;
        }

        //After a load, the first time the game runs
        public static void Tick()
        {
            if (!pending || CrewSim.system == null) return;
            pending = false;
            try
            {
                Ensure();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Adding the mod's factions failed: " + ex);
            }
        }

        //Runs on EmptyScene, so the next world loaded is checked
        public static void Reset()
        {
            pending = true;
        }
    }
}
