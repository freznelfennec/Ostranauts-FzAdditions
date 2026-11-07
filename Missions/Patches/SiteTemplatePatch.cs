using Freznel.FzAdditions.Util;
using Freznel.FzAdditions.Util.SiteGen;
using HarmonyLib;
using System;

namespace Freznel.FzAdditions.Patches
{
    //Generated site templates are only registered for the session they're made in. If the game asks for one that isn't
    //registered, after a load, it's built again from its name (kind, size, seed and corporation), which gives the same layout.
    [HarmonyPatch(typeof(DataHandler), nameof(DataHandler.GetShip))]
    public static class SiteTemplatePatch
    {
        [HarmonyPrefix()]
        public static void GetShip_Prefix(string strName)
        {
            if (strName == null || DataHandler.dictShips == null || DataHandler.dictShips.ContainsKey(strName)) return;
            if (!SiteShell.TryParseTemplateName(strName, out SiteKind kind, out SiteSize size, out int seed, out string corp)) return;
            try
            {
                //A name from before sites had corporations is built as Generic, then kept under the name asked for too
                string built = SiteSpawner.Register(kind, size, seed, corp);
                if (built != null && built != strName && DataHandler.dictShips.TryGetValue(built, out JsonShip ship)) DataHandler.dictShips[strName] = ship;
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Rebuilding site template {strName} failed: {ex}");
            }
        }
    }
}
