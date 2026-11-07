using Freznel.FzAdditions.Util.AI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //Everything the mod spawns into the system (stations, and later ships), marked so it can be found and cleared up.
    //
    //A spawn carries two persisted conditions on its ShipCO: IsFzSpawned, and IsFzSpawn<Kind> for the kind that made
    //it. Sections register their kinds when the plugin loads, each with a registration prefix its spawns are given and,
    //if it can tell, a check for whether a spawn is still wanted (a mission site whose mission has ended isn't).
    //
    //Sweep removes orphans: spawns whose kind says they're no longer wanted, spawns of a kind no section registers any
    //more, stations whose orbit has gone, and spawns that failed partway, which have a kind's registration prefix but
    //not its conditions (StarSystem.SpawnStationFromJSON adds the ship and its orbit before the starting conditions).
    //Orbits with a kind's prefix and no ship go too. It runs once after each load, as the game first unpauses, and
    //before every save, so no save keeps an orphan. Nothing the player is aboard or docked to is removed.
    public static class FzSpawns
    {
        public const string SpawnedCond = "IsFzSpawned";
        public const string KindCondPrefix = "IsFzSpawn";

        private class Kind
        {
            public string Name;
            public string RegPrefix;
            public Func<Ship, bool> IsWanted;
            public string LegacyCond;
        }

        private static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>();
        private static bool sweepPending = true;

        public static string KindCond(string kind) => KindCondPrefix + kind;

        //isWanted, if given, says whether a spawn of this kind still has a purpose. regPrefix starts the registration of
        //every spawn of the kind, such as "FZT-"; it has to be unique to the kind. legacyCond is a condition older
        //versions marked the kind with: the sweep moves spawns carrying it onto the current marks before judging them.
        public static void RegisterKind(string kind, string regPrefix, Func<Ship, bool> isWanted = null, string legacyCond = null)
        {
            Kinds[kind] = new Kind { Name = kind, RegPrefix = regPrefix, IsWanted = isWanted, LegacyCond = legacyCond };
        }

        public static IEnumerable<string> KindNames => Kinds.Keys.OrderBy(k => k);

        //Starting conditions that mark a spawn, for a JsonSpawnStation's aStartingConds
        public static string[] MarkConds(string kind) => new[] { $"{SpawnedCond}=1.0x1", $"{KindCond(kind)}=1.0x1" };

        public static void Mark(Ship ship, string kind)
        {
            if (ship?.ShipCO == null) return;
            ship.ShipCO.SetCondAmount(SpawnedCond, 1.0);
            ship.ShipCO.SetCondAmount(KindCond(kind), 1.0);
        }

        //The registered kind a ship's conditions name, if any
        public static string KindOf(Ship ship)
        {
            CondOwner co = ship?.ShipCO;
            if (co == null) return null;
            foreach (string kind in Kinds.Keys)
            {
                if (co.HasCond(KindCond(kind))) return kind;
            }
            return null;
        }

        public static bool IsSpawn(Ship ship)
        {
            if (ship == null) return false;
            if (ship.ShipCO != null && ship.ShipCO.HasCond(SpawnedCond)) return true;
            return PrefixKind(ship.strRegID) != null;
        }

        //The kind whose registration prefix a registration starts with
        private static Kind PrefixKind(string regID)
        {
            if (regID == null) return null;
            return Kinds.Values.FirstOrDefault(k => !string.IsNullOrEmpty(k.RegPrefix) && regID.StartsWith(k.RegPrefix));
        }

        //A registration for a new spawn of a kind, free in the system as a ship and as an orbit
        public static string NewRegID(string kind)
        {
            StarSystem system = CrewSim.system;
            string prefix = Kinds.TryGetValue(kind, out Kind k) ? k.RegPrefix : "FZ-";
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string id = prefix;
                for (int i = 0; i < 3; i++) id += chars[UnityEngine.Random.Range(0, chars.Length)];
                if (system.GetShipByRegID(id) == null && system.GetBO(id) == null) return id;
            }
            return prefix + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
        }

        //Whether the player is aboard or docked to a ship, in which case it's left alone
        public static bool IsInUse(Ship ship)
        {
            Ship player = CrewSim.GetSelectedCrew()?.ship;
            if (player == null || ship == null) return false;
            return ship == player || ship.GetAllDockedShips().Contains(player);
        }

        //Takes a spawn out of the system with its orbit, if it's a station
        public static void Remove(Ship ship)
        {
            StarSystem system = CrewSim.system;
            if (system == null || ship == null) return;
            FzAI.Detach(ship);
            BodyOrbit orbit = system.GetBO(ship.strRegID);
            ship.Destroy(isDespawning: false);
            if (orbit != null) system.RemoveBO(orbit);
        }

        //Takes the half-made remains of a spawn that failed, by its registration: its ship if one was made, its orbit if one was
        public static void RemoveRemains(string regID)
        {
            StarSystem system = CrewSim.system;
            if (system == null || regID == null) return;
            Ship ship = system.GetShipByRegID(regID);
            if (ship != null) Remove(ship);
            BodyOrbit orbit = system.GetBO(regID);
            if (orbit != null) system.RemoveBO(orbit);
        }

        //Removes every spawn of a kind that the filter picks, except ones in use. Returns how many went.
        public static int RemoveKind(string kind, out List<string> kept, Func<Ship, bool> filter = null)
        {
            kept = new List<string>();
            StarSystem system = CrewSim.system;
            if (system?.dictShips == null) return 0;
            int removed = 0;
            foreach (Ship ship in system.dictShips.Values.ToList())
            {
                if (ship == null || !IsSpawn(ship)) continue;
                string shipKind = KindOf(ship) ?? PrefixKind(ship.strRegID)?.Name;
                if (shipKind != kind || (filter != null && !filter(ship))) continue;
                if (IsInUse(ship))
                {
                    kept.Add(ship.strRegID);
                    continue;
                }
                Remove(ship);
                removed++;
            }
            return removed;
        }

        //Why a spawn is an orphan, or null if it isn't
        public static string OrphanReason(Ship ship)
        {
            if (ship == null || !IsSpawn(ship)) return null;
            string kind = KindOf(ship);
            if (kind == null)
            {
                return PrefixKind(ship.strRegID) != null ? "it failed partway through spawning" : "no section registers its kind any more";
            }
            if (ship.objSS != null && ship.objSS.bIsBO && CrewSim.system.GetBO(ship.strRegID) == null) return "its orbit is gone";
            Func<Ship, bool> isWanted = Kinds[kind].IsWanted;
            if (isWanted != null && !isWanted(ship)) return "it's no longer wanted";
            return null;
        }

        //Removes every orphan, and orbits left with no ship. Returns a line for each one.
        public static List<string> Sweep()
        {
            var report = new List<string>();
            StarSystem system = CrewSim.system;
            if (system?.dictShips == null || Kinds.Count == 0) return report;

            //Spawns marked the way older versions did
            foreach (Kind kind in Kinds.Values.Where(k => k.LegacyCond != null))
            {
                foreach (Ship ship in system.dictShips.Values)
                {
                    if (ship?.ShipCO == null || !ship.ShipCO.HasCond(kind.LegacyCond)) continue;
                    Mark(ship, kind.Name);
                    ship.ShipCO.ZeroCondAmount(kind.LegacyCond);
                }
            }

            foreach (Ship ship in system.dictShips.Values.ToList())
            {
                string reason;
                try
                {
                    reason = OrphanReason(ship);
                }
                catch (Exception ex)
                {
                    FzAdditions.Logger.LogError($"Checking spawn {ship?.strRegID} failed: {ex}");
                    continue;
                }
                if (reason == null) continue;
                if (IsInUse(ship))
                {
                    report.Add($"Kept orphan {ship.strRegID} ({reason}): you're aboard or docked to it.");
                    continue;
                }
                Remove(ship);
                report.Add($"Removed {ship.strRegID}: {reason}.");
            }

            if (system.aBOs != null)
            {
                foreach (BodyOrbit orbit in system.aBOs.Values.ToList())
                {
                    if (orbit == null || PrefixKind(orbit.strName) == null || system.GetShipByRegID(orbit.strName) != null) continue;
                    system.RemoveBO(orbit);
                    report.Add($"Removed orbit {orbit.strName}, which had no station.");
                }
            }

            foreach (string line in report) FzAdditions.Logger.LogInfo("FzSpawns: " + line);
            return report;
        }

        //Runs the sweep a load asks for, the first time the game runs after it
        public static void Tick()
        {
            if (!sweepPending || CrewSim.system == null) return;
            sweepPending = false;
            Sweep();
        }

        //Runs on EmptyScene, so the next world loaded is swept once it runs
        public static void Reset()
        {
            sweepPending = true;
        }
    }
}
