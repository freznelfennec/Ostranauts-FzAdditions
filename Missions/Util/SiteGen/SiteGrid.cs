using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //What a tile of a generated site is for. The shape stage lays these out; later stages turn them into rooms and items.
    public enum SiteZone : byte
    {
        Space,
        Rock,       //Solid rock around a black site
        Wall,       //Hull wall
        Corridor,
        Room,       //Area later split into rooms
        Spoke,      //Corridor from the ring to the core
        CoreRoom,   //Core area around the vault: reactor, life support, servers
        Vault,
        DockPod,
        Dock,       //Where a docking port goes, on the hull's outer edge
        Mount,      //Where a weapon goes, on the hull's outer edge
        Tunnel,     //Passage cut through rock
        Checkpoint, //Security chamber on a black site's entry tunnel
        Door,       //A door's portal, in a straight 5-tile wall line across a 3-tile passage (DoorWallTiles)
        Strut       //Solid structure, such as a relay's diagonal spokes: walled through, not walked
    }

    //Which way something on the hull faces, out from the site. Grid y runs down, so North is up the image.
    public enum Facing : byte
    {
        North,
        East,
        South,
        West
    }

    public enum SiteKind
    {
        Relay,
        BlackSite
    }

    public enum SiteSize
    {
        Small,
        Medium,
        Large
    }

    public enum MountKind
    {
        PDC,
        Rail
    }

    public struct SiteDock
    {
        public int X, Y;
        public Facing Facing;
    }

    public struct SiteMount
    {
        public int X, Y;
        public Facing Facing;
        public MountKind Kind;
    }

    //What a room is for, which decides how it's furnished (SiteFurnish)
    public enum RoomRole
    {
        Corridor,
        Equipment,
        Reactor,
        LifeSupport,
        Servers,
        Batteries,
        Vault,
        Security,
        Quarters,
        Galley,
        Storage,
        Armory,
        Lab,
        DockPod,
        Checkpoint
    }

    //A door across a passage: the portal tile, and whether the passage runs north and south (so the door's wall runs east and
    //west) or east and west
    public struct SiteDoor
    {
        public int X, Y;
        public bool PassageNorthSouth;
    }

    //A generated site's tiles, before any items. Pure C#, with nothing from the game, so it can be generated and
    //previewed anywhere. x runs right and y runs down from the top left.
    public class SiteGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly SiteZone[,] Tiles;
        public readonly List<SiteDock> Docks = new List<SiteDock>();
        public readonly List<SiteMount> Mounts = new List<SiteMount>();
        public readonly List<SiteDoor> Doors = new List<SiteDoor>();
        //Roles a generator gives its rooms, each by a tile inside the room; rooms without one get a role from their zone
        public readonly List<(int x, int y, RoomRole role)> RoleHints = new List<(int x, int y, RoomRole role)>();
        public int DoorsMissed;   //Doors a generator asked for that didn't fit
        public readonly List<(int x, int y, int dx, int dy)> DoorMisses = new List<(int x, int y, int dx, int dy)>();

        public SiteKind Kind;
        public SiteSize Size;
        public int Seed;

        public SiteGrid(int width, int height)
        {
            Width = width;
            Height = height;
            Tiles = new SiteZone[width, height];
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public SiteZone this[int x, int y]
        {
            get => InBounds(x, y) ? Tiles[x, y] : SiteZone.Space;
            set
            {
                if (InBounds(x, y)) Tiles[x, y] = value;
            }
        }

        //Tiles people stand on
        public static bool IsWalkable(SiteZone zone)
        {
            switch (zone)
            {
                case SiteZone.Corridor:
                case SiteZone.Room:
                case SiteZone.Spoke:
                case SiteZone.CoreRoom:
                case SiteZone.Vault:
                case SiteZone.DockPod:
                case SiteZone.Dock:
                case SiteZone.Tunnel:
                case SiteZone.Checkpoint:
                case SiteZone.Door:
                    return true;
                default:
                    return false;
            }
        }

        //Tiles inside the hull, walls included
        public static bool IsInterior(SiteZone zone) => zone != SiteZone.Space && zone != SiteZone.Rock;

        private static readonly int[] DX8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DY8 = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly int[] DX4 = { 1, -1, 0, 0 };
        private static readonly int[] DY4 = { 0, 0, 1, -1 };

        //Makes the outer layer of the hull wall: every interior tile touching space or rock, corners included, so the
        //hull is sealed against diagonal gaps. Docks and mounts stay as they are; they're set on the wall afterwards.
        public void BuildHull()
        {
            var walls = new List<(int, int)>();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    SiteZone zone = Tiles[x, y];
                    if (!IsInterior(zone) || zone == SiteZone.Wall || zone == SiteZone.Strut || zone == SiteZone.Dock || zone == SiteZone.Mount) continue;
                    for (int i = 0; i < 8; i++)
                    {
                        SiteZone near = this[x + DX8[i], y + DY8[i]];
                        if (!IsInterior(near) || near == SiteZone.Strut) //A strut is solid, so what it touches is walled off from it too
                        {
                            walls.Add((x, y));
                            break;
                        }
                    }
                }
            }
            foreach (var (x, y) in walls) Tiles[x, y] = SiteZone.Wall;
        }

        //Puts a dock or mount on the hull where a ray from (cx, cy) along the facing leaves the interior, from a
        //starting tile. Returns false if no wall is met.
        public bool PlaceOnHull(int x, int y, Facing facing, Action<int, int> place)
        {
            (int dx, int dy) = Step(facing);
            int lastWallX = -1, lastWallY = -1;
            while (InBounds(x, y))
            {
                if (Tiles[x, y] == SiteZone.Wall)
                {
                    lastWallX = x;
                    lastWallY = y;
                }
                else if (!IsInterior(Tiles[x, y]) && lastWallX >= 0)
                {
                    break;
                }
                x += dx;
                y += dy;
            }
            if (lastWallX < 0) return false;
            place(lastWallX, lastWallY);
            return true;
        }

        //The tiles of a door's wall line, besides its portal: two either side of it, across the passage
        public static IEnumerable<(int x, int y)> DoorWallTiles(SiteDoor door)
        {
            int ax = door.PassageNorthSouth ? 1 : 0, ay = door.PassageNorthSouth ? 0 : 1; //Along the door's wall
            foreach (int a in new[] { -2, -1, 1, 2 }) yield return (door.X + ax * a, door.Y + ay * a);
        }

        //Whether a door fits with its portal at x, y: its wall line is straight and seals the passage, with walls (or
        //struts) two tiles either side of the portal and walkable floor on both sides of it along the passage. The tiles
        //between are the passage itself (3 wide), or a wall the door replaces, such as a vault's.
        public bool DoorFits(int x, int y, bool passageNorthSouth)
        {
            int ax = passageNorthSouth ? 1 : 0, ay = passageNorthSouth ? 0 : 1; //Along the door's wall
            int px = passageNorthSouth ? 0 : 1, py = passageNorthSouth ? 1 : 0; //Along the passage
            foreach (int a in new[] { -2, 2 })
            {
                SiteZone end = this[x + ax * a, y + ay * a];
                if (end != SiteZone.Wall && end != SiteZone.Strut) return false;
            }
            foreach (int a in new[] { -1, 0, 1 })
            {
                SiteZone mid = this[x + ax * a, y + ay * a];
                if (!IsWalkable(mid) && mid != SiteZone.Wall) return false;
                if (mid == SiteZone.Door || mid == SiteZone.Dock) return false;
            }
            return IsWalkable(this[x - px, y - py]) && IsWalkable(this[x + px, y + py]);
        }

        //Puts a door with its portal at x, y if it fits: the portal becomes Door and the tiles either side of it Wall,
        //for the door item to bring
        public bool PlaceDoor(int x, int y, bool passageNorthSouth)
        {
            if (!DoorFits(x, y, passageNorthSouth)) return false;
            var door = new SiteDoor { X = x, Y = y, PassageNorthSouth = passageNorthSouth };
            foreach (var (wx, wy) in DoorWallTiles(door)) Tiles[wx, wy] = SiteZone.Wall;
            Tiles[x, y] = SiteZone.Door;
            Doors.Add(door);
            return true;
        }

        //Puts a door across a passage at the first place it fits, stepping along the passage (dx, dy) from x, y for up
        //to steps tiles
        public bool PlaceDoorAlong(int x, int y, int dx, int dy, int steps)
        {
            for (int i = 0; i <= steps; i++)
            {
                if (PlaceDoor(x + dx * i, y + dy * i, dx == 0)) return true;
            }
            DoorsMissed++;
            DoorMisses.Add((x, y, dx, dy));
            return false;
        }

        public static (int dx, int dy) Step(Facing facing)
        {
            switch (facing)
            {
                case Facing.North: return (0, -1);
                case Facing.East: return (1, 0);
                case Facing.South: return (0, 1);
                default: return (-1, 0);
            }
        }

        //The cardinal facing nearest a direction, for things that can only turn in quarter turns
        public static Facing Nearest(double dx, double dy)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? Facing.East : Facing.West;
            return dy >= 0 ? Facing.South : Facing.North;
        }

        public int Count(SiteZone zone)
        {
            int count = 0;
            foreach (SiteZone tile in Tiles)
            {
                if (tile == zone) count++;
            }
            return count;
        }

        //Which tiles can be reached on foot from a dock, through edge-sharing walkable tiles
        public bool[,] Reachable()
        {
            var seen = new bool[Width, Height];
            var queue = new Queue<(int, int)>();
            foreach (SiteDock dock in Docks)
            {
                seen[dock.X, dock.Y] = true;
                queue.Enqueue((dock.X, dock.Y));
            }
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + DX4[i], ny = y + DY4[i];
                    if (!InBounds(nx, ny) || seen[nx, ny] || !IsWalkable(Tiles[nx, ny])) continue;
                    seen[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
            return seen;
        }

        //Walkable tiles that can't be reached on foot from any dock
        public int Unreachable()
        {
            bool[,] seen = Reachable();
            int unreachable = 0;
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (IsWalkable(Tiles[x, y]) && !seen[x, y]) unreachable++;
                }
            }
            return unreachable;
        }

        //Interior tiles next to space with no wall between, which would leak air
        public int Leaks()
        {
            int leaks = 0;
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (!IsWalkable(Tiles[x, y]) || Tiles[x, y] == SiteZone.Dock) continue;
                    for (int i = 0; i < 8; i++)
                    {
                        if (!IsInterior(this[x + DX8[i], y + DY8[i]]))
                        {
                            leaks++;
                            break;
                        }
                    }
                }
            }
            return leaks;
        }

        //A rough count of the items the layout will need: a floor and something more on every walkable tile, a wall on
        //every wall tile, and rock on rock
        public int EstimatedItems()
        {
            int walkable = 0, walls = 0, rock = 0;
            foreach (SiteZone tile in Tiles)
            {
                if (IsWalkable(tile)) walkable++;
                else if (tile == SiteZone.Wall || tile == SiteZone.Mount) walls++;
                else if (tile == SiteZone.Rock) rock++;
            }
            return (int)(walkable * 1.8) + walls + rock;
        }

        public string Summary()
        {
            var counts = Enum.GetValues(typeof(SiteZone)).Cast<SiteZone>().Where(z => z != SiteZone.Space)
                .Select(z => (z, Count(z))).Where(c => c.Item2 > 0).Select(c => $"{c.z} {c.Item2}");
            return $"{Kind} {Size}, seed {Seed}: {Width}x{Height}, {Docks.Count} docks, "
                + $"{Mounts.Count(m => m.Kind == MountKind.PDC)} PDC and {Mounts.Count(m => m.Kind == MountKind.Rail)} rail mounts, {Doors.Count} doors, "
                + $"about {EstimatedItems()} items, {Unreachable()} unreachable and {Leaks()} leaking tiles\n  "
                + string.Join(", ", counts);
        }
    }
}
