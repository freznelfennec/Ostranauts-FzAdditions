# Procedural mission stations

This is a design for generating corporate relay stations and black sites for blueprint missions.

Milestone 1 is built: the shape stage and the PNG preview, in `Missions/Util/SiteGen`. In the game, `fz site preview <relay|blacksite|all> [size|all] [seed]` writes PNGs to `BepInEx/FzSitePreviews`. Outside the game, run `dotnet run -- <folder> <seed...>` in `Design/Missions/Tools/SitePreview`, which compiles the same generator files. The generator is plain C# with nothing from Unity or the game, and is seeded with `System.Random`, so a kind, size and seed always give the same shape.

## What the game gives us

- **A layout is just items on a grid.** The game's asteroid generator builds a `JsonShip` from nothing but `aItems`, `nCols`, `nRows` and `vShipPos`. The game rebuilds rooms from walls and floors when a ship loads (`Ship.CreateRooms`). Saved `aRooms` only carry room IDs, so a generated layout doesn't need them.
- **Air comes free.** A ship spawned from a template is flagged `bPrefill`, which fills every sealed room with air at 297 K the first time it fully loads.
- **Spawning one is easy.** `StarSystem.SpawnShip` looks templates up in `DataHandler.dictShips`. Put a generated `JsonShip` there under a unique name, then spawn it with `SpawnStationFromJSON` like any station. Set `nConstructionProgress = 100`, or the game spawns the station's construction template instead.
- **Saving is automatic.** A shallow ship saves a copy of its whole json, and a loaded one saves its items, so a generated layout is never generated again.
- **Round stations work in this game.** The base game ships two unused round layouts, `LA_PORT_1` and `LA_MAINTENANCE_1`, both about 166 tiles square. Their hulls are stepped tile circles built from 1×1 walls and floors, so a ring station fits how the game draws.
- **Templates can be merged in.** `GridUtils.CreateOffset` and `MergeShipOntoAsteroid` merge a hand-built template into a layout, but crudely: they check every placed item against every new one, don't give items new IDs, and skip a whole tile if anything is already there. We'll write our own merge that gives each item a new `strID` and remaps the `strParentID`s.

## Pipeline

Each site is generated from a seed in five stages. A stage that fails validation tries again with the next seed.

1. **Site spec:** picks the shape, size class, owner faction, style and room program, from data.
2. **Shape:** lays out zones on the tile grid: hull, corridors, room areas, dock pods and weapon mounts.
3. **Rooms:** splits zones into rooms and gives each one a role from the room program.
4. **Fit-out:** fills each room from hand-built kits, and lays doors, lights, conduits, life support and weapons.
5. **Output:** builds the `JsonShip` and registers it as `FzSite_<regID>`. It's then spawned as a station and flown by an FzAI pilot.

### 1. Site spec (data)

Sites are read from every mod's `data/fz/sites/` folder in load order, the same way `fz/fabrication` is read, so any mod can add a corporation.

- **Site types** (`fz_sitetypes_*.json`):
  - shape (relay or black site);
  - size classes with their radii;
  - dock count;
  - vault count;
  - countdown;
  - weapon budget;
  - room program.
- **Styles** (`fz_sitestyles_*.json`), one per corporation:
  - the floor, wall, window and light items to use, and any decor;
  - which kits it prefers.
  A corporation with no style uses the generic one.
- **Room program:**
  - **Required rooms:** a dock bay at every dock, a security post behind every dock, the reactor, life support, ammo storage near each weapon, and the vaults in the core.
  - **Fillers:** quarters, galley, storage and labs, weighted by the site type.

### 2. Shape

**Relay station.** It has an outer docking ring, spokes, and a core, like the reference image. Below is a sketch at radius 30: 69 tiles across, `D` is a dock, `V` is the vault level in the core, and `-` is the ring corridor.

