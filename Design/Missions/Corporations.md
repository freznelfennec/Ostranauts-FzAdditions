# Corporations

Each corporation that runs sites is defined in `data/fz/sites/corps` (`JsonSiteCorp`), so another mod can add or change one. A corporation sets:

- **Factions:** the owning faction, whose standing the site affects, and its security faction, for defenders later.
- **Blueprints:** the blueprints its vaults hold, named or by recipe category.
- **Rare blueprints:** each vault rolls for these separately (`aRareBlueprints`), and they never come up in the usual picks.
- **Look:** its style for each kind of site (walls, floors, lights).
- **Fixtures:** clutter it adds to the site type's.
- **Defences:** its guns and sensors.
- **Loot:** its tables, by room role, or by `Kind.Role` for one kind only.
- **Names:** the sites' public names.

Per the user, only corporations run sites for now, not governments.

## The mod's own technology

Only Faure-Zhang has `bModTech`. The others never hold:
- the mod's blueprints (Exotic tier, a recipe making an `ItmFz` item, or the Fabrication or FaureZhang categories);
- its equipment;
- exotic matter.

The one exception is the breach spike, which belt pirates carry (`DataVaults.md`).

## Relays and black sites

Every corporation runs both kinds. Relays are less rewarding:
- fewer blueprints a vault, fewer of them Specialised;
- fewer data cores in a vault's storage;
- container loot less often, and from thinner tables.

Black sites have crews (still to come), larger vaults and the better tables.

Per the user, these numbers are a starting point. Timing will need tuning so a player can't take everything a site offers every time.

## Owners

Owners are factions from the base game's `star_system.json`, except Faure-Zhang's. That faction, `FzFaureZhang`, is defined in `data/fz/factions` and added to a game that lacks it (`FzFactions`).

## The corporations

| Corporation | Owner (security) | Blueprints its vaults hold | Theme |
|---|---|---|---|
| **Prokofiev Penal Colony, LLC** | `HQCHCiv` (`HQCH`, Prokofiev SecForce) | Soyuzneft PDC, missile launcher and Belter Bomb missile, 20mm rounds, airlocks; Doors | Penal-industrial: dark Testudo plate and orange light. Heavy Soyuzneft guns. Relays are prison-network switchboards; black sites are interrogation annexes and contract-labour holds. Big armouries, propaganda posters, liquor and cigarettes in the lockers. |
| **Ayotimiwa Ship Breaking Co.** | `OKLGCorp` (`OKLGLEO`, AyoSec) | Airlocks, reclaimed Sulaiman fusion parts; Hull, Doors, Parts and Scrap | Salvage: mismatched aerostat plate and grating, cargo and toolboxes everywhere. Relays are salvage-claim beacons; black sites are where hulls that never reached the yard went. Storage full of parts and damaged fusion gear. |
| **Coalition of Ceres Resource Extraction (CCRE)** | `CCRECiv` (`CCRE` Enforcers) | Smartlink PDC, launcher and Rancor missile; D2O and helium canisters, cryo pump, fuel regulator, reactor core, IC reactor; FusionParts and LifeSupport | Fuel and ice: Caylon plate, black sites cut into ice instead of rock, D2O and He3 cargo in the halls. Relays are extraction-claim relays; black sites are fuel depots on no manifest. |
| **Peralta Prospecting Concern** | `OKLGProspector` | Every make of sensor; Sensors and Tools | Survey: Langdon-Phillips plate, a hull bristling with sensors (every make), light defences. Relays are survey-data relays; black sites hold unfiled claims, with mineral samples in every crate. |
| **Cloudbreak** | `VCBRCiv` (`VCBRLEO`, Cloudbreak Corporate Security) | The Polaris nav console and every nav module (ControlSystems), Polaris decoy launcher, GDM-4 and GDM-800 decoys, optical sensor; LuxuryGoods and Furniture. **Rare:** the GDM-X1 decoy, 5% a vault. | Venusian aerostat money: Van Hummel plate, pale floors, planters and couches even on relays. Decoys over guns. Relays are traffic-control relays; black sites are executive retreats with something to hide. |
| **Suzhou Orbital** | `MSUZCiv` (`MSUZ`) | Zhuangzi sensors, BNW CIWS; Electronics and Media | Orderly Minsheng plate, Zhuangzi sensors, Brave New World guns. Relays are municipal data exchanges; black sites are quiet, well-kept, state-adjacent research stations. |
| **Hangzhou** | `MHNGCiv` (`MHNG`) | BNW CIWS, launcher and Little Dragon missile; IndustrialProducts and HVAC | Minsheng plate, BNW launchers and more PDCs than Suzhou. Relays are freight-routing relays; black sites are bonded warehouses whose bonds nobody can find. Locked crates. |
| **Faure-Zhang** | `FzFaureZhang` | Its own tech; see below | The mod's own. Spotless MSS white, white light, guns bought from everyone. Relays carry beam-former telemetry; black sites are fronts for the exotic-matter refinery. The only sites with exotics and beam formers in their containers. |
| **Generic (Unmarked Operator)** | none | The unbranded guns (coilgun, railgun, mass thrower), 20mm and 150mm rounds, and everything else in Weapons, Sensors, FusionParts, Doors and Hull. **Rare:** the GDM-X1 decoy, 2% a vault. | No livery. The fallback for a site whose corporation isn't defined, and for testing. |

## Every munition has a source

| Munition | Where its blueprint is |
|---|---|
| 20mm PDC rounds | Generic, Prokofiev |
| 150mm mass-thrower rounds | Generic |
| Smartlink Rancor missile | CCRE |
| BNW Little Dragon missile | Hangzhou |
| Soyuzneft Belter Bomb missile | Prokofiev |
| Polaris GDM-4 and GDM-800 decoys | Cloudbreak |
| Polaris GDM-X1 decoy (never fails) | Rare only: Cloudbreak 5%, Generic 2% |

Per the user, the GDM-X1 is otherwise only had by salvaging combat ships. Its recipe is Specialised, so recycling one doesn't teach it. That's a named override in the CostModel generator (`specialisedNames`), so it survives regenerating the recipes.

## Faure-Zhang's blueprints

**Have recipes now:** these are hand-written in `fz_manual_fztech.json` and `fz_manual_beamformers.json`, tier Exotic. Each takes a little Exotics, so they can only be made with Faure-Zhang's own refinery output.

| Blueprint | Item |
|---|---|
| `FzSmes01` | Basic SMES Unit |
| `FzSmesReactorTap01` | Basic SMES Reactor Tap |
| `FzJumpFieldEmitter01` | Jump Drive Field Emitter |
| `FzJumpNavCom` | Jump NavCom |
| `FzBeamFormer01` to `03` | Matter beam formers (Specialised) |

**Planned:** these are named in Faure-Zhang's list already, and join its vaults once their items and recipes exist.

| Blueprint | Item |
|---|---|
| `FzSmes02`, `FzSmes03` | Advanced and Superior SMES units |
| `FzSmesReactorTap02`, `FzSmesReactorTap03` | Advanced and Superior reactor taps |
| `FzJumpFieldEmitter02`, `FzJumpFieldEmitter03` | Higher-tier field emitters |
| `FzRecycler02`, `FzRecycler03` | Advanced and Superior recyclers |
| `FzFabricator02`, `FzFabricator03` | Advanced and Superior fabricators |

The basic recycler and fabricator stay public tech, so they aren't vault blueprints.

**Costs:** the material costs of the SMES, tap, emitter and NavCom recipes are rough starting points, scaled to comparable base game items plus Exotics.
