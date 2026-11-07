using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.Ships;
using System;

namespace Freznel.FzAdditions.Patches
{
    //Standing costs at sites depend on whether the site knows who the player is (SiteIdentity): docking with the
    //transponder on identifies them, and until then what the player's people do aboard, and shooting at the site while
    //flying dark, costs them nothing with its owner
    [HarmonyPatch]
    public static class SiteStandingPatch
    {
        private static readonly AccessTools.FieldRef<WeaponsSystem, Ship> WeaponsShip = AccessTools.FieldRefAccess<WeaponsSystem, Ship>("_ship");

        //Both ships of a docking may call this; a sync re-links docks that already exist, such as on loading, so it
        //identifies no one
        [HarmonyPatch(typeof(Ship), nameof(Ship.Dock), new[] { typeof(Ship), typeof(bool), typeof(string), typeof(string) })]
        [HarmonyPostfix()]
        public static void Dock_Postfix(Ship __instance, Ship dockingTarget, bool bSyncOnly)
        {
            if (bSyncOnly) return;
            try
            {
                SiteIdentity.Docked(__instance, dockingTarget);
                SiteIdentity.Docked(dockingTarget, __instance);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Noting a docking at a site failed: {ex}");
            }
        }

        //Interactions, explosions and the like: a cost to standing for something done to a thing aboard a site
        [HarmonyPatch(typeof(CondOwner), nameof(CondOwner.ApplyFactionReps))]
        [HarmonyPrefix()]
        public static bool ApplyFactionReps_Prefix(CondOwner __instance, CondOwner coDoing, float fChange)
        {
            try
            {
                return !SiteIdentity.Excuses(__instance, coDoing, fChange);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Checking a site standing change failed: {ex}");
                return true;
            }
        }

        //Firing on a ship
        [HarmonyPatch(typeof(WeaponsSystem), nameof(WeaponsSystem.ApplyFactionDamage))]
        [HarmonyPrefix()]
        public static bool ApplyFactionDamage_Prefix(WeaponsSystem __instance, float factionPenalty, Ship target)
        {
            try
            {
                Ship shooter = WeaponsShip(__instance);
                return !SiteIdentity.ExcusesFire(shooter, target ?? shooter?.shipCombatTarget, factionPenalty);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Checking a site standing change from fire failed: {ex}");
                return true;
            }
        }
    }
}
