using Freznel.FzAdditions.Util.SiteGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    //Reads data/fz/sites from every loaded mod in load order (SiteData), and gives each site its corporation's
    //blueprints from the fabrication recipes. Data, not game state, so it's read once and kept.
    public static class SiteDataLoader
    {
        public const string Folder = "fz/sites/";

        private static SiteData cached;
        private static readonly object loadLock = new object();

        public static SiteData Get()
        {
            if (cached != null) return cached;
            lock (loadLock)
            {
                if (cached == null) cached = Load();
            }
            return cached;
        }

        public static void Reload()
        {
            lock (loadLock) cached = Load();
        }

        private static SiteData Load()
        {
            var data = new SiteData();
            var log = new StringBuilder();
            try
            {
                foreach (string modPath in ModData.ModPathsInLoadOrder())
                {
                    string mod = ModData.ModName(modPath);
                    foreach (string file in ModData.Files(modPath + "data/" + Folder + "sitetypes"))
                    {
                        foreach (JsonSiteType type in ModData.Read<JsonSiteType>(file)) Put(data.Types, type?.strName, type, mod, log);
                    }
                    foreach (string file in ModData.Files(modPath + "data/" + Folder + "corps"))
                    {
                        foreach (JsonSiteCorp corp in ModData.Read<JsonSiteCorp>(file)) Put(data.Corps, corp?.strName, corp, mod, log);
                    }
                    foreach (string file in ModData.Files(modPath + "data/" + Folder + "styles"))
                    {
                        foreach (JsonSiteStyle style in ModData.Read<JsonSiteStyle>(file)) Put(data.Styles, style?.strName, style, mod, log);
                    }
                }
                foreach (SiteKind kind in Enum.GetValues(typeof(SiteKind)))
                {
                    if (!data.Types.ContainsKey(kind.ToString())) log.AppendLine($"  no site type for {kind}, so those sites get no fixtures");
                }
                if (!data.Corps.ContainsKey(SiteData.GenericCorp)) log.AppendLine($"  no {SiteData.GenericCorp} corporation, which sites with an unknown one fall back on");
                foreach (JsonSiteCorp corp in data.Corps.Values)
                {
                    if (corp.strFaction != null && CrewSim.system != null && CrewSim.system.GetFaction(corp.strFaction) == null) log.AppendLine($"  corporation {corp.strName}: no faction {corp.strFaction} in this game");
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Site data failed to load: " + ex);
            }
            FzAdditions.Logger.LogInfo($"Site data: {data.Types.Count} site types, {data.Corps.Count} corporations, {data.Styles.Count} styles");
            if (log.Length > 0) FzAdditions.Logger.LogInfo("Site data notes:\n" + log.ToString().TrimEnd());
            return data;
        }

        private static void Put<T>(Dictionary<string, T> into, string name, T entry, string mod, StringBuilder log)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (into.ContainsKey(name)) log.AppendLine($"  {name}: replaced by {mod}");
            into[name] = entry;
        }

        //A site's program, with its corporation's blueprints by tier
        public static SiteProgram Program(SiteKind kind, string corp)
        {
            SiteData data = Get();
            SiteProgram program = data.Program(kind, corp);
            FillBlueprints(program, data.Corp(corp));
            return program;
        }

        //Blueprints a corporation's vaults can hold: those it names, and everything in its recipe categories, that can
        //be fabricated and aren't known from the start. Only a corporation with bModTech gives the mod's own (Exotic
        //tier, or making an Fz item). Its rare blueprints are kept apart, for vaults to roll for one by one.
        private static void FillBlueprints(SiteProgram program, JsonSiteCorp corp)
        {
            if (corp == null) return;
            var named = new HashSet<string>(corp.aBlueprints ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var rare = new HashSet<string>((corp.aRareBlueprints ?? new JsonSiteRare[0]).Where(r => r?.strBlueprint != null).Select(r => r.strBlueprint), StringComparer.OrdinalIgnoreCase);
            string[] categories = corp.aBlueprintCategories ?? new string[0];
            var specialised = new SortedSet<string>(StringComparer.Ordinal);
            var ordinary = new SortedSet<string>(StringComparer.Ordinal);
            foreach (JsonFabRecipe recipe in FabData.Recipes.Values)
            {
                if (!recipe.bFabricate || string.IsNullOrEmpty(recipe.strBlueprint) || FabData.IsStartKnown(recipe.strBlueprint)) continue;
                if (rare.Contains(recipe.strBlueprint)) continue;
                bool wanted = named.Contains(recipe.strBlueprint) || categories.Any(c => InCategory(recipe.strCategory, c));
                if (!wanted) continue;
                if (!corp.bModTech && IsModTech(recipe)) continue;
                if (recipe.strTier == FabTier.Ordinary) ordinary.Add(recipe.strBlueprint);
                else specialised.Add(recipe.strBlueprint);
            }
            ordinary.ExceptWith(specialised);
            program.SpecialisedBlueprints.AddRange(specialised);
            program.OrdinaryBlueprints.AddRange(ordinary);
            foreach (JsonSiteRare entry in corp.aRareBlueprints ?? new JsonSiteRare[0])
            {
                if (entry?.strBlueprint == null || !FabData.IsBlueprint(entry.strBlueprint)) continue;
                if (!corp.bModTech && FabData.GetBlueprintRecipes(entry.strBlueprint).Any(IsModTech)) continue;
                program.RareBlueprints.Add((entry.strBlueprint, entry.fChance));
            }
        }

        public static bool InCategory(string category, string root)
        {
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(root)) return false;
            return string.Equals(category, root, StringComparison.OrdinalIgnoreCase) || category.StartsWith(root + ".", StringComparison.OrdinalIgnoreCase);
        }

        //The mod's own technology, which only Faure-Zhang's sites give
        public static bool IsModTech(JsonFabRecipe recipe)
        {
            return recipe.strTier == FabTier.Exotic
                || (recipe.strCOFabricate != null && recipe.strCOFabricate.StartsWith("ItmFz"))
                || InCategory(recipe.strCategory, "Fabrication");
        }
    }
}
