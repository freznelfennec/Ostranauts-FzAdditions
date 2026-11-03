using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Burns the NavCom flies for itself. A course correction turns a running series' velocity against its target
    //towards the aim point, keeping its speed, during the recharges. A velocity match brings the velocity against
    //its target to nothing. Either keeps everyone else's hands off the ship's thrust while it runs. The torch only
    //thrusts along the ship's nose, so it turns first, and it never fires inside a no wake zone.
    public static class JumpCorrection
    {
        public enum Result { Done, Burning, Waiting, Failed }

        //A cross match only takes out the velocity across the line to the aim point, keeping the speed along it
        public enum Goal { Course, Match, CrossMatch }

        private enum Phase { Turning, Burning }

        //Saved with the series' or the match's target, so a burn picks up after a load where it was. Thrust the game
        //saves on the ship.
        private class Burn
        {
            public Goal Goal;
            public bool Torch;
            public Phase Phase;
            public double Speed; //In AU per second, held from the start, the speed falls while a burn turns a course round
            public string Flow; //The reactor's flow setting before the torch was lit, null while it isn't
            public double CoreTarget; //The core temperature the torch is held at, thrust follows it

            private static readonly string[] Keys = { Constants.BurnTorchProp, Constants.BurnPhaseProp, Constants.BurnSpeedProp, Constants.BurnFlowProp, Constants.BurnCoreProp };

            public void Save(Ship ship)
            {
                TargetStore store = GetStore(Goal);
                store.SetValue(ship, Constants.BurnTorchProp, Torch.ToString().ToLower());
                store.SetValue(ship, Constants.BurnPhaseProp, Phase.ToString());
                store.SetValue(ship, Constants.BurnSpeedProp, Speed.ToString("R", CultureInfo.InvariantCulture));
                store.SetValue(ship, Constants.BurnFlowProp, Flow);
                store.SetValue(ship, Constants.BurnCoreProp, CoreTarget.ToString("R", CultureInfo.InvariantCulture));
            }

            //Null if the ship has none saved
            public static Burn Load(Ship ship)
            {
                return Load(ship, Goal.Course) ?? Load(ship, Goal.Match);
            }

            private static Burn Load(Ship ship, Goal goal)
            {
                TargetStore store = GetStore(goal);
                string phase = store.GetValue(ship, Constants.BurnPhaseProp);
                if (phase == null || !Enum.TryParse(phase, out Phase parsed)) return null;

                return new Burn
                {
                    Goal = goal,
                    Torch = bool.TryParse(store.GetValue(ship, Constants.BurnTorchProp), out bool torch) && torch,
                    Phase = parsed,
                    Speed = GetDouble(store, ship, Constants.BurnSpeedProp),
                    Flow = store.GetValue(ship, Constants.BurnFlowProp),
                    CoreTarget = GetDouble(store, ship, Constants.BurnCoreProp)
                };
            }

            public static void Clear(Ship ship)
            {
                foreach (string key in Keys)
                {
                    SeriesTarget.Store.SetValue(ship, key, null);
                    MatchTarget.Store.SetValue(ship, key, null);
                }
            }

            private static double GetDouble(TargetStore store, Ship ship, string key)
            {
                return double.TryParse(store.GetValue(ship, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;
            }
        }

        private static TargetStore GetStore(Goal goal)
        {
            return goal == Goal.Course ? SeriesTarget.Store : MatchTarget.Store;
        }

        private const double TorchCutLead = 0.3; //Game seconds, the reactor sets its thrust about this often
        private const float WarningInterval = 2f; //Real seconds between lockout warnings
        private const string LockedWarning = "NAVCOM IN CONTROL: THRUST LOCKED";
        private const string CycleProp = "slidCycle"; //The reactor's torch throttle, it cools the core as it thrusts
        private const string FlowProp = "slidFlow"; //The reactor's pellet flow, which heats the core
        private const string BusProp = "knobBus";
        private const string CoreTempCond = "StatICCoreTemp";
        private const string PelletMaxCond = "StatICPellMax";
        private const double CoreHotLimit = 0.725; //FusionIC's full core temperature, past 0.75 the walls take damage

        //Keyed by the ship's strRegID, the burns read from their series so far. Emptied when a save loads.
        private static readonly Dictionary<string, Burn> burns = new Dictionary<string, Burn>();
        private static readonly HashSet<string> waiting = new HashSet<string>(); //Off course with nothing to thrust on, worked out on each check
        private static float nextWarning;

        //Set around the NavCom's own calls, so the lockout lets them through
        public static bool NavComThrusting { get; private set; }

        //A save loading has ships of its own, their burns are read from their series as they are needed
        public static void Reset()
        {
            burns.Clear();
            waiting.Clear();
            nextWarning = 0f;
            NavComThrusting = false;
        }

        private static Burn GetBurn(Ship ship)
        {
            if (ship == null) return null;
            if (burns.TryGetValue(ship.strRegID, out Burn burn)) return burn;

            burn = Burn.Load(ship);
            if (burn != null) burns[ship.strRegID] = burn;
            return burn;
        }

        //A series with a target or a velocity match, and a ship docked to one shares its thrust
        public static bool IsLocked(Ship ship)
        {
            if (ship == null) return false;
            if (HasControl(ship)) return true;
            return ship.GetAllDockedShips()?.Any(HasControl) ?? false;
        }

        private static bool HasControl(Ship ship)
        {
            return JumpSequence.IsSteering(ship) || MatchTarget.IsMatching(ship);
        }

        public static bool IsBurning(Ship ship)
        {
            return GetBurn(ship) != null;
        }

        public static bool IsTorchBurning(Ship ship)
        {
            Burn burn = GetBurn(ship);
            return burn != null && burn.Torch && burn.Phase == Phase.Burning;
        }

        public enum BurnState { None, Turning, Torch, Rcs }

        //What a burn is doing now, for the nav console. None before its first check and once it is done.
        public static BurnState GetBurnState(Ship ship)
        {
            Burn burn = GetBurn(ship);
            if (burn == null) return BurnState.None;
            if (!burn.Torch) return BurnState.Rcs;
            return burn.Phase == Phase.Turning ? BurnState.Turning : BurnState.Torch;
        }

        public static bool IsWaiting(Ship ship)
        {
            return ship != null && waiting.Contains(ship.strRegID);
        }

        //A burn only steers while its ship is the one loaded and its series or match runs, any other is stopped
        public static void StopOthers(Ship current)
        {
            foreach (string id in burns.Keys.ToList())
            {
                Ship ship = CrewSim.system?.GetShipByRegID(id);
                if (ship == null) burns.Remove(id);
                else if (ship != current || !HasControl(ship)) Stop(ship);
            }
        }

        //In AU per second, below this a course is close enough. The angle keeps fast ships from chasing slight drift.
        public static double GetThreshold(double speed)
        {
            return Math.Max(Constants.CorrectionMinDeltaV / Constants.MetersPerAU, speed * Constants.CorrectionAngle);
        }

        public static bool IsNeeded(double deltaV, double speed)
        {
            return deltaV > GetThreshold(speed);
        }

        //The change in velocity that points the ship's velocity against its target at the aim point at the same speed.
        //Relative is the ship's position from the aim point.
        public static void GetDeltaV(double relativeX, double relativeY, double velocityX, double velocityY, out double deltaX, out double deltaY)
        {
            GetDeltaV(relativeX, relativeY, velocityX, velocityY, Math.Sqrt(velocityX * velocityX + velocityY * velocityY), out deltaX, out deltaY);
        }

        //The same towards a set speed
        public static void GetDeltaV(double relativeX, double relativeY, double velocityX, double velocityY, double speed, out double deltaX, out double deltaY)
        {
            double distance = Math.Sqrt(relativeX * relativeX + relativeY * relativeY);
            deltaX = distance > 0.0 ? -relativeX / distance * speed - velocityX : 0.0;
            deltaY = distance > 0.0 ? -relativeY / distance * speed - velocityY : 0.0;
        }

        //Close enough to stop, or after a jump past the aim point, which a jump can't come back from. Distances are in AU.
        public static bool HasArrived(double relativeX, double relativeY, double velocityX, double velocityY, double stopDistance, bool afterJump)
        {
            double remaining = Math.Sqrt(relativeX * relativeX + relativeY * relativeY) - stopDistance - Constants.MinJumpDistance / Constants.MetersPerAU;
            if (remaining <= Constants.SeriesArrivalTolerance / Constants.MetersPerAU) return true;
            return afterJump && relativeX * velocityX + relativeY * velocityY > 0.0;
        }

        //The ship's rotation that points the torch along this vector. SetThrust pushes along (-sin, cos) of it.
        public static double GetHeading(double x, double y)
        {
            return Math.Atan2(-x, y);
        }

        public static double WrapAngle(double angle)
        {
            angle %= 2.0 * Math.PI;
            if (angle > Math.PI) angle -= 2.0 * Math.PI;
            if (angle < -Math.PI) angle += 2.0 * Math.PI;
            return angle;
        }

        //Game seconds to turn through this angle from a standstill and stop, at the turn rate a correction holds to
        public static double GetTurnTime(double angle)
        {
            double acceleration = Constants.CorrectionTurnAcceleration;
            double maxRate = Constants.CorrectionMaxTurnRate;
            if (Math.Sqrt(angle * acceleration) <= maxRate) return 2.0 * Math.Sqrt(angle / acceleration);
            return angle / maxRate + maxRate / acceleration;
        }

        //In AU per second squared, slow enough that the reactor's updates can't overshoot a small correction
        public static double GetTorchBurnAcceleration(double torchAcceleration, double deltaV)
        {
            return Math.Min(torchAcceleration, deltaV / Constants.CorrectionTorchBurnTime);
        }

        //Every frame while a series is recharging or a match runs. Aim is the target as it is now.
        public static Result Update(Ship ship, JumpDrive.AimTarget aim, bool torchMode, Goal goal)
        {
            Result result = Steer(ship, aim, torchMode, goal);
            if (result == Result.Waiting) waiting.Add(ship.strRegID);
            else waiting.Remove(ship.strRegID);
            return result;
        }

        private static Result Steer(Ship ship, JumpDrive.AimTarget aim, bool torchMode, Goal goal)
        {
            Result result = SteerBurn(ship, aim, torchMode, goal);
            GetBurn(ship)?.Save(ship);
            return result;
        }

        //The change still needed for the goal. A course keeps the speed its burn started with, a match brings the
        //velocity against the target to nothing.
        private static void GetGoalDeltaV(Goal goal, double relativeX, double relativeY, double velocityX, double velocityY, double speed, out double deltaX, out double deltaY)
        {
            if (goal == Goal.Course) GetDeltaV(relativeX, relativeY, velocityX, velocityY, speed, out deltaX, out deltaY);
            else GetMatchDeltaV(relativeX, relativeY, velocityX, velocityY, goal == Goal.CrossMatch, out deltaX, out deltaY);
        }

        //The change a match still needs. Cross leaves the velocity along the line to the aim point, relative is the
        //ship's position from it. At the aim point there is no line, so all of it goes.
        public static void GetMatchDeltaV(double relativeX, double relativeY, double velocityX, double velocityY, bool cross, out double deltaX, out double deltaY)
        {
            deltaX = -velocityX;
            deltaY = -velocityY;
            double distance = Math.Sqrt(relativeX * relativeX + relativeY * relativeY);
            if (!cross || distance <= 0.0) return;

            double along = (velocityX * relativeX + velocityY * relativeY) / (distance * distance);
            deltaX += along * relativeX;
            deltaY += along * relativeY;
        }

        //In AU per second, the speed towards the aim point, negative going away
        public static double GetClosingSpeed(double relativeX, double relativeY, double velocityX, double velocityY)
        {
            double distance = Math.Sqrt(relativeX * relativeX + relativeY * relativeY);
            return distance > 0.0 ? -(velocityX * relativeX + velocityY * relativeY) / distance : 0.0;
        }

        //In AU per second, how close a burn has to come to its goal to stop
        private static double GetFinish(Goal goal, double speed)
        {
            return goal == Goal.Course ? GetThreshold(speed) * Constants.CorrectionFinishShare : Constants.MatchTolerance / Constants.MetersPerAU;
        }

        private static Result SteerBurn(Ship ship, JumpDrive.AimTarget aim, bool torchMode, Goal goal)
        {
            ShipSitu situ = ship.objSS;
            float seconds = CrewSim.TimeElapsedScaled();
            double relativeX = situ.vPosx - aim.X;
            double relativeY = situ.vPosy - aim.Y;
            double velocityX = situ.vVelX - aim.VelocityX;
            double velocityY = situ.vVelY - aim.VelocityY;

            Burn burn = GetBurn(ship);
            double speed = burn?.Speed ?? Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
            GetGoalDeltaV(goal, relativeX, relativeY, velocityX, velocityY, speed, out double deltaX, out double deltaY);
            double deltaV = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            if (burn == null)
            {
                if (goal == Goal.Course ? !IsNeeded(deltaV, speed) : deltaV <= GetFinish(goal, speed)) return Result.Done;

                bool? torch = ChooseEngine(ship, torchMode);
                if (torch == null) return Result.Waiting;

                burn = new Burn { Goal = goal, Torch = torch.Value, Phase = torch.Value ? Phase.Turning : Phase.Burning, Speed = speed };
                burns[ship.strRegID] = burn;
                FzAdditions.Logger.LogInfo(goal != Goal.Course
                    ? $"{ship.strRegID} matching {(goal == Goal.CrossMatch ? "cross-velocity" : "velocity")} with {aim.Name},{MathUtils.GetDistUnits(deltaV)}/s on {(burn.Torch ? "torch" : "RCS")}"
                    : $"{ship.strRegID} correcting course by {MathUtils.GetDistUnits(deltaV)}/s on {(burn.Torch ? "torch" : "RCS")}, holding {MathUtils.GetDistUnits(speed)}/s");
            }

            if (deltaV <= GetFinish(goal, speed))
            {
                Stop(ship);
                FzAdditions.Logger.LogInfo($"{ship.strRegID} {(goal != Goal.Course ? "velocity matched" : "course corrected")}, now {MathUtils.GetDistUnits(Math.Sqrt(velocityX * velocityX + velocityY * velocityY))}/s");
                return Result.Done;
            }

            if (seconds <= 0f) return Result.Burning;

            //The torch can be lost partway, inside a no wake zone or with the reactor down
            if (burn.Torch && JumpDrive.GetTorchBlock(ship, situ.vPosx, situ.vPosy) != JumpDrive.TorchBlock.None)
            {
                TorchOff(ship);
                burn.Torch = false;
                burn.Phase = Phase.Burning;
            }

            if (!burn.Torch)
            {
                if (JumpDrive.GetRcsAcceleration(ship) <= 0.0 || !(ship.DeltaVRemainingRCS > 0.0))
                {
                    Stop(ship);
                    return Result.Failed;
                }
                SteerRcs(ship, deltaX, deltaY, deltaV, seconds);
                return Result.Burning;
            }

            double error = WrapAngle(GetHeading(deltaX, deltaY) - situ.fRot);
            if (burn.Phase == Phase.Burning && Math.Abs(error) > Constants.CorrectionTorchAim * 2.0)
            {
                TorchOff(ship);
                burn.Phase = Phase.Turning;
            }

            Turn(ship, error, seconds);
            if (burn.Phase == Phase.Turning)
            {
                if (Math.Abs(error) < Constants.CorrectionTorchAim && Math.Abs(situ.fW) < Constants.CorrectionMaxTurnRate * 0.1) burn.Phase = Phase.Burning;
                return Result.Burning;
            }

            //Cut once what is left along the nose is about what the reactor's next update would add. Anything left
            //over across the nose is trimmed on RCS.
            double noseX = -Math.Sin(situ.fRot);
            double noseY = Math.Cos(situ.fRot);
            double along = deltaX * noseX + deltaY * noseY;
            double thrust = Math.Sqrt(situ.vAccIn.x * situ.vAccIn.x + situ.vAccIn.y * situ.vAccIn.y);
            if (along <= thrust * TorchCutLead)
            {
                TorchOff(ship);
                burn.Torch = false;
                return Result.Burning;
            }

            TorchOn(ship, deltaV);
            return Result.Burning;
        }

        //True for the torch, false for RCS, null if neither can thrust right now. The torch needs RCS to turn the ship.
        private static bool? ChooseEngine(Ship ship, bool torchMode)
        {
            if (JumpDrive.GetRcsAcceleration(ship) <= 0.0) return null;
            return torchMode && JumpDrive.GetTorchBlock(ship, ship.objSS.vPosx, ship.objSS.vPosy) == JumpDrive.TorchBlock.None;
        }

        //The flow is put back even once the torch has gone over to RCS, it is only kept while it isn't
        public static void Stop(Ship ship)
        {
            if (ship == null) return;
            waiting.Remove(ship.strRegID);
            Burn burn = GetBurn(ship);
            if (burn == null) return;

            TorchOff(ship);
            Maneuver(ship, 0f, 0f, 0f, 1E-10f);
            burns.Remove(ship.strRegID);
            Burn.Clear(ship);
        }

        //Thrusts straight along the change still needed, in the ship's own axes, easing off as it closes
        private static void SteerRcs(Ship ship, double deltaX, double deltaY, double deltaV, float seconds)
        {
            double rotation = ship.objSS.fRot;
            double scale = Math.Min(1.0, deltaV / (JumpDrive.GetRcsAcceleration(ship) * seconds)) / deltaV;
            float x = (float)((deltaX * Math.Cos(rotation) + deltaY * Math.Sin(rotation)) * scale);
            float y = (float)((-deltaX * Math.Sin(rotation) + deltaY * Math.Cos(rotation)) * scale);
            Maneuver(ship, x, y, 0f, seconds);
        }

        //Brings the nose round and holds it there, slowing in time to stop on the heading
        private static void Turn(Ship ship, double error, float seconds)
        {
            double acceleration = Constants.CorrectionTurnAcceleration;
            double wanted = Math.Sign(error) * Math.Min(Constants.CorrectionMaxTurnRate, Math.Sqrt(2.0 * acceleration * Math.Abs(error)) * 0.8);
            float input = (float)Math.Max(-1.0, Math.Min(1.0, (wanted - ship.objSS.fW) / seconds / acceleration));
            Maneuver(ship, 0f, 0f, input, seconds);
        }

        private static void Maneuver(Ship ship, float x, float y, float rotation, float seconds)
        {
            NavComThrusting = true;
            try
            {
                ship.Maneuver(x, y, rotation, 0, seconds, Ship.EngineMode.RCS);
            }
            finally
            {
                NavComThrusting = false;
            }
        }

        //Throttled so the burn takes about CorrectionTorchBurnTime, never past what the torch can hold, with the margin
        //left over for the flow to hold the core's temperature with
        private static float GetTorchCycle(Ship ship, double deltaV)
        {
            float limit = JumpDrive.GetSustainableCycle(ship) * (float)Constants.JumpTorchBrakingMargin;
            double wanted = GetTorchBurnAcceleration(JumpDrive.GetTorchAcceleration(ship), deltaV) / Constants.JumpTorchBrakingMargin;
            float low = 0f;
            float high = limit;
            for (int i = 0; i < 20; i++)
            {
                float middle = (low + high) / 2f;
                if (ship.GetMaxTorchThrust(middle) < wanted) low = middle; else high = middle;
            }
            return high;
        }

        private static void TorchOn(Ship ship, double deltaV)
        {
            float high = GetTorchCycle(ship, deltaV);
            Burn burn = GetBurn(ship);
            CondOwner reactor = ship.Reactor;
            if (burn.Flow == null)
            {
                burn.Flow = ship.GetReactorGPMValue(FlowProp) ?? "0";
                burn.CoreTarget = Math.Max(Math.Min(reactor.GetCondAmount(CoreTempCond), Constants.CorrectionCoreMax), Constants.CorrectionCoreMin);
                FzAdditions.Logger.LogInfo($"{ship.strRegID} torch lit at cycle {high:0.###}, holding core at {burn.CoreTarget:0.###} from {reactor.GetCondAmount(CoreTempCond):0.###}, flow was {burn.Flow}");
            }

            ship.SetReactorGPMValue(CycleProp, high.ToString());
            ship.SetReactorGPMValue(FlowProp, GetHoldingFlow(ship, reactor, high, burn.CoreTarget).ToString());
        }

        //The flow that brings the core to the target and holds it there against the cycle's cooling. Thrust is in step
        //with the core's temperature, so holding it holds the thrust, and keeps the core from going out or overheating.
        //Worked out the way FusionIC sets its pellet rate, which a cooling core raises on its own.
        private static float GetHoldingFlow(Ship ship, CondOwner reactor, float cycle, double target)
        {
            double pelletMax = reactor.GetCondAmount(PelletMaxCond);
            if (pelletMax <= 0.0) return 1f;

            double temperature = reactor.GetCondAmount(CoreTempCond);
            bool bus = int.TryParse(ship.GetReactorGPMValue(BusProp), out int knob) && knob > 0;
            double floor = bus ? pelletMax + (0.001 - pelletMax) * Math.Min(temperature / CoreHotLimit, 1.0) : 1.0;

            double heating = cycle * JumpDrive.TorchCoolingPerCycle + (target - temperature) / Constants.CorrectionCoreSettleTime;
            double pellets = heating / JumpDrive.TorchHeatPerPellet;
            if (pelletMax - floor <= 0.0) return 1f;
            return (float)Math.Max(0.0, Math.Min(1.0, (pellets - floor) / (pelletMax - floor)));
        }

        //Puts the flow back the way the pilot had it
        private static void TorchOff(Ship ship)
        {
            ship.SetReactorGPMValue(CycleProp, "0");
            ship.SetThrust(0.0);
            Burn burn = GetBurn(ship);
            if (burn != null && burn.Flow != null)
            {
                ship.SetReactorGPMValue(FlowProp, burn.Flow);
                burn.Flow = null;
            }
        }

        //Each check while a series runs. Any torch cycle the NavCom didn't set is wound back, and an engaged course
        //is dropped, its autopilot thrusts without going through Maneuver. Without a burn any thrust left on, such as
        //thrust a save kept, is cleared, the lockout keeps the controls that would otherwise clear it away.
        public static void HoldControls(Ship ship)
        {
            if (!IsTorchBurning(ship))
            {
                string cycle = ship.GetReactorGPMValue(CycleProp);
                if (!string.IsNullOrEmpty(cycle) && cycle != "0") ship.SetReactorGPMValue(CycleProp, "0");
            }

            ShipSitu situ = ship.objSS;
            if (situ != null && !IsBurning(ship))
            {
                if (situ.vAccRCS != Vector2.zero || situ.fA != 0f) Maneuver(ship, 0f, 0f, 0f, 1E-10f);
                if (situ.vAccIn != Vector2.zero) ship.SetThrust(0.0);
            }

            if (ship.objSS != null && ship.objSS.HasNavData())
            {
                ship.objSS.ResetNavData();
                if (GUIOrbitDraw.Instance != null && GUIOrbitDraw.Instance.COSelf?.ship == ship) GUIOrbitDraw.Instance.SetPropMapData("chkEngage", false.ToString());
            }
        }

        //Shown on the nav console when the player tries to fly a locked ship
        public static void WarnLocked(Ship ship)
        {
            GUIOrbitDraw orbit = GUIOrbitDraw.Instance;
            if (orbit == null || orbit.COSelf?.ship != ship || !orbit.PlayerThrusting || Time.unscaledTime < nextWarning) return;

            nextWarning = Time.unscaledTime + WarningInterval;
            orbit.CGClampWarning(LockedWarning, bSkipDH: true);
        }

        //A burn over part of a time skip, which has no frames to steer in. The skip's own update moves the ship under
        //the acceleration. Turning time is coasted first and the ship is set on its heading at once.
        public struct SkipBurn
        {
            public bool Torch;
            public double TurnTime; //Game seconds
            public double Heading;
            public double AccelerationX; //In AU per second squared
            public double AccelerationY;
            public double DeltaV; //In AU per second
            public double Duration; //Game seconds of burning
        }

        public static Result PlanSkipBurn(Ship ship, JumpDrive.AimTarget aim, bool torchMode, Goal goal, out SkipBurn burn)
        {
            burn = new SkipBurn();
            ShipSitu situ = ship.objSS;
            double velocityX = situ.vVelX - aim.VelocityX;
            double velocityY = situ.vVelY - aim.VelocityY;
            double speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
            GetGoalDeltaV(goal, situ.vPosx - aim.X, situ.vPosy - aim.Y, velocityX, velocityY, speed, out double deltaX, out double deltaY);
            double deltaV = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (goal == Goal.Course ? !IsNeeded(deltaV, speed) : deltaV <= GetFinish(goal, speed)) return Result.Done;

            bool? torch = ChooseEngine(ship, torchMode);
            if (torch == null) return Result.Waiting;

            double acceleration = 0.0;
            burn.Torch = torch.Value;
            if (burn.Torch)
            {
                burn.Heading = GetHeading(deltaX, deltaY);
                burn.TurnTime = GetTurnTime(Math.Abs(WrapAngle(burn.Heading - situ.fRot)));
                acceleration = GetTorchBurnAcceleration(JumpDrive.GetTorchAcceleration(ship), deltaV);

                //Nothing can cut the torch partway through a skip, so a burn that would end inside a no wake zone is on RCS
                double seconds = burn.TurnTime + deltaV / acceleration;
                double endX = situ.vPosx + situ.vVelX * seconds + 0.5 * deltaX * deltaV / acceleration;
                double endY = situ.vPosy + situ.vVelY * seconds + 0.5 * deltaY * deltaV / acceleration;
                burn.Torch = !JumpDrive.IsInNoWakeZone(endX, endY);
            }

            if (!burn.Torch)
            {
                if (!(ship.DeltaVRemainingRCS >= deltaV)) return Result.Failed;
                burn.TurnTime = 0.0;
                acceleration = JumpDrive.GetRcsAcceleration(ship);
            }

            burn.DeltaV = deltaV;
            burn.Duration = deltaV / acceleration;
            burn.AccelerationX = deltaX / deltaV * acceleration;
            burn.AccelerationY = deltaY / deltaV * acceleration;
            return Result.Burning;
        }

        //What a velocity match would take, for its confirmation. Problem says why it can't be made, null if it can.
        //Reactant is what the reactor burns over the torch's part of it, from its burn now scaled to the pellet rate
        //that holds the burn, negative if the reactor's burn can't be read.
        public struct MatchEstimate
        {
            public string Problem;
            public bool Torch;
            public double DeltaV; //In AU per second
            public double Closing; //In AU per second, the speed towards the aim point a cross match keeps
            public double BurnTime; //Game seconds, turning included
            public double RcsFuel;
            public double ReactantA; //In the order of FusionIC.aReactantNames
            public double ReactantB;
        }

        private const string PelletRateCond = "StatICPellRate";
        private const string ReactantRateACond = "StatICDRate";
        private const string ReactantRateBCond = "StatICHe3Rate";

        public static MatchEstimate EstimateMatch(Ship ship, JumpDrive.AimTarget aim, bool torchMode, bool cross)
        {
            MatchEstimate estimate = new MatchEstimate { ReactantA = -1.0, ReactantB = -1.0 };
            ShipSitu situ = ship.objSS;
            double relativeX = situ.vPosx - aim.X;
            double relativeY = situ.vPosy - aim.Y;
            double velocityX = situ.vVelX - aim.VelocityX;
            double velocityY = situ.vVelY - aim.VelocityY;
            GetMatchDeltaV(relativeX, relativeY, velocityX, velocityY, cross, out double deltaX, out double deltaY);
            estimate.DeltaV = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            estimate.Closing = cross ? GetClosingSpeed(relativeX, relativeY, velocityX, velocityY) : 0.0;
            if (estimate.DeltaV <= Constants.CorrectionMinDeltaV / Constants.MetersPerAU)
            {
                estimate.Problem = cross ? "CROSS-VELOCITY ALREADY MATCHED" : "VELOCITY ALREADY MATCHED";
                return estimate;
            }

            bool? torch = ChooseEngine(ship, torchMode);
            if (torch == null)
            {
                estimate.Problem = "NO RCS TO MATCH WITH";
                return estimate;
            }

            estimate.Torch = torch.Value;
            if (!estimate.Torch)
            {
                estimate.BurnTime = estimate.DeltaV / JumpDrive.GetRcsAcceleration(ship);
                estimate.RcsFuel = ship.CalculateRCSFuelConsumption(estimate.DeltaV);
                if (!(ship.DeltaVRemainingRCS >= estimate.DeltaV)) estimate.Problem = "NOT ENOUGH RCS PROPELLANT";
                return estimate;
            }

            double burning = estimate.DeltaV / GetTorchBurnAcceleration(JumpDrive.GetTorchAcceleration(ship), estimate.DeltaV);
            estimate.BurnTime = GetTurnTime(Math.Abs(WrapAngle(GetHeading(deltaX, deltaY) - situ.fRot))) + burning;

            CondOwner reactor = ship.Reactor;
            double pellets = reactor.GetCondAmount(PelletRateCond);
            if (pellets > 0.0)
            {
                double scale = GetTorchCycle(ship, estimate.DeltaV) * JumpDrive.TorchCoolingPerCycle / JumpDrive.TorchHeatPerPellet / pellets * burning;
                estimate.ReactantA = reactor.GetCondAmount(ReactantRateACond) * scale;
                estimate.ReactantB = reactor.GetCondAmount(ReactantRateBCond) * scale;
            }
            return estimate;
        }

        //Turns for a torch burn, then burns for as much of the burn as the skip has left. Left reads what the skip has
        //left and pass runs part of it. False if the RCS propellant ran short. The torch draws on fusion that runs
        //regardless, so only RCS is billed.
        public static bool RunSkipBurn(Ship ship, SkipBurn burn, Func<double> left, Action<double> pass, string log)
        {
            ShipSitu situ = ship.objSS;
            if (burn.Torch && burn.TurnTime > 0.0)
            {
                pass(Math.Min(burn.TurnTime, left()));
                if (left() <= 0.0) return true;
            }
            if (burn.Torch)
            {
                situ.fRot = (float)burn.Heading;
                situ.fW = 0f;
            }

            double piece = Math.Min(burn.Duration, left());
            double startX = situ.vVelX;
            double startY = situ.vVelY;
            SetSkipAcceleration(ship, burn.Torch, burn.AccelerationX, burn.AccelerationY);
            try
            {
                pass(piece);
            }
            finally
            {
                SetSkipAcceleration(ship, burn.Torch, 0.0, 0.0);
            }

            double deltaV = burn.DeltaV * piece / burn.Duration;
            string fuel = "";
            bool enough = true;
            if (!burn.Torch)
            {
                float wanted = (float)ship.CalculateRCSFuelConsumption(deltaV);
                float taken = ship.RemoveGasMass(wanted);
                enough = taken >= wanted * 0.999f;
                fuel = $", RCS propellant wanted {wanted:G6} taken {taken:G6}";
            }

            double measured = MathUtils.GetDistance(startX, startY, situ.vVelX, situ.vVelY);
            FzAdditions.Logger.LogInfo($"{log} on {(burn.Torch ? "torch" : "RCS")}: {piece:0.#}s of {burn.Duration:0.#}s, turn {burn.TurnTime:0.#}s, "
                + $"planned {MathUtils.GetDistUnits(deltaV)}/s, measured {MathUtils.GetDistUnits(measured)}/s with gravity{fuel}");
            return enough;
        }

        //Sets the ship accelerating for a skip burn, or stops it with zero
        public static void SetSkipAcceleration(Ship ship, bool torch, double x, double y)
        {
            ShipSitu situ = ship.objSS;
            if (torch) situ.vAccIn = new Vector2((float)x, (float)y);
            else situ.vAccRCS = new Vector2((float)x, (float)y);
            ship.UpdateDockedShips(situ);
        }
    }
}
