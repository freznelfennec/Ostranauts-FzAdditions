using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;

namespace Freznel.FzAdditions.Patches
{
    //Puts Faure-Zhang's kiosk in at OKLG the first time its main station fully loads, when its tiles are there to fit
    //the kiosk against
    [HarmonyPatch(typeof(Ship), "PostGameLoad")]
    public static class FaureZhangKioskPatch
    {
        [HarmonyPostfix()]
        public static void PostGameLoad_Postfix(Ship __instance, Ship.Loaded nLoad)
        {
            if (nLoad != Ship.Loaded.Full || !FaureZhangKiosk.IsTarget(__instance)) return;
            try
            {
                FaureZhangKiosk.TryPlace(__instance);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Placing the Faure-Zhang kiosk on {__instance?.strRegID} failed: {ex}");
            }
        }
    }
}
