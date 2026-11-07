using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.Core;
using System;

namespace Freznel.FzAdditions.Patches
{
    //Sweeps away orphaned spawns (FzSpawns) once after each load, as the game first runs, and before every save
    [HarmonyPatch]
    public static class FzSpawnsPatch
    {
        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.EmptyScene))]
        [HarmonyPrefix()]
        public static void EmptyScene_Prefix()
        {
            FzSpawns.Reset();
        }

        //The game only calls this while unpaused, with the system loaded
        [HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.Update))]
        [HarmonyPostfix()]
        public static void Update_Postfix()
        {
            try
            {
                FzSpawns.Tick();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Sweeping orphaned spawns after loading failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(LoadManager), "SaveGameData")]
        [HarmonyPrefix()]
        public static void SaveGameData_Prefix()
        {
            try
            {
                FzSpawns.Sweep();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Sweeping orphaned spawns before saving failed: " + ex);
            }
        }
    }
}
