using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //A black site: rooms scattered through a rock, joined by corridors cut through it, with one way in.
    //
    //The rock is a blob whose edge wanders with a few random harmonics, generated here from the seed rather than by
    //the game's asteroid generator, which draws on Unity's shared random state and so couldn't be repeated from a seed.
    //
    //Rooms are walled rectangles of varied sizes, placed at random where they fit in the rock with rock between them.
    //Corridors are cut through the rock to join them: a spanning tree, so every room is reached, plus a few extra
    //corridors that make loops. Each runs in straight legs with right-angle turns (an L or a Z), leaving and entering
    //rooms square to a wall, so it's 3 wide inside straight walls and a door fits where it meets each room. The room
    //farthest from the entrance on a dead end of the corridors is the vault, on a single corridor; the rooms nearest it are the core (reactor, life
    //support, servers). A straight entry tunnel runs from a room near the rock's edge out to the only dock, through one
    //or two checkpoint chambers, and weapon stubs run straight out from rooms near the surface.
    //
    //A room no corridor can reach is turned back into rock. A layout that leaves anything unreachable, leaking or a
    //door that doesn't fit is made again from the next variant of the seed, so the same seed always gives the same site.
    public static class BlackSiteShape
    {
        private const int StubHeadHalf = 5; //A stub's end is 11 tiles across, walls included: two weapon slots 4 apart

        private class Spec
        {
            public int RockRadius;
            public int Rooms;
            public int MaxRoomSide;   //Inside its walls
            public int ExtraCorridors;
            public int CoreRooms;
            public int Stubs;
        }

        private class Room
        {
            public int X0, Y0, X1, Y1; //Inside its walls
            public SiteZone Zone = SiteZone.Room;
            public int CX => (X0 + X1) / 2;
            public int CY => (Y0 + Y1) / 2;
            public bool Contains(int x, int y, int margin) => x >= X0 - margin && x <= X1 + margin && y >= Y0 - margin && y <= Y1 + margin;
        }

        private const int Margin = 3;
        private const int Half = 2;            //Corridors and tunnels are 5 wide with their walls, 3 to walk, a door's width
        private const int CheckpointHalf = 3;  //Checkpoint chambers are 7 tiles square
        private const int Leg = 5;             //Straight run a corridor keeps from a room's wall before turning
        private const int RoomGap = 7;         //Rock between rooms, room for a corridor between them
        private const int MinRoomSide = 6;   //Rooms are clearly bigger than the 3-wide corridors joining them
        private const int Attempts = 20;
        private const int RouteTries = 8;      //Variations tried for each corridor shape
        private const int MaxCrossing = 11;    //Cells of a corridor that may come near another: one square crossing of a 5-wide corridor

        //Why the last attempt that failed outright gave up, for the console and the preview tool
        public static string LastFailure { get; private set; }
        public static readonly List<string> Failures = new List<string>();

        public static SiteGrid Generate(SiteSize size, int seed)
        {
            SiteGrid best = null;
            Failures.Clear();
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                SiteGrid grid = TryGenerate(size, seed, attempt);
                if (grid == null) continue;
                if (grid.Unreachable() == 0 && grid.Leaks() == 0 && grid.DoorsMissed == 0) return grid;
                if (best == null) best = grid;
            }
            return best ?? TryGenerate(size, seed, 0);
        }

        private static SiteGrid TryGenerate(SiteSize size, int seed, int attempt)
        {
            var random = new Random(seed + attempt * 7919);
            Spec spec = MakeSpec(size, random);

            //The rock's edge, as a radius for each direction: the base radius with three random harmonics on it
            var harmonics = new List<(int n, double amp, double phase)>();
            for (int i = 0; i < 3; i++) harmonics.Add((2 + i + random.Next(2), 0.03 + random.NextDouble() * 0.04, random.NextDouble() * Math.PI * 2.0));
            double Edge(double angle)
            {
                double r = 1.0;
                foreach (var (n, amp, phase) in harmonics) r += amp * Math.Sin(n * angle + phase);
                return spec.RockRadius * r;
            }

            int extent = 2 * ((int)(spec.RockRadius * 1.25) + Margin) + 1;
            var grid = new SiteGrid(extent, extent) { Kind = SiteKind.BlackSite, Size = size, Seed = seed };
            int c = extent / 2;
            for (int x = 0; x < extent; x++)
            {
                for (int y = 0; y < extent; y++)
                {
                    double dx = x - c, dy = y - c;
                    if (Math.Sqrt(dx * dx + dy * dy) <= Edge(Math.Atan2(dy, dx))) grid[x, y] = SiteZone.Rock;
                }
            }

            //Rooms, wherever one fits in the rock with rock round it and a gap from every other room
            var rooms = new List<Room>();
            double placeRadius = spec.RockRadius * 0.72;
            for (int tries = 0; tries < 2000 && rooms.Count < spec.Rooms; tries++)
            {
                int w = MinRoomSide + random.Next(spec.MaxRoomSide - MinRoomSide + 1);
                int h = MinRoomSide + random.Next(spec.MaxRoomSide - MinRoomSide + 1);
                double angle = random.NextDouble() * Math.PI * 2.0, dist = Math.Sqrt(random.NextDouble()) * placeRadius;
                int x0 = c + (int)(Math.Cos(angle) * dist) - w / 2, y0 = c + (int)(Math.Sin(angle) * dist) - h / 2;
                var room = new Room { X0 = x0, Y0 = y0, X1 = x0 + w - 1, Y1 = y0 + h - 1 };
                if (!AllRock(grid, room, 1 + Margin)) continue;
                if (rooms.Any(o => Overlaps(o, room, 1 + RoomGap))) continue;
                rooms.Add(room);
            }
            if (rooms.Count < 4) return Fail($"only {rooms.Count} rooms fitted");
            foreach (Room room in rooms) WriteRoom(grid, room);

            //The entry: a straight tunnel from a room's wall out of the rock, on the side the seed picks. The room
            //reaching furthest that way whose tunnel crosses no other room is used, preferring a tunnel long enough for a
            //checkpoint.
            Facing entrance = (Facing)random.Next(4);
            (int ox, int oy) = SiteGrid.Step(entrance);
            Room entry = null;
            (int x, int y) entryDoor = default;
            int tunnelLength = -1;
            foreach (Room room in rooms.OrderByDescending(r => Reach(r, ox, oy)))
            {
                var door = SideMiddle(room, entrance);
                int length = StraightOut(grid, rooms, room, door, ox, oy, CheckpointHalf);
                if (length < 0) continue;
                if (entry == null || (tunnelLength < 14 && length > tunnelLength))
                {
                    entry = room;
                    entryDoor = door;
                    tunnelLength = length;
                }
                if (tunnelLength >= 14) break;
            }
            if (entry == null) return Fail("no room had a clear way out for the entry tunnel");

            var doors = new List<(int x, int y, int dx, int dy)>();
            //The entry tunnel, with one or two checkpoints along it, ending in a short pod out of the rock so its end
            //wall is straight for the dock. It's cut before the corridors, which then route round it, and its door is kept
            //clear of theirs.
            int tunnelEnd = tunnelLength; //Tiles from the entry room's wall to the first tile past the rock
            Carve(grid, entryDoor.x + ox, entryDoor.y + oy, ox, oy, tunnelEnd + 1, Half, SiteZone.Tunnel, overwrite: false);
            int[] checkpoints = tunnelEnd >= 3 * (2 * CheckpointHalf + 4)
                ? new[] { tunnelEnd / 3, 2 * tunnelEnd / 3 }
                : tunnelEnd >= 2 * CheckpointHalf + 6 ? new[] { tunnelEnd / 2 } : new int[0];
            foreach (int at in checkpoints)
            {
                Carve(grid, entryDoor.x + ox * (at - CheckpointHalf), entryDoor.y + oy * (at - CheckpointHalf), ox, oy, 2 * CheckpointHalf + 1, CheckpointHalf, SiteZone.Checkpoint, overwrite: true);
            }
            Carve(grid, entryDoor.x + ox * tunnelEnd, entryDoor.y + oy * tunnelEnd, ox, oy, 3, 3, SiteZone.DockPod, overwrite: true);
            doors.Add((entryDoor.x, entryDoor.y, ox, oy));

            //Corridors: a spanning tree over the rooms, shortest corridors first. A room no corridor can reach is turned
            //back into rock.
            var connected = new HashSet<Room> { entry };
            var corridors = rooms.ToDictionary(r => r, r => 0);
            while (connected.Count < rooms.Count)
            {
                bool joined = false;
                var pairs = rooms.Where(connected.Contains)
                    .SelectMany(a => rooms.Where(b => !connected.Contains(b)).Select(b => (a, b)))
                    .OrderBy(p => Distance(p.a, p.b)).ToList();
                foreach (var (from, to) in pairs)
                {
                    if (!TryCorridor(grid, rooms, from, to, random, doors)) continue;
                    connected.Add(to);
                    corridors[from]++;
                    corridors[to]++;
                    joined = true;
                    break;
                }
                if (joined) continue;
                foreach (Room lost in rooms.Where(r => !connected.Contains(r)).ToList())
                {
                    FillRock(grid, lost);
                    rooms.Remove(lost);
                }
            }
            if (rooms.Count < 4) return Fail($"only {rooms.Count} rooms could be joined up");

            //The vault is the farthest room from the entry on a dead end of the tree, so it has a single corridor, and
            //the core the rooms nearest it
            Room vault = rooms.Where(r => r != entry && corridors[r] == 1).OrderByDescending(r => Distance(r, entry)).FirstOrDefault();
            if (vault == null) return Fail("no dead end for the vault");
            vault.Zone = SiteZone.Vault;
            foreach (Room room in rooms.Where(r => r != entry && r != vault).OrderBy(r => Distance(r, vault)).Take(spec.CoreRooms)) room.Zone = SiteZone.CoreRoom;
            foreach (Room room in rooms) WriteRoom(grid, room);
            AssignRoles(grid, rooms, entry, vault, random);

            //A few extra corridors between nearby rooms for loops, never to the vault
            int extra = 0;
            var others = rooms.Where(r => r != vault).ToList();
            var loops = new List<(Room a, Room b)>();
            for (int i = 0; i < others.Count; i++)
            {
                for (int j = i + 1; j < others.Count; j++) loops.Add((others[i], others[j]));
            }
            foreach (var (a, b) in loops.OrderBy(p => Distance(p.a, p.b)))
            {
                if (extra >= spec.ExtraCorridors) break;
                if (Distance(a, b) > spec.RockRadius) continue;
                if (TryCorridor(grid, rooms, a, b, random, doors)) extra++;
            }

            //Weapon stubs: straight out from rooms near the surface, never the vault or the entry, each on its own side
            var stubs = new List<(int x, int y, Facing facing)>();
            var usedFacings = new HashSet<Facing> { entrance };
            var stubOptions = new List<(Room room, Facing facing, (int x, int y) door, int length)>();
            foreach (Room room in rooms.Where(r => r != vault && r != entry))
            {
                foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    var door = SideMiddle(room, facing);
                    (int sx, int sy) = SiteGrid.Step(facing);
                    int length = StraightOut(grid, rooms, room, door, sx, sy, Half);
                    if (length > 0 && DoorClear(doors, door.x, door.y)) stubOptions.Add((room, facing, door, length));
                }
            }
            foreach (var (room, facing, door, length) in stubOptions.OrderBy(o => o.length))
            {
                if (stubs.Count >= spec.Stubs) break;
                if (usedFacings.Contains(facing)) continue;
                (int sx, int sy) = SiteGrid.Step(facing);
                Carve(grid, door.x + sx, door.y + sy, sx, sy, length + 1, Half, SiteZone.Tunnel, overwrite: false);
                Carve(grid, door.x + sx * length, door.y + sy * length, sx, sy, 3, StubHeadHalf, SiteZone.Tunnel, overwrite: true); //3 deep, so a row of floor runs between its walls
                doors.Add((door.x, door.y, sx, sy));
                stubs.Add((door.x + sx * (length - 1), door.y + sy * (length - 1), facing));
                usedFacings.Add(facing);
            }

            grid.BuildHull();

            //The one dock, on the end of the entry pod
            grid.PlaceOnHull(entryDoor.x + ox * (tunnelEnd - 1), entryDoor.y + oy * (tunnelEnd - 1), entrance, (x, y) =>
            {
                grid[x, y] = SiteZone.Dock;
                grid.Docks.Add(new SiteDock { X = x, Y = y, Facing = entrance });
            });

            //Doors: where every corridor, the tunnel and each stub meet a room, at both ends of each checkpoint, and across
            //the tunnel behind the dock
            foreach (var (x, y, dx, dy) in doors) grid.PlaceDoorAlong(x, y, dx, dy, 0);
            foreach (int at in checkpoints)
            {
                grid.PlaceDoorAlong(entryDoor.x + ox * (at - CheckpointHalf), entryDoor.y + oy * (at - CheckpointHalf), -ox, -oy, 1);
                grid.PlaceDoorAlong(entryDoor.x + ox * (at + CheckpointHalf), entryDoor.y + oy * (at + CheckpointHalf), ox, oy, 1);
            }
            grid.PlaceDoorAlong(entryDoor.x + ox * (tunnelEnd - 1), entryDoor.y + oy * (tunnelEnd - 1), -ox, -oy, 4);

            //Weapons across the flat end of each stub: two slots, 4 apart, so each has a 3-tile run of hull with floor
            //behind it. The first two stubs take a rail gun in one slot; the rest are PDCs.
            int rails = 0;
            foreach (var (x, y, facing) in stubs)
            {
                (int sx, int sy) = SiteGrid.Step(facing);
                foreach (int w in new[] { -2, 2 })
                {
                    MountKind kind = rails < 2 && w < 0 ? MountKind.Rail : MountKind.PDC;
                    grid.PlaceOnHull(x + sy * w, y + sx * w, facing, (hx, hy) =>
                    {
                        if (grid[hx, hy] != SiteZone.Wall) return;
                        grid[hx, hy] = SiteZone.Mount;
                        grid.Mounts.Add(new SiteMount { X = hx, Y = hy, Facing = facing, Kind = kind });
                        if (kind == MountKind.Rail) rails++;
                    });
                }
            }

            return grid;
        }

        //Black sites are staffed: the entry room is the security post, the core rooms the reactor, life support and
        //servers nearest the vault first, and the rest give the staff somewhere to sleep and eat before anything else
        private static void AssignRoles(SiteGrid grid, List<Room> rooms, Room entry, Room vault, Random random)
        {
            var coreRoles = new[] { RoomRole.Reactor, RoomRole.LifeSupport, RoomRole.Servers };
            //Core roles the core rooms didn't cover come first, then quarters and a galley
            int coreRooms = rooms.Count(r => r.Zone == SiteZone.CoreRoom && r != vault && r != entry);
            var firstRoles = coreRoles.Skip(coreRooms).Concat(new[] { RoomRole.Quarters, RoomRole.Galley }).ToArray();
            var moreRoles = new[] { RoomRole.Quarters, RoomRole.Storage, RoomRole.Armory, RoomRole.Lab, RoomRole.Storage };
            int core = 0, other = 0;
            foreach (Room room in rooms.OrderBy(r => Distance(r, vault)))
            {
                RoomRole role;
                if (room == vault) role = RoomRole.Vault;
                else if (room == entry) role = RoomRole.Security;
                else if (room.Zone == SiteZone.CoreRoom) role = coreRoles[core++ % coreRoles.Length];
                else role = other < firstRoles.Length ? firstRoles[other++] : moreRoles[random.Next(moreRoles.Length)];
                grid.RoleHints.Add((room.CX, room.CY, role));
            }
        }

        private static SiteGrid Fail(string reason)
        {
            LastFailure = reason;
            Failures.Add(reason);
            return null;
        }

        //A corridor between two rooms, as an L or a Z of straight legs, square to a wall of each, through rock alone.
        //The first shape that fits is carved, and its two doors noted. Its door on each room keeps clear of that room's
        //other doors.
        private static bool TryCorridor(SiteGrid grid, List<Room> rooms, Room a, Room b, Random random, List<(int x, int y, int dx, int dy)> doors)
        {
            //Several tries of each shape: the first aims each door at the other room, later ones put the doors anywhere
            //along the walls and the Z's turn anywhere between its legs
            var shapes = new List<List<(int x, int y)>>();
            for (int k = 0; k < RouteTries; k++)
            {
                bool aimed = k == 0;
                int AimY(Room r, Room at) => aimed ? Pick(r.Y0, r.Y1, at.CY, random) : Pick(r.Y0, r.Y1, random.Next(r.Y0, r.Y1 + 1), random);
                int AimX(Room r, Room at) => aimed ? Pick(r.X0, r.X1, at.CX, random) : Pick(r.X0, r.X1, random.Next(r.X0, r.X1 + 1), random);
                int Between(int from, int to) => aimed ? (from + to) / 2 : Math.Min(from, to) + Leg + random.Next(Math.Max(1, Math.Abs(to - from) - 2 * Leg + 1));

                //Z shapes: out of a's side facing b, across, and into b's facing side
                if (b.X0 - a.X1 >= 2 * Leg + 2 || a.X0 - b.X1 >= 2 * Leg + 2)
                {
                    int dir = b.X0 > a.X1 ? 1 : -1;
                    int ya = AimY(a, b), yb = AimY(b, a);
                    int xa = dir > 0 ? a.X1 + 1 : a.X0 - 1, xb = dir > 0 ? b.X0 - 1 : b.X1 + 1;
                    int mx = Between(xa, xb);
                    shapes.Add(new List<(int, int)> { (xa, ya), (mx, ya), (mx, yb), (xb, yb) });
                }
                if (b.Y0 - a.Y1 >= 2 * Leg + 2 || a.Y0 - b.Y1 >= 2 * Leg + 2)
                {
                    int dir = b.Y0 > a.Y1 ? 1 : -1;
                    int xa = AimX(a, b), xb = AimX(b, a);
                    int ya = dir > 0 ? a.Y1 + 1 : a.Y0 - 1, yb = dir > 0 ? b.Y0 - 1 : b.Y1 + 1;
                    int my = Between(ya, yb);
                    shapes.Add(new List<(int, int)> { (xa, ya), (xa, my), (xb, my), (xb, yb) });
                }
                //L shapes: out of a sideways and into b from above or below, and the other way round
                {
                    int dir = b.CX > a.CX ? 1 : -1;
                    int xa = dir > 0 ? a.X1 + 1 : a.X0 - 1;
                    int ya = AimY(a, b);
                    int xb = AimX(b, a);
                    int yb = ya < b.Y0 ? b.Y0 - 1 : b.Y1 + 1;
                    if ((xb - xa) * dir >= Leg + 1 && Math.Abs(yb - ya) >= Leg + 1 && (ya < b.Y0 || ya > b.Y1)) shapes.Add(new List<(int, int)> { (xa, ya), (xb, ya), (xb, yb) });
                }
                {
                    int dir = b.CY > a.CY ? 1 : -1;
                    int ya = dir > 0 ? a.Y1 + 1 : a.Y0 - 1;
                    int xa = AimX(a, b);
                    int yb = AimY(b, a);
                    int xb = xa < b.X0 ? b.X0 - 1 : b.X1 + 1;
                    if ((yb - ya) * dir >= Leg + 1 && Math.Abs(xb - xa) >= Leg + 1 && (xa < b.X0 || xa > b.X1)) shapes.Add(new List<(int, int)> { (xa, ya), (xa, yb), (xb, yb) });
                }
            }
            //The aimed tries first, in a random order, then the rest
            var firstTries = shapes.Take(4).ToList();
            var laterTries = shapes.Skip(4).ToList();
            Shuffle(firstTries, random);
            Shuffle(laterTries, random);
            shapes = firstTries.Concat(laterTries).ToList();

            foreach (List<(int x, int y)> path in shapes)
            {
                var (sx, sy) = path[0];
                var (ex, ey) = path[path.Count - 1];
                if (!DoorClear(doors, sx, sy) || !DoorClear(doors, ex, ey)) continue;
                var cells = PathCells(path);
                if (!Fits(grid, rooms, cells, a, b)) continue;
                foreach (var (x, y) in cells)
                {
                    for (int dx = -Half; dx <= Half; dx++)
                    {
                        for (int dy = -Half; dy <= Half; dy++)
                        {
                            if (grid[x + dx, y + dy] == SiteZone.Rock) grid[x + dx, y + dy] = SiteZone.Corridor;
                        }
                    }
                }
                var (fx, fy) = path[1];
                doors.Add((sx, sy, Math.Sign(fx - sx), Math.Sign(fy - sy)));
                var (lx, ly) = path[path.Count - 2];
                doors.Add((ex, ey, Math.Sign(ex - lx), Math.Sign(ey - ly)));
                return true;
            }
            return false;
        }

        //The tiles along a corridor's centre line, leaving out its two ends, which are doors in room walls
        private static List<(int x, int y)> PathCells(List<(int x, int y)> path)
        {
            var cells = new List<(int, int)>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                var (x0, y0) = path[i];
                var (x1, y1) = path[i + 1];
                int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
                int x = x0, y = y0;
                while (true)
                {
                    if (!cells.Contains((x, y))) cells.Add((x, y));
                    if (x == x1 && y == y1) break;
                    x += dx;
                    y += dy;
                }
            }
            cells.RemoveAt(0);
            cells.RemoveAt(cells.Count - 1);
            return cells;
        }

        //Whether a corridor along these cells, 5 wide, stays clear of every other room and its walls and of space. It may
        //cut through rock, and cross one other corridor. The two rooms it joins are left out: its legs meet them square, from their doors.
        private static bool Fits(SiteGrid grid, List<Room> rooms, List<(int x, int y)> cells, Room a, Room b)
        {
            int nearCorridor = 0; //Cells that come near another corridor
            foreach (var (x, y) in cells)
            {
                foreach (Room room in rooms)
                {
                    if (room != a && room != b && room.Contains(x, y, 1 + Half)) return false;
                }
                bool near = false;
                for (int dx = -Half - 1; dx <= Half + 1; dx++)
                {
                    for (int dy = -Half - 1; dy <= Half + 1; dy++)
                    {
                        if (a.Contains(x + dx, y + dy, 1) || b.Contains(x + dx, y + dy, 1)) continue;
                        SiteZone zone = grid[x + dx, y + dy];
                        if (zone == SiteZone.Corridor) near = true;
                        else if (zone != SiteZone.Rock && zone != SiteZone.Wall) return false;
                    }
                }
                if (near) nearCorridor++;
            }
            //It may cross another corridor once, square on, but not run alongside one, which would merge them into a wide
            //open area
            return nearCorridor <= MaxCrossing;
        }

        //How far a straight tunnel from a room's wall runs out through rock to clear the rock's edge, or -1 if it would
        //cross another room or a corridor on the way. halfWidth is how far either side of its line it has to keep clear.
        private static int StraightOut(SiteGrid grid, List<Room> rooms, Room from, (int x, int y) door, int dx, int dy, int halfWidth)
        {
            for (int t = 1; t < grid.Width; t++)
            {
                int x = door.x + dx * t, y = door.y + dy * t;
                bool clear = true;
                for (int w = -halfWidth - 1; w <= halfWidth + 1; w++)
                {
                    int cx = x + dy * w, cy = y + dx * w;
                    SiteZone zone = grid[cx, cy];
                    if (zone == SiteZone.Rock) clear = false;
                    else if (zone != SiteZone.Space) return -1;
                    if (rooms.Any(r => r != from && r.Contains(cx, cy, 1))) return -1;
                }
                if (clear) return t;
            }
            return -1;
        }

        //Carves a straight run of length tiles from x, y along dx, dy, halfWidth either side of its line
        private static void Carve(SiteGrid grid, int x, int y, int dx, int dy, int length, int halfWidth, SiteZone zone, bool overwrite)
        {
            for (int t = 0; t < length; t++)
            {
                for (int w = -halfWidth; w <= halfWidth; w++)
                {
                    int cx = x + dx * t + dy * w, cy = y + dy * t + dx * w;
                    SiteZone here = grid[cx, cy];
                    if (here == SiteZone.Rock || here == SiteZone.Space || (overwrite && (here == SiteZone.Tunnel || here == SiteZone.Corridor))) grid[cx, cy] = zone;
                }
            }
        }

        private static void FillRock(SiteGrid grid, Room room)
        {
            for (int x = room.X0 - 1; x <= room.X1 + 1; x++)
            {
                for (int y = room.Y0 - 1; y <= room.Y1 + 1; y++) grid[x, y] = SiteZone.Rock;
            }
        }

        private static void WriteRoom(SiteGrid grid, Room room)
        {
            for (int x = room.X0 - 1; x <= room.X1 + 1; x++)
            {
                for (int y = room.Y0 - 1; y <= room.Y1 + 1; y++)
                {
                    bool wall = x < room.X0 || x > room.X1 || y < room.Y0 || y > room.Y1;
                    grid[x, y] = wall ? SiteZone.Wall : room.Zone;
                }
            }
        }

        //A point on the middle of a room's wall on one side, where a door fits
        private static (int x, int y) SideMiddle(Room room, Facing side)
        {
            switch (side)
            {
                case Facing.North: return (room.CX, room.Y0 - 1);
                case Facing.South: return (room.CX, room.Y1 + 1);
                case Facing.East: return (room.X1 + 1, room.CY);
                default: return (room.X0 - 1, room.CY);
            }
        }

        //A point along a room's side, aimed at a target but kept a tile from each corner so a door fits
        private static int Pick(int lo, int hi, int target, Random random)
        {
            int min = lo + 1, max = hi - 1;
            if (max < min) return (lo + hi) / 2;
            return Math.Max(min, Math.Min(max, target + random.Next(-1, 2)));
        }

        //A new door has to be 5 tiles from any other, so their wall lines don't overlap
        private static bool DoorClear(List<(int x, int y, int dx, int dy)> doors, int x, int y)
        {
            return doors.All(d => Math.Abs(d.x - x) + Math.Abs(d.y - y) >= 5);
        }

        private static bool AllRock(SiteGrid grid, Room room, int margin)
        {
            for (int x = room.X0 - margin; x <= room.X1 + margin; x++)
            {
                for (int y = room.Y0 - margin; y <= room.Y1 + margin; y++)
                {
                    if (grid[x, y] != SiteZone.Rock) return false;
                }
            }
            return true;
        }

        private static bool Overlaps(Room a, Room b, int gap)
        {
            return a.X0 - gap <= b.X1 && b.X0 - gap <= a.X1 && a.Y0 - gap <= b.Y1 && b.Y0 - gap <= a.Y1;
        }

        private static int Reach(Room room, int dx, int dy) => dx > 0 ? room.X1 : dx < 0 ? -room.X0 : dy > 0 ? room.Y1 : -room.Y0;

        private static double Distance(Room a, Room b)
        {
            double dx = a.CX - b.CX, dy = a.CY - b.CY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Spec MakeSpec(SiteSize size, Random random)
        {
            Spec spec;
            switch (size)
            {
                case SiteSize.Small:
                    spec = new Spec { RockRadius = 29, Rooms = 6, MaxRoomSide = 10, ExtraCorridors = 1, CoreRooms = 1, Stubs = 2 };
                    break;
                case SiteSize.Medium:
                    spec = new Spec { RockRadius = 36, Rooms = 8, MaxRoomSide = 12, ExtraCorridors = 2, CoreRooms = 2, Stubs = 3 };
                    break;
                default:
                    spec = new Spec { RockRadius = 42, Rooms = 11, MaxRoomSide = 14, ExtraCorridors = 3, CoreRooms = 3, Stubs = 3 };
                    break;
            }
            spec.RockRadius += random.Next(-1, 3);
            return spec;
        }

        private static void Shuffle<T>(List<T> list, Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
