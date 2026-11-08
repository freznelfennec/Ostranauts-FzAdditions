using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Patches
{
    //Some derelicts carry a broken Sideband Interceptor, so players can find one, repair it or learn its blueprint,
    //rather than knowing it from the start. A derelict spawns shallow from a clone of its template, so the
    //interceptor goes in that clone's items, loose on one of its floors. It's saved with the derelict and spawns
    //when the derelict loads, along with the rest of its wreckage.
    [HarmonyPatch(typeof(StarSystem), nameof(StarSystem.AddDerelict))]
    public static class DerelictInterceptorPatch
    {
        public const string InterceptorCO = "ItmFzInterceptor01DmgLoose";
        private const float Chance = 0.2f; //Of each derelict spawned
        private const float Clearance = 0.4f; //Tiles from anything but a floor at the same spot

        [HarmonyPostfix()]
        public static void AddDerelict_Postfix(Ship __result)
        {
            if (__result?.json?.aItems == null || UnityEngine.Random.value >= Chance) return;
            try
            {
                if (!DataHandler.dictCOs.ContainsKey(InterceptorCO)) return;
                JsonItem[] items = __result.json.aItems;
                List<JsonItem> others = items.Where(i => i != null && i.strName != null && !i.strName.StartsWith("ItmFloor") && string.IsNullOrEmpty(i.strParentID)).ToList();
                List<JsonItem> floors = items.Where(i => i != null && i.strName != null && i.strName.StartsWith("ItmFloor") && string.IsNullOrEmpty(i.strParentID))
                    .Where(f => others.All(o => Mathf.Abs(o.fX - f.fX) > Clearance || Mathf.Abs(o.fY - f.fY) > Clearance))
                    .ToList();
                if (floors.Count == 0) return;
                JsonItem floor = floors[UnityEngine.Random.Range(0, floors.Count)];
                var interceptor = new JsonItem
                {
                    strName = InterceptorCO,
                    fX = floor.fX,
                    fY = floor.fY,
                    fRotation = UnityEngine.Random.Range(0, 4) * 90f,
                    strID = DataHandler.GetNextID()
                };
                __result.json.aItems = items.Concat(new[] { interceptor }).ToArray();
                FzAdditions.Logger.LogInfo($"Derelict {__result.strRegID} carries a broken interceptor");
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Adding an interceptor to derelict {__result?.strRegID} failed: {ex}");
            }
        }
    }
}