```
                                  D
                                #####
                                #...#
                           ######...######
                      ###...-------------...###
                  ###..----...............----..###
                ##.---..####    #...#    ####..---.##
             ##..--..##         #...#         ##..--..##
           ##.--.##             #...#             ##.--.##
         #.--.##                #...#                ##.--.#
        #.--.#                  #...#                  #.--.#
      ##.-.##                ##.......##                ##.-.##
      #.--.#              ##.............##              #.--.#
   ####.-..################.....VVVVV.....################..-.####
  D#....-......................VVVVVVV......................-....#D
   ####.-..################.....VVVVV.....################..-.####
      #.--.#              ##.............##              #.--.#
      ##.-.##                ##.......##                ##.-.##
        #.--.#                  #...#                  #.--.#
         #.--.##                #...#                ##.--.#
           ##.--.##             #...#             ##.--.##
             ##..--..##         #...#         ##..--..##
                ##.---..####    #...#    ####..---.##
                  ###..----...............----..###
                      ###...-------------...###
                           ######...######
                                #...#
                                  D
```

- **The ring:** an annulus around the centre (inner and outer radius), tested on tile centres. Relays are unmanned, so the ring is tight: the ring corridor along its inner wall, and one band of equipment rooms (3 or 4 tiles deep) outside it against the hull, where weapons and sensors go. Most of a relay is the empty space between the ring and the core, crossed by long spokes. A tile is a wall when it's in the annulus but next to a tile outside it.
- **Spokes:** straight corridors on the cardinal axes. A spoke is 5 tiles wide with its walls (3 to walk). Inside the core only the cardinal spokes carry on to the vault corridor, 3 wide, so the core keeps room for rooms; large relays' diagonal spokes stop at a corridor round the inside of the core's edge. A dock, a spoke and the core then line up, so each spoke is a defensible chokepoint, and its axis-aligned walls need no stepped edges.
  - Sites with more spokes add diagonal ones, drawn as stepped corridors.
- **Dock pods:** short rectangles out from the ring at the four cardinal points. A docking port has to face a cardinal direction and sit on an axis-aligned outer edge, so the pods give it one. Larger relays add pods on the diagonals, each with a flat face on its dominant axis.
- **The core:** a small disc split into wedges for the reactor, minimal life support and servers, around a central vault room. A ring corridor between the wedges and the vault room joins every spoke. Its radius is 14, 15 or 17 by size (plus up to 1 from the seed), against a ring inner radius of about 23, 29 or 34, which leaves gaps of about 9, 14 and 17 tiles. The core rooms are walled off from the spokes and from the vault corridor (whose outer edge is a square wall), and each gets a door on a straight wall onto the corridor or a spoke, as near its middle as fits. A scrap of room too small for any door is filled in as wall.
- **Weapon mounts:** on the ring's outer wall, turned to the cardinal rotation nearest its outward normal, because weapons can only face 0, 90, 180 or 270. Four PDC groups at 45° apart, each with an 85° arc, cover nearly every approach. Rail guns sit on the dock axes, so docking means flying down a gun barrel.
- **Size classes:**

  | Class | Radius | Width | Docks | Vaults |
  |---|---|---|---|---|
  | Small | 31 | 63 | 2 | 1 |
  | Medium | 37 | 75 | 4 | 2 |
  | Large | 43 | 87 | 4, plus 4 on the diagonals | 3 |

  Widths are the ring's; pods and equipment modules add a few tiles. Base game stations have 3,000 to 9,000 items, so the aim is 6,000 at most. With the tight ring, the preview's estimate (1.8 items per floor tile, one per wall) puts small relays at about 2,900 to 3,300, medium at 3,600 to 4,300 and large at 5,700 to 6,300 once furnished.

**Black site.** It's hidden, compact and armoured: a bunker carved into rock.

- **The rock:** a blob whose edge wanders with three random harmonics, generated from the seed. The game's own asteroid generator draws on Unity's shared random state, so it couldn't be repeated from a seed. Milestone 2 fills it with the game's rock walls and floors.
- **The bunker:** a compact rectangular complex carved out of the rock's middle. It sits a third of the rock's radius towards the far side, so the entry tunnel is long. Tunnels are 5 tiles wide with walls. The tunnel gets two checkpoints if it's long enough to keep them a third of its length apart, otherwise one. Weapon stubs only go out the bunker's sides, since one out the far side would open into the vault.
  - **One entrance:** a single hardened dock leads into a long entry tunnel with two security checkpoints.
  - **The core:** the vault cluster sits deepest, behind the reactor.
  - **No windows** anywhere.
