using Freznel.FzAdditions.UI;
using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{
    [HarmonyPatch(typeof(Powered))]
    public static class JumpCellPatch
    {
        //Runs about once a game second, Powered has just drawn its standby power and knows what the conduit offers
        [HarmonyPatch("Run")]
        [HarmonyPostfix()]
        public static void Run_Postfix(Powered __instance)
        {
            CondOwner co = __instance.CO;
            if (!JumpCell.IsCell(co)) return;
            FzCellLook.Attach(co);

            //A series recharges its cells itself through a time skip, even one it finishes partway through
            if (GUIFFWD.Active && (JumpSequence.IsSeriesRunning(co.ship) || JumpSequence.IsSkipped(co.ship))) return;

            JumpCell.Recharge(__instance, JumpCell.GetTimeSinceUpdate(__instance));
        }
    }

    //A cell's item is built before its CO, which has its name and saved conditions once this has run. Runs while
    //the game is paused, as a save loads.
    [HarmonyPatch(typeof(CondOwner), nameof(CondOwner.SetData))]
    public static class CellLookPatch
    {
        [HarmonyPostfix()]
        public static void SetData_Postfix(CondOwner __instance)
        {
            FzCellLook.Attach(__instance);
        }
    }
}
