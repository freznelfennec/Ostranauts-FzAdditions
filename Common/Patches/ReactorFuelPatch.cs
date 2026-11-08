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

        //What a watched reactor holds before its fusion update, so the update's whole burn can be measured
        public struct Held
        {
            public bool Measured;
            public double A;
            public double B;
        }

        [HarmonyPatch(typeof(FusionIC), "Fusion")]
        [HarmonyPrefix()]
        public static void Fusion_Prefix(FusionIC __instance, out Held __state)
        {
            __state = new Held();
            CondOwner reactor = __instance.COSelf;
            if (!ReactorFuel.IsWatched(reactor)) return;
            ReactorFuel.GetHeld(reactor, out __state.A, out __state.B);
            __state.Measured = true;
        }

        //Only runs while fusing, after the reactor has taken its own burn
        [HarmonyPatch(typeof(FusionIC), "Fusion")]
        [HarmonyPostfix()]
        public static void Fusion_Postfix(FusionIC __instance, double fDeltaTime, Held __state)
        {
            ReactorFuel.PayBurn(__instance, fDeltaTime);
            if (!__state.Measured) return;
            try
            {
                ReactorFuel.GetHeld(__instance.COSelf, out double leftA, out double leftB);
                ReactorFuel.ReportBurn(__instance.COSelf, Math.Max(0.0, __state.A - leftA), Math.Max(0.0, __state.B - leftB), fDeltaTime);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Reporting a reactor's burn failed: " + ex);
            }
        }

        //Every reactor update, fusing or not
        [HarmonyPatch(typeof(FusionIC), "Run")]
        [HarmonyPostfix()]
        public static void Run_Postfix(FusionIC __instance, double fDeltaTime)
        {
            try
            {
                ReactorFuel.ReportRun(__instance.COSelf, fDeltaTime);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Reporting a reactor's update failed: " + ex);
            }
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
