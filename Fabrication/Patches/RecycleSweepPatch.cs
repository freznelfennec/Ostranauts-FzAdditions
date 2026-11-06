using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Patches
{
    //Clears items left locked for recycling with no recycler working on them (RecyclerProcess.SweepOrphans), and items
    //left locked or unfinished by a fabricator (FabricatorProcess.SweepOrphans), at the two
    //moments it's worth a look through a ship: before the game saves, so no save keeps one, and as a ship fully loads,
    //which also catches any in a save made before this check.
    [HarmonyPatch]
    public static class RecycleSweepPatch
    {
        [HarmonyPatch(typeof(LoadManager), "SaveGameData")]
        [HarmonyPrefix()]
        public static void SaveGameData_Prefix()
        {
            try
            {
                IEnumerable<Ship> ships = CrewSim.system?.GetAllLoadedShips() ?? Enumerable.Empty<Ship>();
                foreach (Ship ship in ships.ToList())
                {
                    RecyclerProcess.SweepOrphans(ship);
                    FabricatorProcess.SweepOrphans(ship);
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Clearing orphaned recycler locks before saving failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(Ship), "PostGameLoad")]
        [HarmonyPostfix()]
        public static void PostGameLoad_Postfix(Ship __instance, Ship.Loaded nLoad)
        {
            if (nLoad != Ship.Loaded.Full) return;
            try
            {
                RecyclerProcess.SweepOrphans(__instance);
                FabricatorProcess.SweepOrphans(__instance);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Clearing orphaned recycler locks on {__instance?.strRegID} failed: {ex}");
            }
        }
    }
}
