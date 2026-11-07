using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Fog of war over the stations the mod spawns. Per the user, the fog covers everything of the station but its hull's
    //outline, only what the player's people can directly see is revealed, and what's been revealed stays revealed.
    //
    //Sight is cast on the station's own tile grid, from each of the player's people aboard: rays out to SightRange tiles,
    //stopped by walls and closed doors (a doorway is see-through only with IsOpen), with the tile that stops them seen
    //too. It's recast a few times a second, and only while a ship is fully loaded.
    //
    //What's revealed is kept as a bitset over the station's tile indices, in a prop map on the station's first docking
    //port (prop maps on parts are saved; the ShipCO's aren't), so it lasts through saves and loads. The dictionary here
    //is only a cache of that, cleared on EmptyScene. 'fz fog clear' reveals a whole station for good.
    public static class SiteFog
    {
        public const string PanelName = "FzFog";
        private const string RevealedKey = "strRevealed";
        private const string ClearedKey = "strCleared";
        public const int SightRange = 24;
        private const float CastInterval = 0.2f; //Real seconds between sight casts

        private static readonly AccessTools.FieldRef<Ship, List<string>> DockingPorts = AccessTools.FieldRefAccess<Ship, List<string>>("aDockingPorts");

        public class State
        {
            public Ship Ship;
            public BitArray Revealed;
            public bool Cleared;
            public int Version; //Goes up whenever what's revealed changes, so the view knows to redraw
            public CondOwner Keeper;
        }

        private static readonly Dictionary<string, State> states = new Dictionary<string, State>();
        private static float nextCast;

        //Stations the mod spawned are fogged
        public static bool Applies(Ship ship) => ship != null && !ship.bDestroyed && ship.LoadState == Ship.Loaded.Full && ship.aTiles != null && FzSpawns.IsSpawn(ship);

        public static State Get(Ship ship)
        {
            if (!Applies(ship)) return null;
            if (states.TryGetValue(ship.strRegID, out State state) && state.Ship == ship && state.Revealed.Length == ship.aTiles.Count) return state;
            state = Load(ship);
            states[ship.strRegID] = state;
            return state;
        }

        public static bool IsRevealed(Ship ship, int index)
        {
            State state = Get(ship);
            return state == null || state.Cleared || index < 0 || index >= state.Revealed.Length || state.Revealed[index];
        }

        //Recasts sight for every fogged station, a few times a second
        public static void Tick()
        {
            if (CrewSim.system == null || Time.unscaledTime < nextCast) return;
            nextCast = Time.unscaledTime + CastInterval;
            foreach (Ship ship in CrewSim.system.GetAllLoadedShips())
            {
                State state = Get(ship);
                if (state == null || state.Cleared) continue;
                bool changed = false;
                foreach (CondOwner viewer in Viewers(ship)) changed |= Cast(state, viewer);
                if (changed)
                {
                    state.Version++;
                    Save(state);
                }
            }
        }

        //The player and their crew aboard the station
        private static IEnumerable<CondOwner> Viewers(Ship ship)
        {
            var seen = new HashSet<CondOwner>();
            foreach (CondOwner co in ship.GetPeople(false))
            {
                if (co != null && !co.bDestroyed && SiteIdentity.IsPlayersPerson(co) && seen.Add(co)) yield return co;
            }
        }

        //Rays from the viewer's tile to every tile on the edge of a square SightRange out, each marking what it passes
        //until a tile that blocks sight, which is seen too. Returns whether anything new was revealed.
        private static bool Cast(State state, CondOwner viewer)
        {
            Ship ship = state.Ship;
            Vector3 at = viewer.tf.position;
            int origin = ship.GetTileIndexAtWorldCoords(at.x, at.y);
            if (origin < 0) return false;
            int ox = origin % ship.nCols, oy = origin / ship.nCols;
            bool changed = Reveal(state, origin);
            int r = SightRange;
            for (int i = -r; i <= r; i++)
            {
                changed |= Ray(state, ox, oy, ox + i, oy - r);
                changed |= Ray(state, ox, oy, ox + i, oy + r);
                changed |= Ray(state, ox, oy, ox - r, oy + i);
                changed |= Ray(state, ox, oy, ox + r, oy + i);
            }
            return changed;
        }

        private static bool Ray(State state, int x0, int y0, int x1, int y1)
        {
            Ship ship = state.Ship;
            bool changed = false;
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, x = x0, y = y0;
            int limit = SightRange * SightRange;
            while (true)
            {
                if (x != x0 || y != y0)
                {
                    if ((x - x0) * (x - x0) + (y - y0) * (y - y0) > limit) break;
                    if (x < 0 || y < 0 || x >= ship.nCols || y >= ship.nRows) break;
                    int index = x + y * ship.nCols;
                    changed |= Reveal(state, index);
                    if (BlocksSight(ship.GetTileByIndex(index))) break;
                }
                if (x == x1 && y == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y += sy;
                }
            }
            return changed;
        }

        private static bool BlocksSight(Tile tile)
        {
            if (tile == null) return false;
            if (tile.IsWall) return true;
            return tile.IsPortal && (tile.coProps == null || !tile.coProps.HasCond("IsOpen"));
        }

        private static bool Reveal(State state, int index)
        {
            if (index < 0 || index >= state.Revealed.Length || state.Revealed[index]) return false;
            state.Revealed[index] = true;
            return true;
        }

        //Reveals the whole station for good
        public static bool Clear(Ship ship)
        {
            State state = Get(ship);
            if (state == null) return false;
            state.Cleared = true;
            state.Version++;
            Save(state);
            return true;
        }

        //Fogs the whole station again, for testing
        public static bool Reset(Ship ship)
        {
            State state = Get(ship);
            if (state == null) return false;
            state.Cleared = false;
            state.Revealed.SetAll(false);
            RevealOutline(state);
            state.Version++;
            Save(state);
            return true;
        }

        public static int RevealedCount(State state)
        {
            int count = 0;
            for (int i = 0; i < state.Revealed.Length; i++)
            {
                if (state.Revealed[i]) count++;
            }
            return count;
        }

        private static State Load(Ship ship)
        {
            var state = new State { Ship = ship, Revealed = new BitArray(ship.aTiles.Count), Keeper = Keeper(ship) };
            RevealOutline(state);
            Dictionary<string, string> props = null;
            if (state.Keeper?.mapGUIPropMaps != null) state.Keeper.mapGUIPropMaps.TryGetValue(PanelName, out props);
            if (props == null) return state;
            state.Cleared = props.TryGetValue(ClearedKey, out string cleared) && cleared == "1";
            if (props.TryGetValue(RevealedKey, out string bits) && !string.IsNullOrEmpty(bits))
            {
                try
                {
                    var saved = new BitArray(Convert.FromBase64String(bits));
                    for (int i = 0; i < state.Revealed.Length && i < saved.Length; i++) state.Revealed[i] = saved[i];
                }
                catch (FormatException)
                {
                    FzAdditions.Logger.LogWarning($"{ship.strRegID}'s fog record couldn't be read, so it's fogged again");
                }
            }
            return state;
        }

        //Per the user, the hull's outline shows from the start: every wall that touches anything outside the station
        private static void RevealOutline(State state)
        {
            Ship ship = state.Ship;
            for (int i = 0; i < ship.aTiles.Count; i++)
            {
                Tile tile = ship.aTiles[i];
                if (tile == null || !tile.IsWall) continue;
                int x = i % ship.nCols, y = i / ship.nCols;
                bool outside = false;
                for (int dx = -1; dx <= 1 && !outside; dx++)
                {
                    for (int dy = -1; dy <= 1 && !outside; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= ship.nCols || ny >= ship.nRows) outside = true;
                        else
                        {
                            Tile near = ship.aTiles[nx + ny * ship.nCols];
                            if (near == null || !near.IsShipTileOrSub) outside = true;
                        }
                    }
                }
                if (outside) state.Revealed[i] = true;
            }
        }

        private static void Save(State state)
        {
            CondOwner keeper = state.Keeper;
            if (keeper == null || keeper.bDestroyed)
            {
                keeper = state.Keeper = Keeper(state.Ship);
                if (keeper == null) return;
            }
            if (keeper.mapGUIPropMaps == null) keeper.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
            var bytes = new byte[(state.Revealed.Length + 7) / 8];
            state.Revealed.CopyTo(bytes, 0);
            keeper.mapGUIPropMaps[PanelName] = new Dictionary<string, string>
            {
                [RevealedKey] = Convert.ToBase64String(bytes),
                [ClearedKey] = state.Cleared ? "1" : "0"
            };
        }

        //The part the record is kept on: the station's first docking port
        private static CondOwner Keeper(Ship ship)
        {
            List<string> ports = DockingPorts(ship);
            if (ports == null) return null;
            foreach (string id in ports)
            {
                CondOwner co = ship.GetCOByID(id);
                if (co != null && !co.bDestroyed) return co;
            }
            return null;
        }

        //Runs on EmptyScene; the records stay on the stations
        public static void Forget()
        {
            states.Clear();
            nextCast = 0f;
        }
    }
}
