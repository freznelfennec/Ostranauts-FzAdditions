using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace Freznel.FzAdditions.Patches
{
    //Per the user, a generated site's weapons, sensors, power and life support can't be uninstalled or dismantled until
    //its Equipment Lockout Controller is hacked for their kind (SiteLockout). Every uninstall and dismantle in the game
    //is an interaction the game builds from an installable, named ACT<installable> (and its ACT<installable>Allow), so
    //those fail their participant check on locked equipment, the way recycling locks work (RecycleLockPatch).
    [HarmonyPatch]
    public static class SiteLockoutPatch
    {
        private static HashSet<string> removals;

        //The interaction names of every uninstall and dismantle, built once the game's installables are loaded
        private static HashSet<string> Removals()
        {
            if (removals != null) return removals;
            var names = new HashSet<string>();
            foreach (var dict in new[] { DataHandler.dictInstallables, DataHandler.dictInstallables2 })
            {
                if (dict == null) continue;
                foreach (JsonInstallable ji in dict.Values)
                {
                    if (ji?.strName == null) continue;
                    if (ji.strJobType != "uninstall" && ji.strJobType != "dismantle") continue;
                    names.Add("ACT" + ji.strName);
                    names.Add("ACT" + ji.strName + "Allow");
                }
            }
            if (names.Count > 0) removals = names;
            return names;
        }

        [HarmonyPatch(typeof(Interaction), "ValidateParticipants")]
        [HarmonyPostfix()]
        public static void ValidateParticipants_Postfix(Interaction __instance, CondOwner them, ref bool __result)
        {
            if (!__result || them == null || __instance?.strName == null) return;
            try
            {
                if (Removals().Contains(__instance.strName) && SiteLockout.IsLocked(them)) __result = false;
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Checking a site's equipment lockout failed: " + ex);
            }
        }
    }
}
