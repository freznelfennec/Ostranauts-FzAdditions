using Freznel.FzAdditions.Util.SiteGen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //Fills a SiteCatalog from the game's loaded data, so furnishing follows the game's own item sockets. Built once,
    //after the data has loaded, and cached; the data doesn't change while the game runs.
    public static class SiteCatalogLoader
    {
        private static SiteCatalog cached;

        public static SiteCatalog Get()
        {
            if (cached != null) return cached;
            if (DataHandler.dictItemDefs == null || DataHandler.dictCOs == null || DataHandler.dictLoot == null) return null;

            var catalog = new SiteCatalog();
            foreach (var pair in DataHandler.dictLoot)
            {
                string[] cos = pair.Value?.aCOs;
                if (cos == null || cos.Length == 0) continue;
                catalog.Tags[pair.Key] = cos.Select(c => c.Split('=')[0]).ToArray();
            }

            foreach (var pair in DataHandler.dictCOs)
            {
                AddItem(catalog, pair.Key, pair.Value);
            }
            if (DataHandler.dictCOOverlays != null)
            {
                foreach (var pair in DataHandler.dictCOOverlays)
                {
                    if (pair.Value?.strCOBase == null || !DataHandler.dictCOs.TryGetValue(pair.Value.strCOBase, out JsonCondOwner baseCO)) continue;
                    AddItem(catalog, pair.Key, baseCO);
                }
            }
            cached = catalog;
            return catalog;
        }

        private static void AddItem(SiteCatalog catalog, string name, JsonCondOwner co)
        {
            if (co?.strItemDef == null || !DataHandler.dictItemDefs.TryGetValue(co.strItemDef, out JsonItemDef def)) return;
            if (def.nCols <= 0 || def.aSocketAdds == null || def.aSocketAdds.Length == 0) return;
            var shape = new ItemShape
            {
                Name = name,
                W = def.nCols,
                H = Math.Max(1, def.aSocketAdds.Length / def.nCols),
                Adds = def.aSocketAdds,
                Reqs = def.aSocketReqs,
                Forbids = def.aSocketForbids
            };
            SiteCatalog.ParsePoints(shape, co.mapPoints);
            catalog.Items[name] = shape;
        }
    }
}
