# Reactor breeder blankets

Built (2026-11-07), not yet tried in the game. Code in `Reactor/`, data in the `fz_*_reactor.json` files and `fz_manual_reactor.json`. Methane ice gives five times water ice's Volatiles (12,350), through CostModel's `oreMultiples`, and is left out of the material price fit.

## What they do

A breeder blanket fitted to a fusion reactor turns its loaded Volatiles into D2O and He-3. It returns a share of the fuel the reactor burns, at 5 Volatiles per kg of fuel. When its Volatiles are all converted it's spent, and has to be taken out and recycled. Per the user, blankets aren't meant to be strong: other reactor fuel mechanics will come later.

## Tiers

| | Basic | Advanced | Superior |
|---|---|---|---|
| Net yield of fuel burned | 15% | 25% | 40% |
| Conversion time | 24 h | 12 h | 3 h |
| Volatiles loaded | 10,000 | 25,000 | 60,000 |
| Life (kg of fuel bred) | 2,000 | 5,000 | 12,000 |
| Lasts at full torch | 54 h | 81 h | 121 h |
| Blankets per D2O tank | 9.9 | 7.5 | 6.2 |
| D2O tank lasts at full torch (453 h without) | 532 h | 603 h | 754 h |

- **Fuel returned:** D2O and He-3 in the ratio the reactor burns them, 0.667 kg of D2O to 1 kg of He-3 (`FusionIC.aReactantAmounts`). He-3 runs out first: a He-3 canister holds 5,216 kg against a D2O canister's 44,723 kg, so a ship carries about 13 He-3 canisters per D2O canister. Breeding only D2O would be nearly useless.
- **Conversion time:** delays the fuel without lowering the yield. About two thirds comes back within it and nearly all within three times it.
- **Assumptions:** hours of full-throttle torch at 5 pellets a second, 247 kg burned an hour. With 3 pellet feeders and 3 laser arrays the reactor tops out at 6 pellets a second (297 kg an hour), and a full-throttle burn held at full core temperature uses 5, as throttle cools the core at the rate 5 pellets heat it. A coasting reactor burns almost nothing, so blankets wear with torch time, not travel time.
- **A D2O tank:** one D2O canister plus about 13 He-3 canisters, 111,773 kg of fuel. Blanket counts include the bred fuel, since it's burned too.
- **SMES taps:** one tap at full 100 MW burns another 333 kg an hour, so a ship charging a jump drive wears blankets out more than twice as fast.

## Recipes

Hand-written in `fz_manual_reactor.json`, category `Reactor.BreederBlankets`.

| | Basic (`ItmFzBreederBlanket01`) | Advanced (`02`) | Superior (`03`) |
|---|---|---|---|
| Tier | Ordinary, learned by recycling 3 | Specialised | Exotic |
| Volatiles | 10,000 | 25,000 | 60,000 |
| Metal Alloys | 120 | 300 | 600 |
| Ceramics | 160 | 400 | 800 |
| Base Metals | 80 | 150 | 250 |
| Rare Earths | 30 | 120 | 300 |
| Silicates | | 100 | 200 |
| Precious Metals | | 20 | 60 |
| Exotics | | | 2 |
| Fabricating, speed 1 | about 27 min | about 75 min | about 2.8 h |
| Beam former wear | 890 | 2,340 | 5,212 |

The Superior's 2 Exotics cover the 1.2 its conversion uses; the rest is the price of its boost.

**Where to get them:**
- **Basic:** fabricated once learned by recycling, sold at the Faure-Zhang kiosk, and found on derelicts.
- **Advanced:** its blueprint is one of Faure-Zhang vaults' usual picks.
- **Superior:** a rare blueprint in Faure-Zhang vaults, rolled for separately.
- **Only Faure-Zhang:** both vault blueprints come only from Faure-Zhang, like the rest of the mod's own technology.

## Look

Per the user, blankets use the base game's ablative core liner sprite (`ItmFusionABLWall01Loose`, 32×16, 2×1 tiles, 28 kg), recoloured so they look distinct. Approved previews are in `Looks/breeder_blankets.png`.

- **Body:** the liner's slate blue (hue 195 to 230) becomes copper brown (hue 28, saturation 0.38) on every blanket.
- **Bottom stripe:** the liner's teal (hue 150 to 185), recoloured by tier in the mod's material colours:
  - Basic: ice blue, the Volatiles colour (hue 197, saturation 0.55, brightened 1.25×).
  - Advanced: amber, the He-3 colour (hue 38, saturation 0.62, brightened 1.3×).
  - Superior: magenta, the Exotics colour (hue 312, saturation 0.62, brightened 1.3×).
