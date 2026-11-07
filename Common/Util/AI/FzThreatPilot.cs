using System;

namespace Freznel.FzAdditions.Util.AI
{
    //A pilot that picks fights: FzFindThreats asks it which ships are hostile, out to how far.
    public abstract class FzThreatPilot : FzPilot
    {
        protected FzThreatPilot(Ship ship) : base(ship)
        {
        }

        public abstract bool IsHostileTo(Ship other);

        //How far out it looks for threats, in AU. By default a little past its longest gun, so a target is already
        //picked as it comes in range.
        public virtual double ThreatRangeAU => FzWeapons.MaxRangeAU(ShipUs) * 1.1;
    }
}
