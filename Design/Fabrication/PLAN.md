# Fabrication, recycling and blueprints: design plan

A proposal to review before anything is built. It builds on the cost list in `base_item_costs.csv` (see `README.md`).

## Goals

- Turn salvage into something useful besides cash: break items down into materials, and build items from materials.
- Make finding things matter: you can only build what you know the blueprint for.
- Give the mod's own tech, starting with the jump drive, a gate through Exotics.
- Keep the game's feel. Everything is a physical item or machine on the ship, takes power and time, and survives a save and load.

## The loop

1. Salvage or buy items.
2. Feed them to the **Recycler**, which turns them into materials.
3. The materials go into the ship's installed **silos**.
4. Pick a known **blueprint** at the **Fabricator**, which draws the materials from the silos and builds the item.

Blueprints are learned from blueprint items, from recycling, and from quest events.

## Materials

Eleven materials: Base Metals, Metal Alloys, Precious Metals, Rare Earths, Polymers, Ceramics, Organics, Silicates, Volatiles, Helium Fines, Exotics.

- **Volatiles:** water, methane and other ices. They're used for munitions, and later refined into fuel.
- **Helium Fines:** the fine regolith dust that solar wind implants helium-3 in. They come only from regolith, and are later refined into He-3.

**Units are abstract,** as in the cost list: roughly one piece of scrap each, and one share of a silo's capacity. Each unit weighs 0.01 kg, so stored material weighs on the ship. That may be raised later.

**Where materials come from:**

| Material | Main sources |
|---|---|
| Base Metals, Polymers, Organics | Recycling almost anything. Base Metals also from meteoric iron |
| Metal Alloys, Ceramics | Recycling hull, weapons, fusion and industrial parts. Alloys also from cobalt and wolfram ore, ceramics from carbon/carbide ore |
| Silicates, Precious Metals, Rare Earths | Recycling electronics, sensors and control systems. Also from ore: silicates from silicate ore, olivine and regolith, precious metals from platinum, iridium, palladium and gold ore, rare earths from olivine and a little from regolith |
| Volatiles | Water ice, methane ice, hydrates and volatiles canisters |
| Helium Fines | Regolith only |
| Exotics | Recycling the mod's own specialised tech, and void opal. Other sources to be decided |

Organics stay as they are, including trash and carbon fibre scrap, so they're a common material found in most items.

**Mined ore and ice recycle like any item,** at the recycler's 50%. Their makeup is set by hand in CostModel, and each gives about 6 units per 10 kg on the usual mass curve. The material, not the amount, carries the value: platinum ore and olivine give the same number of units.

## Storage silos

Materials live in silos, not as loose items. One silo CO holds any mix of materials as conditions, up to a fixed overall capacity.

**This follows the reactor.** Its fuel canisters hold reactant as conditions, and the reactor draws from every installed canister on the same ship (`FusionIC.GetReactants`). Silos work the same way for the recycler and fabricator.

**Which materials a silo holds is set by conditions.** Every material has its own silo condition, `IsFzSilo<material>`, for example `IsFzSiloBaseMetals` or `IsFzSiloExotics`, all defined in `fz_conditions_simple_fabrication.json`. A silo holds every material whose condition it carries. So a specialised silo, for example a cryogenic one that only takes Volatiles, is just a silo CO with that one condition, all in data. A material from another mod gets `IsFzSilo<its ID>` unless it names another condition in `strSiloCond`, and the mod defines that condition and adds it to whichever silos should take the material.

**Three silos to start:**

| Silo | Size | Capacity | Conditions | Holds |
|---|---|---|---|---|
| Small silo | 1 tile, like the O2/N2 gas canister (`ItmCanister01`) | 5,000 units | The ten everyday ones, `IsFzSiloBaseMetals` to `IsFzSiloHeliumFines` | Any material except Exotics |
| Large silo | 7×7 tiles, like the reactor's D2O tank (`ItmCanisterLH02`) | 100,000 units | The same ten | Any material except Exotics |
| Exotics silo | 1 tile, like the small silo | 5,000 units | `IsFzSiloExotics` | Exotics only |

At 0.01 kg a unit, a full small silo carries 50 kg of material and a full large one 1,000 kg.

**Every silo:**

