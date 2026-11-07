using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //An item as it sits on the tile grid, from its item definition and condowner: its size, the socket tags it adds to
    //the tiles it covers, what it needs and forbids on and round them, and its named points.
    //
    //Unturned, row 0 is the item's north edge. Adds are W by H; reqs and forbids are padded a tile all round, (W + 2) by
    //(H + 2). A socket tag (such as TILWallAdds) is a loot entry listing tile conditions (IsWall, IsObstruction). An item
    //fits where every tile it requires something of has all of that tag's conditions, and every tile it forbids
    //something on has none of them. Points are in sixteenths of a tile from the item's centre, x east and y north.
    public class ItemShape
    {
        public string Name;
        public int W, H;
        public string[] Adds;
        public string[] Reqs;
        public string[] Forbids;
        public readonly Dictionary<string, (float x, float y)> Points = new Dictionary<string, (float x, float y)>();
    }

    //Every item a site might use, and what each socket tag means. Filled from the game's data: in the game from
    //DataHandler (SiteCatalogLoader), and outside it from the game's JSON (the preview tool), so the rules simulated here
    //are the game's own.
    public class SiteCatalog
    {
        public readonly Dictionary<string, ItemShape> Items = new Dictionary<string, ItemShape>();
        public readonly Dictionary<string, string[]> Tags = new Dictionary<string, string[]>();

        public ItemShape Get(string name)
        {
            if (name == null) return null;
            Items.TryGetValue(name, out ItemShape shape);
            return shape;
        }

        //The tile conditions a socket tag stands for. "Blank" and unknown tags stand for nothing.
        public string[] TagConds(string tag)
        {
            if (tag == null || tag == "Blank") return null;
            Tags.TryGetValue(tag, out string[] conds);
            return conds;
        }

        //Points come as "name,x,y"
        public static void ParsePoints(ItemShape shape, IEnumerable<string> points)
        {
            if (points == null) return;
            foreach (string point in points)
            {
                string[] parts = point?.Split(',');
                if (parts == null || parts.Length < 3) continue;
                if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                {
                    shape.Points[parts[0]] = (x, y);
                }
            }
        }
    }

    //An item placed on the grid: its centre in grid coordinates (x east, y south; halves for even sizes) and its
    //rotation in degrees anticlockwise, 0 facing north
    public struct Placement
    {
        public ItemShape Shape;
        public double CX, CY;
        public int Rotation;

        //Rotated size
        public int W => Rotation % 180 == 0 ? Shape.W : Shape.H;
        public int H => Rotation % 180 == 0 ? Shape.H : Shape.W;

        //The grid tile of an unturned item's tile (c east, r south from its north-west corner; -1 and W or H for the
        //padding round it)
        public (int x, int y) Tile(int c, int r)
        {
            double lx = c - (Shape.W - 1) / 2.0, ly = (Shape.H - 1) / 2.0 - r; //From the centre, y north
            var (rx, ry) = Rotate(lx, ly, Rotation);
            return ((int)Math.Round(CX + rx), (int)Math.Round(CY - ry));
        }

        //The grid tile a named point falls on, or null if the item has no such point
        public (int x, int y)? Point(string name)
        {
            if (!Shape.Points.TryGetValue(name, out var p)) return null;
            var (rx, ry) = Rotate(p.x / 16.0, p.y / 16.0, Rotation);
            return ((int)Math.Round(CX + rx), (int)Math.Round(CY - ry));
        }

        public IEnumerable<(int x, int y)> Footprint()
        {
            for (int r = 0; r < Shape.H; r++)
            {
                for (int c = 0; c < Shape.W; c++) yield return Tile(c, r);
            }
        }

        private static (double x, double y) Rotate(double x, double y, int degrees)
        {
            switch (((degrees % 360) + 360) % 360)
            {
                case 90: return (-y, x);
                case 180: return (-x, -y);
                case 270: return (y, -x);
                default: return (x, y);
            }
        }

        //The centre for an item whose rotated footprint has its north-west tile at x, y
        public static Placement At(ItemShape shape, int x, int y, int rotation)
        {
            int w = rotation % 180 == 0 ? shape.W : shape.H, h = rotation % 180 == 0 ? shape.H : shape.W;
            return new Placement { Shape = shape, CX = x + (w - 1) / 2.0, CY = y + (h - 1) / 2.0, Rotation = rotation };
        }
    }

    //The tile conditions every placed item has added, so a placement can be checked against them the way the game
    //checks its sockets
    public class SiteTiles
    {
        private readonly SiteCatalog catalog;
        private readonly Dictionary<(int, int), HashSet<string>> tiles = new Dictionary<(int, int), HashSet<string>>();

        public SiteTiles(SiteCatalog catalog)
        {
            this.catalog = catalog;
        }

        public bool Has(int x, int y, string cond) => tiles.TryGetValue((x, y), out var set) && set.Contains(cond);

        public bool Fits(Placement p)
        {
            ItemShape s = p.Shape;
            int pw = s.W + 2;
            for (int r = -1; r <= s.H; r++)
            {
                for (int c = -1; c <= s.W; c++)
                {
                    int i = (r + 1) * pw + (c + 1);
                    var (x, y) = p.Tile(c, r);
                    tiles.TryGetValue((x, y), out var set);
                    string[] need = s.Reqs != null && i < s.Reqs.Length ? catalog.TagConds(s.Reqs[i]) : null;
                    if (need != null)
                    {
                        if (set == null) return false;
                        foreach (string cond in need)
                        {
                            if (!set.Contains(cond)) return false;
                        }
                    }
                    string[] forbid = s.Forbids != null && i < s.Forbids.Length ? catalog.TagConds(s.Forbids[i]) : null;
                    if (forbid != null && set != null)
                    {
                        foreach (string cond in forbid)
                        {
                            if (set.Contains(cond)) return false;
                        }
                    }
                }
            }
            return true;
        }

        //The first socket rule a placement breaks, for tracing, or null if it fits
        public string Explain(Placement p)
        {
            ItemShape s = p.Shape;
            int pw = s.W + 2;
            for (int r = -1; r <= s.H; r++)
            {
                for (int c = -1; c <= s.W; c++)
                {
                    int i = (r + 1) * pw + (c + 1);
                    var (x, y) = p.Tile(c, r);
                    tiles.TryGetValue((x, y), out var set);
                    string[] need = s.Reqs != null && i < s.Reqs.Length ? catalog.TagConds(s.Reqs[i]) : null;
                    if (need != null && (set == null || need.Any(n => !set.Contains(n)))) return $"{s.Reqs[i]} needed at {x},{y} (row {r}, column {c}), which has {(set == null ? "nothing" : string.Join("+", set))}";
                    string[] forbid = s.Forbids != null && i < s.Forbids.Length ? catalog.TagConds(s.Forbids[i]) : null;
                    if (forbid != null && set != null && forbid.Any(set.Contains)) return $"{s.Forbids[i]} forbidden at {x},{y} (row {r}, column {c}), which has {string.Join("+", set)}";
                }
            }
            return null;
        }

        public void Apply(Placement p)
        {
            ItemShape s = p.Shape;
            if (s.Adds == null) return;
            for (int r = 0; r < s.H; r++)
            {
                for (int c = 0; c < s.W; c++)
                {
                    int i = r * s.W + c;
                    string[] conds = i < s.Adds.Length ? catalog.TagConds(s.Adds[i]) : null;
                    if (conds == null) continue;
                    var key = p.Tile(c, r);
                    if (!tiles.TryGetValue(key, out var set)) tiles[key] = set = new HashSet<string>();
                    foreach (string cond in conds) set.Add(cond);
                }
            }
        }

        //Whether someone can stand on a tile: floor, with nothing in the way, or an open doorway
        public bool Walkable(int x, int y)
        {
            if (!tiles.TryGetValue((x, y), out var set)) return false;
            if (set.Contains("IsPortal")) return true;
            return set.Contains("IsFloor") && !set.Contains("IsObstruction") && !set.Contains("IsWall") && !set.Contains("IsItemTile");
        }
    }
}
