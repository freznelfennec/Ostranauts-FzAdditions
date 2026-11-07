using Freznel.FzAdditions.UI;
using Freznel.FzAdditions.Util;
using HarmonyLib;

namespace Freznel.FzAdditions.Patches
{
    //Clears the missions section's caches when the world is emptied for a load; what they cache is saved on the ships
    [HarmonyPatch]
    public static class MissionsScenePatch
    {
        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.EmptyScene))]
        [HarmonyPrefix()]
        public static void EmptyScene_Prefix()
        {
            SiteFog.Forget();
            FzSiteFogView.Clear();
            FzBreachApp.Forget();
            SignalTraces.Reset(); //Global state, read back from the save being loaded
        }
    }
}
