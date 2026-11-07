using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section for SMES units
    public class FzSmesModule : FzTextModule
    {
        private static GameObject prefab;

        public static GameObject GetPrefab()
        {
            if (prefab == null) prefab = CreatePrefab<FzSmesModule>("FzSmesModule");
            return prefab;
        }

        protected override bool Accepts(CondOwner co)
        {
            return Smes.IsUnit(co);
        }

        protected override IEnumerable<string> GetLines()
        {
            float capacity = (float)Smes.GetCapacity(CO);
            float charge = (float)Smes.GetCharge(CO);

            List<string> lines = new List<string>
            {
                Header("SMES SPECS"),
                $"Max capacity: {FzNumeric.FormatEnergy((float)CO.GetCondAmount(SmesConstants.ChargeMaxCond))}",
                $"Max grid charge rate: {FzNumeric.FormatPower((float)Smes.GetChargeRate(CO))}",
                Header("CHARGE"),
                $"Stored: {FzNumeric.FormatEnergy(charge)}",
                $"Capacity: {FzNumeric.FormatEnergy(capacity)}",
                $"Level: {(capacity > 0f ? charge / capacity * 100f : 0f):0}%"
            };

            if (!CO.HasCond("IsInstalled")) return lines;

            double rate = Smes.GetCurrentRate(CO);
            lines.Add(Header("CHARGING"));
            lines.Add($"Grid charging: {(Smes.IsGridChargeEnabled(CO) ? "Enabled" : "Disabled")}");
            lines.Add($"Source: {(rate <= 0.0 ? "None" : Smes.IsTapCharging(CO) ? "Reactor tap" : "Grid")}");
            lines.Add($"Charge rate: {FzNumeric.FormatPower((float)rate)}");
            lines.Add($"Charge time: {FzNumeric.FormatDuration((float)Math.Ceiling(Smes.GetChargeTime(CO)))}");
            return lines;
        }
    }
}
