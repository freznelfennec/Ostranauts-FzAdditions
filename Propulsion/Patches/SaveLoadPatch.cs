using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{
    //The jump drive's state lives on the ship and its parts, which the game saves. What is held here besides is only
    //read from them, so it goes when the world does and is read again from what the next one loads.
    [HarmonyPatch]
    public static class SaveLoadPatch
    {
        //Runs before every load and new game, and when leaving to the menu
        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.EmptyScene))]
        [HarmonyPrefix()]
        public static void EmptyScene_Prefix()
        {
            try
            {
                JumpSequence.Reset();
                JumpCorrection.Reset();
                SeriesTarget.Reset();
                MatchTarget.Reset();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Jump drive reset failed: " + ex);
            }
        }
    }
}
