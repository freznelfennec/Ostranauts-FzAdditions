using Freznel.FzAdditions.UI;
using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.UI.MegaToolTip;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.Patches
{

    [HarmonyPatch(typeof(ConsoleResolver))]
    public static class ConsolePatch
    {
        [HarmonyPatch(nameof(ConsoleResolver.ResolveString))]
        [HarmonyPrefix()]
        public static bool ResolveString_Prefix(ref string strInput, ref bool __result)
        {
            string[] args = strInput.Trim().Split(' ');

            if (args[0].ToLower() == "restock")
            {
                __result = KeywordRestock(ref strInput, args);
                return false;
            }

            if (args[0].ToLower() == "fieldcontour")
            {
                __result = KeywordFieldContour(ref strInput);
                return false;
            }

            if (args[0].ToLower() == "fieldremove")
            {
                __result = KeywordFieldRemove(ref strInput, args);
                return false;
            }

            if (args.Length == 2 && args[0].ToLower() == "help" && args[1] == "fieldremove")
            {
                strInput += "\nremoves everything outside the jump field contour on the current ship or station";
                strInput += "\n'fieldremove' only reports what is outside the contour";
                strInput += "\n'fieldremove confirm' permanently destroys it and characters fall away, objects crossing the contour break, the primary docking port is kept";
                __result = true;
                return false;
            }

            if (args[0].ToLower() == "navmoddump")
            {
                __result = KeywordNavModDump(ref strInput, args);
                return false;
            }

            if (args.Length == 2 && args[0].ToLower() == "help" && args[1] == "navmoddump")
            {
                strInput += "\nwrites the layout of a nav module's prefab to the log";
                strInput += "\n'navmoddump NavModSensorsMFD' dumps that module";
                __result = true;
                return false;
            }

            if (args[0].ToLower() == "tapstatus")
            {
                __result = KeywordTapStatus(ref strInput);
                return false;
            }

            if (args.Length == 2 && args[0].ToLower() == "help" && args[1] == "tapstatus")
            {
                strInput += "\nlists everything a reactor tap needs to charge jump cells on the current ship or station";
                __result = true;
                return false;
            }

            if (args[0].ToLower() == "jumpcharge")
            {
                __result = KeywordJumpCharge(ref strInput);
                return false;
            }

            if (args.Length == 2 && args[0].ToLower() == "help" && args[1] == "jumpcharge")
            {
                strInput += "\nplays the jump drive charge effect on the current ship's field contour, run it again while it plays to fade it out, and once more to stop it at once";
                __result = true;
                return false;
            }

            if (args[0].ToLower() == "jumpflash")
            {
                bool played = CrewSim.objInstance != null && FzJumpFlash.Play(CrewSim.shipCurrentLoaded);
                strInput += played ? "\nJump flash played." : "\nNo contour, it needs at least 3 working emitters and does not run on stations.";
                __result = played;
                return false;
            }

            if (args.Length == 2 && args[0].ToLower() == "help" && args[1] == "jumpflash")
            {
                strInput += "\nplays the jump flash on its own, it also plays at the end of a completed jumpcharge";
                __result = true;
                return false;
            }

            if (args.Length == 2 && args[0].ToLower() == "help" && args[1] == "fieldcontour")
            {
                strInput += "\ntoggles the jump field contour overlay on the current ship or station";
                __result = true;
                return false;
            }

            if (args.Length == 2 && args[0].ToLower() == "help" && args[1] == "restock")
            {
                strInput += "\nforces kiosks to restock on the next tick while the game is unpaused";
                strInput += "\n'restock' restocks every kiosk on the current ship or station";
                strInput += "\n'restock [them]' restocks the kiosk selected in the tooltip";
                __result = true;
                return false;
            }

            return true;
        }

        [HarmonyPatch("KeywordHelp")]
        [HarmonyPostfix()]
        public static void KeywordHelp_Postfix(ref string strInput, string[] strings)
        {
            if (strings.Length == 1) strInput = strInput.Replace("\nrename\n", "\nrename\nrestock\n").Replace("\nhelp\n", "\nfieldcontour\nfieldremove\nnavmoddump\ntapstatus\nhelp\n").Replace("\nkill\n", "\njumpcharge\njumpflash\nkill\n");
        }

        private static bool KeywordNavModDump(ref string strInput, string[] args)
        {
            string name = args.Length > 1 ? args[1] : "NavModSensorsMFD";
            GameObject prefab = Resources.Load<GameObject>("GUIShip/GUIOrbitDraw/" + name);
            if (prefab == null)
            {
                strInput += "\nNo nav module prefab named " + name;
                return false;
            }

            DumpObject(prefab.transform, 0);
            strInput += "\nWrote " + name + " to the log.";
            return true;
        }

        private static void DumpObject(Transform transform, int depth)
        {
            RectTransform rect = transform as RectTransform;
            string layout = rect != null ? $"anchors {rect.anchorMin}-{rect.anchorMax} offsets {rect.offsetMin}-{rect.offsetMax}" : "";
            FzAdditions.Logger.LogInfo($"{new string(' ', depth * 2)}{transform.name} active={transform.gameObject.activeSelf} {layout}");

            foreach (Component component in transform.GetComponents<Component>())
            {
                string detail = component switch
                {
                    Image image => $" sprite={image.sprite?.name} color={image.color} type={image.type}",
                    TMP_Text text => $" text='{text.text}' font={text.font?.name} size={text.fontSize} color={text.color}",
                    _ => ""
                };
                FzAdditions.Logger.LogInfo($"{new string(' ', depth * 2)}  [{component?.GetType().Name}]{detail}");
            }

            foreach (Transform child in transform) DumpObject(child, depth + 1);
        }

        private static bool KeywordTapStatus(ref string strInput)
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

        private static bool KeywordFieldContour(ref string strInput)
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
                strInput += "\nUsage: fieldremove or fieldremove confirm";
                return false;
            }

            if (FieldContour.Calculate(ship).Count == 0)
            {
                strInput += "\nNo contour, it needs at least 3 working emitters and does not run on stations.";
                return false;
            }

            if (args.Length == 1)
            {
                strInput += $"\n{FieldContour.FindOutside(ship).Count} object(s) are outside the contour, {FieldContour.FindIntersecting(ship).Count} are crossing it, {FieldContour.FindShipsToUndock(ship).Count} docked ship(s) would undock.";
                strInput += "\nRun 'fieldremove confirm' to destroy them. This cannot be undone.";
                return true;
            }

            int removed = FieldContour.RemoveOutside(ship, out int lost, out int damaged, out int undocked);
            strInput += $"\nRemoved {removed} object(s) outside the contour and broke {damaged} crossing it, undocked {undocked} ship(s), {lost} character(s) are falling away.";
            return true;
        }

        private static bool KeywordJumpCharge(ref string strInput)
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

        private static bool KeywordRestock(ref string strInput, string[] args)
        {
            if (CrewSim.objInstance == null)
            {
                strInput += "\nCrewSim instance not found.";
                return false;
            }

            if (args.Length > 2 || (args.Length == 2 && args[1] != "[them]"))
            {
                strInput += "\nUsage: restock or restock [them]";
                return false;
            }

            List<CondOwner> kiosks = new List<CondOwner>();
            if (args.Length == 2)
            {
                CondOwner selected = GUIMegaToolTip.Selected;
                if (selected == null)
                {
                    strInput += "\nNo target selected for [them].";
                    return false;
                }
                if (IsKiosk(selected)) kiosks.Add(selected);
            }
            else if (CrewSim.shipCurrentLoaded != null)
            {
                kiosks.AddRange(CrewSim.shipCurrentLoaded.GetCOs(null, bSubObjects: true, bAllowDocked: true, bAllowLocked: true).Where(IsKiosk));
            }

            if (kiosks.Count == 0)
            {
                strInput += "\nNo kiosks found.";
                return false;
            }

            foreach (CondOwner kiosk in kiosks) kiosk.AddCondAmount("ForceRestock", 1.0);

            strInput += $"\nQueued restock for {kiosks.Count} kiosk(s).";
            return true;
        }

        //Human traders also carry a Trader component
        private static bool IsKiosk(CondOwner co)
        {
            return co != null && co.GetComponent<Trader>() != null && !co.HasCond("IsHuman");
        }

    }
}
