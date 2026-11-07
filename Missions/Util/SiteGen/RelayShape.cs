using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //A corporate relay station: an outer docking ring, long spokes and a small central core with the vault at its heart.
    //Relays are unmanned, so they only need room for a reactor, weapons, data vaults, sensors and minimal life support:
    //the ring is tight, and most of the station is the empty space between it and the core.
    //
    //The ring is an annulus tested on tile centres, so its hull is a stepped circle, as on the base game's round
    //layouts. Its inner band is the ring corridor, with one band of equipment rooms outside it, against the hull where the
    //weapons and sensors go. Walkable spokes run on the cardinal axes from the ring corridor to the core, so a dock, its
    //spoke and the core line up and every spoke is a chokepoint. Large relays add diagonal spokes as solid struts, for the
    //look: a door can't sit across a diagonal passage, so they aren't walked. Docks sit on straight pods out from the ring,
    //since a docking port has to face a cardinal direction from a straight outer edge. Between pods, the seed adds
    //equipment modules to vary the silhouette. Weapons go on the ring's outer hull: rail guns beside the cardinal docks,
    //facing out with them, and PDCs spread round between.
    //
    //Everything is laid out for doors, which need a straight 5-tile wall line across a 3-tile passage (SiteGrid.DoorFits).
    //Spokes, pods and the vault corridor are 3 wide inside their walls, and the vault is a walled square. Doors are put
    //across each pod where it's clear of the ring's curve, across each spoke at both ends, and on one side of the vault.
    //Rooms (milestone 3) are to be axis-aligned rectangles, so their walls are straight for hatches too.
    public static class RelayShape
    {
        private class Spec
        {
            public int Radius;        //Ring's outer radius in tiles
            public int RoomBand;      //Depth of the band of equipment rooms outside the ring corridor
            public int CorridorWidth;
            public int CoreRadius;
            public int VaultRadius;   //Half the vault square's side, inside its walls
            public int SpokeWidth;
            public bool DiagonalSpokes;
            public Facing[] CardinalDocks;
            public bool DiagonalDocks;
            public int PDCs;
        }

        private struct Pod
        {
            public int StartX, StartY;  //Where it starts, inside the ring
            public Facing Facing;
            public int DoorX, DoorY;    //Its first row clear of the ring
        }

        private const int PodReach = 8;      //Most tiles a pod reaches out past the ring
        private const int PodHalfWidth = 2;  //A pod is 5 tiles across, walls included, the docking port's and a door's width
        private const int BayHalfWidth = 3;  //A weapon bay is 7 tiles across, walls included, room for a weapon 5 wide
        private const int Margin = 3;        //Space left round the edge of the grid

        public static SiteGrid Generate(SiteSize size, int seed)
        {
            var random = new Random(seed);
            Spec spec = MakeSpec(size, random);

            int reach = spec.Radius + PodReach; //Pods and equipment modules stick out past the ring
            int extent = 2 * (reach + Margin) + 1;
            var grid = new SiteGrid(extent, extent) { Kind = SiteKind.Relay, Size = size, Seed = seed };
            int c = extent / 2;

            int ringOut = spec.Radius;
            int ringIn = ringOut - (spec.RoomBand + spec.CorridorWidth) - 2; //Walls on both faces
            double corridorIn = ringIn + 0.5; //The corridor runs along the ring's inner wall, whose tiles BuildHull turns to wall
            double corridorOut = corridorIn + spec.CorridorWidth;

            //Ring and core by distance from the centre. In the core, the vault is a square, walled all round, inside a
            //square corridor 3 wide, so the walls a door goes on are straight.
            int vault = spec.VaultRadius;
            for (int x = 0; x < extent; x++)
            {
                for (int y = 0; y < extent; y++)
                {
                    int dx = x - c, dy = y - c;
                    double d = Distance(dx, dy);
                    int square = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (square <= vault) grid[x, y] = SiteZone.Vault;
                    else if (square == vault + 1) grid[x, y] = SiteZone.Wall;
                    else if (square <= vault + 4) grid[x, y] = SiteZone.Corridor;
                    else if (square == vault + 5 && d <= spec.CoreRadius) grid[x, y] = SiteZone.Wall; //The corridor's outer wall, closing off the core rooms
                    else if (d <= spec.CoreRadius) grid[x, y] = SiteZone.CoreRoom;
                    else if (d >= ringIn - 0.5 && d <= ringOut + 0.5)
                    {
                        grid[x, y] = d >= corridorIn && d < corridorOut ? SiteZone.Corridor : SiteZone.Room;
                    }
                }
            }

            //Cardinal spokes, from the vault corridor out to the ring corridor: 3 wide in the core, where they run
            //between rooms, and 5 wide with their walls across the gap. Diagonal struts, 3 thick, from the core to the ring.
            int spokeStart = vault + 5;
            double half = spec.SpokeWidth / 2.0;
            double s = Math.Sqrt(0.5);
            var cardinals = new[] { (0.0, -1.0), (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0) };
            var diagonals = new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) };
            for (int x = 0; x < extent; x++)
            {
                for (int y = 0; y < extent; y++)
                {
                    double dx = x - c, dy = y - c;
                    foreach ((double ux, double uy) in cardinals)
                    {
                        double along = dx * ux + dy * uy;
                        double across = Math.Abs(dx * uy - dy * ux);
                        if (along < spokeStart || along > corridorIn + 1 || across > half) continue;
                        if (Distance(dx, dy) > spec.CoreRadius) SetSpoke(grid, x, y);
                        else if (across <= 1.5) grid[x, y] = SiteZone.Spoke; //Through the core, walled off from its rooms
                        else if (grid[x, y] == SiteZone.CoreRoom) grid[x, y] = SiteZone.Wall;
                    }
                    if (!spec.DiagonalSpokes) continue;
                    foreach ((int sx, int sy) in diagonals)
                    {
                        double along = (dx * sx + dy * sy) * s;
                        double across = Math.Abs(dx * sy - dy * sx) * s;
                        if (along >= spec.CoreRadius - 1 && along <= corridorIn && across <= 1.5 && grid[x, y] == SiteZone.Space)
                        {
                            grid[x, y] = SiteZone.Strut;
                        }
                    }
                }
            }

            //Docking pods: on the cardinal points, and on large relays the diagonals, where a pod runs out along the
            //axis the seed picks
            var pods = new List<Pod>();
            foreach (Facing facing in spec.CardinalDocks)
            {
                (int dx, int dy) = SiteGrid.Step(facing);
                pods.Add(AddPod(grid, c + dx * (ringOut - 2), c + dy * (ringOut - 2), facing));
            }
            foreach ((int sx, int sy) in diagonals)
            {
                int along = (int)Math.Round((ringOut - 2) * s);
                if (spec.DiagonalDocks)
                {
                    Facing face = random.Next(2) == 0 ? (sx > 0 ? Facing.East : Facing.West) : (sy > 0 ? Facing.South : Facing.North);
                    pods.Add(AddPod(grid, c + sx * along, c + sy * along, face));
                    continue;
                }
                if (random.NextDouble() >= 0.5) continue;

                //An equipment module: a square block just outside the ring, its inner corner overlapping the ring
                int halfSize = 3 + random.Next(2);
                int at = (int)Math.Round((ringOut + halfSize - 1) * s);
                int bx = c + sx * at, by = c + sy * at;
                for (int x = bx - halfSize; x <= bx + halfSize; x++)
                {
                    for (int y = by - halfSize; y <= by + halfSize; y++)
                    {
                        if (!SiteGrid.IsInterior(grid[x, y])) grid[x, y] = SiteZone.Room;
                    }
                }
            }

            //Weapon bays: rail guns beside each cardinal dock, facing out with it, and PDCs spread round the ring between
            //the docks, each facing the nearest cardinal to straight out. The ring's hull is a stepped curve, and a weapon
            //needs a straight run of hull with floor behind it, so each gets a bay pushed out from the ring until it
            //clears the curve, whose flat outer wall it sits across.
            var bays = new List<SiteMount>();
            foreach (Facing facing in spec.CardinalDocks)
            {
                (int dx, int dy) = SiteGrid.Step(facing);
                foreach (int side in new[] { -1, 1 })
                {
                    int w = (PodHalfWidth + BayHalfWidth + 3) * side;
                    AddBay(grid, c + dx * (ringOut - 3) + dy * w, c + dy * (ringOut - 3) + dx * w, facing, MountKind.Rail, bays);
                }
            }
            double offset = random.NextDouble() * Math.PI * 2.0 / spec.PDCs;
            for (int i = 0; i < spec.PDCs; i++)
            {
                //Where one would crowd a rail bay or a pod, it slides a little along the ring until it fits
                foreach (double nudge in new[] { 0.0, 0.1, -0.1, 0.2, -0.2, 0.3, -0.3 })
                {
                    double angle = offset + i * Math.PI * 2.0 / spec.PDCs + nudge;
                    double ax = Math.Cos(angle), ay = Math.Sin(angle);
                    if (Math.Abs(ax) > 0.97 || Math.Abs(ay) > 0.97) continue; //Leave the dock axes to the pods and rails
                    int sx = c + (int)Math.Round(ax * (ringOut - 3));
                    int sy = c + (int)Math.Round(ay * (ringOut - 3));
                    if (AddBay(grid, sx, sy, SiteGrid.Nearest(ax, ay), MountKind.PDC, bays)) break;
                }
            }

            grid.BuildHull();
            foreach (SiteMount bay in bays) AddMount(grid, bay.X, bay.Y, bay.Facing, bay.Kind);
            DoorCoreRooms(grid);

            //Docks on the end wall of each pod, and a door across it where it's clear of the ring
            foreach (Pod pod in pods)
            {
                grid.PlaceOnHull(pod.StartX, pod.StartY, pod.Facing, (x, y) => AddDock(grid, x, y, pod.Facing));
                (int dx, int dy) = SiteGrid.Step(pod.Facing);
                grid.PlaceDoorAlong(pod.DoorX, pod.DoorY, dx, dy, 1);
            }

            //Doors across each cardinal spoke where it leaves the ring and where it reaches the core, and one into the vault
            foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
            {
                (int dx, int dy) = SiteGrid.Step(facing);
                grid.PlaceDoorAlong(c + dx * (ringIn - 2), c + dy * (ringIn - 2), -dx, -dy, 3);
                grid.PlaceDoorAlong(c + dx * (spec.CoreRadius + 2), c + dy * (spec.CoreRadius + 2), dx, dy, 3);
            }
            {
                (int dx, int dy) = SiteGrid.Step((Facing)random.Next(4));
                grid.PlaceDoorAlong(c + dx * (vault + 1), c + dy * (vault + 1), dx, dy, 0);
            }


            return grid;
        }

        //A docking pod from a point inside the ring, straight out along its facing until every tile three either side of
        //its line is clear of the ring, then on for its door row, a row of floor, the docking port's alcove and the port
        private static Pod AddPod(SiteGrid grid, int x0, int y0, Facing facing)
        {
            (int fx, int fy) = SiteGrid.Step(facing);
            int ax = fy != 0 ? 1 : 0, ay = fx != 0 ? 1 : 0; //Across the pod
            int clear = 0;
            for (int t = 0; t < 40; t++)
            {
                bool open = true;
                for (int w = -PodHalfWidth - 1; w <= PodHalfWidth + 1 && open; w++)
                {
                    if (SiteGrid.IsInterior(grid[x0 + fx * t + ax * w, y0 + fy * t + ay * w])) open = false;
                }
                if (open)
                {
                    clear = t;
                    break;
                }
            }
            for (int t = 0; t <= clear + 3; t++)
            {
                for (int w = -PodHalfWidth; w <= PodHalfWidth; w++)
                {
                    int x = x0 + fx * t + ax * w, y = y0 + fy * t + ay * w;
                    if (grid[x, y] != SiteZone.Corridor) grid[x, y] = SiteZone.DockPod;
                }
            }
            return new Pod { StartX = x0, StartY = y0, Facing = facing, DoorX = x0 + fx * clear, DoorY = y0 + fy * clear };
        }

        private static Spec MakeSpec(SiteSize size, Random random)
        {
            Spec spec;
            switch (size)
            {
                case SiteSize.Small:
                    spec = new Spec { Radius = 31, RoomBand = 3, CorridorWidth = 3, CoreRadius = 14, VaultRadius = 2, SpokeWidth = 5, PDCs = 6 };
                    spec.CardinalDocks = random.Next(2) == 0 ? new[] { Facing.East, Facing.West } : new[] { Facing.North, Facing.South };
                    break;
                case SiteSize.Medium:
                    spec = new Spec { Radius = 37, RoomBand = 3, CorridorWidth = 3, CoreRadius = 15, VaultRadius = 3, SpokeWidth = 5, PDCs = 8 };
                    spec.CardinalDocks = new[] { Facing.North, Facing.East, Facing.South, Facing.West };
                    break;
                default:
                    spec = new Spec { Radius = 43, RoomBand = 4, CorridorWidth = 3, CoreRadius = 17, VaultRadius = 3, SpokeWidth = 5, PDCs = 12, DiagonalSpokes = true, DiagonalDocks = true };
                    spec.CardinalDocks = new[] { Facing.North, Facing.East, Facing.South, Facing.West };
                    break;
            }
            //Small variations from the seed
            spec.Radius += random.Next(-1, 2);
            spec.RoomBand += random.Next(0, 2);
            spec.CoreRadius += random.Next(0, 2); //Never smaller: the core rooms need the room outside the vault corridor
            return spec;
        }

        //Each core room gets a door on a straight wall onto the vault corridor or a spoke, as near its middle as fits. A
        //scrap of room no door fits is filled in as wall.
        private static void DoorCoreRooms(SiteGrid grid)
        {
            var seen = new HashSet<(int x, int y)>();
            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    if (grid[x, y] != SiteZone.CoreRoom || seen.Contains((x, y))) continue;
                    var room = new List<(int x, int y)>();
                    var queue = new Queue<(int x, int y)>();
                    queue.Enqueue((x, y));
                    seen.Add((x, y));
                    while (queue.Count > 0)
                    {
                        var t = queue.Dequeue();
                        room.Add(t);
                        foreach (var (dx, dy) in steps)
                        {
                            var n = (t.x + dx, t.y + dy);
                            if (grid[n.Item1, n.Item2] != SiteZone.CoreRoom || seen.Contains(n)) continue;
                            seen.Add(n);
                            queue.Enqueue(n);
                        }
                    }

                    double mx = room.Average(t => t.x), my = room.Average(t => t.y);
                    var spots = new List<(int x, int y, bool northSouth)>();
                    foreach (var t in room)
                    {
                        foreach (var (dx, dy) in steps)
                        {
                            int wx = t.x + dx, wy = t.y + dy;
                            SiteZone beyond = grid[wx + dx, wy + dy];
                            if (grid[wx, wy] == SiteZone.Wall && (beyond == SiteZone.Corridor || beyond == SiteZone.Spoke)) spots.Add((wx, wy, dx == 0));
                        }
                    }
                    bool doored = false;
                    foreach (var (wx, wy, northSouth) in spots.OrderBy(s => (s.x - mx) * (s.x - mx) + (s.y - my) * (s.y - my)))
                    {
                        if (!grid.PlaceDoor(wx, wy, northSouth)) continue;
                        doored = true;
                        break;
                    }
                    if (!doored)
                    {
                        foreach (var (rx, ry) in room) grid[rx, ry] = SiteZone.Wall;
                    }
                }
            }
        }

        private static void SetSpoke(SiteGrid grid, int x, int y)
        {
            SiteZone zone = grid[x, y];
            if (zone == SiteZone.Space || zone == SiteZone.CoreRoom || zone == SiteZone.Room) grid[x, y] = SiteZone.Spoke;
        }

        //A weapon bay from a point in the ring's room band, straight out along its facing until every tile a bay's width
        //and one more either side is clear of the ring: room tiles 7 across, so BuildHull gives it a flat outer wall 7
        //long with a row of floor 5 wide behind it. The mount goes in the middle of that wall. A bay that would run into
        //a docking pod, a spoke or a strut isn't made.
        private static bool AddBay(SiteGrid grid, int x0, int y0, Facing facing, MountKind kind, List<SiteMount> bays)
        {
            (int fx, int fy) = SiteGrid.Step(facing);
            int ax = fy != 0 ? 1 : 0, ay = fx != 0 ? 1 : 0; //Across the bay
            int clear = -1;
            for (int t = 1; t < 40 && clear < 0; t++)
            {
                bool open = true;
                for (int w = -BayHalfWidth - 1; w <= BayHalfWidth + 1 && open; w++)
                {
                    if (SiteGrid.IsInterior(grid[x0 + fx * t + ax * w, y0 + fy * t + ay * w])) open = false;
                }
                if (open) clear = t;
            }
            if (clear < 0) return false;
            for (int t = 0; t <= clear; t++)
            {
                for (int w = -BayHalfWidth; w <= BayHalfWidth; w++)
                {
                    SiteZone zone = grid[x0 + fx * t + ax * w, y0 + fy * t + ay * w];
                    if (zone == SiteZone.DockPod || zone == SiteZone.Spoke || zone == SiteZone.Strut || zone == SiteZone.Dock) return false;
                }
            }
            foreach (SiteMount other in bays)
            {
                if (Math.Abs(other.X - (x0 + fx * clear)) + Math.Abs(other.Y - (y0 + fy * clear)) < 2 * BayHalfWidth + 3) return false; //Keep bays apart
            }
            for (int t = 0; t <= clear; t++)
            {
                for (int w = -BayHalfWidth; w <= BayHalfWidth; w++)
                {
                    int x = x0 + fx * t + ax * w, y = y0 + fy * t + ay * w;
                    if (!SiteGrid.IsInterior(grid[x, y])) grid[x, y] = SiteZone.Room;
                }
            }
            bays.Add(new SiteMount { X = x0 + fx * clear, Y = y0 + fy * clear, Facing = facing, Kind = kind });
            return true;
        }

        private static void AddDock(SiteGrid grid, int x, int y, Facing facing)
        {
            grid[x, y] = SiteZone.Dock;
            grid.Docks.Add(new SiteDock { X = x, Y = y, Facing = facing });
        }

        private static void AddMount(SiteGrid grid, int x, int y, Facing facing, MountKind kind)
        {
            if (grid[x, y] != SiteZone.Wall) return;
            foreach (SiteMount mount in grid.Mounts)
            {
                if (Math.Abs(mount.X - x) <= 2 && Math.Abs(mount.Y - y) <= 2) return; //Keep mounts apart
            }
            grid[x, y] = SiteZone.Mount;
            grid.Mounts.Add(new SiteMount { X = x, Y = y, Facing = facing, Kind = kind });
        }

        private static double Distance(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
    }
}
