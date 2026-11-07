using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Patches
{
    //SMES units, their reactor taps and the reactor's extra burn keep their state on the parts, which the game saves.
    //What is held here besides is only read from them, so it goes when the world does.
    [HarmonyPatch]
    public static class SmesSaveLoadPatch
    {
        //Runs before every load and new game, and when leaving to the menu
        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.EmptyScene))]
        [HarmonyPrefix()]
        public static void EmptyScene_Prefix()
        {
            try
            {
                Smes.Reset();
                SmesTap.Reset();
                ReactorFuel.Reset();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("SMES reset failed: " + ex);
            }
        }

        //Burn owed to the reactor is paid into its fuel before it is saved
        [HarmonyPatch(typeof(LoadManager), "SaveGameData")]
        [HarmonyPrefix()]
        public static void SaveGameData_Prefix()
        {
            try
            {
                ReactorFuel.PayAllPending();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("SMES save failed: " + ex);
            }
        }
    }
}
