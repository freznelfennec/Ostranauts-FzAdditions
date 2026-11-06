using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Patches
{
    //Loads the fabrication data once the game's has loaded, and keeps learned blueprints in the game save
    [HarmonyPatch]
    public static class FabricationPatch
    {
        //After every mod's data is in, so recipes can check the COs they name. Should this mod's patches go in after
        //that has run, the data loads on first use instead.
        [HarmonyPatch(typeof(DataHandler), nameof(DataHandler.AllPostLoadAsync))]
        [HarmonyPostfix()]
        public static void AllPostLoadAsync_Postfix()
        {
            FabData.Load();
        }

        //Runs before every load and new game, and when leaving to the menu
        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.EmptyScene))]
        [HarmonyPrefix()]
        public static void EmptyScene_Prefix()
        {
            Blueprints.Reset();
        }

        //The save's custom infos are global state outside any ship, written as the game saves
        [HarmonyPatch(typeof(CrewSim), "CustomInfosString")]
        [HarmonyPostfix()]
        public static void CustomInfosString_Postfix(ref string[] __result)
        {
            try
            {
                __result = (__result ?? Array.Empty<string>()).Concat(new[] { Blueprints.SaveKey + "=" + Blueprints.Save() }).ToArray();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Saving blueprints failed: " + ex);
            }
        }

        //Read back near the end of loading a save, and also run by any interaction that carries custom infos, which is
        //how a quest beat grants a blueprint. The game acts only on keys it knows, so it leaves these alone.
        [HarmonyPatch(typeof(CrewSim), "SetCustomInfos")]
        [HarmonyPostfix()]
        public static void SetCustomInfos_Postfix(string[] inputs)
        {
            if (inputs == null) return;
            try
            {
                foreach (string input in inputs)
                {
                    int split = input?.IndexOf('=') ?? -1;
                    if (split <= 0) continue;
                    string key = input.Substring(0, split), value = input.Substring(split + 1);
                    if (key == Blueprints.SaveKey) Blueprints.Load(value);
                    else if (key == Blueprints.AddKey && !Blueprints.Learn(value.Trim()) && !Blueprints.IsKnown(value.Trim()))
                    {
                        FzAdditions.Logger.LogWarning("An interaction granted blueprint " + value + ", which no recipe has");
                    }
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Loading blueprints failed: " + ex);
            }
        }
    }
}
