using Ostranauts.Ships;
using Ostranauts.Trading;
using Ostranauts.Utils.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util.AI
{
    //A ship's weapons as the mod's AI sees them. Rounds are real items loaded in each weapon, and firing through the
    //game's own calls uses them up and saves that: a shallow ship marks the rounds it fires spent on its saved items
    //(ShootShallow), a loaded ship destroys them (ShootAuto). The game's AI shoot command only uses ShootShallow,
    //whose ammo list is only filled while the ship is shallow, so a loaded ship never fires through it. Fire picks
    //the call by the ship's load state.
    public static class FzWeapons
    {
        private const double MetresPerAU = 149597872000.0;

        //Rounds loaded in one weapon
        public static int Rounds(Ship ship, DataCOWrapper weapon)
        {
            if (ship == null || weapon == null) return 0;
            if (ship.LoadState <= Ship.Loaded.Shallow)
            {
                return ship.WeaponsSystem.GetAmmoUnderWeaponShallow(weapon)?.Count ?? 0;
            }
            CondOwner co = weapon.CO;
            if (co == null) return 0;
            return WeaponsSystem.GetAmmo(co)?.Sum(StackSize) ?? 0;
        }

        //Rounds loaded across the ship's weapons that can hit something (decoy launchers aside)
        public static int Rounds(Ship ship)
        {
            if (ship?.WeaponsSystem == null) return 0;
            return ship.WeaponsSystem.AllWeapons.Where(IsGun).Sum(w => Rounds(ship, w));
        }

        private static int StackSize(CondOwner round)
        {
            if (round == null) return 0;
            return Math.Max(1, round.StackCount);
        }

        //Weapons that fire at ships, so not decoy launchers
        public static bool IsGun(DataCOWrapper weapon)
        {
            return weapon != null && !weapon.HasCond("IsShipWeaponDecoyLauncher");
        }

        //A weapon's reach in metres, with the gunner's bonus mass throwers get
        public static double RangeMetres(Ship ship, DataCOWrapper weapon)
        {
            double range = weapon.DataCO.GetCondAmount("IsShipWeaponArcRange");
            if (weapon.DataCO.HasCond("IsShipWeaponMassThrower")) range *= ship.WeaponsSystem.fRangeModGunner;
            return range;
        }

        //The longest reach of the ship's guns, in AU
        public static double MaxRangeAU(Ship ship)
        {
            if (ship?.WeaponsSystem == null) return 0.0;
            double best = 0.0;
            foreach (DataCOWrapper weapon in ship.WeaponsSystem.AllWeapons)
            {
                if (IsGun(weapon)) best = Math.Max(best, RangeMetres(ship, weapon));
            }
            return best / MetresPerAU;
        }

        //Whether a weapon, turned as it's installed, has the target in its arc and range
        public static bool CanBear(Ship ship, DataCOWrapper weapon, float rotation, ShipSitu target)
        {
            double range = RangeMetres(ship, weapon);
            double arc = weapon.DataCO.GetCondAmount("IsShipWeaponArcAngle");
            if (Math.Abs(arc - 360.0) < 0.01) arc = 360.0;
            float angle = ship.WeaponsSystem.GetItemsDefaultFiringAngle(rotation);
            double x = ship.objSS.vPosx + ship.objSS.GetRadiusAU() * Mathf.Cos(angle + Mathf.PI / 2f);
            double y = ship.objSS.vPosy + ship.objSS.GetRadiusAU() * Mathf.Sin(angle + Mathf.PI / 2f);
            Point direction = new Point(x, y) - ship.objSS.vPos;
            return WeaponsSystem.IsPointInView(ship.objSS.vPos, direction, target.vPos, range, arc);
        }

        //Fires one weapon at a target through the game's own call for the ship's load state, using its rounds up. False
        //if it's reloading, empty, jammed or couldn't fire. After a shot the weapon also waits the time the game's AI
        //takes to aim it again (its arc over its targeting speed), so it keeps the game's AI's pace.
        public static bool Fire(Ship ship, DataCOWrapper weapon, float rotation, Ship target)
        {
            if (ship == null || weapon == null || target?.objSS == null) return false;
            if (WeaponsSystem.IsReloading(weapon)) return false;

            bool fired;
            if (ship.LoadState <= Ship.Loaded.Shallow)
            {
                fired = ship.WeaponsSystem.ShootShallow(weapon, rotation, target.objSS);
            }
            else
            {
                if (weapon.CO == null) return false;
                fired = ship.WeaponsSystem.ShootAuto(new List<CondOwner> { weapon.CO }, target.objSS);
            }
            if (!fired) return false;

            double arc = weapon.DataCO.GetCondAmount("IsShipWeaponArcAngle");
            double speed = weapon.GetCondAmount("IsShipWeaponTargetingSpeed");
            if (speed > 0.0) weapon.SetCondAmount("IsReloading", weapon.GetCondAmount("IsReloading") + arc / speed);
            return true;
        }

        //Spends every round loaded in the ship's weapons, as if it had fired them all, decoys included: a shallow ship
        //marks them spent on its saved items the way ShootShallow does, a loaded one destroys them the way ShootAuto
        //does. Returns how many rounds went.
        public static int Disarm(Ship ship)
        {
            if (ship?.WeaponsSystem == null) return 0;
            int spent = 0;
            foreach (DataCOWrapper weapon in ship.WeaponsSystem.AllWeapons.ToList())
            {
                if (ship.LoadState <= Ship.Loaded.Shallow)
                {
                    foreach (DataCOWrapper round in ship.WeaponsSystem.GetAmmoUnderWeaponShallow(weapon) ?? new List<DataCOWrapper>())
                    {
                        round.CurrentDamage = round.DataCO.GetMaxHealth();
                        spent++;
                    }
                    continue;
                }
                CondOwner co = weapon.CO;
                if (co == null) continue;
                foreach (CondOwner round in WeaponsSystem.GetAmmo(co) ?? new List<CondOwner>())
                {
                    if (round == null || round.bDestroyed) continue;
                    spent += StackSize(round);
                    round.RemoveFromCurrentHome(bForce: true);
                    round.Destroy();
                }
            }
            if (spent > 0) ship.WeaponsSystem.ResetWeaponData();
            return spent;
        }

        //Rounds spent while the ship was shallow are only marked spent, with their damage at full, and the game's
        //ammo check doesn't forbid damage. When a flown ship loads, spent rounds are taken out so they can't come back
        //as live ammo. Returns how many were taken.
        public static int ClearSpentRounds(Ship ship)
        {
            if (ship?.WeaponsSystem == null || ship.LoadState <= Ship.Loaded.Shallow) return 0;
            int cleared = 0;
            foreach (DataCOWrapper weapon in ship.WeaponsSystem.AllWeapons.ToList())
            {
                CondOwner co = weapon.CO;
                if (co == null) continue;
                foreach (CondOwner round in WeaponsSystem.GetAmmo(co) ?? new List<CondOwner>())
                {
                    if (round == null || round.bDestroyed) continue;
                    double health = DataHandler.GetDataCO(round.strName)?.GetMaxHealth() ?? 0.0;
                    if (health <= 0.0 || round.GetCondAmount("StatDamage") < health - 0.01) continue;
                    round.RemoveFromCurrentHome(bForce: true);
                    round.Destroy();
                    cleared++;
                }
            }
            if (cleared > 0) ship.WeaponsSystem.ResetWeaponData();
            return cleared;
        }
    }
}
