using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section for jump drive reactor taps
    public class FzTapModule : FzTextModule
    {
        private const double KWhPerMWh = 1000.0;

        private static GameObject prefab;

        public static GameObject GetPrefab()
        {
            if (prefab == null) prefab = CreatePrefab<FzTapModule>("FzTapModule");
            return prefab;
        }

        protected override bool Accepts(CondOwner co)
        {
            return co.HasCond(Constants.TapCond);
        }

        protected override IEnumerable<string> GetLines()
        {
            double burn = JumpTap.GetBurnRate(CO) * KWhPerMWh;

            List<string> lines = new List<string>
            {
                Header("TAP SPECS"),
                $"Max charge rate: {FzNumeric.FormatPower((float)JumpTap.GetMaxRate(CO))}",
                $"D2O per MWh: {burn * FusionIC.aReactantAmounts[0]:0.##}kg",
                $"He-3 per MWh: {burn * FusionIC.aReactantAmounts[1]:0.##}kg",
                Header("CONFIGURATION"),
                $"Charging: {(JumpTap.IsChargeEnabled(CO) ? "Enabled" : "Disabled")}",
                $"Charge rate: {FzNumeric.FormatPower((float)JumpTap.GetRateSetting(CO))}"
            };

            if (!CO.HasCond("IsInstalled")) return lines;

            List<CondOwner> cells = JumpCell.GetCells(CO.ship);
            lines.Add(Header("STATUS"));
            lines.Add($"Reactor: {(ReactorFuel.IsFusing(CO.ship?.Reactor) ? "Fusing" : "Not fusing")}");
            lines.Add($"Powered cells: {cells.Count}");
            lines.Add($"Ship charge rate: {FzNumeric.FormatPower((float)cells.Where(JumpCell.IsTapCharging).Sum(JumpCell.GetCurrentRate))}");
            return lines;
        }
    }
}
