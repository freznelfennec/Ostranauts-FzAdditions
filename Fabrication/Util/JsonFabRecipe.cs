using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    //One recipe from a mod's data/fz/fabrication/recipes folder, in the game's own JSON style. A recipe covers the COs
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
        //Ordinary only: how many undamaged ones have to be recycled to learn its blueprint by reverse engineering.
        //0 turns it off. Complicated technology takes more than simple things.
        public int nReverseEngineer { get; set; }
        public float fTimeScale { get; set; } = 1f; //On the run time worked out from its materials

        //Where it was last defined, set when loaded
        public string SourceMod { get; set; }
        public int LoadOrder { get; set; }

        public int TotalUnits => mapMaterials?.Values.Sum() ?? 0;
    }

    //One material from a mod's data/fz/fabrication/materials folder
    public class JsonFabMaterial
    {
        public string strName { get; set; } //Material ID that recipes refer to
        public string strNameFriendly { get; set; }
        public string strColor { get; set; } = FabSilo.DefaultColor; //Hex, shown on silo panels and matching its compacted item
        public float fUnitPrice { get; set; }
        public float fRecycleTime { get; set; } = FabSilo.DefaultUnitTime; //Game seconds to recycle one unit of it
        public float fFabricateTime { get; set; } = FabSilo.DefaultUnitTime; //Game seconds to fabricate with one unit of it
        public float fRecycleEnergy { get; set; } = FabSilo.DefaultUnitEnergy; //kWh to recycle one unit of it
        public float fFabricateEnergy { get; set; } = FabSilo.DefaultUnitEnergy; //kWh to fabricate with one unit of it
        public string strBlockCO { get; set; } //What it's compacted into
        public string strSiloCond { get; set; } //A silo can hold it only with this condition, IsFzSilo<strName> if not given

        public string SourceMod { get; set; }
    }

    public static class FabTier
    {
        public const string Ordinary = "Ordinary"; //Learned by recycling, blueprint items and quests
        public const string Specialised = "Specialised"; //Only from corporate sites
        public const string Exotic = "Exotic"; //The mod's own tech, from set sources

        public static readonly string[] All = { Ordinary, Specialised, Exotic };
    }

    //Each material has its own silo condition, and a silo holds every material whose condition it has. So a silo for
    //any mix of materials can be added with just data.
    public static class FabSilo
    {
        public const string CondPrefix = "IsFzSilo";
        public const string DefaultColor = "#FFFFFF";
        public const float DefaultUnitTime = 3f; //Game seconds a unit takes when a material doesn't say
        public const float DefaultUnitEnergy = 0.075f; //kWh a unit takes when a material doesn't say, on the scale CostModel uses

        public static string DefaultCond(string material) => CondPrefix + material;
    }
}
