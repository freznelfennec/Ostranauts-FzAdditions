# FzAdditions notes for Claude

Save anything new worth remembering about this project here, not in the auto-memory folder.

## Line endings

Use CRLF line endings in all FzAdditions source and data files. The user asked for this after edits left files with LF or mixed endings.

After creating or scripting changes to `.cs` or `.json` files, check for bare LF and convert to CRLF. The Write tool and PowerShell here-strings can introduce LF.

## Save/load consistency

The jump drive and NavCom must behave the same after a save and load as they did before it. Static state that only survives a load by accident is a bug.

Keep lasting state on the ship or its parts, where the game saves it: conditions with `bPersists` on the ShipCO, or prop maps on a part such as the nav station (prop maps on the ShipCO aren't saved). Anything held in static fields is a cache of that, and must be cleared in `SaveLoadPatch` when `CrewSim.EmptyScene` runs.

## conditions_simple files need unique strNames

Ostranauts loads every conditions_simple file of a mod into one dictionary keyed by the file's strName. Two files both named "Simple Conditions" collide with no log message. The conditions in the losing file don't exist, `DataHandler.GetCond` returns null for them, and they silently vanish from aStartingConds. Any condtrig that requires them fails.

Give each new conditions_simple file its own strName. Suspect this when a data item loses an action or a condtrig fails with nothing in the log.

## Ostranauts data modding wiki

https://ostranauts.wiki.gg/wiki/Modding/Data_Modding is a fan-maintained guide to Ostranauts JSON mods, last edited 2025-11-12 with no target game version stated. It's useful for concepts and sub-page links, such as Modding/CondOwners, Modding/Interactions and Modding/Loot.

Treat the base game JSON and the decompiled `DataHandler` as authoritative. As of 2026-11-01, these wiki claims don't hold for this install:

- `loading_order.json` is in `Ostranauts_Data/Mods/`, not `Ostranauts_Data/`.
- The log is `Player.log`, not `output_log.txt`.
- Not every item needs a COOverlay of the same name. Most base items are tied to a condowner through `strItemDef`. Per the user, COOverlays avoid redefining similar objects and save memory in save files, and aren't required for every condowner.
- Socket arrays aren't always 9 elements.
- Its sample condtrig fields (`aCondsToTest`, `aCondsToAdd`) aren't used in base condtrigs. Use `aReqs`, `aForbids` and `aTriggers`.
