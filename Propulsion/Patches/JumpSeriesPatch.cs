using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{
    //A series was worked out for the drive as it stood, so changing its parts ends the series. While one runs the
    //NavCom has the ship's thrust to itself.
    [HarmonyPatch]
    public static class JumpSeriesPatch
    {
        private const double MinSkip = 60.0; //Game seconds, a skip is at least an hour and the frames around it are paused

        private static bool splitting;

        //Super fast forward applies its whole skip in this one call. A running series splits it into pieces at its
        //corrections and jumps instead, each run through this same update. A failure partway must not stop the skip
        //finishing, it would leave the game on the fast forward screen, so whatever is left is applied as one piece.
        [HarmonyPatch(typeof(StarSystem), nameof(StarSystem.Update))]
        [HarmonyPrefix()]
        public static bool Update_Prefix(StarSystem __instance, double fTimeDelta)
        {
            Ship ship = CrewSim.shipCurrentLoaded;
            if (splitting || !GUIFFWD.Active || fTimeDelta < MinSkip) return true;

            bool matching = VelocityMatch.IsMatching(ship);
            if (!matching && !JumpSequence.CanRunSkip(ship)) return true;

            double applied = 0.0;
            void Advance(double piece)
            {
                splitting = true;
                try
                {
                    __instance.Update(piece);
                }
                finally
                {
                    splitting = false;
                }
                applied += piece;
            }

            try
            {
                if (matching) VelocityMatch.RunSkip(ship, fTimeDelta, Advance);
                else JumpSequence.RunSkip(ship, fTimeDelta, Advance);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Jump series time skip failed: " + ex);
                if (fTimeDelta - applied > 0.0) Advance(fTimeDelta - applied);
            }
            return false;
        }

        //Starts once the time skip has caught every object up
        [HarmonyPatch(typeof(GUIFFWD), "Unpause")]
        [HarmonyPrefix()]
        public static void Unpause_Prefix(GUIFFWD __instance)
        {
            try
            {
                JumpSequence.FinishSkip(__instance.COSelf?.ship);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Jump series time skip finish failed: " + ex);
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
            bool cellInstalled = Smes.IsUnit(coNew) && coNew.HasCond("IsInstalled") && !__instance.HasCond("IsInstalled");
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

        //Every RCS and rotation input goes through here, the nav console's keys, the docking screen, held thrust and
        //station keeping. Only the NavCom's own get through while a series runs.
        [HarmonyPatch(typeof(Ship), nameof(Ship.Maneuver))]
        [HarmonyPrefix()]
        public static bool Maneuver_Prefix(Ship __instance)
        {
            if (JumpCorrection.NavComThrusting || !JumpCorrection.IsLocked(__instance)) return true;

            JumpCorrection.WarnLocked(__instance);
            return false;
        }

        //The reactor sets the torch's thrust here on each of its updates
        [HarmonyPatch(typeof(Ship), nameof(Ship.SetThrust))]
        [HarmonyPrefix()]
        public static void SetThrust_Prefix(Ship __instance, ref double fAmount)
        {
            if (fAmount > 0.0 && JumpCorrection.IsLocked(__instance) && !JumpCorrection.IsTorchBurning(__instance)) fAmount = 0.0;
        }
    }
}
