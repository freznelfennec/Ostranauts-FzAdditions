using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    //Every mod's fabrication recipes and materials, from the data/fzfabrecipes folders of all loaded mods. Mods are
    //read in the game's own load order, core first, and an entry with the same strName as an earlier one replaces it,
    //the same as the game's own data. The order comes from DataHandler.aModPaths, so it doesn't matter whether this
    //mod's patches were in place when the other mods loaded.
    //
    //This is data, not game state, so it lasts through loads and is only read again on request (fabreload).
    public static class FabData
    {
        public const string Folder = "fzfabrecipes/";
        private const string RecipesFolder = Folder + "recipes/";
        private const string MaterialsFolder = Folder + "materials/";

        private static Dictionary<string, JsonFabRecipe> recipes = new Dictionary<string, JsonFabRecipe>();
        private static Dictionary<string, JsonFabMaterial> materials = new Dictionary<string, JsonFabMaterial>();
        private static Dictionary<string, JsonFabRecipe> recipeByCO = new Dictionary<string, JsonFabRecipe>();
        private static Dictionary<string, List<JsonFabRecipe>> recipesByBlueprint = new Dictionary<string, List<JsonFabRecipe>>();
        private static FabCategory categories = FabCategory.Build(Enumerable.Empty<JsonFabRecipe>());
        private static readonly object loadLock = new object();
        private static bool loaded;

        public static IReadOnlyDictionary<string, JsonFabRecipe> Recipes { get { EnsureLoaded(); return recipes; } }
        public static IReadOnlyDictionary<string, JsonFabMaterial> Materials { get { EnsureLoaded(); return materials; } }
        public static IEnumerable<string> Blueprints { get { EnsureLoaded(); return recipesByBlueprint.Keys; } }
        //Every fabricable recipe by category, built once as the data loads
        public static FabCategory Categories { get { EnsureLoaded(); return categories; } }

        //Null when no recipe covers the CO
        public static JsonFabRecipe GetRecipeForCO(string coName)
        {
            EnsureLoaded();
            return coName != null && recipeByCO.TryGetValue(coName, out JsonFabRecipe recipe) ? recipe : null;
        }

        public static JsonFabRecipe GetRecipe(string name)
        {
            EnsureLoaded();
            return name != null && recipes.TryGetValue(name, out JsonFabRecipe recipe) ? recipe : null;
        }

        //The fabricable recipes a blueprint unlocks, empty for an unknown one
        public static IReadOnlyList<JsonFabRecipe> GetBlueprintRecipes(string blueprint)
        {
            EnsureLoaded();
            return blueprint != null && recipesByBlueprint.TryGetValue(blueprint, out List<JsonFabRecipe> list) ? list : (IReadOnlyList<JsonFabRecipe>)Array.Empty<JsonFabRecipe>();
        }

        public static bool IsBlueprint(string blueprint)
        {
            EnsureLoaded();
            return blueprint != null && recipesByBlueprint.ContainsKey(blueprint);
        }

        //Known in every game without being learned
        public static bool IsStartKnown(string blueprint)
        {
            return GetBlueprintRecipes(blueprint).Any(r => r.bStartKnown);
        }

        //Loading needs the game's own data in place, to check the COs recipes name
        public static void EnsureLoaded()
        {
            if (loaded) return;
            Load();
        }

        public static void Load()
        {
            lock (loadLock)
            {
                try
                {
                    LoadAll();
                }
                catch (Exception ex)
                {
                    FzAdditions.Logger.LogError("Fabrication data failed to load: " + ex);
                }
                loaded = true;
            }
        }

        private static void LoadAll()
        {
            var newMaterials = new Dictionary<string, JsonFabMaterial>();
            var newRecipes = new Dictionary<string, JsonFabRecipe>();
            var log = new StringBuilder();
            int order = 0;

            foreach (string modPath in GetModPathsInLoadOrder())
            {
                string mod = GetModName(modPath);
                foreach (string file in GetFiles(modPath + "data/" + MaterialsFolder))
                {
                    foreach (JsonFabMaterial material in ReadFile<JsonFabMaterial>(file))
                    {
                        material.SourceMod = mod;
                        newMaterials[material.strName] = material;
                    }
                }
                foreach (string file in GetFiles(modPath + "data/" + RecipesFolder))
                {
                    foreach (JsonFabRecipe recipe in ReadFile<JsonFabRecipe>(file))
                    {
                        recipe.SourceMod = mod;
                        recipe.LoadOrder = order++;
                        if (newRecipes.ContainsKey(recipe.strName)) log.AppendLine($"  {recipe.strName}: replaced by {mod}");
                        newRecipes[recipe.strName] = recipe;
                    }
                }
            }

            foreach (JsonFabMaterial material in newMaterials.Values) CheckMaterial(material, log);

            int skipped = 0;
            foreach (JsonFabRecipe recipe in newRecipes.Values.ToList())
            {
                if (CheckRecipe(recipe, newMaterials, log)) continue;
                newRecipes.Remove(recipe.strName);
                skipped++;
            }

            //Later recipes in load order win a CO two recipes both claim
            var newByCO = new Dictionary<string, JsonFabRecipe>();
            foreach (JsonFabRecipe recipe in newRecipes.Values.OrderBy(r => r.LoadOrder))
            {
                foreach (string co in recipe.aCOs)
                {
                    if (newByCO.TryGetValue(co, out JsonFabRecipe other)) log.AppendLine($"  {co}: in {other.strName} and {recipe.strName}, {recipe.strName} kept");
                    newByCO[co] = recipe;
                }
            }

            var newByBlueprint = newRecipes.Values.Where(r => r.bFabricate).GroupBy(r => r.strBlueprint)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.strName).ToList());

            recipes = newRecipes;
            materials = newMaterials;
            recipeByCO = newByCO;
            recipesByBlueprint = newByBlueprint;
            categories = FabCategory.Build(newRecipes.Values);

            int mods = newRecipes.Values.Select(r => r.SourceMod).Concat(newMaterials.Values.Select(m => m.SourceMod)).Distinct().Count();
            FzAdditions.Logger.LogInfo($"Fabrication data: {materials.Count} materials, {recipes.Count} recipes ({recipes.Values.Count(r => r.bFabricate)} fabricable, {recipesByBlueprint.Count} blueprints) from {mods} mod(s), {skipped} skipped");
            if (log.Length > 0) FzAdditions.Logger.LogInfo("Fabrication data notes:\n" + log.ToString().TrimEnd());
        }

        //aModPaths has each mod put at its front as it loads, so the load order is the list reversed
        private static IEnumerable<string> GetModPathsInLoadOrder()
        {
            if (DataHandler.aModPaths == null) return Enumerable.Empty<string>();
            return DataHandler.aModPaths.AsEnumerable().Reverse().Select(p => p.EndsWith("/") || p.EndsWith("\\") ? p : p + "/").ToList();
        }

        private static string GetModName(string modPath)
        {
            string trimmed = modPath.TrimEnd('/', '\\');
            return trimmed == DataHandler.strAssetPath.TrimEnd('/', '\\') ? "core" : Path.GetFileName(trimmed);
        }

        //Every JSON file under the folder, skipping any the game's ignore patterns name, the same as its own data
        private static IEnumerable<string> GetFiles(string folder)
        {
            if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
            string[] ignore = DataHandler.dictModList != null && DataHandler.dictModList.TryGetValue("Mod Loading Order", out JsonModList list) ? list.aIgnorePatterns : null;
            return Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                .Where(f => ignore == null || !ignore.Any(pattern => !string.IsNullOrEmpty(pattern) && DataHandler.PathSanitize(f).IndexOf(pattern) >= 0));
        }

        //Read with the game's own reader, which keys entries by strName and logs a file it can't read
        private static IEnumerable<T> ReadFile<T>(string file)
        {
            var entries = new Dictionary<string, T>();
            DataHandler.JsonToData(file, entries);
            return entries.Values;
        }

        private static void CheckMaterial(JsonFabMaterial material, StringBuilder log)
        {
            if (string.IsNullOrEmpty(material.strSiloCond)) material.strSiloCond = FabSilo.DefaultCond(material.strName);
            if (DataHandler.dictConds != null && !DataHandler.dictConds.ContainsKey(material.strSiloCond))
            {
                log.AppendLine($"  material {material.strName}: silo condition {material.strSiloCond} isn't defined, no silo can hold it until one is");
            }
            string contents = Silo.ContentsCond(material.strName);
            if (DataHandler.dictConds != null && !DataHandler.dictConds.ContainsKey(contents))
            {
                log.AppendLine($"  material {material.strName}: contents condition {contents} isn't defined, silos can't hold it until it is (define it with bPersists true)");
            }
            if (!UnityEngine.ColorUtility.TryParseHtmlString(material.strColor, out _))
            {
                log.AppendLine($"  material {material.strName}: colour {material.strColor} isn't a hex colour, using {FabSilo.DefaultColor}");
                material.strColor = FabSilo.DefaultColor;
            }
            if (material.fRecycleTime <= 0f || material.fFabricateTime <= 0f)
            {
                log.AppendLine($"  material {material.strName}: times must be over 0, using {FabSilo.DefaultUnitTime} for any that aren't");
                if (material.fRecycleTime <= 0f) material.fRecycleTime = FabSilo.DefaultUnitTime;
                if (material.fFabricateTime <= 0f) material.fFabricateTime = FabSilo.DefaultUnitTime;
            }
            if (material.fRecycleEnergy < 0f || material.fFabricateEnergy < 0f)
            {
                log.AppendLine($"  material {material.strName}: energy can't be negative, using 0 for any that are");
                material.fRecycleEnergy = Math.Max(0f, material.fRecycleEnergy);
                material.fFabricateEnergy = Math.Max(0f, material.fFabricateEnergy);
            }
            if (string.IsNullOrEmpty(material.strNameFriendly)) material.strNameFriendly = material.strName;
        }

        //The material's colour, white for one it doesn't know
        public static UnityEngine.Color GetColor(string material)
        {
            return Materials.TryGetValue(material ?? "", out JsonFabMaterial found) ? SpriteUtil.ColorFromHex(found.strColor) : UnityEngine.Color.white;
        }

        //False skips the recipe. Missing COs are dropped, so one recipe pack can cover mods that may not be installed.
        private static bool CheckRecipe(JsonFabRecipe recipe, Dictionary<string, JsonFabMaterial> knownMaterials, StringBuilder log)
        {
            string name = recipe.strName;
            if (recipe.mapMaterials == null || recipe.mapMaterials.Count == 0)
            {
                log.AppendLine($"  {name}: skipped, no materials");
                return false;
            }
            string unknown = recipe.mapMaterials.Keys.FirstOrDefault(m => !knownMaterials.ContainsKey(m));
            if (unknown != null)
            {
                log.AppendLine($"  {name}: skipped, unknown material {unknown}");
                return false;
            }
            if (recipe.mapMaterials.Values.Any(units => units < 0))
            {
                log.AppendLine($"  {name}: skipped, negative material");
                return false;
            }

            var present = (recipe.aCOs ?? Array.Empty<string>()).Where(COExists).Distinct().ToArray();
            if (present.Length == 0)
            {
                log.AppendLine($"  {name}: skipped, none of its COs exist");
                return false;
            }
            recipe.aCOs = present;

            if (recipe.bFabricate)
            {
                if (string.IsNullOrEmpty(recipe.strBlueprint) || !COExists(recipe.strCOFabricate))
                {
                    log.AppendLine($"  {name}: can't be fabricated, it needs a blueprint and a CO to spawn that exists");
                    recipe.bFabricate = false;
                }
            }
            if (recipe.fRecycleYield < 0f || recipe.fRecycleYield > 1f)
            {
                log.AppendLine($"  {name}: recycle yield {recipe.fRecycleYield} kept between 0 and 1");
                recipe.fRecycleYield = Math.Max(0f, Math.Min(1f, recipe.fRecycleYield));
            }
            if (!FabTier.All.Contains(recipe.strTier))
            {
                log.AppendLine($"  {name}: unknown tier {recipe.strTier}, using {FabTier.Ordinary}");
                recipe.strTier = FabTier.Ordinary;
            }
            if (recipe.fTimeScale <= 0f) recipe.fTimeScale = 1f;
            if (string.IsNullOrEmpty(recipe.strNameFriendly)) recipe.strNameFriendly = name;
            if (string.IsNullOrEmpty(recipe.strCategory)) recipe.strCategory = "Misc";
            return true;
        }

        private static bool COExists(string co)
        {
            return !string.IsNullOrEmpty(co) && (DataHandler.dictCOs.ContainsKey(co) || DataHandler.dictCOOverlays.ContainsKey(co));
        }
    }
}
