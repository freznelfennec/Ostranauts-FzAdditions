# FzAdditions notes for Claude

Save anything new worth remembering about this project here, not in the auto-memory folder.

## Project layout

Code is split into sections at the project root, each keeping the same Patches, UI and Util folders:

- `Propulsion/` holds everything directly about the jump drive.
- `Profiling/` holds the timing profiler run by the `fz profile` console command. Its patches have no Harmony attributes and use their own Harmony instance, so they go on only with `fz profile start` and come off with `fz profile stop`. Keep it that way: anything there with `[HarmonyPatch]` would be applied whenever the game runs.
- `Fabrication/` holds recycling, fabrication and blueprints. Recipes and materials load from every mod's `data/fzfabrecipes/` folder in load order (`FabData`), and learned blueprints are global state saved in the game save's custom infos (`Blueprints`).
- `Tweaks/` holds changes to how the base game runs, for speed, that leave what it does the same. `ShipWear` spreads a loaded ship's periodic wear over several frames, and finishes any still queued before a save.
- `Missions/` holds missions at temporary corporate sites. So far that's the station defence pilot (`FzStationDefensePilot`) and the `fz encounter` test station (`TestEncounter`), the site generator (`Missions/Util/SiteGen` for shapes and shells, `fz site preview`), and `fz site spawn`, which registers a generated template (`SiteSpawner`) and spawns it beside the player (`StationPlacer`, shared with `fz encounter`). `SiteTemplatePatch` rebuilds a generated template from its name if the game asks for one after a load, which is plain C# so `Design/Missions/Tools/SitePreview` can run it outside the game. Its design and milestones are in `Design/Missions/StationGeneration.md`.
- `Common/` holds everything else, shared code that doesn't know about any section. That includes the SMES units and SMES reactor taps, with their art (`Common/Art/smes`) and its generator (`Common/Tools/make_smes.ps1`), and `ReactorFuel`, the reactor's extra burn, and the mod's own AI (`Common/Util/AI`).

Common doesn't reference a section. A section adds what it needs to Common when the plugin loads, through `ConsolePatch.AddCommand`, `CustomPrefabRegistry.Register`, the SMES hooks (`Smes.AddRechargeHold`, `Smes.AddDischargeCheck`, `Smes.PowerSwitched`) and AI pilot types (`FzAI.RegisterPilot`), from its own setup class (`PropulsionSection.Init`, `ProfilingSection.Init`, `TweaksSection.Init`, `FabricationSection.Init`, `MissionsSection.Init`).

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

- **Data files** start with `fz_`, for example `fz_items_smes.json` or `fz_recipes_walls.json`. The exception is `mod_info.json`, whose name the game reads.
- **strNames** of new data contain `Fz`, for example `ItmFzSmes01`, `StatFzSmesCharge`, `TIsFzSmesInstalled` or `Fz SMES Simple Conditions`. Keep the game's own type prefix (Itm, Stat, Is, TIs, MS, ACT) in front.
- **Images** start with `Fz`, for example `smes/FzSmesIdle_h0.png`, since the game keys images by their path under `images/`.

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
- Per the user, BLUEPRINTS is browsed by category. `FabData.Categories` is a tree of `FabCategory` built once from the recipes' dotted `strCategory` paths as the data loads, each node holding the blueprints in it and below, and the panel shows only categories with a known blueprint. Subcategories come before blueprints, in their own colour with a `>` marker and their known blueprint count. The top line is breadcrumbs. BACK and OPEN keys, and the arrow keys (up and down move, left back, right open; read through the Input System's `Keyboard.current`, as the game's own keypad does, and ignored with Shift held, which is the game's panel switching). Per the user, a keybind holds down its button on the panel for as long as the key is held, with its pressed sprite and sounds: `FzHoldBtn.KeyDown` and `KeyUp` call its `GUIAudioBtn`'s pointer handlers for the sounds and set the image's `overrideSprite` to the pressed sprite, as the Button's sprite swap does. UP and DOWN repeat while held, by key or mouse. A highlighted blueprint is the chosen recipe.
- Per the user, `fz bplearn target` learns the blueprint of what's selected, whichever form it is. In play a click doesn't bracket an item: it picks it for the info panel, `GUIMegaToolTip.Selected`, so that's read first, then `CrewSim.GetBracketTarget` (crew, or anything in ship edit). `FabData.GetFabricableFor` resolves a CO to its fabricable recipe: installed and loose forms share a recipe, but damaged and patched forms have unfabricable recipes of their own, so it also tries a COOverlay's base and the name with trailing state words (Dmg, Patch, Loose, Off, On) taken off, as a CO and as a recipe name.
- Per the user, FILL ITEMS (prop `FzFill`) fills canisters from the ship's reserves, and while it's on COST lists what a fresh one would take of each reserve (`Reserves.DefCapacity`, from its definition) against the ship's.
- FILL ITEMS fills canisters from the ship's reserves. A made item with a reserve's vessel condition (`IsVesselO2` and so on) is emptied when it's spawned, and with FILL on is filled from the ship's installed canisters when done. Items that hold gas without a vessel condition, such as oxygen candles, keep what they start with.

