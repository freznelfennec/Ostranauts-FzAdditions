using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //Spawns a station beside the player. A station is a body orbit with a ship on it, so this makes its own orbit round
    //the body nearest the player, at the player's distance from that body. The orbit's clock is shifted so the station is
    //at the chosen spot now, and it goes round the way the player is moving. The system saves the orbit and the ship like
    //any other station's.
    public static class StationPlacer
    {
        private const double KmPerAU = 149597872.0;

        public class Request
        {
            public string Template;       //A ship template in DataHandler.dictShips
            public string PublicName;     //Shown on the nav map; {0} is replaced with the last part of the registration
            public string Owner;          //A faction, or null
            public double Km;
            public string Kind;           //The FzSpawns kind it's marked as
            public string[] StartingConds;
        }

        public static Ship SpawnNear(Request request, out string message)
        {
            message = null;
            StarSystem system = CrewSim.system;
            Ship player = CrewSim.GetSelectedCrew()?.ship;
            if (system == null || player?.objSS == null)
            {
                message = "No player ship to spawn beside.";
                return null;
            }
            if (DataHandler.GetShip(request.Template) == null)
            {
                message = $"No ship template named {request.Template}.";
                return null;
            }
            if (request.Owner != null && system.GetFaction(request.Owner) == null)
            {
                message = $"No faction named {request.Owner}.";
                return null;
            }

            ShipSitu us = player.objSS;
            us.UpdateTime(StarSystem.fEpoch);
            OrbitPlan plan = FollowPlayer(system, player, request.Km, out string matchNote) ?? Circular(system, us, request.Km, out message);
            if (plan == null) return null;

            string regID = FzSpawns.NewRegID(request.Kind);
            var spec = new JsonSpawnStation
            {
                strName = regID,
                strPublicName = string.Format(request.PublicName ?? "{0}", regID.Substring(regID.Length - 3)),
                strNameParent = plan.Parent.strName,
                strShipType = request.Template,
                strOrbitType = "EX",
                strClassification = "OrbitalStation",
                fPeriapsisAU = plan.Periapsis,
                fApoapsisAU = plan.Apoapsis,
                fEccentricity = (float)plan.Eccentricity,
                fOrbitalPeriodYears = plan.Period / SecondsPerYear, //0 has the game work it out from the body's mass
                fDegreesCW = (float)plan.AngleDeg,
                fRadiusKM = 1f,
                fMassKG = 100000f,
                bIsNoFees = true,
                nConstructionProgress = 100, //Less spawns the station's construction template instead of its layout
                strOwner = request.Owner,
                aFactions = request.Owner != null ? new[] { request.Owner } : null,
                aStartingConds = FzSpawns.MarkConds(request.Kind).Concat(request.StartingConds ?? new string[0]).ToArray()
            };

            //The game adds the orbit and the ship before the starting conditions, so a spawn that fails partway leaves an
            //unmarked station behind. Whatever was made is taken away again.
            Ship station;
            BodyOrbit orbit;
            try
            {
                system.SpawnStationFromJSON(spec);
                station = system.GetShipByRegID(regID);
                orbit = system.GetBO(regID);
                if (station == null || orbit == null || FzSpawns.KindOf(station) != request.Kind) throw new InvalidOperationException("the station, its orbit or its marks weren't made");
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Spawning {request.Template} as {regID} failed: {ex}");
                FzSpawns.RemoveRemains(regID);
                message = $"The game couldn't spawn {request.Template}: {ex.Message}. Nothing was left behind; see the log.";
                return null;
            }

            //The spawn spec carries the angle and eccentricity as floats, so the exact values are set again, with the
            //direction and the clock shift that puts the station where it should be now
            if (plan.Period > 0.0)
            {
                orbit.fAngle = plan.AngleDeg;
                orbit.fEcc = plan.Eccentricity;
                orbit.fAxis2 = orbit.fAxis1 * Math.Sqrt(1.0 - plan.Eccentricity * plan.Eccentricity);
                orbit.fPeriod = plan.Period;
                orbit.fPeriodShift = plan.Shift;
            }
            else
            {
                //Circular: the orbit's angle is where it starts, so its clock starts there at this moment
                orbit.fPeriodShift = orbit.fPeriod - StarSystem.fEpoch % orbit.fPeriod;
            }
            orbit.nOrbitDirection = plan.Direction;
            orbit.dTimeCalcLast = -1000.0;
            orbit.dVelCalcLast = -1000.0;
            orbit.UpdateTime(StarSystem.fEpoch);
            station.objSS.UpdateTime(StarSystem.fEpoch);

            //How well it worked, measured from the orbit the game will move the station along
            double apart = (orbit.vPos - us.vPos).magnitude * KmPerAU;
            double drift = (orbit.vVel - us.vVel).magnitude * MetresPerAU;
            message = $"Spawned {station.publicName} ({regID}), a {request.Template} owned by {request.Owner ?? "no one"}, "
                + (plan.Followed
                    ? $"{apart:0.#} km {(plan.Ahead ? "ahead of" : "behind")} you on your orbit around {plan.Parent.strName}, {drift:0.#} m/s from your velocity."
                    : $"{apart:0.#} km away in a circular orbit around {plan.Parent.strName}, {drift:0.#} m/s from your velocity: {matchNote}.");
            return station;
        }

        private const double MetresPerAU = 149597870700.0;
        private const double SecondsPerYear = 31556926.0;

        //An orbit for a station: its parent body, shape, and the clock shift that puts it at the chosen spot now
        private class OrbitPlan
        {
            public BodyOrbit Parent;
            public double Periapsis, Apoapsis, Eccentricity, AngleDeg;
            public double Period;    //Seconds, or 0 for the game to work out
            public double Shift;
            public int Direction;
            public bool Followed;    //On the player's own orbit
            public bool Ahead;
        }

        //The player's own orbit round the body pulling hardest on them, as the game fits it (BodyOrbit.CreateBOFromShip),
        //with the station Km ahead of or behind them along it. It then moves just as they do. Null, with why, if they
        //aren't on an orbit the station could follow.
        private static OrbitPlan FollowPlayer(StarSystem system, Ship player, double km, out string note)
        {
            note = null;
            ShipSitu us = player.objSS;
            UnityEngine.Vector2 pull = default;
            BodyOrbit closest = null;
            BodyOrbit parent = system.GetGreatestGravBO(us, StarSystem.fEpoch, ref pull, ref closest);
            if (parent == null)
            {
                note = "no body pulls on your ship";
                return null;
            }
            parent.UpdateTime(StarSystem.fEpoch);

            BodyOrbit fit = BodyOrbit.CreateBOFromShip(player, parent);
            if (fit == null)
            {
                note = $"your ship isn't on a closed orbit around {parent.strName}";
                return null;
            }
            if (fit.fPerh <= parent.fRadius)
            {
                note = $"your orbit dips into {parent.strName}";
                return null;
            }
            double speed = (us.vVel - parent.vVel).magnitude;
            if (speed <= 0.0)
            {
                note = "your ship isn't moving round anything";
                return null;
            }

            bool ahead = UnityEngine.Random.Range(0, 2) == 0;
            double offset = km / KmPerAU / speed * (ahead ? 1.0 : -1.0); //Seconds along the orbit
            double shift = (fit.fPeriodShift + offset) % fit.fPeriod;
            if (shift < 0.0) shift += fit.fPeriod;
            return new OrbitPlan
            {
                Parent = parent,
                Periapsis = fit.fPerh,
                Apoapsis = fit.fAph,
                Eccentricity = fit.fEcc,
                AngleDeg = fit.fAngle,
                Period = fit.fPeriod,
                Shift = shift,
                Direction = fit.nOrbitDirection,
                Followed = true,
                Ahead = ahead
            };
        }

        //A circular orbit round the nearest body through a spot Km from the player in a random direction, going round
        //the way the player is. Used when the player isn't on an orbit to follow.
        private static OrbitPlan Circular(StarSystem system, ShipSitu us, double km, out string message)
        {
            message = null;
            BodyOrbit parent = system.GetNearestBO(us, StarSystem.fEpoch, bIncludePlaceholders: false);
            if (parent == null)
            {
                message = "No body to orbit near the player.";
                return null;
            }
            parent.UpdateTime(StarSystem.fEpoch);

            double heading = UnityEngine.Random.Range(0f, (float)(Math.PI * 2.0));
            double rx = us.vPosx + Math.Cos(heading) * km / KmPerAU - parent.dXReal;
            double ry = us.vPosy + Math.Sin(heading) * km / KmPerAU - parent.dYReal;
            double radius = Math.Sqrt(rx * rx + ry * ry);
            if (radius <= parent.fRadius)
            {
                message = $"That spot is inside {parent.strName}.";
                return null;
            }
            double vx = us.vVelX - parent.dVelX;
            double vy = us.vVelY - parent.dVelY;
            return new OrbitPlan
            {
                Parent = parent,
                Periapsis = radius,
                Apoapsis = radius,
                AngleDeg = Math.Atan2(ry, rx) * 180.0 / Math.PI,
                Direction = rx * vy - ry * vx < 0.0 ? -1 : 1
            };
        }

    }
}
