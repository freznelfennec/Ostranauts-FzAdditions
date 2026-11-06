using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //A category of fabricable recipes, from their dotted strCategory paths, such as Walls.Testudo. The tree is built
    //once when the fabrication data loads (FabData.Categories), and the fabricator's panel shows only the parts of it
    //with known blueprints.
    public class FabCategory
    {
        public string Name { get; private set; } //This level's part of the path, empty for the root
        public string Path { get; private set; } //The whole dotted path, empty for the root
        public FabCategory Parent { get; private set; }
        public List<FabCategory> Children { get; } = new List<FabCategory>(); //By name
        public List<JsonFabRecipe> Recipes { get; } = new List<JsonFabRecipe>(); //Directly in it, by name
        public HashSet<string> Blueprints { get; } = new HashSet<string>(StringComparer.Ordinal); //In it and below

        public bool IsRoot => Parent == null;

        public static FabCategory Build(IEnumerable<JsonFabRecipe> recipes)
        {
            var root = new FabCategory { Name = "", Path = "" };
            foreach (JsonFabRecipe recipe in recipes.Where(r => r.bFabricate))
            {
                FabCategory node = root;
                node.Blueprints.Add(recipe.strBlueprint);
                foreach (string part in (recipe.strCategory ?? "Misc").Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    FabCategory child = node.Children.FirstOrDefault(c => c.Name == part);
                    if (child == null)
                    {
                        child = new FabCategory { Name = part, Path = node.IsRoot ? part : node.Path + "." + part, Parent = node };
                        node.Children.Add(child);
                    }
                    node = child;
                    node.Blueprints.Add(recipe.strBlueprint);
                }
                node.Recipes.Add(recipe);
            }
            root.Sort();
            return root;
        }

        private void Sort()
        {
            Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Recipes.Sort((a, b) => string.Compare(a.strNameFriendly, b.strNameFriendly, StringComparison.OrdinalIgnoreCase));
            foreach (FabCategory child in Children) child.Sort();
        }

        //Whether a blueprint known now unlocks anything in it or below it
        public bool AnyKnown(Func<string, bool> known) => Blueprints.Any(known);

        //The category at a dotted path, or the deepest one of it that exists
        public FabCategory Find(string path)
        {
            FabCategory node = this;
            foreach (string part in (path ?? "").Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                FabCategory child = node.Children.FirstOrDefault(c => c.Name == part);
                if (child == null) break;
                node = child;
            }
            return node;
        }

        //From the root down to this one
        public List<FabCategory> Trail()
        {
            var trail = new List<FabCategory>();
            for (FabCategory node = this; node != null; node = node.Parent) trail.Insert(0, node);
            return trail;
        }
    }
}
