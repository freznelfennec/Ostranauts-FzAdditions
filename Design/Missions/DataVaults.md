# Data vaults, data cores and the breach spike

How a site pays out, and how players break into it. Built so far:
- the vault item;
- data core items;
- what each vault holds;
- the breach spike for door locks.

The vault interface and vault hacking are built too (`FzBreachApp`, `VaultHack`), but not tried in the game.

## The choice at the vault

Per the user, a data vault holds both prizes: blueprints in its archive and data cores in its sealed storage. Hacking it means choosing what to spend time on:

- **Decrypt archive entries** for blueprints, which the player keeps for good (`Blueprints.Learn`).
- **Unlock the core storage**, one of the hack's options. The cores then have to be hauled out: each is 120 kg and cumbersome, so it's dragged back to the ship one at a time. Cores sell well, and later can be sold to a rival corporation for standing.

Either takes a skilled hacker, or a breach spike and a lot more time.

Per the user, the site's self-destruct countdown starts the moment a ship docks, so time is the price of both. That's with the mission lifecycle, still to build; defenders come later too.

## The data vault (`ItmFzDataVault01`)

- **What it is:** a copy of the base game's AI Cargo (the same 2×3 item definition, light and hum), installed only.
  - Per the user, it can't be uninstalled: there's no installables entry for it and no pick-up interaction, so nothing offers to uninstall, dismantle or carry it.
  - With 40 damage it's very hard to destroy.
- **Core storage:** it's a container (3 slots, cores only, `TIsFitContainerFzDataCore`). Its only interaction, `FzDataVaultStorage`, is a copy of the game's Inventory that only works once the storage is open (`IsFzVaultStorageOpen`, persisted, `TIsFzVaultStorageOpen`). Its cores are put in when the template is made (`SiteFurnish`, `nCoresMin` to `nCoresMax` from the site type). `fz site unlock` opens every vault's storage on the current encounter, for testing until the interface exists.
- **Contents:** set when the site is made (`SiteFurnish.VaultContents`), in its `FzDataVault` prop map:
  - `strCorp`, the corporation;
  - `strBlueprints`, a comma-separated list picked from the corporation's pool.
- **Relays and black sites:** relay vaults hold 1 to 2 blueprints, 60% Specialised. Black site vaults hold 2 to 4, 80% Specialised. Both are in `fz/sites/sitetypes`.
- **Placement:** one per relay vault room, one or two per black site vault room.
- **Destroyed:** it's gone (`ACTDefaultDestroy`), and so are its blueprints.

Per the user, the vault keeps the AI Cargo look until custom art is made. The options compared, at the same scale: `Design/Missions/Looks/vault_options.png`.

### Other looks the vault could have

| Option | Pros | Cons |
|---|---|---|
| AI Cargo (now) | Reads as valuable hardware, has a hum and a light, 2×3 | Players may know it as cargo |
| Server rack (`ItmRack1x401` look) | Reads as a server | It's a container, so players will try to open it |
| Navigation console (`ItmStationNav`) | A proper terminal look with a use point | Its art reads as nav |
| Cargo Trade Kiosk (`ItmKioskCargo01`) | Already a screen with a panel | The recycler and fabricator use it |
| Faction kiosk (`ItmFactionKiosk01*`) | Each corporation could get its own face | Few corporations have one |
| New art, made like the SMES unit (`make_smes.ps1`) | Can be made to look like a vault, with lit states for locked, breached and lockdown | Needs art |

### Data core (`ItmFzDataCore01`)

- **Item:** one loose item on the base game's nav module board look (`ItmNavMod`, 1×1), chosen by the user from `Design/Missions/Looks/core_options.png`. Other looks considered: the wall battery, the Cerebrum armature, the laser capacitor, science cargo, the MHD generator, the field coils and AI Cargo.
- **Weight and price:** 120 kg and cumbersome, so it's dragged. Base price 60,000.
- **Where it is:** only in data vaults' storage. Relay vaults hold 0 to 1, black site vaults 1 to 3.
- **Destroyed:** shooting it wipes it.
- **Not tried in the game:**
  - whether a vault that's destroyed drops its cores, which would make shooting it a way round the hack;
  - whether a cumbersome item can be taken out of a container straight onto the floor.
