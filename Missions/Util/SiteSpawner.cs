using Freznel.FzAdditions.Util.SiteGen;
using Ostranauts.Trading;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //The game side of generated sites: wraps a SiteLayout in a JsonShip, registers it as a ship template, and spawns it.
    //
    //A template is only held in DataHandler.dictShips for the session. A spawned station saves its own copy of its
    //layout, so it never needs the template again; if the game does ask for one after a load, SiteTemplatePatch builds
    //it again from its name, which holds the kind, size and seed.
    public static class SiteSpawner
    {
        //Builds a site's template and registers it, unless it already is. Returns its name. Null if the site couldn't be laid out.
        public static string Register(SiteKind kind, SiteSize size, int seed)
        {
            string name = SiteShell.TemplateName(kind, size, seed);
            if (DataHandler.dictShips != null && !DataHandler.dictShips.ContainsKey(name))
            {
                SiteLayout layout = SiteShell.Build(kind, size, seed, SiteCatalogLoader.Get());
                if (layout == null) return null;
                DataHandler.dictShips[name] = ToJsonShip(layout);
            }
            return name;
        }

        public static JsonShip ToJsonShip(SiteLayout layout)
        {
            JsonItem[] items = layout.Items.Select(item => new JsonItem
            {
                strName = item.Name,
                fX = item.X,
                fY = item.Y,
                fRotation = item.Rotation,
                strID = item.Id,
                aGPMSettings = item.Pin != null ? LockPanel(item.Pin) : null
            }).ToArray();

            SiteGrid grid = layout.Grid;
            return new JsonShip
            {
                strName = layout.TemplateName,
                strTemplateName = layout.TemplateName,
                publicName = grid.Kind == SiteKind.Relay ? "Corporate Relay" : "Black Site",
                make = "Unknown",
                model = $"{grid.Size} {(grid.Kind == SiteKind.Relay ? "relay station" : "black site")}",
                designation = "Station",
                origin = "Unknown",
                aItems = items,
                aDockingPorts = layout.DockIds.ToArray(),
                nCols = layout.Width,
                nRows = layout.Height,
                vShipPos = new Vector2(layout.OriginX, layout.OriginY),
                nConstructionProgress = 100,
                fShallowMass = Mass(items),
                //Every base template has these, and the game reads them without checking: StarSystem.SpawnStation sets
                //json.objSS.bIsBO, and a few docking checks read json.aDocked
                objSS = new JsonShipSitu(),
                aDocked = new System.Collections.Generic.Dictionary<string, string>()
            };
        }

        //A door's lock panel with its own PIN: the game's Door01 panel (LockDoor01Closed), with strPIN added, as base
        //ship templates give their doors. Without it a door opens with the panel's default code.
        private static JsonGUIPropMap[] LockPanel(string pin)
        {
            var settings = new List<string>();
            if (DataHandler.dictGUIPropMapUnparsed != null && DataHandler.dictGUIPropMapUnparsed.TryGetValue("LockDoor01Closed", out JsonGUIPropMap panel) && panel?.dictGUIPropMap != null)
            {
                settings.AddRange(panel.dictGUIPropMap);
            }
            else
            {
                settings.AddRange(new[] { "strGUIPrefab", "GUILocks", "strFriendlyName", "Door Lock", "strBrand", "Bieler", "strBrandSub", "Commercial Door Systems" });
            }
            settings.Add("strPIN");
            settings.Add(pin);
            return new[] { new JsonGUIPropMap { strName = "Panel A", dictGUIPropMap = settings.ToArray() } };
        }

        //The same sum the game's asteroid generator makes
        private static double Mass(JsonItem[] items)
        {
            double total = 0.0;
            foreach (var group in items.GroupBy(i => i.strName))
            {
                DataCO data = DataHandler.GetDataCO(group.Key);
                if (data != null) total += data.GetCondAmount("StatMass") * group.Count();
            }
            return total;
        }

        //Spawns a generated site as a test station beside the player
        public static Ship SpawnTest(SiteKind kind, SiteSize size, int seed, double km, string owner, out string message)
        {
            string template = Register(kind, size, seed);
            if (template == null)
            {
                message = $"Seed {seed} couldn't be laid out as a {size} {kind}: {SiteShapes.FailureReason(kind)}. Try another seed.";
                return null;
            }
            Ship station = StationPlacer.SpawnNear(new StationPlacer.Request
            {
                Template = template,
                PublicName = (kind == SiteKind.Relay ? "Test Relay " : "Test Black Site ") + "{0}",
                Owner = owner,
                Km = km,
                Kind = TestEncounter.SpawnKind
            }, out message);
            if (station != null) message += $" Seed {seed}.";
            return station;
        }
    }
}
