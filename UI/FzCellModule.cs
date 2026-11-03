using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section for jump energy cells
    public class FzCellModule : FzTextModule
    {
        private static GameObject prefab;

        public static GameObject GetPrefab()
        {
            if (prefab == null) prefab = CreatePrefab<FzCellModule>("FzCellModule");
            return prefab;
        }

        protected override bool Accepts(CondOwner co)
        {
            return JumpCell.IsCell(co);
        }

        protected override IEnumerable<string> GetLines()
        {
            float capacity = (float)JumpCell.GetCapacity(CO);
            float charge = (float)JumpCell.GetCharge(CO);

            List<string> lines = new List<string>
            {
                Header("CELL SPECS"),
                $"Max capacity: {FzNumeric.FormatEnergy((float)CO.GetCondAmount(Constants.CellChargeMaxCond))}",
                $"Max grid charge rate: {FzNumeric.FormatPower((float)JumpCell.GetChargeRate(CO))}",
                Header("CHARGE"),
                $"Stored: {FzNumeric.FormatEnergy(charge)}",
                $"Capacity: {FzNumeric.FormatEnergy(capacity)}",
                $"Level: {(capacity > 0f ? charge / capacity * 100f : 0f):0}%"
            };

            if (!CO.HasCond("IsInstalled")) return lines;

            double rate = JumpCell.GetCurrentRate(CO);
            lines.Add(Header("CHARGING"));
            lines.Add($"Grid charging: {(JumpCell.IsGridChargeEnabled(CO) ? "Enabled" : "Disabled")}");
            lines.Add($"Source: {(rate <= 0.0 ? "None" : JumpCell.IsTapCharging(CO) ? "Reactor tap" : "Grid")}");
            lines.Add($"Charge rate: {FzNumeric.FormatPower((float)rate)}");
            lines.Add($"Charge time: {FzNumeric.FormatDuration((float)Math.Ceiling(JumpCell.GetChargeTime(CO)))}");
            return lines;
        }
    }
}
