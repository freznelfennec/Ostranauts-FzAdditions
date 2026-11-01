using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{
    [HarmonyPatch]
    public static class ReactorFuelPatch
    {
        private const double RateDisplayScale = 87658.125; //GUIReactor's scale for its rate readouts

        private static readonly AccessTools.FieldRef<GUIReactor, GUI7Seg> SegDRate = AccessTools.FieldRefAccess<GUIReactor, GUI7Seg>("segDRate");
        private static readonly AccessTools.FieldRef<GUIReactor, GUI7Seg> SegHe3Rate = AccessTools.FieldRefAccess<GUIReactor, GUI7Seg>("segHe3Rate");

        //Only runs while fusing, after the reactor has taken its own burn
        [HarmonyPatch(typeof(FusionIC), "Fusion")]
        [HarmonyPostfix()]
        public static void Fusion_Postfix(FusionIC __instance, double fDeltaTime)
        {
            ReactorFuel.PayBurn(__instance, fDeltaTime);
        }

        [HarmonyPatch(typeof(GUIReactor), "UpdateUI")]
        [HarmonyPostfix()]
        public static void UpdateUI_Postfix(GUIReactor __instance)
        {
            CondOwner reactor = __instance.COSelf;
            double burn = ReactorFuel.GetBurnRate(reactor);
            if (burn <= 0.0) return;

            SetRate(SegDRate(__instance), reactor, "StatICDRate", burn * FusionIC.aReactantAmounts[0]);
            SetRate(SegHe3Rate(__instance), reactor, "StatICHe3Rate", burn * FusionIC.aReactantAmounts[1]);
        }

        private static void SetRate(GUI7Seg display, CondOwner reactor, string rateCond, double extra)
        {
            display.SetValue(Convert.ToSingle((reactor.GetCondAmount(rateCond) + extra) * RateDisplayScale));
        }
    }
}