- **Installable part** with Loose, Installed and Damaged forms, like the jump cell.
- **Contents:** one persisted condition per material, for example `StatFzMatBaseMetals`. The total can't go past the capacity. Amounts are fractional internally and shown as whole units.
- **Mass:** the silo's `StatMass` is its empty mass plus its contents, kept up to date as it fills. The ship's mass, and so its thrust and the jump field's energy, see the load.
- **Carrying contents:** persisted conditions carry over when an item changes state (`CondOwner.ModeSwitchInternal`). So uninstalling a silo keeps what's in it, and a loose silo can be hauled with its load.
- **Price:** a loose silo's `StatBasePrice` is its own price plus the value of its contents.
- **Damage:** a silo keeps its full capacity when damaged, but loses all of its contents at once, with nothing spilled or recovered. That's the penalty for not protecting it. Because persisted conditions would otherwise carry over into the damaged form, the contents have to be cleared as it switches. Once repaired, it works again, empty.
- **Panel:** a bar for each material against the capacity, drawn in the material's colour (`strColor`), the same colour as its compacted block, a button to compact a material (see below), and one to purge it, discarding it to free space. Purging asks for confirmation.

**Which silos a machine uses:** every installed, working silo on the same ship, as with the reactor's canisters. Docked ships don't share. Material moves between ships as compacted blocks.

**Filling and drawing:**

- **Filling:** the recycler puts each material into the silo that already holds the most of it and still has space, then any other silo with space. Only silos with the material's silo condition count. That keeps materials grouped, so uninstalling one silo takes a sensible load with it.
- **Drawing:** the fabricator takes each material from the silo holding the least of it first, emptying stragglers.
- **Full silos:** when there's no space for a material, the recycler pauses until there is.

## Compacted material

Material the player doesn't want can be compacted out of a silo into blocks to sell or move.

- **One item per material,** for example "Compacted Base Metals". Exotics are compacted into an **Exotic Canister** instead of a block, which follows the same rules. Every block or canister holds exactly 100 units, so compacting needs at least 100 of that material in the silo.
- **Stacking:** blocks stack to 5, set with the item's `nStackLimit` as the game does for its own stackable items. They can only stack because every block holds the same amount.
- **Where it's made:** the silo panel's compact button, so no extra machine. The block appears beside the silo.
- **Look:** one block sprite tinted with the material's colour, so a block and its silo bar match. The Exotic Canister has its own art in the Exotics colour.
- **Mass and price:** 100 units' mass each, 1 kg at 0.01 kg a unit. The price is 100 times the material's unit price.
- **Selling:** blocks carry `IsScrap`, so the game's scrap kiosks buy them as they are. Their buy trigger, `TIsBarterGenericScrapKiosk`, already accepts any item marked `IsScrap`.
- **Not for sale:** kiosks buy blocks but never sell them. Materials come from recycling.
- **Getting it back:** feeding a block to the recycler returns all 100 units, quickly. So blocks are also how materials move between ships. An Exotic Canister goes back into a silo with `IsFzSiloExotics`.
- **Exotic Canisters may get a use of their own later,** for example as a part some exotic items need whole.

## Machines

Both are installable ship parts with power draw, a GUI panel and a Loose, Installed and Damaged family, built the same way as the jump cells.

### Working area

Both machines work on the floor in front of them, not through a container. Items to recycle are set down in the recycler's area, and fabricated items appear in the fabricator's.

- **Configured in the panel:** a rectangle of tiles in front of the machine, set by width, depth and how far out it starts, and turning with the machine. It's saved in the machine's prop map.
  - **Limits:** each machine has a largest area, for example 3×3 tiles for the recycler and 7×7 for the fabricator.
  - **What it means for the fabricator:** its area is also the largest item it can make, so a reactor-sized part needs the full area clear.
- **What counts as in the area:** loose items whose position falls on one of its tiles.
- **Safety:**
  - **Never touched:** crew, robots and animals, installed parts, walls, floors and anything else built into the ship.
  - **Someone standing in the area:** the machine pauses until they leave.
- **Containers:** a container in the recycler's area is recycled only once it's empty, so nothing inside is lost by accident.

### Visuals

- **The working area,** projected on the floor as a border only, with the inside left clear. The border is a band of slanted stripes, alternating colour and transparency like hazard tape, running around the area's edge. Its colour shows the machine's state:
  - **Blue:** idle.
  - **Amber:** working.
  - **Red:** blocked, for example by a full silo, a person in the area, or no room for an item.
- **When it shows:** while the panel is open, while the machine is working, and when it's blocked. A panel switch can keep it on.
- **Under loose items, over the floor:** items are drawn in depth order set by their `fZScale`. Installed floors use 0.01 and loose floor tiles 0.02, and ordinary items 0.2 or more, for example a welder 0.2, a crate 0.25 and a heater 0.5. The border is a mesh in the ship's own world, at a depth equal to about 0.025. That puts it just above every floor and under the items standing on it, so it reads as projected onto the floor.
  - **Not the overlay camera:** the jump field contour uses the mod's overlay camera, but that draws after everything, on top of items, so it doesn't suit this.
  - **Bright in the dark:** the border would use the same material as item light sprites, which the game draws at full brightness in a dark room, the way the jump cell's glow does.
  - **The stripes:** a small repeating texture laid along the band, so they keep an even width and angle at any area size, and can scroll slowly while the machine works.
  - **To check while building:** that a world mesh at that depth sorts between floors and items as expected, and that the light sprite material renders on it. If the light sprite material doesn't work, a plain unlit transparent material is the fallback, though it would dim with the room's lighting.
