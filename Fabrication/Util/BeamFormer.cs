using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //A matter beam former, which a recycler or fabricator needs to work. Each machine holds one in its one-slot
    //inventory, which only takes beam formers (TIsFitContainerFzBeamFormer), the way a CO2 scrubber holds its
    //cartridge. Its life is the material units it can still shape, StatFzBeamLife, persisted so it's saved; every unit a
    //machine recycles or fabricates through it uses one up. A worn out former is swapped for a spent one, dropped by
    //the machine, so the slot is free for a new one. Each tier has its own spent former, which recycles for half its
    //tier's materials, without the exotics.
    public static class BeamFormer
    {
        public const string FormerCond = "IsFzBeamFormer";
        public const string LifeCond = "StatFzBeamLife";
        public const string LifeMaxCond = "StatFzBeamLifeMax";
        public const string SpentSuffix = "Spent"; //Each tier's spent former is its CO's name with this after it

        public const string MissingReason = "NO BEAM FORMER";
        public const string LowReason = "BEAM FORMER LOW";

        //The former in a machine's slot, spent or not, or null
        public static CondOwner Installed(CondOwner machine)
        {
            if (machine?.objContainer == null) return null;
            foreach (CondOwner inside in machine.objContainer.ContainedCOs)
            {
                if (inside != null && !inside.bDestroyed && inside.HasCond(FormerCond)) return inside;
            }
            return null;
        }

        public static double Life(CondOwner former) => former != null ? Math.Max(0.0, former.GetCondAmount(LifeCond)) : 0.0;

        public static double LifeMax(CondOwner former) => former != null ? Math.Max(0.0, former.GetCondAmount(LifeMaxCond)) : 0.0;

        //The machine's former's life, 0 with none
        public static double MachineLife(CondOwner machine) => Life(Installed(machine));

        //Why the machine's former can't shape that many units, or null when it can
        public static string Check(CondOwner machine, double units)
        {
            CondOwner former = Installed(machine);
            if (former == null || Life(former) <= 0.0) return MissingReason;
            return Life(former) + 1e-6 >= units ? null : LowReason;
        }

        //Uses up that many units of the machine's former's life, and swaps it for a spent one when it's gone
        public static void Wear(CondOwner machine, double units)
        {
            if (units <= 0.0) return;
            CondOwner former = Installed(machine);
            if (former == null) return;
            double left = Life(former) - units;
            if (left > 1e-6)
            {
                former.SetCondAmount(LifeCond, left);
                return;
            }
            Spend(machine, former);
        }

        //Takes a worn out former out of the machine and drops a spent one in front of it
        private static void Spend(CondOwner machine, CondOwner former)
        {
            FzAdditions.Logger.LogInfo($"The beam former in {machine.strID} is spent");
            string spentCO = former.strCODef + SpentSuffix;
            former.RemoveFromCurrentHome(bForce: true);
            former.Destroy();
            if (DataHandler.GetCondOwnerDef(spentCO) == null) return;
            CondOwner spent = DataHandler.GetCondOwner(spentCO, null, null, false);
            if (spent == null || machine.ship == null) return;
            try
            {
                //Its panel side, where its use point is, is the bottom of its sprite
                Vector2 front = (Vector2)machine.tf.position - (Vector2)machine.tf.up * 1.5f;
                machine.ship.DropCO(spent, front);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Dropping a spent beam former failed: " + ex);
            }
        }
    }
}
