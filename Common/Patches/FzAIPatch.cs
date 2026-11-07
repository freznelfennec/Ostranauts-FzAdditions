using Freznel.FzAdditions.Util.AI;
using HarmonyLib;
using System;

namespace Freznel.FzAdditions.Patches
{
    //Runs the mod's AI (FzAI) beside the game's, and clears it with the world
    [HarmonyPatch]
    public static class FzAIPatch
    {
        //The game only calls this while unpaused and while game time moves
        [HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.Update))]
        [HarmonyPostfix()]
        public static void Update_Postfix()
        {
            try
            {
                FzAI.Tick();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("FzAI update failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.EmptyScene))]
        [HarmonyPrefix()]
        public static void EmptyScene_Prefix()
        {
            try
            {
                FzAI.Reset();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("FzAI reset failed: " + ex);
            }
        }

        //A flown ship loading takes out rounds it spent while shallow, so they can't come back as live ammo
        [HarmonyPatch(typeof(Ship), "PostGameLoad")]
        [HarmonyPostfix()]
        public static void PostGameLoad_Postfix(Ship __instance, Ship.Loaded nLoad)
        {
            if (nLoad <= Ship.Loaded.Shallow || FzAI.TypeOf(__instance) == null) return;
            try
            {
                int cleared = FzWeapons.ClearSpentRounds(__instance);
                if (cleared > 0) FzAdditions.Logger.LogInfo($"FzAI took {cleared} spent rounds out of {__instance.strRegID}'s weapons as it loaded.");
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Clearing spent rounds on {__instance?.strRegID} failed: {ex}");
            }
        }
    }
}
