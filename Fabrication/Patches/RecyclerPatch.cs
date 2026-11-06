using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;

namespace Freznel.FzAdditions.Patches
{
    //Runs recycling and fabrication from each machine's power update, about once a game second, after Powered has drawn its idle
    //power and knows what the grid offers
    [HarmonyPatch(typeof(Powered), "Run")]
    public static class RecyclerPatch
    {
        private static readonly AccessTools.FieldRef<Powered, double> LastUpdate = AccessTools.FieldRefAccess<Powered, double>("fUpdateLast");

        [HarmonyPostfix()]
        public static void Run_Postfix(Powered __instance)
        {
            CondOwner co = __instance.CO;
            if (co == null) return;
            bool recycler = Recycler.IsRecycler(co);
            if (!recycler && !Fabricator.IsFabricator(co)) return;
            try
            {
                double seconds = co.Clock.Epoch - LastUpdate(__instance);
                if (recycler) RecyclerProcess.Run(__instance, seconds);
                else FabricatorProcess.Run(__instance, seconds);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Machine {co.strID} failed to run: {ex}");
            }
        }
    }
}
