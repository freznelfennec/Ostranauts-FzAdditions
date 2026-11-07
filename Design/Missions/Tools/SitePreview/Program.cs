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
//The mod's own data, from this repository: its items join the catalog, and fz/sites gives the site programs
string modFolder = Environment.GetEnvironmentVariable("FZ_MOD_DATA") ?? Path.GetFullPath(Path.Combine("..", "..", "..", "..", "DataMods", "FzAdditions", "data"));
SiteCatalog catalog = Directory.Exists(dataFolder) ? GameData.Load(dataFolder, Directory.Exists(modFolder) ? modFolder : null) : null;
if (catalog == null) Console.WriteLine($"No game data at {dataFolder}, so sites aren't furnished.");
else Console.WriteLine($"Catalog: {catalog.Items.Count} items, {catalog.Tags.Count} socket tags.");
SiteData siteData = Directory.Exists(modFolder) ? GameData.LoadSites(Path.Combine(modFolder, "fz", "sites")) : new SiteData();
Console.WriteLine($"Site data: {siteData.Types.Count} site types, {siteData.Corps.Count} corporations, {siteData.Styles.Count} styles, from {modFolder}");
//  --corp=<name> builds the sites as that corporation's (Generic by default)
string corpArg = args.FirstOrDefault(a => a.StartsWith("--corp="))?.Substring(7) ?? SiteData.GenericCorp;
args = args.Where(a => !a.StartsWith("--")).ToArray();

//  dotnet run -- catalog <regex> lists the catalog's items whose names match, with their size and points
if (args[0] == "catalog")
{
    var match = new System.Text.RegularExpressions.Regex(args[1]);
    foreach (var shape in catalog.Items.Values.Where(s => match.IsMatch(s.Name)).OrderBy(s => s.Name))
    {
        Console.WriteLine($"{shape.Name} {shape.W}x{shape.H} {string.Join(" ", shape.Points.Keys)}");
    }
    return 0;
}

//  dotnet run -- hull <kind> <size> <seed> prints why weapons and sensors were turned down round the first mount
if (args[0] == "hull")
{
    SiteShapes.TryParseKind(args[1], out SiteKind hk);
    SiteShapes.TryParseSize(args[2], out SiteSize hs);
    SiteGrid hg = SiteShapes.Generate(hk, hs, int.Parse(args[3]));
    SiteMount first = hg.Mounts[0];
    Console.WriteLine($"first mount {first.Kind} at {first.X},{first.Y} facing {first.Facing}");
    for (int y = first.Y - 4; y <= first.Y + 4; y++) Console.WriteLine(string.Join(" ", Enumerable.Range(first.X - 4, 9).Select(x => (x == first.X && y == first.Y ? "*" : "") + hg[x, y].ToString().Substring(0, 3))));
    var reasons = new List<string>();
    SiteFurnish.HullTrace = reasons.Add;
    SiteShell.Build(hg, siteData.Program(hk, corpArg), catalog);
    if (args.Length > 4) foreach (string r in reasons.Where(r => r.Contains(args[4]))) Console.WriteLine(r);
    foreach (var group in reasons.Take(4000).GroupBy(r => r.Substring(r.IndexOf(':') + 2)).OrderByDescending(g => g.Count()).Take(12)) Console.WriteLine($"{group.Count()}x {group.Key}  e.g. {group.First()}");
    return 0;
}

