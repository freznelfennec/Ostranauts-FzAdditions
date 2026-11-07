using System.Text.Json;
using Freznel.FzAdditions.Util.SiteGen;

//Draws every kind and size of mission site for each seed given, furnished, the same as 'fz site preview' in the game,
//and prints each one's summary and anything wrong with it. Furnishing follows the game's own item rules, read from its
//data folder. Run from this folder:
//  dotnet run -- <output folder> <seed> [seed...]
//The game's data folder is found in the default Steam location, or set FZ_GAME_DATA to it.
if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run -- <output folder> <seed> [seed...]");
    return 1;
}

string dataFolder = Environment.GetEnvironmentVariable("FZ_GAME_DATA")
    ?? @"C:\Program Files (x86)\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data";
SiteCatalog catalog = Directory.Exists(dataFolder) ? GameData.Load(dataFolder) : null;
if (catalog == null) Console.WriteLine($"No game data at {dataFolder}, so sites aren't furnished.");
else Console.WriteLine($"Catalog: {catalog.Items.Count} items, {catalog.Tags.Count} socket tags.");

//  dotnet run -- debug <kind> <size> <seed> <x> <y> prints the zones and placed items round a tile
if (args[0] == "debug")
{
    SiteShapes.TryParseKind(args[1], out SiteKind dk);
    SiteShapes.TryParseSize(args[2], out SiteSize ds);
    int dseed = int.Parse(args[3]), cx = int.Parse(args[4]), cy = int.Parse(args[5]);
    SiteGrid dg = SiteShapes.Generate(dk, ds, dseed);
    SiteLayout dl = SiteShell.Build(dg, SiteStyle.For(dk), catalog);
    for (int y = cy - 5; y <= cy + 5; y++)
    {
        Console.WriteLine(string.Join(" ", Enumerable.Range(cx - 5, 11).Select(x => (x == cx && y == cy ? "*" : "") + dg[x, y].ToString().Substring(0, 3))));
    }
    foreach (var p in dl.Placed.Where(p => p.Footprint().Any(t => Math.Abs(t.x - cx) <= 3 && Math.Abs(t.y - cy) <= 3)))
    {
        Console.WriteLine($"{p.Shape.Name} rot {p.Rotation} at {string.Join(" ", p.Footprint())} use {p.Point("use")}");
    }
    foreach (var d in dg.Doors.Where(d => Math.Abs(d.X - cx) <= 6 && Math.Abs(d.Y - cy) <= 6)) Console.WriteLine($"door {d.X},{d.Y} northsouth {d.PassageNorthSouth}");
    return 0;
}

string folder = args[0];
var shortfalls = new Dictionary<string, (int missing, int sites)>();
foreach (int seed in args.Skip(1).Select(int.Parse))
{
    foreach (SiteKind kind in Enum.GetValues<SiteKind>())
    {
        foreach (SiteSize size in Enum.GetValues<SiteSize>())
        {
            SiteGrid grid = SiteShapes.Generate(kind, size, seed);
            if (grid == null)
            {
                Console.WriteLine($"{kind} {size}, seed {seed}: FAILED, {BlackSiteShape.LastFailure}");
                foreach (string reason in BlackSiteShape.Failures.Distinct()) Console.WriteLine("    " + reason + " x" + BlackSiteShape.Failures.Count(f => f == reason));
                continue;
            }
            Console.WriteLine(grid.Summary());

            //The shell as items, furnished, and anything wrong with it
            SiteLayout layout = SiteShell.Build(grid, SiteStyle.For(kind), catalog);
            SitePreview.Write(grid, Path.Combine(folder, $"{kind}_{size}_{seed}.png".ToLower()), layout);
            Console.WriteLine($"  {layout.Items.Count} items ({layout.Furnished} furnishing), {layout.DockIds.Count} docking ports, template {layout.TemplateName}");
            foreach (string problem in SiteShell.Check(layout)) Console.WriteLine("  PROBLEM: " + problem);
            foreach (var (item, missing) in layout.Shortfalls) shortfalls[$"{kind} {item}"] = (shortfalls.TryGetValue($"{kind} {item}", out var s) ? s.missing : 0) + missing is int m ? (m, (shortfalls.TryGetValue($"{kind} {item}", out var s2) ? s2.sites : 0) + 1) : default;
        }
    }
}
foreach (var (item, (missing, sites)) in shortfalls.OrderByDescending(s => s.Value.missing)) Console.WriteLine($"Short: {item} missed {missing} times in {sites} sites");
return 0;

//Reads item shapes and socket tags from the game's JSON, as SiteCatalogLoader does from DataHandler in the game
static class GameData
{
    public static SiteCatalog Load(string data)
    {
        var catalog = new SiteCatalog();
        var defs = new Dictionary<string, JsonElement>();
        var cos = new Dictionary<string, JsonElement>();
        var overlays = new Dictionary<string, string>();

        foreach (JsonElement loot in Objects(Path.Combine(data, "loot")))
        {
            if (!loot.TryGetProperty("strName", out var name) || !loot.TryGetProperty("aCOs", out var list) || list.ValueKind != JsonValueKind.Array) continue;
            var conds = list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString().Split('=')[0]).ToArray();
            if (conds.Length > 0) catalog.Tags[name.GetString()] = conds;
        }
        foreach (JsonElement def in Objects(Path.Combine(data, "items")))
        {
            if (def.TryGetProperty("strName", out var name)) defs[name.GetString()] = def;
        }
        foreach (JsonElement co in Objects(Path.Combine(data, "condowners")))
        {
            if (co.TryGetProperty("strName", out var name)) cos[name.GetString()] = co;
        }
        foreach (JsonElement overlay in Objects(Path.Combine(data, "cooverlays")))
        {
            if (overlay.TryGetProperty("strName", out var name) && overlay.TryGetProperty("strCOBase", out var co) && co.ValueKind == JsonValueKind.String)
            {
                overlays[name.GetString()] = co.GetString();
            }
        }

        foreach (var (name, co) in cos) Add(catalog, name, co, defs);
        foreach (var (name, baseName) in overlays)
        {
            if (cos.TryGetValue(baseName, out var co)) Add(catalog, name, co, defs);
        }
        return catalog;
    }

    private static void Add(SiteCatalog catalog, string name, JsonElement co, Dictionary<string, JsonElement> defs)
    {
        if (!co.TryGetProperty("strItemDef", out var defName) || defName.ValueKind != JsonValueKind.String) return;
        if (!defs.TryGetValue(defName.GetString(), out var def)) return;
        int cols = def.TryGetProperty("nCols", out var c) && c.TryGetInt32(out int n) ? n : 0;
        string[] adds = Strings(def, "aSocketAdds");
        if (cols <= 0 || adds.Length == 0) return;
        var shape = new ItemShape { Name = name, W = cols, H = Math.Max(1, adds.Length / cols), Adds = adds, Reqs = Strings(def, "aSocketReqs"), Forbids = Strings(def, "aSocketForbids") };
        SiteCatalog.ParsePoints(shape, Strings(co, "mapPoints"));
        catalog.Items[name] = shape;
    }

    private static string[] Strings(JsonElement e, string property)
    {
        if (!e.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) return new string[0];
        return list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToArray();
    }

    private static IEnumerable<JsonElement> Objects(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(File.ReadAllText(file), options);
            }
            catch
            {
                continue;
            }
            if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object) yield return item;
            }
        }
    }
}
