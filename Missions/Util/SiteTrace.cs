using System;

namespace Freznel.FzAdditions.Util
{
    //How close a site is to tracing the player's intrusion, 0 to 100, kept on the site's ShipCO as StatFzSiteTrace
    //(persisted), so every vault at a site shares it. The vault interface raises it as the player hacks.
    //
    //Per the user, a full trace hands the player to a system still to be designed that will have them hunted by the
    //corporation's mercenaries. Until it exists, OnTraceFull only logs; anything that wants to react can subscribe.
    public static class SiteTrace
    {
        public const string TraceCond = "StatFzSiteTrace";
        public const string TracedCond = "IsFzSiteTraced"; //Set once the trace has filled, so it's only reported once
        public const double Full = 100.0;

        //Raised once per site when its trace fills: the site, and the corporation (fz/sites) that owns it
        public static event Action<Ship, string> OnTraceFull;

        public static double Get(Ship site) => site?.ShipCO?.GetCondAmount(TraceCond) ?? 0.0;

        public static bool IsTraced(Ship site) => site?.ShipCO != null && site.ShipCO.HasCond(TracedCond);

        //Raises a site's trace, and reports it the first time it fills. Returns the new trace.
        public static double Add(Ship site, double amount, string corp)
        {
            if (site?.ShipCO == null) return 0.0;
            double trace = Math.Min(Full, Math.Max(0.0, Get(site) + amount));
            site.ShipCO.SetCondAmount(TraceCond, trace);
            if (trace >= Full && !IsTraced(site))
            {
                site.ShipCO.SetCondAmount(TracedCond, 1.0);
                Hunt(site, corp);
            }
            return trace;
        }

        //The hook for the corporate mercenary hunt, to be designed
        private static void Hunt(Ship site, string corp)
        {
            string name = string.IsNullOrEmpty(site.publicName) ? site.strRegID : site.publicName;
            FzAdditions.Logger.LogInfo($"{name} traced the intrusion to the player; {corp ?? "its owner"} will want them hunted (not built yet)");
            CrewSim.coPlayer?.LogMessage($"{name} has traced the intrusion. Someone will come looking.", "Bad", "Game");
            try
            {
                OnTraceFull?.Invoke(site, corp);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("A site trace handler failed: " + ex);
            }
        }
    }
}
