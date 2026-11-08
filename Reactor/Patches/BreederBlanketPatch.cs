using Freznel.FzAdditions.UI;
using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.UI.MegaToolTip;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Tier = Freznel.FzAdditions.Util.BreederBlanket.Tier;

namespace Freznel.FzAdditions.Patches
{
    //Fitting, removing and dropping breeder blankets, and where they turn up
    [HarmonyPatch]
    public static class BreederBlanketPatch
    {
        private const float LooseChance = 0.1f; //Of derelicts, a loose Basic blanket on a floor
        private const float FittedChance = 0.25f; //Of derelicts with a fusion reactor, a Basic blanket fitted to it
        private const float MaxDerelictConverted = 0.4f; //A derelict's blanket starts this much converted at most
        private const float Clearance = 0.4f; //Tiles from anything but a floor at the same spot

        //The installed fusion reactor COs, which get the fit and remove interactions. Found from the data each time
        //it loads.
        private static HashSet<string> reactorCOs = new HashSet<string>();

        private static readonly AccessTools.FieldRef<ModuleHost, GameObject[]> ItemModules = AccessTools.FieldRefAccess<ModuleHost, GameObject[]>("_itemModulePrefabs");

        //Every installed form of every fusion reactor, the station uplink aside, can have a blanket fitted
        [HarmonyPatch(typeof(DataHandler), nameof(DataHandler.AllPostLoadAsync))]
        [HarmonyPostfix()]
        public static void AllPostLoadAsync_Postfix()
        {
            try
            {
                reactorCOs = new HashSet<string>(DataHandler.dictCOs.Values
                    .Where(co => co.strName != null && co.strName.StartsWith("ItmReactorIC") && !co.strName.Contains("Mini")
                        && co.aStartingConds != null && co.aStartingConds.Any(c => c.StartsWith("IsInstalled=")))
                    .Select(co => co.strName));
                foreach (string name in reactorCOs)
                {
                    JsonCondOwner co = DataHandler.dictCOs[name];
                    var interactions = (co.aInteractions ?? new string[0]).ToList();
                    foreach (string added in new[] { ReactorConstants.FitInteraction, ReactorConstants.RemoveInteraction })
                        if (!interactions.Contains(added)) interactions.Add(added);
                    co.aInteractions = interactions.ToArray();
                }
                FzAdditions.Logger.LogInfo($"Breeder blankets fit {reactorCOs.Count} reactor forms: {string.Join(", ", reactorCOs)}");
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Adding breeder blanket interactions to reactors failed: " + ex);
            }
        }

