using HarmonyLib;
using System;

namespace Freznel.FzAdditions.Patches
{
    //Per the user, breach spikes come from belt pirate ships for now, the one piece of the mod's equipment found outside
    //Faure-Zhang's sites. Belt pirate crews (the CombatPilotBeltPirates person spec) are given FzLootBeltPirateCrew once
    //the game's data has loaded: their usual loot table, plus a chance of a spike. The base table is shared with other
    //pirates and isn't copied, so a game update to it still applies.
    [HarmonyPatch]
    public static class PirateLootPatch
    {
        private const string Spec = "CombatPilotBeltPirates";
        private const string BaseLoot = "ItmPirateStartLootSuitWeapon";
        private const string FzLoot = "FzLootBeltPirateCrew";

        [HarmonyPatch(typeof(DataHandler), nameof(DataHandler.AllPostLoadAsync))]
        [HarmonyPostfix()]
        public static void AllPostLoadAsync_Postfix()
        {
            try
            {
                if (DataHandler.dictPersonSpecs == null || !DataHandler.dictPersonSpecs.TryGetValue(Spec, out JsonPersonSpec spec)) return;
                if (DataHandler.dictLoot == null || !DataHandler.dictLoot.ContainsKey(FzLoot)) return;
                if (spec.strLoot == BaseLoot) spec.strLoot = FzLoot;
                else if (spec.strLoot != FzLoot) FzAdditions.Logger.LogInfo($"{Spec} carries {spec.strLoot}, not {BaseLoot}, so belt pirates get no breach spikes");
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Giving belt pirates breach spikes failed: " + ex);
            }
        }
    }
}
