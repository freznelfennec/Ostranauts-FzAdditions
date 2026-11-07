using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using System.Collections.Generic;

namespace Freznel.FzAdditions.Util.AI.Commands
{
    //Picks what an FzThreatPilot should fight, from the ships in its reach that it says are hostile. Fills in the
    //blackboard the way the game's threat checks do (Enemies, ClosestEnemy, InCombat), so the game's own combat
    //commands can follow it, and sets the ship's combat target. Keeps the target it has while it's still an enemy.
    //Ships docked anywhere are passed over: nothing can hit them.
    public class FzFindThreats : BaseCommand
    {
        private readonly FzThreatPilot pilot;

        public override string DescriptionFriendly => "Watching for threats";

        public FzFindThreats(FzThreatPilot pilot) : base(pilot)
        {
            this.pilot = pilot;
        }

        public override CommandStatus RunCommand(Blackboard blackboard)
        {
            Ship us = pilot.ShipUs;
            if (us?.objSS == null) return Failure;

            double reach = pilot.ThreatRangeAU;
            var enemies = new List<Ship>();
            Ship closest = null;
            double best = double.PositiveInfinity;
            foreach (Ship other in CrewSim.system.GetAllLoadedShips())
            {
                if (other == null || other == us || other.bDestroyed || other.objSS == null || other.HideFromSystem) continue;
                if (other.Classification == Ship.TypeClassification.Projectile || other.IsDocked()) continue;
                if (!pilot.IsHostileTo(other)) continue;
                double range = us.GetRangeTo(other);
                if (range > reach) continue;
                enemies.Add(other);
                if (range < best)
                {
                    best = range;
                    closest = other;
                }
            }

            blackboard.Set("Enemies", enemies);
            if (closest == null)
            {
                blackboard.Remove("ClosestEnemy");
                blackboard.Set("InCombat", false);
                if (us.shipCombatTarget != null) us.ClearShipTarget();
                return Failure;
            }

            Ship target = us.shipCombatTarget != null && enemies.Contains(us.shipCombatTarget) ? us.shipCombatTarget : closest;
            us.shipCombatTarget = target;
            us.shipScanTarget = target;
            blackboard.Set("ClosestEnemy", closest);
            blackboard.Set("InCombat", true);
            return Success;
        }
    }
}
