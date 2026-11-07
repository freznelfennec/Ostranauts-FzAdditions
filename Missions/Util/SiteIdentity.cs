using System;

namespace Freznel.FzAdditions.Util
{
    //Whether a site knows who the player is. Per the user, what the player does at a site only costs standing with its
    //owner if they let themselves be identified: docking with the transponder on identifies them to that site for good
    //(it has their registration), docking dark doesn't. Shooting at a site from outside counts by the transponder at the
    //moment of firing. A site is anything FzSpawns spawned; IsFzSiteIdentified on its ShipCO is persisted.
    public static class SiteIdentity
    {
        public const string IdentifiedCond = "IsFzSiteIdentified";

        public static bool IsSite(Ship ship) => ship != null && FzSpawns.IsSpawn(ship);

        public static bool IsIdentified(Ship site) => site?.ShipCO != null && site.ShipCO.HasCond(IdentifiedCond);

        //A ship of the player's: the one they're in, or one they own
        public static bool IsPlayers(Ship ship)
        {
            if (ship == null || CrewSim.coPlayer == null) return false;
            if (ship.IsPlayerShip()) return true;
            return CrewSim.system?.GetShipOwner(ship.strRegID) == CrewSim.coPlayer.strID;
        }

        //The player or someone in their crew
        public static bool IsPlayersPerson(CondOwner co)
        {
            if (co == null || CrewSim.coPlayer == null) return false;
            if (co == CrewSim.coPlayer || co == CrewSim.GetSelectedCrew()) return true;
            var roster = CrewSim.coPlayer.Company?.mapRoster;
            return roster != null && roster.ContainsKey(co.strID);
        }

        //A ship of the player's has docked to a site: with its transponder on, the site now knows who they are
        public static void Docked(Ship ship, Ship site)
        {
            if (!IsSite(site) || !IsPlayers(ship) || site.ShipCO == null) return;
            string name = string.IsNullOrEmpty(site.publicName) ? site.strRegID : site.publicName;
            if (!ship.IsFlyingDark())
            {
                if (IsIdentified(site)) return;
                site.ShipCO.SetCondAmount(IdentifiedCond, 1.0);
                CrewSim.coPlayer?.LogMessage($"{name} logged {ship.strRegID}'s transponder as it docked. Whatever happens here is on your record with its owner.", "Bad", "Game");
            }
            else if (!IsIdentified(site))
            {
                CrewSim.coPlayer?.LogMessage($"{ship.strRegID} docked to {name} with its transponder off. Nobody here knows who you are.", "Good", "Game");
            }
        }

        //Whether a standing change from something done to a thing aboard a site is skipped: only when it's a cost, done by
        //the player's side, at a site that doesn't know who they are
        public static bool Excuses(CondOwner target, CondOwner doer, float change)
        {
            if (change >= 0f || target == null) return false;
            Ship site = target.ship;
            return IsSite(site) && !IsIdentified(site) && IsPlayersPerson(doer);
        }

        //Whether the standing cost of firing on a ship is skipped: a site, fired on by a player's ship flying dark
        public static bool ExcusesFire(Ship shooter, Ship target, float change)
        {
            if (change >= 0f) return false;
            return IsSite(target) && IsPlayers(shooter) && shooter.IsFlyingDark();
        }
    }
}
