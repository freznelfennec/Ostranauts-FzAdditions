using BepInEx;
using Freznel.FzAdditions.Util;
using Freznel.FzAdditions.Util.SiteGen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Freznel.FzAdditions.Patches
{
    //The missions section's console commands, added to the ones ConsolePatch handles
    public static class MissionConsoleCommands
    {
        private const double DefaultKm = 25.0; //Inside Corsair's Hollow's railgun reach (120 km), outside its PDCs' (12 km)

        public static void Register()
        {
            ConsolePatch.AddCommand("encounter", KeywordEncounter,
                "spawns a hostile test station beside the player, flown by the mod's AI",
                $"'fz encounter' spawns one {DefaultKm:0} km away, a {TestEncounter.DefaultTemplate} owned by {TestEncounter.DefaultOwner}",
                "'fz encounter <km> [template] [faction]' picks the distance, the station's ship template and its owner",
                "'fz encounter clear' removes every test station, except one you're docked to",
                "The station fights the player's ships in reach until its guns are empty. 'fz ai' shows it.");
            ConsolePatch.AddCommand("site", KeywordSite,
                "generates mission site layouts",
                "'fz site preview <relay|blacksite|all> [small|medium|large|all] [seed]' draws generated shapes to PNGs in BepInEx/FzSitePreviews",
                $"'fz site spawn <relay|blacksite> [small|medium|large] [seed] [km] [faction]' spawns a site's shell beside the player, {DefaultSiteKm:0} km away and owned by no one by default",
                "'fz site clear' removes spawned sites and test stations, except one you're docked to",
                "With no seed, a random one is picked and shown. The same kind, size and seed always give the same layout.");
        }

        private const double DefaultSiteKm = 5.0;
        private const string SiteUsage = "\nUsage: fz site preview <relay|blacksite|all> [small|medium|large|all] [seed], fz site spawn <relay|blacksite> [small|medium|large] [seed] [km] [faction], fz site clear";

        private static bool KeywordSite(ref string strInput, string[] args)
        {
            string sub = args.Length > 1 ? args[1].ToLower() : "";
            if (sub == "clear")
            {
                TestEncounter.Clear(out string cleared);
                strInput += "\n" + cleared;
                return true;
            }
            if (sub == "spawn") return SiteSpawn(ref strInput, args);
            if (sub != "preview" || args.Length < 3)
            {
                strInput += SiteUsage;
                return false;
            }

            var kinds = new List<SiteKind>();
            if (args[2].ToLower() == "all") kinds.AddRange(new[] { SiteKind.Relay, SiteKind.BlackSite });
            else if (SiteShapes.TryParseKind(args[2], out SiteKind kind)) kinds.Add(kind);
            else
            {
                strInput += $"\nNo site kind named {args[2]}. Kinds: relay, blacksite, all";
                return false;
            }

            var sizes = new List<SiteSize>();
            string sizeArg = args.Length > 3 ? args[3] : "all";
            if (sizeArg.ToLower() == "all") sizes.AddRange((SiteSize[])Enum.GetValues(typeof(SiteSize)));
            else if (SiteShapes.TryParseSize(sizeArg, out SiteSize size)) sizes.Add(size);
            else
            {
                strInput += $"\nNo site size named {sizeArg}. Sizes: small, medium, large, all";
                return false;
            }

            int seed;
            if (args.Length > 4)
            {
                if (!int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                {
                    strInput += $"\n{args[4]} isn't a whole number.";
                    return false;
                }
            }
            else seed = UnityEngine.Random.Range(1, 1000000);

            string folder = Path.Combine(Paths.BepInExRootPath, "FzSitePreviews");
            foreach (SiteKind kind in kinds)
            {
                foreach (SiteSize size in sizes)
                {
                    SiteGrid grid = SiteShapes.Generate(kind, size, seed);
                    if (grid == null)
                    {
                        strInput += $"\n{kind} {size}, seed {seed}: couldn't be laid out, {SiteShapes.FailureReason(kind)}";
                        continue;
                    }
                    string path = Path.Combine(folder, $"{kind}_{size}_{seed}.png".ToLower());
                    SiteLayout layout = SiteShell.Build(grid, SiteStyle.For(kind), SiteCatalogLoader.Get());
                    SitePreview.Write(grid, path, layout);
                    strInput += "\n" + grid.Summary() + $"\n  {layout.Items.Count} items, {layout.Furnished} of them furnishing";
                    foreach (string problem in SiteShell.Check(layout)) strInput += "\n  Problem: " + problem;
                }
            }
            strInput += $"\nWrote to {folder}";
            return true;
        }

        private static bool SiteSpawn(ref string strInput, string[] args)
        {
            if (args.Length < 3 || !SiteShapes.TryParseKind(args[2], out SiteKind kind))
            {
                strInput += SiteUsage;
                return false;
            }
            SiteSize size = SiteSize.Small;
            if (args.Length > 3 && !SiteShapes.TryParseSize(args[3], out size))
            {
                strInput += $"\nNo site size named {args[3]}. Sizes: small, medium, large";
                return false;
            }
            int seed = UnityEngine.Random.Range(1, 1000000);
            if (args.Length > 4 && !int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
            {
                strInput += $"\n{args[4]} isn't a whole number.";
                return false;
            }
            double km = DefaultSiteKm;
            if (args.Length > 5 && (!double.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out km) || km <= 0.0))
            {
                strInput += $"\n{args[5]} isn't a distance in km.";
                return false;
            }
            string owner = args.Length > 6 ? args[6] : null;

            Ship station = SiteSpawner.SpawnTest(kind, size, seed, km, owner, out string message);
            strInput += "\n" + message;
            return station != null;
        }

        private static bool KeywordEncounter(ref string strInput, string[] args)
        {
            if (args.Length == 2 && args[1].ToLower() == "clear")
            {
                TestEncounter.Clear(out string cleared);
                strInput += "\n" + cleared;
                return true;
            }

            double km = DefaultKm;
            if (args.Length > 1 && (!double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out km) || km <= 0.0))
            {
                strInput += "\nUsage: fz encounter [km] [template] [faction], or fz encounter clear";
                return false;
            }
            string template = args.Length > 2 ? args[2] : TestEncounter.DefaultTemplate;
            string owner = args.Length > 3 ? args[3] : TestEncounter.DefaultOwner;

            Ship station = TestEncounter.Spawn(km, template, owner, out string message);
            strInput += "\n" + message;
            return station != null;
        }
    }
}
