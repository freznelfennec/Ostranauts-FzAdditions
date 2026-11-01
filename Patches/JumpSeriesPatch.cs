using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{
    //A series was worked out for the drive as it stood, so changing its parts ends the series
    [HarmonyPatch]
    public static class JumpSeriesPatch
    {
        private static readonly AccessTools.FieldRef<GUIFFWD, float> FastForwardTime = AccessTools.FieldRefAccess<GUIFFWD, float>("fTimeDelta");

        //Called once the time skip has been applied. Super fast forward has no frames for a series to run in.
        //A failure here must not stop the skip finishing, it would leave the game on the fast forward screen.
        [HarmonyPatch(typeof(GUIFFWD), "Unpause")]
        [HarmonyPrefix()]
        public static void Unpause_Prefix(GUIFFWD __instance)
        {
            try
            {
                JumpSequence.CatchUpSeries(__instance.COSelf?.ship, FastForwardTime(__instance));
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Jump series catch up failed: " + ex);
            }
        }

        //Installing, uninstalling, damage and switching an emitter off all replace the CO, the new one says what
        //became of it. A cell switches off without being replaced, its panel handles that.
        [HarmonyPatch(typeof(CondOwner), nameof(CondOwner.ModeSwitch))]
        [HarmonyPrefix()]
        public static void ModeSwitch_Prefix(CondOwner __instance, CondOwner coNew)
        {
            if (coNew == null) return;

            bool lost = JumpSequence.IsDrivePart(__instance) && !JumpSequence.IsWorkingDrivePart(coNew);
            bool cellInstalled = JumpCell.IsCell(coNew) && coNew.HasCond("IsInstalled") && !__instance.HasCond("IsInstalled");
            if (lost || cellInstalled) JumpSequence.CancelForDriveChange(__instance.ship);
        }

        //Anything else that takes a part off the ship. A ship being unloaded is destroyed first and loses nothing.
        [HarmonyPatch(typeof(Ship), nameof(Ship.RemoveCO))]
        [HarmonyPrefix()]
        public static void RemoveCO_Prefix(Ship __instance, CondOwner objCO)
        {
            if (__instance.bDestroyed || !JumpSequence.IsDrivePart(objCO) || objCO.HasCond("IsModeSwitching")) return;
            JumpSequence.CancelForDriveChange(__instance);
        }
    }
}
