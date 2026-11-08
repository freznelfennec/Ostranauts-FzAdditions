using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Tier = Freznel.FzAdditions.Util.BreederBlanket.Tier;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section for a reactor with a breeder blanket fitted, and for a blanket item
    public class FzBlanketModule : FzTextModule
    {
        private static GameObject prefab;

        public static GameObject GetPrefab()
        {
            if (prefab == null) prefab = CreatePrefab<FzBlanketModule>("FzBlanketModule");
            return prefab;
        }

        protected override bool Accepts(CondOwner co)
        {
            return BreederBlanket.IsBlanket(co) || BreederBlanket.HasBlanket(co);
        }

        protected override IEnumerable<string> GetLines()
        {
            bool fitted = BreederBlanket.HasBlanket(CO);
            Tier tier = BreederBlanket.GetTier(fitted ? BreederBlanket.FittedTier(CO) : BreederBlanket.ItemTier(CO));
            if (tier == null) return new string[0];

            double unconverted = BreederBlanket.Unconverted(CO, tier);
            var lines = new List<string>
            {
                Header(fitted ? "BREEDER BLANKET" : "BLANKET SPECS"),
                $"Tier: {tier.Name}",
                $"Yield: {tier.Yield * 100:0}% of fuel burned",
                $"Conversion time: {tier.ConversionHours:0} h",
                $"Volatiles left: {unconverted * 100:0.#}% of {tier.Volatiles:N0}",
                $"Fuel left to breed: {unconverted * tier.LifeKg:N0} kg"
            };

            double d2o = BreederBlanket.WaitingD2O(CO);
            double he3 = BreederBlanket.WaitingHe3(CO);
            if (d2o + he3 >= ReactorConstants.WaitingDone) lines.Add($"Fuel waiting: {d2o:0.#} kg D2O, {he3:0.#} kg He-3");

            if (fitted && BreederBlanket.IsSpent(CO, tier)) lines.Add("SPENT: remove it for recycling");
            else if (fitted && unconverted <= 1e-6) lines.Add("Converted: returning its last fuel");
            else if (fitted && !ReactorFuel.IsFusing(CO)) lines.Add("Idle: the reactor isn't fusing");
            return lines;
        }
    }
}