- **Placement:**
  - On a surface, with orbit type `GROUND`.
  - In a remote orbit, as rock that reads like an asteroid on sensors until scanned.
- **Defence:** weapons only on short exposed stubs at the rock's edge, plus armed platforms around it flown by the AI.

### 3. Rooms

- **Ring:** the ring is cut into sectors at the spokes and dock pods. Each sector's room band is split along the ring into rooms of 4 to 9 tiles of arc, each with a door onto the corridor.
- **Core:** the wedges hold the reactor, life support and servers. The vault room in the middle is reached only through security doors.
- **Assigning roles:** required roles go first, each to the place that suits it:
  - security posts beside dock pods;
  - ammo storage behind weapon mounts;
  - vaults in the core.
  Fillers take the rest at random, weighted by the program.

### 4. Fit-out

- **Kits:**
  - **What they are:** small hand-built rooms, saved as ship templates named `fz_kit_<role>_<n>`, and tagged with role, size and door sides.
  - **How they're built:** in the game's own ship editor, so making new kits needs no code.
  - **Placing them:** a kit goes in the largest rectangle that fits its room. The rest of the floor gets scattered decor from the style.
  - **Vault kits:** hold the hack terminal and the data cores the mission reads.
- **Doors:** a hatch or door on each room's corridor edge. Airlocks go at the dock pods.
- **Power:** a conduit tree from the reactor along the corridors to every powered item, laid under the floors.
- **Life support and lights:** vents and lights per room, from the style.
- **Weapons:** placed with ammo loaded inside them, so FzAI's ammo rules apply.

### 5. Output and spawn

- **The template:** a `JsonShip` with:
  - `aItems`, `nCols`, `nRows` and `vShipPos`;
  - `aDockingPorts`, the IDs of the dock items;
  - `publicName` from the style;
  - `origin "$TEMPLATE"`, so it's prefilled with air.
- **Spawning:** registered in `DataHandler.dictShips` for this session only, then spawned with `SpawnStationFromJSON`. Its starting conditions include its FzAI pilot and the mission's own conditions.
- **The seed:** kept on the ShipCO, for debugging and to regenerate the preview.

## Validation

A layout is rejected and tried again when:
- a floor tile can't be reached from a dock;
- a room isn't sealed (floor next to space with no wall between);
- a powered item isn't on the conduit tree;
- a required room couldn't be placed;
- it goes over the item budget.

## Tools

- `fz site preview <type> <seed>` writes a PNG of the layout to the BepInEx folder, coloured by zone and room role. Shapes can be checked without flying anywhere.
- `fz site spawn <type> <seed> [faction]` spawns a site beside the player, the way `fz encounter` does.
- `fz site export <type> <seed>` writes the `JsonShip`, so it can be opened in the game's ship editor.

## Unknowns to check first

- Whether 1×1 walls join cleanly on stepped circle edges, or need corner pieces. The LA layouts suggest they're fine.
- Where multi-tile items sit: they use half-tile offsets, such as a PDC at `fX -0.9999995`. How fX and fY relate to rotation needs working out from templates.
- Docking port rules: which item, which edge, and whether an airlock pairing is needed.
- How power connects: by conduits next to each other, or through each part's `Electrical` `inputConnections` prop map, which the COHO weapons carry.
- Whether anything looks a ship up by its template name after spawning, once the generated template has gone from `dictShips` after a load.
- Load time and frame cost for 6,000 items when the player arrives.

## Milestones

1. **Shapes and the PNG preview:** relay and black site, with no spawning. Done.
2. **The shell:** floors, walls, dock pods and air, spawned as a station. Dock and walk around it. Built, not yet tried in the game: `fz site spawn <relay|blacksite> [size] [seed] [km] [faction]` (see "Milestone 2" below). Doors wait for rooms in milestone 3.
3. **Rooms and the first kits:** vault, security post, reactor and quarters.
4. **Systems:** power, lights and weapons, with the station defence pilot flying it. Built, not yet tried in the game (see "Milestone 4" below).
5. **Black site:** the carved asteroid shell.
6. **Data:** styles and site types from `fz/sites/`, plus a generic style. Built with milestone 4: site types, corporations and styles (see "Site and corporation data").