- **Recycling:** two laser beams from the recycler's back corners sweep back and forth across the item, crossing, while the line they hit scans down it, and the item wears away under them as the run goes.
- **Fabricating:**
  - **Before a run:** a ghost of the item shows where it will appear.
  - **During the run:** the item builds up from the floor along a scan line, solid below and ghost above, so its progress is visible.
- **Sound,** later: a hum and scan for each, made with the jump sequence's synthesiser (`Common/Util/AudioDsp.cs`).

To check while building: whether the game's item shader can fade an item and cut it off at a height. If not, the scan line and ghost are drawn as overlay sprites over the item, and the item is shown or hidden when the run ends.

### Recycler

- **Input:** loose items in its working area. Start a run from the panel, or set it to take items automatically as they're put down. It works through them one at a time.
- **Run time:** each unit of the item's full material cost at its material's `fRecycleTime`, scaled by the recipe's `fTimeScale`.
- **Power:** a small idle draw, plus the item's energy (each unit at its material's `fRecycleEnergy`) spread over its run time, so complex items draw more.
- **Containers:** a container is recycled all at once with everything in it that can be recycled, at every depth. Its run time, energy, yield and reserves are its own plus those of its recyclable contents. Contents that can't be recycled are left on the floor where the container was, with anything inside them. The WORKING AREA list shows contents nested under their container.
- **Room:** each yield is put into the ship's silos as `Silo.Fill` would, and reserves into their canisters. With DUMP EXCESS off, a yield that won't fit stops the recycler, and shows red on WORKING AREA. With it on, what doesn't fit is destroyed, and shows yellow.
- **AUTO:** recycles whatever turns up in the area. While it's on, RECYCLE shows lit and can't be switched off; its own setting returns when AUTO is off.
- **Whole items only:** an item counts only when every tile it covers is an open tile of the area, so one hanging over the edge, or over a blocked tile, is left alone.
- **Walls:** the area stops at walls. A wall tile, a tile that isn't part of the ship, and any tile that can only be reached from the recycler through a wall are blocked, and the recycler ignores what's on them. SETUP's map shows them red.
- **Yield:** 50% of the item's cost, by material, rounded down per material with the remainders carried to the next item. **Not** the game's debris tables, which make every floor yield the same and would let the recycler make materials from nothing.
  - **Undamaged item:** 50% of its cost.
  - **Damaged item:** 50% of its damaged cost, which is lower again.
  - **The game's scrap** (scrap steel and the like) and **compacted blocks:** 100%, so nothing's wasted.
- **Output:** into the ship's silos, pausing when they're full.
- **Learning blueprints:** recycling an ordinary item whose blueprint you don't know has a chance, for example 10%, to teach it outright. Specialised items can't be learned this way (see Blueprints).

### Fabricator

There's one fabricator.

