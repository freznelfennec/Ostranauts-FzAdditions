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
    public static class SmesPatch
    {
        //Runs about once a game second, Powered has just drawn its standby power and knows what the conduit offers
        [HarmonyPatch("Run")]
        [HarmonyPostfix()]
        public static void Run_Postfix(Powered __instance)
        {
            CondOwner co = __instance.CO;
            if (!Smes.IsUnit(co)) return;
            FzSmesLook.Attach(co);

            //Something else is recharging the unit itself, see Smes.AddRechargeHold
            if (Smes.IsRechargeHeld(co)) return;

            Smes.Recharge(__instance, Smes.GetTimeSinceUpdate(__instance));
        }
    }

    //A unit's item is built before its CO, which has its name and saved conditions once this has run. Runs while
    //the game is paused, as a save loads.
    [HarmonyPatch(typeof(CondOwner), nameof(CondOwner.SetData))]
    public static class SmesLookPatch
    {
        [HarmonyPostfix()]
        public static void SetData_Postfix(CondOwner __instance)
        {
            FzSmesLook.Attach(__instance);
        }
    }
}