- **Later:**
  - a buyer for cores at rival corporations' stations, paying in standing as well as money;
  - a core's value could depend on its corporation and site kind, through a persisted condition such as `StatFzCoreGrade`.

## Vault interface (built, not tried in the game)

### As built

- **Look:** per the user, it's one of the game's PDA apps, BREACH//OS, in blue, laid out from `fz dumpui pda`: the PDA's own frame, screen, top bar and nav bar (taken off the live PDA by `PdaLook`), and inside them the pattern the game's apps share. That means a title bar, a link strip with the target and the device (the PDA, or a breach spike and its jobs left), TRACE, a list with column headers whose rows are clicked to pick, a bordered SESSION box, and rounded QUIET, FORCE and RUN buttons. The mockup is `Design/Missions/Looks/breachos_mockup.png`.
- **Opening it:** "Access Vault" (`FzDataVaultGUI`, through `FzDataVaultGUIAllow`, raising the `FzDataVault` prop map) opens BREACH//OS in the player's PDA (`BreachAppPatch` catches `CrewSim.RaiseUI`), which slides in as it does for its own apps. Its home and close keys, its hotkey or another app close it. It's sized in ratios like the mod's other panels.
  - **Header:** the corporation, a STORAGE lamp, and the site's TRACE.
  - **ARCHIVE:** the entries, then the core storage, with UP and DOWN.
  - **INTRUSION:** target, progress bar, status and time left, operator, access, method and the last result, with QUIET, FORCE and HACK.
- **Working:** as the game's lock panel does, the hack runs while the panel is open, in game time, and only while the operator (the selected crew) stays within 2.5 tiles of the vault's use point. A finished target stops the hack, and so does walking away.
- **Work times:** an entry is 480 standard seconds of skilled QUIET work and the core storage 300. FORCE is three times faster.
- **Trace:** QUIET raises the site's trace by 2.5 a game minute and FORCE by 10 (`SiteTrace`), so a slow operator draws more trace per entry.
- **Access:**
  - an unskilled operator with a breach spike works at a quarter of the pace;
  - a skilled one with a spike works at double;
  - starting a fresh target with a spike uses one of its jobs;
  - with neither, HACK refuses.
- **Standing:** each start runs the game's `HackingCrimePenalty` on the vault, which `SiteIdentity` forgives for a player who docked dark.
- **Results:**
  - a new blueprint is learned;
  - a known one drops a data chip at the operator's feet, named for it;
  - finished core storage sets `IsFzVaultStorageOpen`.
- **Saving:** a hack is kept in the vault's prop map (`strProgress`, `strStorage`, `strDone`, `strTarget`, `strMethod`), so it can be left and resumed, and survives saves.
- **Not built yet:**
  - trace thresholds below full (a black site guard response);
  - the vault needing power;
  - the countdown on docking.
- **Not tried in the game:**
  - whether the panel opens and lays out right;
  - whether its prop map writes stick once the panel closes;
  - whether the data chip's name lasts through a save.

### The design it came from

The game's lock panel is a PIN pad, which doesn't fit a vault. The vault gets its own panel, `FzDataVaultGUI`, opened by a `FzDataVaultGUI` interaction (the guipropmap already names the prefab). It's sized in ratios of its parent, like every panel in the mod.

```
+--------------------------------------------------------------+
| PROKOFIEV PENAL COLONY  /  VAULT 3F-A2      [ SEALED ]  TRACE |
|                                                         [###  ]|
+---------------------------+----------------------------------+
| ARCHIVE                   | INTRUSION                        |
|  > [##  ##  ##] Weapons    |  target: entry 2                 |
|    [##  ##  ##] Doors      |  method: (o) QUIET   ( ) FORCE   |
|    [##  ##  ##] Weapons    |  progress  [######......] 52%    |
|                           |  operator: J. Okafor (Hacking 3) |
|                           |  [ BEGIN ]   [ ABORT ]           |
+---------------------------+----------------------------------+
| EXTRACTED: Railgun (learned)   Soyuzneft PDC (already known)  |
+--------------------------------------------------------------+
```