## conditions_simple files need unique strNames

Ostranauts loads every conditions_simple file of a mod into one dictionary keyed by the file's strName. Two files both named "Simple Conditions" collide with no log message. The conditions in the losing file don't exist, `DataHandler.GetCond` returns null for them, and they silently vanish from aStartingConds. Any condtrig that requires them fails.

Give each new conditions_simple file its own strName. Suspect this when a data item loses an action or a condtrig fails with nothing in the log.

## Lore: Faure-Zhang

Per the user, the mod's technology is made by **Faure-Zhang**, a corporation chosen on 2026-11-06. Its initials match the mod's Fz prefix. Use it in item descriptions and other lore text.

- How widely each technology is known:
  - Fabrication and recycling (recyclers, fabricators, silos, beam formers) are public and widely distributed.
  - Deflector shields (not built yet) are heavily restricted.
  - SMES units, SMES reactor taps and jump drives are secret, known only to top executives and scientists.
- Backstory draft, built from the base game's lore:
  - It was founded in 2058 on Voltaire, Deimos, by Dr. Hélène Faure, a field physicist at Voltaire University, and Zhang Wenli, a Tharsis waste engineer.
  - Its first contract was licensed reclamation for Tharsis, during the game's crackdown on unlicensed recyclers.
  - It sells to every bloc, and earns from beam formers, whose exotic matter only it refines.
  - Shields are leased, sealed units for navies only.
  - The secret work hides inside an "exotic-matter refinery" project.
  - The base game's radioactive ghost ships near Venus can be its failed test hulls.
- The game's present is about 2079. Item text is dry and spec-like, often ending on a wry line, and ads are one-line punny slogans.

## SMES units and reactor taps

- Per the user, SMES units (superconducting magnetic energy storage, "Basic SMES Unit", `ItmFzSmes01`) and SMES reactor taps ("Basic SMES Reactor Tap", `ItmFzSmesReactorTap01`) are generic devices, used by more than the jump drive. In the lore they're Faure-Zhang's secret technology. They were the jump energy cell and jump drive reactor tap before 2026-11-06.
- They live in Common (`Smes`, `SmesTap`, `SmesConstants`, `FzSmesGUI`, `FzSmesReactorTapGUI`, `FzSmesLook`, and the `FzSmesModule` and `FzSmesTapModule` tooltips), with their own save/load and tooltip patches. Their GUIs are registered in `CustomPrefabRegistry`'s static constructor.
- What draws on them hooks in from its section's setup. The jump drive (`PropulsionSection.Init`) holds off their own recharge during a series time skip (`Smes.AddRechargeHold`), shows them discharging while a jump charges (`Smes.AddDischargeCheck`), and cancels on a panel power switch (`Smes.PowerSwitched`).
- The `01` on their CO names is the tier, like the beam formers, so later tiers can be added beside them.

