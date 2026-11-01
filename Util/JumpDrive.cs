using Ostranauts.ShipGUIs.Utilities;
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
        private const double SecondsPerHour = 3600.0;
        private const double StrengthScale = 100.0; //Field strength is a percentage
        private const double MinFieldStrength = 0.0;
        private const double UncertaintyCondition = 0.8; //GetDamageState, 1 is undamaged. Wear above this adds none.
        private const double MaxUncertainty = 100.0;
        private const int SolveSteps = 60;
        private const int CoastSteps = 8;
        private const double MinNavSpeed = 6.684586911775981E-14; //In AU per second, the nav console shows no ETA below this
        private const float TorchHeatPerPellet = 0.04f; //FusionIC's core heating per second for each unit of pellet rate
        private const float TorchCoolingPerCycle = 0.2f; //FusionIC's core cooling per second at full cycle
        private const string ReferenceStar = "Sol";

        public enum TargetType { None, Ship, Station, Asteroid, Planet, Star }

        //What a jump would be aimed at. Distances are in AU, and speed in AU per second.
        public struct JumpTarget
        {
            public string Name;
            public TargetType Type;
            public double Distance; //To the surface, as the nav station measures range
            public double Buffer; //How close a jump can end to it
            public double Braking; //Room to stop in after the jump, a jump keeps the ship's velocity
            public bool BrakesOnTorch;
            public double RelativeSpeed;
            public double BrakingAcceleration; //In AU per second squared, 0 if the ship has no thrust to brake with
            public double NavEta; //In seconds, what the nav console shows, negative when it shows none

            //In seconds to kill the relative speed, negative without thrust to do it
            public double BrakingTime => BrakingAcceleration > 0.0 ? RelativeSpeed / BrakingAcceleration : -1.0;

            public bool CanPlot => Type != TargetType.None && Type != TargetType.Star;

            //How far a jump needs to go, which a jump's own minimum can already cover
            public double JumpDistance => Math.Max(Distance - Buffer - Braking - MinDistanceAU, 0.0);
        }

        private static double MinDistanceAU => Constants.MinJumpDistance / Constants.MetersPerAU;

        //BrakeOnTorch picks the thrust to stop on, otherwise it is RCS. Without a torch reading it is RCS regardless.
        public static JumpTarget GetTarget(Ship ship, NavPOI poi, bool brakeOnTorch)
        {
            JumpTarget target = new JumpTarget { Name = poi?.name ?? "None", Type = GetTargetType(poi) };
            if (target.Type == TargetType.None || ship?.objSS == null) return target;

            ShipSitu situ = ship.objSS;
            poi.GetSXY(out double x, out double y);
            poi.GetVSXY(out double velocityX, out double velocityY);

            double surface = poi.Ship != null ? CollisionManager.GetCollisionDistanceAU(ship, poi.Ship)
                : poi.bodyOrbit != null ? CollisionManager.GetCollisionDistanceAU(ship, poi.bodyOrbit)
                : CollisionManager.GetCollisionDistanceAU(situ, poi.stellarObj.objSS);

            target.Distance = Math.Max(situ.GetDistance(x, y) - surface, 0.0);
            target.Buffer = (target.Type == TargetType.Planet ? Constants.JumpBufferPlanet : Constants.JumpBufferObject) / Constants.MetersPerAU;
            target.RelativeSpeed = MathUtils.GetDistance(velocityX, velocityY, situ.vVelX, situ.vVelY);

            target.BrakesOnTorch = brakeOnTorch && GetTorchAcceleration(ship) > 0.0;
            double braking = Math.Min(target.BrakesOnTorch ? GetTorchAcceleration(ship) : GetRcsAcceleration(ship), Constants.JumpBrakingLimit / Constants.MetersPerAU);
            if (braking > 0.0) target.Braking = target.RelativeSpeed * target.RelativeSpeed / (2.0 * braking);
            target.BrakingAcceleration = braking;
            target.NavEta = GetNavEta(ship, x, y, velocityX, velocityY, target.RelativeSpeed);
            return target;
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
        private static double GetTorchAcceleration(Ship ship)
        {
            float cycle = Mathf.Min((float)ship.PelletMax * TorchHeatPerPellet / TorchCoolingPerCycle, 1f);
            double torch = ship.GetMaxTorchThrust(cycle);
            return double.IsNaN(torch) ? 0.0 : torch * Constants.JumpTorchBrakingMargin;
        }

        private static double GetRcsAcceleration(Ship ship)
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
                double coast = speed * Math.Max(energy - held, 0.0) / fillRate;
                energy = GetSeriesEnergy(Math.Max(target.JumpDistance - coast, 0.0), need, capacity, speed, averageStrength);
            }
            return energy;
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
            time *= Math.Exp(Constants.JumpTimeScalingExponent * time / SecondsPerHour);
            return time * Math.Max(averageStrength, MinFieldStrength) / StrengthScale;
        }

        //Percent the jump time can land either side of its value, 1 for each 1% of each emitter's condition under the limit
        public static double GetUncertainty(Ship ship)
        {
            double total = FieldContour.GetEmitterCOs(ship).Sum(co => Math.Max(UncertaintyCondition - co.GetDamageState(), 0.0) * StrengthScale);
            return Math.Min(total, MaxUncertainty);
        }

        //0 without a contour
        public static double GetAverageStrength(Ship ship)
        {
            List<FieldContour.Segment> segments = FieldContour.CalculateSegments(ship);
            return segments.Count > 0 ? segments.Average(segment => segment.Strength) : 0.0;
        }

        //In AU per second, the ship's velocity against the selected target, so a course flown at a target plots to it.
        //The point of reference can be the star even beside a small body, so it only stands in without a target.
        //Every jump calculation uses this one velocity for both speed and direction.
        public static void GetVelocity(Ship ship, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            ShipSitu situ = ship?.objSS;
            if (situ == null) return;

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