        //The fit interaction fetches a blanket and destroys it as its effects apply, so its state is taken first.
        //Removing makes the item. A reactor dismantled with a blanket on gives it up first.
        [HarmonyPatch(typeof(Interaction), nameof(Interaction.ApplyEffects))]
        [HarmonyPrefix()]
        public static void ApplyEffects_Prefix(Interaction __instance, bool isCancelIa)
        {
            if (isCancelIa) return;
            try
            {
                CondOwner us = __instance.objUs;
                CondOwner them = __instance.objThem;
                if (__instance.strName == ReactorConstants.FitInteraction)
                {
                    CondOwner blanket = __instance.aLootItemRemoveContract?.FirstOrDefault(BreederBlanket.IsBlanket);
                    if (blanket != null) BreederBlanket.Fit(them, blanket);
                }
                else if (__instance.strName == ReactorConstants.RemoveInteraction || (__instance.strName != null && __instance.strName.Contains("Dismantle") && BreederBlanket.HasBlanket(them)))
                {
                    if (them != null && us != null) BreederBlanket.Remove(them, them.ship ?? us.ship, us.tf.position);
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Breeder blanket {__instance.strName} failed: {ex}");
            }
        }

        //Uninstalling a reactor drops its blanket where it stands. Its conditions came across to the loose form.
        [HarmonyPatch(typeof(CondOwner), "ModeSwitchInternal")]
        [HarmonyPostfix()]
        public static void ModeSwitchInternal_Postfix(CondOwner __instance, CondOwner coNew)
        {
            if (coNew == null || !BreederBlanket.HasBlanket(coNew) || coNew.HasCond("IsInstalled")) return;
            try
            {
                BreederBlanket.Remove(coNew, coNew.ship ?? __instance?.ship, coNew.tf.position);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Dropping a breeder blanket from an uninstalled reactor failed: " + ex);
            }
        }

        //Some derelicts carry a Basic blanket, loose on a floor or fitted to their reactor, part used. A derelict
        //spawns shallow from a clone of its template, so the blanket goes in that clone, with its state as the item's
        //condition overrides, which the game applies as the item spawns.
        [HarmonyPatch(typeof(StarSystem), nameof(StarSystem.AddDerelict))]
        [HarmonyPostfix()]
        public static void AddDerelict_Postfix(Ship __result)
        {
            if (__result?.json?.aItems == null) return;
            try
            {
                Tier basic = BreederBlanket.GetTier(1);
                JsonItem reactor = __result.json.aItems.FirstOrDefault(i => i?.strName != null && reactorCOs.Contains(i.strName));
                if (reactor != null && UnityEngine.Random.value < FittedChance)
                {
                    reactor.SetCondAmount(ReactorConstants.BlanketTierCond, basic.Number);
                    reactor.SetCondAmount(ReactorConstants.BlanketConvertedCond, RandomConverted(basic));
                    FzAdditions.Logger.LogInfo($"Derelict {__result.strRegID}'s reactor has a breeder blanket fitted");
                }
                if (UnityEngine.Random.value < LooseChance) AddLoose(__result, basic);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Adding a breeder blanket to derelict {__result?.strRegID} failed: {ex}");
            }
        }

        private static double RandomConverted(Tier tier) => Math.Round(UnityEngine.Random.Range(0f, MaxDerelictConverted) * tier.Volatiles);

        private static void AddLoose(Ship derelict, Tier tier)
        {
            string co = BreederBlanket.BlanketCO(tier.Number);
            if (!DataHandler.dictCOs.ContainsKey(co)) return;
            JsonItem[] items = derelict.json.aItems;
            List<JsonItem> others = items.Where(i => i != null && i.strName != null && !i.strName.StartsWith("ItmFloor") && string.IsNullOrEmpty(i.strParentID)).ToList();
            List<JsonItem> floors = items.Where(i => i != null && i.strName != null && i.strName.StartsWith("ItmFloor") && string.IsNullOrEmpty(i.strParentID))
                .Where(f => others.All(o => Mathf.Abs(o.fX - f.fX) > Clearance || Mathf.Abs(o.fY - f.fY) > Clearance))
                .ToList();
            if (floors.Count == 0) return;
            JsonItem floor = floors[UnityEngine.Random.Range(0, floors.Count)];
            var blanket = new JsonItem
            {
                strName = co,
                fX = floor.fX,
                fY = floor.fY,
                fRotation = UnityEngine.Random.Range(0, 4) * 90f,
                strID = DataHandler.GetNextID()
            };
            blanket.SetCondAmount(ReactorConstants.BlanketConvertedCond, RandomConverted(tier));
            derelict.json.aItems = items.Concat(new[] { blanket }).ToArray();
            FzAdditions.Logger.LogInfo($"Derelict {derelict.strRegID} carries a loose breeder blanket");
        }

        //The blanket's section goes last in the tooltip, after the show more toggle, like the mod's others
        [HarmonyPatch(typeof(ModuleHost), "Awake")]
        [HarmonyPostfix()]
        public static void Awake_Postfix(ModuleHost __instance)
        {
            ItemModules(__instance) = ItemModules(__instance).Concat(new[] { FzBlanketModule.GetPrefab() }).ToArray();
        }
    }
}
