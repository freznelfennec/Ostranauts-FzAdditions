using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section for jump field emitters
    public class FzEmitterModule : FzTextModule
    {
        private static readonly int ContourTicks = 4; //Tooltip ticks are 0.5s, the contour is rebuilt every 2s

        private static GameObject prefab;

        public static GameObject GetPrefab()
        {
            if (prefab == null) prefab = CreatePrefab<FzEmitterModule>("FzEmitterModule");
            return prefab;
        }

        private FieldContour.Segment segment;
        private bool inContour;
        private int ticks;

        protected override bool Accepts(CondOwner co)
        {
            return co.HasCond(Constants.EmitterCond);
        }

        protected override void OnRefresh(bool first)
        {
            ticks++;
            if (!first && ticks < ContourTicks) return;
            ticks = 0;
            inContour = FieldContour.TryGetSegment(CO, out segment);
        }

        protected override IEnumerable<string> GetLines()
        {
            float offset = FieldContour.GetOffset(CO);
            float energyMin = (float)CO.GetCondAmount(Constants.FieldEnergyMinCond);
            float energyMax = (float)CO.GetCondAmount(Constants.FieldEnergyMaxCond);

            List<string> lines = new List<string>
            {
                Header("EMITTER SPECS"),
                $"Max offset: {FieldContour.GetMaxOffset(CO):0.##}m",
                $"Max segment: {FieldContour.GetBaseMaxSegmentLength(CO):0.##}m",
                $"Field energy: {FzNumeric.FormatEnergy(energyMin)}-{FzNumeric.FormatEnergy(energyMax)}",
                Header("CONFIGURATION"),
                $"Field offset: {offset:0.##}m",
                $"Reinforcement: {FieldContour.GetReinforcement(CO) * 100f:0}%",
                $"Effective max segment: {FieldContour.GetMaxSegmentLength(CO):0.##}m",
                $"Field energy: {FzNumeric.FormatEnergy(FieldContour.GetFieldEnergy(CO, offset))}",
                Header("FIELD")
            };

            if (inContour)
            {
                lines.Add($"Field strength: {segment.Strength:0}%");
                lines.Add($"Segment length: {segment.Length:0.##}m");
            }
            else
            {
                lines.Add("Field strength: 0%");
                lines.Add("Not part of a field contour");
            }

            return lines;
        }
    }
}
