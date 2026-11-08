using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Breeder blankets: loose items fitted to a fusion reactor, which turn their loaded volatiles into D2O and He-3 as a
    //share of the fuel the reactor burns. Fitted, a blanket's state is conditions on the reactor, carried across its
    //forms; taken off, the same conditions on the blanket item. Bred fuel waits in the blanket and returns to the
    //ship's canisters over the tier's conversion time. See Design/Reactor/BreederBlankets.md.
    public static class BreederBlanket
    {
        public class Tier
        {
            public int Number;
            public string Name;
            public double Volatiles; //Loaded when it's made
            public double Yield; //Share of the fuel burned that comes back
            public double ConversionHours; //About two thirds of bred fuel returns within this, nearly all within three times it

            public double LifeKg => Volatiles / ReactorConstants.VolatilesPerKg;
        }

        public static readonly IReadOnlyList<Tier> Tiers = new[]
        {
            new Tier { Number = 1, Name = "Basic", Volatiles = 10000, Yield = 0.15, ConversionHours = 24 },
            new Tier { Number = 2, Name = "Advanced", Volatiles = 25000, Yield = 0.25, ConversionHours = 12 },
            new Tier { Number = 3, Name = "Superior", Volatiles = 60000, Yield = 0.40, ConversionHours = 3 },
        };

        public static Tier GetTier(int number) => Tiers.FirstOrDefault(t => t.Number == number);

        public static string BlanketCO(int tier) => ReactorConstants.BlanketPrefix + tier.ToString("00");

        //The tier of a blanket item from its name, 0 if it isn't a live blanket
        public static int ItemTier(CondOwner item)
        {
            string name = item?.strCODef;
            if (name == null || !name.StartsWith(ReactorConstants.BlanketPrefix) || name.EndsWith(ReactorConstants.SpentSuffix)) return 0;
            return int.TryParse(name.Substring(ReactorConstants.BlanketPrefix.Length), out int tier) && GetTier(tier) != null ? tier : 0;
        }

        public static bool IsBlanket(CondOwner item) => ItemTier(item) > 0;

        //The tier of the blanket fitted to a reactor, 0 for none
        public static int FittedTier(CondOwner reactor)
        {
            if (reactor == null) return 0;
            int tier = (int)Math.Round(reactor.GetCondAmount(ReactorConstants.BlanketTierCond));
            return GetTier(tier) != null ? tier : 0;
        }

        public static bool HasBlanket(CondOwner reactor) => FittedTier(reactor) > 0;

        //Units of volatiles converted, kg waiting, on a reactor or a blanket item
        public static double Converted(CondOwner co) => Math.Max(0.0, co.GetCondAmount(ReactorConstants.BlanketConvertedCond));
        public static double WaitingD2O(CondOwner co) => Math.Max(0.0, co.GetCondAmount(ReactorConstants.BlanketD2OCond));
        public static double WaitingHe3(CondOwner co) => Math.Max(0.0, co.GetCondAmount(ReactorConstants.BlanketHe3Cond));
        public static double Waiting(CondOwner co) => WaitingD2O(co) + WaitingHe3(co);

        //Share of a blanket's loaded volatiles still unconverted
        public static double Unconverted(CondOwner co, Tier tier) => tier == null ? 0.0 : Math.Max(0.0, 1.0 - Converted(co) / tier.Volatiles);

        //Every volatile converted and every kg returned
        public static bool IsSpent(CondOwner co, Tier tier) => tier != null && Unconverted(co, tier) <= 1e-6 && Waiting(co) < ReactorConstants.WaitingDone;

        public static bool IsBreeding(CondOwner reactor)
        {
            Tier tier = GetTier(FittedTier(reactor));
            return tier != null && Unconverted(reactor, tier) > 1e-6;
        }

        //From a fusion update, with the kg of each reactant it burned
        public static void Breed(CondOwner reactor, double burnedA, double burnedB)
        {
            Tier tier = GetTier(FittedTier(reactor));
            if (tier == null || reactor.HasCond("IsDamaged")) return;

            double left = tier.Volatiles - Converted(reactor);
            if (left <= 1e-6) return;
            double bredA = burnedA * tier.Yield;
            double bredB = burnedB * tier.Yield;
            double units = (bredA + bredB) * ReactorConstants.VolatilesPerKg;
            if (units <= 0.0) return;
            if (units > left)
            {
                double share = left / units;
                bredA *= share;
                bredB *= share;
                units = left;
            }

            reactor.AddCondAmount(ReactorConstants.BlanketConvertedCond, units);
            reactor.AddCondAmount(ReactorConstants.BlanketD2OCond, bredA);
            reactor.AddCondAmount(ReactorConstants.BlanketHe3Cond, bredB);
        }

        //From every reactor update, fusing or not, with the game seconds it covered. A share of what's waiting goes to
        //the ship's canisters; what they've no room for keeps waiting.
        public static void Release(CondOwner reactor, double seconds)
        {
            Tier tier = GetTier(FittedTier(reactor));
            if (tier == null || reactor.ship == null || seconds <= 0.0) return;

            double share = 1.0 - Math.Exp(-seconds / (tier.ConversionHours * 3600.0));
            ReleaseOne(reactor, ReactorConstants.BlanketD2OCond, "D2O", share);
            ReleaseOne(reactor, ReactorConstants.BlanketHe3Cond, "He3", share);
        }

        private static void ReleaseOne(CondOwner reactor, string cond, string reserveKey, double share)
        {
            double waiting = Math.Max(0.0, reactor.GetCondAmount(cond));
            if (waiting <= 0.0) return;
            double give = waiting < ReactorConstants.WaitingDone ? waiting : waiting * share;
            double given = Reserves.Fill(reactor.ship, Reserves.Get(reserveKey), give);
            if (given > 0.0) reactor.SetCondAmount(cond, Math.Max(0.0, waiting - given));
        }

        //A blanket item's state goes onto the reactor, before the fit interaction destroys the item
        public static void Fit(CondOwner reactor, CondOwner blanket)
        {
            int tier = ItemTier(blanket);
            if (reactor == null || tier == 0 || HasBlanket(reactor)) return;
            reactor.SetCondAmount(ReactorConstants.BlanketTierCond, tier);
            CopyState(blanket, reactor);
            FzAdditions.Logger.LogInfo($"{GetTier(tier).Name} breeder blanket fitted to {reactor.strID}, {Converted(reactor):0} volatiles converted");
        }

        //Takes the fitted blanket off as an item, spent or with its state, dropped on that ship at the spot given
        public static CondOwner Remove(CondOwner reactor, Ship ship, Vector2 at)
        {
            Tier tier = GetTier(FittedTier(reactor));
            if (tier == null) return null;

            bool spent = IsSpent(reactor, tier);
            string co = BlanketCO(tier.Number) + (spent ? ReactorConstants.SpentSuffix : "");
            CondOwner blanket = DataHandler.GetCondOwner(co, null, null, false);
            if (blanket != null && !spent) CopyState(reactor, blanket);
            ClearState(reactor);
            if (blanket == null) return null;

            try
            {
                (ship ?? reactor.ship)?.DropCO(blanket, at);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Dropping a breeder blanket failed: " + ex);
            }
            FzAdditions.Logger.LogInfo($"{tier.Name} breeder blanket taken off {reactor.strID}{(spent ? ", spent" : "")}");
            return blanket;
        }

        private static void CopyState(CondOwner from, CondOwner to)
        {
            foreach (string cond in new[] { ReactorConstants.BlanketConvertedCond, ReactorConstants.BlanketD2OCond, ReactorConstants.BlanketHe3Cond })
            {
                double amount = from.GetCondAmount(cond);
                if (amount > 0.0) to.SetCondAmount(cond, amount);
                else to.ZeroCondAmount(cond);
            }
        }

        private static void ClearState(CondOwner reactor)
        {
            reactor.ZeroCondAmount(ReactorConstants.BlanketTierCond);
            reactor.ZeroCondAmount(ReactorConstants.BlanketConvertedCond);
            reactor.ZeroCondAmount(ReactorConstants.BlanketD2OCond);
            reactor.ZeroCondAmount(ReactorConstants.BlanketHe3Cond);
        }

        //Recycling a blanket item: its volatiles and exotics only for the share still unconverted
        public static double RecycleShare(CondOwner item, string material)
        {
            if (material != ReactorConstants.VolatilesMaterial && material != ReactorConstants.ExoticsMaterial) return 1.0;
            Tier tier = GetTier(ItemTier(item));
            return tier == null ? 1.0 : Unconverted(item, tier);
        }

        //Recycling a blanket item returns all of its bred fuel
        public static Dictionary<string, double> RecycleReserves(CondOwner item)
        {
            if (!IsBlanket(item)) return null;
            return new Dictionary<string, double> { ["D2O"] = WaitingD2O(item), ["He3"] = WaitingHe3(item) };
        }
    }
}
