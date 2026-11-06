using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Patches
{
    //Hands a loaded ship's wear to ShipWear to spread over several frames. A ship that isn't loaded only adds to a
    //running total, which is cheap, so the game's own method does that as before.
    [HarmonyPatch]
    public static class ShipWearPatch
    {
        private const double WearInterval = 300.0; //Game seconds, the game's, Ship.DamageOverTime

        //Runs for every ship every frame. The same timing as the game's, only the breaking in moves.
        [HarmonyPatch(typeof(Ship), nameof(Ship.DamageOverTime))]
        [HarmonyPrefix()]
        public static bool DamageOverTime_Prefix(Ship __instance)
        {
            if (__instance.fLastWearEpoch == 0.0 || __instance.LoadState < Ship.Loaded.Edit) return true;

            double seconds = StarSystem.fEpoch - __instance.fLastWearEpoch;
            if (seconds < WearInterval) return false;

            try
            {
                ShipWear.Queue(__instance, seconds);
                __instance.fLastWearEpoch = StarSystem.fEpoch;
                return false;
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Ship wear could not be spread, the game applies it: " + ex);
                return true;
            }
        }

        //Before every load and new game, and when leaving to the menu
        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.EmptyScene))]
        [HarmonyPrefix()]
        public static void EmptyScene_Prefix()
        {
            ShipWear.Reset();
        }

        [HarmonyPatch(typeof(LoadManager), "SaveGameData")]
        [HarmonyPrefix()]
        public static void SaveGameData_Prefix()
        {
            try
            {
                ShipWear.FinishAll();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Ship wear could not be finished before saving: " + ex);
            }
        }
    }
}
