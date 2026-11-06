using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //What the profiler times: every per-frame Unity method in the game and in this mod, and the simulation's own
    //steps, which run inside StarSystem.Update and the scheduler rather than in a Unity method of their own. The
    //list takes in what https://github.com/themaoci/Ostronauts-Performance-Optimisations found worth optimising.
    public static class ProfileTargets
    {
        private static readonly string[] FrameMethods = { "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnPreRender", "OnRenderImage", "OnPostRender" };

        //Type, then method, every overload of it
        private static readonly string[][] Simulation =
        {
            new[] { "StarSystem", "Update" },
            new[] { "StarSystem", "UpdateShip" },
            new[] { "StarSystem", "UpdateDockingGroups" },
            new[] { "Ship", "UpdateCrewSkills" },
            new[] { "Ship", "DamageOverTime" },
            new[] { "Ship", "Sparks" },
            new[] { "Ship", "CreateRooms" },
            new[] { "Ship", "UpdatePower" },
            new[] { "Ship", "CheckLocks" },
            new[] { "Ship", "UpdateSensors" },
            new[] { "Ship", "CheckTargets" },
            new[] { "ShipSitu", "TimeAdvance" },
            new[] { "AIShipManager", "Update" },
            new[] { "AIShipManager", "FFWD" },
            new[] { "Ostranauts.Trading.MarketManager", "Run" },
            new[] { "CollisionManager", "CheckCollisions" },
            new[] { "CollisionManager", "CheckProjectileCollisions" },
            new[] { "CollisionManager", "RunQueue" },
            new[] { "StarSystem", "DeliverMessages" },
            new[] { "Ship", "VisitCOs" },
            new[] { "Crew", "CheckLightVisibility" },
            new[] { "Visibility", "NotifyAllPreRender" },
            new[] { "Item", "NotifyPreRender" },

            //The scheduler runs every condition owner's turn, its steps were CrewSim.AdvanceSim and UpdateICOs in older versions
            new[] { "Condowner.Utils.Scheduler", "Run" },
            new[] { "Condowner.Utils.Scheduler", "AdvanceSim" },
            new[] { "Condowner.Utils.Scheduler", "ProcessUrgentInteractions" },
            new[] { "Condowner.Utils.Scheduler", "DrainPriorityCredits" },
            new[] { "Condowner.Utils.Scheduler", "UpdateClockRates" },
            new[] { "Condowner.Utils.Scheduler", "ProcessEndTurnTicks" },
            new[] { "Condowner.Utils.Scheduler", "FinishCompletedEndTurn" },
            new[] { "Condowner.Utils.Scheduler", "UpdateICOs" },
            new[] { "Condowner.Utils.Scheduler", "UpdateTickerOrder" },
            new[] { "Condowner.Utils.Scheduler", "RefreshAISamples" },

            //A condition owner's turn and the AI work inside it
            new[] { "CondOwner", "UpdateManualDeferred" },
            new[] { "CondOwner", "EndTurn" },
            new[] { "CondOwner", "UpdateStats" },
            new[] { "CondOwner", "Cleanup" },
            new[] { "CondOwner", "GetMove2" },
            new[] { "CondOwner", "GetWork" },
            new[] { "CondOwner", "GetCOScore" },
            new[] { "CondOwner", "ParseCondLoot" },
            new[] { "CondOwner", "AIIssueOrder" },
            new[] { "CondOwner", "AICancelAll" },
            new[] { "CrewSim", "GetAvailActionsForCO" },
            new[] { "DataHandler", "GetInteraction" },

            //Called often enough to allocate a lot, found by the Ostronauts Performance Optimisations mod
            new[] { "Ostranauts.Core.LogHandler", "IsDuplicate" },
            new[] { "Ostranauts.Core.LogHandler", "TrimLog" },
            new[] { "Ostranauts.Core.InteractionObjectTracker", "RemoveNullsFromDictionary" },
            new[] { "Ostranauts.InputControl.InputManager", "GetControllerType" },

            //Saving runs on the main thread and shows up as a single long frame
            new[] { "Ostranauts.Core.LoadManager", "SaveGame" },
            new[] { "Ostranauts.Core.LoadManager", "SaveScreenShot" },
            new[] { "Ostranauts.Core.LoadManager", "SaveCrewPortraits" },

            //Each log call formats a string and writes it out, the game makes a lot of them
            new[] { "UnityEngine.Debug", "Log" },
            new[] { "UnityEngine.Debug", "LogFormat" },
            new[] { "UnityEngine.Debug", "LogWarning" },
            new[] { "UnityEngine.Debug", "LogError" }
        };

        public static IEnumerable<MethodBase> GetTargets()
        {
            return GetFrameMethods().Concat(GetSimulationMethods()).Distinct();
        }

        public static string GetName(MethodBase method)
        {
            string name = $"{method.DeclaringType?.FullName}.{method.Name}";
            int overloads = method.DeclaringType?.GetMethods(AccessTools.all).Count(other => other.Name == method.Name) ?? 1;
            return overloads > 1 ? $"{name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})" : name;
        }

        //The game's and this mod's MonoBehaviours, leaving out the profiler's own frame counter
        private static IEnumerable<MethodBase> GetFrameMethods()
        {
            foreach (Assembly assembly in new[] { typeof(CrewSim).Assembly, typeof(FzAdditions).Assembly })
            {
                foreach (Type type in GetTypes(assembly))
                {
                    if (type == null || type.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(type) || type.DeclaringType == typeof(FzProfiler)) continue;
                    foreach (string name in FrameMethods)
                    {
                        MethodInfo method = AccessTools.DeclaredMethod(type, name, Type.EmptyTypes);
                        if (IsPatchable(method)) yield return method;
                    }
                }
            }
        }

        private static IEnumerable<MethodBase> GetSimulationMethods()
        {
            foreach (string[] target in Simulation)
            {
                Type type = AccessTools.TypeByName(target[0]);
                if (type == null)
                {
                    FzAdditions.Logger.LogWarning($"Profiler: no type {target[0]}");
                    continue;
                }

                List<MethodInfo> methods = AccessTools.GetDeclaredMethods(type).Where(method => method.Name == target[1] && IsPatchable(method)).ToList();
                if (methods.Count == 0) FzAdditions.Logger.LogWarning($"Profiler: no method {target[0]}.{target[1]}");
                foreach (MethodInfo method in methods) yield return method;
            }
        }

        private static bool IsPatchable(MethodInfo method)
        {
            return method != null && !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null;
        }

        //A type that can't load leaves a gap, the rest are still timed
        private static IEnumerable<Type> GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }
    }
}
