using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Patches
{
    //Fabrication's console commands, added to the ones ConsolePatch handles
    public static class FabConsoleCommands
    {
        private const int ListLimit = 40; //Lines a list prints before saying how many more there are

        public static void Register()
        {
            ConsolePatch.AddCommand("recipe", KeywordRecipe,
                "shows fabrication recipes",
                "'fz recipe' sums up the recipes loaded from every mod",
                "'fz recipe ItmHeater01' shows the recipe for a CO, recipe or blueprint",
                "'fz recipe find heater' lists recipes whose name, category or COs contain the text");
            ConsolePatch.AddCommand("bplearn", KeywordLearn,
                "teaches the player a fabrication blueprint",
                "'fz bplearn Heater01' learns one blueprint, 'fz bplearn all' learns every one");
            ConsolePatch.AddCommand("bpforget", KeywordForget,
                "forgets a learned fabrication blueprint",
                "'fz bpforget Heater01' forgets one blueprint, 'fz bpforget all' forgets every learned one");
            ConsolePatch.AddCommand("bplist", KeywordList,
                "lists the fabrication blueprints the player knows",
                "'fz bplist' lists them all, 'fz bplist wall' only those containing the text");
            ConsolePatch.AddCommand("silo", KeywordSilo,
                "shows and changes the material silos on the current ship or station",
                "'fz silo' lists the working silos and what each holds",
                "'fz silo give BaseMetals 500' fills the silos with that much, the one holding the most of it first",
                "'fz silo take BaseMetals 500' draws that much out, the one holding the least of it first",
                "'fz silo clear' empties every working silo");
            ConsolePatch.AddCommand("fabreload", KeywordReload,
                "reads every mod's fabrication recipes and materials again, for checking edits without restarting");
        }

        private static bool KeywordRecipe(ref string strInput, string[] args)
        {
            if (args.Length == 1)
            {
                var recipes = FabData.Recipes.Values.ToList();
                strInput += $"\n{FabData.Materials.Count} materials, {recipes.Count} recipes, {recipes.Count(r => r.bFabricate)} fabricable, {FabData.Blueprints.Count()} blueprints";
                foreach (var mod in recipes.GroupBy(r => r.SourceMod).OrderBy(g => g.Key)) strInput += $"\n  {mod.Key}: {mod.Count()} recipes";
                return true;
            }

            if (args.Length == 3 && args[1].ToLower() == "find")
            {
                string text = args[2].ToLower();
                var found = FabData.Recipes.Values.Where(r => r.strName.ToLower().Contains(text) || r.strNameFriendly.ToLower().Contains(text)
                    || r.strCategory.ToLower().Contains(text) || r.aCOs.Any(co => co.ToLower().Contains(text))).OrderBy(r => r.strCategory).ThenBy(r => r.strName).ToList();
                foreach (var recipe in found.Take(ListLimit)) strInput += $"\n{recipe.strName}: {recipe.strNameFriendly} [{recipe.strCategory}]";
                strInput += found.Count > ListLimit ? $"\n...and {found.Count - ListLimit} more" : $"\n{found.Count} found";
                return true;
            }

            string name = args[1];
            var matches = new List<JsonFabRecipe>();
            if (FabData.GetRecipe(name) is JsonFabRecipe byName) matches.Add(byName);
            if (FabData.GetRecipeForCO(name) is JsonFabRecipe byCO && !matches.Contains(byCO)) matches.Add(byCO);
            matches.AddRange(FabData.GetBlueprintRecipes(name).Where(r => !matches.Contains(r)));
            if (matches.Count == 0)
            {
                strInput += $"\nNo recipe, CO or blueprint named {name}.";
                return false;
            }
            foreach (var recipe in matches) strInput += Describe(recipe);
            return true;
        }

        private static string Describe(JsonFabRecipe recipe)
        {
            var text = new StringBuilder();
            text.Append($"\n{recipe.strName}: {recipe.strNameFriendly} [{recipe.strCategory}], from {recipe.SourceMod}");
            text.Append("\n  materials: " + string.Join(", ", recipe.mapMaterials.Where(m => m.Value > 0).Select(m => $"{FabData.Materials[m.Key].strNameFriendly} {m.Value}")) + $" ({recipe.TotalUnits} units)");
            text.Append(recipe.bFabricate
                ? $"\n  fabricate: {recipe.strCOFabricate}, blueprint {recipe.strBlueprint} ({recipe.strTier}, {(Blueprints.IsKnown(recipe.strBlueprint) ? "known" : "not known")})"
                : "\n  fabricate: no");
            text.Append(recipe.bRecycle ? $"\n  recycle: yes, {recipe.fRecycleYield:0.##} yield" : "\n  recycle: no");
            text.Append("\n  COs: " + string.Join(" ", recipe.aCOs));
            return text.ToString();
        }

        private static bool KeywordLearn(ref string strInput, string[] args)
        {
            if (args.Length != 2)
            {
                strInput += "\nUsage: fz bplearn <blueprint> or fz bplearn all";
                return false;
            }
            if (args[1].ToLower() == "all")
            {
                int count = FabData.Blueprints.ToList().Count(Blueprints.Learn);
                strInput += $"\nLearned {count} blueprint(s).";
                return true;
            }
            string blueprint = FindBlueprint(args[1]);
            if (blueprint == null)
            {
                strInput += $"\nNo blueprint named {args[1]}. 'fz recipe find <text>' can help find one.";
                return false;
            }
            strInput += Blueprints.Learn(blueprint) ? $"\nLearned {blueprint}." : $"\n{blueprint} is already known.";
            return true;
        }

        private static bool KeywordForget(ref string strInput, string[] args)
        {
            if (args.Length != 2)
            {
                strInput += "\nUsage: fz bpforget <blueprint> or fz bpforget all";
                return false;
            }
            if (args[1].ToLower() == "all")
            {
                int count = Blueprints.Learned.Count();
                Blueprints.Reset();
                strInput += $"\nForgot {count} blueprint(s). Blueprints known from the start stay known.";
                return true;
            }
            string blueprint = FindBlueprint(args[1]) ?? args[1];
            strInput += Blueprints.Forget(blueprint) ? $"\nForgot {blueprint}." : $"\n{blueprint} wasn't learned.";
            return true;
        }

        private static bool KeywordList(ref string strInput, string[] args)
        {
            string text = args.Length > 1 ? args[1].ToLower() : null;
            var known = Blueprints.Known.Where(b => text == null || b.ToLower().Contains(text)).OrderBy(b => b, StringComparer.Ordinal).ToList();
            foreach (string blueprint in known.Take(ListLimit))
            {
                var recipes = FabData.GetBlueprintRecipes(blueprint);
                strInput += $"\n{blueprint}: {recipes[0].strNameFriendly}{(recipes.Count > 1 ? $" (+{recipes.Count - 1} variants)" : "")}{(FabData.IsStartKnown(blueprint) ? ", known from the start" : "")}";
            }
            strInput += known.Count > ListLimit ? $"\n...and {known.Count - ListLimit} more" : $"\n{known.Count} known of {FabData.Blueprints.Count()}";
            return true;
        }

        private static bool KeywordSilo(ref string strInput, string[] args)
        {
            Ship ship = CrewSim.objInstance != null ? CrewSim.shipCurrentLoaded : null;
            if (ship == null)
            {
                strInput += "\nNo ship loaded.";
                return false;
            }
            string action = args.Length > 1 ? args[1].ToLower() : "list";

            if (action == "give" || action == "take")
            {
                string material = args.Length == 4 ? FabData.Materials.Keys.FirstOrDefault(m => string.Equals(m, args[2], StringComparison.OrdinalIgnoreCase)) : null;
                if (material == null || !double.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double units) || units <= 0)
                {
                    strInput += $"\nUsage: fz silo {action} <material> <units>. Materials: {string.Join(", ", FabData.Materials.Keys)}";
                    return false;
                }
                double moved = action == "give" ? Silo.Fill(ship, material, units) : Silo.Draw(ship, material, units);
                strInput += action == "give" ? $"\nPut {moved:0.##} of {units:0.##} units of {material} in." : $"\nTook {moved:0.##} of {units:0.##} units of {material} out.";
                strInput += $" The ship's silos hold {Silo.ShipAmount(ship, material):0.##}.";
                return true;
            }

            List<CondOwner> silos = Silo.GetWorking(ship);
            if (action == "clear")
            {
                foreach (CondOwner silo in silos) Silo.Clear(silo);
                strInput += $"\nEmptied {silos.Count} silo(s).";
                return true;
            }
            if (action != "list")
            {
                strInput += "\nUsage: fz silo, fz silo give <material> <units>, fz silo take <material> <units>, or fz silo clear";
                return false;
            }

            strInput += $"\n{silos.Count} working silo(s)";
            foreach (CondOwner silo in silos)
            {
                var contents = Silo.Contents(silo).ToList();
                strInput += $"\n  {silo.strName} ({silo.strCODef}): {Silo.Total(silo):0.##} of {Silo.Capacity(silo):0} units, {silo.GetCondAmount("StatMass"):0.##} kg, worth {silo.GetCondAmount("StatBasePrice"):0}";
                if (contents.Count > 0) strInput += "\n    " + string.Join(", ", contents.Select(c => $"{FabData.Materials[c.Key].strNameFriendly} {c.Value:0.##}"));
                var refused = FabData.Materials.Keys.Where(m => !Silo.Accepts(silo, m)).ToList();
                if (refused.Count > 0) strInput += "\n    doesn't take: " + string.Join(", ", refused);
            }
            return true;
        }

        private static bool KeywordReload(ref string strInput, string[] args)
        {
            FabData.Load();
            strInput += $"\nRead {FabData.Recipes.Count} recipes and {FabData.Materials.Count} materials. The BepInEx log has the details.";
            return true;
        }

        //Blueprint names are matched without minding case
        private static string FindBlueprint(string name)
        {
            return FabData.Blueprints.FirstOrDefault(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
