# FzAdditions notes for Claude

Save anything new worth remembering about this project here, not in the auto-memory folder.

## Project layout

Code is split into sections at the project root, each keeping the same Patches, UI and Util folders:

- `Propulsion/` holds everything directly about the jump drive, plus its art (`Art/`) and generator (`Tools/`).
- `Profiling/` holds the timing profiler run by the `fz profile` console command. Its patches have no Harmony attributes and use their own Harmony instance, so they go on only with `fz profile start` and come off with `fz profile stop`. Keep it that way: anything there with `[HarmonyPatch]` would be applied whenever the game runs.
- `Fabrication/` holds recycling, fabrication and blueprints. Recipes and materials load from every mod's `data/fzfabrecipes/` folder in load order (`FabData`), and learned blueprints are global state saved in the game save's custom infos (`Blueprints`).
- `Tweaks/` holds changes to how the base game runs, for speed, that leave what it does the same. `ShipWear` spreads a loaded ship's periodic wear over several frames, and finishes any still queued before a save.
- `Common/` holds everything else, shared code that doesn't know about any section.

Common doesn't reference a section. A section adds what it needs to Common when the plugin loads, through `ConsolePatch.AddCommand` and `CustomPrefabRegistry.Register`, from its own setup class (`PropulsionSection.Init`, `ProfilingSection.Init`, `TweaksSection.Init`, `FabricationSection.Init`).

Every console command the mod adds is a subcommand of one root command, `fz`, so the game's command list only gains one entry. `fz` on its own lists them, and `fz help <command>` explains one. Write a command's help and usage text with the `fz ` prefix. `FzAdditionsPlugin.cs` stays at the root and calls each section's setup. Namespaces weren't changed by the move. The data mod stays in `DataMods/`, since the game reads its layout.

## Line endings

Use CRLF line endings in all FzAdditions source and data files. The user asked for this after edits left files with LF or mixed endings.

After creating or scripting changes to `.cs` or `.json` files, check for bare LF and convert to CRLF. The Write tool and PowerShell here-strings can introduce LF.

## UI sizes are ratios, never pixels

Per the user, every UI element must be sized and placed in ratios of its parent, not pixels, so panels work on any screen size. Use anchors (`FzUIElement.SetTransform`, `anchorMin` and `anchorMax`) with zero offsets, never `sizeDelta`, pixel offsets or fixed pixel widths. Fonts may have point sizes, but use auto-sizing within a min and max where a label's space can change. If a control needs to keep a shape, such as a square button or the safety switch's proportions, work out its ratios for the parent's shape and note the screen shape they assume.

Two places can't be anchored, so they size in canvas units from ratios instead and recalculate when their parent changes size: slider handles (Unity's `Slider` moves the handle by its anchors, so `FzSlider` and `FzStickySlider` set its other side from the slider's size in `OnRectTransformDimensionsChange`), and mega tooltip sections (the game's tooltip stacks sections by height, so `FzTextModule` gives its text's height, padded in proportion to its font).

## Save/load consistency

The jump drive and NavCom must behave the same after a save and load as they did before it. Static state that only survives a load by accident is a bug.