- **Archive:** one row per blueprint, encrypted. Only its category shows, as a hint at what it is. A decrypted row shows its name.
- **Core storage:** per the user, its own row under the archive: `CORE STORAGE — SEALED (2 cores)`. It's hacked like an entry. When done it sets `IsFzVaultStorageOpen`, and the vault's Open Core Storage interaction starts working.
- **Intrusion:** pick a row and a method, then BEGIN. The selected character stands at the vault's use point and works a timed job; the panel only shows progress. Progress is kept in the vault's prop map (`FzProgress<n>`), so a hack can be left and picked up again, and survives save and load.
- **Method:**
  - **QUIET** is slow and raises TRACE a little.
  - **FORCE** is about three times faster and raises TRACE a lot.
- **Speed:** depends on the operator's `SkillHacking`. With a breach spike, an unskilled operator works at a quarter of the skilled pace, and a skilled one works twice as fast. Each entry worked uses one of the spike's jobs.
- **TRACE:** the site noticing. It's kept on the site's ShipCO as `StatFzSiteTrace` (persisted), so all its vaults share it (`SiteTrace`).
  - At thresholds it raises the site's alert (`StatFzAIAlert`), and, for black sites, sends the guard post to the vault. The countdown is already running from docking.
  - Per the user, a full trace hands the player to a system still to be designed, in which the corporation's mercenaries hunt them. `SiteTrace.Add` reports a full trace once a site (`IsFzSiteTraced`) through `SiteTrace.OnTraceFull`. For now that only logs and warns the player.
- **Extraction:** a finished entry is learned (`Blueprints.Learn`). Per the user, one already known comes out as a data chip (`ItmFzDataChip`, small, pocketable, sellable), so vaults stay worth hacking once a player knows everything.
- **Power:** the vault draws from the site's power, like the doors' lock panels. With power cut, BEGIN is greyed out. Unlike doors, that stays so with a breach spike, which needs the vault awake.
- **Standing:** each BEGIN is a hacking act on the owner's property, so it goes through `ApplyFactionReps`. `SiteIdentity` then forgives it unless the player docked with their transponder on.

## The breach spike (`ItmFzBreachSpike`, built for door locks)

- **Item:** a handheld, pocketable grey-market item, on the base game tablet's look for now. Base price 12,000.
- **Uses:** per the user, it's good for 10 jobs (`StatFzBreachUses`, persisted), and burns out at none.
- **Where to find one:** per the user, belt pirate crews carry them. It's the one piece of mod equipment found outside Faure-Zhang's sites.
  - `PirateLootPatch` gives the `CombatPilotBeltPirates` person spec the table `FzLootBeltPirateCrew`, once the game's data has loaded. That table is their usual loot plus a 25% chance of a spike.
  - The base table isn't copied, so game updates to it still apply.
- **Doors:** `BreachSpikePatch` changes the game's lock hack.
  - **Without `SkillHacking`:** the hack starts anyway, and the code settles about four times more slowly (`HackingActive` runs one frame in four).
  - **With the skill:** the game's own hack runs twice per frame.
  - **Either way:** it uses a job, and the standing cost applies as normal.
- **Vaults:** the vault interface treats a spike the same way.
- **Not yet decided:** whether it should also work on the game's PDA cracking (`ENCHackPDA*`).

Other ways to break in, for later:
- **Cut the power:** make doors drop to manual, so they can be forced open with a crowbar, as an alternative to hacking.
- **Software:** a modded PDA cartridge (the base game has `ItmRandomPDACartModded`) as the spike's software half, so the spike needs a cartridge loaded and different cartridges change its speed.
- **Uplink:** hack the site's systems remotely from the docked ship's nav console. That would be slower again, but safe from defenders.
