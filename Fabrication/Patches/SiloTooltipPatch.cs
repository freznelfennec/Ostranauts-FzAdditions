using Freznel.FzAdditions.UI;
using HarmonyLib;
using Ostranauts.UI.MegaToolTip;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Patches
{
    //Adds the silo section to the mega tooltip, after the game's own sections like the jump drive's
    [HarmonyPatch(typeof(ModuleHost))]
    public static class SiloTooltipPatch
    {
        private static readonly AccessTools.FieldRef<ModuleHost, GameObject[]> ItemModules = AccessTools.FieldRefAccess<ModuleHost, GameObject[]>("_itemModulePrefabs");

        [HarmonyPatch("Awake")]
        [HarmonyPostfix()]
        public static void Awake_Postfix(ModuleHost __instance)
        {
            ItemModules(__instance) = ItemModules(__instance).Concat(new[] { FzSiloModule.GetPrefab() }).ToArray();
        }
    }
}
