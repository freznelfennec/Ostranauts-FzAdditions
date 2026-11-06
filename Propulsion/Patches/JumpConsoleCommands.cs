using Freznel.FzAdditions.UI;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{
    //The jump drive's console commands, added to the ones ConsolePatch handles
    public static class JumpConsoleCommands
    {
        public static void Register()
        {
            ConsolePatch.AddCommand("fieldcontour", KeywordFieldContour, "toggles the jump field contour overlay on the current ship or station");
            ConsolePatch.AddCommand("fieldremove", KeywordFieldRemove,
                "removes everything outside the jump field contour on the current ship or station",
                "'fz fieldremove' only reports what is outside the contour",
                "'fz fieldremove confirm' permanently destroys it and characters fall away, objects crossing the contour break, the primary docking port is kept");
            ConsolePatch.AddCommand("tapstatus", KeywordTapStatus, "lists everything a reactor tap needs to charge jump cells on the current ship or station");
            ConsolePatch.AddCommand("jumpcharge", KeywordJumpCharge, "plays the jump drive charge effect on the current ship's field contour, run it again while it plays to fade it out, and once more to stop it at once");
            ConsolePatch.AddCommand("jumpflash", KeywordJumpFlash, "plays the jump flash on its own, it also plays at the end of a completed jumpcharge");
        }

        private static bool KeywordJumpFlash(ref string strInput, string[] args)
        {
            bool played = CrewSim.objInstance != null && FzJumpFlash.Play(CrewSim.shipCurrentLoaded);
            strInput += played ? "\nJump flash played." : "\nNo contour, it needs at least 3 working emitters and does not run on stations.";
            return played;
        }

        private static bool KeywordTapStatus(ref string strInput, string[] args)
        {
            Ship ship = CrewSim.objInstance != null ? CrewSim.shipCurrentLoaded : null;
            if (ship == null)
            {
                strInput += "\nNo ship loaded.";
                return false;
            }

            CondOwner reactor = ship.Reactor;
            strInput += $"\nReactor: {(reactor != null ? reactor.strName : "none")}, fusing {ReactorFuel.IsFusing(reactor)}";

            List<CondOwner> taps = ship.GetCOs(DataHandler.GetCondTrigger(Constants.TapInstalledTrigger), bSubObjects: false, bAllowDocked: false, bAllowLocked: false);
            int active = ship.GetCOs(DataHandler.GetCondTrigger(Constants.TapActiveTrigger), bSubObjects: false, bAllowDocked: false, bAllowLocked: false).Count;
            strInput += $"\nTaps: {taps.Count} installed, {active} active";
            foreach (CondOwner tap in taps)
            {
                strInput += $"\n  {tap.strName}: powered {tap.HasCond("IsPowered")}, off {tap.HasCond("IsOff")}, override off {tap.HasCond("IsOverrideOff")}, rocker {JumpTap.IsChargeEnabled(tap)}, rate {JumpTap.GetRate(tap)}kW of {JumpTap.GetMaxRate(tap)}kW";
            }

            List<CondOwner> cells = JumpCell.GetAllCells(ship);
            strInput += $"\nCells: {cells.Count}";
            foreach (CondOwner cell in cells)
            {
                strInput += $"\n  {cell.strName}: powered {cell.HasCond("IsPowered")}, charge button {JumpCell.IsGridChargeEnabled(cell)}, missing {JumpCell.GetMissingCharge(cell)}kWh, tap charging {JumpCell.IsTapCharging(cell)}, rate {JumpCell.GetCurrentRate(cell)}kW";
            }
            return true;
        }

        private static bool KeywordFieldContour(ref string strInput, string[] args)
        {
            if (CrewSim.objInstance == null)
            {
                strInput += "\nCrewSim instance not found.";
                return false;
            }

            if (!FzFieldOverlay.Toggle())
            {
                strInput += "\nJump field contour hidden.";
                return true;
            }

            foreach (FieldContour.Segment segment in FieldContour.CalculateSegments(CrewSim.shipCurrentLoaded))
            {
                CondOwner emitter = segment.Emitter.CO;
                string line = $"{emitter.strName} at {emitter.GetPos()}, offset {segment.Emitter.Offset:0.##}m, handles {segment.Length:0.##}m, strength {segment.Strength:0}%";
                strInput += "\n" + line;
                FzAdditions.Logger.LogInfo("Field emitter: " + line);
            }

            strInput += FzFieldOverlay.CornerCount > 0
                ? $"\nJump field contour showing with {FzFieldOverlay.CornerCount} emitter(s)."
                : "\nJump field contour on, but it needs at least 3 working emitters to draw.";
            return true;
        }

        private static bool KeywordFieldRemove(ref string strInput, string[] args)
        {
            Ship ship = CrewSim.objInstance != null ? CrewSim.shipCurrentLoaded : null;
            if (ship == null)
            {
                strInput += "\nNo ship loaded.";
                return false;
            }

            if (args.Length > 2 || (args.Length == 2 && args[1].ToLower() != "confirm"))
            {
                strInput += "\nUsage: fz fieldremove or fz fieldremove confirm";
                return false;
            }

            if (FieldContour.Calculate(ship).Count == 0)
            {
                strInput += "\nNo contour, it needs at least 3 working emitters and does not run on stations.";
                return false;
            }

            if (args.Length == 1)
            {
                FieldContour.RemovalReport report = FieldContour.GetRemovalReport(ship);
                strInput += $"\n{report.Destroyed} object(s) would be destroyed, {report.Broken} broken, {report.Crew} character(s) would fall away, {report.Undocked} docked ship(s) would undock.";
                strInput += "\nRun 'fz fieldremove confirm' to destroy them. This cannot be undone.";
                return true;
            }

            int removed = FieldContour.RemoveOutside(ship, out int lost, out int damaged, out int undocked);
            strInput += $"\nRemoved {removed} object(s) outside the contour and broke {damaged} crossing it, undocked {undocked} ship(s), {lost} character(s) are falling away.";
            return true;
        }

        private static bool KeywordJumpCharge(ref string strInput, string[] args)
        {
            if (CrewSim.objInstance == null)
            {
                strInput += "\nCrewSim instance not found.";
                return false;
            }

            if (FzJumpEffect.Interrupted)
            {
                FzJumpEffect.Stop();
                strInput += "\nJump charge stopped.";
                return true;
            }

            if (FzJumpEffect.Playing)
            {
                FzJumpEffect.Interrupt();
                strInput += "\nJump charge interrupted, fading out.";
                return true;
            }

            if (!FzJumpEffect.Play(CrewSim.shipCurrentLoaded))
            {
                strInput += "\nNo contour, it needs at least 3 working emitters and does not run on stations.";
                return false;
            }

            strInput += "\nJump charge started.";
            return true;
        }
    }
}
