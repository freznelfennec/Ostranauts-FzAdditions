using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships;
using Ostranauts.Trading;
using System;
using System.Collections.Generic;

namespace Freznel.FzAdditions.Util.AI.Commands
{
    //Fires every gun that bears on the ship's combat target and is ready, through FzWeapons.Fire, so rounds are used
    //up whether the ship is shallow or loaded. A ship that hits someone loses standing with them, as the game's AI
    //does. Running while it has a target, so a sequence stays on it; Failure with none.
    public class FzShoot : BaseCommand
    {
        private const float ShotRepPenalty = -5f; //What the game's AI shoot command costs per shot

        private readonly FzPilot pilot;

        public override string DescriptionFriendly => "Shooting at " + (pilot.ShipUs?.shipCombatTarget?.strRegID ?? "target");

        public FzShoot(FzPilot pilot) : base(pilot)
        {
            this.pilot = pilot;
        }

        public override CommandStatus RunCommand(Blackboard blackboard)
        {
            Ship us = pilot.ShipUs;
            Ship target = us?.shipCombatTarget;
            if (us?.objSS == null || target?.objSS == null || target.bDestroyed) return Failure;

            bool fired = false;
            foreach (var weapon in us.WeaponsSystem.GetWeaponsAndRotations())
            {
                if (!FzWeapons.IsGun(weapon.Item1) || WeaponsSystem.IsReloading(weapon.Item1)) continue;
                if (!FzWeapons.CanBear(us, weapon.Item1, weapon.Item2, target.objSS)) continue;
                if (FzWeapons.Fire(us, weapon.Item1, weapon.Item2, target)) fired = true;
            }
            if (fired) ApplyFactionChange(us, target);
            return Running;
        }

        private void ApplyFactionChange(Ship us, Ship target)
        {
            if (us.IsLocalAuthority || target.Comms == null) return;
            if (target.Comms.GetCaptain(out CondOwner captain) && captain != null && pilot.CaptainUs != null)
            {
                captain.ApplyFactionReps(pilot.CaptainUs, ShotRepPenalty);
            }
        }
    }
}
