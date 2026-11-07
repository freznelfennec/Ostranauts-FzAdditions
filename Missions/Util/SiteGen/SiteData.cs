using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //Sites and the corporations that own them, read from every mod's data/fz/sites folder in load order, the way
    //fz/fabrication is read, so any mod can add a corporation or change one. The game reads these with its own reader
    //(SiteDataLoader) and the preview tool with System.Text.Json, so they're plain classes with public properties.
    //
    //  fz/sites/sitetypes: one per kind of site (Relay, BlackSite): what each room role holds, weapons, sensors, ammo,
    //                     loot odds and how much a vault gives. Relays are unmanned and simpler, so they give less.
    //  fz/sites/corps:     one per corporation: its factions, blueprints, style, and anything it adds or changes.
    //  fz/sites/styles:    the items a site is built from.
    //
    //A later entry with the same strName replaces an earlier one, so another mod can change a corporation by
    //redefining it.

    //Fixtures one room role gets: aItems are alternatives, one picked for each placed (so rooms vary), nMin to nMax of
    //them. With bLoot, each placed one is a container that gets a loot spawner for the room's loot. fChance is the odds
    //the entry is used at all, for optional clutter.
    public class JsonSiteFixture
    {
        public string strRole { get; set; }
        public string[] aItems { get; set; }
        public int nMin { get; set; }
        public int nMax { get; set; } = 1;
        public float fChance { get; set; } = 1f;
        public bool bLoot { get; set; }
    }

    //The rounds loaded into one kind of weapon, in stacks of nStack
    public class JsonSiteAmmo
    {
        public string strWeapon { get; set; }
        public string strAmmo { get; set; }
        public int nMin { get; set; }
        public int nMax { get; set; }
        public int nStack { get; set; } = 1;
    }

    //Loose things lying about a room: nMin to nMax loot spawners on its open floor, each dropping strLoot where it
    //stands, so rooms are worth searching on the way to the vault. fChance is the odds the entry is used at all.
    public class JsonSiteFloorLoot
    {
        public string strRole { get; set; }
        public string strLoot { get; set; }
        public int nMin { get; set; }
        public int nMax { get; set; } = 1;
        public float fChance { get; set; } = 1f;
    }

    //A blueprint a vault holds only rarely, on top of its usual ones: each vault rolls fChance for it
    public class JsonSiteRare
    {
        public string strBlueprint { get; set; }
        public float fChance { get; set; }
    }

    public class JsonSiteType
    {
        public string strName { get; set; }             //A SiteKind: Relay or BlackSite
        public string strStyle { get; set; }            //The style used when the corporation doesn't name one
        public JsonSiteFixture[] aFixtures { get; set; }
        public string[] aWeaponsPDC { get; set; }       //Picked from for each PDC mount
        public string[] aWeaponsRail { get; set; }      //Picked from for each rail mount
        public string[] aSensors { get; set; }
        public int nSensorsMin { get; set; }
        public int nSensorsMax { get; set; }
        public JsonSiteAmmo[] aAmmo { get; set; }
        public float fLootChance { get; set; } = 1f;    //Odds each loot container gets loot
        public Dictionary<string, string> mapLoot { get; set; } //Room role to a loot table, for containers with bLoot
        public int nBlueprintsMin { get; set; }         //Blueprints each data vault holds
        public int nBlueprintsMax { get; set; }
        public float fSpecialisedShare { get; set; } = 1f; //Share of those that are Specialised, the rest Ordinary
        public JsonSiteFloorLoot[] aFloorLoot { get; set; } //Loose things on room floors
        public string strLockoutController { get; set; } //The terminal whose hack releases the site's equipment to be uninstalled
        public int nCoresMin { get; set; }              //Data cores sealed in each data vault's storage
        public int nCoresMax { get; set; }
    }

    public class JsonSiteCorp
    {
        public string strName { get; set; }             //Its ID, used in template names and commands
        public string strNameFriendly { get; set; }
        public string strFaction { get; set; }          //The owning faction, whose standing the site affects
        public string strSecurityFaction { get; set; }  //Its security force, for defenders later
        public string strDesc { get; set; }             //How its sites are themed, for designers
        public string[] aKinds { get; set; }            //Which kinds of site it runs
        public string[] aBlueprints { get; set; }       //Blueprints its vaults can hold
        public string[] aBlueprintCategories { get; set; } //And every blueprint in these recipe categories and below
        public JsonSiteRare[] aRareBlueprints { get; set; } //Rolled for separately by each vault, and never in the usual picks
        public JsonSiteFloorLoot[] aFloorLoot { get; set; } //Added to the site type's, for its own munitions and gear
        public bool bModTech { get; set; }              //Whether its sites may hold the mod's own tech: Fz blueprints, items and exotics
        public Dictionary<string, string> mapStyles { get; set; } //Kind to style
        public JsonSiteFixture[] aFixtures { get; set; } //Added to the site type's
        public string[] aWeaponsPDC { get; set; }       //Replace the site type's when given
        public string[] aWeaponsRail { get; set; }
        public string[] aSensors { get; set; }
        public Dictionary<string, string> mapLoot { get; set; } //Role, or Kind.Role, to a loot table; replaces the site type's
        public string strRelayName { get; set; }        //Public names, {0} for a number
        public string strBlackSiteName { get; set; }
    }

    //The items a site is built from (SiteStyle)
    public class JsonSiteStyle
    {
        public string strName { get; set; }
        public string strHullWall { get; set; }
        public string strTunnelWall { get; set; }
        public string[] aRockWalls { get; set; }
        public string strRoomFloor { get; set; }
        public string strCorridorFloor { get; set; }
        public string strCoreFloor { get; set; }
        public string strVaultFloor { get; set; }
        public string strDockFloor { get; set; }
        public string strTunnelFloor { get; set; }
        public string strLight { get; set; }
    }

    //Everything loaded from fz/sites
    public class SiteData
    {
        public const string GenericCorp = "Generic";

        public readonly Dictionary<string, JsonSiteType> Types = new Dictionary<string, JsonSiteType>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, JsonSiteCorp> Corps = new Dictionary<string, JsonSiteCorp>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, JsonSiteStyle> Styles = new Dictionary<string, JsonSiteStyle>(StringComparer.OrdinalIgnoreCase);

        public JsonSiteCorp Corp(string name)
        {
            if (name != null && Corps.TryGetValue(name, out JsonSiteCorp corp)) return corp;
            Corps.TryGetValue(GenericCorp, out corp);
            return corp;
        }

        //The corporation and site type merged into what one site is built with
        public SiteProgram Program(SiteKind kind, string corpName)
        {
            Types.TryGetValue(kind.ToString(), out JsonSiteType type);
            JsonSiteCorp corp = Corp(corpName);
            var program = new SiteProgram { Kind = kind, Corp = corp?.strName ?? GenericCorp, Owner = corp?.strFaction, ModTech = corp?.bModTech ?? false };
            if (type == null) return program;

            string styleName = corp?.mapStyles != null && corp.mapStyles.TryGetValue(kind.ToString(), out string s) ? s : type.strStyle;
            program.Style = Styles.TryGetValue(styleName ?? "", out JsonSiteStyle style) ? SiteStyle.From(style, kind) : SiteStyle.For(kind);
            program.Fixtures.AddRange((type.aFixtures ?? new JsonSiteFixture[0]).Concat(corp?.aFixtures ?? new JsonSiteFixture[0]).Where(f => f?.aItems != null && f.aItems.Length > 0));
            program.WeaponsPDC = Pick(corp?.aWeaponsPDC, type.aWeaponsPDC);
            program.WeaponsRail = Pick(corp?.aWeaponsRail, type.aWeaponsRail);
            program.WeaponsFallback = (type.aWeaponsPDC ?? new string[0]).Concat(type.aWeaponsRail ?? new string[0]).Distinct().ToArray();
            program.Sensors = Pick(corp?.aSensors, type.aSensors);
            program.SensorsMin = type.nSensorsMin;
            program.SensorsMax = Math.Max(type.nSensorsMin, type.nSensorsMax);
            foreach (JsonSiteAmmo ammo in type.aAmmo ?? new JsonSiteAmmo[0])
            {
                if (ammo?.strWeapon != null) program.Ammo[ammo.strWeapon] = ammo;
            }
            program.LootChance = type.fLootChance;
            foreach (var pair in type.mapLoot ?? new Dictionary<string, string>()) program.Loot[pair.Key] = pair.Value;
            foreach (var pair in corp?.mapLoot ?? new Dictionary<string, string>())
            {
                //Kind.Role applies to that kind only
                int dot = pair.Key.IndexOf('.');
                if (dot < 0) program.Loot[pair.Key] = pair.Value;
            }
            foreach (var pair in corp?.mapLoot ?? new Dictionary<string, string>())
            {
                int dot = pair.Key.IndexOf('.');
                if (dot > 0 && string.Equals(pair.Key.Substring(0, dot), kind.ToString(), StringComparison.OrdinalIgnoreCase)) program.Loot[pair.Key.Substring(dot + 1)] = pair.Value;
            }
            program.BlueprintsMin = type.nBlueprintsMin;
            program.BlueprintsMax = Math.Max(type.nBlueprintsMin, type.nBlueprintsMax);
            program.SpecialisedShare = type.fSpecialisedShare;
            program.CoresMin = type.nCoresMin;
            program.FloorLoot.AddRange((type.aFloorLoot ?? new JsonSiteFloorLoot[0]).Concat(corp?.aFloorLoot ?? new JsonSiteFloorLoot[0]).Where(f => f?.strLoot != null && f.strRole != null));
            program.LockoutController = type.strLockoutController;
            program.CoresMax = Math.Max(type.nCoresMin, type.nCoresMax);
            program.PublicName = kind == SiteKind.Relay ? corp?.strRelayName : corp?.strBlackSiteName;
            return program;
        }

        private static string[] Pick(string[] first, string[] fallback) => first != null && first.Length > 0 ? first : fallback ?? new string[0];
    }

    //One site's build settings: its site type with its corporation's changes applied
    public class SiteProgram
    {
        public SiteKind Kind;
        public string Corp = SiteData.GenericCorp;
        public string Owner;
        public bool ModTech;
        public string PublicName;
        public SiteStyle Style;
        public readonly List<JsonSiteFixture> Fixtures = new List<JsonSiteFixture>();
        public string[] WeaponsPDC = new string[0];
        public string[] WeaponsRail = new string[0];
        public string[] WeaponsFallback = new string[0]; //The site type's own, for a mount none of the corporation's fit
        public string[] Sensors = new string[0];
        public int SensorsMin, SensorsMax;
        public readonly Dictionary<string, JsonSiteAmmo> Ammo = new Dictionary<string, JsonSiteAmmo>();
        public float LootChance = 1f;
        public readonly Dictionary<string, string> Loot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int BlueprintsMin, BlueprintsMax;
        public float SpecialisedShare = 1f;
        public int CoresMin, CoresMax;
        public readonly List<JsonSiteFloorLoot> FloorLoot = new List<JsonSiteFloorLoot>();
        public string LockoutController;

        //Blueprints the corporation's vaults can hold, by tier, filled by the game from its recipes (the preview has
        //none, so its vaults list none)
        public readonly List<string> SpecialisedBlueprints = new List<string>();
        public readonly List<string> OrdinaryBlueprints = new List<string>();
        public readonly List<(string blueprint, float chance)> RareBlueprints = new List<(string, float)>();

        public IEnumerable<JsonSiteFixture> For(RoomRole role) => Fixtures.Where(f => string.Equals(f.strRole, role.ToString(), StringComparison.OrdinalIgnoreCase));

        public string LootFor(RoomRole role) => Loot.TryGetValue(role.ToString(), out string loot) ? loot : null;
    }
}
