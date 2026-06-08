using Freznel.FzAdditions.UI.CustomPrefabs;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.Patches
{
    [HarmonyPatch]
    public static class GUIPrefabPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var result = new List<MethodBase>
            {
                typeof(CrewSim).GetMethod("RaiseUI", [typeof(string), typeof(CondOwner)])
            };
            return result;
        }

        static GameObject HandlePrefab(string prefabName)
        {
            FzAdditions.Logger.LogInfo("Intercepting call to UnityEngine.Resources.Load() for " + prefabName);

            if (prefabName.StartsWith("GUIShip/Fz"))
            {
                string customPrefabName = prefabName.Substring(8);
                if (CustomPrefabRegistry.Prefabs.ContainsKey(customPrefabName))
                {
                    FzAdditions.Logger.LogInfo("Found custom GUI prefab " + customPrefabName);
                    return CustomPrefabRegistry.Prefabs[customPrefabName];
                }
                else
                {
                    FzAdditions.Logger.LogError("Could not find custom GUI prefab " + customPrefabName);
                }
            }

            if (prefabName == "GUIShip/FzTestGUI") return CustomPrefabRegistry.Prefabs["TestGUI"];

            return UnityEngine.Resources.Load<GameObject>(prefabName);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FzAdditions.Logger.LogInfo("Applying UI prefab transpiler patch");
            MethodInfo UnityEngineResourceLoadMethod = typeof(UnityEngine.Resources).GetMethods().FirstOrDefault(
                x => x.Name.Equals("Load", StringComparison.OrdinalIgnoreCase) && x.IsGenericMethod && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(string))
                ?.MakeGenericMethod(typeof(GameObject));
            
            if (UnityEngineResourceLoadMethod == null) throw new Exception("Failed to find UnityEngine.Resources.Load<GameObject>(string)");

            return new CodeMatcher(instructions).Start()
                .SearchForward((i) => i.Calls(UnityEngineResourceLoadMethod))
                .ThrowIfInvalid("Could not find call to UnityEngine.Resources.Load()")
                .RemoveInstruction()
                .InsertAndAdvance(CodeInstruction.Call(() => HandlePrefab(default)))
                .Instructions();
        }

    }
}