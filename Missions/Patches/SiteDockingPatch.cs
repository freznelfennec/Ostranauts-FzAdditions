using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.Ships.Comms;
using System;
using System.Collections.Generic;

namespace Freznel.FzAdditions.Patches
{
    //Generated sites answer comms like an uncrewed ship, need the game's forced docking procedure, and black sites can
    //be moored to (SiteDocking)
    [HarmonyPatch]
    public static class SiteDockingPatch
    {
        private const string NoPilotList = "TXTShipStandardInteractionsNoPilot";
        private static readonly AccessTools.FieldRef<Comms, Ship> CommsShip = AccessTools.FieldRefAccess<Comms, Ship>("_ship");

        //Hailing a site offers what hailing an uncrewed ship offers, built the way the game builds its own list
        [HarmonyPatch(typeof(Comms), nameof(Comms.GetAvailableInteractions))]
        [HarmonyPrefix()]
        public static bool GetAvailableInteractions_Prefix(Comms __instance, CondOwner caller, CondOwner captain3rdShip, ref List<Interaction> __result)
        {
            try
            {
                Ship ship = CommsShip(__instance);
                if (!SiteDocking.IsSite(ship)) return true;
                var list = new List<Interaction>();
                foreach (string lootName in DataHandler.GetLoot(NoPilotList).GetLootNames())
                {
                    Interaction interaction = DataHandler.GetInteraction(lootName);
                    if (interaction == null) continue;
                    interaction.objUs = caller;
                    interaction.objThem = ship.ShipCO;
                    interaction.obj3rd = captain3rdShip;
                    if (!interaction.Triggered()) continue;
                    if (interaction.nLogging == Interaction.Logging.NONE) interaction = Comms.GetDeepestVisibleReply(interaction);
                    list.Add(interaction);
                }
                __result = list;
                return false;
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Listing a site's comms failed: " + ex);
                return true;
            }
        }

        //The forced docking chain refuses stations (its NotAStationNotPlayer test); a site passes it
        [HarmonyPatch(typeof(JsonShipSpec), nameof(JsonShipSpec.Matches))]
        [HarmonyPrefix()]
        public static void Matches_Prefix(JsonShipSpec __instance, out SiteDocking.Context __state)
        {
            __state = SiteDocking.Pretending;
            if (__instance.strName == "NotAStationNotPlayer") SiteDocking.Pretending = SiteDocking.Context.Docking;
        }

        [HarmonyPatch(typeof(JsonShipSpec), nameof(JsonShipSpec.Matches))]
        [HarmonyFinalizer()]
        public static void Matches_Finalizer(SiteDocking.Context __state)
        {
            SiteDocking.Pretending = __state;
        }

        //The mooring control refuses stations; a black site, carved into rock, can be moored to like an asteroid
        [HarmonyPatch(typeof(NavModMooringControl), "UpdateUI")]
        [HarmonyPrefix()]
        public static void UpdateUI_Prefix(out SiteDocking.Context __state)
        {
            __state = SiteDocking.Pretending;
            SiteDocking.Pretending = SiteDocking.Context.Mooring;
        }

        [HarmonyPatch(typeof(NavModMooringControl), "UpdateUI")]
        [HarmonyFinalizer()]
        public static void UpdateUI_Finalizer(SiteDocking.Context __state)
        {
            SiteDocking.Pretending = __state;
        }

        [HarmonyPatch(typeof(NavModMooringControl), "OnClampPressed")]
        [HarmonyPrefix()]
        public static void OnClampPressed_Prefix(out SiteDocking.Context __state)
        {
            __state = SiteDocking.Pretending;
            SiteDocking.Pretending = SiteDocking.Context.Mooring;
        }

        [HarmonyPatch(typeof(NavModMooringControl), "OnClampPressed")]
        [HarmonyFinalizer()]
        public static void OnClampPressed_Finalizer(SiteDocking.Context __state)
        {
            SiteDocking.Pretending = __state;
        }

        //Inside those checks only, a site isn't a station. Cheap when nothing is pretending, as this is called often.
        [HarmonyPatch(typeof(Ship), nameof(Ship.IsStation))]
        [HarmonyPostfix()]
        public static void IsStation_Postfix(Ship __instance, ref bool __result)
        {
            if (!__result || SiteDocking.Pretending == SiteDocking.Context.None) return;
            if (SiteDocking.HidesStation(__instance)) __result = false;
        }
    }
}
