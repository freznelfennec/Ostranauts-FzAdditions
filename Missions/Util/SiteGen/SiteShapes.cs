using System;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //Picks the shape generator for a kind of site
    public static class SiteShapes
    {
        //Null if no layout could be made; FailureReason says why
        public static SiteGrid Generate(SiteKind kind, SiteSize size, int seed)
        {
            return kind == SiteKind.Relay ? RelayShape.Generate(size, seed) : BlackSiteShape.Generate(size, seed);
        }

        public static string FailureReason(SiteKind kind) => kind == SiteKind.BlackSite ? BlackSiteShape.LastFailure : "the relay couldn't be laid out";

        public static bool TryParseKind(string text, out SiteKind kind)
        {
            switch (text.ToLower())
            {
                case "relay": kind = SiteKind.Relay; return true;
                case "blacksite": case "black": kind = SiteKind.BlackSite; return true;
                default: kind = SiteKind.Relay; return false;
            }
        }

        public static bool TryParseSize(string text, out SiteSize size)
        {
            return Enum.TryParse(text, ignoreCase: true, out size) && Enum.IsDefined(typeof(SiteSize), size);
        }
    }
}
