using Freznel.FzAdditions.UI;
using Ostranauts.ShipGUIs.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //A jump from the button to its arrival. Held on the ship rather than on the nav module, whose objects go
    //when the console closes, so the charge carries on and a reopened console finds it still running.
    //A series is a condition on the ship's own CO, which saves with it, and its target is saved on the nav station
    //it was started from. A charge only lasts as long as its effect.
    public static class JumpSequence
    {
        private const double CatchUpStep = 300.0; //Game seconds of recharge at a time in a time skip, when the wait can't be told
        private const double MinSkipStep = 1.0; //Game seconds, so a recharge that lands just short doesn't creep up on its charge
        private const int MaxSkipCorrections = 3; //Burns before each jump in a time skip, gravity can leave a little to trim
        private const string SkipLog = "[SFF series]";

        //Real seconds between series checks, a share of the wait so they close in as the jump nears. The longest is
        //16 game seconds at the game's top speed, which is what a wait that turns out shorter than expected can lose.
        //A correction burn steers every frame.
        private const float MaxCheckInterval = 1f;
        private const float MinCheckInterval = 0.05f;
        private const float CheckWaitShare = 0.25f;

        private const float ReplanInterval = 1f; //Real seconds between re-plans of a running series while it recharges

        private static float nextSeriesCheck;
        private static float nextReplan;

        //The ship whose series the time skip under way has already played out, so its cells don't recharge twice
        private static string skippedShip;

        //The ship whose cells still need their clocks set once the skip has finished
        private static string pendingSkipShip;

        //The jump whose charge is draining the cells, and how far its charge had got when they were last drained. What
        //has been drained is saved on the ship, a load starts the charge again with this.
        private static double drainEnergy;
        private static float drainProgress;
        private static List<CondOwner> drainCells = new List<CondOwner>(); //Read once, scanning the ship every frame of the charge adds up

        public enum SeriesState { Idle, Jumping, Thrusting, Recharging, Correcting, NoThrust }

        //Saved as a number on the ship's CO, so new reasons go on the end
        public enum JumpFailure { None, NoShip, Thrusting, HeldInPlace, FieldStrength, SettingTooLow, NotCharged, CellsTooSmall, LandingBlocked, NoContour, NoTarget, TargetLost, CorrectionFailed, MatchTargetLost, MatchFailed, Matching }

        //Outlives the nav console and any save loaded, a series can take days to recharge between jumps
        public static void Init()
        {
            GameObject runner = new GameObject("FzJumpSeries");
            runner.AddComponent<SeriesRunner>();
            UnityEngine.Object.DontDestroyOnLoad(runner);
        }

        public static bool IsActive(Ship ship)
        {
            return FzJumpEffect.IsCharging(ship);
        }

        //Energy is the total to spend in kWh. False if the ship can't jump, nothing has changed then. A single jump
        //keeps its energy on the ship while it charges, so a save made partway starts the charge again when it loads.
        //A series starts its own jumps again. The cells are drained as the charge builds, whatever has gone is lost
        //if the jump doesn't happen.
        public static bool Start(Ship ship, double energy)
        {
            if (CheckJump(ship, energy) != JumpFailure.None || !FzJumpEffect.Play(ship, () => Complete(ship, energy))) return false;
            if (!IsSeriesRunning(ship)) ship.ShipCO.SetCondAmount(Constants.JumpChargingCond, energy);
            drainEnergy = energy;
            drainProgress = 0f;
            drainCells = JumpCell.GetCells(ship);
            return true;
        }

        //Every frame. Each step takes the share of what is still owed that the charge has built through since the last,
        //so the cells are empty of the jump's energy as the charge completes, wherever it started from.
        private static void DrainCharge()
        {
            Ship ship = CrewSim.shipCurrentLoaded;
            if (drainEnergy <= 0.0 || !FzJumpEffect.IsCharging(ship)) return;

            float progress = FzJumpEffect.ChargeProgress;
            if (progress <= drainProgress) return;

            double owed = GetOwed(ship, drainEnergy);
            double share = drainProgress < 1f ? (progress - drainProgress) / (1f - drainProgress) : 1.0;
            drainProgress = progress;

            //A cell switched off or replaced since the jump started has no part in it
            drainCells.RemoveAll(cell => cell == null || !JumpCell.IsOn(cell));
            if (owed > 0.0) ship.ShipCO.AddCondAmount(Constants.JumpDrainedCond, SpendEnergy(drainCells, owed * share));
        }

        //kWh of the jump still to come out of the cells
        private static double GetOwed(Ship ship, double energy)
        {
            return Math.Max(energy - (ship?.ShipCO?.GetCondAmount(Constants.JumpDrainedCond) ?? 0.0), 0.0);
        }

        //What was drained is gone once the jump ends, whether or not it happened
        private static void ClearDrain(Ship ship)
        {
            ship?.ShipCO?.ZeroCondAmount(Constants.JumpDrainedCond);
            if (ship != null && ship != CrewSim.shipCurrentLoaded) return;
            drainEnergy = 0.0;
            drainCells.Clear();
        }

        //Everything here that isn't saved belongs to the world being left behind
        public static void Reset()
        {
            nextSeriesCheck = 0f;
            nextReplan = 0f;
            skippedShip = null;
            pendingSkipShip = null;
            drainEnergy = 0.0;
            drainProgress = 0f;
            drainCells.Clear();
            FzJumpEffect.Stop();
            FzJumpFlash.Stop();
        }

        //A single jump that was charging when the game was saved, its charge starts again from the beginning
        private static void ResumeCharge(Ship ship)
        {
            double energy = ship?.ShipCO?.GetCondAmount(Constants.JumpChargingCond) ?? 0.0;
            if (energy <= 0.0 || FzJumpEffect.IsPlaying(ship) || IsSeriesRunning(ship)) return;

            ship.ShipCO.ZeroCondAmount(Constants.JumpChargingCond);
            FzAdditions.Logger.LogInfo($"{ship.strRegID} jump of {energy:0.0} kWh charging again after a load");
            if (!Start(ship, energy))
            {
                JumpFailure failure = CheckJump(ship, energy);
                FailJump(ship, failure != JumpFailure.None ? failure : JumpFailure.NoContour);
            }
        }

        //Why a jump of this energy can't start right now
        public static JumpFailure CheckJump(Ship ship, double energy)
        {
            JumpFailure failure = GetReadyFailure(ship, energy);
            if (failure != JumpFailure.None) return failure;
            return HasCharge(JumpCell.GetCells(ship), GetOwed(ship, energy)) ? JumpFailure.None : JumpFailure.NotCharged;
        }

        //Why a series of this energy can't begin, it doesn't need to be charged yet
        public static JumpFailure CheckSeries(Ship ship, double energy)
        {
            JumpFailure failure = GetReadyFailure(ship, energy);
            if (failure != JumpFailure.None) return failure;
            return JumpCell.GetCells(ship).Sum(JumpCell.GetCapacity) > FieldContour.GetTotalFieldEnergy(ship) ? JumpFailure.None : JumpFailure.CellsTooSmall;
        }

        //A series with a target steers to it and has the ship's thrust to itself. One without runs along its velocity.
        public static bool IsSteering(Ship ship)
        {
            return IsSeriesRunning(ship) && SeriesTarget.HasTarget(ship);
        }

        //What a jump of this energy would cost, empty if nothing. The landing is checked at both ends of the uncertainty.
        public static List<string> GetJumpWarnings(Ship ship, double energy)
        {
            List<string> warnings = GetRemovalWarnings(ship);
            double excess = energy - FieldContour.GetTotalFieldEnergy(ship);
            double jumpTime = JumpDrive.GetJumpTime(excess, JumpDrive.GetAverageStrength(ship));
            GetDirection(ship, out double directionX, out double directionY, out double speed);

            foreach (double scale in GetUncertaintyScales(ship))
            {
                double distance = JumpDrive.GetDistance(speed, jumpTime * scale);
                if (IsLandingClear(ship, directionX * distance, directionY * distance, out string body)) continue;

                warnings.Add($"JUMP WOULD LAND INSIDE {body.ToUpper()}");
                break;
            }
            return warnings;
        }

        //The same for a series of this energy, from each arrival its plan has, or along the velocity without a target.
        //The bodies are taken as they are now, a series can run for long enough that they will have moved, so its
        //jumps check their landings again as they go.
        public static List<string> GetSeriesWarnings(Ship ship, double energy, NavPOI poi, bool torch)
        {
            if (!JumpDrive.ResolvePOI(ship, poi, out JumpDrive.AimTarget aim)) return GetStraightSeriesWarnings(ship, energy);

            List<string> warnings = GetRemovalWarnings(ship);

            SeriesPlan plan = PlanSeries(ship, energy, aim, torch);
            double[] scales = GetUncertaintyScales(ship);
            int jump = 0;

            foreach (SeriesPlan.Step step in plan.Steps.Where(step => step.Kind == SeriesPlan.StepKind.Jump))
            {
                jump++;
                foreach (double scale in scales)
                {
                    double x = aim.X + step.StartX + (step.EndX - step.StartX) * scale;
                    double y = aim.Y + step.StartY + (step.EndY - step.StartY) * scale;
                    if (IsLandingClear(ship, x - ship.objSS.vPosx, y - ship.objSS.vPosy, out string body)) continue;

                    warnings.Add($"JUMP {jump} WOULD LAND INSIDE {body.ToUpper()}");
                    return warnings;
                }
            }
            return warnings;
        }

        private static List<string> GetStraightSeriesWarnings(Ship ship, double energy)
        {
            List<string> warnings = GetRemovalWarnings(ship);
            List<CondOwner> cells = JumpCell.GetCells(ship);
            double need = FieldContour.GetTotalFieldEnergy(ship);
            double capacity = cells.Sum(JumpCell.GetCapacity);
            double held = JumpCell.GetTotalCharge(cells);
            double fillRate = JumpCell.GetFillRate(ship, cells, JumpTap.GetActiveTaps(ship));
            double strength = JumpDrive.GetAverageStrength(ship);
            GetDirection(ship, out double directionX, out double directionY, out double speed);
            JumpDrive.JumpSeries series = JumpDrive.GetSeries(energy, need, capacity, speed, strength);
            double[] scales = GetUncertaintyScales(ship);

            for (int jump = 1; jump <= series.Jumps; jump++)
            {
                double jumped = series.FullJumpDistance * Math.Min(jump, series.FullJumps);
                if (jump > series.FullJumps) jumped += JumpDrive.GetDistance(speed, JumpDrive.GetJumpTime(energy - series.FullJumps * capacity - need, strength));
                double coast = speed * JumpDrive.GetCoastTime(Math.Min(capacity * jump, energy), held, fillRate);

                foreach (double scale in scales)
                {
                    double distance = jumped * scale + coast;
                    if (IsLandingClear(ship, directionX * distance, directionY * distance, out string body)) continue;

                    warnings.Add($"JUMP {jump} WOULD LAND INSIDE {body.ToUpper()}");
                    return warnings;
                }
            }
            return warnings;
        }

        //The series as it would run from the ship as it is now, the same plan its jumps and corrections follow
        public static SeriesPlan PlanSeries(Ship ship, double energy, JumpDrive.AimTarget aim, bool torch)
        {
            ShipSitu situ = ship.objSS;
            List<CondOwner> cells = JumpCell.GetCells(ship);
            JumpDrive.JumpTarget target = JumpDrive.GetTarget(ship, aim, torch, series: true);

            return SeriesPlan.Build(new SeriesPlan.Input
            {
                Ship = ship,
                RelativeX = situ.vPosx - aim.X,
                RelativeY = situ.vPosy - aim.Y,
                VelocityX = situ.vVelX - aim.VelocityX,
                VelocityY = situ.vVelY - aim.VelocityY,
                AimX = aim.X,
                AimY = aim.Y,
                TargetVelocityX = aim.VelocityX,
                TargetVelocityY = aim.VelocityY,
                StopDistance = target.StopDistance,
                Energy = energy,
                Need = FieldContour.GetTotalFieldEnergy(ship),
                Capacity = cells.Sum(JumpCell.GetCapacity),
                Held = JumpCell.GetTotalCharge(cells),
                FullMargin = JumpCell.GetFullMargin(cells),
                FillRate = JumpCell.GetFillRate(ship, cells, JumpTap.GetActiveTaps(ship)),
                Strength = JumpDrive.GetAverageStrength(ship),
                RcsAcceleration = JumpDrive.GetRcsAcceleration(ship),
                RcsDeltaV = JumpDrive.GetRcsAcceleration(ship) > 0.0 ? ship.DeltaVRemainingRCS : 0.0,
                TorchAcceleration = torch && IsTorchUsableOnPlan(ship) ? JumpDrive.GetTorchAcceleration(ship) : 0.0,
                Torch = torch,
                Rotation = situ.fRot
            });
        }

        //The plan works out the no wake zones along its own path, anything else stands for the whole series
        private static bool IsTorchUsableOnPlan(Ship ship)
        {
            JumpDrive.TorchBlock block = JumpDrive.GetTorchBlock(ship, ship.objSS.vPosx, ship.objSS.vPosy);
            return block == JumpDrive.TorchBlock.None || block == JumpDrive.TorchBlock.NoWakeZone;
        }

        private static double[] GetUncertaintyScales(Ship ship)
        {
            double uncertainty = JumpDrive.GetUncertainty(ship) / 100.0;
            return new[] { 1.0 - uncertainty, 1.0, 1.0 + uncertainty };
        }

        private static List<string> GetRemovalWarnings(Ship ship)
        {
            FieldContour.RemovalReport report = FieldContour.GetRemovalReport(ship);
            List<string> warnings = new List<string>();
            if (report.Destroyed > 0) warnings.Add($"{report.Destroyed} OBJECTS DESTROYED");
            if (report.Broken > 0) warnings.Add($"{report.Broken} OBJECTS BROKEN");
            if (report.Crew > 0) warnings.Add($"{report.Crew} CREW LOST");
            if (report.Undocked > 0) warnings.Add($"{report.Undocked} SHIPS UNDOCKED");
            return warnings;
        }

        public static void Cancel(Ship ship)
        {
            EndSeries(ship);
            ship?.ShipCO?.ZeroCondAmount(Constants.JumpChargingCond);
            ClearDrain(ship);
            if (IsActive(ship)) FzJumpEffect.Interrupt();
        }

        //Hands the ship's thrust back
        private static void EndSeries(Ship ship)
        {
            ship?.ShipCO?.ZeroCondAmount(Constants.SeriesEnergyCond);
            JumpCorrection.Stop(ship);
            SeriesTarget.Clear(ship);
        }

        //An installed cell or emitter, in whatever state
        public static bool IsDrivePart(CondOwner co)
        {
            return co != null && co.HasCond("IsInstalled") && (co.HasCond(Constants.CellCond) || co.HasCond(Constants.EmitterCond));
        }

        public static bool IsWorkingDrivePart(CondOwner co)
        {
            return IsDrivePart(co) && !co.HasCond("IsDamaged") && !co.HasCond("IsOff");
        }

        //A dock group shares its field, so a change to the drive on any ship ends a series on any of them
        public static void CancelForDriveChange(Ship ship)
        {
            if (ship == null) return;

            List<Ship> group = new List<Ship>(ship.GetAllDockedShips()) { ship };
            foreach (Ship member in group)
            {
                if (IsSeriesRunning(member)) Cancel(member);
            }
        }

        public static bool IsSeriesRunning(Ship ship)
        {
            return ship?.ShipCO != null && ship.ShipCO.GetCondAmount(Constants.SeriesEnergyCond) > 0.0;
        }

        //Energy the running series has left to spend in kWh, negative if none is running
        public static double GetSeriesEnergy(Ship ship)
        {
            return IsSeriesRunning(ship) ? ship.ShipCO.GetCondAmount(Constants.SeriesEnergyCond) : -1.0;
        }

        //Wait is game seconds until the cells are ready for the next jump, negative if they won't be at current rates
        public static SeriesState GetSeriesState(Ship ship, List<CondOwner> cells, out double wait)
        {
            wait = 0.0;
            if (!IsSeriesRunning(ship)) return SeriesState.Idle;
            if (FzJumpEffect.IsPlaying(ship)) return SeriesState.Jumping;
            if (JumpCorrection.IsBurning(ship)) return SeriesState.Correcting;
            if (JumpCorrection.IsWaiting(ship)) return SeriesState.NoThrust;
            if (IsThrusting(ship.objSS)) return SeriesState.Thrusting;

            wait = GetChargeWait(cells, Math.Min(GetSeriesEnergy(ship), cells.Sum(JumpCell.GetCapacity)));
            return SeriesState.Recharging;
        }

        private static double GetChargeWait(List<CondOwner> cells, double energy)
        {
            return JumpCell.GetTimeUntilCharged(cells, energy - JumpCell.GetFullMargin(cells));
        }

        //Energy is the total for the whole series in kWh. Station is the nav station it is started from, which keeps
        //its target. Without a target it runs along the ship's velocity. False if it can't begin, it doesn't need to
        //be charged yet.
        public static bool StartSeries(Ship ship, double energy, CondOwner station, NavPOI poi, bool torch)
        {
            if (CheckSeries(ship, energy) != JumpFailure.None) return false;

            SeriesTarget.Clear(ship);
            JumpDrive.AimTarget aim = new JumpDrive.AimTarget();
            bool targeted = station != null && JumpDrive.ResolvePOI(ship, poi, out aim);
            if (targeted) SeriesTarget.Save(ship, station, aim, torch);

            ship.ShipCO.SetCondAmount(Constants.SeriesEnergyCond, energy);
            nextSeriesCheck = 0f;
            if (targeted)
            {
                JumpCorrection.HoldControls(ship);
                FzAdditions.Logger.LogInfo($"{ship.strRegID} series of {energy:0.0} kWh to {aim.Name}, correcting on {(torch ? "torch" : "RCS")}");
            }
            else FzAdditions.Logger.LogInfo($"{ship.strRegID} series of {energy:0.0} kWh with no target, along its velocity");
            return true;
        }

        //The energy for the series' next jump, and the cells it will come from. Finishes the series if what is left
        //can't raise a field. False if there is no next jump to wait for, for now at least.
        private static bool GetNextSeriesJump(Ship ship, out double energy, out List<CondOwner> cells)
        {
            energy = 0.0;
            cells = null;

            double need = FieldContour.GetTotalFieldEnergy(ship);
            double seriesEnergy = GetSeriesEnergy(ship);
            if (seriesEnergy < need)
            {
                FinishSeries(ship);
                return false;
            }

            cells = JumpCell.GetCells(ship);
            double capacity = cells.Sum(JumpCell.GetCapacity);
            energy = Math.Min(seriesEnergy, capacity);
            return capacity > need;
        }

        //Corrects the course, then starts the next jump once the last has finished and the cells have recharged.
        //Having too few cells only delays it, which is also how a ship looks while it is still loading. A ship's series
        //waits while the ship isn't the one loaded. Returns the game seconds it expects to wait, negative if it can't tell.
        private static double AdvanceSeries()
        {
            Ship ship = CrewSim.shipCurrentLoaded;
            JumpCorrection.StopOthers(ship);
            if (GUIFFWD.Active) return -1.0;

            //Nothing steers a burn while the ship is being edited, so none is left thrusting
            if (CrewSim.bShipEdit)
            {
                JumpCorrection.Stop(ship);
                return -1.0;
            }

            //A match keeps a ship from jumping, so it has the checks to itself
            if (VelocityMatch.IsMatching(ship))
            {
                VelocityMatch.Advance(ship);
                return JumpCorrection.IsBurning(ship) ? 0.0 : -1.0;
            }

            //A charge plays on the overlay, which isn't there until the ship's view is. Corrections don't need it.
            bool view = FzOverlayCamera.Get() != null;
            if (view) ResumeCharge(ship);
            if (!IsSeriesRunning(ship)) return -1.0;

            bool steering = SeriesTarget.HasTarget(ship);
            if (steering) JumpCorrection.HoldControls(ship);
            if (FzJumpEffect.Playing) return 0.0;

            if (steering)
            {
                if (!SeriesTarget.TryResolve(ship, out JumpDrive.AimTarget aim))
                {
                    FailJump(ship, JumpFailure.TargetLost);
                    return 0.0;
                }

                switch (JumpCorrection.Update(ship, aim, SeriesTarget.CorrectsOnTorch(ship), JumpCorrection.Goal.Course))
                {
                    case JumpCorrection.Result.Burning: return 0.0;
                    case JumpCorrection.Result.Waiting: return -1.0;
                    case JumpCorrection.Result.Failed:
                        FailJump(ship, JumpFailure.CorrectionFailed);
                        return 0.0;
                }

                if (Time.unscaledTime >= nextReplan)
                {
                    nextReplan = Time.unscaledTime + ReplanInterval;
                    RefreshSeries(ship);
                    if (!IsSeriesRunning(ship)) return 0.0;
                }
            }

            //A series without a target leaves the thrust to the player, a burn only delays it
            if (IsThrusting(ship.objSS) || !GetNextSeriesJump(ship, out double energy, out List<CondOwner> cells)) return -1.0;

            if (!HasCharge(cells, GetOwed(ship, energy))) return GetChargeWait(cells, GetOwed(ship, energy));
            if (!view) return -1.0;

            if (!Start(ship, energy))
            {
                JumpFailure failure = CheckJump(ship, energy);
                FailJump(ship, failure != JumpFailure.None ? failure : JumpFailure.NoContour);
            }
            return 0.0;
        }

        private static float GetCheckInterval(double wait)
        {
            if (wait < 0.0) return MaxCheckInterval;
            return Mathf.Clamp((float)wait * CheckWaitShare / Mathf.Max(Time.timeScale, 0.01f), MinCheckInterval, MaxCheckInterval);
        }

        //True while a time skip is under way that has played this ship's series out itself
        public static bool IsSkipped(Ship ship)
        {
            return GUIFFWD.Active && ship != null && ship.strRegID == skippedShip;
        }

        //Once the time skip has caught every object up. The skip is spent, without this each cell would recharge for
        //all of it again on its next update and the next jump would be ready the moment the skip ended. Their clocks
        //only reach the end of the skip in that catch up, so this can't be done when the series is played out.
        public static void FinishSkip(Ship ship)
        {
            if (ship == null || ship.strRegID != pendingSkipShip) return;

            pendingSkipShip = null;
            List<CondOwner> cells = JumpCell.GetAllCells(ship);
            foreach (CondOwner cell in cells)
            {
                if (cell.Pwr != null) JumpCell.SetTimeSinceUpdate(cell.Pwr, 0.0);
            }
            FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} skip finished, cells hold {JumpCell.GetTotalCharge(cells):0.0} of {cells.Sum(JumpCell.GetCapacity):0.0} kWh");
        }

        //Plays out a running series over a super fast forward, which would otherwise apply its seconds to the ship all
        //at once and leave no frames for AdvanceSeries. Advance runs the game's own update for part of the skip, so the
        //ship coasts to each correction and each jump from where the last left it, the same as it would in real time.
        //Cells recharge piece by piece, a single piece would only ever fill them once. Whatever is left of the skip
        //once the series has ended or stopped is advanced as one piece.
        public static void RunSkip(Ship ship, double seconds, Action<double> advance)
        {
            skippedShip = ship.strRegID;
            FzJumpEffect.Stop();
            JumpCorrection.Stop(ship);

            CondOwner reactor = ship.Reactor;
            ReactorFuel.BurnReceipt totalBurn = new ReactorFuel.BurnReceipt();
            ReactorFuel.BurnReceipt phaseBurn = new ReactorFuel.BurnReceipt();
            ReactorFuel.GetHeld(reactor, out double startA, out double startB);
            double left = seconds;
            double phaseSeconds = 0.0;
            double phaseCharge = 0.0;
            int jumps = 0;
            int corrections = 0; //Since the last jump
            bool outOfFuel = false;

            FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} skip of {seconds:0}s, {GetSeriesEnergy(ship):0.0} kWh left in series, {FormatHeld(startA, startB)}");

            //Runs part of the skip, recharging the cells for it and billing the reactor
            void Pass(double piece, List<CondOwner> cells)
            {
                advance(piece);
                left -= piece;
                phaseSeconds += piece;
                phaseCharge += RechargeCells(cells, piece);

                ReactorFuel.BurnReceipt receipt = ReactorFuel.PayPending(reactor);
                phaseBurn.Add(receipt);
                if (receipt.Short)
                {
                    outOfFuel = true;
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} tanks ran out with {left:0}s left");
                }
            }

            bool steering = SeriesTarget.HasTarget(ship);
            while (left > 0.0 && IsSeriesRunning(ship) && !outOfFuel)
            {
                JumpDrive.AimTarget aim = new JumpDrive.AimTarget();
                if (steering && !SeriesTarget.TryResolve(ship, out aim))
                {
                    FailJump(ship, JumpFailure.TargetLost);
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended, target lost");
                    break;
                }

                //Whatever the ship drifted before the skip and through its last piece
                RefreshSeries(ship);
                if (!IsSeriesRunning(ship))
                {
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended on its re-plan");
                    break;
                }
                if (!GetNextSeriesJump(ship, out double energy, out List<CondOwner> cells)) break;

                //A burn the update didn't carry out would otherwise be tried again until the propellant ran out
                JumpCorrection.Result correction = JumpCorrection.Result.Done;
                JumpCorrection.SkipBurn burn = new JumpCorrection.SkipBurn();
                if (steering)
                {
                    JumpCorrection.HoldControls(ship);
                    correction = JumpCorrection.PlanSkipBurn(ship, aim, SeriesTarget.CorrectsOnTorch(ship), JumpCorrection.Goal.Course, out burn);
                }
                if (correction == JumpCorrection.Result.Burning && corrections >= MaxSkipCorrections)
                {
                    if (corrections++ == MaxSkipCorrections) FzAdditions.Logger.LogWarning($"{SkipLog} {ship.strRegID} course still off after {MaxSkipCorrections} corrections, jumping without more");
                    correction = JumpCorrection.Result.Done;
                }
                if (correction == JumpCorrection.Result.Failed)
                {
                    FailJump(ship, JumpFailure.CorrectionFailed);
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended, not enough RCS propellant to correct course");
                    break;
                }
                if (correction == JumpCorrection.Result.Waiting)
                {
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series waiting, no thrust to correct course with");
                    break;
                }
                if (correction == JumpCorrection.Result.Burning)
                {
                    corrections++;
                    if (!JumpCorrection.RunSkipBurn(ship, burn, () => left, piece => Pass(piece, cells), SkipLog + " " + ship.strRegID + " correction"))
                    {
                        FailJump(ship, JumpFailure.CorrectionFailed);
                        FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended, RCS propellant ran out mid correction");
                        break;
                    }
                    continue;
                }

                if (HasCharge(cells, GetOwed(ship, energy)))
                {
                    LogRecharge(ship, jumps + 1, phaseSeconds, phaseCharge, phaseBurn);
                    totalBurn.Add(phaseBurn);
                    phaseBurn = new ReactorFuel.BurnReceipt();
                    phaseSeconds = 0.0;
                    phaseCharge = 0.0;

                    JumpFailure failure = GetReadyFailure(ship, energy);
                    if (failure != JumpFailure.None)
                    {
                        FailJump(ship, failure);
                        FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended, not able to jump");
                        break;
                    }

                    GetJump(ship, energy, out double moveX, out double moveY, out double jumpTime);
                    if (!IsLandingClear(ship, moveX, moveY, out string body))
                    {
                        FailJump(ship, JumpFailure.LandingBlocked);
                        FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended, jump would have landed at {body}");
                        break;
                    }

                    ApplyJump(ship, energy, moveX, moveY, jumpTime);
                    jumps++;
                    corrections = 0;
                    ContinueSeries(ship, energy);
                    continue;
                }

                double wait = GetChargeWait(cells, energy);
                double step = Math.Min(wait >= 0.0 ? Math.Max(wait, MinSkipStep) : CatchUpStep, left);
                double before = phaseCharge;
                Pass(step, cells);

                //Nothing is feeding the cells, the rest of the skip won't change that
                if (phaseCharge <= before) break;
            }

            //What a finished or stopped series leaves of the skip still charges the cells, the same as any other skip
            if (left > 0.0)
            {
                if (outOfFuel) advance(left);
                else Pass(left, JumpCell.GetCells(ship));
            }
            LogRecharge(ship, jumps + 1, phaseSeconds, phaseCharge, phaseBurn);
            totalBurn.Add(phaseBurn);

            pendingSkipShip = ship.strRegID;

            ReactorFuel.GetHeld(reactor, out double endA, out double endB);
            FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} done, {jumps} jumps, {Math.Max(GetSeriesEnergy(ship), 0.0):0.0} kWh left in series, burn {totalBurn.Burn:G6}, "
                + $"{FusionIC.aReactantNames[0]} wanted {totalBurn.WantedA:G6} taken {totalBurn.TakenA:G6} measured {startA - endA:G6}, "
                + $"{FusionIC.aReactantNames[1]} wanted {totalBurn.WantedB:G6} taken {totalBurn.TakenB:G6} measured {startB - endB:G6}, {FormatHeld(endA, endB)}");
        }

        //Spends a jump from the series and works the rest out again from where the jump left the ship, which takes out
        //the jump's uncertainty and any drift. Finishes the series once it has arrived or gone past, which a jump can't
        //come back from, or once what is left couldn't raise another field and the target is out of reach anyway.
        private static void ContinueSeries(Ship ship, double spent)
        {
            if (!IsSeriesRunning(ship)) return;

            double left = GetSeriesEnergy(ship) - spent;
            if (!SeriesTarget.HasTarget(ship)) SpendSeriesEnergy(ship, left);
            else Replan(ship, left, afterJump: true);
        }

        //Between jumps, so drift while the cells recharge doesn't throw the series off. Until its course is corrected a
        //ship can be heading away from the target, so only reaching it counts as arriving here.
        private static void RefreshSeries(Ship ship)
        {
            if (!IsSteering(ship) || JumpCorrection.IsBurning(ship) || FzJumpEffect.IsPlaying(ship)) return;
            Replan(ship, GetSeriesEnergy(ship), afterJump: false);
        }

        private static void Replan(Ship ship, double left, bool afterJump)
        {
            if (!SeriesTarget.TryResolve(ship, out JumpDrive.AimTarget aim))
            {
                FailJump(ship, JumpFailure.TargetLost);
                return;
            }

            ShipSitu situ = ship.objSS;
            JumpDrive.JumpTarget target = JumpDrive.GetTarget(ship, aim, SeriesTarget.CorrectsOnTorch(ship), series: true);
            if (JumpCorrection.HasArrived(situ.vPosx - aim.X, situ.vPosy - aim.Y, situ.vVelX - aim.VelocityX, situ.vVelY - aim.VelocityY, target.StopDistance, afterJump))
            {
                FinishSeries(ship);
                FzAdditions.Logger.LogInfo($"{ship.strRegID} series arrived at {aim.Name}, {MathUtils.GetDistUnits(target.Distance)} from it");
                return;
            }

            List<CondOwner> cells = JumpCell.GetCells(ship);
            double need = FieldContour.GetTotalFieldEnergy(ship);
            double capacity = cells.Sum(JumpCell.GetCapacity);
            double energy = JumpDrive.GetSeriesEnergy(target, need, capacity, target.RelativeSpeed, JumpDrive.GetAverageStrength(ship), JumpCell.GetTotalCharge(cells), JumpCell.GetFillRate(ship, cells, JumpTap.GetActiveTaps(ship)));

            //Out of reach from here, what is left carries on as it was
            if (energy >= need) left = Math.Min(energy, capacity * Constants.MaxSeriesJumps);

            SpendSeriesEnergy(ship, left);
            if (afterJump && IsSeriesRunning(ship)) FzAdditions.Logger.LogInfo($"{ship.strRegID} series replanned to {left:0.0} kWh, {MathUtils.GetDistUnits(target.Distance)} from {aim.Name}");
        }

        //Left is what the series has after its jump, finished once that couldn't raise another field
        private static void SpendSeriesEnergy(Ship ship, double left)
        {
            if (left <= 0.0 || left < FieldContour.GetTotalFieldEnergy(ship)) FinishSeries(ship);
            else ship.ShipCO.SetCondAmount(Constants.SeriesEnergyCond, left);
        }

        //A series without a target leaves the thrust to the player, and a burn going on as the skip starts can't be played out
        public static bool CanRunSkip(Ship ship)
        {
            return IsSeriesRunning(ship) && (SeriesTarget.HasTarget(ship) || !IsThrusting(ship.objSS));
        }

        //Seconds of recharge for each cell, as if that long had passed since each last ran. Returns the kWh gained.
        private static double RechargeCells(List<CondOwner> cells, double seconds)
        {
            double held = JumpCell.GetTotalCharge(cells);
            foreach (CondOwner cell in cells)
            {
                if (cell.Pwr == null) continue;
                JumpCell.SetTimeSinceUpdate(cell.Pwr, seconds);
                JumpCell.Recharge(cell.Pwr, seconds);
            }
            return JumpCell.GetTotalCharge(cells) - held;
        }

        private static void LogRecharge(Ship ship, int jump, double seconds, double charge, ReactorFuel.BurnReceipt burn)
        {
            if (seconds <= 0.0) return;

            ReactorFuel.GetHeld(ship.Reactor, out double heldA, out double heldB);
            FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} recharge before jump {jump}: {seconds:0}s, {charge:0.0} kWh gained, burn {burn.Burn:G6} ({(charge > 0.0 ? burn.Burn / charge : 0.0):G6} per kWh), "
                + $"{FusionIC.aReactantNames[0]} wanted {burn.WantedA:G6} taken {burn.TakenA:G6}, {FusionIC.aReactantNames[1]} wanted {burn.WantedB:G6} taken {burn.TakenB:G6}, {FormatHeld(heldA, heldB)}");
        }

        private static string FormatHeld(double heldA, double heldB)
        {
            return $"tanks {FusionIC.aReactantNames[0]} {heldA:G8} {FusionIC.aReactantNames[1]} {heldB:G8}";
        }

        //Body names the first one the landing is too close to
        private static bool IsLandingClear(Ship ship, double moveX, double moveY, out string body)
        {
            body = null;
            double x = ship.objSS.vPosx + moveX;
            double y = ship.objSS.vPosy + moveY;
            double buffer = Constants.JumpBufferObject / Constants.MetersPerAU;

            foreach (BodyOrbit orbit in CrewSim.system.aBOs.Values)
            {
                if (orbit.IsPlaceholder() || orbit.IsAsteroidField) continue;
                if (MathUtils.GetDistance(x, y, orbit.dXReal, orbit.dYReal) >= CollisionManager.GetCollisionDistanceAU(ship, orbit) + buffer) continue;

                body = orbit.strName;
                return false;
            }
            return true;
        }

        //Runs as the charge lands. Conditions can have changed over the charge, false ends it without jumping.
        private static bool Complete(Ship ship, double energy)
        {
            ship.ShipCO.ZeroCondAmount(Constants.JumpChargingCond);
            JumpFailure failure = GetReadyFailure(ship, energy);
            if (failure != JumpFailure.None)
            {
                FailJump(ship, failure);
                return false;
            }

            //A series can wait for charge, anything else stops it
            bool inSeries = IsSeriesRunning(ship);
            if (!HasCharge(JumpCell.GetCells(ship), GetOwed(ship, energy)))
            {
                if (inSeries) ClearDrain(ship);
                else FailJump(ship, JumpFailure.NotCharged);
                return false;
            }

            //A single jump's landing was confirmed when it started. A series was only checked as things stood then.
            GetJump(ship, energy, out double moveX, out double moveY, out double jumpTime);
            if (inSeries && !IsLandingClear(ship, moveX, moveY, out string body))
            {
                FailJump(ship, JumpFailure.LandingBlocked);
                FzAdditions.Logger.LogInfo($"{ship.strRegID} series ended, jump would have landed at {body}");
                return false;
            }

            ApplyJump(ship, energy, moveX, moveY, jumpTime);
            if (inSeries) ContinueSeries(ship, energy);
            return true;
        }

        private class SeriesRunner : MonoBehaviour
        {
            //Scanning the ship every frame of a recharge that can take days adds up, a burn steers every frame
            private void Update()
            {
                if (!GUIFFWD.Active) skippedShip = null;
                if (Time.unscaledTime < nextSeriesCheck && !JumpCorrection.IsBurning(CrewSim.shipCurrentLoaded)) return;
                nextSeriesCheck = Time.unscaledTime + GetCheckInterval(AdvanceSeries());
            }

            //Follows the charge effect, which moves on every frame
            private void LateUpdate()
            {
                DrainCharge();
            }
        }

        private static void FinishSeries(Ship ship)
        {
            EndSeries(ship);
            ship.ShipCO.SetCondAmount(Constants.SeriesFinishedCond, 1.0);
        }

        //True once for a ship whose series finished, not one cancelled, so its nav console can go back to single jumps.
        //Held until asked for, the console may be closed when the last jump lands.
        public static bool TakeSeriesFinished(Ship ship)
        {
            if (ship?.ShipCO == null || ship.ShipCO.GetCondAmount(Constants.SeriesFinishedCond) <= 0.0) return false;
            ship.ShipCO.ZeroCondAmount(Constants.SeriesFinishedCond);
            return true;
        }

        //Ends the jump and any series it is part of, and leaves the reason for the nav console
        private static void FailJump(Ship ship, JumpFailure failure)
        {
            ship?.ShipCO?.SetCondAmount(Constants.JumpFailureCond, (int)failure);
            Cancel(ship);
        }

        //Once for each failure, so its nav console can show it. Held until asked for, the console may be closed
        //when a jump fails at the end of its charge.
        public static JumpFailure TakeFailure(Ship ship)
        {
            if (ship?.ShipCO == null) return JumpFailure.None;

            int failure = (int)ship.ShipCO.GetCondAmount(Constants.JumpFailureCond);
            if (failure != 0) ship.ShipCO.ZeroCondAmount(Constants.JumpFailureCond);
            return (JumpFailure)failure;
        }

        //Everything a jump needs but the charge itself
        private static JumpFailure GetReadyFailure(Ship ship, double energy)
        {
            ShipSitu situ = ship?.objSS;
            if (situ == null) return JumpFailure.NoShip;
            if (VelocityMatch.IsMatching(ship)) return JumpFailure.Matching;
            if (IsThrusting(situ)) return JumpFailure.Thrusting;
            if (IsHeldInPlace(ship)) return JumpFailure.HeldInPlace;

            //Any emitter holding no field at all leaves a hole in the contour
            if (FieldContour.CalculateSegments(ship).Any(segment => segment.Strength <= 0f)) return JumpFailure.FieldStrength;

            return energy >= FieldContour.GetTotalFieldEnergy(ship) ? JumpFailure.None : JumpFailure.SettingTooLow;
        }

        private static bool HasCharge(List<CondOwner> cells, double energy)
        {
            return energy <= JumpCell.GetTotalCharge(cells) + JumpCell.GetFullMargin(cells);
        }

        //Gravity doesn't count, a ship is always falling towards something
        private static bool IsThrusting(ShipSitu situ)
        {
            return situ.vAccIn != Vector2.zero || situ.vAccRCS != Vector2.zero;
        }

        //A station can't leave its body, so neither can anything docked to it
        private static bool IsHeldInPlace(Ship ship)
        {
            if (ship.objSS.bBOLocked || ship.objSS.bIsBO || ship.IsStation(bIgnoreDocks: true)) return true;
            return ship.GetAllDockedShips()?.Any(docked => docked.objSS.bBOLocked || docked.objSS.bIsBO || docked.IsStation(bIgnoreDocks: true)) ?? false;
        }

        //Rolls the uncertainty, so this is the one jump that happens
        private static void GetJump(Ship ship, double energy, out double moveX, out double moveY, out double jumpTime)
        {
            double excess = energy - FieldContour.GetTotalFieldEnergy(ship);
            double uncertainty = JumpDrive.GetUncertainty(ship) / 100.0;
            jumpTime = JumpDrive.GetJumpTime(excess, JumpDrive.GetAverageStrength(ship));
            jumpTime *= 1.0 + UnityEngine.Random.Range((float)-uncertainty, (float)uncertainty);

            GetDirection(ship, out double directionX, out double directionY, out double speed);
            double distance = JumpDrive.GetDistance(speed, jumpTime);
            moveX = directionX * distance;
            moveY = directionY * distance;
        }

        //Takes whatever of the jump's energy its charge hasn't already drained
        private static void ApplyJump(Ship ship, double energy, double moveX, double moveY, double jumpTime)
        {
            SpendEnergy(ship, GetOwed(ship, energy));
            ClearDrain(ship);
            FieldContour.RemoveOutside(ship, out _, out _, out _);

            //What was removed or undocked stays behind, the group is read after that
            List<Ship> group = FieldContour.GetDockGroup(ship);
            foreach (Ship member in group)
            {
                member.UnlockFromOrbit();
                member.objSS.ResetNavData();
                member.objSS.vPosx += moveX;
                member.objSS.vPosy += moveY;
                member.objSS.aPathRecent?.Clear();
            }

            ship.objSS._dockGroup?.UpdateAnchor();

            foreach (Ship member in group) SettleInNewPosition(member.objSS);
            if (ship.objSS._dockGroup?.Anchor != null) SettleInNewPosition(ship.objSS._dockGroup.Anchor);

            FzAdditions.Logger.LogInfo($"{ship.strRegID} jumped {MathUtils.GetDistUnits(Math.Sqrt(moveX * moveX + moveY * moveY))} in {jumpTime:0.0}s of jump time");
        }

        //Unit vector along the velocity the plot was drawn from. Only a stationary ship has its nose to go by.
        private static void GetDirection(Ship ship, out double x, out double y, out double speed)
        {
            JumpDrive.GetVelocity(ship, out double velocityX, out double velocityY);
            speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);

            if (speed > 0.0)
            {
                x = velocityX / speed;
                y = velocityY / speed;
                return;
            }

            var heading = ship.objSS.GetDirectionVector(invert: false).normalized;
            x = heading.X;
            y = heading.Y;
        }

        private static double SpendEnergy(Ship ship, double energy)
        {
            return SpendEnergy(JumpCell.GetCells(ship), energy);
        }

        //Each cell gives up the same share of its charge. Returns the kWh taken, short of energy if the cells are.
        private static double SpendEnergy(List<CondOwner> cells, double energy)
        {
            double available = JumpCell.GetTotalCharge(cells);
            if (available <= 0.0 || energy <= 0.0) return 0.0;

            double share = Math.Min(energy / available, 1.0);
            foreach (CondOwner cell in cells)
            {
                cell.AddCondAmount(Constants.CellChargeCond, -JumpCell.GetCharge(cell) * share);
            }
            return available * share;
        }

        //The game keeps the body a ship falls towards and only looks it up again if these are empty, so a ship
        //jumping out of a sphere of influence would go on falling towards the body it left. Read them here
        //rather than clearing them, so the point of reference is right before the next update.
        private static void SettleInNewPosition(ShipSitu situ)
        {
            Vector2 gravity = Vector2.zero;
            BodyOrbit closest = null;
            BodyOrbit greatest = CrewSim.system.GetGreatestGravBO(situ, StarSystem.fEpoch, ref gravity, ref closest);

            situ.BOGreatestGrav = greatest;
            situ.BOClosest = closest;
            situ.boReference = null;
            if (greatest != null) situ.strBOPORShip = greatest.strName;
        }
    }
}