//  dotnet run -- debug <kind> <size> <seed> <x> <y> prints the zones and placed items round a tile
if (args[0] == "debug")
{
    SiteShapes.TryParseKind(args[1], out SiteKind dk);
    SiteShapes.TryParseSize(args[2], out SiteSize ds);
    int dseed = int.Parse(args[3]), cx = int.Parse(args[4]), cy = int.Parse(args[5]);
    SiteGrid dg = SiteShapes.Generate(dk, ds, dseed);
    SiteLayout dl = SiteShell.Build(dg, siteData.Program(dk, corpArg), catalog);
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
var missingItems = new SortedSet<string>();
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
            SiteLayout layout = SiteShell.Build(grid, siteData.Program(kind, corpArg), catalog);
            SitePreview.Write(grid, Path.Combine(folder, $"{kind}_{size}_{seed}_{corpArg}.png".ToLower()), layout);
            Console.WriteLine($"  {layout.Items.Count} items ({layout.Furnished} furnishing), {layout.DockIds.Count} docking ports, template {layout.TemplateName}");
            Console.WriteLine($"  {layout.Weapons} of {grid.Mounts.Count} weapon mounts armed, {layout.Rounds} rounds, {layout.Sensors} sensors, {layout.LootSpawners} looted containers, {layout.FloorLoot} floor loot, {layout.Controllers} lockout controller");
            foreach (string missing in layout.MissingItems) missingItems.Add(missing);
            foreach (string problem in SiteShell.Check(layout)) Console.WriteLine("  PROBLEM: " + problem);
            foreach (var (item, missing) in layout.Shortfalls) shortfalls[$"{kind} {item}"] = (shortfalls.TryGetValue($"{kind} {item}", out var s) ? s.missing : 0) + missing is int m ? (m, (shortfalls.TryGetValue($"{kind} {item}", out var s2) ? s2.sites : 0) + 1) : default;
        }
    }
}
foreach (var (item, (missing, sites)) in shortfalls.OrderByDescending(s => s.Value.missing)) Console.WriteLine($"Short: {item} missed {missing} times in {sites} sites");
if (missingItems.Count > 0) Console.WriteLine("Not in the catalog: " + string.Join(", ", missingItems));
return 0;

//Reads item shapes and socket tags from the game's JSON, as SiteCatalogLoader does from DataHandler in the game
static class GameData
{
    //The game's data, then the mod's over it, as the game loads them
    public static SiteCatalog Load(string data, string mod = null)
    {
        var catalog = new SiteCatalog();
        var defs = new Dictionary<string, JsonElement>();
        var cos = new Dictionary<string, JsonElement>();
        var overlays = new Dictionary<string, string>();

        foreach (string folder in new[] { data, mod }.Where(f => f != null))
        {
            foreach (JsonElement loot in Objects(Path.Combine(folder, "loot")))
            {
                if (!loot.TryGetProperty("strName", out var name) || !loot.TryGetProperty("aCOs", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                var conds = list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString().Split('=')[0]).ToArray();
                if (conds.Length > 0) catalog.Tags[name.GetString()] = conds;
            }
            foreach (JsonElement def in Objects(Path.Combine(folder, "items")))
            {
                if (def.TryGetProperty("strName", out var name)) defs[name.GetString()] = def;
            }
            foreach (JsonElement co in Objects(Path.Combine(folder, "condowners")))
            {
                if (co.TryGetProperty("strName", out var name)) cos[name.GetString()] = co;
            }
            foreach (JsonElement overlay in Objects(Path.Combine(folder, "cooverlays")))
            {
                if (overlay.TryGetProperty("strName", out var name) && overlay.TryGetProperty("strCOBase", out var co) && co.ValueKind == JsonValueKind.String)
                {
                    overlays[name.GetString()] = co.GetString();
                }
            }
        }

        foreach (var (name, co) in cos) Add(catalog, name, co, defs);
        foreach (var (name, baseName) in overlays)
        {
            if (cos.TryGetValue(baseName, out var co)) Add(catalog, name, co, defs);
        }
        return catalog;
    }

    //fz/sites, read the way SiteDataLoader reads it in the game
    public static SiteData LoadSites(string folder)
    {
        var data = new SiteData();
        var options = new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
        void Read<T>(string sub, Dictionary<string, T> into, Func<T, string> name)
        {
            string path = Path.Combine(folder, sub);
            if (!Directory.Exists(path)) return;
            foreach (string file in Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (T entry in JsonSerializer.Deserialize<T[]>(File.ReadAllText(file), options) ?? new T[0])
                {
                    if (entry != null && name(entry) != null) into[name(entry)] = entry;
                }
            }
        }
        Read<JsonSiteType>("sitetypes", data.Types, t => t.strName);
        Read<JsonSiteCorp>("corps", data.Corps, c => c.strName);
        Read<JsonSiteStyle>("styles", data.Styles, s => s.strName);
        return data;
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
            catch (JsonException ex)
            {
                //The game reads some files this can't; say which, in case it's one of the mod's
                if (file.Contains("FzAdditions")) Console.WriteLine($"Couldn't read {file}: {ex.Message}");
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
