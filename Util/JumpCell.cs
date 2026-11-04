using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    public static class JumpCell
    {
        private const double RechargeShare = 0.001; //Of the missing charge each second, the same curve as a ship battery
        private const int SolveSteps = 40;
        private const double FullMargin = 0.05; //In kWh, the charge only approaches its capacity and this reads as full on the display

        private static readonly AccessTools.FieldRef<Powered, double> LastUpdate = AccessTools.FieldRefAccess<Powered, double>("fUpdateLast");

        private static readonly Dictionary<string, double> currentRates = new Dictionary<string, double>();
        private static readonly HashSet<string> tapCharging = new HashSet<string>();

        //Read from each cell's last recharge, which in a save loading hasn't happened yet
        public static void Reset()
        {
            currentRates.Clear();
            tapCharging.Clear();
        }

        public static bool IsCell(CondOwner co)
        {
            return co != null && co.HasCond(Constants.CellCond);
        }

        //Damage shrinks what a cell can hold, the same as a battery
        public static double GetCapacity(CondOwner co)
        {
            return co.GetCondAmount(Constants.CellChargeMaxCond) * co.GetDamageState();
        }

        //In kW, the most a cell can take from the grid
        public static double GetChargeRate(CondOwner co)
        {
            double rate = co.GetCondAmount(Constants.CellChargeRateCond);
            return rate > 0.0 ? rate : Constants.DefaultCellChargeRate;
        }

        //A cell switched off, without power or broken takes no part in the jump drive
        public static bool IsOn(CondOwner co)
        {
            return co.HasCond("IsPowered");
        }

        //The installed cells that are on, this is the one to use unless the cells that are off matter too
        public static List<CondOwner> GetCells(Ship ship)
        {
            return GetAllCells(ship).Where(IsOn).ToList();
        }

        //Every installed cell on the ship, damaged ones included
        public static List<CondOwner> GetAllCells(Ship ship)
        {
            if (ship == null) return new List<CondOwner>();
            return ship.GetCOs(DataHandler.GetCondTrigger(Constants.CellInstalledTrigger), bSubObjects: false, bAllowDocked: false, bAllowLocked: false);
        }

        public static double GetCharge(CondOwner co)
        {
            return co.GetCondAmount(Constants.CellChargeCond);
        }

        public static double GetTotalCharge(IEnumerable<CondOwner> cells)
        {
            return cells.Sum(GetCharge);
        }

        public static double GetMissingCharge(CondOwner co)
        {
            return Math.Max(GetCapacity(co) - GetCharge(co), 0.0);
        }

        //Close enough to its capacity to read as full
        public static bool IsFull(CondOwner co)
        {
            return GetMissingCharge(co) <= FullMargin;
        }

        //Whether a cell is due a share of a reactor tap's rate
        public static bool CanTakeCharge(CondOwner co)
        {
            return IsCell(co) && IsOn(co) && GetMissingCharge(co) > 0.0;
        }

        public static bool IsTapCharging(CondOwner co)
        {
            return tapCharging.Contains(co.strID);
        }

        public static bool IsGridChargeEnabled(CondOwner co)
        {
            return co.mapGUIPropMaps.TryGetValue(Constants.CellGUIPropMap, out Dictionary<string, string> props)
                && props.TryGetValue(Constants.CellGridChargeProp, out string value)
                && bool.TryParse(value, out bool enabled)
                && enabled;
        }

        //What the cell actually took on its last update, in kW. Not saved, it only reads as 0 until the cell next updates.
        public static double GetCurrentRate(CondOwner co)
        {
            return currentRates.TryGetValue(co.strID, out double rate) ? rate : 0.0;
        }

        //False until the cell's first power update since the game loaded. IsPowered isn't saved, so until then a cell
        //reads as off whether or not it is.
        public static bool HasRun(CondOwner co)
        {
            return currentRates.ContainsKey(co.strID);
        }

        //Game seconds until the cell reads as full at its current rate, negative when it is not charging
        public static double GetChargeTime(CondOwner co)
        {
            double rate = GetCurrentRate(co) / Constants.SecondsPerHour;
            if (rate <= 0.0) return -1.0;

            return GetFillTime(GetMissingCharge(co), rate, tapCharging.Contains(co.strID));
        }

        //Rate is in kWh per game second
        private static double GetFillTime(double missing, double rate, bool tap)
        {
            if (missing <= FullMargin) return 0.0;

            //A reactor tap holds its rate all the way to full
            if (tap) return missing / rate;

            //Held at the current rate until the curve falls below it, then the curve decays towards full
            double decay = -Math.Log(1.0 - RechargeShare);
            double knee = Math.Min(missing, rate / decay);
            double held = (missing - knee) / rate;
            return held + Math.Max(0.0, Math.Log(knee / FullMargin) / decay);
        }

        //In kWh per game second, the average rate these cells would fill from empty. Worked out from the taps and
        //cells rather than what they are taking now, since cells that are already full take nothing. 0 if nothing would fill them.
        public static double GetFillRate(Ship ship, List<CondOwner> cells, List<CondOwner> activeTaps)
        {
            if (ship == null || cells.Count == 0) return 0.0;

            if (ReactorFuel.IsFusing(ship.Reactor))
            {
                double tapRate = activeTaps.Sum(JumpTap.GetRate);
                if (tapRate > 0.0) return tapRate / Constants.SecondsPerHour;
            }

            List<CondOwner> gridCells = cells.Where(IsGridChargeEnabled).ToList();
            double slowest = gridCells.Count > 0 ? gridCells.Max(co => GetFillTime(GetCapacity(co), GetChargeRate(co) / Constants.SecondsPerHour, false)) : 0.0;
            return slowest > 0.0 ? gridCells.Sum(GetCapacity) / slowest : 0.0;
        }

        //kWh these cells can fall short of a full charge by and still count as holding it. The grid curve only ever
        //approaches its capacity, so a jump at full power would otherwise never be ready.
        public static double GetFullMargin(IEnumerable<CondOwner> cells)
        {
            return cells.Count() * FullMargin;
        }

        //Game seconds until these cells together hold the charge at their current rates, negative if they never will
        public static double GetTimeUntilCharged(List<CondOwner> cells, double charge)
        {
            if (GetTotalCharge(cells) >= charge) return 0.0;

            double high = cells.Count > 0 ? cells.Max(GetChargeTime) : -1.0;
            if (high <= 0.0 || cells.Sum(co => GetChargeAfter(co, high)) < charge) return -1.0;

            double low = 0.0;
            for (int i = 0; i < SolveSteps; i++)
            {
                double middle = (low + high) / 2.0;
                if (cells.Sum(co => GetChargeAfter(co, middle)) < charge) low = middle; else high = middle;
            }
            return high;
        }

        //The same model as GetChargeTime, a tap holds its rate and the grid decays towards full past the knee
        private static double GetChargeAfter(CondOwner co, double seconds)
        {
            double charge = GetCharge(co);
            double missing = GetMissingCharge(co);
            double rate = GetCurrentRate(co) / Constants.SecondsPerHour;
            if (rate <= 0.0 || missing <= 0.0) return charge;
            if (tapCharging.Contains(co.strID)) return charge + Math.Min(missing, rate * seconds);

            double decay = -Math.Log(1.0 - RechargeShare);
            double knee = Math.Min(missing, rate / decay);
            double hold = (missing - knee) / rate;
            if (seconds <= hold) return charge + rate * seconds;
            return charge + missing - knee * Math.Exp(-decay * (seconds - hold));
        }

        //Game seconds since the cell last ran
        public static double GetTimeSinceUpdate(Powered power)
        {
            return power.CO.Clock.Epoch - LastUpdate(power);
        }

        public static void SetTimeSinceUpdate(Powered power, double seconds)
        {
            LastUpdate(power) = power.CO.Clock.Epoch - seconds;
        }

        //Seconds is the game time since the last recharge
        public static void Recharge(Powered power, double seconds)
        {
            double amount = TakeCharge(power, seconds);
            currentRates[power.CO.strID] = seconds > 0.0 ? amount / seconds * Constants.SecondsPerHour : 0.0;
        }

        //Takes what it can from the cell's conduit and returns how much
        private static double TakeCharge(Powered power, double seconds)
        {
            CondOwner co = power.CO;
            tapCharging.Remove(co.strID);
            if (seconds <= 0.0 || !IsOn(co)) return 0.0;

            double missing = GetMissingCharge(co);
            if (missing <= 0.0) return 0.0;

            //A running tap replaces the grid, so the batteries are left alone and the grid button doesn't apply
            double tapped = JumpTap.TakeCharge(co, missing, seconds);
            if (tapped > 0.0)
            {
                tapCharging.Add(co.strID);
                co.AddCondAmount(Constants.CellChargeCond, tapped);
                return tapped;
            }

            if (!IsGridChargeEnabled(co)) return 0.0;

            //Exact for any gap, so a long time skip lands where the same time played out would
            double wanted = missing * (1.0 - Math.Pow(1.0 - RechargeShare, seconds));
            wanted = Math.Min(wanted, GetChargeRate(co) * seconds / Constants.SecondsPerHour);

            //UserPowerExt shuts the cell down if it asks for more than the grid has
            double amount = Math.Min(wanted, power.PowerConnected);
            if (amount <= 0.0) return 0.0;

            power.UserPowerExt(amount);
            co.AddCondAmount(Constants.CellChargeCond, amount);
            return amount;
        }
    }
}
