using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section for material silos: how full it is, and each material it holds any of, in that material's
    //colour. Materials it could hold but has none of are left out, the silo's panel lists those.
    public class FzSiloModule : FzTextModule
    {
        private static GameObject prefab;

        public static GameObject GetPrefab()
        {
            if (prefab == null) prefab = CreatePrefab<FzSiloModule>("FzSiloModule");
            return prefab;
        }

        protected override bool Accepts(CondOwner co)
        {
            return Silo.IsSilo(co);
        }

        protected override IEnumerable<string> GetLines()
        {
            double capacity = Silo.Capacity(CO);
            double total = Silo.Total(CO);
            var lines = new List<string>
            {
                Header("SILO"),
                $"Used: {Units(total)} / {Units(capacity)} units ({(capacity > 0 ? total / capacity * 100.0 : 0.0):0}%)"
            };

            var contents = Silo.Contents(CO).Where(c => Math.Floor(c.Value + 1e-6) >= 1).ToList();
            if (contents.Count == 0)
            {
                lines.Add("Empty");
                return lines;
            }

            lines.Add(Header("CONTENTS"));
            foreach (var content in contents)
            {
                JsonFabMaterial material = FabData.Materials[content.Key];
                lines.Add($"<color={material.strColor}>{material.strNameFriendly}</color>: {Units(content.Value)}");
            }
            return lines;
        }

        //Whole units, rounded down like the silo's panel
        private static string Units(double amount) => Math.Floor(Math.Max(0.0, amount) + 1e-6).ToString("N0", CultureInfo.InvariantCulture);
    }
}
