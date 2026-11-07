using BepInEx.Configuration;
using Freznel.FzAdditions.Patches;
using Freznel.FzAdditions.UI;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    //Several navmod layouts on each nav console, switched with tabs on its frame or Ctrl+1 to 9. The game keeps one
    //layout in the nav station's NavModConfig prop map. The panels are copies of it in the FzNavPanels prop map beside
    //it, and NavModConfig always holds the one showing, so the game, other mods, and the console with this turned off
    //all see an ordinary layout. Both prop maps are on the nav station, so the panels and the one chosen are saved.
    //A setting turns it off; its patches have no Harmony attributes and only go on when it's enabled.
    public static class NavPanels
    {
        public const int MaxPanels = 9;

        private const string HarmonyID = PluginInfo.PLUGIN_GUID + ".navpanels";
        private const string PropMap = "FzNavPanels";
        private const string ConfigMap = "NavModConfig"; //The game's own, the layout showing
        private const string CountKey = "nCount";
        private const string SelectedKey = "nSelected";
        private const string LayoutKey = "strLayout"; //With the panel's index on the end, "name=x|y|x|y;..." for each placed navmod

        //Private to the game, a game update could rename them
        private static readonly FieldInfo EditModeField = AccessTools.Field(typeof(GUIOrbitDraw), "_editModeActive");
        private static readonly FieldInfo NavModsField = AccessTools.Field(typeof(GUIOrbitDraw), "_navMods");
        private static readonly MethodInfo LoadModulesMethod = AccessTools.Method(typeof(GUIOrbitDraw), "LoadModules");
        private static readonly MethodInfo SaveModulesMethod = AccessTools.Method(typeof(GUIOrbitDraw), "SaveModules");
        private static readonly MethodInfo InitMethod = AccessTools.Method(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.Init), new[] { typeof(CondOwner), typeof(Dictionary<string, string>), typeof(string) });
        private static readonly MethodInfo CrewSwitchMethod = AccessTools.Method(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.CrewSwitch));

        public static bool Enabled { get; private set; }

        public static void Init(ConfigFile config)
        {
            bool wanted = config.Bind("NavConsole", "Panels", true,
                "Several navmod layouts on each nav console, switched with numbered tabs on its frame or Ctrl+1 to 9. " +
                "Turn it off if another mod changes the nav console's modules; each console keeps the panel last shown. " +
                "Takes effect when the game restarts.").Value;
            if (!wanted) return;

            object[] needed = { InitMethod, CrewSwitchMethod, SaveModulesMethod, LoadModulesMethod, EditModeField, NavModsField };
            if (needed.Any(member => member == null))
            {
                FzAdditions.Logger.LogWarning("Nav console panels are off, the nav console no longer has what they need");
                return;
            }

            Harmony harmony = new Harmony(HarmonyID);
            harmony.Patch(InitMethod, postfix: new HarmonyMethod(typeof(NavPanelsPatch), nameof(NavPanelsPatch.Opened_Postfix)));
            harmony.Patch(CrewSwitchMethod, postfix: new HarmonyMethod(typeof(NavPanelsPatch), nameof(NavPanelsPatch.Opened_Postfix)));
            harmony.Patch(SaveModulesMethod, postfix: new HarmonyMethod(typeof(NavPanelsPatch), nameof(NavPanelsPatch.SaveModules_Postfix)));
            Enabled = true;
        }

        //The console has opened on a nav station, or someone else has taken it over. What it shows is the chosen panel,
        //as it may have been changed with this off.
        public static void Opened(GUIOrbitDraw orbit)
        {
            bool shown = !orbit.IsPDANav && orbit.COSelf != null;
            if (shown) StoreShowing(orbit.COSelf);
            FzNavPanelTabs.Show(orbit, shown);
        }

        //After the console's edit mode has saved the layout
        public static void StoreShowing(CondOwner nav)
        {
            Dictionary<string, string> panels = GetPanels(nav);
            if (panels == null) return;
            panels[LayoutKey + GetSelected(nav)] = Serialize(GetConfig(nav));
        }

        public static int GetCount(CondOwner nav)
        {
            Dictionary<string, string> panels = GetPanels(nav);
            return panels != null && int.TryParse(panels.TryGetValue(CountKey, out string count) ? count : null, out int value) ? Math.Max(1, Math.Min(value, MaxPanels)) : 1;
        }

        public static int GetSelected(CondOwner nav)
        {
            Dictionary<string, string> panels = GetPanels(nav);
            int selected = panels != null && int.TryParse(panels.TryGetValue(SelectedKey, out string value) ? value : null, out int index) ? index : 0;
            return Math.Max(0, Math.Min(selected, GetCount(nav) - 1));
        }

        //Panels can't change while the console is being rearranged. In ship edit the console makes throwaway copies of
        //every navmod each time it lays them out, so they don't change there either.
        public static bool CanChange(GUIOrbitDraw orbit)
        {
            return orbit != null && orbit.COSelf != null && !orbit.IsPDANav && !CrewSim.bShipEdit && !CrewSim.bDebugFightMode
                && !(bool)EditModeField.GetValue(orbit);
        }

        public static bool Select(GUIOrbitDraw orbit, int index)
        {
            CondOwner nav = orbit?.COSelf;
            if (!CanChange(orbit) || index < 0 || index >= GetCount(nav) || index == GetSelected(nav)) return false;

            StoreShowing(nav);
            GetPanels(nav)[SelectedKey] = index.ToString(CultureInfo.InvariantCulture);
            Show(orbit, GetPanels(nav).TryGetValue(LayoutKey + index, out string layout) ? layout : "");
            return true;
        }

        //A new panel starts as a copy of the one showing, to rearrange from there
        public static bool Add(GUIOrbitDraw orbit)
        {
            CondOwner nav = orbit?.COSelf;
            int count = GetCount(nav);
            if (!CanChange(orbit) || count >= MaxPanels) return false;

            StoreShowing(nav);
            Dictionary<string, string> panels = GetPanels(nav);
            panels[LayoutKey + count] = Serialize(GetConfig(nav));
            panels[CountKey] = (count + 1).ToString(CultureInfo.InvariantCulture);
            panels[SelectedKey] = count.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        //Removes the panel showing and shows the one before it. The last panel can't go.
        public static bool Remove(GUIOrbitDraw orbit)
        {
            CondOwner nav = orbit?.COSelf;
            int count = GetCount(nav);
            if (!CanChange(orbit) || count <= 1) return false;

            Dictionary<string, string> panels = GetPanels(nav);
            int removed = GetSelected(nav);
            for (int i = removed; i < count - 1; i++)
            {
                panels[LayoutKey + i] = panels.TryGetValue(LayoutKey + (i + 1), out string next) ? next : "";
            }
            panels.Remove(LayoutKey + (count - 1));
            panels[CountKey] = (count - 1).ToString(CultureInfo.InvariantCulture);

            int shown = Math.Max(0, removed - 1);
            panels[SelectedKey] = shown.ToString(CultureInfo.InvariantCulture);
            Show(orbit, panels.TryGetValue(LayoutKey + shown, out string layout) ? layout : "");
            return true;
        }

        //Puts the layout in NavModConfig and has the console lay its modules out again from it, the way it does when
        //someone else sits down at it
        private static void Show(GUIOrbitDraw orbit, string layout)
        {
            CondOwner nav = orbit.COSelf;
            Dictionary<string, string> config = GetConfig(nav);
            foreach (string key in config.Keys.ToList()) config[key] = "";
            foreach (KeyValuePair<string, string> entry in Deserialize(layout)) config[entry.Key] = entry.Value;

            foreach (NavModBase mod in orbit.GetComponentsInChildren<NavModBase>()) mod.DisableMod();
            ((Dictionary<string, CondOwner>)NavModsField.GetValue(orbit)).Clear();
            LoadModulesMethod.Invoke(orbit, new object[] { nav });
        }

        //Made from the layout showing the first time the station is used with this on
        private static Dictionary<string, string> GetPanels(CondOwner nav)
        {
            if (nav?.mapGUIPropMaps == null) return null;
            if (!nav.mapGUIPropMaps.TryGetValue(PropMap, out Dictionary<string, string> panels) || panels == null)
            {
                panels = new Dictionary<string, string>
                {
                    [CountKey] = "1",
                    [SelectedKey] = "0",
                    [LayoutKey + "0"] = Serialize(GetConfig(nav))
                };
                nav.mapGUIPropMaps[PropMap] = panels;
            }
            return panels;
        }

        private static Dictionary<string, string> GetConfig(CondOwner nav)
        {
            if (!nav.mapGUIPropMaps.TryGetValue(ConfigMap, out Dictionary<string, string> config) || config == null)
            {
                config = new Dictionary<string, string>();
                nav.mapGUIPropMaps[ConfigMap] = config;
            }
            return config;
        }

        //Only the navmods placed, an empty value means one isn't
        private static string Serialize(Dictionary<string, string> config)
        {
            return string.Join(";", config.Where(entry => !string.IsNullOrEmpty(entry.Value)).Select(entry => entry.Key + "=" + entry.Value));
        }

        private static IEnumerable<KeyValuePair<string, string>> Deserialize(string layout)
        {
            if (string.IsNullOrEmpty(layout)) yield break;
            foreach (string entry in layout.Split(';'))
            {
                int split = entry.IndexOf('=');
                if (split > 0) yield return new KeyValuePair<string, string>(entry.Substring(0, split), entry.Substring(split + 1));
            }
        }
    }
}
