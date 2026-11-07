using Freznel.FzAdditions.Util.AI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //A hostile station spawned beside the player for testing the mod's AI, by 'fz encounter'. StationPlacer puts it in
    //its own orbit next to the player. It and every other test station, such as a generated site from 'fz site spawn',
    //are FzSpawns of the Test kind, registered FZT-, which 'fz encounter clear' removes. Test stations are always wanted,
    //so the sweep only removes broken ones.
    public static class TestEncounter
    {
        public const string SpawnKind = "Test";
        public const string RegPrefix = "FZT-";
        public const string LegacyCond = "IsFzTestEncounter"; //What test stations were marked with before FzSpawns
        public const string DefaultTemplate = "Station_COHO"; //Corsair's Hollow: docks, sensors, a railgun and two PDCs, lightly loaded
        public const string DefaultOwner = "BeltPirates";

        public static Ship Spawn(double km, string template, string owner, out string message)
        {
            Ship station = StationPlacer.SpawnNear(new StationPlacer.Request
            {
                Template = template,
                PublicName = "Hostile Test Station {0}",
                Owner = owner,
                Km = km,
                Kind = SpawnKind,
                StartingConds = new[]
                {
                    $"{FzAI.PilotCond(FzStationDefensePilot.TypeName)}=1.0x1",
                    $"{FzAI.AlertCond}=1.0x{FzAI.Hostile}"
                }
            }, out message);
            if (station != null) FaceGunsAt(station, CrewSim.GetSelectedCrew().ship);
            return station;
        }

        //Turns the station so its first gun points at the player. Corsair's Hollow's guns all face one way, so this
        //brings them all to bear.
        private static void FaceGunsAt(Ship station, Ship target)
        {
            var gun = station.WeaponsSystem.GetWeaponsAndRotations().FirstOrDefault(w => FzWeapons.IsGun(w.Item1));
            if (gun == null) return;
            double toTarget = Math.Atan2(target.objSS.vPosy - station.objSS.vPosy, target.objSS.vPosx - station.objSS.vPosx);
            float rotation = (float)(toTarget - Math.PI / 2.0 - gun.Item2 * Math.PI / 180.0);
            station.objSS.fRot = rotation;
            if (station.json?.objSS != null) station.json.objSS.fRot = rotation;
        }

        //Spends every round on every test station, as if they'd fired until empty, so they can be docked with safely
        public static int Disarm(out string message)
        {
            var lines = new List<string>();
            int stations = 0;
            foreach (Ship ship in CrewSim.system.dictShips.Values.ToList())
            {
                if (ship == null || FzSpawns.KindOf(ship) != SpawnKind) continue;
                int rounds = FzWeapons.Disarm(ship);
                stations++;
                lines.Add($"  {ship.strRegID} ({ship.publicName}): {rounds} round(s) spent, {FzWeapons.Rounds(ship)} left");
            }
            message = stations == 0 ? "No test stations to disarm." : $"Disarmed {stations} test station(s):\n" + string.Join("\n", lines);
            return stations;
        }

        //Removes everyone aboard test stations who isn't the player or their crew, such as Corsair's Hollow's pirates.
        //Only loaded stations have people as COs; a shallow one spawns its crew when it loads.
        public static int ClearNpcs(out string message)
        {
            int removed = 0, stations = 0;
            foreach (Ship ship in CrewSim.system.dictShips.Values.ToList())
            {
                if (ship == null || FzSpawns.KindOf(ship) != SpawnKind) continue;
                List<CondOwner> people = ship.GetPeople(false);
                if (people == null) continue;
                bool any = false;
                foreach (CondOwner person in people.ToList())
                {
                    if (person == null || person.bDestroyed || SiteIdentity.IsPlayersPerson(person)) continue;
                    try
                    {
                        ship.RemoveCO(person, bForce: true);
                        person.Destroy();
                        removed++;
                        any = true;
                    }
                    catch (Exception ex)
                    {
                        FzAdditions.Logger.LogWarning($"Removing {person.strName} from {ship.strRegID} failed: {ex.Message}");
                    }
                }
                if (any) stations++;
            }
            message = removed == 0 ? "No NPCs aboard loaded test stations." : $"Removed {removed} NPC(s) from {stations} test station(s).";
            return removed;
        }

        //Takes every test station out of the system, except one the player is docked to
        public static int Clear(out string message)
        {
            int removed = FzSpawns.RemoveKind(SpawnKind, out List<string> kept);
            message = $"Removed {removed} test station(s)." + (kept.Count > 0 ? $" Kept {string.Join(", ", kept)}, which you're docked to." : "");
            return removed;
        }
    }
}
