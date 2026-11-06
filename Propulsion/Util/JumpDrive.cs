using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Energy past the field's own need buys jump time, and a jump moves the ship forward along its course by
    //that time at its current speed. Field strength scales the time, and worn emitters make it uncertain.
    public static class JumpDrive
    {
        private const double StrengthScale = 100.0; //Field strength is a percentage
        private const double MinFieldStrength = 0.0;
        private const double UncertaintyCondition = 0.8; //GetDamageState, 1 is undamaged. Wear above this adds none.
        private const double MaxUncertainty = 100.0;
        private const int SolveSteps = 60;
        private const int CoastSteps = 8;
        private const double MinNavSpeed = 6.684586911775981E-14; //In AU per second, the nav console shows no ETA below this
        public const float TorchHeatPerPellet = 0.04f; //FusionIC's core heating per second for each unit of pellet rate
        public const float TorchCoolingPerCycle = 0.2f; //FusionIC's core cooling per second at full cycle
        private const string ReferenceStar = "Sol";
        private const string TorchArmProp = "knobRatio"; //The reactor's cycle switch, 1 when armed

        public enum TargetType { None, Ship, Station, Asteroid, Planet, Star }

        public enum TargetKind { Ship, Body, Object }

        //Why a torch can't be used to thrust on right now
        public enum TorchBlock { None, NoTorch, NoWakeZone, NotArmed }

        //What a jump would be aimed at. Distances are in AU, and speed in AU per second.
        public struct JumpTarget
        {
            public string Name;
            public TargetType Type;
            public double Distance; //To the surface, as the nav station measures range
            public double Surface;
            public double Buffer; //How close a jump can end to it
            public double Approach; //From the aim point to where braking has to be done by, at least the surface and buffer
            public double Braking; //Room to stop in after the jump, a jump keeps the ship's velocity
            public bool BrakesOnTorch;
            public bool OutsideNoWakeZone; //Braking on the torch before the aim point's no wake zone, which it can't fire in
            public TorchBlock TorchBlock; //Why a torch asked for isn't being used
            public double RelativeSpeed;
            public double BrakingAcceleration; //In AU per second squared, 0 if the ship has no thrust to brake with
            public double NavEta; //In seconds, what the nav console shows, negative when it shows none

            //In seconds to kill the relative speed, negative without thrust to do it
            public double BrakingTime => BrakingAcceleration > 0.0 ? RelativeSpeed / BrakingAcceleration : -1.0;

            public bool CanPlot => Type != TargetType.None && Type != TargetType.Star;

            //How far a jump needs to go, which a jump's own minimum can already cover
            public double JumpDistance => Math.Max(Distance + Surface - StopDistance - MinDistanceAU, 0.0);

            //How far from the aim point a series stops, where the pilot starts braking
            public double StopDistance => Approach + Braking;
        }

        //A target and the point it is aimed at on or near it, the crosshair can be dragged off its centre. Kept by
        //the IDs the nav console saves its own target by. Positions are in AU and velocity in AU per second.
        public struct AimTarget
        {
            public string Name;
            public TargetType Type;
            public TargetKind Kind;
            public string ID;
            public double CenterX;
            public double CenterY;
            public double OffsetX;
            public double OffsetY;
            public double VelocityX;
            public double VelocityY;
            public double Surface; //Collision distance from the ship

            public double X => CenterX + OffsetX;
            public double Y => CenterY + OffsetY;
        }

        private static double MinDistanceAU => Constants.MinJumpDistance / Constants.MetersPerAU;

        //False for anything a jump can't be plotted to. The name and type are still filled in to show.
        public static bool ResolvePOI(Ship ship, NavPOI poi, out AimTarget aim)
        {
            aim = new AimTarget { Name = poi?.name ?? "None", Type = GetTargetType(poi) };
            if (aim.Type == TargetType.None || aim.Type == TargetType.Star || ship?.objSS == null) return false;

            TargetKind kind = poi.Ship != null ? TargetKind.Ship : poi.bodyOrbit != null ? TargetKind.Body : TargetKind.Object;
            string id = kind == TargetKind.Ship ? poi.Ship.strRegID : kind == TargetKind.Body ? poi.bodyOrbit.strName : poi.stellarObj.strID;
            if (!Resolve(ship, kind, id, 0.0, 0.0, out aim)) return false;

            //Whatever the crosshair adds, a drag or the sensors' error, is kept as the offset
            poi.GetSXY(out double x, out double y);
            aim.OffsetX = x - aim.CenterX;
            aim.OffsetY = y - aim.CenterY;
            return true;
        }

        //Finds a target by its ID as it is now. False if it is gone.
        public static bool Resolve(Ship ship, TargetKind kind, string id, double offsetX, double offsetY, out AimTarget aim)
        {
            aim = new AimTarget { Name = id ?? "None", Kind = kind, ID = id, OffsetX = offsetX, OffsetY = offsetY };
            if (id == null || ship?.objSS == null || CrewSim.system == null) return false;

            if (kind == TargetKind.Body)
            {
                BodyOrbit body = CrewSim.system.GetBO(id);
                if (body == null) return false;

                aim.Type = body.boParent == null ? TargetType.Star : CrewSim.system.GetShipByRegID(body.strName) != null ? TargetType.Station : TargetType.Planet;
                aim.CenterX = body.dXReal;
                aim.CenterY = body.dYReal;
                aim.VelocityX = body.dVelX;
                aim.VelocityY = body.dVelY;
                aim.Surface = CollisionManager.GetCollisionDistanceAU(ship, body);
                return aim.Type != TargetType.Star;
            }

            ShipSitu situ;
            if (kind == TargetKind.Ship)
            {
                Ship target = CrewSim.system.GetShipByRegID(id);
                if (target == null || target.bDestroyed || target.objSS == null) return false;

                aim.Type = target.Classification == Ship.TypeClassification.Asteroid ? TargetType.Asteroid : target.IsStation(bIgnoreDocks: true) ? TargetType.Station : TargetType.Ship;
                aim.Surface = CollisionManager.GetCollisionDistanceAU(ship, target);
                situ = target.objSS;
            }
            else
            {
                if (!CrewSim.system.dictStellarObjects.TryGetValue(id, out IStellarObject stellar) || stellar?.objSS == null) return false;

                aim.Type = TargetType.Asteroid;
                aim.Surface = CollisionManager.GetCollisionDistanceAU(ship.objSS, stellar.objSS);
                situ = stellar.objSS;
            }

            //The same position NavPOI gives, a locked ship is placed by its body
            BodyOrbit holder = (situ.bBOLocked || situ.bIsBO) && situ.strBOPORShip != null ? CrewSim.system.GetBO(situ.strBOPORShip) : null;
            aim.CenterX = holder != null ? holder.dXReal + situ.vBOOffsetx : situ.vPosx;
            aim.CenterY = holder != null ? holder.dYReal + situ.vBOOffsety : situ.vPosy;
            aim.VelocityX = situ.vVelX;
            aim.VelocityY = situ.vVelY;
            return true;
        }

        //BrakeOnTorch picks the thrust to stop on, otherwise it is RCS. Without a torch reading it is RCS regardless.
        //A series ending in a no wake zone, around a station or anything near one, can only brake on the torch outside
        //it. A fast arrival stops short of the zone to brake on the torch there, in either thrust mode, and a slow one or
        //a ship with no torch goes in and brakes on RCS. Single jumps brake where they land.
        public static JumpTarget GetTarget(Ship ship, AimTarget aim, bool brakeOnTorch, bool series)
        {
            JumpTarget target = new JumpTarget { Name = aim.Name, Type = aim.Type };
            if (!target.CanPlot || ship?.objSS == null) return target;

            ShipSitu situ = ship.objSS;
            target.Surface = aim.Surface;
            target.Distance = Math.Max(situ.GetDistance(aim.X, aim.Y) - aim.Surface, 0.0);
            target.Buffer = (target.Type == TargetType.Planet ? Constants.JumpBufferPlanet : Constants.JumpBufferObject) / Constants.MetersPerAU;
            target.Approach = target.Surface + target.Buffer;
            target.RelativeSpeed = MathUtils.GetDistance(aim.VelocityX, aim.VelocityY, situ.vVelX, situ.vVelY);

            //At a no wake zone the arrival speed decides, whatever the thrust is set to
            bool hasTorch = ReactorFuel.IsFusing(ship.Reactor) && GetTorchAcceleration(ship) > 0.0;
            bool torch = brakeOnTorch && hasTorch;
            if (brakeOnTorch && !hasTorch) target.TorchBlock = TorchBlock.NoTorch;

            double zoneExit = series ? GetNoWakeExit(situ, aim) : -1.0;
            if (zoneExit >= 0.0)
            {
                if (target.RelativeSpeed * Constants.MetersPerAU > Constants.SeriesTorchArrivalSpeed && hasTorch)
                {
                    torch = true;
                    target.TorchBlock = TorchBlock.None;
                    target.OutsideNoWakeZone = true;
                    target.Approach = Math.Max(target.Approach, zoneExit + target.Buffer);
                }
                else if (torch)
                {
                    torch = false;
                    target.TorchBlock = TorchBlock.NoWakeZone;
                }
            }

            target.BrakesOnTorch = torch;
            double braking = Math.Min(torch ? GetTorchAcceleration(ship) : GetRcsAcceleration(ship), Constants.JumpBrakingLimit / Constants.MetersPerAU);
            if (braking > 0.0) target.Braking = target.RelativeSpeed * target.RelativeSpeed / (2.0 * braking);
            target.BrakingAcceleration = braking;
            target.NavEta = GetNavEta(ship, aim.X, aim.Y, aim.VelocityX, aim.VelocityY, target.RelativeSpeed);
            return target;
        }

        //In AU, how far back from the aim point a ship coming in from where it is now crosses into the aim point's
        //no wake zone. Negative if the aim point isn't in one.
        private static double GetNoWakeExit(ShipSitu situ, AimTarget aim)
        {
            Ship station = CrewSim.system?.GetNearestStation(aim.X, aim.Y, excludeOutposts: true);
            if (station?.objSS == null) return -1.0;

            double fromStationX = aim.X - station.objSS.vPosx;
            double fromStationY = aim.Y - station.objSS.vPosy;
            double inside = fromStationX * fromStationX + fromStationY * fromStationY - Constants.NoWakeDistance * Constants.NoWakeDistance;
            if (inside >= 0.0) return -1.0;

            double distance = situ.GetDistance(aim.X, aim.Y);
            if (distance <= 0.0) return Constants.NoWakeDistance;

            //Back along the line towards the ship until it leaves the circle
            double along = (fromStationX * (situ.vPosx - aim.X) + fromStationY * (situ.vPosy - aim.Y)) / distance;
            return -along + Math.Sqrt(along * along - inside);
        }

        //Why the torch can't thrust with the ship at this position, None if it can. The reactor zeroes the torch's cycle
        //unless its cycle switch is armed, and disarms it inside a no wake zone, so that is checked first.
        public static TorchBlock GetTorchBlock(Ship ship, double x, double y)
        {
            if (!ReactorFuel.IsFusing(ship?.Reactor) || GetTorchAcceleration(ship) <= 0.0) return TorchBlock.NoTorch;
            if (IsInNoWakeZone(x, y)) return TorchBlock.NoWakeZone;
            return ship.GetReactorGPMValue(TorchArmProp) == "1" ? TorchBlock.None : TorchBlock.NotArmed;
        }

        //The same test the reactor makes before it cuts the torch
        public static bool IsInNoWakeZone(double x, double y)
        {
            Ship station = CrewSim.system?.GetNearestStation(x, y, excludeOutposts: true);
            return station?.objSS != null && station.objSS.GetDistance(x, y) <= Constants.NoWakeDistance;
        }

        //The same reading as NavModTargetData, a course being flown gives its arrival, otherwise it is range over
        //relative speed and only while closing on the target. X and y are the target's position and velocity is its own.
        private static double GetNavEta(Ship ship, double x, double y, double velocityX, double velocityY, double relativeSpeed)
        {
            ShipSitu situ = ship.objSS;
            bool engaged = GUIOrbitDraw.Instance != null && GUIOrbitDraw.Instance.GetPropMapData("chkEngage", false);
            if (engaged && situ.HasNavData()) return situ.NavData.GetArrivalEpoch() - StarSystem.fEpoch;
            if (relativeSpeed < MinNavSpeed) return -1.0;

            double closing = (x - situ.vPosx) * (velocityX - situ.vVelX) + (y - situ.vPosy) * (velocityY - situ.vVelY);
            return closing > 0.0 ? -1.0 : situ.GetDistance(x, y) / relativeSpeed;
        }

        //In AU per second squared, what the torch can hold through a whole burn rather than its peak. Past the cycle
        //where pellets heat the core as fast as cycling cools it, the core cools until the reactor stalls.
        //The torch reads nothing until the reactor has run, so it can be unavailable.
        public static double GetTorchAcceleration(Ship ship)
        {
            double torch = ship.GetMaxTorchThrust(GetSustainableCycle(ship));
            return double.IsNaN(torch) ? 0.0 : torch * Constants.JumpTorchBrakingMargin;
        }

        //The reactor's cycle setting the torch can hold through a whole burn
        public static float GetSustainableCycle(Ship ship)
        {
            return Mathf.Min((float)ship.PelletMax * TorchHeatPerPellet / TorchCoolingPerCycle, 1f);
        }

        public static double GetRcsAcceleration(Ship ship)
        {
            double rcs = ship.RCSAccelMax;
            return double.IsNaN(rcs) ? 0.0 : rcs;
        }

        //A station can be picked as its ship or as the orbit it follows
        private static TargetType GetTargetType(NavPOI poi)
        {
            if (poi?.Ship != null)
            {
                if (poi.Ship.Classification == Ship.TypeClassification.Asteroid) return TargetType.Asteroid;
                return poi.Ship.IsStation(bIgnoreDocks: true) ? TargetType.Station : TargetType.Ship;
            }

            if (poi?.stellarObj != null) return TargetType.Asteroid;
            if (poi?.bodyOrbit == null) return TargetType.None;
            if (poi.bodyOrbit.boParent == null) return TargetType.Star;
            return CrewSim.system.GetShipByRegID(poi.bodyOrbit.strName) != null ? TargetType.Station : TargetType.Planet;
        }

        //In AU per second, the speed that reaches the target in this much jump time. Negative if no time was given.
        public static double GetMinSpeed(JumpTarget target, double jumpTime)
        {
            return jumpTime > 0.0 ? target.JumpDistance / jumpTime : -1.0;
        }

        //In kWh past the field's need, negative if the target is out of reach at this speed
        public static double GetEnergyToReach(JumpTarget target, double speed, double averageStrength)
        {
            return GetEnergyForDistance(target.JumpDistance, speed, averageStrength);
        }

        //What a series of jumps adds up to. A series spends the setting a full bank of cells at a time, recharging
        //in between, and each jump pays the field's need again. Whatever is left too small to raise a field is unused.
        public struct JumpSeries
        {
            public int Jumps;
            public int FullJumps; //The rest, at most one, is a shorter last jump
            public double JumpTime;
            public double Distance; //In AU
            public double FullJumpDistance; //In AU, every jump but a shorter last one
        }

        public static JumpSeries GetSeries(double energy, double need, double capacity, double speed, double averageStrength)
        {
            JumpSeries series = new JumpSeries();
            if (capacity <= need || capacity <= 0.0) return series;

            int full = (int)Math.Floor(energy / capacity);
            double rest = energy - full * capacity;
            series.FullJumps = full;
            series.Jumps = full + (rest > 0.0 && rest >= need ? 1 : 0);
            series.JumpTime = full * GetJumpTime(capacity - need, averageStrength);
            if (series.Jumps > full) series.JumpTime += GetJumpTime(rest - need, averageStrength);
            series.Distance = series.Jumps * MinDistanceAU + speed * series.JumpTime;
            series.FullJumpDistance = GetDistance(speed, GetJumpTime(capacity - need, averageStrength));
            return series;
        }

        //Total kWh for a series to reach the target, negative if it is out of reach. The ship keeps coasting while
        //its cells recharge, which covers part of the distance, and the more energy the longer that is. Held is the
        //charge already in the cells in kWh, and fillRate how fast they recharge in kWh per second.
        public static double GetSeriesEnergy(JumpTarget target, double need, double capacity, double speed, double averageStrength, double held, double fillRate)
        {
            double energy = GetSeriesEnergy(target.JumpDistance, need, capacity, speed, averageStrength);
            if (fillRate <= 0.0) return energy;

            //Coasting is slight next to a jump, so this settles within a few rounds
            for (int i = 0; i < CoastSteps && energy >= 0.0; i++)
            {
                double coast = speed * GetCoastTime(energy, held, fillRate);
                energy = GetSeriesEnergy(Math.Max(target.JumpDistance - coast, 0.0), need, capacity, speed, averageStrength);
            }
            return energy;
        }

        //Game seconds a series spends recharging to spend this much energy, all of which the ship coasts through.
        //0 if the cells already hold it or nothing recharges them.
        public static double GetCoastTime(double energy, double held, double fillRate)
        {
            return fillRate > 0.0 ? Math.Max(energy - held, 0.0) / fillRate : 0.0;
        }

        private static double GetSeriesEnergy(double jumpDistance, double need, double capacity, double speed, double averageStrength)
        {
            if (jumpDistance <= 0.0) return need;
            if (capacity <= need) return -1.0;

            double fullDistance = GetDistance(speed, GetJumpTime(capacity - need, averageStrength));
            if (speed <= 0.0 || averageStrength <= 0.0 || fullDistance <= 0.0) return -1.0;

            double distance = jumpDistance + MinDistanceAU;
            double full = Math.Floor(distance / fullDistance);
            double rest = distance - full * fullDistance - MinDistanceAU;
            if (rest <= 0.0) return full * capacity;

            double excess = GetEnergyForDistance(rest, speed, averageStrength);
            return excess < 0.0 ? -1.0 : full * capacity + need + excess;
        }

        private static double GetEnergyForDistance(double distance, double speed, double averageStrength)
        {
            if (distance <= 0.0) return 0.0;
            if (speed <= 0.0 || averageStrength <= 0.0) return -1.0;

            double time = distance / speed;
            double low = 0.0;
            double high = 1.0;
            while (GetJumpTime(high, averageStrength) < time)
            {
                high *= 2.0;
                if (double.IsInfinity(high)) return -1.0;
            }

            //The compounding has no simple inverse, so close in on it
            for (int i = 0; i < SolveSteps; i++)
            {
                double middle = (low + high) / 2.0;
                if (GetJumpTime(middle, averageStrength) < time) low = middle; else high = middle;
            }
            return high;
        }

        //Game seconds of jump time for this much energy past the field's need, before any uncertainty.
        //The compounding is per hour of jump time, so it stays slight until a jump runs for many hours.
        public static double GetJumpTime(double excessEnergy, double averageStrength)
        {
            double time = Math.Max(excessEnergy, 0.0) * Constants.JumpSecondsPerKWh;
            time *= Math.Exp(Constants.JumpTimeScalingExponent * time / Constants.SecondsPerHour);
            return time * Math.Max(averageStrength, MinFieldStrength) / StrengthScale;
        }

        //Percent the jump time can land either side of its value, 1 for each 1% of each emitter's condition under the limit
        public static double GetUncertainty(Ship ship)
        {
            return GetUncertainty(FieldContour.GetEmitterCOs(ship));
        }

        public static double GetUncertainty(List<CondOwner> emitters)
        {
            double total = emitters.Sum(co => Math.Max(UncertaintyCondition - co.GetDamageState(), 0.0) * StrengthScale);
            return Math.Min(total, MaxUncertainty);
        }

        //0 without a contour
        public static double GetAverageStrength(Ship ship)
        {
            return GetAverageStrength(FieldContour.GetEmitters(ship));
        }

        public static double GetAverageStrength(List<FieldContour.Emitter> emitters)
        {
            List<FieldContour.Segment> segments = FieldContour.CalculateSegments(emitters);
            return segments.Count > 0 ? segments.Average(segment => segment.Strength) : 0.0;
        }

        //In AU per second, the ship's velocity against the selected target, so a course flown at a target plots to it.
        //A running series goes by the target it was started for instead. The point of reference can be the star
        //even beside a small body, so it only stands in without a target.
        //Every jump calculation uses this one velocity for both speed and direction.
        public static void GetVelocity(Ship ship, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            ShipSitu situ = ship?.objSS;
            if (situ == null) return;

            if (JumpSequence.IsSeriesRunning(ship) && SeriesTarget.TryResolve(ship, out AimTarget aim))
            {
                x = situ.vVelX - aim.VelocityX;
                y = situ.vVelY - aim.VelocityY;
                return;
            }

            NavPOI target = GUIOrbitDraw.CrossHairTarget;
            TargetType type = GetTargetType(target);
            if (type != TargetType.None && type != TargetType.Star)
            {
                target.GetVSXY(out double targetX, out double targetY);
                x = situ.vVelX - targetX;
                y = situ.vVelY - targetY;
                return;
            }

            BodyOrbit body = GetPointOfReference(situ);
            x = situ.vVelX - (body?.dVelX ?? 0.0);
            y = situ.vVelY - (body?.dVelY ?? 0.0);
        }

        public static double GetSpeed(Ship ship)
        {
            GetVelocity(ship, out double x, out double y);
            return Math.Sqrt(x * x + y * y);
        }

        //Falls back the way NavModTargetData does, a placeholder stands in for its parent and nothing means the star
        private static BodyOrbit GetPointOfReference(ShipSitu situ)
        {
            BodyOrbit body = CrewSim.system?.GetBO(situ.strBOPORShip);
            if (body != null && body.IsPlaceholder()) body = body.boParent;
            return body ?? CrewSim.system?.GetBO(ReferenceStar);
        }

        //In AU, the minimum applies even to a jump of no time or from a standstill
        public static double GetDistance(double speed, double jumpTime)
        {
            return Constants.MinJumpDistance / Constants.MetersPerAU + speed * jumpTime;
        }
    }
}
