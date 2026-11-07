using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Linq;

namespace Freznel.FzAdditions.Patches
{
    //Runs signal traces (SignalTraces) in game time, and keeps them in the save's custom infos
    [HarmonyPatch]
    public static class SignalTracePatch
    {
        //The game only calls this while unpaused and while game time moves
        [HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.Update))]
        [HarmonyPostfix()]
        public static void Update_Postfix()
        {
            try
            {
                SignalTraces.Tick();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Signal trace update failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(CrewSim), "CustomInfosString")]
        [HarmonyPostfix()]
        public static void CustomInfosString_Postfix(ref string[] __result)
        {
            try
            {
                __result = (__result ?? Array.Empty<string>()).Concat(new[] { SignalTraces.SaveKey + "=" + SignalTraces.Save() }).ToArray();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Saving signal traces failed: " + ex);
            }
        }

        //Read back near the end of loading a save. The game acts only on keys it knows, so it leaves this alone.
        [HarmonyPatch(typeof(CrewSim), "SetCustomInfos")]
        [HarmonyPostfix()]
        public static void SetCustomInfos_Postfix(string[] inputs)
        {
            if (inputs == null) return;
            try
            {
                string prefix = SignalTraces.SaveKey + "=";
                string entry = inputs.FirstOrDefault(i => i != null && i.StartsWith(prefix));
                if (entry != null) SignalTraces.Load(entry.Substring(prefix.Length));
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Loading signal traces failed: " + ex);
            }
        }
    }
}