- **Spent:** a greyed body (saturation 0.08) and a dark stripe (brightness 0.55×, no colour).
- **Unchanged:** the tan top edge and the shading. The shape is unchanged too, so every form reuses the liner's normal map (`ItmFusionABLWall01Loosen`).
- **Generator:** `Reactor/Tools/make_blankets.ps1`, like the SMES and machine tier art.
- **Item definitions:** blankets only exist as loose items, so each tier and spent form needs one item definition, copied from the liner's.

## Fitting and removing

- **Fitting:** a work interaction on an installed reactor uses up the blanket in hand. Its state goes onto the reactor as persisted conditions: tier, Volatiles converted and fuel waiting. Persisted conditions carry across the reactor's form changes (Off, Ignition and so on). One blanket per reactor.
- **Removing:** "Remove breeder blanket" takes it out as an item with its state. Refitted, it carries on where it left off.
- **Spent:** it comes out as a spent item (`ItmFzBreederBlanket01Spent` and so on). Until then it does nothing but block the slot.
- **The reactor:** uninstalling or dismantling it drops the blanket. A damaged reactor keeps the blanket but breeds nothing, and a destroyed one loses it.

## Breeding

- **Measuring the burn:** the fuel burned is measured on each reactor fusion update, from the fuel aboard before and after. This includes the SMES taps' extra burn (`ReactorFuel`).
- **Converting:** the yield share comes out of the blanket's Volatiles at 5 per kg, and waits as converted fuel. It returns over the conversion time into the ship's D2O and He-3 canisters, up to their capacity (`Reserves.Fill`).
- **Running out:** once the Volatiles are used up, the blanket stops taking in new burn, finishes returning what's waiting, and is spent.

## Recycling a blanket

| Part | Returned |
|---|---|
| Structure materials (all but Volatiles and Exotics) | half, as usual |
| Unconverted Volatiles | half |
| Exotics (Superior) | half of the unconverted share |
| Converted fuel still waiting | all of it, into the ship's D2O and He-3 canisters |

- **A spent blanket:** gives back half its structure materials and nothing else.
- **Blueprints:** any blanket that isn't spent counts towards learning its blueprint.
- **Code:** this needs a hook in `Recycler.Breakdown`, since a blanket's yield depends on its state.

## Derelicts

- **Loose:** about 10% of derelicts get a loose Basic blanket on a free floor. The hook is the one that adds the Sideband Interceptor (`DerelictInterceptorPatch`, a `StarSystem.AddDerelict` postfix on the derelict's own template clone).
- **Fitted:** about 25% of derelicts with a fusion reactor have a Basic blanket fitted to it.
- **State:** either way, it starts 0 to 40% converted, picked at random: 6,000 to 10,000 Volatiles and 1,200 to 2,000 kg of life left, with no fuel waiting.
- **Odds:** both are to tune.

## Volatiles changes

| Per unit of Volatiles | Now | Planned |
|---|---|---|
| Fabricate time | 1.5 s | 0.05 s |
| Fabricate energy | 0.047 kWh | 0.0016 kWh |
| Recycle time | 1 s | 0.05 s |
| Recycle energy | 0.021 kWh | 0.001 kWh |

- **Energy:** cut in step with time, so the machines keep drawing the same 75 kW.
- **Where it's set:** in the CostModel generator, as an override for Volatiles, which writes `fz_materials.json`.
- **Ice recycling** on a speed-1 recycler: water ice (2,470 Volatiles) takes about 2 min instead of 41; methane ice (12,350) about 10 min instead of 3.4 h.
- **Other recipes:** everything else made from or recycled into Volatiles also speeds up.

## Wear multiplier for materials

- **New field:** materials get `fWearMultiplier`, how many units of beam former life each material unit uses. CostModel writes it.
- **Values:** 1 for every material to start, 0.05 for Volatiles. A material from another mod without the field wears at 1.
- **Code:** `BeamFormer.Wear` applies it when recycling and fabricating, and so does the check that a former has life for the whole item before a run starts.
- **Effect:** a water ice chunk wears 124 instead of 2,470, and a blanket's Volatiles wear a twentieth of what they would.

## Readout

A reactor tooltip section shows tier, yield, Volatiles left as a share converted, fuel waiting, and a warning once the blanket is spent.

## Code

- **New section:** `Reactor/`, for this and the other reactor fuel mechanics planned.
- **What it uses:** Common's reactor fuel update (`ReactorFuelPatch`) to measure the burn, and Fabrication's canister filling (`Reserves.Fill`), the way Missions already uses Fabrication.
- **Fabrication changes:** the recycler breakdown hook, the wear multiplier, and the CostModel overrides for Volatiles.

## Still to check while building

- **The reactor's burn rate:** worked out from `FusionIC.Fusion`, not measured in game.
- **Super fast forward:** a skip's burn needs its own breeding step, as the jump drive's skip code has for SMES charging.
- **The fitting interaction:** whether a base-game style interaction can take the blanket from the character's hands.
- **Fitted derelict blankets:** whether conditions can be set on a reactor in a derelict's template, or have to be added when it loads.
