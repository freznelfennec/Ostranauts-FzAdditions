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
        private const char Separator = ';'; //The game splits custom infos on '=', so the list can't use that

        //Kept even when no loaded recipe uses them, so a blueprint survives a mod being removed and put back
        private static readonly HashSet<string> learned = new HashSet<string>();

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
