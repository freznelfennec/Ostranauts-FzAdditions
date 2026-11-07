using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Patches
{
    //Nav console panels' hooks into the console. No Harmony attributes: NavPanels applies them only when its setting
    //is on, so with it off the console is untouched.
    public static class NavPanelsPatch
    {
        //GUIOrbitDraw.Init, as the console opens on a nav station, and CrewSwitch, as someone else takes it over
        public static void Opened_Postfix(GUIOrbitDraw __instance)
        {
            try
            {
                NavPanels.Opened(__instance);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Nav console panels failed to open: " + ex);
            }
        }

        //The layout has just been saved as edit mode closes, it belongs to the panel showing
        public static void SaveModules_Postfix(GUIOrbitDraw __instance)
        {
            try
            {
                NavPanels.StoreShowing(__instance.COSelf);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Nav console panels failed to keep a layout: " + ex);
            }
        }
    }
}
