# Base item fabrication costs

`base_item_costs.csv` gives a material cost for every obtainable base game item, as a starting point for the recycling and fabrication system. Each row is one item in one state, undamaged or damaged, with the CO to spawn and the item's other COs.

Materials: Base Metals, Metal Alloys, Precious Metals, Rare Earths, Polymers, Ceramics, Organics, Silicates, Volatiles, Helium Fines, Exotics. Units are abstract, roughly one piece of scrap each. Among base items, only void opal ore gives Exotics, since they're reserved for tech the base game doesn't have.

## What's included

- **Obtainable:** placed on a ship, in any loot table, or given by an interaction, plus every other state of such an item (installed, loose, off, damaged).
- **Mined ore and ice:** the 12 ores, loose regolith, water and methane ice, the volatiles canister and ore cargo. They're listed whether or not a loot table or ship shows them, since they come from mining. Their makeup is set by hand (see Ore and ice).
- **Deposits and placed regolith:** rock and ice cores, rock deposits, regolith walls and ice walls, for the salvaging laser to cut down. They're costed from the ore they're mined for (see Ore and ice).
- **Left out:** items with no mass (data files and other virtual items), ice gangue, food, drink, drugs, meat, water, D2O and He-3 cargo, body parts, transient states like armed explosives, and debug or test items.
- **Spawn CO:** the loose form where an item has one, since that's what a fabricator would hand over. Otherwise the installed form.

## Is mass a good scale?

Mostly, and better than footprint. Against the game's own prices, log mass correlates at r = 0.51 overall. Within physical categories it's much stronger: hull 0.69, industrial products 0.87, weapons 0.89, sensors 0.84. It's weak for electronics (0.14), media and space suits, whose price is in what they hold, not what they weigh.

So mass sets how much material an item takes, and price only shifts the mix:

- **Amount:** units = mass^0.75, at least 1. That keeps a 3,100 kg cargo lift (415 units) from dwarfing everything, while a shoe (2.1 kg) still costs less than a motor, a pump or an EVA suit (50 kg).
- **Mix:** each category has a share of each material, for example electronics lean on silicates, precious metals and rare earths. An item that's pricier per kilogram than its category's median moves up to 20% of its common materials into rarer ones, and a cheaper one does the reverse.

## Scrap

Every item contains at least what it scraps into, by either route the game has:

- **Dismantling:** an installable's `strLootOut`.
- **Destruction:** the components loot table the item turns into once its damage runs out.

An undamaged item also contains what its damaged forms scrap into, since it can be damaged first. Scrap outputs are costed first, down to 13 building blocks set by hand: scrap steel, aluminium, carbon fibre, plastic, cloth and trash, small mechanical and electronic parts, motherboard, motor, heat sink and screen. Scrap cloth weighs 25 g, so it holds a tenth of a unit, though fabricating a piece still takes a whole one.

## Ore and ice

Mined ore and ice take their amount from the usual mass curve, about 6 units per 10 kg, and their makeup from a hand-set table at the top of CostModel:

| Ore or ice | Makeup |
|---|---|
| Meteoric iron | Base Metals, some Metal Alloys |
| Olivine | Silicates, Base Metals and Rare Earths |
| Carbon/carbides | Ceramics and Organics |
| Silicates | Silicates |
| Platinum, iridium, palladium, gold | Precious Metals |
| Cobalt, wolfram | Metal Alloys |
| Hydrates, water ice, methane ice, volatiles canister | Volatiles |
| Void opal | Exotics |
| Regolith | Mostly Helium Fines and Silicates, with a little Base Metals and Rare Earths. The only source of Helium Fines |
| Ore cargo | Mixed: Base Metals, Silicates, Rare Earths and Precious Metals |

**Deposits and placed regolith** hold the ore their mining tables in the game's `loot_mining.json` give. Their makeup is the expected mix of that ore, and their amount is the usual mass curve:

| Deposit | Mined for | Mostly |
|---|---|---|
| Stony rock cores and deposits | The S-class mineral table | Silicates and Base Metals, with some Rare Earths, Precious Metals and Helium Fines |
| Dark rock cores and deposits | The C-class table | Silicates, Volatiles, Ceramics and Organics, with a little Exotics from void opal |
| Shiny rock cores and deposits | The M-class table | Base Metals and Metal Alloys, with Precious Metals |
| Stony regolith walls | Meteoric iron, olivine and regolith | Base Metals, Silicates and Helium Fines |
| Shiny regolith walls | Carbon/carbides, silicates and regolith | Ceramics, Organics, Silicates and Helium Fines |
| Dark regolith walls | Platinum, iridium and regolith | Precious Metals |
| Ice cores and ice walls | Water ice and some methane ice | Volatiles |

The game's tables set some surprises. Dark regolith walls are mostly Precious Metals, and shiny regolith walls also contain base metal, because they're destroyed into a different table from the one they're mined for. The scrap rule covers both.

## Damaged items

A damaged item costs three fifths of its undamaged form in each material, rounded down, but never less than it scraps into. When that scrap is all an undamaged item holds too, as with an ice wall that cracks into the same block of ice, the undamaged one takes a unit more. Every damaged item costs less than its undamaged form.

## Walls and floors

These are judged against an unremarkable one of their kind: the plain wall `ItmWall1x1` (24 kg, durability 15) and the plain steel grate floor `ItmFloorGrate01` (6.5 kg, durability 15). Per tile:

- **Structure:** the material an ordinary panel carries its load in, scaled by its weight against the baseline. It's base metal, polymer for plastic panels, mostly silicate for windows, and mostly organics for wood.
- **Strength beyond its weight:** comes from alloys. A light wall that's as tough as the baseline swaps base metal for alloy, and armour needs more of both.
- **Extras:** walls that hold more pressure than the baseline need more structural material. Aerodynamic panels add polymer and alloy for their lift, and fireproof panels add ceramics.

The `Basis` column says how each row was worked out, including the role a wall or floor was given.

## Caveats

- **Shared debris:** most walls, and every floor, break into the same debris table in the game's data. So the scrap rule sets a minimum of about 9 base metal for every floor, and lightweight panels can't drop their base metal below it. They come out lighter on base metal than the role formula alone would give, but not by much. If the recycler gives its own yields instead of using the game's debris tables, that minimum goes away.
- **Generous scrap tables:** some small items scrap into a lot. A 0.4 kg PDA dismantles into a motherboard, a screen and three electronic parts, so it costs 17 units.
- **Categories are the game's:** an item is costed by the first of its categories that has a profile, so a few land in odd places.

## Regenerating

The list comes from two tools in `Tools/`, kept out of the plugin's build:

```
dotnet run -c Release --project Design/Fabrication/Tools/ItemScan -- <folder>
dotnet run -c Release --project Design/Fabrication/Tools/CostModel -- <folder> Design/Fabrication/base_item_costs.csv
```

ItemScan reads the game's data from its default Steam install and writes `items.tsv` to the folder. CostModel turns that into the CSV. The material shares, building blocks and wall and floor rules are all at the top of `CostModel/Program.cs`.
