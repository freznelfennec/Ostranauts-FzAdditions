using Freznel.FzAdditions.Util.SiteGen;

namespace Freznel.FzAdditions.Util
{
    //How the player gets aboard a generated site. Per the user, a site has nobody to answer comms, so it doesn't talk
    //like a station: hailing it offers what hailing an uncrewed ship does (the game's TXTShipStandardInteractionsNoPilot:
    //forced docking, forced undocking and a status request), and docking means the game's forced docking procedure.
    //Black sites, carved into rock, can also be moored to like an asteroid.
    //
    //A site is still a station everywhere else (its orbit, the AI, the nav map), so it only stops counting as one inside
    //the checks that would refuse those: the forced docking chain's NotAStationNotPlayer ship test, and, for black
    //sites, the mooring control's checks. SiteDockingPatch sets Pretending around them; Ship.IsStation answers false
    //for a site while it's set.
    public static class SiteDocking
    {
        public enum Context { None, Docking, Mooring }

        [System.ThreadStatic] public static Context Pretending;

        //A site the mod generated, by its template's name, so not test stations such as Corsair's Hollow
        public static bool IsSite(Ship ship, out SiteKind kind)
        {
            kind = SiteKind.Relay;
            if (ship == null) return false;
            string template = ship.strTemplateName ?? ship.json?.strTemplateName;
            return SiteShell.TryParseTemplateName(template, out kind, out _, out _, out _);
        }

        public static bool IsSite(Ship ship) => IsSite(ship, out _);

        //Whether Ship.IsStation should answer false for this ship right now
        public static bool HidesStation(Ship ship)
        {
            if (Pretending == Context.None || !IsSite(ship, out SiteKind kind)) return false;
            return Pretending == Context.Docking || (Pretending == Context.Mooring && kind == SiteKind.BlackSite);
        }
    }
}
