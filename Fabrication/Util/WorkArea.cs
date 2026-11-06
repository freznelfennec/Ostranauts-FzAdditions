using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //What the recycler and the fabricator share: each works on a rectangle of floor behind it, its working area, and
    //runs one item at a time, with its settings and its run in its own panel's prop map, so they're saved and carry
    //over between its forms. A machine's panel is used from the bottom of its sprite, where its use point is, and the
    //area reaches out from the top, from its back edge.
    public static class WorkArea
    {
        public const string RecyclerProps = "FzRecyclerGUI";
        public const string FabricatorProps = "FzFabricatorGUI";
        public const string FabricatorCond = "IsFzFabricator";

        public const string AreaWidthProp = "FzAreaWidth";
        public const string AreaDepthProp = "FzAreaDepth";
        public const string CurrentItemProp = "FzCurrentItem"; //The ID of the item it's working on
        public const string ProgressProp = "FzProgress"; //Game seconds of work done on it

        //A machine's tier is in its conditions: how far its area can reach across and out, and how fast it works, as a
        //multiple of the materials' times. A faster machine uses the same energy per item, so it draws more power.
        public const string MaxWidthCond = "StatFzAreaMaxWidth";
        public const string MaxDepthCond = "StatFzAreaMaxDepth";
        public const string SpeedCond = "StatFzWorkSpeed";

        public const int MinWidth = 1, DefaultWidth = 3;
        public const int MinDepth = 1, DefaultDepth = 2;
        public const int FallbackMaxWidth = 3, FallbackMaxDepth = 3; //For a machine without the conditions
        public const int LimitWidth = 15, LimitDepth = 15; //The most any tier can have

        public static int MaxWidth(CondOwner co) => Limit(co, MaxWidthCond, FallbackMaxWidth, MinWidth, LimitWidth);
        public static int MaxDepth(CondOwner co) => Limit(co, MaxDepthCond, FallbackMaxDepth, MinDepth, LimitDepth);

        private static int Limit(CondOwner co, string cond, int fallback, int min, int max)
        {
            double amount = co != null ? co.GetCondAmount(cond) : 0.0;
            return Mathf.Clamp(amount > 0.0 ? (int)Math.Round(amount) : fallback, min, max);
        }

        //Its working speed, 1 when it doesn't say
        public static double Speed(CondOwner co)
        {
            double speed = co != null ? co.GetCondAmount(SpeedCond) : 0.0;
            return speed > 0.0 ? speed : 1.0;
        }

        //Half a machine's depth in tiles, from its centre to its back edge, where the area starts
        private const float BackEdge = 1f;

        public static bool IsMachine(CondOwner co) => co != null && (co.HasCond(Recycler.RecyclerCond) || co.HasCond(FabricatorCond));

        //Its area, kept within its tier's limits, so a setting carried over from a bigger one shrinks to fit
        public static int GetWidth(CondOwner co) => Mathf.Clamp(GetInt(co, AreaWidthProp, DefaultWidth), MinWidth, MaxWidth(co));
        public static int GetDepth(CondOwner co) => Mathf.Clamp(GetInt(co, AreaDepthProp, DefaultDepth), MinDepth, MaxDepth(co));
        public static void SetWidth(CondOwner co, int width) => SetProp(co, AreaWidthProp, Mathf.Clamp(width, MinWidth, MaxWidth(co)).ToString(CultureInfo.InvariantCulture));
        public static void SetDepth(CondOwner co, int depth) => SetProp(co, AreaDepthProp, Mathf.Clamp(depth, MinDepth, MaxDepth(co)).ToString(CultureInfo.InvariantCulture));

        //The columns the area covers, counted in tiles from the machine's centre column, negative to its left. An even
        //width can't centre on that column, so it reaches one tile further left than right.
        public static int FirstColumn(int width) => -(width / 2);

        //The area in the machine's own frame: x along its right, y along its up, at positive y behind it
        public static Rect LocalArea(CondOwner co)
        {
            int width = GetWidth(co), depth = GetDepth(co);
            return new Rect(FirstColumn(width) - 0.5f, BackEdge, width, depth);
        }

        //Which of the area's tiles it can work on, by column across and row out from the machine. A tile is blocked if
        //it's a wall or not part of the ship, or if it can only be reached from the machine through a wall, so a
        //machine never works through a wall. Reaching starts at the row against its back edge and goes tile to tile.
        public static bool[,] OpenTiles(CondOwner co)
        {
            int width = GetWidth(co), depth = GetDepth(co);
            var open = new bool[width, depth];
            Ship ship = co?.ship;
            if (ship == null || co.tf == null) return open;

            var floor = new bool[width, depth];
            for (int col = 0; col < width; col++)
            {
                for (int row = 0; row < depth; row++)
                {
                    Vector2 centre = TileCentre(co, col, row);
                    Tile tile = ship.GetTileAtWorldCoords1(centre.x, centre.y, bAllowDocked: false);
                    floor[col, row] = tile != null && tile.IsShipTileOrSub && !tile.IsWall;
                }
            }

            var reach = new Queue<(int Col, int Row)>();
            for (int col = 0; col < width; col++)
            {
                if (!floor[col, 0]) continue;
                open[col, 0] = true;
                reach.Enqueue((col, 0));
            }
            while (reach.Count > 0)
            {
                var (col, row) = reach.Dequeue();
                foreach (var (c, r) in new[] { (col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1) })
                {
                    if (c < 0 || r < 0 || c >= width || r >= depth || open[c, r] || !floor[c, r]) continue;
                    open[c, r] = true;
                    reach.Enqueue((c, r));
                }
            }
            return open;
        }

        //A tile's centre in world space, by column and row of the area
        public static Vector2 TileCentre(CondOwner co, int col, int row)
        {
            Transform tf = co.tf;
            return (Vector2)tf.position + (Vector2)tf.right * (FirstColumn(GetWidth(co)) + col) + (Vector2)tf.up * (BackEdge + 0.5f + row);
        }

        //The area tile a point is on, or false when it's outside the area
        public static bool TileAt(CondOwner co, Vector2 world, out int col, out int row)
        {
            Transform tf = co.tf;
            Vector2 offset = world - (Vector2)tf.position;
            Vector2 local = new Vector2(Vector2.Dot(offset, tf.right), Vector2.Dot(offset, tf.up));
            Rect area = LocalArea(co);
            col = Mathf.FloorToInt(local.x - area.xMin);
            row = Mathf.FloorToInt(local.y - area.yMin);
            return area.Contains(local) && col >= 0 && row >= 0 && col < GetWidth(co) && row < GetDepth(co);
        }

        //Whether a point is on a tile of the area the machine can work on. open is from OpenTiles, worked out once for
        //a whole scan.
        public static bool InArea(CondOwner co, Vector2 world, bool[,] open)
        {
            return TileAt(co, world, out int col, out int row) && col < open.GetLength(0) && row < open.GetLength(1) && open[col, row];
        }

        //The centres of the tiles an item covers. An item's width and height in tiles are swapped as it's turned, so
        //they're always across and up the world, around its position.
        public static IEnumerable<Vector2> Footprint(CondOwner item, Vector2 centre)
        {
            Item shape = item?.Item;
            int width = Math.Max(1, shape != null ? shape.nWidthInTiles : 1), height = Math.Max(1, shape != null ? shape.nHeightInTiles : 1);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    yield return centre + new Vector2(x - (width - 1) / 2f, y - (height - 1) / 2f);
        }

        //Whether every tile an item covers is one of the area's open tiles, so an item hanging over the area's edge or
        //a blocked tile is left alone
        public static bool WhollyInArea(CondOwner co, CondOwner item, bool[,] open)
        {
            return Footprint(item, item.tf.position).All(tile => InArea(co, tile, open));
        }

        //Loose things on the ship: not installed, not inside something, not people or robots, not system objects such
        //as the loot spawners ship designs place, and not the machine itself
        private static IEnumerable<CondOwner> LooseThings(CondOwner co)
        {
            Ship ship = co?.ship;
            if (ship == null) yield break;
            foreach (CondOwner item in ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
            {
                if (item == null || item == co || item.bDestroyed || item.objCOParent != null || item.Item == null || item.tf == null) continue;
                if (item.IsHumanOrRobot || item.HasCond("IsInstalled") || item.HasCond("IsSystem")) continue;
                yield return item;
            }
        }

        //Loose items lying wholly on the area's open tiles
        public static List<CondOwner> ItemsInArea(CondOwner co)
        {
            if (co?.ship == null || co.tf == null) return new List<CondOwner>();
            bool[,] open = OpenTiles(co);
            return LooseThings(co).Where(item => WhollyInArea(co, item, open)).ToList();
        }

        //Whether a person or robot is standing on the area's open tiles, which keeps it from working
        public static bool AnyoneInArea(CondOwner co)
        {
            Ship ship = co?.ship;
            if (ship == null || co.tf == null) return false;
            bool[,] open = OpenTiles(co);
            foreach (CondOwner other in ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
            {
                if (other != null && !other.bDestroyed && other.IsHumanOrRobot && other.tf != null && InArea(co, other.tf.position, open)) return true;
            }
            return false;
        }

        //The ship's tiles, by index, that loose items or people cover anywhere on it, so nothing new is put on them
        public static HashSet<int> Occupied(CondOwner co)
        {
            var taken = new HashSet<int>();
            Ship ship = co?.ship;
            if (ship == null) return taken;
            foreach (CondOwner item in LooseThings(co))
            {
                foreach (Vector2 tile in Footprint(item, item.tf.position))
                {
                    int index = TileIndex(ship, tile);
                    if (index >= 0) taken.Add(index);
                }
            }
            foreach (CondOwner other in ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
            {
                if (other == null || other.bDestroyed || !other.IsHumanOrRobot || other.tf == null) continue;
                int index = TileIndex(ship, other.tf.position);
                if (index >= 0) taken.Add(index);
            }
            return taken;
        }

        //The index of the ship's tile a point is on, or -1 off the ship. Matching tiles by the ship's own grid, not by
        //rounding positions: tile centres are halfway between whole numbers, and a machine two tiles deep has its centre
        //on a line between rows, so rounding from either picked rows unreliably.
        public static int TileIndex(Ship ship, Vector2 world)
        {
            Tile tile = ship?.GetTileAtWorldCoords1(world.x, world.y, bAllowDocked: false);
            return tile != null ? tile.Index : -1;
        }
        //The item a machine is working on, if it's still on the ship
        public static CondOwner CurrentItem(CondOwner co)
        {
            string id = GetString(co, CurrentItemProp);
            if (string.IsNullOrEmpty(id)) return null;
            CondOwner item = co.ship?.GetCOByID(id);
            return item != null && !item.bDestroyed ? item : null;
        }

        public static void SetCurrent(CondOwner co, CondOwner item)
        {
            SetProp(co, CurrentItemProp, item != null ? item.strID : "");
            SetProgress(co, 0.0);
        }

        public static bool HasRun(CondOwner co) => !string.IsNullOrEmpty(GetString(co, CurrentItemProp));
        public static double Progress(CondOwner co) => GetDouble(co, ProgressProp, 0.0);
        public static void SetProgress(CondOwner co, double seconds) => SetProp(co, ProgressProp, seconds.ToString("R", CultureInfo.InvariantCulture));

        //A machine's settings, in its own panel's prop map
        public static Dictionary<string, string> Props(CondOwner co, bool create)
        {
            if (co == null) return null;
            string key = co.HasCond(FabricatorCond) ? FabricatorProps : RecyclerProps;
            if (co.mapGUIPropMaps.TryGetValue(key, out Dictionary<string, string> props)) return props;
            if (!create) return null;
            props = new Dictionary<string, string>();
            co.mapGUIPropMaps[key] = props;
            return props;
        }

        public static string GetString(CondOwner co, string key)
        {
            var props = Props(co, false);
            return props != null && props.TryGetValue(key, out string value) ? value : null;
        }

        public static int GetInt(CondOwner co, string key, int fallback)
        {
            string value = GetString(co, key);
            return value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
        }

        public static double GetDouble(CondOwner co, string key, double fallback)
        {
            string value = GetString(co, key);
            return value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : fallback;
        }

        public static bool GetBool(CondOwner co, string key, bool fallback = false)
        {
            string value = GetString(co, key);
            return value != null && bool.TryParse(value, out bool result) ? result : fallback;
        }

        public static void SetProp(CondOwner co, string key, string value)
        {
            var props = Props(co, true);
            if (props != null) props[key] = value;
        }
    }
}
