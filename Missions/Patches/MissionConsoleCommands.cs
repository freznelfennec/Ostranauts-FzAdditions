using BepInEx;
using Freznel.FzAdditions.Util;
using Freznel.FzAdditions.Util.SiteGen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
                "'fz encounter disarm' spends every round on every test station, as if they'd fired until empty",
                "'fz encounter clearnpcs' removes everyone aboard test stations but you and your crew",
                "'fz encounter factions' lists the factions an encounter can be owned by",
                "'fz encounter clear' removes every test station, except one you're docked to",
                "The station fights the player's ships in reach until its guns are empty. 'fz ai' shows it.");
            ConsolePatch.AddCommand("site", KeywordSite,
                "generates mission site layouts",
                "'fz site preview <relay|blacksite|all> [small|medium|large|all] [seed] [corp]' draws generated sites to PNGs in BepInEx/FzSitePreviews",
                $"'fz site spawn <relay|blacksite> [small|medium|large] [seed] [km] [corp] [faction]' spawns a site beside the player, {DefaultSiteKm:0} km away, armed and hostile, owned by the corporation's faction unless a faction is given",
                "'fz site corps' lists the corporations from fz/sites, and what their vaults hold",
                "'fz site unlock' opens the data core storage of every vault on the station you're aboard or docked to, as a finished hack would",
                "'fz site clear' removes spawned sites and test stations, except one you're docked to",
                "With no seed, a random one is picked and shown. The same kind, size and seed always give the same layout.");
            ConsolePatch.AddCommand("dock", KeywordDock,
                "docks your ship straight to a station or ship, for testing",
                "'fz dock' docks to the nearest station the mod spawned",
                "'fz dock <regID>' docks to any ship or station by its registration",
                "Your ship is undocked from anything it's docked to, moved to the target and docked at a free port, as the game's debug fast travel does. It counts as a real docking: a site you dock to with your transponder on logs it.");
            ConsolePatch.AddCommand("trace", KeywordTrace,
                "lists and tests signal traces, which sideband interceptors log and lead to corporate sites",
                "'fz trace' lists them, with how well the ship is listening",
                "'fz trace add [corp] [relay|blacksite]' logs a new one now, near a body weighted towards nearby ones",
                "'fz trace step <id>' finishes the trace's current job: decoding, a narrowing step, or reacquiring",
                "'fz trace locate <id>' finishes every job until its site spawns, wherever your ship is",
                "'fz trace clear' discards every trace, with any sites they've spawned that you're not aboard",
                "A Signal Traces navmod shows them, and picks the one being investigated.");
            ConsolePatch.AddCommand("fog", KeywordFog,
                "shows or clears the fog of war on the station you're aboard or docked to",
                "'fz fog' says how much of the current encounter's station is revealed",
                "'fz fog clear' reveals all of it for good",
                "'fz fog reset' fogs all of it again, for testing");
        }

        //The station the player's current encounter is on: the spawned station they're aboard, or one docked to their ship
        private static Ship CurrentEncounter()
        {
            Ship here = CrewSim.GetSelectedCrew()?.ship;
            if (here == null) return null;
            if (FzSpawns.IsSpawn(here)) return here;
            foreach (Ship docked in here.GetAllDockedShips())
            {
                if (FzSpawns.IsSpawn(docked)) return docked;
            }
            return null;
        }

        //Docks the player's ship to a target the way the game's debug fast travel does: undocks it from everything,
        //lets go of any orbit it's locked to, puts it on the target's position and velocity, and docks it through
        //CrewSim.DockShip, which picks a free port on each and lines the ships up at them
        private static bool KeywordDock(ref string strInput, string[] args)
        {
            Ship ours = CrewSim.coPlayer?.ship;
            if (ours == null || CrewSim.system == null)
            {
                strInput += "\nNo player ship to dock.";
                return false;
            }

            Ship target;
            if (args.Length > 1)
            {
                target = CrewSim.system.GetShipByRegID(args[1]) ?? CrewSim.system.GetShipByRegID(args[1].ToUpperInvariant());
                if (target == null)
                {
                    strInput += $"\nNo ship registered {args[1]}.";
                    return false;
                }
            }
            else
            {
                target = CrewSim.system.dictShips.Values
                    .Where(s => s != null && s != ours && !s.bDestroyed && s.objSS != null && FzSpawns.IsSpawn(s))
                    .OrderBy(s => Distance(ours, s))
                    .FirstOrDefault();
                if (target == null)
                {
                    strInput += "\nNo station the mod spawned to dock to. 'fz site spawn' or 'fz encounter' makes one, or give a registration.";
                    return false;
                }
            }
            if (target == ours)
            {
                strInput += "\nThat's your own ship.";
                return false;
            }
            if (ours.GetAllDockedShips().Contains(target))
            {
                strInput += $"\nAlready docked to {target.publicName} ({target.strRegID}).";
                return true;
            }

            foreach (Ship docked in ours.GetAllDockedShips().ToList()) CrewSim.UndockShip(ours, docked, bPushback: false);
            ours.objSS.strBOPORShip = null;
            ours.objSS.CopyFrom(target.objSS, bKinematicsOnly: true);
            Ship result = CrewSim.DockShip(ours, target.strRegID);
            if (result == null)
            {
                strInput += $"\nCouldn't dock to {target.publicName} ({target.strRegID}): no free docking port on one of the ships. Your ship was moved beside it.";
                return false;
            }
            strInput += $"\nDocked to {target.publicName} ({target.strRegID}).";
            return true;
        }

        private static double Distance(Ship a, Ship b)
        {
            double dx = a.objSS.vPosx - b.objSS.vPosx, dy = a.objSS.vPosy - b.objSS.vPosy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool KeywordFog(ref string strInput, string[] args)
        {
            string sub = args.Length > 1 ? args[1].ToLower() : "";
            Ship site = CurrentEncounter();
            if (site == null)
            {
                strInput += "\nYou're not aboard or docked to a station the mod spawned.";
                return false;
            }
            SiteFog.State state = SiteFog.Get(site);
            if (state == null)
            {
                strInput += $"\n{site.strRegID} isn't fully loaded, so it has no fog yet.";
                return false;
            }
            switch (sub)
            {
                case "clear":
                    SiteFog.Clear(site);
                    strInput += $"\nCleared the fog on {site.publicName} ({site.strRegID}).";
                    return true;
                case "reset":
                    SiteFog.Reset(site);
                    strInput += $"\nFogged all of {site.publicName} ({site.strRegID}) again.";
                    return true;
                case "":
                    strInput += $"\n{site.publicName} ({site.strRegID}): " + (state.Cleared ? "fog cleared" : $"{SiteFog.RevealedCount(state)} of {state.Revealed.Length} tiles revealed");
                    return true;
                default:
                    strInput += "\nUsage: fz fog, fz fog clear or fz fog reset";
                    return false;
            }
        }

        private const string TraceUsage = "\nUsage: fz trace, fz trace add [corp] [relay|blacksite], fz trace step <id>, fz trace locate <id>, fz trace clear";

        private static bool KeywordTrace(ref string strInput, string[] args)
        {
            string sub = args.Length > 1 ? args[1].ToLower() : "";
            SignalTraces.Readings r = SignalTraces.Read();
            switch (sub)
            {
                case "":
                    strInput += $"\nInterceptors {r.Active} on of {r.Installed}, open space {r.Open * 100.0:0}%, {(r.Skilled ? "skilled" : "unskilled")}: "
                        + $"{r.DetectPerHour * 24.0:0.00} signals a day, investigating at {r.Speed:0.00}x.";
                    if (SignalTraces.All.Count == 0) strInput += "\nNo signal traces.";
                    foreach (SignalTrace t in SignalTraces.All)
                    {
                        double area = SignalTraces.DistanceToAreaKm(t, r.Ship);
                        strInput += $"\n  {t.Id}{(SignalTraces.Investigating == t.Id ? "*" : "")}: {t.Corp} {t.Size} {t.Kind} seed {t.Seed} near {t.Body}, level {t.Level}/{t.Steps}, "
                            + $"next {SignalTraces.JobFor(t)} {t.Progress:0.0}/{SignalTraces.JobHours(SignalTraces.JobFor(t)):0}h"
                            + (double.IsInfinity(area) ? "" : area <= 0.0 ? ", in its area" : $", {area:N0} km from its area")
                            + (t.SiteReg != null ? $", site {t.SiteReg}{(t.Engaged ? " engaged" : "")}" : "");
                    }
                    return true;
                case "add":
                {
                    string corp = args.Length > 2 ? args[2] : null;
                    SiteKind? kind = null;
                    if (args.Length > 3)
                    {
                        if (!Enum.TryParse(args[3], true, out SiteKind parsed))
                        {
                            strInput += TraceUsage;
                            return false;
                        }
                        kind = parsed;
                    }
                    if (corp != null && SiteDataLoader.Get()?.Corp(corp) == null)
                    {
                        strInput += $"\nNo corporation named {corp}. 'fz site corps' lists them.";
                        return false;
                    }
                    SignalTrace trace = SignalTraces.NewTrace(CrewSim.coPlayer?.ship, corp, kind);
                    strInput += trace == null ? "\nNo trace could be made." : $"\nAdded trace {trace.Id}: a {trace.Size} {trace.Corp} {trace.Kind} near {trace.Body}, {trace.Steps} narrowing steps.";
                    return trace != null;
                }
                case "step":
                case "locate":
                {
                    SignalTrace trace = args.Length > 2 && int.TryParse(args[2], out int id) ? SignalTraces.Find(id) : null;
                    if (trace == null)
                    {
                        strInput += "\nNo trace with that number." + TraceUsage;
                        return false;
                    }
                    //'step' finishes the current job; 'locate' finishes jobs until the site spawns, wherever the ship is
                    int jobs = sub == "locate" ? trace.Steps + 2 : 1;
                    for (int i = 0; i < jobs && SignalTraces.JobFor(trace) != SignalTraces.Job.None; i++)
                    {
                        SignalTraces.Advance(trace, CrewSim.coPlayer?.ship);
                    }
                    strInput += $"\nTrace {trace.Id} is at level {trace.Level}/{trace.Steps}" + (trace.SiteReg != null ? $", site {trace.SiteReg}." : ".");
                    return true;
                }
                case "clear":
                {
                    int count = SignalTraces.All.Count;
                    foreach (int id in SignalTraces.All.Select(t => t.Id).ToList()) SignalTraces.Discard(id);
                    strInput += $"\nDiscarded {count} trace(s) and any sites they had spawned.";
                    return true;
                }
                default:
                    strInput += TraceUsage;
                    return false;
            }
        }

        private const double DefaultSiteKm = 5.0;
        private const string SiteUsage = "\nUsage: fz site preview <relay|blacksite|all> [small|medium|large|all] [seed] [corp], fz site spawn <relay|blacksite> [small|medium|large] [seed] [km] [corp] [faction], fz site corps, fz site unlock, fz site clear";

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
            if (sub == "corps") return SiteCorps(ref strInput);
            if (sub == "unlock") return SiteUnlock(ref strInput);
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

            string corp = args.Length > 5 ? args[5] : SiteData.GenericCorp;
            if (!SiteDataLoader.Get().Corps.ContainsKey(corp))
            {
                strInput += $"\nNo corporation named {corp}. 'fz site corps' lists them.";
                return false;
            }

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
                    string path = Path.Combine(folder, $"{kind}_{size}_{seed}_{corp}.png".ToLower());
                    SiteLayout layout = SiteShell.Build(grid, SiteDataLoader.Program(kind, corp), SiteCatalogLoader.Get());
                    SitePreview.Write(grid, path, layout);
                    strInput += "\n" + grid.Summary() + $"\n  {layout.Items.Count} items, {layout.Furnished} of them furnishing: {layout.Weapons} weapons with {layout.Rounds} rounds, {layout.Sensors} sensors, {layout.LootSpawners} looted containers";
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
            string corp = args.Length > 6 ? args[6] : SiteData.GenericCorp;
            string owner = args.Length > 7 ? args[7] : null;
            if (!SiteDataLoader.Get().Corps.ContainsKey(corp))
            {
                //A faction where the corporation goes, as the command took before corporations
                if (CrewSim.system?.GetFaction(corp) != null && owner == null)
                {
                    owner = corp;
                    corp = SiteData.GenericCorp;
                }
                else
                {
                    strInput += $"\nNo corporation named {corp}. 'fz site corps' lists them.";
                    return false;
                }
            }

            Ship station = SiteSpawner.SpawnTest(kind, size, seed, km, corp, owner, out string message);
            strInput += "\n" + message;
            return station != null;
        }

        //Opens every data vault's core storage on the current encounter, as hacking it open will
        private static bool SiteUnlock(ref string strInput)
        {
            Ship site = CurrentEncounter();
            if (site == null)
            {
                strInput += "\nYou're not aboard or docked to a station the mod spawned.";
                return false;
            }
            CondTrigger vaults = DataHandler.GetCondTrigger("TIsFzDataVault");
            int opened = 0;
            foreach (CondOwner vault in site.GetCOs(vaults, true, false, true))
            {
                vault.SetCondAmount("IsFzVaultStorageOpen", 1.0);
                opened++;
            }
            strInput += $"\nOpened the core storage of {opened} data vault(s) on {site.publicName}.";
            return opened > 0;
        }

        private static bool SiteCorps(ref string strInput)
        {
            SiteData data = SiteDataLoader.Get();
            foreach (JsonSiteCorp corp in data.Corps.Values.OrderBy(c => c.strName))
            {
                strInput += $"\n{corp.strName}: {corp.strNameFriendly}, owned by {corp.strFaction ?? "no one"}, runs {string.Join(" and ", corp.aKinds ?? new string[0])}";
                foreach (SiteKind kind in new[] { SiteKind.Relay, SiteKind.BlackSite })
                {
                    SiteProgram program = SiteDataLoader.Program(kind, corp.strName);
                    strInput += $"\n  {kind}: {program.SpecialisedBlueprints.Count} Specialised and {program.OrdinaryBlueprints.Count} Ordinary blueprints, {program.BlueprintsMin} to {program.BlueprintsMax} a vault";
                }
            }
            return true;
        }

        private static bool KeywordEncounter(ref string strInput, string[] args)
        {
            if (args.Length == 2 && args[1].ToLower() == "clear")
            {
                TestEncounter.Clear(out string cleared);
                strInput += "\n" + cleared;
                return true;
            }
            if (args.Length == 2 && args[1].ToLower() == "clearnpcs")
            {
                int removed = TestEncounter.ClearNpcs(out string cleared);
                strInput += "\n" + cleared;
                return removed > 0;
            }
            if (args.Length == 2 && args[1].ToLower() == "factions")
            {
                var factions = CrewSim.system?.GetAllFactions();
                if (factions == null || factions.Count == 0)
                {
                    strInput += "\nNo factions; load a game first.";
                    return false;
                }
                strInput += "\nFactions an encounter can be owned by:";
                foreach (JsonFaction faction in factions.Where(f => f != null && !string.IsNullOrEmpty(f.strName)).OrderBy(f => f.strName))
                {
                    strInput += $"\n  {faction.strName}  ({faction.strNameFriendly})";
                }
                return true;
            }
            if (args.Length == 2 && args[1].ToLower() == "disarm")
            {
                int disarmed = TestEncounter.Disarm(out string result);
                strInput += "\n" + result;
                return disarmed > 0;
            }

            double km = DefaultKm;
            if (args.Length > 1 && (!double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out km) || km <= 0.0))
            {
                strInput += "\nUsage: fz encounter [km] [template] [faction], fz encounter disarm, fz encounter clearnpcs, fz encounter factions or fz encounter clear";
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
