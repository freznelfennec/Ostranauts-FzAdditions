using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //Furnishes a site's shell: fixtures for each room's role, lights, and power.
    //
    //Every placement is checked against the game's own socket rules, simulated in SiteTiles from the catalog, so the
    //game would accept it too. Rooms are the walkable areas between walls and doors, one zone each; a generator can name
    //a room's role (SiteGrid.RoleHints), and the rest get one from their zone.
    //
    //Fixtures go where they fit, against walls where they can, never on the tiles in front of a door, and never where
    //they'd cut a room's doors off from each other or leave a fixture's use point out of reach. Wall lights are spaced
    //through every room and corridor. A power uplink in the reactor room feeds conduits laid under walls where possible
    //and floors otherwise, out to every light, powered fixture and door, and doors become powered doors.
    public static class SiteFurnish
    {
        private class Area
        {
            public RoomRole Role;
            public SiteZone Zone;
            public readonly List<(int x, int y)> Tiles = new List<(int x, int y)>();
            public readonly HashSet<(int x, int y)> Set = new HashSet<(int x, int y)>();
            public readonly List<(int x, int y)> Anchors = new List<(int x, int y)>(); //Door fronts and use points that must stay reachable
            public readonly Dictionary<int, List<(int x, int y)>> Openings = new Dictionary<int, List<(int x, int y)>>(); //Tiles opening onto each neighbouring area, by its index
        }

        public const string Uplink = "ItmReactorIC02IgnitionMini"; //"Power Uplink: Station", what base game stations draw on
        private const string Conduit = "ItmConduit01";
        public const string LootSpawner = "SysLootSpawner";     //The game's own: spawns a loot table into containers on its tile when the station spawns
        public const string DataVaultPrefix = "ItmFzDataVault";  //Data vaults are told which blueprints they hold
        public const string DataVaultPanel = "FzDataVault";
        public const string DataCore = "ItmFzDataCore01";       //Sealed in a vault's storage, until a hack opens it
        private const int LightReach = 4;       //Tiles a light covers each way
        private const int MaxCandidates = 500;  //Positions tried per fixture in a large area

        //Set to see why hull items were turned down (the preview tool's 'hull' mode)
        public static Action<string> HullTrace;

        //Fixtures come from the site's program (SiteData): each role's entries in turn, bigger items first in the data
        //so they find room, each placed one picked from the entry's alternatives so rooms vary.
        public static List<string> Furnish(SiteLayout layout, SiteCatalog catalog, SiteProgram program)
        {
            var problems = new List<string>();
            SiteGrid grid = layout.Grid;
            SiteStyle style = program.Style;
            var random = new Random(grid.Seed ^ 0x5F17);
            var tiles = new SiteTiles(catalog);
            var blocked = new HashSet<(int x, int y)>();

            //The shell's items, as the game will have them
            var shellDoors = new List<int>();
            for (int i = 0; i < layout.Items.Count; i++)
            {
                SiteItem item = layout.Items[i];
                ItemShape shape = catalog.Get(item.Name);
                if (shape == null) continue;
                tiles.Apply(ToPlacement(layout, item, shape));
                if (item.Name == style.Door) shellDoors.Add(i);
            }

            //Tiles in front of doors and docks, kept clear and joined up
            var reserved = new HashSet<(int x, int y)>();
            var doorWalls = new HashSet<(int x, int y)>();
            foreach (SiteDoor door in grid.Doors)
            {
                int px = door.PassageNorthSouth ? 0 : 1, py = door.PassageNorthSouth ? 1 : 0;
                int ax = py, ay = px;
                foreach (int side in new[] { -1, 1 })
                {
                    for (int d = 1; d <= 2; d++)
                    {
                        for (int a = -1; a <= 1; a++) reserved.Add((door.X + px * side * d + ax * a, door.Y + py * side * d + ay * a));
                    }
                }
                foreach (var t in SiteGrid.DoorWallTiles(door)) doorWalls.Add(t);
                doorWalls.Add((door.X, door.Y));
            }
            foreach (SiteDock dock in grid.Docks)
            {
                (int ox, int oy) = SiteGrid.Step(dock.Facing);
                int ax = oy != 0 ? 1 : 0, ay = ox != 0 ? 1 : 0;
                for (int d = 1; d <= 3; d++)
                {
                    for (int a = -1; a <= 1; a++) reserved.Add((dock.X - ox * d + ax * a, dock.Y - oy * d + ay * a));
                }
                foreach (var t in SiteShell.DockWallTiles(dock)) doorWalls.Add(t);
            }

            //Weapons on their mounts and sensors elsewhere on the hull, before anything inside, since each needs the
            //floor row behind its hull wall kept clear
            var placed = new List<Placement>();
            var hullRoles = new Dictionary<int, string>(); //Placed index to "weapon" or "sensor", for the checks
            var hullUsed = new HashSet<(int x, int y)>();
            PlaceWeapons(grid, tiles, catalog, program, reserved, doorWalls, blocked, random, placed, hullRoles, layout, hullUsed);

            List<Area> areas = FindAreas(grid);
            AssignRoles(grid, areas);
            var areaOf = new Dictionary<(int x, int y), int>();
            for (int i = 0; i < areas.Count; i++)
            {
                foreach (var t in areas[i].Tiles) areaOf[t] = i;
            }
            foreach (Area area in areas)
            {
                foreach (var t in area.Tiles)
                {
                    //Its door fronts stay clear. Where it opens straight onto a neighbouring area, such as a relay's
                    //equipment band onto the ring corridor, at least one tile of each opening has to stay clear and reachable.
                    if (reserved.Contains(t)) area.Anchors.Add(t);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var n = (t.x + dx, t.y + dy);
                        if (!areaOf.TryGetValue(n, out int other) || area.Set.Contains(n)) continue;
                        if (!area.Openings.TryGetValue(other, out var list)) area.Openings[other] = list = new List<(int x, int y)>();
                        if (!list.Contains(t)) list.Add(t);
                    }
                }
            }

            //Sensors, now the rooms are known, so one never cuts a room off as it hangs on the wall's inside face
            PlaceSensors(grid, tiles, catalog, program, reserved, doorWalls, blocked, random, placed, hullRoles, layout, hullUsed, areas);

            //Fixtures
            var looted = new List<(int index, RoomRole role)>(); //Placed containers that take loot, by index in placed
            var overdue = new List<(JsonSiteFixture entry, int count, RoomRole role)>(); //Fixtures short of their room's minimum
            var missingItems = new HashSet<string>();
            foreach (Area area in areas)
            {
                foreach (JsonSiteFixture entry in program.For(area.Role))
                {
                    if (entry.fChance < 1f && random.NextDouble() >= entry.fChance) continue;
                    int count = entry.nMin + random.Next(Math.Max(0, entry.nMax - entry.nMin) + 1);
                    int done = 0;
                    for (int n = 0; n < count; n++)
                    {
                        Placement? p = TryPlaceAny(grid, tiles, catalog, area, entry, reserved, doorWalls, blocked, random, missingItems);
                        if (p == null) break;
                        if (entry.bLoot) looted.Add((placed.Count, area.Role));
                        placed.Add(p.Value);
                        done++;
                    }
                    string label = $"{area.Role} {entry.aItems[0]}";
                    if (done < count) layout.Shortfalls[label] = (layout.Shortfalls.TryGetValue(label, out int missing) ? missing : 0) + count - done;
                    if (done < entry.nMin) overdue.Add((entry, entry.nMin - done, area.Role));
                }
            }
            //An alternative the game doesn't have is only a note, but an entry with none it has is a problem
            layout.MissingItems.UnionWith(missingItems);
            foreach (JsonSiteFixture entry in program.Fixtures.Where(f => f.aItems.All(i => catalog.Get(i) == null)))
            {
                problems.Add($"the catalog has none of {string.Join(", ", entry.aItems)} for {entry.strRole}");
            }

            //What a room needed but couldn't fit goes in the biggest equipment or storage areas instead, so a site always
            //gets its power and life support
            var overflow = areas.Where(a => a.Role == RoomRole.Equipment || a.Role == RoomRole.Storage).OrderByDescending(a => a.Tiles.Count).ToList();
            foreach (var (entry, count, role) in overdue)
            {
                int left = count;
                foreach (Area area in overflow)
                {
                    while (left > 0 && TryPlaceAny(grid, tiles, catalog, area, entry, reserved, doorWalls, blocked, random, missingItems) is Placement p)
                    {
                        if (entry.bLoot) looted.Add((placed.Count, role));
                        placed.Add(p);
                        left--;
                    }
                }
                if (left > 0 && entry.aItems.Contains(Uplink)) problems.Add($"no room for the power uplink in a {role} room or anywhere else");
            }
            if (!placed.Any(p => p.Shape.Name == Uplink) && !program.Fixtures.Any(f => f.aItems.Contains(Uplink))) problems.Add($"the site's program places no power uplink ({Uplink})");

            //The equipment lockout controller: one, in a room picked at random, so finding it is part of the job.
            //Never in the vault, a dock pod or a corridor.
            if (!string.IsNullOrEmpty(program.LockoutController))
            {
                ItemShape controller = catalog.Get(program.LockoutController);
                if (controller == null) problems.Add($"the catalog has no {program.LockoutController}");
                else
                {
                    var rooms = areas.Where(a => a.Role != RoomRole.Vault && a.Role != RoomRole.DockPod && a.Role != RoomRole.Corridor).ToList();
                    Shuffle(rooms, random);
                    Placement? spot = null;
                    foreach (Area area in rooms)
                    {
                        spot = TryPlace(grid, tiles, catalog, area, controller, reserved, doorWalls, blocked, random);
                        if (spot != null) break;
                    }
                    if (spot == null) problems.Add("no room for the equipment lockout controller");
                    else
                    {
                        placed.Add(spot.Value);
                        layout.Controllers++;
                    }
                }
            }

            //Lights through every room and corridor
            ItemShape light = catalog.Get(style.Light);
            if (light == null) problems.Add($"the catalog has no {style.Light}");
            else
            {
                foreach (Area area in areas) placed.AddRange(Light(tiles, area, light, blocked, doorWalls));
            }

            //Power: an uplink feeds conduits to everything that draws power, and doors become powered doors
            var poweredDoors = new List<Placement>();
            Placement? uplink = placed.Where(p => p.Shape.Name == Uplink).Cast<Placement?>().FirstOrDefault();
            ItemShape conduit = catalog.Get(Conduit);
            ItemShape poweredDoor = catalog.Get(style.PoweredDoor);
            if (uplink == null) problems.Add("no power uplink was placed, so nothing is powered");
            else if (conduit == null) problems.Add($"the catalog has no {Conduit}");
            else
            {
                if (poweredDoor != null)
                {
                    foreach (int i in shellDoors)
                    {
                        SiteItem item = layout.Items[i];
                        item.Name = style.PoweredDoor;
                        layout.Items[i] = item;
                        poweredDoors.Add(ToPlacement(layout, item, poweredDoor));
                    }
                }
                var network = Power(grid, uplink.Value, placed.Concat(poweredDoors), problems);
                foreach (var (x, y) in network)
                {
                    var p = Placement.At(conduit, x, y, 0);
                    if (!tiles.Fits(p)) continue;
                    tiles.Apply(p);
                    placed.Add(p);
                }
            }

            //Everything reachable from the docks with the furniture in place: every doorway and every use point
            var reach = Reachable(grid, tiles);
            var lostUses = placed.Where(p => p.Point("use") is (int, int) u && !p.Footprint().Contains(u) && !reach.Contains(u)).ToList();
            if (lostUses.Count > 0) problems.Add($"{lostUses.Count} fixtures' use points can't be reached, such as {string.Join(", ", lostUses.Take(3).Select(p => $"{p.Shape.Name} using {p.Point("use")}"))}");
            var lostFronts = areas.SelectMany(a => a.Anchors.Where(t => tiles.Walkable(t.x, t.y) && !reach.Contains(t)).Select(t => (a, t))).ToList();
            if (lostFronts.Count > 0) problems.Add($"{lostFronts.Count} door fronts can't be reached, such as {string.Join(", ", lostFronts.Take(3).Select(f => $"{f.t} in a {f.a.Role} area ({f.a.Zone})"))}");

            var ids = new string[placed.Count];
            int added = 0;
            for (int i = 0; i < placed.Count; i++)
            {
                Placement p = placed[i];
                ids[i] = Guid.NewGuid().ToString();
                var item = new SiteItem
                {
                    Name = p.Shape.Name,
                    X = (float)(layout.OriginX + p.CX),
                    Y = (float)(layout.OriginY - p.CY),
                    Rotation = p.Rotation,
                    Id = ids[i]
                };
                if (p.Shape.Name.StartsWith(DataVaultPrefix)) VaultContents(ref item, program, random);
                if (p.Shape.Name == program.LockoutController)
                {
                    item.PanelName = "FzLockout";
                    item.Panel = new[] { "strGUIPrefab", "FzDataVaultGUI", "strFriendlyName", "Equipment Lockout", "strCorp", program.Corp };
                }
                layout.Items.Add(item);
                added++;
                if (!p.Shape.Name.StartsWith(DataVaultPrefix)) continue;

                //Data cores in its storage, which a hack has to open (IsFzVaultStorageOpen) before anyone can take them
                int cores = program.CoresMin + random.Next(Math.Max(0, program.CoresMax - program.CoresMin) + 1);
                for (int n = 0; n < cores; n++)
                {
                    layout.Items.Add(new SiteItem { Name = DataCore, X = item.X, Y = item.Y, Id = Guid.NewGuid().ToString(), ParentId = item.Id });
                    added++;
                }
                layout.Cores += cores;
            }

            //Rounds loaded in each weapon: stacks whose first round is in the weapon and the rest in that first round,
            //as base templates load them
            foreach (var pair in hullRoles.Where(h => h.Value == "weapon"))
            {
                Placement p = placed[pair.Key];
                if (!program.Ammo.TryGetValue(p.Shape.Name, out JsonSiteAmmo ammo) || ammo.strAmmo == null) continue;
                int rounds = ammo.nMin + random.Next(Math.Max(0, ammo.nMax - ammo.nMin) + 1);
                int stack = Math.Max(1, ammo.nStack);
                string head = null;
                for (int n = 0; n < rounds; n++)
                {
                    string id = Guid.NewGuid().ToString();
                    bool first = n % stack == 0;
                    layout.Items.Add(new SiteItem
                    {
                        Name = ammo.strAmmo,
                        X = (float)(layout.OriginX + p.CX),
                        Y = (float)(layout.OriginY - p.CY),
                        Id = id,
                        ParentId = first ? ids[pair.Key] : head
                    });
                    if (first) head = id;
                    added++;
                }
                layout.Rounds += rounds;
            }

            //Loot: the game's loot spawner on a container's tile drops the room's loot table into it when the station
            //spawns. Not every container gets some; relays are given poorer odds and tables.
            foreach (var (index, role) in looted)
            {
                string loot = program.LootFor(role);
                if (loot == null || random.NextDouble() >= program.LootChance) continue;
                Placement p = placed[index];
                var (tx, ty) = p.Footprint().OrderBy(t => Math.Abs(t.x - p.CX) + Math.Abs(t.y - p.CY)).First();
                layout.Items.Add(new SiteItem
                {
                    Name = LootSpawner,
                    X = layout.ItemX(tx),
                    Y = layout.ItemY(ty),
                    Id = Guid.NewGuid().ToString(),
                    PanelName = "Panel A",
                    Panel = new[]
                    {
                        "strGUIPrefab", "GUILootSpawn", "strFriendlyName", "Loot Spawner",
                        "strType", "Loot", "strLoot", loot, "strRange", "0", "strCount", "1",
                        "strNew", "True", "strDamaged", "True", "strDerelict", "True"
                    }
                });
                layout.LootSpawners++;
                added++;
            }

            //Loose things on room floors: a loot spawner on an open tile drops its table where it stands when the
            //station spawns, so rooms are worth searching on the way to the vault. Never on a door front, a use point
            //or anything placed.
            var floorUsed = new HashSet<(int x, int y)>();
            foreach (Area area in areas)
            {
                foreach (JsonSiteFloorLoot entry in program.FloorLoot.Where(f => string.Equals(f.strRole, area.Role.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    if (entry.fChance < 1f && random.NextDouble() >= entry.fChance) continue;
                    int count = entry.nMin + random.Next(Math.Max(0, entry.nMax - entry.nMin) + 1);
                    var open = area.Tiles.Where(t => tiles.Walkable(t.x, t.y) && !blocked.Contains(t) && !reserved.Contains(t) && !area.Anchors.Contains(t) && !floorUsed.Contains(t)).ToList();
                    Shuffle(open, random);
                    foreach (var (tx, ty) in open.Take(count))
                    {
                        floorUsed.Add((tx, ty));
                        layout.Items.Add(new SiteItem
                        {
                            Name = LootSpawner,
                            X = layout.ItemX(tx),
                            Y = layout.ItemY(ty),
                            Id = Guid.NewGuid().ToString(),
                            PanelName = "Panel A",
                            Panel = new[]
                            {
                                "strGUIPrefab", "GUILootSpawn", "strFriendlyName", "Loot Spawner",
                                "strType", "Loot", "strLoot", entry.strLoot, "strRange", "0", "strCount", "1",
                                "strNew", "True", "strDamaged", "True", "strDerelict", "True"
                            }
                        });
                        layout.FloorLoot++;
                        added++;
                    }
                }
            }

            layout.Furnished = added;
            layout.Placed.AddRange(placed);
            layout.Weapons = hullRoles.Count(h => h.Value == "weapon");
            layout.Sensors = hullRoles.Count(h => h.Value == "sensor");
            return problems;
        }

        //A data vault's contents, picked from its corporation's blueprints when the site is made: mostly Specialised,
        //the rest Ordinary, then any of its rare ones that come up, kept in its FzDataVault prop map with the corporation
        private static void VaultContents(ref SiteItem item, SiteProgram program, Random random)
        {
            int count = program.BlueprintsMin + random.Next(Math.Max(0, program.BlueprintsMax - program.BlueprintsMin) + 1);
            var chosen = new List<string>();
            var specialised = program.SpecialisedBlueprints.ToList();
            var ordinary = program.OrdinaryBlueprints.ToList();
            for (int n = 0; n < count; n++)
            {
                List<string> from = (random.NextDouble() < program.SpecialisedShare && specialised.Count > 0) || ordinary.Count == 0 ? specialised : ordinary;
                if (from.Count == 0) break;
                int i = random.Next(from.Count);
                chosen.Add(from[i]);
                from.RemoveAt(i);
            }
            //Rare ones on top, each rolled for on its own
            foreach (var (blueprint, chance) in program.RareBlueprints)
            {
                if (random.NextDouble() < chance && !chosen.Contains(blueprint)) chosen.Add(blueprint);
            }
            item.PanelName = DataVaultPanel;
            item.Panel = new[] { "strGUIPrefab", "FzDataVaultGUI", "strFriendlyName", "Data Vault", "strCorp", program.Corp, "strBlueprints", string.Join(",", chosen) };
        }

        //One fixture from an entry's alternatives: tried in a random order until one fits
        private static Placement? TryPlaceAny(SiteGrid grid, SiteTiles tiles, SiteCatalog catalog, Area area, JsonSiteFixture entry,
            HashSet<(int x, int y)> reserved, HashSet<(int x, int y)> doorWalls, HashSet<(int x, int y)> blocked, Random random, HashSet<string> missing)
        {
            var order = entry.aItems.ToList();
            Shuffle(order, random);
            foreach (string name in order)
            {
                ItemShape shape = catalog.Get(name);
                if (shape == null)
                {
                    missing.Add(name);
                    continue;
                }
                Placement? p = TryPlace(grid, tiles, catalog, area, shape, reserved, doorWalls, blocked, random);
                if (p != null) return p;
            }
            return null;
        }

        //Rotation for a hull item facing out: unturned it faces north, and rotations go anticlockwise
        private static int Outward(Facing facing)
        {
            switch (facing)
            {
                case Facing.North: return 0;
                case Facing.West: return 90;
                case Facing.South: return 180;
                default: return 270;
            }
        }

        //Weapons across their mounts. A hull item reaches out into space across the hull wall, how far in decided by the
        //game's own sockets (SiteTiles), and the floor just inside it is kept clear like a door's front.
        private static void PlaceWeapons(SiteGrid grid, SiteTiles tiles, SiteCatalog catalog, SiteProgram program,
            HashSet<(int x, int y)> reserved, HashSet<(int x, int y)> doorWalls, HashSet<(int x, int y)> blocked, Random random,
            List<Placement> placed, Dictionary<int, string> roles, SiteLayout layout, HashSet<(int x, int y)> used)
        {
            int missedWeapons = 0;
            foreach (SiteMount mount in grid.Mounts)
            {
                string[] choices = mount.Kind == MountKind.PDC ? program.WeaponsPDC : program.WeaponsRail;
                Placement? p = TryHull(grid, tiles, catalog, choices, mount.X, mount.Y, mount.Facing, reserved, doorWalls, blocked, used, random);
                if (p == null && program.WeaponsFallback.Length > 0)
                {
                    //None of the corporation's guns fit this mount; any the site type uses might
                    p = TryHull(grid, tiles, catalog, program.WeaponsFallback.Except(choices).ToArray(), mount.X, mount.Y, mount.Facing, reserved, doorWalls, blocked, used, random);
                }
                if (p == null)
                {
                    if (choices.Length > 0) missedWeapons++;
                    continue;
                }
                //Floor its back row hangs over can't be walked
                foreach (var t in p.Value.Footprint())
                {
                    if (SiteGrid.IsWalkable(grid[t.x, t.y])) blocked.Add(t);
                }
                roles[placed.Count] = "weapon";
                placed.Add(p.Value);
            }
            if (missedWeapons > 0) layout.Shortfalls["Hull weapon"] = missedWeapons;
        }

        //Sensors on hull facing space, clear of the docks, each leaving its room joined up and its use point reachable
        private static void PlaceSensors(SiteGrid grid, SiteTiles tiles, SiteCatalog catalog, SiteProgram program,
            HashSet<(int x, int y)> reserved, HashSet<(int x, int y)> doorWalls, HashSet<(int x, int y)> blocked, Random random,
            List<Placement> placed, Dictionary<int, string> roles, SiteLayout layout, HashSet<(int x, int y)> used, List<Area> areas)
        {
            int sensors = program.Sensors.Length == 0 ? 0 : program.SensorsMin + random.Next(program.SensorsMax - program.SensorsMin + 1);
            if (sensors == 0) return;
            var spots = new List<(int x, int y, Facing facing)>();
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    if (grid[x, y] != SiteZone.Wall) continue;
                    foreach (Facing facing in new[] { Facing.North, Facing.South, Facing.East, Facing.West })
                    {
                        var (ox, oy) = SiteGrid.Step(facing);
                        if (grid[x + ox, y + oy] == SiteZone.Space && SiteGrid.IsWalkable(grid[x - ox, y - oy])) spots.Add((x, y, facing));
                    }
                }
            }
            Shuffle(spots, random);
            int done = 0;
            foreach (var (x, y, facing) in spots)
            {
                if (done >= sensors) break;
                if (grid.Mounts.Any(m => Math.Abs(m.X - x) + Math.Abs(m.Y - y) < 3) || grid.Docks.Any(d => Math.Abs(d.X - x) + Math.Abs(d.Y - y) < 8)) continue;
                Area area = null;
                HashSet<(int x, int y)> blocks = null;
                (int x, int y)? use = null;
                bool Keeps(Placement c)
                {
                    var footprint = new HashSet<(int x, int y)>(c.Footprint());
                    use = c.Point("use") is (int, int) u && !footprint.Contains(u) ? u : ((int, int)?)null;
                    area = areas.FirstOrDefault(a => use != null && a.Set.Contains(use.Value)) ?? areas.FirstOrDefault(a => footprint.Any(a.Set.Contains));
                    if (area == null) return false;
                    blocks = new HashSet<(int x, int y)>(footprint.Where(area.Set.Contains));
                    var anchors = area.Anchors.ToList();
                    if (use != null) anchors.Add(use.Value);
                    return Joined(area, anchors, blocked, blocks);
                }
                Placement? p = TryHull(grid, tiles, catalog, program.Sensors, x, y, facing, reserved, doorWalls, blocked, used, random, Keeps);
                if (p == null) continue;
                Keeps(p.Value);
                if (blocks != null) blocked.UnionWith(blocks);
                if (use != null) area?.Anchors.Add(use.Value);
                roles[placed.Count] = "sensor";
                placed.Add(p.Value);
                done++;
            }
            if (done < sensors) layout.Shortfalls["Hull sensor"] = sensors - done;
        }

        //The best fit of one of a hull item's alternatives across a hull tile, facing out: the placement covering the
        //tile whose centre is nearest it, in space and on hull only, leaving its inner floor row and use point clear
        private static Placement? TryHull(SiteGrid grid, SiteTiles tiles, SiteCatalog catalog, string[] choices, int hx, int hy, Facing facing,
            HashSet<(int x, int y)> reserved, HashSet<(int x, int y)> doorWalls, HashSet<(int x, int y)> blocked, HashSet<(int x, int y)> used, Random random,
            Func<Placement, bool> accept = null)
        {
            int rotation = Outward(facing);
            var (ox, oy) = SiteGrid.Step(facing);
            var order = choices.ToList();
            Shuffle(order, random);
            foreach (string name in order)
            {
                ItemShape shape = catalog.Get(name);
                if (shape == null) continue;
                int reach = Math.Max(shape.W, shape.H) + 1;
                Placement? best = null;
                double bestDistance = double.MaxValue;
                for (int x = hx - reach; x <= hx + reach; x++)
                {
                    for (int y = hy - reach; y <= hy + reach; y++)
                    {
                        Placement p = Placement.At(shape, x, y, rotation);
                        var footprint = p.Footprint().ToList();
                        if (!footprint.Contains((hx, hy))) continue;
                        bool ok = true;
                        foreach (var t in footprint)
                        {
                            SiteZone zone = grid[t.x, t.y];
                            //Space, hull, or floor just inside it (a sensor's inner row hangs on the wall's inside face)
                            bool fits = zone == SiteZone.Space || zone == SiteZone.Wall || zone == SiteZone.Mount
                                || (SiteGrid.IsWalkable(zone) && zone != SiteZone.Door && zone != SiteZone.Dock && !blocked.Contains(t));
                            if (!fits || used.Contains(t) || doorWalls.Contains(t) || reserved.Contains(t)) ok = false;
                        }
                        if (!ok)
                        {
                            HullTrace?.Invoke($"{name} at {x},{y}: footprint on {string.Join(" ", footprint.Select(t => grid[t.x, t.y]))}");
                            continue;
                        }
                        if (!tiles.Fits(p))
                        {
                            HullTrace?.Invoke($"{name} at {x},{y} rot {rotation}: {tiles.Explain(p)}");
                            continue;
                        }
                        //The game's sockets decide how far in it reaches (a PDC stops at the wall, a mass thrower's back row
                        //hangs over the floor); it only has to be usable, from a floor tile nothing else is on
                        if (p.Point("use") is (int ux, int uy) && !footprint.Contains((ux, uy))
                            && (!SiteGrid.IsWalkable(grid[ux, uy]) || grid[ux, uy] == SiteZone.Door || blocked.Contains((ux, uy)) || used.Contains((ux, uy))))
                        {
                            HullTrace?.Invoke($"{name} at {x},{y}: its use point is on {grid[ux, uy]}");
                            continue;
                        }
                        double distance = Math.Abs(p.CX - hx) + Math.Abs(p.CY - hy);
                        if (distance >= bestDistance) continue;
                        if (accept != null && !accept(p))
                        {
                            HullTrace?.Invoke($"{name} at {x},{y}: would cut its room off");
                            continue;
                        }
                        best = p;
                        bestDistance = distance;
                    }
                }
                if (best == null) continue;
                Placement chosen = best.Value;
                tiles.Apply(chosen);
                //The floor just inside it stays clear, as a door's front does, and so does its use point
                var footprintSet = new HashSet<(int x, int y)>(chosen.Footprint());
                foreach (var t in footprintSet)
                {
                    used.Add(t);
                    var inside = (t.x - ox, t.y - oy);
                    if (grid[t.x, t.y] != SiteZone.Space && !footprintSet.Contains(inside) && SiteGrid.IsWalkable(grid[inside.Item1, inside.Item2])) reserved.Add(inside);
                }
                if (chosen.Point("use") is (int, int) use && !footprintSet.Contains(use)) reserved.Add(use);
                return chosen;
            }
            return null;
        }

        private static Placement ToPlacement(SiteLayout layout, SiteItem item, ItemShape shape)
        {
            return new Placement { Shape = shape, CX = item.X - layout.OriginX, CY = layout.OriginY - item.Y, Rotation = (int)Math.Round(item.Rotation) };
        }

        //The walkable areas between walls and doors, one zone each. Corridors, spokes and tunnels count as one zone.
        private static List<Area> FindAreas(SiteGrid grid)
        {
            SiteZone Kind(SiteZone zone) => zone == SiteZone.Spoke || zone == SiteZone.Tunnel ? SiteZone.Corridor : zone;
            var seen = new HashSet<(int, int)>();
            var areas = new List<Area>();
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    SiteZone zone = grid[x, y];
                    if (!SiteGrid.IsWalkable(zone) || zone == SiteZone.Door || zone == SiteZone.Dock || seen.Contains((x, y))) continue;
                    var area = new Area { Zone = Kind(zone) };
                    var queue = new Queue<(int x, int y)>();
                    queue.Enqueue((x, y));
                    seen.Add((x, y));
                    while (queue.Count > 0)
                    {
                        var t = queue.Dequeue();
                        area.Tiles.Add(t);
                        area.Set.Add(t);
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            var n = (t.x + dx, t.y + dy);
                            SiteZone nz = grid[n.Item1, n.Item2];
                            if (seen.Contains(n) || !SiteGrid.IsWalkable(nz) || nz == SiteZone.Door || nz == SiteZone.Dock || Kind(nz) != area.Zone) continue;
                            seen.Add(n);
                            queue.Enqueue(n);
                        }
                    }
                    areas.Add(area);
                }
            }
            return areas;
        }

        //Roles from the generator's hints, then from zones: a relay's core areas, biggest first, are life support, the
        //reactor and servers, and its equipment band holds the batteries
        private static void AssignRoles(SiteGrid grid, List<Area> areas)
        {
            var hinted = new HashSet<Area>();
            foreach (var (x, y, role) in grid.RoleHints)
            {
                Area area = areas.FirstOrDefault(a => a.Set.Contains((x, y)));
                if (area == null) continue;
                area.Role = role;
                hinted.Add(area);
            }
            var coreRoles = new[] { RoomRole.LifeSupport, RoomRole.Reactor, RoomRole.Servers, RoomRole.Servers }; //Life support needs the most floor
            int core = 0;
            foreach (Area area in areas.Where(a => !hinted.Contains(a)).OrderByDescending(a => a.Tiles.Count))
            {
                switch (area.Zone)
                {
                    case SiteZone.CoreRoom: area.Role = core < coreRoles.Length ? coreRoles[core++] : RoomRole.Servers; break;
                    case SiteZone.Vault: area.Role = RoomRole.Vault; break;
                    case SiteZone.Room: area.Role = grid.Kind == SiteKind.Relay ? RoomRole.Equipment : RoomRole.Storage; break;
                    case SiteZone.DockPod: area.Role = RoomRole.DockPod; break;
                    case SiteZone.Checkpoint: area.Role = RoomRole.Checkpoint; break;
                    default: area.Role = RoomRole.Corridor; break;
                }
            }
        }

        //The best place for one fixture in an area, against walls where it can be, or null if it fits nowhere
        private static Placement? TryPlace(SiteGrid grid, SiteTiles tiles, SiteCatalog catalog, Area area, ItemShape shape,
            HashSet<(int x, int y)> reserved, HashSet<(int x, int y)> doorWalls, HashSet<(int x, int y)> blocked, Random random)
        {
            //Positions: the area's tiles and the wall tiles round them, since some fixtures sit half in a wall
            var positions = new HashSet<(int x, int y)>();
            foreach (var (x, y) in area.Tiles)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++) positions.Add((x + dx, y + dy));
                }
            }
            var order = positions.ToList();
            Shuffle(order, random);
            if (order.Count > MaxCandidates) order = order.Take(MaxCandidates).ToList();

            var options = new List<(Placement p, HashSet<(int x, int y)> blocks, (int x, int y)? use, double score)>();
            foreach (var (x, y) in order)
            {
                foreach (int rotation in new[] { 0, 90, 180, 270 })
                {
                    Placement p = Placement.At(shape, x, y, rotation);
                    var footprint = p.Footprint().ToList();
                    bool ok = true;
                    var blocks = new HashSet<(int x, int y)>();
                    int wallSides = 0;
                    for (int r = 0; r < shape.H && ok; r++)
                    {
                        for (int c = 0; c < shape.W && ok; c++)
                        {
                            var t = p.Tile(c, r);
                            bool inArea = area.Set.Contains(t);
                            SiteZone zone = grid[t.x, t.y];
                            if (!inArea && zone != SiteZone.Wall) ok = false;
                            else if (reserved.Contains(t) || blocked.Contains(t) || doorWalls.Contains(t)) ok = false;
                            else
                            {
                                string[] conds = catalog.TagConds(shape.Adds[r * shape.W + c]);
                                if (inArea && conds != null && conds.Any(k => k == "IsObstruction" || k == "IsWall" || k == "IsItemTile")) blocks.Add(t);
                            }
                        }
                    }
                    if (!ok || !tiles.Fits(p)) continue;
                    //Where it's used from: its use point, or for an item whose use point is under it, a free tile beside it
                    (int x, int y)? use = p.Point("use");
                    if (use is (int fx, int fy) && footprint.Contains((fx, fy)))
                    {
                        use = null;
                        foreach (var t in footprint)
                        {
                            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                var n = (t.x + dx, t.y + dy);
                                if (use == null && area.Set.Contains(n) && !footprint.Contains(n) && !blocked.Contains(n) && tiles.Walkable(n.Item1, n.Item2)) use = n;
                            }
                        }
                        if (use == null) continue;
                    }
                    if (use is (int ux, int uy) && (!area.Set.Contains((ux, uy)) || blocked.Contains((ux, uy)) || blocks.Contains((ux, uy)) || !tiles.Walkable(ux, uy))) continue;
                    foreach (var t in footprint)
                    {
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            if (grid[t.x + dx, t.y + dy] == SiteZone.Wall) wallSides++;
                        }
                    }
                    options.Add((p, blocks, use, wallSides + random.NextDouble()));
                }
            }

            foreach (var (p, blocks, use, _) in options.OrderByDescending(o => o.score).Take(12))
            {
                var anchors = area.Anchors.ToList();
                if (use is (int, int) u) anchors.Add(u);
                if (!Joined(area, anchors, blocked, blocks)) continue;
                tiles.Apply(p);
                foreach (var t in blocks) blocked.Add(t);
                if (use is (int, int) keep) area.Anchors.Add(keep);
                return p;
            }
            return null;
        }

        //Whether every anchor in an area is still clear and can reach every other round what's blocked, and each of its
        //openings onto a neighbouring area still has a clear tile they can reach
        private static bool Joined(Area area, List<(int x, int y)> anchors, HashSet<(int x, int y)> blocked, HashSet<(int x, int y)> more)
        {
            bool Clear((int x, int y) t) => area.Set.Contains(t) && !blocked.Contains(t) && !more.Contains(t);
            var open = anchors.Where(a => area.Set.Contains(a)).ToList();
            if (!open.All(Clear)) return false;
            var start = open.Count > 0 ? open[0] : area.Openings.Values.SelectMany(o => o).Where(Clear).Cast<(int x, int y)?>().FirstOrDefault();
            if (start == null) return area.Openings.Count == 0;
            var seen = new HashSet<(int x, int y)> { start.Value };
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue(start.Value);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (t.x + dx, t.y + dy);
                    if (seen.Contains(n) || !area.Set.Contains(n) || blocked.Contains(n) || more.Contains(n)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            return open.All(seen.Contains) && area.Openings.Values.All(o => o.Any(seen.Contains));
        }

        //Wall lights until every tile of an area has one within LightReach, each on a free tile against a wall
        private static List<Placement> Light(SiteTiles tiles, Area area, ItemShape light, HashSet<(int x, int y)> blocked, HashSet<(int x, int y)> doorWalls)
        {
            var lights = new List<Placement>();
            var dark = new HashSet<(int x, int y)>(area.Tiles);
            var anchors = new HashSet<(int x, int y)>(area.Anchors); //Use points and door fronts stay clear
            foreach (var target in area.Tiles.OrderBy(t => t.y).ThenBy(t => t.x))
            {
                if (!dark.Contains(target)) continue;
                Placement? best = null;
                int bestDistance = int.MaxValue;
                foreach (var t in area.Tiles)
                {
                    int distance = Math.Max(Math.Abs(t.x - target.x), Math.Abs(t.y - target.y));
                    if (distance >= bestDistance || distance > LightReach || blocked.Contains(t) || anchors.Contains(t) || !tiles.Walkable(t.x, t.y)) continue;
                    foreach (int rotation in new[] { 0, 90, 180, 270 })
                    {
                        Placement p = Placement.At(light, t.x, t.y, rotation);
                        if (p.Point("PowerSource") is (int px, int py) && doorWalls.Contains((px, py))) continue; //Not on a door's wall
                        if (!tiles.Fits(p)) continue;
                        best = p;
                        bestDistance = distance;
                        break;
                    }
                }
                if (best == null)
                {
                    dark.Remove(target);
                    continue;
                }
                tiles.Apply(best.Value);
                lights.Add(best.Value);
                var (lx, ly) = best.Value.Tile(0, 0);
                dark.RemoveWhere(t => Math.Max(Math.Abs(t.x - lx), Math.Abs(t.y - ly)) <= LightReach);
            }
            return lights;
        }

        //Conduit tiles joining the uplink to every powered item: for each, the cheapest way from what's already joined to
        //one of its power points, through walls before floors, never through space, rock, docks or doorways
        private static HashSet<(int x, int y)> Power(SiteGrid grid, Placement uplink, IEnumerable<Placement> items, List<string> problems)
        {
            var network = new HashSet<(int x, int y)>();
            if (uplink.Point("PowerOutput") is (int, int) output) network.Add(output);
            else
            {
                problems.Add("the power uplink has no PowerOutput point");
                return network;
            }

            int Cost(int x, int y)
            {
                SiteZone zone = grid[x, y];
                switch (zone)
                {
                    case SiteZone.Wall:
                    case SiteZone.Strut:
                    case SiteZone.Mount:
                        return 1;
                    case SiteZone.Space:
                    case SiteZone.Rock:
                    case SiteZone.Dock:
                    case SiteZone.Door:
                        return -1;
                    default:
                        return 3;
                }
            }

            var consumers = new List<List<(int x, int y)>>();
            foreach (Placement p in items)
            {
                var points = p.Shape.Points.Keys.Where(k => k.StartsWith("Power") && k != "PowerOutput").Select(k => p.Point(k)).Where(t => t != null).Select(t => t.Value).Where(t => Cost(t.x, t.y) > 0).ToList();
                if (points.Count > 0) consumers.Add(points);
            }
            var (ux, uy) = output;
            consumers = consumers.OrderBy(c => c.Min(t => Math.Abs(t.x - ux) + Math.Abs(t.y - uy))).ToList();

            int unpowered = 0;
            foreach (var targets in consumers)
            {
                if (targets.Any(network.Contains)) continue;
                var path = Cheapest(grid, network, new HashSet<(int x, int y)>(targets), Cost);
                if (path == null)
                {
                    unpowered++;
                    continue;
                }
                foreach (var t in path) network.Add(t);
            }
            if (unpowered > 0) problems.Add($"{unpowered} powered items have no way to the uplink");
            return network;
        }

        private static List<(int x, int y)> Cheapest(SiteGrid grid, HashSet<(int x, int y)> from, HashSet<(int x, int y)> to, Func<int, int, int> cost)
        {
            var dist = new Dictionary<(int, int), int>();
            var back = new Dictionary<(int, int), (int, int)>();
            var open = new SortedSet<(int d, int x, int y)>();
            foreach (var t in from)
            {
                dist[t] = 0;
                open.Add((0, t.x, t.y));
            }
            while (open.Count > 0)
            {
                var (d, x, y) = open.Min;
                open.Remove(open.Min);
                if (to.Contains((x, y)))
                {
                    var path = new List<(int x, int y)>();
                    var at = (x, y);
                    while (!from.Contains(at))
                    {
                        path.Add(at);
                        at = back[at];
                    }
                    return path;
                }
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (!grid.InBounds(nx, ny)) continue;
                    int c = cost(nx, ny);
                    if (c < 0) continue;
                    int nd = d + c;
                    if (dist.TryGetValue((nx, ny), out int old) && old <= nd) continue;
                    if (dist.ContainsKey((nx, ny))) open.Remove((old, nx, ny));
                    dist[(nx, ny)] = nd;
                    back[(nx, ny)] = (x, y);
                    open.Add((nd, nx, ny));
                }
            }
            return null;
        }

        //The tiles someone can walk to from the docks, with everything in place
        private static HashSet<(int x, int y)> Reachable(SiteGrid grid, SiteTiles tiles)
        {
            var seen = new HashSet<(int x, int y)>();
            var queue = new Queue<(int x, int y)>();
            foreach (SiteDock dock in grid.Docks)
            {
                (int ox, int oy) = SiteGrid.Step(dock.Facing);
                var start = (dock.X - ox, dock.Y - oy);
                if (seen.Add(start)) queue.Enqueue(start);
            }
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (t.x + dx, t.y + dy);
                    if (seen.Contains(n) || !tiles.Walkable(n.Item1, n.Item2)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            return seen;
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
