using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    //Extra fuel burn for systems that draw power directly from a reactor. A system adds to the burn as it
    //takes energy, then the reactor pays for it from its tanks on its next fusion update.
    //One unit of burn removes FusionIC.aReactantAmounts of each reactant, the same as the reactor's own burn.
    public static class ReactorFuel
    {
        private const double RateWindow = 5.0; //In game seconds, systems add in lumps so the rate is averaged for display
        private const double ShortShare = 0.999; //Of a burn the tanks must give, tank amounts are too large for an exact match

        private class BurnState
        {
            public CondOwner Reactor;
            public double Pending;
            public double WindowBurn;
            public double WindowTime;
            public double Rate;
        }

        private static readonly Dictionary<string, BurnState> states = new Dictionary<string, BurnState>();

        //Sections that want to know what a reactor burns, such as breeder blankets. Wants says which reactors are
        //worth measuring, as measuring scans the ship. Burned gets the kg of each reactant a fusion update took, the
        //reactor's own burn and the extra together. Registered as the plugin loads, so they outlast a load.
        public delegate void BurnWatcher(CondOwner reactor, double burnedA, double burnedB, double seconds);
        private static readonly List<(Func<CondOwner, bool> Wants, BurnWatcher Burned)> watchers = new List<(Func<CondOwner, bool>, BurnWatcher)>();

        //Every reactor update, fusing or not, with the game time it covered
        public static event Action<CondOwner, double> ReactorRun;

        public static void AddBurnWatcher(Func<CondOwner, bool> wants, BurnWatcher burned)
        {
            watchers.Add((wants, burned));
        }

        public static bool IsWatched(CondOwner reactor) => reactor != null && watchers.Any(w => w.Wants(reactor));

        public static void ReportBurn(CondOwner reactor, double burnedA, double burnedB, double seconds)
        {
            foreach (var watcher in watchers)
            {
                if (watcher.Wants(reactor)) watcher.Burned(reactor, burnedA, burnedB, seconds);
            }
        }

        public static void ReportRun(CondOwner reactor, double seconds) => ReactorRun?.Invoke(reactor, seconds);

        //Extra burn is only paid for while this is true, systems should not draw from a reactor otherwise
        //Matches FusionIC.CTFuse without using it. Reading that property before the reactor's first update
        //makes the reactor skip its own initialisation, because it checks the same backing field.
        public static bool IsFusing(CondOwner reactor)
        {
            return reactor != null && reactor.HasCond("IsReadyFusion") && !reactor.HasCond("IsOverrideOff");
        }

        public static void AddBurn(CondOwner reactor, double burn)
        {
            if (reactor == null || burn <= 0.0) return;
            GetState(reactor).Pending += burn;
        }

        //Burn units per game second added on top of the reactor's own burn
        public static double GetBurnRate(CondOwner reactor)
        {
            if (!IsFusing(reactor)) return 0.0;
            return states.TryGetValue(reactor.strID, out BurnState state) ? state.Rate : 0.0;
        }

        //Seconds is the game time the reactor's fusion update covered
        public static void PayBurn(FusionIC fusion, double seconds)
        {
            if (!states.TryGetValue(fusion.COSelf.strID, out BurnState state)) return;

            double burn = state.Pending;
            state.Pending = 0.0;
            if (burn > 0.0) RemoveBurn(fusion, burn);

            state.WindowBurn += burn;
            state.WindowTime += seconds;
            if (state.WindowTime < RateWindow) return;
            state.Rate = state.WindowBurn / state.WindowTime;
            state.WindowBurn = 0.0;
            state.WindowTime = 0.0;
        }

        //What a settled burn asked of the tanks and what they gave, for checking a time skip's fuel use
        public struct BurnReceipt
        {
            public double Burn;
            public double WantedA;
            public double WantedB;
            public double TakenA;
            public double TakenB;

            public bool Short => TakenA < WantedA * ShortShare || TakenB < WantedB * ShortShare;

            public void Add(BurnReceipt other)
            {
                Burn += other.Burn;
                WantedA += other.WantedA;
                WantedB += other.WantedB;
                TakenA += other.TakenA;
                TakenB += other.TakenB;
            }
        }

        //Pays what is owed now rather than on the next fusion update, a time skip has no updates between its steps
        public static BurnReceipt PayPending(CondOwner reactor)
        {
            BurnReceipt receipt = new BurnReceipt();
            FusionIC fusion = reactor?.GetComponent<FusionIC>();
            if (fusion == null || !states.TryGetValue(reactor.strID, out BurnState state) || state.Pending <= 0.0) return receipt;

            receipt.Burn = state.Pending;
            state.Pending = 0.0;
            receipt.WantedA = receipt.Burn * FusionIC.aReactantAmounts[0];
            receipt.WantedB = receipt.Burn * FusionIC.aReactantAmounts[1];

            GetHeld(reactor, out double heldA, out double heldB);
            RemoveBurn(fusion, receipt.Burn);
            GetHeld(reactor, out double leftA, out double leftB);
            receipt.TakenA = heldA - leftA;
            receipt.TakenB = heldB - leftB;
            return receipt;
        }

        //The reactants aboard, in the order of FusionIC.aReactantNames
        public static void GetHeld(CondOwner reactor, out double heldA, out double heldB)
        {
            heldA = 0.0;
            heldB = 0.0;
            FusionIC fusion = reactor?.GetComponent<FusionIC>();
            if (fusion == null || reactor.ship == null) return;

            heldA = reactor.ship.GetCOs(fusion.CTReactantA, bSubObjects: true, bAllowDocked: false, bAllowLocked: true).Sum(tank => tank.GetCondAmount(FusionIC.aReactantNames[0]));
            heldB = reactor.ship.GetCOs(fusion.CTReactantB, bSubObjects: true, bAllowDocked: false, bAllowLocked: true).Sum(tank => tank.GetCondAmount(FusionIC.aReactantNames[1]));
        }

        //An emptied tank is left for the reactor to find, it shuts down when its own burn can't be met
        private static void RemoveBurn(FusionIC fusion, double burn)
        {
            Ship ship = fusion.COSelf.ship;
            if (ship == null) return;
            RemoveReactant(ship, fusion.CTReactantA, FusionIC.aReactantNames[0], burn * FusionIC.aReactantAmounts[0]);
            RemoveReactant(ship, fusion.CTReactantB, FusionIC.aReactantNames[1], burn * FusionIC.aReactantAmounts[1]);
        }

        //Anything aboard holding the reactant counts as a tank, so tanks aren't tied to specific items
        private static void RemoveReactant(Ship ship, CondTrigger holdsReactant, string reactant, double amount)
        {
            foreach (CondOwner tank in ship.GetCOs(holdsReactant, bSubObjects: true, bAllowDocked: false, bAllowLocked: true))
            {
                double taken = Math.Min(amount, tank.GetCondAmount(reactant));
                tank.AddCondAmount(reactant, -taken);
                amount -= taken;
                if (amount <= 0.0) return;
            }
        }

        private static BurnState GetState(CondOwner reactor)
        {
            if (!states.TryGetValue(reactor.strID, out BurnState state))
            {
                state = new BurnState { Reactor = reactor };
                states[reactor.strID] = state;
            }
            state.Reactor = reactor;
            return state;
        }

        //Before the game saves, so the charge in the units is paid for in the same save. A burn is otherwise only
        //paid on the reactor's next update, which the save would never see.
        public static void PayAllPending()
        {
            foreach (BurnState state in states.Values)
            {
                if (state.Pending > 0.0 && state.Reactor != null && !state.Reactor.bDestroyed) PayPending(state.Reactor);
            }
        }

        //A save loading has its own reactors, and its units were paid for when it was saved
        public static void Reset()
        {
            states.Clear();
        }
    }
}
