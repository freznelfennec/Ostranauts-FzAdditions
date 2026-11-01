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

            //A series recharges its cells itself once the time skip is over
            if (GUIFFWD.Active && JumpSequence.IsSeriesRunning(co.ship)) return;

            JumpCell.Recharge(__instance, JumpCell.GetTimeSinceUpdate(__instance));
        }
    }
}
