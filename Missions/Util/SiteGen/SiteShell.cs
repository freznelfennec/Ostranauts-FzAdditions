using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //One base game item to place: its CO name, where its centre goes in ship coordinates, and which way it's turned
    public struct SiteItem
    {
        public string Name;
        public float X;
        public float Y;
        public float Rotation;
        public string Id;
        public string Pin;   //A door's lock code, written to its lock panel
        public string ParentId;     //What it's inside, such as rounds in a weapon
        public string PanelName;    //One prop map to give it, such as a loot spawner's Panel A
        public string[] Panel;      //Its settings, key and value in turn
    }

    //A site's shell as items, ready to become a ship template: hull, floors, rock and docks. Still plain C#; the game
    //side (SiteSpawner) wraps it in a JsonShip.
    public class SiteLayout
    {
        public SiteGrid Grid;
        public SiteProgram Program;
        public string TemplateName;
        public float OriginX;   //The top left tile's position, the template's vShipPos
        public float OriginY;
        public readonly List<SiteItem> Items = new List<SiteItem>();
        public readonly List<string> DockIds = new List<string>();
        public int Furnished;                                   //Items the furnisher added
        public int Weapons, Sensors, Rounds, LootSpawners, Cores, FloorLoot, Controllers; //What went on the hull, into containers and onto floors
        public readonly HashSet<string> MissingItems = new HashSet<string>(); //Fixture alternatives the catalog doesn't have
        public readonly List<string> FurnishProblems = new List<string>();
        public readonly List<Placement> Placed = new List<Placement>();   //What the furnisher placed, for the preview
        public readonly Dictionary<string, int> Shortfalls = new Dictionary<string, int>(); //Fixtures, by item, that didn't fit where a role wanted them

        public int Width => Grid.Width;
        public int Height => Grid.Height;

        //Ship coordinates of a tile: x runs right from the origin and y runs up, so grid rows go down from it
        public float ItemX(int x) => OriginX + x;
        public float ItemY(int y) => OriginY - y;
    }

    //Turns a site's tiles into items.
    //
    //Each walkable tile gets a floor for its zone, each wall tile a wall, each rock tile solid rock, and each dock a
    //docking port. Rooms aren't written: the game rebuilds them from the walls when the ship loads, and a ship spawned
    //from a template fills its sealed rooms with air on its first full load.
    //
    //The docking port (ItmDockSys02Closed) is 7 by 2 tiles and brings its own walls. Unturned it faces north: its outer
    //row holds the port with two walls either side of it, and its inner row a wall two tiles either side of a 3-tile
    //alcove. The pods' 5-tile width matches it, so the walls it brings are left off the hull. Rotations go
    //anticlockwise: 0 faces north, 90 west, 180 south and 270 east, and the item's centre sits half a tile in from the
    //port, between its two rows.
    public static class SiteShell
    {
        private const string Prefix = "FzSite_";

        //A template's name holds everything needed to make it again, so it can be rebuilt if the game asks for it after
        //a load (SiteTemplatePatch)
        public static string TemplateName(SiteKind kind, SiteSize size, int seed, string corp) => $"{Prefix}{kind}_{size}_{seed}_{corp ?? SiteData.GenericCorp}";

        //Templates made before sites had corporations have no corporation part, and are Generic
        public static bool TryParseTemplateName(string name, out SiteKind kind, out SiteSize size, out int seed, out string corp)
        {
            kind = SiteKind.Relay;
            size = SiteSize.Small;
            seed = 0;
            corp = SiteData.GenericCorp;
            if (name == null || !name.StartsWith(Prefix)) return false;
            string[] parts = name.Substring(Prefix.Length).Split(new[] { '_' }, 4);
            if (parts.Length < 3 || !Enum.TryParse(parts[0], out kind) || !Enum.TryParse(parts[1], out size) || !int.TryParse(parts[2], out seed)) return false;
            if (parts.Length == 4 && parts[3].Length > 0) corp = parts[3];
            return true;
        }

        //With a catalog, the shell is furnished too (SiteFurnish), by the corporation's program from the data
        public static SiteLayout Build(SiteKind kind, SiteSize size, int seed, string corp, SiteData data, SiteCatalog catalog = null)
        {
            SiteGrid grid = SiteShapes.Generate(kind, size, seed);
            if (grid == null) return null;
            SiteProgram program = data?.Program(kind, corp) ?? new SiteProgram { Kind = kind, Corp = corp ?? SiteData.GenericCorp };
            return Build(grid, program, catalog);
        }

        public static SiteLayout Build(SiteGrid grid, SiteProgram program, SiteCatalog catalog = null)
        {
            SiteStyle style = program.Style ?? SiteStyle.For(grid.Kind);
            program.Style = style;
            var layout = new SiteLayout
            {
                Grid = grid,
                Program = program,
                TemplateName = TemplateName(grid.Kind, grid.Size, grid.Seed, program.Corp),
                OriginX = -(grid.Width / 2),
                OriginY = grid.Height / 2
            };

            //Tiles whose walls the docking ports bring
            var dockWalls = new HashSet<(int, int)>();
            foreach (SiteDock dock in grid.Docks)
            {
                foreach (var tile in DockWallTiles(dock)) dockWalls.Add(tile);
            }
            foreach (SiteDoor door in grid.Doors)
            {
                foreach (var tile in SiteGrid.DoorWallTiles(door)) dockWalls.Add(tile);
            }

            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    SiteZone zone = grid[x, y];
                    string name = null;
                    switch (zone)
                    {
                        case SiteZone.Space:
                        case SiteZone.Dock:
                            break;
                        case SiteZone.Rock:
                            name = style.RockWalls[Hash(x, y) % style.RockWalls.Length];
                            break;
                        case SiteZone.Strut:
                        case SiteZone.Wall:
                        case SiteZone.Mount: //Hull; the furnisher puts a weapon across it
                            if (!dockWalls.Contains((x, y))) name = IsTunnelWall(grid, x, y) ? style.TunnelWall : style.HullWall;
                            break;
                        default:
                            name = Floor(style, zone);
                            break;
                    }
                    if (name != null) Add(layout, name, layout.ItemX(x), layout.ItemY(y), 0f);
                    //Walls and rock beside a passage get a floor under them too, as the base game's ships have: a wall's corner
                    //and edge sprites are partly clear, and with nothing under them space shows through. Docks' and doors'
                    //own walls get one as well.
                    if (!SiteGrid.IsWalkable(zone) && zone != SiteZone.Space)
                    {
                        string under = FloorBeside(style, grid, x, y);
                        if (under != null) Add(layout, under, layout.ItemX(x), layout.ItemY(y), 0f);
                    }
                }
            }

            foreach (SiteDock dock in grid.Docks)
            {
                float fx = layout.ItemX(dock.X), fy = layout.ItemY(dock.Y);
                float rotation;
                switch (dock.Facing)
                {
                    case Facing.North: rotation = 0f; fy -= 0.5f; break;
                    case Facing.West: rotation = 90f; fx += 0.5f; break;
                    case Facing.South: rotation = 180f; fy += 0.5f; break;
                    default: rotation = 270f; fx -= 0.5f; break;
                }
                layout.DockIds.Add(Add(layout, style.Dock, fx, fy, rotation));
            }

            //Doors: the 5-tile door item, unturned, lies east to west across a passage running north and south
            foreach (SiteDoor door in grid.Doors)
            {
                Add(layout, style.Door, layout.ItemX(door.X), layout.ItemY(door.Y), door.PassageNorthSouth ? 0f : 90f);
                SiteItem added = layout.Items[layout.Items.Count - 1];
                added.Pin = DoorPin(grid.Seed, door.X, door.Y);
                layout.Items[layout.Items.Count - 1] = added;
            }

            if (catalog != null) layout.FurnishProblems.AddRange(SiteFurnish.Furnish(layout, catalog, program));
            return layout;
        }

        //The floor of a walkable tile next to this one, sides before corners, or null when none is. Dock tiles are
        //skipped: the port brings its own.
        private static string FloorBeside(SiteStyle style, SiteGrid grid, int x, int y)
        {
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            for (int i = 0; i < dx.Length; i++)
            {
                SiteZone next = grid[x + dx[i], y + dy[i]];
                if (next != SiteZone.Dock && SiteGrid.IsWalkable(next)) return Floor(style, next);
            }
            return null;
        }

        //The tiles a docking port puts walls on: two either side of the port on the hull, and one two tiles either side
        //of the alcove behind it
        public static IEnumerable<(int x, int y)> DockWallTiles(SiteDock dock)
        {
            (int ox, int oy) = SiteGrid.Step(dock.Facing);
            int ax = oy != 0 ? 1 : 0, ay = ox != 0 ? 1 : 0; //Along the hull
            foreach (int a in new[] { -2, -1, 1, 2 }) yield return (dock.X + ax * a, dock.Y + ay * a);
            foreach (int a in new[] { -2, 2 }) yield return (dock.X - ox + ax * a, dock.Y - oy + ay * a);
        }

        //Problems with a layout that would stop it working in the game
        public static List<string> Check(SiteLayout layout)
        {
            var problems = new List<string>();
            SiteGrid grid = layout.Grid;
            foreach (SiteDock dock in grid.Docks)
            {
                (int ox, int oy) = SiteGrid.Step(dock.Facing);
                int ax = oy != 0 ? 1 : 0, ay = ox != 0 ? 1 : 0;
                foreach (var (x, y) in DockWallTiles(dock).Take(4))
                {
                    if (grid[x, y] != SiteZone.Wall && grid[x, y] != SiteZone.Mount) problems.Add($"dock at {dock.X},{dock.Y}: its hull wall at {x},{y} is {grid[x, y]}");
                }
                foreach (int a in new[] { -1, 0, 1 })
                {
                    int x = dock.X - ox + ax * a, y = dock.Y - oy + ay * a;
                    if (!SiteGrid.IsWalkable(grid[x, y])) problems.Add($"dock at {dock.X},{dock.Y}: its alcove at {x},{y} is {grid[x, y]}");
                }
                int sx = dock.X + ox, sy = dock.Y + oy;
                if (SiteGrid.IsInterior(grid[sx, sy]) || grid[sx, sy] == SiteZone.Rock) problems.Add($"dock at {dock.X},{dock.Y} doesn't open onto space");
            }
            foreach (SiteDoor door in grid.Doors)
            {
                foreach (var (x, y) in SiteGrid.DoorWallTiles(door))
                {
                    if (grid[x, y] != SiteZone.Wall && grid[x, y] != SiteZone.Strut) problems.Add($"door at {door.X},{door.Y}: its wall at {x},{y} is {grid[x, y]}");
                }
                int px = door.PassageNorthSouth ? 0 : 1, py = door.PassageNorthSouth ? 1 : 0;
                if (!SiteGrid.IsWalkable(grid[door.X - px, door.Y - py]) || !SiteGrid.IsWalkable(grid[door.X + px, door.Y + py])) problems.Add($"door at {door.X},{door.Y} doesn't open onto floor on both sides");
            }
            foreach (var (x, y, dx, dy) in grid.DoorMisses) problems.Add($"a door at {x},{y} across a passage going {dx},{dy} didn't fit: walls {grid[x - dy * 2, y - dx * 2]} {grid[x - dy, y - dx]} {grid[x, y]} {grid[x + dy, y + dx]} {grid[x + dy * 2, y + dx * 2]}, before {grid[x - dx, y - dy]}, after {grid[x + dx, y + dy]}");
            problems.AddRange(layout.FurnishProblems);
            if (grid.Unreachable() > 0) problems.Add($"{grid.Unreachable()} walkable tiles can't be reached from a dock");
            if (grid.Leaks() > 0) problems.Add($"{grid.Leaks()} walkable tiles touch space");
            return problems;
        }

        private static string Floor(SiteStyle style, SiteZone zone)
        {
            switch (zone)
            {
                case SiteZone.Room: return style.RoomFloor;
                case SiteZone.Corridor:
                case SiteZone.Spoke: return style.CorridorFloor;
                case SiteZone.CoreRoom: return style.CoreFloor;
                case SiteZone.Vault: return style.VaultFloor;
                case SiteZone.DockPod: return style.DockFloor;
                case SiteZone.Door: return style.DoorFloor;
                default: return style.TunnelFloor;
            }
        }

        //A wall that only borders passages cut through rock is a rock wall
        private static bool IsTunnelWall(SiteGrid grid, int x, int y)
        {
            bool tunnel = false;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    switch (grid[x + dx, y + dy])
                    {
                        case SiteZone.Tunnel:
                        case SiteZone.Checkpoint:
                            tunnel = true;
                            break;
                        case SiteZone.Room:
                        case SiteZone.Corridor:
                        case SiteZone.Spoke:
                        case SiteZone.CoreRoom:
                        case SiteZone.Vault:
                        case SiteZone.DockPod:
                            return false;
                    }
                }
            }
            return tunnel;
        }

        private static string Add(SiteLayout layout, string name, float x, float y, float rotation)
        {
            string id = Guid.NewGuid().ToString();
            layout.Items.Add(new SiteItem { Name = name, X = x, Y = y, Rotation = rotation, Id = id });
            return id;
        }

        //A door's 4-digit lock code, the same every time for a seed and door, and never the panel's default of 1234
        public static string DoorPin(int seed, int x, int y)
        {
            int pin = new Random(seed ^ Hash(x, y)).Next(10000);
            if (pin == 1234) pin = 4321;
            return pin.ToString("D4");
        }

        private static int Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 73856093 ^ y * 19349663;
                return h & 0x7FFFFFFF;
            }
        }
    }
}
