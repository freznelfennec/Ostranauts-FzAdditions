using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //A material silo: any CO with IsFzSilo. Its contents are one persisted condition per material, StatFzMat<ID>, so
    //they're saved with it and carried over when it's installed, uninstalled or picked up. It holds the materials whose
    //silo conditions it has, up to StatFzSiloCapacity units in all.
    //
    //Its mass and price follow its contents: the CO's own starting StatMass and StatBasePrice, plus the contents'.
    public static class Silo
    {
        public const string SiloCond = "IsFzSilo";
        public const string CapacityCond = "StatFzSiloCapacity";
        public const string ContentsPrefix = "StatFzMat";
        public const string WorkingTrigger = "TIsFzSiloWorking";
        public const double UnitMass = 0.01; //kg

        public static string ContentsCond(string material) => ContentsPrefix + material;

        public static bool IsSilo(CondOwner co) => co != null && co.HasCond(SiloCond);

        //Installed and undamaged, so machines on its ship can use it
        public static bool IsWorking(CondOwner co) => IsSilo(co) && co.HasCond("IsInstalled") && !co.HasCond("IsDamaged");

        public static double Capacity(CondOwner co) => co.GetCondAmount(CapacityCond);

        public static bool Accepts(CondOwner co, string material)
        {
            return IsSilo(co) && FabData.Materials.TryGetValue(material, out JsonFabMaterial found) && co.HasCond(found.strSiloCond);
        }

        public static double Amount(CondOwner co, string material) => co.GetCondAmount(ContentsCond(material));

        public static double Total(CondOwner co) => FabData.Materials.Keys.Sum(m => Amount(co, m));

        public static double Free(CondOwner co) => Math.Max(0.0, Capacity(co) - Total(co));

        //Every material it holds any of, with the amounts
        public static IEnumerable<KeyValuePair<string, double>> Contents(CondOwner co)
        {
            return FabData.Materials.Keys.Select(m => new KeyValuePair<string, double>(m, Amount(co, m))).Where(p => p.Value > 0);
        }

        //Puts in as much as fits and returns how much that was
        public static double Add(CondOwner co, string material, double units)
        {
            if (units <= 0 || !Accepts(co, material)) return 0;
            double added = Math.Min(units, Free(co));
            if (added <= 0) return 0;
            co.AddCondAmount(ContentsCond(material), added);
            Refresh(co);
            return added;
        }

        //Takes out up to that much and returns how much it took
        public static double Remove(CondOwner co, string material, double units)
        {
            if (units <= 0 || !IsSilo(co)) return 0;
            double removed = Math.Min(units, Amount(co, material));
            if (removed <= 0) return 0;
            co.AddCondAmount(ContentsCond(material), -removed);
            Refresh(co);
            return removed;
        }

        public static void Clear(CondOwner co)
        {
            if (!IsSilo(co)) return;
            foreach (string material in FabData.Materials.Keys) co.ZeroCondAmount(ContentsCond(material));
            Refresh(co);
        }

        //Sets its mass and price from its contents
        public static void Refresh(CondOwner co)
        {
            if (!IsSilo(co)) return;
            double mass = StartingAmount(co, "StatMass"), price = StartingAmount(co, "StatBasePrice");
            foreach (var content in Contents(co))
            {
                mass += content.Value * UnitMass;
                price += content.Value * FabData.Materials[content.Key].fUnitPrice;
            }
            co.SetCondAmount("StatMass", mass);
            co.SetCondAmount("StatBasePrice", price);
        }

        //Working silos on the ship, not counting docked ships
        public static List<CondOwner> GetWorking(Ship ship)
        {
            if (ship == null) return new List<CondOwner>();
            CondTrigger trigger = DataHandler.GetCondTrigger(WorkingTrigger);
            if (trigger == null) return new List<CondOwner>(); //A null trigger would match everything on the ship
            return ship.GetCOs(trigger, bSubObjects: false, bAllowDocked: false, bAllowLocked: false) ?? new List<CondOwner>();
        }

        //Fills the ship's silos, the one already holding the most of the material first, so it stays together.
        //Returns how much went in. A silo given as except is left out.
        public static double Fill(Ship ship, string material, double units, CondOwner except = null)
        {
            double left = units;
            foreach (CondOwner silo in GetWorking(ship).Where(s => s != except && Accepts(s, material)).OrderByDescending(s => Amount(s, material)))
            {
                if (left <= 0) break;
                left -= Add(silo, material, left);
            }
            return units - left;
        }

        //Draws from the ship's silos, the one holding the least of the material first, so stragglers empty.
        //Returns how much came out. A silo given as except is left out.
        public static double Draw(Ship ship, string material, double units, CondOwner except = null)
        {
            double left = units;
            foreach (CondOwner silo in GetWorking(ship).Where(s => s != except && Amount(s, material) > 0).OrderBy(s => Amount(s, material)))
            {
                if (left <= 0) break;
                left -= Remove(silo, material, left);
            }
            return units - left;
        }

        public static double ShipAmount(Ship ship, string material) => GetWorking(ship).Sum(s => Amount(s, material));

        //Every material the silo can hold, in the order the materials were loaded
        public static List<string> Accepted(CondOwner co) => FabData.Materials.Keys.Where(m => Accepts(co, m)).ToList();

        //Moves up to that much of the material from this silo into the ship's other silos, returning how much moved
        public static double MoveOut(CondOwner co, string material, double units)
        {
            double moved = Fill(co?.ship, material, Math.Min(units, Amount(co, material)), co);
            Remove(co, material, moved);
            return moved;
        }

        //Moves up to that much of the material from the ship's other silos into this one, returning how much moved
        public static double MoveIn(CondOwner co, string material, double units)
        {
            if (!Accepts(co, material)) return 0;
            double moved = Draw(co.ship, material, Math.Min(units, Free(co)), co);
            Add(co, material, moved);
            return moved;
        }

        public static double OthersFree(CondOwner co, string material) => GetWorking(co?.ship).Where(s => s != co && Accepts(s, material)).Sum(Free);

        public static double OthersAmount(CondOwner co, string material) => GetWorking(co?.ship).Where(s => s != co).Sum(s => Amount(s, material));

        //A condition's amount in the CO's definition, before anything changed it
        private static double StartingAmount(CondOwner co, string cond)
        {
            JsonCondOwner def = DataHandler.GetCondOwnerDef(co.strCODef);
            string entry = def?.aStartingConds?.FirstOrDefault(c => c.StartsWith(cond + "="));
            if (entry == null) return 0;
            int x = entry.LastIndexOf('x');
            return x > 0 && double.TryParse(entry.Substring(x + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) ? amount : 0;
        }
    }
}
