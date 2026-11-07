using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine.UI;

namespace Freznel.FzAdditions.Patches
{
    //The breach spike (ItmFzBreachSpike), per the user: it lets a character without SkillHacking hack a powered PIN lock,
    //four times slower than the skill, and speeds up a character who has the skill. Each hack it helps with uses one of
    //its StatFzBreachUses (10 when new), and it burns out at none.
    //
    //The game's lock panel (GUILocks) only starts a hack for SkillHacking: TryHack checks it, then HackingActive, run
    //every frame while hacking, scrambles the shown code towards the real one. Without the skill, TryHack here does what
    //the game's does (the standing cost included) and starts the hack anyway, and HackingActive runs one frame in
    //SpikeSlowdown. With the skill, the game's TryHack runs, and HackingActive runs SkillBoost times a frame.
    [HarmonyPatch(typeof(GUILocks))]
    public static class BreachSpikePatch
    {
        public const int SpikeSlowdown = 4;
        public const int SkillBoost = 2;
        public const string UsesCond = "StatFzBreachUses";

        private enum Mode { Unskilled, Boosted }

        private static readonly AccessTools.FieldRef<GUILocks, bool> HackingEnabled = AccessTools.FieldRefAccess<GUILocks, bool>("bHackingEnabled");
        private static readonly MethodInfo HackingActiveMethod = AccessTools.Method(typeof(GUILocks), "HackingActive");
        private static readonly ConditionalWeakTable<GUILocks, object> modes = new ConditionalWeakTable<GUILocks, object>();
        private static CondTrigger spikeTrigger;
        private static bool boosting;

        //A working breach spike the character carries, or null
        public static CondOwner Spike(CondOwner co)
        {
            if (co == null) return null;
            if (spikeTrigger == null) spikeTrigger = DataHandler.GetCondTrigger("TIsFzBreachSpike");
            if (spikeTrigger == null) return null;
            return co.GetCOs(false, spikeTrigger).FirstOrDefault(s => s != null && !s.bDestroyed && s.GetCondAmount(UsesCond) > 0.0);
        }

        //Uses one of a spike's jobs, and burns it out when that was its last
        public static void UseOne(CondOwner spike, CondOwner crew)
        {
            if (spike == null) return;
            double left = Math.Max(0.0, spike.GetCondAmount(UsesCond) - 1.0);
            spike.SetCondAmount(UsesCond, left);
            if (left > 0.0) return;
            crew?.LogMessage("The breach spike's cartridge burns out. It's scrap now.", "Bad", crew.strName);
            spike.RemoveFromCurrentHome(bForce: true);
            spike.Destroy();
        }

        [HarmonyPatch("TryHack")]
        [HarmonyPrefix()]
        public static bool TryHack_Prefix(GUILocks __instance, GUIContextButton gcb)
        {
            try
            {
                modes.Remove(__instance);
                CondOwner crew = CrewSim.GetSelectedCrew();
                CondOwner spike = Spike(crew);
                if (crew == null || spike == null) return true;
                Toggle toggle = gcb.GetComponent<Toggle>();
                if (!toggle.isOn || __instance.COSelf.HasCond("IsOff")) return true;

                if (crew.HasCond("SkillHacking"))
                {
                    //The game's own hack, sped up once it starts (TryHack_Postfix)
                    modes.Add(__instance, Mode.Boosted);
                    return true;
                }

                Interaction penalty = DataHandler.GetInteraction("HackingCrimePenalty");
                if (penalty != null)
                {
                    penalty.objUs = crew;
                    penalty.objThem = __instance.COSelf;
                    penalty.ApplyChain();
                }
                toggle.isOn = false;
                HackingEnabled(__instance) = true;
                modes.Add(__instance, Mode.Unskilled);
                UseOne(spike, crew);
                crew.LogMessage("The breach spike starts working the lock. It's slower than knowing how.", "Neutral", crew.strName);
                return false;
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Hacking a lock with a breach spike failed: " + ex);
                return true;
            }
        }

        //A skilled hack the game started uses one of the spike's jobs
        [HarmonyPatch("TryHack")]
        [HarmonyPostfix()]
        public static void TryHack_Postfix(GUILocks __instance)
        {
            if (!modes.TryGetValue(__instance, out object mode) || (Mode)mode != Mode.Boosted) return;
            if (!HackingEnabled(__instance))
            {
                modes.Remove(__instance);
                return;
            }
            CondOwner crew = CrewSim.GetSelectedCrew();
            UseOne(Spike(crew), crew);
        }

        [HarmonyPatch("HackingActive")]
        [HarmonyPrefix()]
        public static bool HackingActive_Prefix(GUILocks __instance)
        {
            if (boosting || !modes.TryGetValue(__instance, out object mode) || (Mode)mode != Mode.Unskilled) return true;
            return UnityEngine.Random.Range(0, SpikeSlowdown) == 0;
        }

        [HarmonyPatch("HackingActive")]
        [HarmonyPostfix()]
        public static void HackingActive_Postfix(GUILocks __instance)
        {
            if (boosting || !modes.TryGetValue(__instance, out object mode) || (Mode)mode != Mode.Boosted) return;
            boosting = true;
            try
            {
                for (int i = 1; i < SkillBoost && HackingEnabled(__instance); i++) HackingActiveMethod.Invoke(__instance, null);
            }
            finally
            {
                boosting = false;
            }
        }

        //A panel opened on another lock starts fresh
        [HarmonyPatch(nameof(GUILocks.Init))]
        [HarmonyPostfix()]
        public static void Init_Postfix(GUILocks __instance)
        {
            modes.Remove(__instance);
        }
    }
}
