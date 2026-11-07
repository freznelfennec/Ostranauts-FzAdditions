using Freznel.FzAdditions.UI;
using HarmonyLib;
using Ostranauts.UI.MegaToolTip;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.Patches
{
    [HarmonyPatch(typeof(ModuleHost))]
    public static class SmesTooltipPatch
    {
        private static readonly AccessTools.FieldRef<ModuleHost, GameObject[]> ItemModules = AccessTools.FieldRefAccess<ModuleHost, GameObject[]>("_itemModulePrefabs");

        //These go last, after the show more toggle, as they are too tall for the tooltip's collapsed height
        [HarmonyPatch("Awake")]
        [HarmonyPostfix()]
        public static void Awake_Postfix(ModuleHost __instance)
        {
            ItemModules(__instance) = ItemModules(__instance).Concat(new[] { FzSmesModule.GetPrefab(), FzSmesTapModule.GetPrefab() }).ToArray();
        }
    }
}
