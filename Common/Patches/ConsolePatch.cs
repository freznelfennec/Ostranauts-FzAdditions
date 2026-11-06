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

    //The mod's console commands, all under one root command so they don't clutter the game's list: 'fz <command>'.
    //Each section adds its own with AddCommand when the plugin loads. A command's handler sees its arguments with
    //its own name first, as if it had been typed on its own.
    [HarmonyPatch(typeof(ConsoleResolver))]
    public static class ConsolePatch
    {
        public const string Root = "fz";
        private const string ListBefore = "getcond"; //The game's command 'fz' is listed ahead of in help, which is alphabetical

        public delegate bool Handler(ref string strInput, string[] args);

        private class Command
        {
            public Handler Run;
            public string[] Help;
        }

        private static readonly Dictionary<string, Command> Commands = new Dictionary<string, Command>();

        static ConsolePatch()
        {
            AddCommand("restock", KeywordRestock,
                "forces kiosks to restock on the next tick while the game is unpaused",
                "'fz restock' restocks every kiosk on the current ship or station",
                "'fz restock [them]' restocks the kiosk selected in the tooltip");
            AddCommand("navmoddump", KeywordNavModDump,
                "writes the layout of a nav module's prefab to the log",
                "'fz navmoddump NavModSensorsMFD' dumps that module");
        }

        //The first help line is the summary 'fz' lists the command with
        public static void AddCommand(string keyword, Handler run, params string[] help)
        {
            Commands[keyword.ToLower()] = new Command { Run = run, Help = help };
        }

        [HarmonyPatch(nameof(ConsoleResolver.ResolveString))]
        [HarmonyPrefix()]
        public static bool ResolveString_Prefix(ref string strInput, ref bool __result)
        {
            string[] args = strInput.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0) return true;
            string keyword = args[0].ToLower();

            //'help fz' and 'help fz <command>' from the game's own help
            if (keyword == "help" && args.Length >= 2 && args[1].ToLower() == Root)
            {
                __result = ShowHelp(ref strInput, args.Length > 2 ? args[2] : null);
                return false;
            }
            if (keyword != Root) return true;

            //'fz', 'fz help' and 'fz help <command>'
            if (args.Length == 1 || args[1].ToLower() == "help")
            {
                __result = ShowHelp(ref strInput, args.Length > 2 ? args[2] : null);
                return false;
            }

            if (!Commands.TryGetValue(args[1].ToLower(), out Command command))
            {
                strInput += $"\nNo {Root} command named {args[1]}.";
                ShowHelp(ref strInput, null);
                __result = false;
                return false;
            }

            try
            {
                __result = command.Run(ref strInput, args.Skip(1).ToArray());
            }
            catch (Exception ex)
            {
                strInput += $"\n{Root} {args[1]} failed: {ex.Message}";
                FzAdditions.Logger.LogError($"Console command '{strInput}' failed: {ex}");
                __result = false;
            }
            return false;
        }

        //Every command with its summary, or one command's full help
        private static bool ShowHelp(ref string strInput, string name)
        {
            if (name == null)
            {
                strInput += $"\nFreznel's Additions commands, run as '{Root} <command>'. '{Root} help <command>' explains one.";
                foreach (var command in Commands.OrderBy(c => c.Key)) strInput += $"\n  {command.Key}: {command.Value.Help.FirstOrDefault()}";
                return true;
            }
            if (!Commands.TryGetValue(name.ToLower(), out Command found))
            {
                strInput += $"\nNo {Root} command named {name}.";
                return false;
            }
            foreach (string line in found.Help) strInput += "\n" + line;
            return true;
        }

        [HarmonyPatch("KeywordHelp")]
        [HarmonyPostfix()]
        public static void KeywordHelp_Postfix(ref string strInput, string[] strings)
        {
            if (strings.Length == 1) strInput = strInput.Replace($"\n{ListBefore}\n", $"\n{Root}\n{ListBefore}\n");
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

        private static bool KeywordRestock(ref string strInput, string[] args)
        {
            if (CrewSim.objInstance == null)
            {
                strInput += "\nCrewSim instance not found.";
                return false;
            }

            if (args.Length > 2 || (args.Length == 2 && args[1] != "[them]"))
            {
                strInput += "\nUsage: fz restock or fz restock [them]";
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
