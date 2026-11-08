using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{

    [HarmonyPatch(typeof(DataHandler))]
    public static class DataPatch
    {
        [HarmonyPatch(nameof(DataHandler.AllPostLoadAsync))]
        [HarmonyPostfix()]
        public static void AllPostLoadAsync_Postfix()
        {
            FzAdditions.Logger.LogMessage("Starting data patch");

            //Jump drives aren't sold anywhere: like the rest of Faure-Zhang's secret tech, they only come from its
            //data vaults.

            //The Signal Traces navmod is sold at OKLG's supply kiosk, so players find it rather than knowing its
            //blueprint. The base navmods are sold there by the station's fixer.
            AddToLoot("ItmOKLGSupplyKioskInv", "ItmFzSignalTraces=0.6x1");

            FzAdditions.Logger.LogMessage("Data patch complete");
        }

        //Adds an entry to a base game loot table in place, so other mods' changes to it stay
        private static void AddToLoot(string table, string entry)
        {
            Loot loot = DataHandler.GetLoot(table);
            if (loot == null || loot.strName != table)
            {
                FzAdditions.Logger.LogError("Could not find loot " + table);
                return;
            }
            string name = entry.Split('=')[0];
            if (loot.aLoots.Any(e => e.Split('=')[0] == name)) return;
            loot.aLoots = loot.aLoots.Append(entry).ToArray();
        }

    }
}
