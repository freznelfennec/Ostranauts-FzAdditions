using Freznel.FzAdditions.Util.AI;
using Freznel.FzAdditions.Util.AI.Commands;
using Ostranauts.Ships.Commands;
using System;

namespace Freznel.FzAdditions.Util
{
    //Flies a station's guns. It never moves. At Hostile it fights the player's ships in its reach; at any level it
    //fights back at whatever targets it, and goes Hostile for that. It only fires what's loaded in its weapons, so a
    //station that has emptied them falls silent and can be docked with.
    public class FzStationDefensePilot : FzThreatPilot
    {
        public const string TypeName = "StationDefense";

        private const double CombatInterval = 0.25; //Game seconds between updates while fighting
        private const double IdleInterval = 2.0;

        private int lastRounds = -1;

        public FzStationDefensePilot(Ship ship) : base(ship)
        {
        }

        public override string Type => TypeName;

        public override double Interval => Blackboard.Get<bool>("InCombat") ? CombatInterval : IdleInterval;

        protected override ICommand BuildTree()
        {
            return new SequenceCommand("FzStationDefense", new ICommand[]
            {
                new FzFindThreats(this),
                new FzShoot(this)
            });
        }

        public override bool IsHostileTo(Ship other)
        {
            Ship us = ShipUs;
            if (other.shipCombatTarget == us)
            {
                if (FzAI.GetAlert(us) < FzAI.Hostile) FzAI.SetAlert(us, FzAI.Hostile);
                return true;
            }
            if (FzAI.GetAlert(us) < FzAI.Hostile) return false;
            return IsPlayers(other);
        }

        private static bool IsPlayers(Ship ship)
        {
            if (ship.IsPlayerShip()) return true;
            return CrewSim.coPlayer != null && CrewSim.system.GetShipOwner(ship.strRegID) == CrewSim.coPlayer.strID;
        }

        public override void Think(double dt)
        {
            base.Think(dt);

            //Tells the player once when the station's guns run dry
            int rounds = FzWeapons.Rounds(ShipUs);
            if (lastRounds > 0 && rounds == 0)
            {
                Ship us = ShipUs;
                string name = string.IsNullOrEmpty(us?.publicName) ? RegID : us.publicName;
                CrewSim.coPlayer?.LogMessage($"{name} has stopped firing. Its guns look empty.", "Good", "Game");
            }
            lastRounds = rounds;
        }

        public override string Describe()
        {
            return base.Describe() + $", {FzWeapons.Rounds(ShipUs)} rounds loaded";
        }
    }
}