Keep lasting state on the ship or its parts, where the game saves it: conditions with `bPersists` on the ShipCO, or prop maps on a part such as the nav station (prop maps on the ShipCO aren't saved). Anything held in static fields is a cache of that, and must be cleared in `SaveLoadPatch` when `CrewSim.EmptyScene` runs.

## Fz prefix on everything the mod ships

Per the user, everything the mod adds carries an Fz prefix so it can't collide with the base game or other mods:

- **Data files** start with `fz_`, for example `fz_items_jumpcell.json` or `fz_recipes_walls.json`. The exception is `mod_info.json`, whose name the game reads.
- **strNames** of new data contain `Fz`, for example `ItmFzJumpEnergyCell`, `StatFzJumpCharge`, `TIsFzJumpEnergyCellInstalled` or `Fz Jump Cell Simple Conditions`. Keep the game's own type prefix (Itm, Stat, Is, TIs, MS, ACT) in front.
- **Images** start with `Fz`, for example `jumpcell/FzPulsebankIdle_h0.png`, since the game keys images by their path under `images/`.

Some names deliberately don't have the prefix:

- A file that replaces a base game entry would have to keep its name. There are none now.
- Fabrication recipe strNames are the CO they cover. Material IDs are plain, such as `BaseMetals`. Both live only in the `fzfabrecipes` data, which nothing else reads, and another mod reuses a name on purpose to replace that recipe or material.

## Hand-written recipes

The CostModel generator deletes and rewrites every `*recipes_*.json` in `fzfabrecipes/recipes`. Recipes written by hand go in `fz_manual_*.json` there, which it leaves alone, for example water in `fz_manual_liquids.json`.

## Material silos

- A silo is any CO with `IsFzSilo`. It holds a material when it carries that material's silo condition (`IsFzSilo<ID>`), up to `StatFzSiloCapacity` units in all. `Fabrication/Util/Silo.cs` handles it.
- Contents are one condition per material, `StatFzMat<ID>`, with `bPersists`, so they're saved and carry over between forms. A material from another mod needs its own `StatFzMat<ID>` defined, or no silo can hold it, and the loader logs that.
- A silo's `StatMass` and `StatBasePrice` are its CO's starting values plus its contents'. `Silo.Refresh` sets them after every change, and a `ModeSwitchInternal` postfix sets them on the new form and empties a damaged one.
- Every silo shares one panel, `FzSiloGUI`, opened by the `FzSiloGUI` interaction and guipropmap. It builds its rows from the silo's conditions when it opens, so a new kind of silo only needs those three names in its installed CO (`aInteractions` and `mapGUIPropMaps`). The chosen material is kept in the silo's prop map as `FzSiloSelected`.
- The small silo (`ItmFzSiloSmall`) uses the base game's metallic canister items (`ItmCanister04*`) through `strItemDef`, so it has no art of its own yet.

## Recycler

- The recycler (`ItmFzRecycler`, with Off, Loose, Dmg and DmgLoose forms) uses the base Cargo Trade Kiosk's sprite and light through its own item definitions in `fz_items_recycler.json`. The base kiosk is indestructible and has only an installed form, so its socket layouts come from the 3x2 CO2 AtmoScrubber instead, and damage, destruction and dismantling use the AtmoScrubber's component loot.
- Its settings (working area width and depth, AUTO, RECYCLE) are in its prop map under `FzRecyclerGUI`, read and written through `Fabrication/Util/Recycler.cs` straight on the CO. The open panel holds a copy of the prop map from when it opened, so the panel goes through `Recycler` too, rather than `GetPropMapData`.
- Per the user, the working area is behind the recycler, on the side away from where its panel is used (its `use` point is at the bottom of its sprite). It's in the recycler's own frame, from its back edge (1 tile above its centre, along `tf.up`) outwards, `width` tiles across. An even width reaches one tile further left. Per the user, a loose item is in the area only when every tile it covers is an open tile (`Recycler.WhollyInArea`, from the item's `nWidthInTiles` and `nHeightInTiles`, which the game swaps when it turns an item, so they're always in world axes). The SETUP map shows it the same way up, with the recycler at the bottom.
- Per the user, a container in the area is recycled all at once with everything recyclable inside it, at every depth: `Recycler.Breakdown`, `ReservesIn`, `RunTime` and `Energy` all include its recyclable contents. The recycling process must keep to this, and leave contents with no recipe on the floor. WORKING AREA lists contents nested under their container.
- DUMP EXCESS (`FzDumpExcess`) makes material the ship has no room for get destroyed instead of stopping the recycler. `Recycler.NoRoom` simulates the fill to find what won't fit, shown red, or yellow with DUMP EXCESS on. AUTO forces the RECYCLE lamp on and locks it, without changing RECYCLE's own setting.
- Recycling runs in `Fabrication/Util/RecyclerProcess.cs`, from a `Powered.Run` postfix (`RecyclerPatch`), so it stops while paused, keeps to game time under time acceleration, and is skipped during `GUIFFWD`. Each update it draws that slice of the item's energy with `Powered.UserPowerExt` (short of `PowerConnected`, the run fails) and delivers the same share of the yield to silos and reserves, so material arrives live. The run (`FzCurrentItem`, `FzProgress`) is in the recycler's prop map and the lock is a persisted condition, so a run survives save and load.
- Per the user, an item being recycled is untouchable from the moment it's picked: it and everything in it get `IsFzRecycleLocked`, its inventory windows are closed, and `RecycleLockPatch` fails every interaction aimed at it (`Interaction.ValidateParticipants` postfix) and every condition trigger tested on it (`CondTrigger.Triggered` prefix), so the player and NPCs can't pick it up, open it or use it, and jobs searching for items pass it over.
- An item can't be left part recycled (it would be recycled whole again), so a run stopped partway by RECYCLE and AUTO going off, power failing, or the recycler going off, being damaged or uninstalled (handled in the `ModeSwitchInternal` postfix, as those forms get no power updates) deletes the item and keeps what was already delivered. A person in the area, or no room for the rest with DUMP EXCESS off, only pauses it. When a run ends, unrecyclable contents are dropped where the item was, unlocked. RECYCLE switches itself off after a pass of the area; AUTO keeps waiting.
- Per the user, locked items no recycler is working on (a recycler destroyed outright, or a run lost some other way) are cleared by `RecyclerProcess.SweepOrphans`, which isn't run all the time: `RecycleSweepPatch` runs it on every loaded ship before a save (`LoadManager.SaveGameData`) and on each ship as it fully loads (`Ship.PostGameLoad`). An item is rightly locked only while it, or what it's in, is a recycler's current item. Orphans that can be recycled go as a finished run's would; ones that can't are just unlocked.
- The recycling effect (`FzRecycleBeams`, driven by `FzAreaProjector`): the item's renderer is hidden and `ItemCut`, a child of the item, draws a copy of its quad with its own material and property block cut off at the scan line, so it disappears with its own look and lighting. Per the user the cut is square to the way the recycler faces, eaten from the item's far side in towards the recycler: the recycler's up axis, taken into the item's frame, picks which of the item's axes the cut follows (both turn in quarter turns). Two additive beams on the overlay camera from the recycler's back corners sweep across the cut, with a glowing cut line, and, per the user, a dense stream of motes (60 a beam) in the colours of the materials and reserves the item yields, picked at random by how much of each, flows along each beam back to the recycler. Per the user it animates on its own clock, which runs on real time only while `CrewSim.Paused` is false, so it freezes with the game. Its share is carried between power updates by the game time since the last one.
- Per the user, walls block the area: `Recycler.OpenTiles` looks up each area tile on the ship, blocks walls and non-ship tiles, then keeps only the tiles reachable tile to tile from the row against the recycler, so it never works through a wall. Blocked tiles are red on the SETUP map.
- The silo panel's screen and the recycler's SHIP RESOURCES screen are the same `FzMaterialScreen`.
- The working area is projected by `FzAreaProjector` (one object made when the plugin loads, cleared on `EmptyScene`), which scans the loaded ship and its docks every half second and moves each `FzAreaProjection` with its recycler every frame. A projection is the game's item quad (`prefabQuad`) with its mesh swapped for the stripe band and an item material from `DataHandler.GetMaterial`, at z = -0.1 (an fZScale of 0.025), between floors and loose items. The light sprite shader was tried first, but it drew over items and people, so it's an item, lit by the room. Transparent pixels in the item shader show space rather than the floor, and a striped texture looked uneven and left a fringe, so each stripe is solid geometry: slanted parallelograms with nothing drawn in the gaps. Stripes are laid by distance along the band's middle line. Along the straight runs they're placed as they are, so they all lean the same way (stretching a whole side as a mitred trapezoid made them stand up and turn blocky towards its end); each corner square is solid, per the user (bending stripes through it left the outside of the bend open), and the stripes run into it and out on the next side at the pace of a quarter circle round, so they still flow round the area. The spacing is stretched so a whole number of stripes fits the loop, leaving no seam. The mesh is rebuilt every frame with the stripes moved from the clock, so they run round the area like a hologram (slow idle and preview, fast working, still when blocked). The stripe PNGs (one for each look, made by `Fabrication/Tools/make_area_stripes.ps1`) only give the game an image to build an item material from and give the look's colour; the texture is swapped at runtime for a solid one. Item materials are cached and shared by image.
- Power follows the field emitter: `FzRecycler` power info switches between the Off and On forms, and `TIsFzRecyclerWorking` means installed, powered, on and undamaged. Its front is the bottom of the sprite, where its `use` point is.

## Machine tiers

Per the user, recyclers and fabricators come in tiers, set by conditions in each form's `aStartingConds` (defined in `fz_conditions_workarea.json`): `StatFzAreaMaxWidth` and `StatFzAreaMaxDepth` for the largest working area, and `StatFzWorkSpeed`, a multiple of the materials' run times (`WorkArea.MaxWidth`, `MaxDepth`, `Speed`). Missing, they're 3×3 and 1, and no tier goes past 15×15. Progress is kept in standard seconds of work, so a faster machine does more of them each game second, uses the same energy per item and draws more power for it. Panels divide times by the speed. SETUP's map is laid out when the panel opens for the machine's own largest area, with a grid kept square by an `AspectRatioFitter`. The Basic Recycler is 3×4 at speed 1; the Basic Fabricator is 3×3 at speed 1.

## Matter beam formers

- Per the user, recyclers and fabricators only work with a matter beam former in their one-slot inventory. Like a CO2 scrubber's cartridge, every form of each machine has `IsContainer`, a 1×1 container and the `Inventory` interaction, and its `strContainerCT` (`TIsFitContainerFzBeamFormer`, needing `IsFzBeamFormer`) keeps anything else out.
- Three tiers, `ItmFzBeamFormer01` to `03` (Basic, Advanced, Superior), with 100k, 500k and 1M material units of life, priced 100k, 200k and 600k. Their life is `StatFzBeamLife`, persisted; `StatFzBeamLifeMax` is the rating. They use the base game's `ItmAmmoPowerCell01` item definition and sprite as a placeholder.
- Every material unit a machine recycles (the yield, not reserves) or fabricates wears one unit off (`BeamFormer.Wear`). A worn out former is destroyed and its tier's spent former (its CO name plus `Spent`, such as `ItmFzBeamFormer01Spent`) dropped in front of the machine. Per the user, a spent former recycles for half its tier's non-exotic materials and no exotics. A run only starts with a former good for the whole item, and pauses if the former is taken out or swapped for one too worn for the rest.
- Their recipes are Specialised, hand-written in `fz_manual_beamformers.json`, and need Exotics and large amounts of precious metals, rare earths, alloys, silicates and ceramics. No blueprint is known from the start.

## Fabricator

- The fabricator (`ItmFzFabricator`, "Basic Fabricator", with Off, Loose, Dmg and DmgLoose forms) is a copy of the recycler's data on the same kiosk sprite, with its settings in its prop map under `FzFabricatorGUI`. `WorkArea` holds what the two machines share (area, open tiles, occupied tiles, current item and progress, prop map access, picking the prop map by `IsFzFabricator`), and `FzMachineGUI` the panels they share (POWER, SHIP RESOURCES, SETUP). `FzAreaProjector` projects both.
- `Fabrication/Util/Fabricator.cs` holds its settings and sums; `FabricatorProcess.cs` runs it from the same `Powered.Run` postfix as the recycler. The chosen recipe is `FzRecipe`; a run is `FzRunRecipe`, plus `FzCurrentItem` (an unfinished item) or `FzTarget` (a stack), and `FzProgress`.
- Per the user, stackable items are built one at a time onto a stack of the same thing in the area with room, and that stack is locked (`IsFzFabricateLocked`) while it's built, the same way the recycler locks items. The unit is only made, and stacked with `StackCO`, when it's done. An item that doesn't stack, or with no stack to go on, is put in the area when the run starts, locked and `IsFzUnfinished`, to hold its space, and grows in through `ItemCut` as the run goes. `Locks.IsLocked` covers both locks for `RecycleLockPatch`.
- Placement is its own fit test (`Fabricator.FindSpot`): the item's unturned size from its item definition, on open area tiles no loose item or person is on (`WorkArea.Occupied`, keyed by the ship's own tile index, `WorkArea.TileIndex`). Don't match tiles by rounding positions: tile centres are halfway between whole numbers, and a machine two tiles deep has its centre between rows, so rounding mixed up neighbouring rows and showed AREA FULL with room left. `Item.CheckFit` can't be used from a machine.
- A run starts only with a whole item's materials in the silos, then draws them as it goes; short, it waits. A run stopped partway deletes the unfinished item and lets the stack go; what it used is lost. `FabricatorProcess.SweepOrphans` runs with the recycler's sweep.
- Per the user, FABRICATE builds COUNT items, counting down and switching off at 0. AUTO forces FABRICATE's lamp on, ignores COUNT, and fills the area, retrying every 5 game seconds once it's full or short (`FabricatorProcess.nextTry`, a cache cleared on `EmptyScene`).
- Per the user, BLUEPRINTS is browsed by category. `FabData.Categories` is a tree of `FabCategory` built once from the recipes' dotted `strCategory` paths as the data loads, each node holding the blueprints in it and below, and the panel shows only categories with a known blueprint. Subcategories come before blueprints, in their own colour with a `>` marker and their known blueprint count. The top line is breadcrumbs. BACK and OPEN keys, and the arrow keys (up and down move, left back, right open; read through the Input System's `Keyboard.current`, as the game's own keypad does, and ignored with Shift held, which is the game's panel switching). A highlighted blueprint is the chosen recipe.
- Per the user, FILL ITEMS (prop `FzFill`) fills canisters from the ship's reserves, and while it's on COST lists what a fresh one would take of each reserve (`Reserves.DefCapacity`, from its definition) against the ship's.
- FILL ITEMS fills canisters from the ship's reserves. A made item with a reserve's vessel condition (`IsVesselO2` and so on) is emptied when it's spawned, and with FILL on is filled from the ship's installed canisters when done. Items that hold gas without a vessel condition, such as oxygen candles, keep what they start with.

