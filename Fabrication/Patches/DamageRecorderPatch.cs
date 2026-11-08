using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using UnityEngine;

namespace Freznel.FzAdditions.Patches
{
    //Feeds the ship's damage recorders: what damage destroys, and each recorder's own upkeep
    [HarmonyPatch]
    public static class DamageRecorderPatch
    {
        //Damage past an item's limit runs its destroy interaction, which mode switches it into its wreckage. The old CO
        //is still whole here. An item mode switched into another installed form (its damaged one) isn't destroyed,
        //and nor is one switched by a dismantle or uninstall, which leave its damage under its limit.
        [HarmonyPatch(typeof(CondOwner), nameof(CondOwner.ModeSwitch))]
        [HarmonyPrefix()]
        public static void ModeSwitch_Prefix(CondOwner __instance, CondOwner coNew)
        {
            try
            {
                if (__instance == null || coNew == null || __instance.ship == null || !__instance.HasCond("IsInstalled") || coNew.HasCond("IsInstalled")) return;
                double max = __instance.GetCondAmount("StatDamageMax");
                if (max <= 0.0 || __instance.GetCondAmount("StatDamage") < max) return;
                DamageRecorder.Log(__instance);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Logging a destroyed item failed: " + ex);
            }
        }

        //A recorder switched off, damaged or uninstalled stops working, and its copy of the log goes with it
        [HarmonyPatch(typeof(CondOwner), "ModeSwitchInternal")]
        [HarmonyPostfix()]
        public static void ModeSwitchInternal_Postfix(CondOwner coNew)
        {
            try
            {
                if (DamageRecorder.IsRecorder(coNew) && !DamageRecorder.IsWorking(coNew)) DamageRecorder.Forget(coNew);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Clearing a damage recorder's log failed: " + ex);
            }
        }

        //About once a game second, whether or not it's powered
        [HarmonyPatch(typeof(Powered), "Run")]
        [HarmonyPostfix()]
        public static void Run_Postfix(Powered __instance)
        {
            CondOwner co = __instance.CO;
            if (!DamageRecorder.IsRecorder(co) || co.ship == null || GUIFFWD.Active) return;
            try
            {
                DamageRecorder.Upkeep(co);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Damage recorder {co.strID} failed: {ex}");
            }
        }
    }
}
