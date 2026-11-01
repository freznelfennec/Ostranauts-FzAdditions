using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    public static class JumpTap
    {
        private const double SecondsPerHour = 3600.0;
        private const double ChargingHold = 5.0; //In game seconds, cell updates spread out when the game slows their clocks

        //The system time each ship's taps last delivered, keyed by reactor strID. Object clocks are slowed
        //by differing amounts while fast forwarding, so they can't be compared with each other.
        private static readonly Dictionary<string, double> lastDelivery = new Dictionary<string, double>();

        //In kW, the most a tap can deliver across all of the ship's cells
        public static double GetMaxRate(CondOwner tap)
        {
            double rate = tap.GetCondAmount(Constants.TapChargeRateCond);
            return rate > 0.0 ? rate : Constants.DefaultTapChargeRate;
        }

        //In kW, where the panel's slider is set. A tap nobody has adjusted runs at its maximum.
        public static double GetRateSetting(CondOwner tap)
        {
            double max = GetMaxRate(tap);
            string value = GetProp(tap, Constants.TapRateProp);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) ? Math.Min(Math.Max(rate, 0.0), max) : max;
        }

        //The panel's rocker, on until it is switched off
        public static bool IsChargeEnabled(CondOwner tap)
        {
            return !bool.TryParse(GetProp(tap, Constants.TapChargeProp), out bool enabled) || enabled;
        }

        //In kW, what the tap's controls currently allow
        public static double GetRate(CondOwner tap)
        {
            return IsChargeEnabled(tap) ? GetRateSetting(tap) : 0.0;
        }

        //In burn units per kWh delivered, see ReactorFuel
        public static double GetBurnRate(CondOwner tap)
        {
            double burn = tap.GetCondAmount(Constants.TapBurnRateCond);
            return burn > 0.0 ? burn : Constants.DefaultTapBurnRate;
        }

        //The rate is shared by the ship's taps, so any tap that is on counts as charging while one delivers
        public static bool IsCharging(CondOwner tap)
        {
            CondOwner reactor = tap.ship?.Reactor;
            if (reactor == null || !tap.HasCond("IsPowered") || GetRate(tap) <= 0.0) return false;
            return lastDelivery.TryGetValue(reactor.strID, out double delivered) && StarSystem.fEpoch - delivered < ChargingHold;
        }

        //Takes up to wanted kWh from the taps on the cell's ship, and bills the reactor for it. Returns how much.
        //Cells share the ship's rate equally, so a cell's update timing doesn't change what it gets. A cell that
        //fills mid update leaves the rest of its share unused until it drops out of the count.
        public static double TakeCharge(CondOwner cell, double wanted, double seconds)
        {
            CondOwner reactor = cell.ship?.Reactor;
            if (wanted <= 0.0 || seconds <= 0.0 || !ReactorFuel.IsFusing(reactor)) return 0.0;

            //One scan finds both, active taps and installed cells
            List<CondOwner> found = cell.ship.GetCOs(DataHandler.GetCondTrigger(Constants.TapOrCellTrigger), bSubObjects: false, bAllowDocked: false, bAllowLocked: false);
            List<CondOwner> taps = found.Where(co => co.HasCond(Constants.TapCond)).ToList();
            double rate = taps.Sum(GetRate);
            if (rate <= 0.0) return 0.0;

            double share = rate / Math.Max(found.Count(JumpCell.CanTakeCharge), 1);
            double amount = Math.Min(wanted, share * seconds / SecondsPerHour);

            lastDelivery[reactor.strID] = StarSystem.fEpoch;
            ReactorFuel.AddBurn(reactor, amount * taps.Sum(tap => GetRate(tap) * GetBurnRate(tap)) / rate);
            return amount;
        }

        private static string GetProp(CondOwner tap, string key)
        {
            return tap.mapGUIPropMaps.TryGetValue(Constants.TapGUIPropMap, out Dictionary<string, string> props) && props.TryGetValue(key, out string value) ? value : null;
        }
    }
}