## conditions_simple files need unique strNames

Ostranauts loads every conditions_simple file of a mod into one dictionary keyed by the file's strName. Two files both named "Simple Conditions" collide with no log message. The conditions in the losing file don't exist, `DataHandler.GetCond` returns null for them, and they silently vanish from aStartingConds. Any condtrig that requires them fails.

Give each new conditions_simple file its own strName. Suspect this when a data item loses an action or a condtrig fails with nothing in the log.

## Item sprites and looks

- Animated looks are sprite sheets read from the top left, played by `objAnimation`. The normal and damage maps need the same sheet layout.
- Each `Item.SetAlt` call with an animation adds another animation listener, and the game only removes them when the item is destroyed. `FzCellLook` removes the old listener before changing looks.
- Discharge looks animated with `objAnimation` drew their whole sheet across the cell in game, though the deployed definitions had the animation. The cause wasn't found. The looks changed every fraction of a second, unlike charging, whose sheets drew fine. Discharge now flashes by `FzCellLook` switching still looks itself.
- Light sprites (a light's `strImg`) draw their whole texture and ignore the texture scale and offset that sheets use, so an animated glow needs one image per frame.
- Simple conditions such as `IsPowered` aren't saved. After a load they're missing until the part's first `Powered.Run`, which waits for the game to unpause. `JumpCell.HasRun` says whether a cell has had that update.
- `Item.NotifyPreRender` only runs for items that have a light with a real `strColor`. Sprite-only lights (`strColor` Blank) don't run it.
- The jump cell's looks are COOverlays in `fz_cooverlays_jumpcell.json`, applied visually by `FzCellLook`. Its art and that COOverlay file are both generated by `Propulsion/Tools/make_pulsebank.ps1` from `Propulsion/Art/pulsebank`, so edit the script rather than the JSON.

## Sound

The game loads every sound effect from its own built-in assets (`Resources.Load<AudioClip>("Audio/...")`), so an `audioemitters` entry can only name a clip the game already has. New sounds have to come from the plugin.

The jump sequence's sound is synthesised live by `Propulsion/UI/FzJumpAudio.cs`, on Unity's audio thread, using the building blocks in `Common/Util/AudioDsp.cs`. `FzJumpEffect` drives it with the charge's progress each frame, wave landings, the hit and power-downs, so it keeps step with the visuals at any game speed. It plays through the game's `PrefsEffects` mixer group, so the effects volume applies. Tune the sound in `FzJumpAudio.Synth`.

## Ostranauts data modding wiki

https://ostranauts.wiki.gg/wiki/Modding/Data_Modding is a fan-maintained guide to Ostranauts JSON mods, last edited 2025-11-12 with no target game version stated. It's useful for concepts and sub-page links, such as Modding/CondOwners, Modding/Interactions and Modding/Loot.

Treat the base game JSON and the decompiled `DataHandler` as authoritative. As of 2026-11-01, these wiki claims don't hold for this install:

- `loading_order.json` is in `Ostranauts_Data/Mods/`, not `Ostranauts_Data/`.
- The log is `Player.log`, not `output_log.txt`.
- Not every item needs a COOverlay of the same name. Most base items are tied to a condowner through `strItemDef`. Per the user, COOverlays avoid redefining similar objects and save memory in save files, and aren't required for every condowner.
- Socket arrays aren't always 9 elements.
- Its sample condtrig fields (`aCondsToTest`, `aCondsToAdd`) aren't used in base condtrigs. Use `aReqs`, `aForbids` and `aTriggers`.