- **Input:** a known blueprint, plus materials from the ship's silos. A run starts only when the silos hold a whole item's materials, and then draws them as it goes, so two fabricators can share stock and one waits when it runs short.
- **Run time and power:** each material's `fFabricateTime` and `fFabricateEnergy` per unit, as the recycler's.
- **Cost:** the undamaged cost from the list. You can't fabricate damaged items.
- **Output:** the item's spawn CO, its loose form, in the working area. Where it goes is chosen before the run starts, by its own fit test on the area's open tiles (`Item.CheckFit` leans on the drag cursor, so it can't be used from a machine). A stackable item is built one at a time onto a stack in the area with room, when there is one.
- **Controls:** FABRICATE builds COUNT items, counting down. AUTO fills the area, looking again every few seconds once it's full. FILL fills the canisters it makes from the ship's reserves; otherwise they come out empty.

### Optional later: Refinery

Turns materials into other materials or fuel, at a loss:

- **Upgrading:** common materials into rarer ones, for example 4 Base Metals plus 1 Rare Earths into 2 Metal Alloys. This gives Base Metals a long-term use, and a way to get alloys when salvage runs short.
- **Fuel:** Volatiles into fuel, and Helium Fines into He-3 for the reactor.

### Optional later: Salvaging laser

Exotic technology: its blueprint is rare, and building it takes Exotics.

- **What it does:** a ship-mounted laser that breaks down parts of a nearby wreck, or an asteroid's deposits and regolith, and sends the material straight into the ship's silos, with no hauling and no recycler. The material comes from the costs in the list, at a lower yield than the recycler, for example 40%. Deposits and placed regolith are in the list for this, costed from the ore they're mined for.
- **Mounting:** an external part, installed like the ship weapons (PDC, coilgun) and aimed at a target picked on the nav console, as the NavCom does.
- **Limits:**
  - **Range:** the target must be close, within docking or towing distance.
  - **Power:** it draws a lot while cutting.
  - **Speed:** it works through a part at a time, slowest on armour and fusion parts.
  - **What it can cut:** never a ship with crew aboard, and never a station.
- **What's lost:** anything the laser cuts is gone. Parts worth keeping whole still have to be removed by hand first, which leaves a choice: strip a wreck carefully, or cut it up quickly.
- **Optional extra:** a small chance of teaching an ordinary blueprint from what it cuts, the same as the recycler.

## Blueprints

**Blueprints are knowledge, not something the fabricator holds.** Once a blueprint is learned, the fabricator can make that item.

**How they're learned:**

- **Blueprint items:** loose items, one per blueprint, that are used up to learn it. They're found in loot or given by quests.
- **Recycling:** the recycler can teach an ordinary item's blueprint outright (see Recycler).
- **Quest events:** a quest beat can grant a blueprint directly, in data alone (see below).

**Learned blueprints are global game state.** It's a single-player game, so they belong to the playthrough, not to a character or ship. Any fabricator offers every blueprint learned, whoever is using it, and taking over a different character keeps them. Only the player can learn them: using a blueprint item while controlling a crew member does nothing.

**Saved in the game save's custom infos.** The save file (`JsonGameSave`) has an `aCustomInfos` list of `Key=value` strings for global state that isn't part of any ship, such as PDA notes, overlays and the quick bar:

- **Writing:** the game builds the list in `CrewSim.CustomInfosString` when it saves (`LoadManager.SaveGameData`). A postfix adds one entry, `FzBlueprints=ItmHeater01;ItmToolWelder01;...`, listing every learned blueprint by its recipe key.
- **Reading:** the game reads the list back in `CrewSim.SetCustomInfos` near the end of loading a save. A postfix picks out `FzBlueprints` and sets the learned list from it.
- **Unknown keys are ignored:** `SetCustomInfos` switches on the key, and only acts on the ones it knows. A save made with the mod still loads without it, though saving again then drops the blueprints.
- **One limit:** `SetCustomInfos` splits each entry on `=`, so the value can't contain one. Recipe keys never do.
- **In memory:** a static set of learned recipe keys, which is the live state between saves. It's cleared when `CrewSim.EmptyScene` runs, so a new game starts with none and a load starts from the save's list. A new game never calls `SetCustomInfos`.

**Quest grants in data alone.** Interactions can carry their own `aCustomInfos`, which the game passes to the same `SetCustomInfos` when the interaction runs. That's how the base game's plot triggers its endings (`Ceres=EndP4Xin`). So the postfix also handles `FzBlueprintAdd=ItmHeater01`: any quest beat can grant a blueprint with one line of JSON, and no code per quest.

**Alternatives that don't work as well:**
- **Conditions on the player's character:** tied to that character, and needing a condition definition for every blueprint.
- **A separate file in the save folder:** saves are compressed and synced to the Steam cloud as a set of known files, so an extra file needs patching in several places.
- **A hidden CO that isn't on any ship:** the game drops those from the save (`SaveGameData` removes saved COs whose last ship no longer exists).

**How each item is learned.** Every recipe says how its blueprint can be had, in one of three tiers:

| Tier | Items | Learned by |
|---|---|---|
| Ordinary | Most items: furniture, clothing, tools, walls and floors, HVAC, everyday electronics and parts | Recycling, blueprint items from loot, quests |
| Specialised | Fusion reactors and their parts, ship weapons, nav consoles and nav modules, sensors, docking systems, and high-end branded tech | Corporate sites only (see below), never recycling or trade |
| Exotic | The mod's own tech | Set sources only (see Exotics) |

**Specialised items** are what the corporations that survived the collapse guard most closely, and what would short-cut progress if one salvaged reactor taught you to build reactors. No corporation sells them. They're taken from the corporations themselves.

**Which tier an item is in** is set in the recipe data by CostModel (see How CostModel fills it). Nearly every item in the game carries a maker's name, so "high-end branded tech" can't mean anything with a brand, and judging it by price caught the wrong things. It's left to an override list once decided.

**Which items get blueprints.** Not every row in the list should. Leave out building blocks (scrap and small parts), clothing variants that only differ in colour, and wall and floor colour variants. Give one blueprint per model and let the player pick its colour when fabricating.

## Corporate sites

Each major manufacturer keeps its designs at hidden stations around the system. Finding and raiding them is an end-game activity, and the only way to learn specialised blueprints.

**Two kinds of site:**

| | Relay station | Black site |
|---|---|---|
| What it is | A small, mostly automated data relay | A secret research or production station |
| Defences | Light: locked doors, a few security drones | Heavy: guards, drones, sealed vaults |
| How it's taken | Can be hacked quietly, or raided | Raided |
| What it holds | One or two of the corporation's blueprint items, and intel pointing to its black site | Most of the corporation's specialised blueprint items, plus rare parts |

**Finding them:**

- **Hidden:** sites aren't on the nav map until they're located.
- **Intel:** coordinates come from quest chains, intel data files looted from corporate ships and derelicts, and relay stations, which point to their corporation's black site.
- **Optional search mechanic:** a faint signal that sensors can track down once you have a rough location.

**Raiding them:**

- **One-time loot:** each site's blueprints can be taken once. The corporation may then reinforce its other sites.
- **Consequences:** standing with that corporation's faction drops, and the corporation may send ships after you or put a bounty on you. Hacking a relay without being caught avoids some of this.
- **Spread across corporations:** each corporation's sites hold only its own lines, for example Sulaiman for reactors and Polaris for nav consoles. Building a full specialised library means taking on several of them.

**Fitting it into the game:**

- **Quests:** the plot system (`data/plots` and `plot_beats`) for the quest chains. Their interactions grant blueprints with `FzBlueprintAdd` (see Blueprints).
- **Stations and ships:** ship templates (`data/ships`) for the site stations and their defenders.
- **Before designing this in detail:** check whether a mod can add new stations to the star system and keep them off the map until they're found.

## Exotics and the mod's tech

- **Sources:** recycling the mod's own specialised tech, such as jump field emitters, jump cells, the reactor tap and the NavCom module, gives back their Exotics at the recycler's 50%. Other sources are to be decided.
- **What they gate:** the mod's own items, and later the salvaging laser. Each needs some Exotics plus ordinary materials.
- **Storage:** only in an Exotics silo.

## Data and code

**Recipes are generated.** CostModel already makes the cost list. Extend it to write the recipes as mod data, with hand-written recipes for the mod's own items alongside. A recipe with the same `strName` overrides a generated one.

### Recipes from other mods

Any mod can add recipes and materials without depending on this one.

**The folder:** every mod puts its files in its own `data/fz/fabrication/`:

```
<mod>/data/fz/fabrication/recipes/*.json     recipes
<mod>/data/fz/fabrication/materials/*.json   materials
```

The two kinds sit in sibling folders because the game's loader searches a folder and everything under it.

**This mod's recipes are split by category,** one file per top-level `strCategory` group, for example `fz_recipes_walls.json` and `fz_recipes_fusionparts.json`, 27 files in all. Every file in the folder is read, so the split only makes the data easier to work with. Another mod can drop a whole category with an ignore pattern naming its file.

**Hand-written recipes** sit beside them in files named `fz_manual_*.json`, which CostModel leaves alone (it only replaces `*recipes_*.json`). `fz_manual_liquids.json` gives water (`LiquidWater`, a 0.25 kg ration, the water in sinks and flasks) a recycling-only recipe of 25 Volatiles at full yield, by mass at 0.01 kg a unit. It isn't fabricable. Water Ice is costed the same way, by mass (24.7 kg is 2,470 Volatiles), through CostModel's `oresByMass`, rather than on the usual mass curve; deposits still weigh it on the curve, so ice in a deposit's mix doesn't swamp it. Ice Walls rose to match, as they break into Water Ice. **Balance point:** Water Ice is priced at 1,200, but its Volatiles are worth about 4,500, so buying ice to recycle and sell would pay if ice can be bought.

**No dependency:**
- **Without this mod:** the game never reads `fz/fabrication`, so a mod shipping recipes loads and runs exactly as it would without them, with nothing logged.
- **With it:** its recipes take effect.
- **References are plain strings:** a recipe pack refers to its own COs and to this mod's material IDs by name only, so it needs no code and no reference to this mod.

**Loading in the game's own order** (`Fabrication/Util/FabData.cs`):
- **The order:** each mod the game loads is put at the front of `DataHandler.aModPaths`, so reading that list in reverse gives the load order, core first. That holds however late this mod's patches go in, so mods that loaded before them are still read.
- **When:** once the game's data has finished loading (a postfix on `DataHandler.AllPostLoadAsync`), or on first use if that has already passed. `fz fabreload` reads everything again.
- **Reading:** each file goes through the game's own `DataHandler.JsonToData`, which keys entries by `strName` and logs a file it can't read. The game's ignore patterns from `loading_order.json` apply.
- **So recipes behave like any game data:** a later mod's entry with the same `strName` replaces an earlier one, and a recipe pack can override this mod's recipes and the generated base ones. When two recipes claim the same CO, the later one in load order keeps it, and the log says so.
- **Checked:** the game's JSON library reads the recipe and material types, including the material table and whole numbers where decimals are expected. Fields a mod leaves out take their defaults.

**Rules for other mods' data:**
- **Missing COs:** a recipe whose COs don't exist is skipped, with one line in the log. So one recipe pack can cover mods that may not be installed.
- **Removing a recipe:** override it with `bFabricate` and `bRecycle` both false.
- **New materials:** a materials entry is enough. The plugin creates each material's silo condition when the data loads, so a mod can add a material without defining a condition for it. A new material still needs its own block CO if it's to be compacted.

### Recipe format

Each file is an array of recipes, in the game's own JSON style: field names prefixed by type (`str`, `b`, `f`, `a`, `map`) and one `strName` key per entry.

**One recipe is one cost.** A recipe covers the COs that share a cost, so an item's undamaged and damaged forms are separate recipes. The damaged one can be recycled but never fabricated.

| Field | Type | Meaning |
|---|---|---|
| `strName` | string | Unique key. The item family's base CO for its undamaged recipe, with `Dmg` added for its damaged one. |
| `strNameFriendly` | string | Name shown on the fabricator and recycler. |
| `strCategory` | string | Dotted path for grouping in the fabricator's list, for example `Walls.Testudo`, `FusionParts.Sulaiman`, `Ore.Precious` or `Deposits.Stony`. |
| `aCOs` | string array | Every CO this recipe applies to. The recycler finds an item's recipe from its CO. |
| `strCOFabricate` | string | The CO the fabricator spawns, usually the loose form. Needed when `bFabricate` is true. |
| `mapMaterials` | object | Material ID to units, for example `{ "BaseMetals": 8, "MetalAlloys": 1 }`. Materials not listed are 0. |
| `bFabricate` | bool | Whether the fabricator can make it. |
| `bRecycle` | bool | Whether the recycler, or later the salvaging laser, accepts it. |
| `fRecycleYield` | number | Share of `mapMaterials` returned when recycled. 0.5 for most items, 1.0 for things whose material isn't lost, such as ore and compacted blocks. |
| `strBlueprint` | string | The blueprint that unlocks fabricating it. Colour variants share one, and the fabricator offers them as choices. Needed when `bFabricate` is true. |
| `strTier` | string | `Ordinary`, `Specialised` or `Exotic`, which sets how its blueprint can be learned. |
| `bStartKnown` | bool, optional | Its blueprint is known from the start of every game without being learned, for basic parts. Default false. |
| `fTimeScale` | number, optional | Multiplies the run time worked out from the material total, for items that are unusually quick or slow to make. Default 1.0. |

**How the flags cover each kind of item:**

| Kind | `bFabricate` | `bRecycle` | `fRecycleYield` |
|---|---|---|---|
| Ordinary item, undamaged | true | true | 0.5 |
| Damaged item | false | true | 0.5, of its damaged cost |
| Game scrap and small parts | true | true | 1.0 |
| Ore, ice, deposits and regolith | false | true | 1.0 |
| Compacted blocks and Exotic Canisters | false | true | 1.0 |
| Mod tech gated by Exotics | true | true | 0.5 |
| Something that can't be broken down, for example a quest item | false | false | none |

The yield is the recipe's own: ore loses nothing because it's raw material, and a manufactured item loses half because recycling it is wasteful. A machine can scale the yield further, for example the salvaging laser at 0.8 of it, so the two stay separate.

**Examples:**

```json
[
  {
    "strName": "ItmHeater01",
    "strNameFriendly": "Heater",
    "strCategory": "HVAC.Sokol",
    "aCOs": [ "ItmHeater01", "ItmHeater01Off", "ItmHeater01OffLoose" ],
    "strCOFabricate": "ItmHeater01OffLoose",
    "mapMaterials": { "BaseMetals": 8, "MetalAlloys": 1, "RareEarths": 1, "Polymers": 3, "Ceramics": 1, "Organics": 2 },
    "bFabricate": true,
    "bRecycle": true,
    "fRecycleYield": 0.5,
    "strBlueprint": "Heater01",
    "strTier": "Ordinary"
  },
  {
    "strName": "ItmHeater01Dmg",
    "strNameFriendly": "Heater (Damaged)",
    "strCategory": "HVAC.Sokol",
    "aCOs": [ "ItmHeater01Dmg", "ItmHeater01DmgLoose" ],
    "mapMaterials": { "BaseMetals": 4, "Polymers": 1, "Organics": 2 },
    "bFabricate": false,
    "bRecycle": true,
    "fRecycleYield": 0.5,
    "strTier": "Ordinary"
  },
  {
    "strName": "ItmMineral05",
    "strNameFriendly": "Ore: Platinum",
    "strCategory": "Ore.Precious",
    "aCOs": [ "ItmMineral05" ],
    "mapMaterials": { "PreciousMetals": 6 },
    "bFabricate": false,
    "bRecycle": true,
    "fRecycleYield": 1.0,
    "strTier": "Ordinary"
  }
]
```

**Materials have their own files,** in `data/fz/fabrication/materials/`, so recipes refer to materials by ID and everything else about a material lives in one place. This mod defines the eleven materials, and other mods can add more:

| Field | Meaning |
|---|---|
| `strName` | Material ID, for example `BaseMetals` or `HeliumFines`. |
| `strNameFriendly` | Name shown in panels. |
| `strColor` | Hex colour, for example `#9AA0A6`, for its bars on the silo panel and the tint of its compacted block. An invalid one is logged and white is used. |
| `fUnitPrice` | Price of one unit, for blocks and loose silos. CostModel sets it low, at the 10th percentile of the price per unit of items mostly made of that material, which lines up with the game's own scrap prices. Higher would make buying cheap items to recycle and sell pay. Even so, the cheapest 71 of 742 priced base items are still worth more recycled and sold than bought, a balance point to revisit. |
| `fRecycleTime` | Game seconds to recycle one unit of it. Common, simple stock is quickest: Organics 0.8, Base Metals and Volatiles 1, Silicates 1.2, Polymers and Ceramics 1.5, Metal Alloys 2, Precious Metals 4, Rare Earths 4.5, Helium Fines 6, Exotics 30. |
| `fFabricateTime` | Game seconds to fabricate with one unit of it, half again longer or more: 1.2, 1.5, 1.8, 2, 2.5, 3, 6, 7, 9 and Exotics 60, in the same order. |
| `fRecycleEnergy` | kWh to recycle one unit of it. CostModel sets it from a power level for each material times its time, then 25 times that (the plain figure was too slight to matter), so harder materials draw more: Organics 50 kW, Base Metals, Polymers and Volatiles 75, Silicates 100, Metal Alloys 125, Ceramics 150, Precious Metals 200, Rare Earths 250, Helium Fines 300, Exotics 1,000. A recycler draws its idle power plus the current item's energy over its run time. |
| `fFabricateEnergy` | kWh to fabricate with one unit of it, at half again the recycling power. |
| `strBlockCO` | The compacted block it's compacted into, for example `ItmFzCompactedBaseMetals`, or the Exotic Canister. |
| `strSiloCond` | The condition a silo needs to hold it, `IsFzSilo<strName>` when left out. A condition the game doesn't define is logged, since no silo could hold the material. |

**Material colours** set by this mod:

| Material | Colour | Material | Colour |
|---|---|---|---|
| Base Metals | `#9AA0A6` grey | Ceramics | `#E8E2D6` bone |
| Metal Alloys | `#4A6FA5` steel blue | Organics | `#8B5A2B` brown |
| Precious Metals | `#D4AF37` gold | Silicates | `#C2B280` sand |
| Rare Earths | `#6FBF73` green | Volatiles | `#7FD4FF` ice blue |
| Polymers | `#3FB8AF` teal | Helium Fines | `#B39DDB` lavender |
| Exotics | `#FF4FD8` magenta | | |

**Checked when the data loads,** with errors logged rather than thrown:
- **Material IDs:** every ID in `mapMaterials` must be a defined material. A recipe that uses an unknown one is skipped.
- **Fabricated recipes:** a recipe with `bFabricate` needs `strCOFabricate` and `strBlueprint`.
- **COs:** must exist, or the recipe is skipped (see Recipes from other mods).
- **Duplicates:** no CO in two recipes.
- **Yield:** `fRecycleYield` must be between 0 and 1.

**How CostModel fills it:**
- **`strCategory`:** a group from the item's role or game category (Walls, Floors, Doors, HVAC, FusionParts, Ore, Deposits and so on), then its maker. The maker is the name before the quoted model in its friendly name, for example Testudo in `Wall: Testudo "Aero Series"`. Items with no maker use their type instead.
- **`strTier`:** Specialised for fusion parts, ship weapons, sensors, nav consoles and nav modules, and docking systems, 61 recipes in all. Judging high-end branded tech by price per kilogram caught gas alarms, ammunition and medical pens, so that's left to an override list once it's decided.
- **`bStartKnown`:** set for the 13 building blocks, the game's scrap and small parts, so they can always be made.
- **`strBlueprint`:** colour variants share one when they share a model name and cost.

**A new code section, `Fabrication/`,** following the project layout:

- `Util/`: the recipe loader, silos, recycling yields and blueprint knowledge.
- `Patches/`: loading every mod's `fz/fabrication` folders during `DataHandler.LoadMod`, hooks for the machines' updates, clearing a silo's contents when it's damaged, saving and loading learned blueprints through custom infos, and loot additions.
- `UI/`: the silo, recycler and fabricator panels, the projected working areas, and the recycling and fabricating effects.
- `FabricationSection.Init`, which registers the GUIs and console commands. Every command is under the mod's root command `fz`:
  - **Built:** `fz recipe` sums up the loaded data, shows one recipe, or finds recipes by text. `fz bplearn`, `fz bpforget` and `fz bplist` manage learned blueprints. `fz fabreload` reads every mod's data again.
  - **Later, with silos:** `fz fabgive` to give materials.

**Save and load,** following the project's rule:

- Silo contents are persisted conditions on the silos.
- Learned blueprints are an `FzBlueprints` entry in the save's custom infos, and a static set in memory cleared on `EmptyScene`.
- A machine's working area, queue, progress and chosen output spot are prop maps on the machine. An item being recycled keeps its place until the run finishes, so a load mid-run picks up where it was.
- Any static cache, such as the recipe table, is rebuilt from data and cleared on `EmptyScene`.

**Time skips.** A run catches up in one step when it next updates, the same way the jump cells recharge.

## Economy and balance

- **No profit loop:** recycling then refabricating an item loses half its material each time, at the 50% yield.
- **Material prices:** set a base price per unit for each material, from the average price per unit of the items it comes from. Rare materials sell for much more than Base Metals or Organics. Compacted blocks and loose silos sell for their contents at these prices.
- **Recycling versus selling:** with kiosks only buying blocks, recycling is worth it when an item's materials, at half its cost, are worth more than the item sells for, or when you need those materials.
- **Station markets:** the game's stations already run category production chains (`market/Production`), such as textiles plus electronics into space suits. Materials could be added as cargo later.
- **Time and power:** these are the main brakes. A big item should take hours to fabricate and draw real power, so the reactor and the jump drive's reactor taps compete for it.

## Phases

1. **Data:** extend CostModel to write recipe data, including each item's tier. Add the recipe loader and the console commands.
2. **Silos and blocks:** the three silos, their contents, mass and price, their panel, filling and drawing, losing contents on damage, and compacted blocks.
3. **Recycler:** machine, panel, working area and its projection, yields, run time and power, learning blueprints by recycling, and the recycling effect.
4. **Blueprints:** blueprint items, learned knowledge, and loot sources.
5. **Fabricator:** machine, panel, working area and output placement, and the fabricating effect. Built; not yet tested in game. A ghost preview of where the next item goes is still to do.
6. **Exotics:** recipes for the mod's own tech, and Exotics from recycling it.
7. **Balance and economy:** prices, rates and station trade. Optionally the Refinery.
8. **End game:** corporate relay stations and black sites, their quest chains, and specialised blueprints. Until then, specialised items can't be fabricated.
9. **Later:** the salvaging laser.

Phases 1 to 3 already give a useful system: break down salvage into materials you can store, sell and move in silos and blocks.

## Decided

- **Organics:** stay as they are, not remapped.
- **Units:** abstract, at 0.01 kg each for now.
- **Silos:**
  - **Small:** 1 tile, 5,000 units.
  - **Large:** 7×7 tiles, 100,000 units.
  - **Exotics:** a small silo that holds only Exotics.
- **Damaged silos:** keep their capacity and lose all of their contents.
- **Blocks:**
  - **Size:** 100 units each, stacking to 5.
  - **Trade:** kiosks buy them but don't sell them.
  - **Exotics:** compacted into Exotic Canisters, which follow the same rules.
- **Blueprints:** items used up to learn them, or taught directly by recycling or quest events. Only the player learns them, and they're global game state, saved in the save's custom infos.
- **Fabricators:** one.
- **Recycling yield:** 50%.
- **Exotics:** from recycling the mod's specialised tech for now.

## Open questions

- **Specialised tier:** which categories belong in it, and how far above its category's price an item has to be to count as high-end.
- **Corporate sites:** which corporations get sites, how many relays and black sites each, and whether taken sites ever restock.
- **Raid consequences:** how much faction standing drops, and whether corporations hunt the player afterwards.
- **Other Exotics sources:** to be decided later.
