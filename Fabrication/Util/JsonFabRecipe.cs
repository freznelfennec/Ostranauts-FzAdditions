using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    //One recipe from a mod's data/fzfabrecipes/recipes folder, in the game's own JSON style. A recipe covers the COs
    //that share a cost, so an item's damaged form is a recipe of its own that can be recycled but not fabricated.
    public class JsonFabRecipe
    {
        public string strName { get; set; }
        public string strNameFriendly { get; set; }
        public string strCategory { get; set; } //Dotted path for grouping, for example Walls.Testudo
        public string[] aCOs { get; set; } //Every CO the recipe applies to
        public string strCOFabricate { get; set; } //The CO the fabricator spawns
        public Dictionary<string, int> mapMaterials { get; set; } //Material ID to units, missing ones are 0
        public bool bFabricate { get; set; }
        public bool bRecycle { get; set; }
        public float fRecycleYield { get; set; } = 0.5f; //Share of the materials recycling returns
        public string strBlueprint { get; set; } //Unlocks fabricating it, colour variants share one
        public string strTier { get; set; } = FabTier.Ordinary; //How its blueprint can be learned
        public bool bStartKnown { get; set; } //Its blueprint is known from the start of every game
        public float fTimeScale { get; set; } = 1f; //On the run time worked out from its materials

        //Where it was last defined, set when loaded
        public string SourceMod { get; set; }
        public int LoadOrder { get; set; }

        public int TotalUnits => mapMaterials?.Values.Sum() ?? 0;
    }

    //One material from a mod's data/fzfabrecipes/materials folder
    public class JsonFabMaterial
    {
        public string strName { get; set; } //Material ID that recipes refer to
        public string strNameFriendly { get; set; }
        public float fUnitPrice { get; set; }
        public string strBlockCO { get; set; } //What it's compacted into
        public string strSilo { get; set; } = FabSilo.Standard; //Which silos may hold it

        public string SourceMod { get; set; }
    }

    public static class FabTier
    {
        public const string Ordinary = "Ordinary"; //Learned by recycling, blueprint items and quests
        public const string Specialised = "Specialised"; //Only from corporate sites
        public const string Exotic = "Exotic"; //The mod's own tech, from set sources

        public static readonly string[] All = { Ordinary, Specialised, Exotic };
    }

    public static class FabSilo
    {
        public const string Standard = "Standard";
        public const string Exotic = "Exotic";
    }
}