## Item sprites and looks

- Animated looks are sprite sheets read from the top left, played by `objAnimation`. The normal and damage maps need the same sheet layout.
- Each `Item.SetAlt` call with an animation adds another animation listener, and the game only removes them when the item is destroyed. `FzSmesLook` removes the old listener before changing looks.
- Discharge looks animated with `objAnimation` drew their whole sheet across the SMES unit in game, though the deployed definitions had the animation. The cause wasn't found. The looks changed every fraction of a second, unlike charging, whose sheets drew fine. Discharge now flashes by `FzSmesLook` switching still looks itself.
- Light sprites (a light's `strImg`) draw their whole texture and ignore the texture scale and offset that sheets use, so an animated glow needs one image per frame.
- Simple conditions such as `IsPowered` aren't saved. After a load they're missing until the part's first `Powered.Run`, which waits for the game to unpause. `Smes.HasRun` says whether a SMES unit has had that update.
- `Item.NotifyPreRender` only runs for items that have a light with a real `strColor`. Sprite-only lights (`strColor` Blank) don't run it.
- The SMES unit's looks are COOverlays in `fz_cooverlays_smes.json`, applied visually by `FzSmesLook`. Its art and that COOverlay file are both generated by `Common/Tools/make_smes.ps1` from `Common/Art/smes`, so edit the script rather than the JSON. The source art keeps the artist's name for it, Pulsebank, and the script writes it out under the SMES names.

## Sound

The game loads every sound effect from its own built-in assets (`Resources.Load<AudioClip>("Audio/...")`), so an `audioemitters` entry can only name a clip the game already has. New sounds have to come from the plugin.

The jump sequence's sound is synthesised live by `Propulsion/UI/FzJumpAudio.cs`, on Unity's audio thread, using the building blocks in `Common/Util/AudioDsp.cs`. `FzJumpEffect` drives it with the charge's progress each frame, wave landings, the hit and power-downs, so it keeps step with the visuals at any game speed. It plays through the game's `PrefsEffects` mixer group, so the effects volume applies. Tune the sound in `FzJumpAudio.Synth`.

The recycler's and fabricator's sounds are positional, heard from each machine. Sound added in `OnAudioFilterRead` isn't placed in 3D by Unity, and a synth per machine wouldn't scale, so `Fabrication/Util/MachineSounds.cs` renders short clips once as the plugin loads (`FzMachineSound.Prepare`, about a quarter of a second). Each machine plays them through its own 3D sources (`FzMachineSound`), set up like the game's CO2 scrubber: the `FalloffLogShallowMin20Max500` curve, a 20 to 50 unit range and the `Ambient` mixer group. Per the user, they're subtle and slow, like heavy printers driven by stepper motors, since a ship may have many. Per the user, a constant low hum carries the sound while a machine runs, with low, sparse printer moves on top, and starting is a swell of hum, so short runs aren't jarring. A machine keeps sounding as working for 2.5 s after a run (`LingerTime`), so runs back to back sound like one. Each machine plays up to 3% sharp or flat and from its own place in the loop. `FzAreaProjector` drives them from each machine's state, and they pause with the game. `MachineSounds` is plain C#, so it can be compiled with `AudioDsp.cs` by PowerShell's `Add-Type` to render previews outside the game.

## Spawned stations and ships

Per the user, everything the mod spawns into the system is marked so it can be cleared up if it's orphaned. `Common/Util/FzSpawns` handles this:

- **Marks:** a spawn carries two persisted conditions, `IsFzSpawned` and `IsFzSpawn<Kind>`.
- **Kinds:** sections register theirs with `FzSpawns.RegisterKind`. Each gives a unique registration prefix, an optional check for whether a spawn is still wanted, and an optional legacy condition, which the sweep moves onto the current marks.
- **Registrations:** spawns take theirs from `FzSpawns.NewRegID(kind)`.
- **Spawning a station:** use `StationPlacer` with a `Kind`. The game adds the orbit and ship before the starting conditions, so a spawn that throws or comes out unmarked is rolled back with `FzSpawns.RemoveRemains`.
- **The sweep:** `FzSpawns.Sweep` removes these orphans:
  - spawns whose kind says they aren't wanted;
  - spawns of a kind no section registers any more;
  - stations whose orbit has gone;
  - unmarked ships with a kind's prefix, which failed partway;
  - orbits with a kind's prefix but no ship.

  It runs once after each load, on the first AI update, and before each save. It never removes what the player is aboard or docked to.
- **The test kind:** `Test` (`FZT-`, legacy `IsFzTestEncounter`) covers `fz encounter` and `fz site spawn`. It's always wanted.
- **Console:** `fz spawns` lists spawns and orphans, and `fz spawns sweep` sweeps now.

## The mod's AI

- Per the user, write AI in capitals in names, as in `FzAI`, `Util.AI` and `IsFzAIPilot`, never `Ai`.
- `Common/Util/AI` is the mod's own AI, run apart from the game's `AIShipManager`, so the game never runs, saves or despawns our pilots. It can be extended to drones later.
- **Pilots:** a pilot (`FzPilot`) is an `IAICharacter`, so the game's own commands can go in its tree beside ours. `FzThreatPilot` adds `IsHostileTo` and a threat range for `FzFindThreats`.
- **Pilot types:** each section registers its types with `FzAI.RegisterPilot`. A ship is flown while its ShipCO has the type's persisted condition, `IsFzAIPilot<Type>`, which each section defines in its data. The alert level is `StatFzAIAlert` (0 calm, 1 alerted, 2 hostile), also persisted.
- **Pilots are a cache:** found every 2 real seconds by a scan of `dictShips`, and cleared on `EmptyScene`.
- **When pilots run:** `FzAI.Tick` runs from an `AIShipManager.Update` postfix, which the game only calls while unpaused, so pilots keep to game time. Each pilot sets its own `Interval` in game seconds, and a frame runs at most 16 pilots that are due. During `GUIFFWD` skips a pilot gets `FastForward` instead of `Think`.
- **Firing:** pilots fire through `FzWeapons.Fire`. It uses `ShootShallow` on a shallow ship and `ShootAuto` on a loaded one, so rounds are used up either way. It also adds the game AI's aiming delay (arc over targeting speed) to the reload. `FzShoot` fires every gun whose real arc and range cover the target; the game's own AI shoot command uses a 5° cone, which suits ships that turn but not stations.
- **Spent rounds:** when a flown ship loads, `FzWeapons.ClearSpentRounds` removes rounds marked spent while it was shallow.
- **Test encounter:** `fz encounter [km] [template] [faction]` spawns a hostile `Station_COHO` (Corsair's Hollow: a railgun and two PDCs, all facing one way, with 3 to 5 rounds each). It goes in its own orbit around the body nearest the player, at the player's distance from that body. The orbit's clock (`fPeriodShift`) and direction (`nOrbitDirection`) are set so the station is at the chosen spot now and goes round the way the player is moving. The station is turned so its guns face the player. `fz encounter clear` removes test stations (FzSpawns kind `Test`), except one the player is docked to. `fz ai` lists flown ships.
- **Station construction:** a `JsonSpawnStation` needs `nConstructionProgress = 100`, or the station spawns its construction template instead of its layout. A generated station template needs an `objSS` (an empty `JsonShipSitu` will do): `StarSystem.SpawnStation` sets `json.objSS.bIsBO` without checking, and fails partway, after adding the orbit and the ship but before the starting conditions.
- **Docking ports:** `ItmDockSys02Closed` is 7 by 2 tiles and brings its own walls: unturned it faces north, its outer row is the port with two walls either side, and its inner row has a wall two tiles either side of a 3-tile alcove. Rotations go anticlockwise (0 north, 90 west, 180 south, 270 east), and its centre is half a tile in from the port. In templates, `vShipPos` is the top left tile, `fX = vShipPos.x + column` and `fY = vShipPos.y - row`. A template's `aDockingPorts` lists its docking items' IDs.
- **Doors:** `ItmDoor01Closed` is 5 by 1 tiles and brings its own walls: a portal with two wall tiles either side, unturned lying east to west across a passage running north and south (rotation 90 for east to west passages), centred on its portal. It forbids anything else on its 5 tiles. So a door needs a straight 5-tile wall line across a passage 3 tiles wide inside its walls. `ItmHatch01Closed` is 1 by 1 but needs a wall tile either side of it. Per the user, generated sites are laid out for doors: spokes, pods and tunnels are 3 wide inside their walls, nothing walkable runs diagonally (a large relay's diagonal spokes are solid `Strut`s), and rooms are to be axis-aligned rectangles. `SiteGrid.PlaceDoor` checks a door fits, and `SiteShell.Check` reports doors that don't.
- **Item sockets:** an item definition's `aSocketAdds` is `nCols` by rows, starting at its north edge unturned. `aSocketReqs` and `aSocketForbids` are padded a tile all round. Each entry names a loot entry (`TILWallAdds`, `TILFloor` and so on) whose `aCOs` are tile conditions (`IsWall`, `IsObstruction`). An item fits where each required tile has all of a tag's conditions and each forbidden tile has none. A cooverlay takes its item definition and `mapPoints` from its `strCOBase`. `mapPoints` are `name,x,y` in sixteenths of a tile from the item's centre (`CondOwner.GetPos`), so `PowerSource,0,16` is the tile north. `SiteCatalog` and `SiteTiles` simulate all of this from the game's data.
- **Power:** installed `IsPowerGen` and `IsPowerStorage` items feed their `PowerOutput` point's tile, and power spreads through edge-adjacent tiles that are `IsPowerPath` (conduits, `ItmConduit01` on `ItmConduit00`, which needs nothing under it and allows one per tile) (`TileUtils`). A powered item draws from the sources on the tiles under its `Power*` input points (`Powered.UsePower`). A wall light's `PowerSource` is the wall tile behind it, so base stations run conduits under their walls. Stations draw on a "Power Uplink: Station" (`ItmReactorIC02IgnitionMini`).
- **Locks and crime:** a locked door is its `Locked` form (`IsLocked`). Its PIN is `strPIN` in its "Panel A" prop map (the `LockDoor01Closed` guipropmap plus `strPIN`); without one, the panel's code is 1234. `GUILocks` lets a character with `SkillHacking` hack a powered PIN lock, and runs `HackingCrimePenalty` (crime "damage", -10 standing with the door's owner). `CrimeManager` only records an interaction's crime when the actor's ship has a `strLaw`, so per the user generated sites are spawned with no law.
- **Standing and transponders:** per the user, standing costs at a site depend on whether the player docked with their transponder on (`SiteIdentity`, `IsFzSiteIdentified`, `SiteStandingPatch`). `Ship.IsFlyingDark` is true when the transponder antenna is off or has no code. Interactions change standing through `objThem.ApplyFactionReps(objUs, fFactionScoreChangeThem)`; for a non-person, `CondOwner.ApplyFactionReps` only applies a change if a person of its faction sees it. Weapons fire changes standing ship to ship through the ships' owners (`WeaponsSystem.ApplyFactionDamage`).
- **Matching the player's velocity:** per the user, a spawned station matches the player ship's velocity. `StationPlacer` fits the player's orbit round the body pulling hardest on them with the game's own `BodyOrbit.CreateBOFromShip` (it returns null for an open orbit, or one it can't match to within about 0.1 m/s), from `StarSystem.GetGreatestGravBO`, then puts the station on that orbit `Km` ahead of or behind the player by shifting its clock. If the player isn't on an orbit it can follow, it falls back to a circular orbit and says so.
- **Station positions:** a station is a body orbit (`Ship.IsStation` needs `objSS.bIsBO` and docking ports). Its position is the orbit's: with no eccentricity it starts at `fDegreesCW` when `(fEpoch + fPeriodShift) % fPeriod` is 0.

## Game hooks for temporary stations (researched 2026-11-06, not yet used)

For the planned corporate site missions. From the decompiled game, untested:

- **Spawning:** build a `JsonSpawnStation` and call `CrewSim.system.SpawnStationFromJSON`, as `Endgame` does for its munitions depot. `strOrbitType` ORBIT, GEO, GROUND (on a body's surface, period from its rotation) or EX. It adds a `BodyOrbit` and the station ship, and applies `aFactions`, `strLaw` and `aStartingConds`.
- **Saving:** the system saves every `BodyOrbit` (`aBOs`) and ship, and a shallow ship saves a copy of its whole `JsonShip`, items included, so a runtime station and a generated layout persist with no extra work.
- **Removing:** `Ship.Destroy(isDespawning: false)` takes a ship out of the system, AI and loaded lists. Its orbit needs `StarSystem.RemoveBO` too.
- **Self-destruct:** `ship.WeaponsSystem.Scuttle(min, max)` undocks every docked ship, throws shrapnel and marks the ship for removal. What happens to anyone still aboard isn't known.
- **Generating layouts:** the game's `LayoutGenerator.GenerateLayout` builds asteroid `JsonShip`s from blueprints and merges a wreck template in with `GridUtils.CreateOffset` and `MergeShipOntoAsteroid`. A station could be assembled the same way from room templates. `SpawnShip` looks layouts up in `DataHandler.dictShips`, so a generated one has to be put there or spawned through the private `_SpawnShip`. A generated layout only needs `aItems`, `nCols`, `nRows` and `vShipPos`: rooms are rebuilt from walls and floors on load (`Ship.CreateRooms`), and a ship spawned from a template gets `bPrefill`, which fills its sealed rooms with air on its first full load. `MergeShipOntoAsteroid` keeps item IDs and compares every item with every other, so use our own merge. The base game ships unused round layouts (`LA_PORT_1`, `LA_MAINTENANCE_1`, hidden by the big ship setting) built from stepped 1x1 tile circles. The station generation design is in `Design/Missions/StationGeneration.md`.
- **Defenders:** `AIShip.SpawnNewShip(faction, shipLoot, damage)`, then `SetFactions`, crew from `AIShip.AddCrew` and `AIShipManager.AddAIToShip(ship, AIType)` (see `ArenaModePilot.CreateAIShip` and `AIShipManager.SpawnNewBeaconResponse`). Stations themselves get a `StationPilot`, which never fights.
- **Armed stations:** nothing stops a station shooting except its pilot. AI fire is the `Shoot` command: it takes the ship's installed undamaged weapons (`WeaponsSystem.AllWeapons`, read from the json items when the ship is shallow) and fires each one in arc and reloaded through `WeaponsSystem.ShootShallow`, so it needs no movement. An `IsShipWeaponArcAngle` of 360 fires all round. Target finding is `AreThreatsNearbyBlockade` and `InitiateAgainstBiggestThreat`, which set `shipCombatTarget`. A pilot is a `BaseCharacter` subclass with a behaviour tree of `ICommand`s, chosen by the private `AIShip.SetupAI` from the saved `AIType` enum, so a mod pilot needs a `SetupAI` patch. The run queue picks ships by `AIType` masks, so a new enum value may be skipped; keeping `AIType.Station` and swapping the pilot avoids that.
- **How game AI sees other ships:** by ship, not by pilot. `ElectronicSystems.GetVisibleShipSignatures` lists loaded ships in sensor range but skips stations, derelicts and ships docked at stations, so game AI never targets or fights back at a station. The threat checks (`AreThreatsNearby`, `AreThreatsNearbyBlockade`) skip projectiles and, in `AreThreatsNearby`, unarmed ships, and they skip a ship with no captain whose `Classification` is `Default`, so a crewless armed drone is ignored even while it shoots. `Comms.GetCaptain` needs someone aboard. Firing through `ShootShallow` calls `AIShipManager.CheckShootingWitnesses`, which makes police and the target's faction treat the shooter as a combatant. Help calls (`AlertNearbyShips` sets `EmergencySignal`), dodging and comms reach only ships registered with `AIShipManager`. `WeaponsSystem.ShipFired` is a static event raised on every shot.
- **Ship ammo:** rounds are items (`IsAmmoShip`, condtrig `TIsShipAmmo`) inside each weapon, and a weapon fires only what's loaded in it. A shallow ship's rounds are its json items under the weapon (`strParentID`, gathered once into `WeaponsSystem._dictAmmo`); `ShootShallow` marks the rounds it fires spent by setting their `StatDamage` to full health on the json item, so it's saved. A loaded ship fires through `ShootAuto`, which pops and destroys the round COs. The game's `Shoot` command only calls `ShootShallow`, and `_dictAmmo` is only filled while shallow, so a loaded ship (a station you're docked to) never fires through it; a mod pilot has to pick `ShootShallow` or `ShootAuto` by `LoadState`. `TIsShipAmmo` doesn't forbid damage, so whether rounds spent while shallow come back as live ammo when the ship loads is untested. Point defence (`WeaponsSystem.ActivateDefenseSystems`, every frame) fires every ship's defensive weapons at missiles, for any ship with weapons and an active sensor, AI or not, and uses ammo the same way.
- **Corporations** are factions in `star_system.json`, several with their own security faction (Prokofiev and Prokofiev SecForce, Ayotimiwa and AyoSec, Cloudbreak and Cloudbreak Corporate Security). Reputation changes go through `JsonFaction.ApplyFactionRep`.
- **Hacking:** the base game has `SkillHacking`, cracking software run from an unlocked PDA, and timed hack interactions (`interactions_hacking.json`, `HackRobot*`, `ENCHackPDA*`) to model a data vault on.

## Checking a game update

When the game updates, compiling only proves public signatures still match. Also:

- Decompile the new `Assembly-CSharp.dll` with `ilspycmd -p -o <folder>`, and diff it against the last decompile to find changed files.
- Check every name-based target still exists with the same overloads: Harmony patches given a string method name, the profiler's `ProfileTargets`, and `AccessTools` fields and methods.
- Look for game data files the update changed (by date under `StreamingAssets/data`), and check the base names the mod uses from them.

The 2026-11-06 update reworked the condition owner scheduler: `CatchUp` lost its parameter, `CrewSim.ProcessDeferredEndTurns` went, `GUIFFWD` completes pending end turns through `Scheduler`, and AI job claims moved to `WorkManager` claim jobs. It also changed `interactions.json`. The mod needed no changes for it.

## Ostranauts data modding wiki

https://ostranauts.wiki.gg/wiki/Modding/Data_Modding is a fan-maintained guide to Ostranauts JSON mods, last edited 2025-11-12 with no target game version stated. It's useful for concepts and sub-page links, such as Modding/CondOwners, Modding/Interactions and Modding/Loot.

Treat the base game JSON and the decompiled `DataHandler` as authoritative. As of 2026-11-01, these wiki claims don't hold for this install:

- `loading_order.json` is in `Ostranauts_Data/Mods/`, not `Ostranauts_Data/`.
- The log is `Player.log`, not `output_log.txt`.
- Not every item needs a COOverlay of the same name. Most base items are tied to a condowner through `strItemDef`. Per the user, COOverlays avoid redefining similar objects and save memory in save files, and aren't required for every condowner.
- Socket arrays aren't always 9 elements.
- Its sample condtrig fields (`aCondsToTest`, `aCondsToAdd`) aren't used in base condtrigs. Use `aReqs`, `aForbids` and `aTriggers`.
