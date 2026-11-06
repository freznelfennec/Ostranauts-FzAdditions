using Freznel.FzAdditions.Util;
using HarmonyLib;

namespace Freznel.FzAdditions.Patches
{
    //An item a recycler is breaking down carries IsFzRecycleLocked, and one a fabricator is building or building onto
    //IsFzFabricateLocked (Locks.IsLocked), and nothing may touch it: no interaction can target
    //it, so the player and NPCs can't pick it up, open it or use it, and no condition trigger matches it, so jobs looking
    //for items, such as a repair wanting scrap steel, pass it over.
    [HarmonyPatch]
    public static class RecycleLockPatch
    {
        //Every interaction test, the ordinary and the deferred, starts here
        [HarmonyPatch(typeof(Interaction), "ValidateParticipants")]
        [HarmonyPostfix()]
        public static void ValidateParticipants_Postfix(CondOwner us, CondOwner them, ref bool __result)
        {
            if (__result && them != null && them != us && Locks.IsLocked(them)) __result = false;
        }

        [HarmonyPatch(typeof(CondTrigger), nameof(CondTrigger.Triggered), typeof(CondOwner), typeof(string), typeof(bool))]
        [HarmonyPrefix()]
        public static bool Triggered_Prefix(CondOwner objOwner, ref bool __result)
        {
            if (objOwner == null || !Locks.IsLocked(objOwner)) return true;
            __result = false;
            return false;
        }
    }
}
