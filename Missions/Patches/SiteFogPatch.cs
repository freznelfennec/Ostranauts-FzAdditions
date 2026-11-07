using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Freznel.FzAdditions.Patches
{
    //What's under the fog can't be hovered, clicked or selected, so the info panel never names it: every mouse pick in
    //the game goes through CrewSim.GetMouseOverCO, and this takes out anything on a fogged tile of a fogged station
    [HarmonyPatch(typeof(CrewSim), "GetMouseOverCO", new[] { typeof(string[]), typeof(CondTrigger), typeof(Vector3?) })]
    public static class SiteFogPatch
    {
        [HarmonyPostfix()]
        public static void GetMouseOverCO_Postfix(List<CondOwner> __result)
        {
            if (__result == null || __result.Count == 0) return;
            try
            {
                __result.RemoveAll(Hidden);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Hiding fogged things from the mouse failed: " + ex);
            }
        }

        private static bool Hidden(CondOwner co)
        {
            Ship ship = co?.ship;
            if (ship == null || !SiteFog.Applies(ship)) return false;
            //Fog only covers the station's own tiles, so something whose centre is out past the hull (a weapon's barrel,
            //a sensor) is never under it
            Vector3 at = co.tf.position;
            int index = ship.GetTileIndexAtWorldCoords(at.x, at.y);
            Tile tile = ship.GetTileByIndex(index);
            if (tile == null || !tile.IsShipTileOrSub) return false;
            return !SiteFog.IsRevealed(ship, index);
        }
    }
}
