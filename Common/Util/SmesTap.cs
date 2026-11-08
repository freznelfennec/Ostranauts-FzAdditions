using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    public static class SmesTap
    {
        private const double ChargingHold = 5.0; //In game seconds, unit updates spread out when the game slows their clocks

        //The system time each ship's taps last delivered, keyed by reactor strID. Object clocks are slowed
        //by differing amounts while fast forwarding, so they can't be compared with each other.
        private static readonly Dictionary<string, double> lastDelivery = new Dictionary<string, double>();

        //Times from the world being left behind mean nothing to a save loading
        public static void Reset()
        {
            lastDelivery.Clear();
        }

        public static List<CondOwner> GetActiveTaps(Ship ship)
        {
            if (ship == null) return new List<CondOwner>();
            return ship.GetCOs(DataHandler.GetCondTrigger(SmesConstants.TapActiveTrigger), bSubObjects: false, bAllowDocked: false, bAllowLocked: false);
        }

        //In kW, the most a tap can deliver across all of the ship's units
        public static double GetMaxRate(CondOwner tap)
        {
            double rate = tap.GetCondAmount(SmesConstants.TapChargeRateCond);
            return rate > 0.0 ? rate : SmesConstants.DefaultTapChargeRate;
        }

        //In kW, where the panel's slider is set. A tap nobody has adjusted runs at its maximum.
        public static double GetRateSetting(CondOwner tap)
        {
            double max = GetMaxRate(tap);
            string value = GetProp(tap, SmesConstants.TapRateProp);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) ? Math.Min(Math.Max(rate, 0.0), max) : max;
        }

        //The panel's rocker, on until it is switched off
        public static bool IsChargeEnabled(CondOwner tap)
        {
            return !bool.TryParse(GetProp(tap, SmesConstants.TapChargeProp), out bool enabled) || enabled;
        }

        //In kW, what the tap's controls currently allow. Nothing without a plasma converter with life left.
        public static double GetRate(CondOwner tap)
        {
            return IsChargeEnabled(tap) && PlasmaConverter.IsWorking(tap) ? GetRateSetting(tap) : 0.0;
        }

        //In burn units per kWh delivered, see ReactorFuel. Its plasma converter's efficiency gets that much more charge
        //from the same burn.
        public static double GetBurnRate(CondOwner tap)
        {
            double burn = tap.GetCondAmount(SmesConstants.TapBurnRateCond);
            if (burn <= 0.0) burn = SmesConstants.DefaultTapBurnRate;
            return burn / (1.0 + PlasmaConverter.Efficiency(tap));
        }

        //The rate is shared by the ship's taps, so any tap that is on counts as charging while one delivers
        public static bool IsCharging(CondOwner tap)
        {
            CondOwner reactor = tap.ship?.Reactor;
            if (reactor == null || !tap.HasCond("IsPowered") || GetRate(tap) <= 0.0) return false;
            return lastDelivery.TryGetValue(reactor.strID, out double delivered) && StarSystem.fEpoch - delivered < ChargingHold;
        }

        //Takes up to wanted kWh from the taps on the unit's ship, and bills the reactor for it. Returns how much.
        //Units share the ship's rate equally, so a unit's update timing doesn't change what it gets. A unit that
        //fills mid update leaves the rest of its share unused until it drops out of the count.
        public static double TakeCharge(CondOwner unit, double wanted, double seconds)
        {
            CondOwner reactor = unit.ship?.Reactor;
            if (wanted <= 0.0 || seconds <= 0.0 || !ReactorFuel.IsFusing(reactor)) return 0.0;

            //One scan finds both, active taps and installed units
            List<CondOwner> found = unit.ship.GetCOs(DataHandler.GetCondTrigger(SmesConstants.TapOrUnitTrigger), bSubObjects: false, bAllowDocked: false, bAllowLocked: false);
            List<CondOwner> taps = found.Where(co => co.HasCond(SmesConstants.TapCond)).ToList();
            double rate = taps.Sum(GetRate);
            if (rate <= 0.0) return 0.0;

            double share = rate / Math.Max(found.Count(Smes.CanTakeCharge), 1);
            double amount = Math.Min(wanted, share * seconds / SmesConstants.SecondsPerHour);

            lastDelivery[reactor.strID] = StarSystem.fEpoch;
            ReactorFuel.AddBurn(reactor, amount * GetShipBurnRate(taps));

            //Each tap delivers its share of this at its own rate, so the hours it spent charging are the energy over
            //the ship's rate, the same for every tap. Summed over the units, that's the time the taps ran.
            double hours = amount / rate;
            foreach (CondOwner tap in taps.Where(t => GetRate(t) > 0.0).ToList()) PlasmaConverter.Wear(tap, hours);
            return amount;
        }

        //In burn units per kWh delivered, across taps sharing a ship's rate. 0 if they deliver nothing.
        public static double GetShipBurnRate(List<CondOwner> taps)
        {
            double rate = taps.Sum(GetRate);
            return rate > 0.0 ? taps.Sum(tap => GetRate(tap) * GetBurnRate(tap)) / rate : 0.0;
        }

        private static string GetProp(CondOwner tap, string key)
        {
            return tap.mapGUIPropMaps.TryGetValue(SmesConstants.TapGUIPropMap, out Dictionary<string, string> props) && props.TryGetValue(key, out string value) ? value : null;
        }
    }
}
