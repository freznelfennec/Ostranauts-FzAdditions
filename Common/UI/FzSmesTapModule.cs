using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section for SMES reactor taps
    public class FzSmesTapModule : FzTextModule
    {
        private const double KWhPerMWh = 1000.0;

        private static GameObject prefab;

        public static GameObject GetPrefab()
        {
            if (prefab == null) prefab = CreatePrefab<FzSmesTapModule>("FzSmesTapModule");
            return prefab;
        }

        protected override bool Accepts(CondOwner co)
        {
            return co.HasCond(SmesConstants.TapCond);
        }

        protected override IEnumerable<string> GetLines()
        {
            double burn = SmesTap.GetBurnRate(CO) * KWhPerMWh;

            List<string> lines = new List<string>
            {
                Header("TAP SPECS"),
                $"Max charge rate: {FzNumeric.FormatPower((float)SmesTap.GetMaxRate(CO))}",
                $"D2O per MWh: {burn * FusionIC.aReactantAmounts[0]:0.##}kg",
                $"He-3 per MWh: {burn * FusionIC.aReactantAmounts[1]:0.##}kg",
                ConverterLine(),
                Header("CONFIGURATION"),
                $"Charging: {(SmesTap.IsChargeEnabled(CO) ? "Enabled" : "Disabled")}",
                $"Charge rate: {FzNumeric.FormatPower((float)SmesTap.GetRateSetting(CO))}"
            };

            if (!CO.HasCond("IsInstalled")) return lines;

            List<CondOwner> units = Smes.GetUnits(CO.ship);
            lines.Add(Header("STATUS"));
            lines.Add($"Reactor: {(ReactorFuel.IsFusing(CO.ship?.Reactor) ? "Fusing" : "Not fusing")}");
            lines.Add($"Powered SMES units: {units.Count}");
            lines.Add($"Ship charge rate: {FzNumeric.FormatPower((float)units.Where(Smes.IsTapCharging).Sum(Smes.GetCurrentRate))}");
            return lines;
        }

        //Its plasma converter's hours left, which it needs to charge at all
        private string ConverterLine()
        {
            CondOwner converter = PlasmaConverter.Installed(CO);
            if (converter == null) return "Plasma converter: None, can't charge";
            double life = PlasmaConverter.Life(converter);
            if (life <= 0.0) return "Plasma converter: Spent, can't charge";
            double bonus = PlasmaConverter.Efficiency(CO);
            return $"Plasma converter: {life:0.#} of {PlasmaConverter.LifeMax(converter):0} h left" + (bonus > 0.0 ? $", +{bonus * 100:0}% efficiency" : "");
        }
    }
}
