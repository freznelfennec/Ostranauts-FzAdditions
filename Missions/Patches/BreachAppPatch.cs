using Freznel.FzAdditions.UI;
using HarmonyLib;
using System;

namespace Freznel.FzAdditions.Patches
{
    //BREACH//OS opens in the player's PDA like one of its own apps (FzBreachApp), not as a machine panel: raising a
    //data vault's or lockout controller's prop map opens the app instead, and the PDA leaving the app's state hides it
    [HarmonyPatch]
    public static class BreachAppPatch
    {
        private const string VaultKey = "FzDataVault";
        private const string LockoutKey = "FzLockout";

        [HarmonyPatch(typeof(CrewSim), nameof(CrewSim.RaiseUI))]
        [HarmonyPrefix()]
        public static bool RaiseUI_Prefix(string strCOGUIKey, CondOwner coSelf)
        {
            if (strCOGUIKey != VaultKey && strCOGUIKey != LockoutKey) return true;
            try
            {
                if (!FzBreachApp.Open(coSelf))
                {
                    CrewSim.GetSelectedCrew()?.LogMessage("There's no PDA to run the intrusion app on.", "Bad", "Game");
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Opening BREACH//OS in the PDA failed: " + ex);
            }
            return false;
        }

        //Home, close, the PDA's hotkey or another app all change its state; any state but the app's own hides it
        [HarmonyPatch(typeof(GUIPDA), nameof(GUIPDA.State), MethodType.Setter)]
        [HarmonyPostfix()]
        public static void State_Postfix(GUIPDA __instance)
        {
            if (__instance.State != FzBreachApp.BreachState) FzBreachApp.Hide();
        }
    }
}
