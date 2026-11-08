using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class FzAdditions : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static ConfigFile Settings; //The mod's settings, BepInEx/config/me.freznel.fzadditions.cfg
        private Harmony _harmony;

        private void Awake()
        {
            Logger = base.Logger;
            Settings = Config;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            // Apply Harmony patches
            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(FzAdditions).Assembly);
            
            Logger.LogInfo("Harmony patches applied successfully!");

            //Each section of the mod hooks itself into the common code
            PropulsionSection.Init();
            ProfilingSection.Init();
            TweaksSection.Init();
            FabricationSection.Init();
            MissionsSection.Init();
            ReactorSection.Init();

            Logger.LogInfo("Attaching last chance exception handler");
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex == null) return;
                Logger.LogFatal($"Unhandled Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            };

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


