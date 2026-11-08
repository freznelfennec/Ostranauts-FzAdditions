using System;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //A plasma converter, which an SMES reactor tap needs to charge. Each tap holds one in its one-slot inventory, which
    //only takes converters (TIsFitContainerFzPlasmaConverter), the way a recycler holds its beam former. Its life is the
    //hours of charging it can still run, StatFzConverterLife, persisted so it's saved. A worn out converter is swapped
    //for a spent one dropped by the tap, so the slot is free for a new one. Higher tiers also make the tap more fuel
    //efficient, by StatFzConverterEfficiency on top of the tap's own.
    public static class PlasmaConverter
    {
        public const string ConverterCond = "IsFzPlasmaConverter";
        public const string LifeCond = "StatFzConverterLife";
        public const string LifeMaxCond = "StatFzConverterLifeMax";
        public const string EfficiencyCond = "StatFzConverterEfficiency"; //0.05 gets 5% more charge from the same fuel
        public const string SpentSuffix = "Spent"; //Each tier's spent converter is its CO's name with this after it

        //The converter in a tap's slot, spent or not, or null
        public static CondOwner Installed(CondOwner tap)
        {
            if (tap?.objContainer == null) return null;
            foreach (CondOwner inside in tap.objContainer.ContainedCOs)
            {
                if (inside != null && !inside.bDestroyed && inside.HasCond(ConverterCond)) return inside;
            }
            return null;
        }

        public static double Life(CondOwner converter) => converter != null ? Math.Max(0.0, converter.GetCondAmount(LifeCond)) : 0.0;

        public static double LifeMax(CondOwner converter) => converter != null ? Math.Max(0.0, converter.GetCondAmount(LifeMaxCond)) : 0.0;

        //Whether the tap has a converter with life left, so it can charge
        public static bool IsWorking(CondOwner tap) => Life(Installed(tap)) > 0.0;

        //The tap's converter's efficiency bonus, 0 with none
        public static double Efficiency(CondOwner tap)
        {
            CondOwner converter = Installed(tap);
            return converter != null ? Math.Max(0.0, converter.GetCondAmount(EfficiencyCond)) : 0.0;
        }

        //Uses up that many hours of the tap's converter's life, and swaps it for a spent one when it's gone
        public static void Wear(CondOwner tap, double hours)
        {
            if (hours <= 0.0) return;
            CondOwner converter = Installed(tap);
            if (converter == null) return;
            double left = Life(converter) - hours;
            if (left > 1e-9)
            {
                converter.SetCondAmount(LifeCond, left);
                return;
            }
            Spend(tap, converter);
        }

        //Takes a worn out converter out of the tap and drops a spent one beside it
        private static void Spend(CondOwner tap, CondOwner converter)
        {
            FzAdditions.Logger.LogInfo($"The plasma converter in {tap.strID} is spent");
            string spentCO = converter.strCODef + SpentSuffix;
            converter.RemoveFromCurrentHome(bForce: true);
            converter.Destroy();
            if (DataHandler.GetCondOwnerDef(spentCO) == null) return;
            CondOwner spent = DataHandler.GetCondOwner(spentCO, null, null, false);
            if (spent == null || tap.ship == null) return;
            try
            {
                tap.ship.DropCO(spent, (Vector2)tap.tf.position);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Dropping a spent plasma converter failed: " + ex);
            }
        }
    }
}
