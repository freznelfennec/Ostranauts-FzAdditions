using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    //The blueprints the player has learned. It's a single-player game, so they belong to the playthrough rather than
    //a character or ship. They're saved as one entry in the game save's custom infos (FabricationSavePatch), and this
    //set is the live copy between saves, emptied whenever a game is left. Blueprints a recipe marks bStartKnown are
    //known in every game without being learned.
    public static class Blueprints
    {
        public const string SaveKey = "FzBlueprints"; //Custom info holding every learned blueprint
        public const string AddKey = "FzBlueprintAdd"; //Custom info an interaction carries to teach one
        public const string ProgressKey = "FzBlueprintProgress"; //Custom info holding reverse engineering in progress
        private const char Separator = ';'; //The game splits custom infos on '=', so the list can't use that

        //Kept even when no loaded recipe uses them, so a blueprint survives a mod being removed and put back
        private static readonly HashSet<string> learned = new HashSet<string>();

        //Reverse engineering in progress: blueprint to how many undamaged ones have been recycled. Global state like
        //the learned set, saved beside it, so it carries across recyclers and ships.
        private static readonly Dictionary<string, int> progress = new Dictionary<string, int>();

        public static IEnumerable<string> Learned => learned;

        public static bool IsKnown(string blueprint)
        {
            return blueprint != null && (learned.Contains(blueprint) || FabData.IsStartKnown(blueprint));
        }

        //Every blueprint the loaded recipes have that the player can use
        public static IEnumerable<string> Known => FabData.Blueprints.Where(IsKnown);

        //False if it was already known or no recipe has it
        public static bool Learn(string blueprint)
        {
            if (!FabData.IsBlueprint(blueprint) || IsKnown(blueprint)) return false;
            learned.Add(blueprint);
            FzAdditions.Logger.LogInfo("Blueprint learned: " + blueprint);
            return true;
        }

        public static bool Forget(string blueprint)
        {
            return blueprint != null && learned.Remove(blueprint);
        }

        //A game being left, its blueprints go with it
        public static void Reset()
        {
            learned.Clear();
            progress.Clear();
        }

        //Reverse engineering in progress, for blueprints still unknown, most nearly done first
        public static List<(string blueprint, int count, int needed)> InProgress()
        {
            var list = new List<(string blueprint, int count, int needed)>();
            foreach (var pair in progress)
            {
                int needed = Needed(pair.Key);
                if (needed > 0 && !IsKnown(pair.Key)) list.Add((pair.Key, pair.Value, needed));
            }
            return list.OrderByDescending(p => (double)p.count / p.needed).ThenBy(p => p.blueprint, StringComparer.Ordinal).ToList();
        }

        //Recycles needed to learn a blueprint, 0 if it can't be reverse engineered: the most any of its recipes asks
        public static int Needed(string blueprint)
        {
            var recipes = FabData.GetBlueprintRecipes(blueprint);
            return recipes == null || recipes.Count == 0 ? 0 : recipes.Max(r => r.strTier == FabTier.Ordinary ? r.nReverseEngineer : 0);
        }

        //Counts an item recycled whole towards reverse engineering its blueprint. Per the user, damaged variants never
        //count: their own recipes have no blueprint, and an item marked damaged is passed over too. A stack counts
        //each unit. Returns the blueprint if this finished it, so the player can be told.
        public static string Recycled(CondOwner item)
        {
            if (item == null || item.HasCond("IsDamaged")) return null;
            JsonFabRecipe recipe = FabData.GetRecipeForCO(item.strCODef);
            if (recipe == null || !recipe.bFabricate || recipe.strTier != FabTier.Ordinary || recipe.nReverseEngineer <= 0) return null;
            string blueprint = recipe.strBlueprint;
            if (string.IsNullOrEmpty(blueprint) || IsKnown(blueprint)) return null;
            int count = (progress.TryGetValue(blueprint, out int had) ? had : 0) + Math.Max(1, item.StackCount);
            if (count < Needed(blueprint))
            {
                progress[blueprint] = count;
                return null;
            }
            progress.Remove(blueprint);
            Learn(blueprint);
            return blueprint;
        }

        public static string SaveProgress()
        {
            return string.Join(Separator.ToString(), progress.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value));
        }

        public static void LoadProgress(string value)
        {
            progress.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (string entry in value.Split(Separator))
            {
                int colon = entry.LastIndexOf(':');
                if (colon <= 0 || !int.TryParse(entry.Substring(colon + 1), out int count) || count <= 0) continue;
                progress[entry.Substring(0, colon).Trim()] = count;
            }
        }

        public static string Save()
        {
            return string.Join(Separator.ToString(), learned.OrderBy(b => b, StringComparer.Ordinal));
        }

        public static void Load(string value)
        {
            learned.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (string blueprint in value.Split(Separator).Select(b => b.Trim()).Where(b => b.Length > 0)) learned.Add(blueprint);
        }
    }
}
