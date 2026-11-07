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

        //Takes every test station out of the system, except one the player is docked to
        public static int Clear(out string message)
        {
            int removed = FzSpawns.RemoveKind(SpawnKind, out List<string> kept);
            message = $"Removed {removed} test station(s)." + (kept.Count > 0 ? $" Kept {string.Join(", ", kept)}, which you're docked to." : "");
            return removed;
        }
    }
}
