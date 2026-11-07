using Freznel.FzAdditions.Util.AI;
using Freznel.FzAdditions.Util.SiteGen;
using Ostranauts.ShipGUIs.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //One signal a ship's sideband interceptors picked up: a corporate site that doesn't exist yet. Everything about the
    //site is decided when the signal is heard (its corporation, kind, size, seed and the body it's near), but it's only
    //spawned once the trace has been narrowed all the way down.
    public class SignalTrace
    {
        public int Id;
        public string Corp;
        public SiteKind Kind;
        public SiteSize Size;
        public int Seed;
        public string Body;          //The body orbit the site is near
        public double Px, Py;        //Where in the end it's likely to be, in AU from the body's centre
        public int Steps;            //Narrowing steps after the reveal, 2 to 4; the last one finds the site
        public int Level = -1;       //-1 unknown, 0 near the body, 1 to Steps - 1 a circle, Steps located
        public double Progress;      //Standard hours of work done on the current job
        public double Detected;      //Game epoch it was heard
        public string SiteReg;       //The spawned site, while there is one
        public double SpawnedAt;
        public bool Engaged;         //The player's ship has come close to the spawned site

        public bool Revealed => Level >= 0;
        public bool Located => Level >= Steps;
    }

    //Signal traces: global state, kept in the save's custom infos (FzSignalTraces) beside the learned blueprints, since
    //they belong to the player rather than to a ship. The list is the live copy between saves and is emptied whenever a
    //game is left.
    //
    //Sideband interceptors (IsFzInterceptor) on the player's ships listen while they're on and powered. Each game hour
    //there's a chance of a new trace, better with more interceptors, far from bodies, and with a hacking-skilled player.
    //One trace at a time is investigated, passively: first to reveal what sent it and the body it's near, then, with the
    //player's ship in the area each time, to narrow where it is. Narrowing ends by spawning the site, out of the player's
    //range, as a FzSpawns kind of its own (Trace). A site nobody comes near within a few days goes again, and the trace
    //then has to be reacquired from the area. Docking at the site closes the trace; the site goes once the player has
    //left and is far away.
    public static class SignalTraces
    {
        public const string SpawnKind = "Trace";
        public const string RegPrefix = "FZS-";
        public const string SaveKey = "FzSignalTraces";
        public const string BoardedCond = "IsFzTraceBoarded";
        public const string InterceptorCond = "IsFzInterceptor";
        public const string InterceptorOnCond = "IsFzInterceptorOn";
        public const int MaxTraces = 8;

        private const double KmPerAU = 149597872.0;
        private const double MetresPerAU = 149597870700.0;
        private const double G = 6.674e-11;

        //Hearing a signal
        private const double DetectChancePerHour = 0.03; //One interceptor, open space, unskilled
        private const int InterceptorCap = 6;            //More than this adds nothing
        private const double SkilledDetect = 1.75;
        private const double OpenPull = 0.05;            //m/s², at or above which a body's pull drowns the sidebands out
        private const double OpenDecades = 2.0;          //How many tenfold drops in pull below that reach open space
        private const double MinOpen = 0.1;

        //Investigating, in standard hours of work
        private const double RevealHours = 6.0;
        private const double NarrowHours = 8.0;
        private const double ReacquireHours = 3.0;
        private const double SkilledInvestigate = 1.5;

        //Where a site is
        private const double SiteMinKm = 25000.0;  //From the body's surface
        private const double SiteMaxKm = 150000.0;
        private const double MinBodyKm = 150.0;     //Smaller bodies are left out
        private const double SafeKm = 8000.0;       //A site spawns at least this far from the player's ship
        private const double FinalRadiusKm = 20000.0; //The last circle; well over SafeKm, so there's always room
        private const double EngageKm = 2000.0;
        private const double LeaveKm = 20000.0;     //A boarded site goes once the player's ship is this far away
        private const double DespawnSeconds = 3.0 * 86400.0;

        private const float TickInterval = 2f; //Real seconds

        public enum Job { None, Reveal, Narrow, Reacquire }

        public struct Readings
        {
            public Ship Ship;
            public int Active;     //Interceptors on and powered
            public int Installed;
            public double Open;    //0.1 to 1, how far from any body's pull
            public bool Skilled;
            public double DetectPerHour;
            public double Speed;   //Standard hours of investigation per game hour
        }

        private static readonly List<SignalTrace> traces = new List<SignalTrace>();
        private static int nextId = 1;
        private static int investigating;
        private static double lastEpoch = -1.0;
        private static float nextTick;
        private static readonly System.Random random = new System.Random();

        public static IReadOnlyList<SignalTrace> All => traces;
        public static int Investigating => investigating;
        public static SignalTrace Find(int id) => traces.FirstOrDefault(t => t.Id == id);

        public static void Reset()
        {
            traces.Clear();
            nextId = 1;
            investigating = 0;
            lastEpoch = -1.0;
        }

        //Whether a Trace spawn still has a purpose, for FzSpawns' sweep
        public static bool IsWanted(Ship ship)
        {
            if (ship == null) return false;
            if (ship.ShipCO != null && ship.ShipCO.HasCond(BoardedCond)) return true;
            return traces.Any(t => t.SiteReg == ship.strRegID);
        }

        //The player's investigation, by trace; 0 for none
        public static void Assign(int id)
        {
            investigating = Find(id) != null ? id : 0;
        }

        public static void Discard(int id)
        {
            SignalTrace trace = Find(id);
            if (trace == null) return;
            RemoveSite(trace);
            traces.Remove(trace);
            if (investigating == id) investigating = 0;
        }

        public static Job JobFor(SignalTrace trace)
        {
            if (trace == null) return Job.None;
            if (!trace.Revealed) return Job.Reveal;
            if (!trace.Located) return Job.Narrow;
            return SiteOf(trace) == null ? Job.Reacquire : Job.None;
        }

        public static double JobHours(Job job)
        {
            switch (job)
            {
                case Job.Reveal: return RevealHours;
                case Job.Narrow: return NarrowHours;
                case Job.Reacquire: return ReacquireHours;
                default: return 0.0;
            }
        }

        public static bool NeedsArea(Job job) => job == Job.Narrow || job == Job.Reacquire;

        public static Ship SiteOf(SignalTrace trace)
        {
            if (trace?.SiteReg == null || CrewSim.system == null) return null;
            Ship ship = CrewSim.system.GetShipByRegID(trace.SiteReg);
            return ship == null || ship.bDestroyed ? null : ship;
        }

        public static BodyOrbit BodyOf(SignalTrace trace)
        {
            return trace?.Body == null ? null : CrewSim.system?.GetBO(trace.Body);
        }

        //What the player has to listen with right now
        public static Readings Read()
        {
            var r = new Readings { Open = MinOpen };
            CondOwner player = CrewSim.coPlayer;
            Ship ship = player?.ship;
            if (ship == null) return r;
            r.Ship = ship;
            r.Skilled = player.HasCond("SkillHacking");

            foreach (Ship member in PlayersShips(ship))
            {
                foreach (CondOwner co in member.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true))
                {
                    if (co == null || !co.HasCond(InterceptorCond) || !co.HasCond("IsInstalled")) continue;
                    r.Installed++;
                    if (co.HasCond(InterceptorOnCond) && co.HasCond("IsPowered") && !co.HasCond("IsDamaged")) r.Active++;
                }
            }

            r.Open = OpenSpace(ship);
            double count = Math.Sqrt(Math.Min(r.Active, InterceptorCap));
            r.DetectPerHour = r.Active == 0 ? 0.0 : DetectChancePerHour * count * r.Open * (r.Skilled ? SkilledDetect : 1.0);
            r.Speed = r.Active == 0 ? 0.0 : count * (0.5 + 0.5 * r.Open) * (r.Skilled ? SkilledInvestigate : 1.0);
            return r;
        }

        //The ship the player is on, and the ships of theirs docked to it; a station they're docked to doesn't listen for them
        private static IEnumerable<Ship> PlayersShips(Ship ship)
        {
            if (SiteIdentity.IsPlayers(ship)) yield return ship;
            foreach (Ship docked in ship.GetAllDockedShips())
            {
                if (docked != null && docked != ship && SiteIdentity.IsPlayers(docked)) yield return docked;
            }
        }

        //From the pull of the body pulling hardest: 1 in open space, down to MinOpen close to a planet or moon
        public static double OpenSpace(Ship ship)
        {
            StarSystem system = CrewSim.system;
            if (system == null || ship?.objSS == null) return MinOpen;
            Vector2 pull = default;
            BodyOrbit closest = null;
            BodyOrbit body = system.GetGreatestGravBO(ship.objSS, StarSystem.fEpoch, ref pull, ref closest);
            if (body == null || body.fMass <= 0.0) return 1.0;
            body.UpdateTime(StarSystem.fEpoch);
            double dx = ship.objSS.vPosx - body.dXReal, dy = ship.objSS.vPosy - body.dYReal;
            double metres = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy) * MetresPerAU);
            double accel = G * body.fMass / (metres * metres);
            double open = Math.Log10(OpenPull / accel) / OpenDecades;
            return Math.Max(MinOpen, Math.Min(1.0, open));
        }

        public static void Tick()
        {
            if (CrewSim.system == null || CrewSim.coPlayer?.ship == null) return;
            if (Time.realtimeSinceStartup < nextTick) return;
            nextTick = Time.realtimeSinceStartup + TickInterval;

            double now = StarSystem.fEpoch;
            if (lastEpoch < 0.0 || now < lastEpoch)
            {
                lastEpoch = now;
                return;
            }
            double hours = (now - lastEpoch) / 3600.0;
            lastEpoch = now;

            Readings r = Read();
            Listen(r, hours);
            Investigate(r, hours);
            KeepSites(r, now);
        }

        private static void Listen(Readings r, double hours)
        {
            if (r.DetectPerHour <= 0.0 || traces.Count >= MaxTraces || hours <= 0.0) return;
            double chance = 1.0 - Math.Pow(1.0 - Math.Min(1.0, r.DetectPerHour), hours);
            if (random.NextDouble() >= chance) return;
            SignalTrace trace = NewTrace(r.Ship, null, null);
            if (trace == null) return;
            Log($"Your sideband interceptors logged an encrypted signal (trace {trace.Id}). A Signal Traces navmod can work on it.", "Good");
        }

        private static void Investigate(Readings r, double hours)
        {
            SignalTrace trace = Find(investigating);
            Job job = JobFor(trace);
            if (job == Job.None || r.Speed <= 0.0 || hours <= 0.0) return;
            if (NeedsArea(job) && !InArea(trace, r.Ship)) return;

            trace.Progress += hours * r.Speed;
            if (trace.Progress < JobHours(job)) return;
            Advance(trace, r.Ship);
        }

        //Finishes the trace's current job
        public static void Advance(SignalTrace trace, Ship player)
        {
            Job job = JobFor(trace);
            trace.Progress = 0.0;
            switch (job)
            {
                case Job.Reveal:
                    trace.Level = 0;
                    Log($"Trace {trace.Id} decoded: {KindName(trace)} traffic with a {FactionName(trace)} signature, from near {trace.Body}.", "Good");
                    break;
                case Job.Narrow:
                    trace.Level++;
                    if (!trace.Located)
                    {
                        Log($"Trace {trace.Id} narrowed to within {RadiusKm(trace, trace.Level):N0} km. Take the ship there to narrow it again.", "Good");
                        break;
                    }
                    Spawn(trace, player);
                    break;
                case Job.Reacquire:
                    Spawn(trace, player);
                    break;
            }
        }

        //Spawned sites: noting the player coming close, letting unvisited ones go, and taking boarded ones away once the
        //player has left them
        private static void KeepSites(Readings r, double now)
        {
            foreach (SignalTrace trace in traces)
            {
                if (trace.SiteReg == null) continue;
                Ship site = SiteOf(trace);
                if (site == null)
                {
                    trace.SiteReg = null;
                    continue;
                }
                double km = DistanceKm(r.Ship, site);
                if (km <= EngageKm) trace.Engaged = true;
                if (trace.Engaged || now - trace.SpawnedAt < DespawnSeconds) continue;
                if (FzSpawns.IsInUse(site) || km < SafeKm * 3.0) continue;
                RemoveSite(trace);
                Log($"The signal behind trace {trace.Id} has gone quiet. Reacquire it from the area to find the site again.", "Neutral");
            }

            foreach (Ship ship in CrewSim.system.dictShips.Values.ToList())
            {
                if (ship == null || ship.ShipCO == null || !ship.ShipCO.HasCond(BoardedCond)) continue;
                if (FzSpawns.IsInUse(ship) || DistanceKm(r.Ship, ship) < LeaveKm) continue;
                FzSpawns.Remove(ship);
            }
        }

        private static void RemoveSite(SignalTrace trace)
        {
            Ship site = SiteOf(trace);
            if (site != null && !FzSpawns.IsInUse(site)) FzSpawns.Remove(site);
            trace.SiteReg = null;
            trace.Engaged = false;
        }

        //A ship of the player's has docked: docking at a traced site closes its trace, and the site goes once they've left
        public static void Docked(Ship ship, Ship site)
        {
            if (site?.ShipCO == null || FzSpawns.KindOf(site) != SpawnKind || !SiteIdentity.IsPlayers(ship)) return;
            site.ShipCO.SetCondAmount(BoardedCond, 1.0);
            SignalTrace trace = traces.FirstOrDefault(t => t.SiteReg == site.strRegID);
            if (trace == null) return;
            traces.Remove(trace);
            if (investigating == trace.Id) investigating = 0;
            Log($"Trace {trace.Id} closed: you've reached its source.", "Good");
        }

        //A new trace near a body, weighted towards nearby ones. Corporation and kind are picked at random unless given.
        public static SignalTrace NewTrace(Ship near, string corpName, SiteKind? kind)
        {
            StarSystem system = CrewSim.system;
            SiteData data = SiteDataLoader.Get();
            if (system == null || data == null) return null;

            JsonSiteCorp corp = corpName != null ? data.Corp(corpName) : PickCorp(data, kind);
            if (corp == null) return null;
            SiteKind siteKind = kind ?? PickKind(corp);

            BodyOrbit body = PickBody(system, near);
            if (body == null) return null;

            double bodyKm = body.fRadiusKM;
            double distKm = bodyKm + SiteMinKm + random.NextDouble() * (SiteMaxKm - SiteMinKm);
            double angle = random.NextDouble() * Math.PI * 2.0;
            var trace = new SignalTrace
            {
                Id = nextId++,
                Corp = corp.strName,
                Kind = siteKind,
                Size = PickSize(),
                Seed = random.Next(1, 1000000),
                Body = body.strName,
                Px = Math.Cos(angle) * distKm / KmPerAU,
                Py = Math.Sin(angle) * distKm / KmPerAU,
                Steps = random.Next(2, 5),
                Detected = StarSystem.fEpoch
            };
            traces.Add(trace);
            return trace;
        }

        private static JsonSiteCorp PickCorp(SiteData data, SiteKind? kind)
        {
            var corps = data.Corps.Values.Where(c => c != null && (kind == null || c.aKinds == null || c.aKinds.Contains(kind.ToString()))).ToList();
            if (corps.Count == 0) return null;
            //The unmarked operator is the fallback for testing, so it turns up less
            double total = corps.Sum(Weight);
            double pick = random.NextDouble() * total;
            foreach (JsonSiteCorp c in corps)
            {
                pick -= Weight(c);
                if (pick <= 0.0) return c;
            }
            return corps[corps.Count - 1];
        }

        private static double Weight(JsonSiteCorp corp) => corp.strName == SiteData.GenericCorp ? 0.5 : 1.0;

        private static SiteKind PickKind(JsonSiteCorp corp)
        {
            var kinds = (corp.aKinds ?? new[] { "Relay", "BlackSite" })
                .Select(k => Enum.TryParse(k, out SiteKind parsed) ? (SiteKind?)parsed : null).Where(k => k != null).Select(k => k.Value).ToList();
            if (kinds.Count == 0) return SiteKind.Relay;
            if (kinds.Count == 1) return kinds[0];
            return random.NextDouble() < 0.6 && kinds.Contains(SiteKind.Relay) ? SiteKind.Relay : kinds.First(k => k != SiteKind.Relay);
        }

        private static SiteSize PickSize()
        {
            double roll = random.NextDouble();
            return roll < 0.45 ? SiteSize.Small : roll < 0.8 ? SiteSize.Medium : SiteSize.Large;
        }

        //Planets, moons and large asteroids, not stations or fields, weighted towards ones near the ship
        private static BodyOrbit PickBody(StarSystem system, Ship near)
        {
            var bodies = system.aBOs.Values.Where(IsBody).ToList();
            if (bodies.Count == 0) return null;
            double sx = near?.objSS?.vPosx ?? 0.0, sy = near?.objSS?.vPosy ?? 0.0;
            var weights = bodies.Select(b =>
            {
                b.UpdateTime(StarSystem.fEpoch);
                double dx = b.dXReal - sx, dy = b.dYReal - sy;
                return 1.0 / (0.5 + Math.Sqrt(dx * dx + dy * dy));
            }).ToList();
            double pick = random.NextDouble() * weights.Sum();
            for (int i = 0; i < bodies.Count; i++)
            {
                pick -= weights[i];
                if (pick <= 0.0) return bodies[i];
            }
            return bodies[bodies.Count - 1];
        }

        private static bool IsBody(BodyOrbit bo)
        {
            if (bo == null || bo.boParent == null || bo.IsPlaceholder() || bo.IsShipOrbit() || bo is AsteroidField) return false;
            if (bo.fRadiusKM < MinBodyKm || bo.fMass <= 0.0) return false;
            return CrewSim.system.GetShipByRegID(bo.strName) == null;
        }

        //The area a job needs the player's ship in, in AU from the body's centre: near the body before any narrowing,
        //then each circle in turn, and the last circle to reacquire a located site
        public static bool GetArea(SignalTrace trace, out double cx, out double cy, out double radius)
        {
            cx = cy = radius = 0.0;
            if (trace == null || !trace.Revealed) return false;
            int level = Math.Min(trace.Level, trace.Steps - 1);
            if (level <= 0)
            {
                radius = Math.Sqrt(trace.Px * trace.Px + trace.Py * trace.Py) + Radius(trace, 1);
                return true;
            }
            Circle(trace, level, out cx, out cy, out radius);
            return true;
        }

        //Circle k's radius: from most of the site's distance from the body down to FinalRadiusKm, evenly by ratio
        private static double Radius(SignalTrace trace, int k)
        {
            double final = FinalRadiusKm / KmPerAU;
            int circles = trace.Steps - 1;
            if (circles <= 1) return final;
            double first = Math.Max(final, 0.6 * Math.Sqrt(trace.Px * trace.Px + trace.Py * trace.Py));
            double t = (double)(Math.Max(1, k) - 1) / (circles - 1);
            return first * Math.Pow(final / first, Math.Min(1.0, t));
        }

        public static double RadiusKm(SignalTrace trace, int k) => Radius(trace, k) * KmPerAU;

        //Each circle holds the likely spot, off centre by up to 60% of its radius, the same every time for a trace
        private static void Circle(SignalTrace trace, int k, out double cx, out double cy, out double radius)
        {
            radius = Radius(trace, k);
            var rng = new System.Random(unchecked(trace.Seed * 31 + trace.Id * 7919 + k * 104729));
            double angle = rng.NextDouble() * Math.PI * 2.0;
            double off = Math.Sqrt(rng.NextDouble()) * 0.6 * radius;
            cx = trace.Px + Math.Cos(angle) * off;
            cy = trace.Py + Math.Sin(angle) * off;
        }

        //The player's ship's offset from the trace's body, in AU
        private static bool Offset(SignalTrace trace, Ship ship, out double x, out double y)
        {
            x = y = 0.0;
            BodyOrbit body = BodyOf(trace);
            if (body == null || ship?.objSS == null) return false;
            body.UpdateTime(StarSystem.fEpoch);
            x = ship.objSS.vPosx - body.dXReal;
            y = ship.objSS.vPosy - body.dYReal;
            return true;
        }

        public static bool InArea(SignalTrace trace, Ship ship) => DistanceToAreaKm(trace, ship) <= 0.0;

        //How far outside the job's area the ship is, 0 or less inside it; infinite if it can't be told
        public static double DistanceToAreaKm(SignalTrace trace, Ship ship)
        {
            if (!GetArea(trace, out double cx, out double cy, out double radius) || !Offset(trace, ship, out double x, out double y)) return double.PositiveInfinity;
            double dx = x - cx, dy = y - cy;
            return (Math.Sqrt(dx * dx + dy * dy) - radius) * KmPerAU;
        }

        private static double DistanceKm(Ship a, Ship b)
        {
            if (a?.objSS == null || b?.objSS == null) return double.PositiveInfinity;
            double dx = a.objSS.vPosx - b.objSS.vPosx, dy = a.objSS.vPosy - b.objSS.vPosy;
            return Math.Sqrt(dx * dx + dy * dy) * KmPerAU;
        }

        //Where the nav console's crosshair should go for a trace: the site once it's there, else the job's area
        public static NavPOI TargetFor(SignalTrace trace)
        {
            Ship site = SiteOf(trace);
            BodyOrbit siteOrbit = site != null ? CrewSim.system.GetBO(site.strRegID) : null;
            if (siteOrbit != null) return new NavPOI(siteOrbit);
            BodyOrbit body = BodyOf(trace);
            if (body == null || !GetArea(trace, out double cx, out double cy, out _)) return null;
            return new NavPOI(body) { fOffsetSX = cx, fOffsetSY = cy };
        }

        //Builds and spawns the site, out of the player's range in the last circle
        private static void Spawn(SignalTrace trace, Ship player)
        {
            BodyOrbit body = BodyOf(trace);
            if (body == null)
            {
                Log($"Trace {trace.Id} led to {trace.Body}, which is gone.", "Bad");
                return;
            }

            string template = null;
            for (int i = 0; i < 10 && template == null; i++)
            {
                template = SiteSpawner.Register(trace.Kind, trace.Size, trace.Seed + i, trace.Corp);
                if (template != null) trace.Seed += i;
            }
            if (template == null)
            {
                FzAdditions.Logger.LogWarning($"Trace {trace.Id}: no {trace.Size} {trace.Kind} could be laid out near seed {trace.Seed}.");
                return;
            }

            PickSpawnPoint(trace, player, body, out double x, out double y);
            FzFactions.Ensure();
            JsonSiteCorp corp = SiteDataLoader.Get().Corp(trace.Corp);
            string publicName = DataHandler.GetShip(template)?.publicName ?? "Unknown Station";
            Ship station = StationPlacer.SpawnAt(new StationPlacer.Request
            {
                Template = template,
                PublicName = publicName.Replace("{", "(").Replace("}", ")"),
                Owner = corp?.strFaction,
                Kind = SpawnKind,
                StartingConds = new[]
                {
                    $"{FzAI.PilotCond(FzStationDefensePilot.TypeName)}=1.0x1",
                    $"{FzAI.AlertCond}=1.0x{FzAI.Hostile}"
                }
            }, body, x, y, random.Next(2) == 0 ? 1 : -1, out string message);
            FzAdditions.Logger.LogInfo($"Trace {trace.Id}: {message}");
            if (station == null) return;

            trace.SiteReg = station.strRegID;
            trace.SpawnedAt = StarSystem.fEpoch;
            trace.Engaged = false;
            Log($"Trace {trace.Id} located: {station.publicName}, near {trace.Body}. It won't stay on the air for long.", "Good");
        }

        //The likely spot if it's far enough from the player's ship, else somewhere else in the last circle that is
        private static void PickSpawnPoint(SignalTrace trace, Ship player, BodyOrbit body, out double x, out double y)
        {
            Circle(trace, trace.Steps - 1, out double cx, out double cy, out double radius);
            Offset(trace, player, out double px, out double py);
            double minFromBody = (body.fRadiusKM + 2000.0) / KmPerAU;
            double safe = SafeKm / KmPerAU;

            bool Good(double tx, double ty) =>
                Math.Sqrt((tx - px) * (tx - px) + (ty - py) * (ty - py)) >= safe && Math.Sqrt(tx * tx + ty * ty) >= minFromBody;

            x = trace.Px;
            y = trace.Py;
            if (player == null || Good(x, y)) return;

            double best = -1.0;
            for (int i = 0; i < 64; i++)
            {
                double a = random.NextDouble() * Math.PI * 2.0, d = Math.Sqrt(random.NextDouble()) * radius;
                double tx = cx + Math.Cos(a) * d, ty = cy + Math.Sin(a) * d;
                if (Math.Sqrt(tx * tx + ty * ty) < minFromBody) continue;
                double apart = Math.Sqrt((tx - px) * (tx - px) + (ty - py) * (ty - py));
                if (apart > best)
                {
                    best = apart;
                    x = tx;
                    y = ty;
                }
                if (apart >= safe) return;
            }
        }

        public static string KindName(SignalTrace trace) => trace.Kind == SiteKind.Relay ? "relay" : "black site";

        public static string FactionName(SignalTrace trace)
        {
            JsonSiteCorp corp = SiteDataLoader.Get()?.Corp(trace.Corp);
            string faction = corp?.strFaction != null ? CrewSim.system?.GetFaction(corp.strFaction)?.strNameFriendly : null;
            return faction ?? corp?.strNameFriendly ?? "unmarked";
        }

        private static void Log(string text, string color)
        {
            CrewSim.coPlayer?.LogMessage(text, color, "Game");
        }

        //Saved as "1|nextId|investigating" then one entry a trace, all split by ';'
        public static string Save()
        {
            var sb = new StringBuilder();
            sb.Append("1|").Append(nextId).Append('|').Append(investigating);
            foreach (SignalTrace t in traces)
            {
                sb.Append(';').Append(string.Join("|", new[]
                {
                    t.Id.ToString(CultureInfo.InvariantCulture), Clean(t.Corp), t.Kind.ToString(), t.Size.ToString(),
                    t.Seed.ToString(CultureInfo.InvariantCulture), Clean(t.Body), D(t.Px), D(t.Py),
                    t.Steps.ToString(CultureInfo.InvariantCulture), t.Level.ToString(CultureInfo.InvariantCulture), D(t.Progress), D(t.Detected),
                    Clean(t.SiteReg), D(t.SpawnedAt), t.Engaged ? "1" : "0"
                }));
            }
            return sb.ToString();
        }

        public static void Load(string value)
        {
            Reset();
            if (string.IsNullOrEmpty(value)) return;
            string[] entries = value.Split(';');
            string[] head = entries[0].Split('|');
            if (head.Length >= 3)
            {
                int.TryParse(head[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out nextId);
                int.TryParse(head[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out investigating);
            }
            for (int i = 1; i < entries.Length; i++)
            {
                string[] f = entries[i].Split('|');
                if (f.Length < 15) continue;
                try
                {
                    var t = new SignalTrace
                    {
                        Id = int.Parse(f[0], CultureInfo.InvariantCulture),
                        Corp = Null(f[1]),
                        Kind = (SiteKind)Enum.Parse(typeof(SiteKind), f[2]),
                        Size = (SiteSize)Enum.Parse(typeof(SiteSize), f[3]),
                        Seed = int.Parse(f[4], CultureInfo.InvariantCulture),
                        Body = Null(f[5]),
                        Px = P(f[6]),
                        Py = P(f[7]),
                        Steps = int.Parse(f[8], CultureInfo.InvariantCulture),
                        Level = int.Parse(f[9], CultureInfo.InvariantCulture),
                        Progress = P(f[10]),
                        Detected = P(f[11]),
                        SiteReg = Null(f[12]),
                        SpawnedAt = P(f[13]),
                        Engaged = f[14] == "1"
                    };
                    traces.Add(t);
                    nextId = Math.Max(nextId, t.Id + 1);
                }
                catch (Exception ex)
                {
                    FzAdditions.Logger.LogWarning($"Skipped a saved signal trace that couldn't be read ({entries[i]}): {ex.Message}");
                }
            }
            if (Find(investigating) == null) investigating = 0;
        }

        private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        private static string Clean(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace("|", "/").Replace(";", ",");
        private static string Null(string s) => s == "-" ? null : s;
    }
}
