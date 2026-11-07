using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util.AI
{
    //The mod's own AI, kept apart from the game's AIShipManager so the game never runs, saves or despawns our pilots.
    //Sections register pilot types when the plugin loads. A ship is flown by a type while its ShipCO has that type's
    //persisted condition (IsFzAIPilot<Type>, defined in each section's data), so which ships are flown, and by what,
    //is saved with the ship. The pilots themselves are only a cache of that, found again by a scan of the system's
    //ships and cleared on EmptyScene.
    //
    //Tick runs right after the game's own AI update, which the game only calls while unpaused and while game time
    //moves, so pilots stop with the game and keep to game time under time acceleration. Each pilot says how often it
    //wants to run, and a frame runs at most Budget of the ones that are due.
    public static class FzAI
    {
        public const string PilotCondPrefix = "IsFzAIPilot";
        public const string AlertCond = "StatFzAIAlert";

        //Alert levels, on the ship as StatFzAIAlert so they're saved. What each means is up to the pilot.
        public const int Calm = 0;
        public const int Alerted = 1;
        public const int Hostile = 2;

        private const int Budget = 16;            //Pilots run per frame at most
        private const float ScanInterval = 2f;    //Real seconds between looks through the system for flown ships
        private const double FailDelay = 10.0;    //Game seconds a pilot rests after throwing

        private static readonly Dictionary<string, Func<Ship, FzPilot>> Factories = new Dictionary<string, Func<Ship, FzPilot>>();
        private static readonly Dictionary<string, FzPilot> Pilots = new Dictionary<string, FzPilot>();
        private static readonly List<FzPilot> Order = new List<FzPilot>();
        private static int cursor;
        private static float nextScan;

        public static void RegisterPilot(string type, Func<Ship, FzPilot> factory)
        {
            Factories[type] = factory;
        }

        public static IEnumerable<string> Types => Factories.Keys.OrderBy(t => t);

        public static IReadOnlyList<FzPilot> All => Order;

        public static string PilotCond(string type) => PilotCondPrefix + type;

        public static FzPilot Get(Ship ship)
        {
            if (ship == null) return null;
            Pilots.TryGetValue(ship.strRegID, out FzPilot pilot);
            return pilot;
        }

        //The registered type a ship's conditions say flies it, if any
        public static string TypeOf(Ship ship)
        {
            CondOwner co = ship?.ShipCO;
            if (co == null) return null;
            foreach (string type in Factories.Keys)
            {
                if (co.HasCond(PilotCond(type))) return type;
            }
            return null;
        }

        //Puts a pilot on a ship, replacing any it has
        public static bool Attach(Ship ship, string type, out string error)
        {
            error = null;
            if (ship?.ShipCO == null)
            {
                error = "That ship has no ShipCO.";
                return false;
            }
            if (!Factories.ContainsKey(type))
            {
                error = $"No pilot type named {type}. Types: {string.Join(", ", Types)}";
                return false;
            }
            if (DataHandler.GetCond(PilotCond(type)) == null)
            {
                error = $"The condition {PilotCond(type)} isn't defined, so the pilot couldn't be saved.";
                return false;
            }

            Detach(ship);
            ship.ShipCO.SetCondAmount(PilotCond(type), 1.0);
            return Add(ship, type) != null;
        }

        public static void Detach(Ship ship)
        {
            if (ship == null) return;
            if (ship.ShipCO != null)
            {
                foreach (string type in Factories.Keys) ship.ShipCO.ZeroCondAmount(PilotCond(type));
            }
            if (Pilots.TryGetValue(ship.strRegID, out FzPilot pilot)) Remove(pilot);
        }

        public static int GetAlert(Ship ship)
        {
            if (ship?.ShipCO == null) return Calm;
            return (int)Math.Round(ship.ShipCO.GetCondAmount(AlertCond));
        }

        public static void SetAlert(Ship ship, int level)
        {
            if (ship?.ShipCO == null) return;
            level = Math.Max(Calm, Math.Min(Hostile, level));
            if (level == Calm) ship.ShipCO.ZeroCondAmount(AlertCond);
            else ship.ShipCO.SetCondAmount(AlertCond, level);
        }

        public static string AlertName(int level)
        {
            switch (level)
            {
                case Calm: return "calm";
                case Alerted: return "alerted";
                case Hostile: return "hostile";
                default: return level.ToString();
            }
        }

        public static void Tick()
        {
            if (CrewSim.system == null) return;

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                Scan();
            }
            if (Order.Count == 0) return;

            double now = StarSystem.fEpoch;
            bool skipping = GUIFFWD.Active;
            int ran = 0;
            int count = Order.Count;
            for (int i = 0; i < count && ran < Budget && Order.Count > 0; i++)
            {
                if (cursor >= Order.Count) cursor = 0;
                FzPilot pilot = Order[cursor];

                Ship ship = pilot.ShipUs;
                if (ship == null || ship.bDestroyed || ship.bRemove || ship.ShipCO == null || !ship.ShipCO.HasCond(PilotCond(pilot.Type)))
                {
                    Remove(pilot);
                    continue;
                }
                cursor++;
                if (now < pilot.NextThink) continue;

                double dt = pilot.LastThink > 0 ? now - pilot.LastThink : 0;
                pilot.LastThink = now;
                pilot.NextThink = now + pilot.Interval;
                ran++;
                try
                {
                    if (skipping) pilot.FastForward(dt);
                    else pilot.Think(dt);
                }
                catch (Exception ex)
                {
                    pilot.NextThink = now + FailDelay;
                    FzAdditions.Logger.LogError($"FzAI pilot {pilot.Type} on {pilot.RegID} failed: {ex}");
                }
            }
        }

        //Finds flown ships with no pilot yet: ones loaded from a save, or spawned with the condition
        private static void Scan()
        {
            if (Factories.Count == 0 || CrewSim.system?.dictShips == null) return;
            foreach (Ship ship in CrewSim.system.dictShips.Values.ToList())
            {
                if (ship == null || ship.bDestroyed || Pilots.ContainsKey(ship.strRegID)) continue;
                string type = TypeOf(ship);
                if (type != null) Add(ship, type);
            }
        }

        private static FzPilot Add(Ship ship, string type)
        {
            FzPilot pilot;
            try
            {
                pilot = Factories[type](ship);
                pilot.Init();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"FzAI couldn't make a {type} pilot for {ship.strRegID}: {ex}");
                return null;
            }
            Pilots[ship.strRegID] = pilot;
            Order.Add(pilot);
            return pilot;
        }

        private static void Remove(FzPilot pilot)
        {
            int index = Order.IndexOf(pilot);
            if (index >= 0)
            {
                Order.RemoveAt(index);
                if (index < cursor) cursor--;
            }
            if (Pilots.TryGetValue(pilot.RegID, out FzPilot held) && held == pilot) Pilots.Remove(pilot.RegID);
            try
            {
                pilot.OnRemoved();
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"FzAI pilot {pilot.Type} on {pilot.RegID} failed to clean up: {ex}");
            }
        }

        //Runs on EmptyScene. The pilots belong to the world being emptied; the next one's are found by Scan.
        public static void Reset()
        {
            Pilots.Clear();
            Order.Clear();
            cursor = 0;
            nextScan = 0f;
        }
    }
}