## Milestone 2: the shell

- **Building the shell:** `SiteShell.Build` turns a grid into items, still in plain C#.
  - Each walkable tile gets a floor for its zone, each wall tile a wall, and each rock tile solid rock (alternating `ItmWallRock051x1` and `ItmWallRock011x1`).
  - A black site's walls that only border tunnels and checkpoints are rock too.
  - Mounts stay hull until milestone 4.
  - The two looks are in `SiteStyle`: MSS white for relays, and TSDO plate and rock for black sites.
- **Coordinates:** as in base templates, `vShipPos` is the top left tile, and an item's `fX` and `fY` are `vShipPos.x + column` and `vShipPos.y - row`. The layout is centred on 0,0.
- **The docking port** (`ItmDockSys02Closed`) is 7 by 2 tiles and brings its own walls.
  - Unturned it faces north: the outer row has the port with two wall tiles either side of it, and the inner row has a wall two tiles either side of a 3-tile alcove. The relay's 5-wide pods match that, so the shell leaves its own walls off those tiles.
  - Rotations go anticlockwise: 0 faces north, 90 west, 180 south and 270 east.
  - The item's centre is half a tile in from the port, between its rows. This is checked against `FuelDepot`, `02` and `Argute Rapid Courier`.
- **The checks:** `SiteShell.Check` confirms each dock's hull walls, its alcove and that it opens onto space, then reachability and leaks. All 30 layouts across five seeds pass.
- **Item counts:** a shell is far under the estimate, since furnishing comes later:

  | Kind | Small | Medium | Large |
  |---|---|---|---|
  | Relay | 1,900 to 2,100 | 2,400 to 2,800 | 3,600 to 4,000 |
  | Black site | 2,100 to 2,700 | 3,400 to 4,100 | 4,800 to 5,600 |

- **Spawning:**
  - **The template:** `SiteSpawner` wraps the layout in a `JsonShip` named `FzSite_<Kind>_<Size>_<Seed>` and registers it in `DataHandler.dictShips` for the session.
  - **The station:** it's then spawned through `StationPlacer`, which `fz encounter` now shares.
  - **No rooms or air in the template:** it has no `aRooms`, since the game builds rooms from the walls, and spawning from a template sets `bPrefill`, which fills sealed rooms with air on the first full load.
  - **After a load:** if the game asks for a generated template that isn't registered, `SiteTemplatePatch` (a `DataHandler.GetShip` prefix) builds it again from its name.
  - **Clearing up:** test sites are FzSpawns of the `Test` kind, so `fz site clear` or `fz encounter clear` removes them. Mission sites will be their own kind, with a check that says when their mission has ended, so the sweep clears them.
- **Not in the shell yet:** lights, power, doors between areas, and weapons. The station will be dark inside.

## Built for doors

- **Door geometry:** the game's door (`ItmDoor01Closed`) is 5 by 1 tiles: a portal with two wall tiles either side, which it brings itself. It needs a straight 5-tile wall line across a passage 3 tiles wide. A hatch (`ItmHatch01Closed`) is 1 by 1 but needs a wall either side, so it needs a straight run of at least 3 wall tiles.
- **Shapes leave room for doors:**
  - Spokes, pods and tunnels are 5 wide with their walls, exactly a door's width.
  - Nothing walkable runs diagonally. A large relay's diagonal spokes are solid struts (`Strut`), kept for the look.
  - Diagonal docking pods run straight out along one axis until they're clear of the ring's curve.
  - The relay vault is a walled square inside a square corridor.
  - The black site vault spans the bunker behind a straight wall.
- **Doors the shapes place:**
  - **Relays:** one across each pod where it's clear of the ring, one at each end of every cardinal spoke, and one into the vault, on a side the seed picks.
  - **Black sites:** into the bunker, at both ends of each checkpoint, across the tunnel behind the dock, into the vault, and into each weapon stub.
