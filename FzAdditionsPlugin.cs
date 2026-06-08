using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Freznel.FzAdditions
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class FzAdditions : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private Harmony _harmony;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            // Apply Harmony patches
            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(FzAdditions).Assembly);
            
            Logger.LogInfo("Harmony patches applied successfully!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "me.freznel.fzadditions";
        public const string PLUGIN_NAME = "Freznel's Additions Plugin";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}


