using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //The ship's reserves that recycling can top up: O2 and N2 in its installed gas canisters, and D2O and He3 in its
    //reactor fuel canisters. Everything is in kg, as the game's ship status shows fuel. Gas is held as moles, through
    //each canister's GasContainer, and fuel as kg conditions, which is how the station refuel screen fills them.
    public static class Reserves
    {
        public class Reserve
        {
            public string Key;      //Short name shown on readouts
            public string Name;
            public string Color;    //Hex, for its bar
            public string Vessel;   //The condition its canisters have
            public string Gas;      //For gas, its name after StatGasMol, else null
            public string FuelCond; //For fuel, the condition holding its kg, else null
            public double KgPerVolume; //For fuel, the kg a canister holds per unit of StatVolume

            public bool IsGas => Gas != null;
        }

        private const double GasConstant = 8.314; //J/(mol K)
        private const double DefaultGasTemp = 293.0;
        private const double KgShown = 0.05; //Less than this of a reserve in an item isn't worth listing

        public static readonly IReadOnlyList<Reserve> All = new[]
        {
            new Reserve { Key = "O2", Name = "OXYGEN", Color = "#5DBB63", Vessel = "IsVesselO2", Gas = "O2" },
            new Reserve { Key = "N2", Name = "NITROGEN", Color = "#4A84E0", Vessel = "IsVesselN2", Gas = "N2" },
            new Reserve { Key = "D2O", Name = "HEAVY WATER", Color = "#9AD9E8", Vessel = "IsVesselH2", FuelCond = "StatLiqD2O", KgPerVolume = 1107.0 },
            new Reserve { Key = "He3", Name = "HELIUM-3", Color = "#FFC870", Vessel = "IsVesselHe3", FuelCond = "StatSolidHe3", KgPerVolume = 129.11 },
        };

        public static Reserve Get(string key) => All.FirstOrDefault(r => r.Key == key);

        //The ship's installed, undamaged canisters for a reserve, not counting docked ships
        public static List<CondOwner> Canisters(Ship ship, Reserve reserve)
        {
            if (ship == null) return new List<CondOwner>();
            return (ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
                .Where(co => co != null && !co.bDestroyed && co.HasCond(reserve.Vessel) && co.HasCond("IsInstalled") && !co.HasCond("IsDamaged")).ToList();
        }

        public static double Amount(CondOwner canister, Reserve reserve)
        {
            if (!reserve.IsGas) return Math.Max(0.0, canister.GetCondAmount(reserve.FuelCond));
            return GasContainer.GetGasMass(reserve.Gas, GasMols(canister, reserve.Gas));
        }

        //What a canister can hold: fuel by its volume, gas at its rated pressure and temperature
        public static double Capacity(CondOwner canister, Reserve reserve)
        {
            double volume = canister.GetCondAmount("StatVolume");
            if (!reserve.IsGas) return volume * reserve.KgPerVolume;
            double temp = canister.GetCondAmount("StatGasTemp");
            if (temp <= 0.0) temp = DefaultGasTemp;
            double mols = canister.GetCondAmount("StatGasPressureMax") * 1000.0 * volume / (GasConstant * temp); //kPa
            return GasContainer.GetGasMass(reserve.Gas, mols);
        }

        public static double ShipAmount(Ship ship, Reserve reserve) => Canisters(ship, reserve).Sum(c => Amount(c, reserve));
        public static double ShipCapacity(Ship ship, Reserve reserve) => Canisters(ship, reserve).Sum(c => Capacity(c, reserve));

        //Tops up the ship's canisters with that many kg, the fullest first so the rest stay together, and returns how
        //much went in. What doesn't fit is lost.
        public static double Fill(Ship ship, Reserve reserve, double kg)
        {
            double left = kg;
            foreach (CondOwner canister in Canisters(ship, reserve).OrderByDescending(c => Amount(c, reserve)))
            {
                if (left <= 0.0) break;
                double room = Capacity(canister, reserve) - Amount(canister, reserve);
                if (room <= 0.0) continue;
                double add = Math.Min(room, left);
                if (reserve.IsGas)
                {
                    GasContainer gas = canister.GasContainer;
                    double perMol = GasContainer.GetGasMass(reserve.Gas, 1.0);
                    if (gas == null || perMol <= 0.0) continue;
                    gas.AddGasMols(reserve.Gas, add / perMol);
                }
                else
                {
                    canister.AddCondAmount(reserve.FuelCond, add);
                }
                left -= add;
            }
            return kg - left;
        }

        //Takes up to that many kg from the ship's canisters, the emptiest first so stragglers empty, and returns how
        //much came out
        public static double Draw(Ship ship, Reserve reserve, double kg)
        {
            double left = kg;
            foreach (CondOwner canister in Canisters(ship, reserve).OrderBy(c => Amount(c, reserve)))
            {
                if (left <= 0.0) break;
                double take = Math.Min(Amount(canister, reserve), left);
                if (take <= 0.0) continue;
                if (!Change(canister, reserve, -take)) continue;
                left -= take;
            }
            return kg - left;
        }

        //Whether an item is a canister for a reserve, which a fabricator empties when it makes one and can fill
        public static bool IsVessel(CondOwner item, Reserve reserve) => item != null && item.HasCond(reserve.Vessel);

        //Empties an item's canister reserves, the ones it would start with
        public static void Empty(CondOwner item)
        {
            foreach (Reserve reserve in All.Where(r => IsVessel(item, r)))
            {
                double kg = Amount(item, reserve);
                if (kg > 0.0) Change(item, reserve, -kg);
            }
        }

        //Fills an item's canisters from the ship's, as far as both allow, and returns kg moved per reserve
        public static Dictionary<string, double> FillItem(Ship ship, CondOwner item)
        {
            var moved = new Dictionary<string, double>();
            foreach (Reserve reserve in All.Where(r => IsVessel(item, r)))
            {
                double room = Capacity(item, reserve) - Amount(item, reserve);
                if (room <= 0.0) continue;
                double taken = Draw(ship, reserve, room);
                if (taken <= 0.0) continue;
                Change(item, reserve, taken);
                moved[reserve.Key] = taken;
            }
            return moved;
        }

        //What a fresh item of a CO can hold of each reserve it's a canister for, in kg, from its definition, without
        //making one. Used to show what FILL will take before anything is built.
        public static Dictionary<string, double> DefCapacity(string strCO)
        {
            var found = new Dictionary<string, double>();
            JsonCondOwner def = DataHandler.GetCondOwnerDef(strCO);
            if (def?.aStartingConds == null) return found;
            double volume = DefAmount(def, "StatVolume");
            foreach (Reserve reserve in All.Where(r => DefAmount(def, r.Vessel) > 0.0))
            {
                double kg;
                if (!reserve.IsGas) kg = volume * reserve.KgPerVolume;
                else
                {
                    double temp = DefAmount(def, "StatGasTemp");
                    if (temp <= 0.0) temp = DefaultGasTemp;
                    kg = GasContainer.GetGasMass(reserve.Gas, DefAmount(def, "StatGasPressureMax") * 1000.0 * volume / (GasConstant * temp));
                }
                if (kg > 0.0) found[reserve.Key] = kg;
            }
            return found;
        }

        //A starting condition's amount in a CO definition, 0 when it hasn't it
        private static double DefAmount(JsonCondOwner def, string cond)
        {
            string entry = def.aStartingConds.FirstOrDefault(c => c.StartsWith(cond + "="));
            if (entry == null) return 0.0;
            int x = entry.LastIndexOf('x');
            return x > 0 && double.TryParse(entry.Substring(x + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double amount) ? amount : 0.0;
        }

        //Adds kg of a reserve to something, or takes it away when negative
        private static bool Change(CondOwner co, Reserve reserve, double kg)
        {
            if (!reserve.IsGas)
            {
                co.AddCondAmount(reserve.FuelCond, kg);
                if (co.GetCondAmount(reserve.FuelCond) < 0.0) co.ZeroCondAmount(reserve.FuelCond);
                return true;
            }
            double perMol = GasContainer.GetGasMass(reserve.Gas, 1.0);
            if (perMol <= 0.0) return false;
            GasContainer gas = co.GasContainer;
            if (gas != null) gas.AddGasMols(reserve.Gas, kg / perMol);
            else co.AddCondAmount("StatGasMol" + reserve.Gas, kg / perMol);
            return true;
        }

        //The reserves an item holds, in kg, with its whole stack, leaving out traces
        public static Dictionary<string, double> InItem(CondOwner item)
        {
            var found = new Dictionary<string, double>();
            if (item == null) return found;
            int count = Math.Max(1, item.StackCount);
            foreach (Reserve reserve in All)
            {
                double kg = (reserve.IsGas ? GasContainer.GetGasMass(reserve.Gas, GasMols(item, reserve.Gas)) : item.GetCondAmount(reserve.FuelCond)) * count;
                if (kg >= KgShown) found[reserve.Key] = kg;
            }
            return found;
        }

        //The gas condition, plus any change its GasContainer holds back to apply on its next run (mapDGasMols is that
        //change, not the total)
        private static double GasMols(CondOwner co, string gas)
        {
            string cond = "StatGasMol" + gas;
            double mols = co.GetCondAmount(cond);
            GasContainer container = co.GasContainer;
            if (container != null && container.mapDGasMols != null && container.mapDGasMols.TryGetValue(cond, out double pending)) mols += pending;
            return Math.Max(0.0, mols);
        }

        //kg to show on a readout: a decimal under 100, whole above
        public static string Kg(double kg) => kg < 100.0 ? kg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Math.Floor(kg).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