- **Placing and checking:** `SiteGrid.PlaceDoor` places a door only where it fits. `SiteShell.Check` reports any door a generator asked for that didn't fit, and any whose walls or floor are wrong. Across five seeds of every kind and size, every door fits.
- **Rooms in milestone 3:** they will be axis-aligned rectangles, so their walls are straight for hatches. In the relay ring, any band tile left between the curved corridor and a room's straight wall becomes corridor.
- **Spawning beside the player:** `fz site spawn` and `fz encounter` put the station on the player's own orbit (`BodyOrbit.CreateBOFromShip`), a few km ahead or behind, so it matches their velocity. If the player isn't on a closed orbit, it falls back to a circular orbit and says how far off the velocity is.

## Black site layout

- **Rooms:** walled rectangles 6 to 10, 12 or 14 tiles a side (by size), clearly bigger than the 3-wide corridors, placed at random in the rock. Each has at least 3 tiles of rock round it and 7 between it and any other room. A site has up to 6, 8 or 11 rooms by size, in a rock of radius 29, 36 or 42.
- **The entry:** a straight tunnel from the room reaching furthest towards the entrance side, preferring one long enough for a checkpoint. It's cut first, with its checkpoints and dock pod, so the corridors route round it.
- **Corridors:**
  - **Shapes:** L or Z shapes of straight legs, 5 wide with their walls, cut through rock. They leave and enter rooms square to a wall, keep 5 tiles straight before turning, and keep a tile from each corner, so a door fits at each end. Doors stay 5 tiles apart.
  - **Routes tried:** each corridor tries 8 routes of each shape: aimed first, then with its doors anywhere along the walls and its turns anywhere between its legs.
  - **The tree:** a spanning tree joins the rooms, shortest corridors first. A room no corridor can reach is turned back into rock.
  - **Crossings:** a corridor may cross one other corridor square on, but not run alongside one, which would merge them into a wide open area.
  - **Loops:** 1 to 3 extra corridors between nearby rooms, never to the vault.
- **Vault and core:** the vault is the farthest room from the entry on a dead end of the tree, so it has a single corridor. The rooms nearest it are the core.
- **Weapon stubs:** straight out from rooms near the surface, one per side, never from the vault or the entry room.
- **Retries:** a layout with anything unreachable or leaking, or a door that doesn't fit, is made again from the next of 20 variants of the seed. Every seed still gives the same site. A site that can't be made at all says why (`BlackSiteShape.LastFailure`, shown by `fz site preview` and `fz site spawn`).
- **The checks:** across 20 seeds of every size, every site passes.
- **Items:** 2,500 to 3,100 for small, 3,900 to 4,600 for medium and 5,300 to 6,100 for large, mostly rock. Large sites can already pass the 6,000 aim before furnishing. Base game stations reach 9,000, so it may be fine; otherwise the rock radius is the lever.
- **Narrower doors:** the base game's hatch (`ItmHatch01Closed`) already gives a door for 1-wide passages: one tile, with a wall either side. Room-to-room doors in milestone 3 can use it; no new door item is needed for that.

## Furnishing

Per the user, rooms are furnished by rules in code rather than hand-built kits. Relays are breathable with minimal life support. Black sites are staffed; spawning their crews comes later, and for now they're furnished as staffed facilities.

- **The game's own rules:** `SiteCatalog` holds every item's footprint, socket adds, requirements, forbids and points, filled from the game's data. In game it comes from `DataHandler` (`SiteCatalogLoader`); in the preview tool it comes from the game's JSON. `SiteTiles` applies each placed item's adds to its tiles and checks a placement's requirements and forbids the same way the game does, so the game accepts what's placed.
- **Rooms:** rooms are the walkable areas between walls and doors, one zone each.
  - **Black site roles:** the generator names them. The entry room is security; core rooms are life support, reactor and servers; then quarters and a galley, then storage, armory, lab or more quarters.
  - **Relay roles:** taken from zones. The core wedges, biggest first, are life support, reactor and servers; the ring band is equipment; the vault is the vault.
- **Fixtures (`SiteFurnish.Rules`):** each role lists items with a fewest-to-most count. Fixtures go where they fit, preferring walls. They never cover the tiles in front of a door. They never leave a door front or a use point unreachable, or cut a room off from an area it opens onto: each opening keeps at least one clear tile.
  - **Use points under an item:** for an item whose use point is under it, a free tile beside it has to stay clear instead.
  - **Overflow:** a fixture short of its room's minimum goes into the largest equipment or storage area, so a site always gets its power and life support.
