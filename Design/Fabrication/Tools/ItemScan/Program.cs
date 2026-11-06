using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Scans Ostranauts base data for item COs: mass, price, footprint, categories, dismantle and destruction output and
// where each is referenced. Usage: dotnet run -c Release -- <output folder>, which gets items.tsv. Reads the game's
// data from its default Steam install.
string data = @"C:\Program Files (x86)\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data";
string outDir = args.Length > 0 ? args[0] : ".";
var opts = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
var inv = CultureInfo.InvariantCulture;

IEnumerable<JsonElement> Objects(string folder)
{
    foreach (var f in Directory.GetFiles(Path.Combine(data, folder), "*.json", SearchOption.AllDirectories))
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(f), opts); }
        catch (Exception e) { Console.Error.WriteLine($"skip {f}: {e.Message}"); continue; }
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            foreach (var o in doc.RootElement.EnumerateArray()) if (o.ValueKind == JsonValueKind.Object) yield return o;
    }
}
string Str(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
string[] Arr(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array
    ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()).ToArray() : Array.Empty<string>();

// "Name=chance x count" or "Name=chance x min-max"
(string name, double chance, double count) ParseEntry(string s)
{
    int eq = s.IndexOf('=');
    if (eq < 0) return (s.Trim(), 1, 1);
    string name = s[..eq].Trim(); string rest = s[(eq + 1)..];
    string[] p = rest.Split('x');
    double chance = double.TryParse(p[0], NumberStyles.Float, inv, out var c) ? c : 1;
    double count = 1;
    if (p.Length > 1)
    {
        string[] r = p[1].Split('-');
        double a = double.TryParse(r[0], NumberStyles.Float, inv, out var x) ? x : 1;
        double b = r.Length > 1 && double.TryParse(r[1], NumberStyles.Float, inv, out var y) ? y : a;
        count = (a + b) / 2;
    }
    return (name, chance, count);
}

var cos = new Dictionary<string, JsonElement>();
foreach (var o in Objects("condowners")) { var n = Str(o, "strName"); if (n != null) cos[n] = o; }
var overlays = new Dictionary<string, JsonElement>();
foreach (var o in Objects("cooverlays")) { var n = Str(o, "strName"); if (n != null) overlays[n] = o; }
var items = new Dictionary<string, JsonElement>();
foreach (var o in Objects("items")) { var n = Str(o, "strName"); if (n != null) items[n] = o; }
var loots = new Dictionary<string, JsonElement>();
foreach (var o in Objects("loot")) { var n = Str(o, "strName"); if (n != null) loots[n] = o; }
var installs = Objects("installables").ToList();

// Expected items out of a loot table, nested tables expanded
Dictionary<string, double> Expand(string loot, int depth = 0)
{
    var res = new Dictionary<string, double>();
    if (depth > 6 || loot == null || !loots.TryGetValue(loot, out var l)) return res;
    foreach (var e in Arr(l, "aCOs")) { var (n, ch, ct) = ParseEntry(e); if (n.Length == 0 || n == "Blank") continue; res[n] = res.GetValueOrDefault(n) + ch * ct; }
    foreach (var e in Arr(l, "aLoots")) { var (n, ch, ct) = ParseEntry(e); foreach (var kv in Expand(n, depth + 1)) res[kv.Key] = res.GetValueOrDefault(kv.Key) + kv.Value * ch * ct; }
    return res;
}

// Conditions on a CO, with an overlay's cond loot applied on top
Dictionary<string, double> Conds(string name)
{
    var d = new Dictionary<string, double>();
    string baseName = name;
    JsonElement? ov = null;
    if (!cos.ContainsKey(name) && overlays.TryGetValue(name, out var o)) { ov = o; baseName = Str(o, "strCOBase"); }
    if (baseName != null && cos.TryGetValue(baseName, out var co))
        foreach (var e in Arr(co, "aStartingConds")) { var (n, ch, ct) = ParseEntry(e); d[n] = ch * ct; }
    if (ov != null)
    {
        var cl = Str(ov.Value, "strCondLoot");
        if (cl != null && loots.TryGetValue(cl, out var l))
            foreach (var e in Arr(l, "aCOs")) { var (n, ch, ct) = ParseEntry(e); d[n] = ch * ct; }
    }
    return d;
}

var all = new SortedSet<string>(cos.Keys.Where(k => Str(cos[k], "strType")?.ToLower() == "item"));
foreach (var k in overlays.Keys) { var b = Str(overlays[k], "strCOBase"); if (b != null && cos.TryGetValue(b, out var bc) && Str(bc, "strType")?.ToLower() == "item") all.Add(k); }

// References by folder: a quoted name or a Name= loot entry
var refs = all.ToDictionary(k => k, k => new SortedSet<string>());
var token = new Regex("\"([A-Za-z0-9_\\-/ ]+?)(?:=[^\"]*)?\"", RegexOptions.Compiled);
string[] scan = { "ships", "loot", "market", "installables", "interactions", "plots", "plot_beats", "careers", "jobs", "jobitems", "personspecs", "crewskins", "homeworlds", "lifeevents", "pledges", "slots", "racing", "transit", "zone_triggers", "tokens" };
foreach (var folder in scan)
{
    string p = Path.Combine(data, folder); if (!Directory.Exists(p)) continue;
    foreach (var f in Directory.GetFiles(p, "*.json", SearchOption.AllDirectories))
    {
        string text = File.ReadAllText(f);
        foreach (Match m in token.Matches(text)) { var n = m.Groups[1].Value; if (refs.TryGetValue(n, out var set)) set.Add(folder); }
    }
}

// Dismantle outputs by the CO dismantled
var dismantle = new Dictionary<string, string>();
foreach (var i in installs)
    if (Str(i, "strJobType") == "dismantle" && Str(i, "strActionCO") is string a && Str(i, "strLootOut") is string lo) dismantle[a] = lo;

// Destruction: a Destructable update command names an interaction loot, whose mode switches either turn the CO
// into another (its damaged state) or into a loot table of items (what is left once it is destroyed)
var modeSwitch = new Dictionary<string, string>();
foreach (var o in Objects("interactions")) { var n = Str(o, "strName"); var ms = Str(o, "objLootModeSwitch"); if (n != null && ms != null) modeSwitch[n] = ms; }
var destroy = new Dictionary<string, Dictionary<string, double>>();
var damageTo = new Dictionary<string, List<string>>();
foreach (var name in all)
{
    string baseName = cos.ContainsKey(name) ? name : Str(overlays[name], "strCOBase");
    if (baseName == null || !cos.TryGetValue(baseName, out var bc)) continue;
    foreach (var cmd in Arr(bc, "aUpdateCommands"))
    {
        var p = cmd.Split(',');
        if (p.Length < 3 || p[0] != "Destructable" || !loots.TryGetValue(p[2], out var act)) continue;
        foreach (var e in Arr(act, "aCOs"))
        {
            var ia = ParseEntry(e).name;
            if (!modeSwitch.TryGetValue(ia, out var target)) continue;
            if (loots.TryGetValue(target, out var lt) && !cos.ContainsKey(target) && !overlays.ContainsKey(target)) destroy[name] = Expand(target);
            else (damageTo.TryGetValue(name, out var l) ? l : damageTo[name] = new List<string>()).Add(target);
        }
    }
}

// Install / uninstall / repair pairs, so a variant of an obtainable item counts as obtainable
var links = new List<(string a, string b)>();
foreach (var i in installs)
{
    var a = Str(i, "strActionCO"); if (a == null) continue;
    foreach (var o in Arr(i, "aLootCOs")) links.Add((a, ParseEntry(o).name));
    if (Str(i, "strStartInstall") is string s) links.Add((a, s));
}
foreach (var name in all)
{
    JsonElement src = cos.ContainsKey(name) ? cos[name] : overlays[name];
    var ms = Arr(src, "mapModeSwitches");
    for (int i = 0; i + 1 < ms.Length; i += 2) links.Add((ms[i], ms[i + 1]));
}

using var w = new StreamWriter(Path.Combine(outDir, "items.tsv"), false, new UTF8Encoding(false));
w.WriteLine("name\tfriendly\tbase\tmass\tprice\ttiles\tcats\tflags\trefs\tdismantle\tlinks\tstats\tisconds\tdestroy\tdamageto");
foreach (var name in all)
{
    JsonElement src = cos.ContainsKey(name) ? cos[name] : overlays[name];
    string baseName = cos.ContainsKey(name) ? name : Str(src, "strCOBase");
    var c = Conds(name);
    string friendly = Str(src, "strNameFriendly") ?? (cos.TryGetValue(baseName, out var bco) ? Str(bco, "strNameFriendly") : "");
    string itemDef = cos.TryGetValue(baseName, out var bc2) ? Str(bc2, "strItemDef") : null;
    int tiles = itemDef != null && items.TryGetValue(itemDef, out var it) ? Math.Max(1, Arr(it, "aSocketAdds").Length) : 0;
    string cats = string.Join(",", c.Keys.Where(k => k.StartsWith("IsCategory")).Select(k => k[10..]));
    string[] flagNames = { "IsInstalled", "IsDamaged", "IsWall", "IsFloor", "IsWindow", "IsDoor", "IsClothing", "IsFood", "IsDrink", "IsContainer", "IsScrap", "IsTool", "IsWeapon", "IsSalvageValueHigh", "IsSalvageValueLow", "IsHuman", "IsRobot", "IsPart", "IsComponent", "IsRawMaterial", "IsLiquid", "IsGas", "IsBody", "IsDatafile", "IsHiddenInv", "IsShipPart" };
    string flags = string.Join(",", flagNames.Where(c.ContainsKey).Select(f => f[2..]));
    string extra = string.Join(",", c.Where(kv => kv.Key.StartsWith("Stat") && (kv.Key.Contains("Armor") || kv.Key.Contains("Insul") || kv.Key.Contains("DamageMax") || kv.Key.Contains("Hull"))).Select(kv => $"{kv.Key[4..]}={kv.Value.ToString("0.##", inv)}"));
    string dis = dismantle.TryGetValue(name, out var lt) ? string.Join(";", Expand(lt).Select(kv => $"{kv.Key}*{kv.Value.ToString("0.##", inv)}")) : "";
    string lk = string.Join(";", links.Where(l => l.a == name).Select(l => l.b).Concat(links.Where(l => l.b == name).Select(l => l.a)).Distinct());
    w.WriteLine(string.Join("\t", name, friendly, baseName == name ? "" : baseName, c.GetValueOrDefault("StatMass").ToString("0.###", inv), c.GetValueOrDefault("StatBasePrice").ToString("0.##", inv), tiles, cats, flags + (extra.Length > 0 ? "|" + extra : ""), string.Join(",", refs[name]), dis, lk,
        string.Join(",", c.Where(kv => kv.Key.StartsWith("Stat")).Select(kv => $"{kv.Key[4..]}={kv.Value.ToString("0.###", inv)}")),
        string.Join(",", c.Keys.Where(k => k.StartsWith("Is") && !k.StartsWith("IsCategory")).Select(k => k[2..])),
        destroy.TryGetValue(name, out var ds) ? string.Join(";", ds.Select(kv => $"{kv.Key}*{kv.Value.ToString("0.##", inv)}")) : "",
        damageTo.TryGetValue(name, out var dt) ? string.Join(";", dt) : ""));
}
Console.WriteLine($"{all.Count} item COs, {cos.Count} COs, {overlays.Count} overlays, {loots.Count} loot tables");
