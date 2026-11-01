using Freznel.FzAdditions.UI;
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
    //A series is a condition on the ship's own CO, which saves with it. A charge only lasts as long as its effect.
    public static class JumpSequence
    {
        private const double CatchUpStep = 300.0; //Game seconds of recharge at a time in a time skip, a jump lands this late at worst
        private const string SkipLog = "[SFF series]";

        public enum SeriesState { Idle, Jumping, Thrusting, Recharging }

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

        //Energy is the total to spend in kWh. False if the ship can't jump, nothing has changed then.
        public static bool Start(Ship ship, double energy)
        {
            return CanJump(ship, energy) && FzJumpEffect.Play(ship, () => Complete(ship, energy));
        }

        public static void Cancel(Ship ship)
        {
            ship?.ShipCO?.ZeroCondAmount(Constants.SeriesEnergyCond);
            if (IsActive(ship)) FzJumpEffect.Interrupt();
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
        public static SeriesState GetSeriesState(Ship ship, out double wait)
        {
            wait = 0.0;
            if (!IsSeriesRunning(ship)) return SeriesState.Idle;
            if (FzJumpEffect.IsPlaying(ship)) return SeriesState.Jumping;
            if (IsThrusting(ship.objSS)) return SeriesState.Thrusting;

            List<CondOwner> cells = JumpCell.GetCells(ship);
            double energy = Math.Min(GetSeriesEnergy(ship), cells.Sum(JumpCell.GetCapacity));
            wait = JumpCell.GetTimeUntilCharged(cells, energy - JumpCell.GetFullMargin(cells));
            return SeriesState.Recharging;
        }

        //Energy is the total for the whole series in kWh. False if it can't begin, it doesn't need to be charged yet.
        public static bool StartSeries(Ship ship, double energy)
        {
            ShipSitu situ = ship?.objSS;
            if (situ == null || IsThrusting(situ) || IsBlocked(ship)) return false;

            double need = FieldContour.GetTotalFieldEnergy(ship);
            double capacity = JumpCell.GetCells(ship).Sum(JumpCell.GetCapacity);
            if (capacity <= need || energy < need) return false;

            ship.ShipCO.SetCondAmount(Constants.SeriesEnergyCond, energy);
            return true;
        }

        //Starts the next jump once the last has finished and the cells have recharged. A burn only delays it, and
        //so does having too few cells, which is also how a ship looks while it is still loading. A ship's series
        //waits while the ship isn't the one loaded.
        private static void AdvanceSeries()
        {
            Ship ship = CrewSim.shipCurrentLoaded;
            if (GUIFFWD.Active || CrewSim.bShipEdit || !IsSeriesRunning(ship) || FzOverlayCamera.Get() == null) return;
            if (FzJumpEffect.Playing || IsThrusting(ship.objSS)) return;

            double need = FieldContour.GetTotalFieldEnergy(ship);
            double seriesEnergy = GetSeriesEnergy(ship);
            if (seriesEnergy < need)
            {
                FinishSeries(ship);
                return;
            }

            List<CondOwner> cells = JumpCell.GetCells(ship);
            double capacity = cells.Sum(JumpCell.GetCapacity);
            if (capacity <= need) return;

            double energy = Math.Min(seriesEnergy, capacity);
            double available = cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond)) + JumpCell.GetFullMargin(cells);
            if (available < energy) return;

            if (!Start(ship, energy)) Cancel(ship);
        }

        //Plays out a running series over a super fast forward, which applies its seconds to the ship all at once and
        //so leaves no frames for AdvanceSeries. The ship has already drifted the whole time, so jumps are added on top
        //of that without their charge. Cells recharge in steps because a single skip would only ever fill them once.
        //Steps stop if a landing isn't clear, and whatever is left of the series then carries on or ends as normal.
        public static void CatchUpSeries(Ship ship, double seconds)
        {
            if (!IsSeriesRunning(ship) || IsThrusting(ship.objSS)) return;

            FzJumpEffect.Stop();

            CondOwner reactor = ship.Reactor;
            ReactorFuel.BurnReceipt totalBurn = new ReactorFuel.BurnReceipt();
            ReactorFuel.BurnReceipt phaseBurn = new ReactorFuel.BurnReceipt();
            ReactorFuel.GetHeld(reactor, out double startA, out double startB);
            double left = seconds;
            double phaseSeconds = 0.0;
            double phaseCharge = 0.0;
            int jumps = 0;
            bool outOfFuel = false;

            FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} skip of {seconds:0}s, {GetSeriesEnergy(ship):0.0} kWh left in series, {FormatHeld(startA, startB)}");

            while (left > 0.0 && IsSeriesRunning(ship))
            {
                double seriesEnergy = GetSeriesEnergy(ship);
                double need = FieldContour.GetTotalFieldEnergy(ship);
                List<CondOwner> cells = JumpCell.GetCells(ship);
                double capacity = cells.Sum(JumpCell.GetCapacity);
                if (seriesEnergy < need)
                {
                    FinishSeries(ship);
                    break;
                }
                if (capacity <= need) break;

                double energy = Math.Min(seriesEnergy, capacity);
                if (HasCharge(ship, energy))
                {
                    LogRecharge(ship, jumps + 1, phaseSeconds, phaseCharge, phaseBurn);
                    totalBurn.Add(phaseBurn);
                    phaseBurn = new ReactorFuel.BurnReceipt();
                    phaseSeconds = 0.0;
                    phaseCharge = 0.0;

                    if (!IsReady(ship, energy))
                    {
                        Cancel(ship);
                        FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended, not able to jump");
                        break;
                    }

                    GetJump(ship, energy, out double moveX, out double moveY, out double jumpTime);
                    if (!IsLandingClear(ship, moveX, moveY, out string body))
                    {
                        Cancel(ship);
                        FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} series ended, jump would have landed at {body}");
                        break;
                    }

                    ApplyJump(ship, energy, moveX, moveY, jumpTime);
                    jumps++;
                    if (IsSeriesRunning(ship)) SpendSeriesEnergy(ship, energy);
                    continue;
                }

                double step = Math.Min(CatchUpStep, left);
                double held = cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond));
                RechargeCells(cells, step);
                double gained = cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond)) - held;
                left -= step;
                phaseSeconds += step;
                phaseCharge += gained;

                ReactorFuel.BurnReceipt receipt = ReactorFuel.PayPending(reactor);
                phaseBurn.Add(receipt);
                if (receipt.Short)
                {
                    outOfFuel = true;
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} tanks ran out with {left:0}s left");
                    break;
                }

                //Nothing is feeding the cells, the rest of the skip won't change that
                if (gained <= 0.0) break;
            }

            //What a finished or stopped series leaves of the skip still charges the cells, the same as any other skip
            if (left > 0.0 && !outOfFuel)
            {
                List<CondOwner> cells = JumpCell.GetCells(ship);
                double held = cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond));
                RechargeCells(cells, left);
                phaseCharge += cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond)) - held;
                phaseSeconds += left;
                phaseBurn.Add(ReactorFuel.PayPending(reactor));
            }
            LogRecharge(ship, jumps + 1, phaseSeconds, phaseCharge, phaseBurn);
            totalBurn.Add(phaseBurn);

            //The skip is spent, without this each cell would recharge for all of it again on its next update
            foreach (CondOwner cell in JumpCell.GetAllCells(ship))
            {
                if (cell.Pwr != null) JumpCell.SetTimeSinceUpdate(cell.Pwr, 0.0);
            }

            ReactorFuel.GetHeld(reactor, out double endA, out double endB);
            FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} done, {jumps} jumps, {Math.Max(GetSeriesEnergy(ship), 0.0):0.0} kWh left in series, burn {totalBurn.Burn:G6}, "
                + $"{FusionIC.aReactantNames[0]} wanted {totalBurn.WantedA:G6} taken {totalBurn.TakenA:G6} measured {startA - endA:G6}, "
                + $"{FusionIC.aReactantNames[1]} wanted {totalBurn.WantedB:G6} taken {totalBurn.TakenB:G6} measured {startB - endB:G6}, {FormatHeld(endA, endB)}");
        }

        //Seconds of recharge for each cell, as if that long had passed since each last ran
        private static void RechargeCells(List<CondOwner> cells, double seconds)
        {
            foreach (CondOwner cell in cells)
            {
                if (cell.Pwr == null) continue;
                JumpCell.SetTimeSinceUpdate(cell.Pwr, seconds);
                JumpCell.Recharge(cell.Pwr, seconds);
            }
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

        //Bodies move on while a skip plays out, so nothing checked the landing when the series was plotted.
        //Body names the first one the landing is too close to.
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
            //A series can wait for charge, anything else stops it
            if (!IsReady(ship, energy))
            {
                Cancel(ship);
                return false;
            }
            if (!HasCharge(ship, energy)) return false;

            Jump(ship, energy);
            if (IsSeriesRunning(ship))
            {
                SpendSeriesEnergy(ship, energy);
                List<CondOwner> cells = JumpCell.GetCells(ship);
                FzAdditions.Logger.LogInfo($"{ship.strRegID} series spent {energy:0.0} kWh, {Math.Max(GetSeriesEnergy(ship), 0.0):0.0} kWh left, {cells.Count} cells, capacity {cells.Sum(JumpCell.GetCapacity):0.0} kWh, field need {FieldContour.GetTotalFieldEnergy(ship):0.0} kWh");
            }
            return true;
        }

        private class SeriesRunner : MonoBehaviour
        {
            private void Update()
            {
                AdvanceSeries();
            }
        }

        //Spends a jump from the series, which is finished once what is left couldn't raise another field
        private static void SpendSeriesEnergy(Ship ship, double energy)
        {
            double left = ship.ShipCO.GetCondAmount(Constants.SeriesEnergyCond) - energy;
            ship.ShipCO.AddCondAmount(Constants.SeriesEnergyCond, -energy);
            if (left <= 0.0 || left < FieldContour.GetTotalFieldEnergy(ship)) FinishSeries(ship);
        }

        private static void FinishSeries(Ship ship)
        {
            ship.ShipCO.ZeroCondAmount(Constants.SeriesEnergyCond);
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

        //True once for a ship whose jump failed on field strength, so its nav console can show the field.
        //Held until asked for, the console may be closed when a jump fails at the end of its charge.
        public static bool TakeFieldFailure(Ship ship)
        {
            if (!HasFieldFailure(ship)) return false;
            ship.ShipCO.ZeroCondAmount(Constants.FieldFailedCond);
            return true;
        }

        private static bool HasFieldFailure(Ship ship)
        {
            return ship?.ShipCO != null && ship.ShipCO.GetCondAmount(Constants.FieldFailedCond) > 0.0;
        }

        private static bool CanJump(Ship ship, double energy)
        {
            return IsReady(ship, energy) && HasCharge(ship, energy);
        }

        //Everything a jump needs but the charge itself
        private static bool IsReady(Ship ship, double energy)
        {
            ShipSitu situ = ship?.objSS;
            if (situ == null || IsThrusting(situ) || IsBlocked(ship)) return false;
            return energy >= FieldContour.GetTotalFieldEnergy(ship);
        }

        private static bool HasCharge(Ship ship, double energy)
        {
            List<CondOwner> cells = JumpCell.GetCells(ship);
            double available = cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond));
            return energy <= available + JumpCell.GetFullMargin(cells);
        }

        private static bool IsBlocked(Ship ship)
        {
            if (IsHeldInPlace(ship)) return true;

            //Any emitter holding no field at all leaves a hole in the contour
            if (!FieldContour.CalculateSegments(ship).Any(segment => segment.Strength <= 0f)) return false;
            ship.ShipCO.SetCondAmount(Constants.FieldFailedCond, 1.0);
            return true;
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

        private static void Jump(Ship ship, double energy)
        {
            GetJump(ship, energy, out double moveX, out double moveY, out double jumpTime);
            ApplyJump(ship, energy, moveX, moveY, jumpTime);
        }

        //Rolls the uncertainty, so this is the one jump that happens
        private static void GetJump(Ship ship, double energy, out double moveX, out double moveY, out double jumpTime)
        {
            double excess = energy - FieldContour.GetTotalFieldEnergy(ship);
            double uncertainty = JumpDrive.GetUncertainty(ship) / 100.0;
            jumpTime = JumpDrive.GetJumpTime(excess, JumpDrive.GetAverageStrength(ship));
            jumpTime *= 1.0 + UnityEngine.Random.Range((float)-uncertainty, (float)uncertainty);

            GetJumpVector(ship, jumpTime, out moveX, out moveY);
        }

        private static void ApplyJump(Ship ship, double energy, double moveX, double moveY, double jumpTime)
        {
            SpendEnergy(ship, energy);
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

        //In AU, along the velocity the plot was drawn from. Only a stationary ship has its nose to go by.
        private static void GetJumpVector(Ship ship, double jumpTime, out double x, out double y)
        {
            JumpDrive.GetVelocity(ship, out double velocityX, out double velocityY);
            double speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
            double distance = JumpDrive.GetDistance(speed, jumpTime);

            if (speed > 0.0)
            {
                x = velocityX / speed * distance;
                y = velocityY / speed * distance;
                return;
            }

            var heading = ship.objSS.GetDirectionVector(invert: false).normalized;
            x = heading.X * distance;
            y = heading.Y * distance;
        }

        //Each cell gives up the same share of its charge
        private static void SpendEnergy(Ship ship, double energy)
        {
            List<CondOwner> cells = JumpCell.GetCells(ship);
            double available = cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond));
            if (available <= 0.0) return;

            foreach (CondOwner cell in cells)
            {
                cell.AddCondAmount(Constants.CellChargeCond, -cell.GetCondAmount(Constants.CellChargeCond) * Math.Min(energy / available, 1.0));
            }
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