- **Lights:** wall lights (`ItmLitWall1x0White` on relays, `Orange` on black sites) are spaced so every tile is within 4 of one.
- **Power:**
  - **The source:** a "Power Uplink: Station" (`ItmReactorIC02IgnitionMini`), which base game stations use.
  - **Conduits:** `ItmConduit01` runs from the uplink to every powered fixture, light and door, routed under walls before floors. Each route is the cheapest path from the network already laid.
  - **Doors:** they become powered doors (`ItmDoor01ClosedOn`).
- **The checks:** with everything placed, every doorway and use point has to be reachable from the docks. Across 10 seeds of every kind and size, every site passes. Fixtures that didn't fit are tallied by role and item (`SiteLayout.Shortfalls`, printed by the preview tool). The remaining shortfalls are optional extras in tight relay core rooms and black site quarters.
- **Items:**

  | Kind | Small | Medium | Large |
  |---|---|---|---|
  | Relay furnishing | 600 to 700 | 800 to 900 | 950 to 1,050 |
  | Black site furnishing | 250 to 400 | 450 to 600 | 600 to 800 |
  | Relay total | 3,000 to 3,500 | 3,700 to 4,300 | 5,000 to 5,600 |
  | Black site total | 2,700 to 3,500 | 4,300 to 5,200 | 5,900 to 6,900 |

  Conduits and lights make up most of the furnishing.
- **The preview:** the PNG draws fixtures in brown, lights in yellow and conduits in cyan. `dotnet run -- debug <kind> <size> <seed> <x> <y>` in the tool prints the zones and items round a tile.
- **Not yet:**
  - **Vault contents:** data vaults and data cores are placed (`Design/Missions/DataVaults.md`); the vault's own hacking interface is still to build.
  - **Weapons and sensors:** placed in milestone 4, below.
  - **Crews:** black site staff need spawning.
  - **Not tried in the game:** whether the uplink keeps supplying power indefinitely, and how the station runs with this much furnishing.

## Locked doors

