using BepInEx;
using BepInEx.Logging;
using Freznel.FzAdditions.VM;
using Freznel.FzAdditions.VM.Execution;
using Freznel.FzAdditions.VM.Objects;
using Freznel.FzAdditions.VM.Objects.Frame;
using Freznel.FzAdditions.VM.Objects.Operator;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

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

            Logger.LogInfo("Attaching last chance exception handler");
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex == null) return;
                File.WriteAllText("C:/crash.txt", $"Unhandled Exception: {ex.GetType().Name}: ${ex.Message}\n{ex.StackTrace}");
            };

            var vm = new VirtualMachine();
            vm.PushFrame(new ExecuteListFrame(new List<VMObject>() { new NumberObject(1), new NumberObject(2), new BinaryOperatorObject(VM.Enum.BinaryOperator.Add) }));
            vm.Run(1000);
            Logger.LogInfo($"\n-- Top Of Stack --\n{string.Join('\n', vm.EnumerateOperands().Select(o => o.ToString()))}\n-- Bottom Of Stack --");
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


