using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{
    [HarmonyPatch(typeof(FusionIC))]
    public static class JumpTapPatch
    {
        private const double ModulePower = 0.022; //What the reactor feeds a laser array, the module the tap is built from

        private static readonly AccessTools.FieldRef<FusionIC, List<CondOwner>> Modules = AccessTools.FieldRefAccess<FusionIC, List<CondOwner>>("aModules");

        //The reactor only runs the modules it knows, so a plugged in tap is fed here the way it feeds its lasers.
        //Its shutdown overrides every plugged in module off, which turns the tap back off.
        [HarmonyPatch("Fusion")]
        [HarmonyPostfix()]
        public static void Fusion_Postfix(FusionIC __instance)
        {
            List<CondOwner> modules = Modules(__instance);
            if (modules == null || __instance.COSelf.HasCond("IsShuttingDown") || !ReactorFuel.IsFusing(__instance.COSelf)) return;

            foreach (CondOwner module in modules)
            {
                if (!module.HasCond(Constants.TapCond)) continue;
                module.ZeroCondAmount("IsOverrideOff");
                module.SetCondAmount("StatPower", ModulePower);
            }
        }
    }
}