- **Locking:** per the user, every door on a site is locked. Players hack the lock, or get through some other way: cutting walls, or damaging or dismantling the door.
- **Door forms:** doors use `ItmDoor01ClosedLocked`, and once powered, `ItmDoor01ClosedOnLocked`, with the same footprint and power points as the unlocked forms.
- **PINs:** each door gets its own 4-digit PIN from the seed and its position (`SiteShell.DoorPin`, never the panel's default 1234). It's written to the door's lock panel when the template is made: the game's `LockDoor01Closed` panel with `strPIN` added, as base ship templates do (`SiteSpawner.LockPanel`).
- **Hacking:** the game's lock panel (`GUILocks`) already lets a character with `SkillHacking` hack a PIN lock, but only while the door is powered. Cutting a site's power would also stop its doors being hacked.
- **No crime:** per the user, nothing done at a site counts as a crime: relays are unmanned, and corporations don't report what happens at black sites. The game only records a crime from an interaction when the ship it happens on has a law (`CrimeManager`, `Ship.strLaw`), and sites are spawned with none, so keep it that way. Hacking a lock still costs 10 standing with the door's owner (`HackingCrimePenalty`'s `fFactionScoreChangeThem`); that's reputation, not crime.

## Standing and the transponder

- **The rule:** per the user, what the player does at a site only costs standing with its owner if they let themselves be identified (`SiteIdentity`).
- **Docking:** docking a ship of the player's to a site with its transponder on (`Ship.IsFlyingDark` false) identifies them to that site for good. That's `IsFzSiteIdentified` on the site's ShipCO, persisted. Docking dark doesn't. The player gets a log line either way.
  - Docks re-linked while loading (`bSyncOnly`) identify no one.
  - Not tried in game: whether every docking the player does, including through AI autopilot, goes through `Ship.Dock` with `bSyncOnly` false.
- **Aboard:** until a site knows who they are, standing costs from what the player or their crew do to things aboard it are skipped (`CondOwner.ApplyFactionReps` prefix). That covers interactions such as hacking a lock, and explosions.
  - The game only applies these costs when someone of the owner's faction sees it happen anyway, so on unmanned relays there are none. This matters for staffed black sites.
- **Fire:** shooting at a site from outside has no docking to go by, so it uses the transponder at the moment of firing. A player's ship flying dark costs no standing with the owner (`WeaponsSystem.ApplyFactionDamage` prefix). The user confirmed this extension of the rule.
- **Defenders:** once mission defenders are spawned, firing on them should follow the same rule, since they'll be spawns of the site's kind.

## Milestone 4: weapons, sensors, loot and vaults

### Weapons and sensors

- **Mounts:** each mount gets a weapon from its corporation's list (`aWeaponsPDC`, `aWeaponsRail`). If none of them fits, it takes any the site type uses (`SiteProgram.WeaponsFallback`).
- **Where a weapon sits:** across the hull wall, turned to face out. Its barrel rows are in space and a back row is on the wall, and on a mass thrower one more row hangs over the floor inside. The game's own sockets (`SiteTiles`) decide all of this. The floor just inside it and its use point are kept clear like a door's front.
- **Relay bays:** the ring is a stepped curve, so every relay mount is on a weapon bay. A bay is a rectangle of room, 7 across, pushed out from the ring until it clears the curve (`RelayShape.AddBay`), so it has a flat outer wall with floor behind. A PDC bay that would crowd a rail bay or pod slides along the ring until it fits.
- **Black site stubs:** each stub ends in a head 11 across and 3 deep, so there's a row of floor between its walls. Two weapon slots sit 4 apart on its flat outer face. The first two stubs each take a rail gun in one slot, and the rest are PDCs. Black sites now have 2 stubs when small and 3 otherwise.
- **Ammo:** each weapon is loaded from `aAmmo`. Rounds are child items (`SiteItem.ParentId`) in stacks: the first round of each stack is in the weapon and the rest in that round, as base templates do. Every round is an item, so loads are modest:
  - relays carry 20 to 60 PDC rounds and 5 to 15 rail rounds a gun;
  - black sites carry 30 to 80 and 10 to 20;
  - `fz encounter disarm` empties them.
- **Sensors:** 2 to 3 on relays and 1 to 2 on black sites, from the corporation's list, on hull facing space away from docks. A sensor's inner row hangs on the wall's inside face, so sensors are placed once rooms are known (`PlaceSensors`). Each has to leave its room joined up, like a fixture.
- **Power and pilot:** weapons and sensors join the conduit network like any powered item. `fz site spawn` gives the site the station defence pilot, hostile.
- **Checks:** 120 sites passed with no problems: 10 seeds of every kind and size as Generic, and 5 seeds each as Prokofiev and as Faure-Zhang. 98% of mounts are armed, and every relay mount is. With rounds, totals reach about 7,200 items for large black sites.

### Site and corporation data

- **Where it lives:** `data/fz/sites/{sitetypes,corps,styles}` is read from every mod in load order (`SiteDataLoader` in the game, `GameData.LoadSites` in the preview tool). A later entry with the same strName replaces an earlier one.
- **The program:** a site's program (`SiteProgram`) is its site type with its corporation's changes applied:
  - **fixtures:** the site type's per role, plus the corporation's;
  - **guns and sensors:** the corporation's, if it lists any;
  - **loot:** by role, or `Kind.Role`;
  - **style.**
- **Templates:** template names carry the corporation (`FzSite_<kind>_<size>_<seed>_<corp>`), and older names rebuild as Generic.
- **Blueprints:** each corporation's pool is filled in game from the fabrication recipes (`SiteDataLoader.FillBlueprints`). A vault's blueprints are picked from it when the template is made.
- **Commands:** `fz site corps` lists the corporations. `fz site preview` and `fz site spawn` take one, and the preview tool takes `--corp=<name>`.
- **Clutter:** each fixture entry lists alternatives, and each item placed is picked from them, so rooms vary: rack and bin sizes, crates, cargo, toolboxes, planters, posters, chairs, tables, TVs and the coffee machine. `fChance` makes an entry optional. An alternative the game doesn't have is skipped (the preview prints it as a note).
- **Loot:** a fixture entry with `bLoot` is a container. Each placed one gets the game's own loot spawner (`SysLootSpawner`, range 0) for its room's loot table, with the site type's `fLootChance`. That's 0.45 on relays and 0.8 on black sites. The spawner fills the container when the station spawns. The tables are in `loot/fz_loot_sites.json`.

### Fog of war

- **What's fogged:** per the user, every station the mod spawns (`FzSpawns`) is fogged when fully loaded (`SiteFog`), except its hull's outline, which shows from the start. A tile is revealed once one of the player's people aboard can see it, and stays revealed.
- **Sight:** rays to SightRange (24 tiles) on the station's tile grid, stopped by walls and doorways without `IsOpen`.
- **Saving:** the revealed tiles are a bitset in a prop map (`FzFog`) on the station's first docking port, so they survive save and load.
- **Drawing:** `FzSiteFogView` draws a solid quad over each fogged tile on the overlay camera, after the game's lighting, so walls, items and people under it don't show.
- **The mouse:** `SiteFogPatch` drops fogged things from every mouse pick (`CrewSim.GetMouseOverCO`), so they can't be hovered or selected.
- **Commands:** `fz fog` shows how much of the current encounter is revealed, `fz fog clear` reveals all of it for good, and `fz fog reset` fogs it again.
- **Not tried in the game:**
  - the overlay's depth;
  - whether a door tile has `IsOpen` while open;
  - how the outline looks from the docked ship before boarding;
  - its cost on a 7,000-item station.
- **Later:**
  - soft edges;
  - fogging the station on the nav map;
  - hiding sounds from fogged rooms;
  - letting cameras or sensor consoles aboard reveal rooms.

## Getting aboard

- **Comms:** per the user, a generated site has nobody aboard to answer comms, so hailing it offers what hailing an uncrewed ship does: forced docking, forced undocking and a status request (`TXTShipStandardInteractionsNoPilot`, `SiteDockingPatch`).
- **Docking:** the game's forced docking procedure. A forced docking still goes through `Ship.Dock`, so the transponder rule for standing applies.
- **Black sites:** they can also be moored to, like the asteroid they're carved into, from the nav station's mooring control. The crew then has to get in from the rock.
- **How:** a site stays a station everywhere else. It stops counting as one only inside the forced docking chain's ship test, and, for black sites, the mooring control's checks.
- **Not tried in the game:**
  - whether the forced docking chain completes against a station;
  - whether mooring holds a ship to a station on its orbit;
  - whether the game gives a site its own station pilot when it handles the docking commands.

## Floor loot and the equipment lockout

- **Floor loot:** per the user, rooms hold a little loose equipment and some munitions, by theme, so searching them pays on the way to the vault.
  - Site types and corporations list `aFloorLoot`: a role, a loot table, a count and odds. Each is a loot spawner on an open floor tile that drops its table there when the station spawns.
  - Relays get 2 to 4 piles, mostly tools, parts and the odd belt of rounds.
  - Black sites get 3 to 8: weapons and ammo in the armoury and security, personal effects in quarters, samples and medical kit in labs, and munitions in equipment rooms.
  - Corporations add their own munitions: Prokofiev Belter Bombs, CCRE Rancors, BNW Little Dragons (Suzhou and Hangzhou), Cloudbreak decoys, Generic 150mm. Ayotimiwa adds salvage and Peralta samples. Faure-Zhang labs may have a spent or basic beam former, or very rarely an exotic canister.
- **Equipment lockout:**
  - **The rule:** per the user, a site''s mounted equipment can''t be uninstalled or dismantled until its Equipment Lockout Controller is hacked for its kind: weapons, sensors, power or life support.
  - **The controller:** a copy of the base game''s terminal, placed in a random room. It''s hacked from the data vault''s panel, one kind at a time, so the player chooses what to free first. Each kind takes 240 standard seconds of skilled QUIET work and raises the site''s trace like any hack.
  - **Not tried in the game:** whether every uninstall and dismantle for site equipment is blocked, including ones queued as jobs.
