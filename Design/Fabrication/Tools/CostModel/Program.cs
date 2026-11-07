using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// Fabrication cost model for base game items, from ItemScan's items.tsv. Usage: dotnet run -c Release -- <folder
// holding items.tsv> <output csv>. The rules are described in Design/Fabrication/README.md.
string dir = args[0];
string outCsv = args[1];
var inv = CultureInfo.InvariantCulture;

string[] Mats = { "Base Metals", "Metal Alloys", "Precious Metals", "Rare Earths", "Polymers", "Ceramics", "Organics", "Silicates", "Volatiles", "Helium Fines", "Exotics" };
const int Base = 0, Alloy = 1, Precious = 2, RareEarth = 3, Polymer = 4, Ceramic = 5, Organic = 6, Silicate = 7, Volatile = 8, Helium = 9, Exotic = 10;

var lines = File.ReadAllLines(Path.Combine(dir, "items.tsv"));
var head = lines[0].Split('\t');
var rows = lines.Skip(1).Select(l => l.Split('\t')).Select(p => head.Select((h, i) => (h, v: i < p.Length ? p[i] : "")).ToDictionary(x => x.h, x => x.v)).ToList();
var by = rows.ToDictionary(r => r["name"]);
double D(string s) => double.TryParse(s, NumberStyles.Float, inv, out var v) ? v : 0;
Dictionary<string, double> Stats(Dictionary<string, string> r) => r["stats"].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => D(p[1]));
HashSet<string> Is(Dictionary<string, string> r) => r["isconds"].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
Dictionary<string, double> Out(string s) => s.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Split('*')).Where(p => p.Length == 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Sum(p => D(p[1])));

// Families: an item's installed, loose, off and damaged forms share a name once their state words are taken off
var suffix = new Regex("(Dmg|Loose|Off|Patch|OnG|On|Open|Closed|Locked|Armed|Secured|Custom|Firing|Batt|IgnitionMini|Ignition|Deposit)+$");
string Key(string n) => suffix.Replace(n, "");
bool Damaged(Dictionary<string, string> r) { var m = suffix.Match(r["name"]); return (m.Success && (m.Value.Contains("Dmg") || m.Value.Contains("Patch"))) || Is(r).Contains("Damaged"); }

// Obtainable: placed on a ship, in a loot table, given by an interaction, or another state of one that is
string[] seeds = { "loot", "ships", "interactions", "slots", "jobitems", "pledges" };
var excludeName = new Regex("^(Body|\\[)|Debug|Test|Placeholder|^ItmIceTrash|^ItmMeat|Armed$|^Liquid|^Sys");
string[] excludeCats = { "Food", "Intoxicants", "Water", "D2O", "He3" };

// What mined ore and ice break down into, set by hand. Regolith is the only source of Helium Fines, the fine dust
// solar wind implants helium-3 in.
var ores = new Dictionary<string, double[]>
{
    ["ItmMineral01"] = Wd((Base, .85), (Alloy, .15)),                         // Meteoric iron, nickel-iron
    ["ItmMineral02"] = Wd((Silicate, .5), (Base, .25), (RareEarth, .25)),      // Olivine
    ["ItmMineral03"] = Wd((Ceramic, .5), (Organic, .5)),                       // Carbon and carbides
    ["ItmMineral04"] = Wd((Silicate, 1)),                                      // Silicates
    ["ItmMineral05"] = Wd((Precious, 1)),                                      // Platinum
    ["ItmMineral06"] = Wd((Precious, 1)),                                      // Iridium
    ["ItmMineral07"] = Wd((Precious, 1)),                                      // Palladium
    ["ItmMineral08"] = Wd((Alloy, 1)),                                         // Cobalt
    ["ItmMineral09"] = Wd((Precious, 1)),                                      // Gold
    ["ItmMineral10"] = Wd((Alloy, 1)),                                         // Wolfram
    ["ItmMineral11"] = Wd((Volatile, 1)),                                      // Hydrates
    ["ItmMineral79"] = Wd((Exotic, 1)),                                        // Void opal
    ["ItmMineralStone01"] = Wd((Helium, .5), (Silicate, .3), (Base, .1), (RareEarth, .1)), // Regolith
    ["ItmIce01"] = Wd((Volatile, 1)),                                          // Water ice
    ["ItmIce02"] = Wd((Volatile, 1)),                                          // Methane ice
    ["ItmCargoVolatiles01"] = Wd((Volatile, 1)),                               // Volatiles canister
    ["ItmCargoOre01"] = Wd((Base, .5), (Silicate, .3), (RareEarth, .1), (Precious, .1)), // Mixed ore cargo
};

// Ores costed by true mass, a unit to every 0.01 kg, instead of the usual curve: water ice, to match the water
// ration's hand-written recipe (25 Volatiles for 0.25 kg, in fz_manual_liquids.json). Deposits still weigh these ores
// on the curve, so a deposit with some ice in it isn't swamped by it.
var oresByMass = new HashSet<string> { "ItmIce01" };
const double UnitMassKg = 0.01;

// Deposits and placed regolith hold the ore their mining tables give (loot_mining.json), here as the expected count
// of each. A deposit's makeup is that mix, its amount the usual mass curve, so the salvaging laser can cut one down.
(string ore, double count)[] stony = { ("ItmMineral02", .3), ("ItmMineral01", .2), ("ItmMineral04", .2), ("ItmMineral08", .02), ("ItmMineral09", .03), ("ItmMineral05", .01), ("ItmMineral06", .01), ("ItmMineral07", .01), ("ItmMineral10", .02), ("ItmMineralStone01", .2) }; // ItmRandomMineralSClass
(string ore, double count)[] dark = { ("ItmMineral03", .37), ("ItmMineral04", .4), ("ItmMineral11", .1), ("ItmIce01", .1), ("ItmMineral79", .03) };     // ItmRandomMineralCClass
(string ore, double count)[] shiny = { ("ItmMineral01", .4), ("ItmMineral02", .2), ("ItmMineral04", .1), ("ItmMineral05", .02), ("ItmMineral06", .03), ("ItmMineral07", .04), ("ItmMineral08", .06), ("ItmMineral09", .04), ("ItmMineral10", .11) }; // ItmRandomMineralMClass
(string ore, double count)[] ice = { ("ItmIce01", 1.0), ("ItmIce02", .2) };                                                                                     // ItmIce01Salvage, less its gangue
var deposits = new Dictionary<string, (string ore, double count)[]>
{
    ["ItmFloorRock02"] = stony, ["ItmFloorRock021x1"] = stony,
    ["ItmFloorRock02C"] = dark, ["ItmFloorRock021x1C"] = dark,
    ["ItmFloorRock02M"] = shiny, ["ItmFloorRock021x1M"] = shiny,
    ["ItmFloorIce02"] = ice, ["ItmFloorIce021x1"] = ice, ["ItmWallIce011x1"] = ice,
    // Regolith walls, from their ItmWallRock0NContents tables
    ["ItmWallRock011x1"] = new[] { ("ItmMineral01", 1.6), ("ItmMineral02", .45), ("ItmMineralStone01", 4.0) },
    ["ItmWallRock021x1"] = new[] { ("ItmMineral01", 6.5), ("ItmMineral02", 2.0), ("ItmMineralStone01", 1.0) },
    ["ItmWallRock031x1"] = new[] { ("ItmMineral03", 1.6), ("ItmMineral04", .45), ("ItmMineralStone01", 4.0) },
    ["ItmWallRock041x1"] = new[] { ("ItmMineral03", 6.5), ("ItmMineral04", 2.0), ("ItmMineralStone01", 1.0) },
    ["ItmWallRock051x1"] = new[] { ("ItmMineral05", 1.6), ("ItmMineral06", .45), ("ItmMineralStone01", 4.0) },
    ["ItmWallRock061x1"] = new[] { ("ItmMineral05", 6.5), ("ItmMineral06", 2.0), ("ItmMineralStone01", 1.0) },
};
var ok = new HashSet<string>();
var queue = new Queue<string>();
foreach (var r in rows)
    if (r["refs"].Split(',').Any(s => seeds.Contains(s)) && !excludeName.IsMatch(r["name"])) { ok.Add(r["name"]); queue.Enqueue(r["name"]); }
//Case is ignored, the game has a floor whose loose form differs from it only in case (ItmFloorLDPH04B, ItmFloorLDPH04bLoose)
var famMembers = rows.GroupBy(r => Key(r["name"]), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(r => r["name"]).ToList(), StringComparer.OrdinalIgnoreCase);
while (queue.Count > 0)
{
    var n = queue.Dequeue(); var r = by[n];
    foreach (var l in r["links"].Split(';').Concat(r["damageto"].Split(';')).Concat(famMembers[Key(n)]))
        if (l.Length > 0 && by.ContainsKey(l) && !excludeName.IsMatch(l) && ok.Add(l)) queue.Enqueue(l);
}
// Ore and ice come from mining, which no loot table or ship shows, and deposits are part of asteroids
foreach (var ore in ores.Keys) if (by.ContainsKey(ore)) ok.Add(ore);
foreach (var deposit in deposits.Keys)
    foreach (var member in famMembers.GetValueOrDefault(Key(deposit), new List<string>())) ok.Add(member);
bool Usable(Dictionary<string, string> r) => ok.Contains(r["name"]) && D(r["mass"]) > 0 && !r["cats"].Split(',').Any(c => excludeCats.Contains(c));

// The building blocks dismantling and destruction produce, set by hand. A scrap of cloth weighs 25 g, so it holds a
// tenth of a unit, but making one still takes a whole unit.
var leafContent = new Dictionary<string, double[]>
{
    ["ItmScrapClothClean"] = Wd((Organic, 0.1)),
    ["ItmScrapClothDirty"] = Wd((Organic, 0.1)),
};
double[] Wd(params (int m, double w)[] e) { var a = new double[Mats.Length]; foreach (var (m, w) in e) a[m] += w; return a; }
var leaves = new Dictionary<string, int[]>
{
    ["ItmScrapSteel"] = M((Base, 1)),
    ["ItmScrapAluminum"] = M((Base, 1)),
    ["ItmScrapCarbonFiber"] = M((Organic, 1)),
    ["ItmScrapPlastic"] = M((Polymer, 1)),
    ["ItmScrapClothClean"] = M((Organic, 1)),
    ["ItmScrapClothDirty"] = M((Organic, 1)),
    ["ItmScrapTrash"] = M((Organic, 1)),
    ["ItmPartsMechSmall01"] = M((Base, 1)),
    ["ItmPartsElecSmall01"] = M((Base, 1), (Silicate, 1)),
    ["ItmComponentMobo01"] = M((Silicate, 1), (Precious, 1), (Polymer, 1)),
    ["ItmComponentMotor01"] = M((Base, 2), (RareEarth, 1)),
    ["ItmHeatSink01"] = M((Base, 2)),
    ["ItmPartsScreen01"] = M((Silicate, 3), (Polymer, 1), (RareEarth, 1), (Precious, 1)),
};
int[] M(params (int m, int n)[] e) { var a = new int[Mats.Length]; foreach (var (m, n) in e) a[m] += n; return a; }

// Shares of an item's material by category, the first listed category that has one wins
var profiles = new Dictionary<string, double[]>
{
    ["SpaceSuits"] = W((Polymer, .55), (Alloy, .15), (Silicate, .1), (Base, .1), (Organic, .1)),
    ["Weapons"] = W((Base, .4), (Alloy, .35), (Polymer, .15), (Ceramic, .1)),
    ["FusionParts"] = W((Alloy, .4), (Ceramic, .25), (Base, .2), (RareEarth, .1), (Precious, .05)),
    ["Sensors"] = W((Silicate, .3), (Base, .3), (RareEarth, .15), (Polymer, .15), (Precious, .1)),
    ["ControlSystems"] = W((Base, .35), (Silicate, .25), (Polymer, .2), (Precious, .1), (RareEarth, .1)),
    ["Electronics"] = W((Base, .3), (Polymer, .25), (Silicate, .25), (Precious, .1), (RareEarth, .1)),
    ["Medical"] = W((Polymer, .4), (Base, .2), (Silicate, .2), (Organic, .1), (Precious, .1)),
    ["LifeSupport"] = W((Base, .4), (Polymer, .3), (Ceramic, .15), (Silicate, .15)),
    ["HVAC"] = W((Base, .6), (Polymer, .2), (Alloy, .1), (Ceramic, .1)),
    ["Tools"] = W((Base, .5), (Alloy, .3), (Polymer, .2)),
    ["Textiles"] = W((Polymer, .6), (Organic, .4)),
    ["LuxuryGoods"] = W((Precious, .4), (Polymer, .3), (Silicate, .2), (Ceramic, .1)),
    ["Media"] = W((Polymer, .5), (Silicate, .3), (Base, .2)),
    ["Furniture"] = W((Polymer, .4), (Base, .4), (Organic, .2)),
    ["IndustrialProducts"] = W((Base, .6), (Alloy, .2), (Polymer, .2)),
    ["Hull"] = W((Base, .55), (Alloy, .25), (Polymer, .2)),
    ["Misc"] = W((Base, .6), (Alloy, .2), (Polymer, .2)),
    ["ConsumerGoods"] = W((Polymer, .6), (Base, .4)),
    ["Science"] = W((Silicate, .4), (Base, .3), (Precious, .15), (RareEarth, .15)),
    ["Metals"] = W((Base, 1)),
    ["Plastics"] = W((Polymer, 1)),
    ["Trash"] = W((Organic, 1)),
};
double[] W(params (int m, double w)[] e) { var a = new double[Mats.Length]; foreach (var (m, w) in e) a[m] += w; return a; }
int[] premium = { Alloy, Precious, RareEarth, Ceramic };
int[] common = { Base, Polymer, Silicate, Organic };

string Category(Dictionary<string, string> r)
{
    if (ores.ContainsKey(r["name"])) return "Ore";
    if (deposits.ContainsKey(Key(r["name"]))) return "Deposit";
    var cats = r["cats"].Split(',', StringSplitOptions.RemoveEmptyEntries);
    var c = profiles.Keys.FirstOrDefault(k => cats.Contains(k));
    if (c != null) return c;
    return r["name"].Contains("Weapon") ? "Weapons" : "Misc";
}

// Material grows with mass, but more slowly, so the heaviest items don't dwarf the rest
double Units(double mass) => Math.Max(1, Math.Pow(mass, 0.75));

// Integer amounts in proportion to the shares, largest remainders first
int[] Allocate(double[] w, double total)
{
    int n = Math.Max(1, (int)Math.Round(total));
    double sum = w.Sum(); var exact = w.Select(x => x / sum * n).ToArray();
    var res = exact.Select(x => (int)Math.Floor(x)).ToArray();
    foreach (var i in Enumerable.Range(0, w.Length).OrderByDescending(i => exact[i] - res[i]).Take(n - res.Sum())) res[i]++;
    return res;
}

// Price per kilogram against the category's median, so value-dense items lean on rarer materials
var undamagedReps = rows.Where(r => Usable(r) && !Damaged(r) && D(r["price"]) > 0).ToList();
var medianPpk = undamagedReps.GroupBy(Category).ToDictionary(g => g.Key, g => { var v = g.Select(r => D(r["price"]) / D(r["mass"])).OrderBy(x => x).ToList(); return v[v.Count / 2]; });
double[] Shift(double[] w, string cat, double ppk)
{
    if (ppk <= 0 || !medianPpk.TryGetValue(cat, out var med) || med <= 0) return w;
    double q = Math.Clamp(Math.Log10(ppk / med), -1, 1), s = 0.2 * Math.Abs(q);
    var res = (double[])w.Clone();
    int[] from = q > 0 ? common : premium, to = q > 0 ? premium : common;
    double fromSum = from.Sum(i => w[i]), toSum = to.Sum(i => w[i]);
    if (fromSum <= 0) return w;
    double moved = s * fromSum;
    foreach (var i in from) res[i] -= moved * w[i] / fromSum;
    if (toSum > 0) foreach (var i in to) res[i] += moved * w[i] / toSum;
    else res[q > 0 ? Alloy : Base] += moved;
    return res;
}

// Walls and floors by how they perform against an unremarkable one of their kind. Structure is the material that
// carries an ordinary panel's load: base metal, or polymer for plastic panels and silicate for windows. Strength
// beyond what its mass gives comes from alloys, so a light wall swaps base metal for alloy and armour needs more of both.
(int[] mats, string role) Panel(Dictionary<string, string> r, double refMass, double refDurability, double refUnits)
{
    var st = Stats(r); var fl = Is(r);
    int tiles = Math.Max(1, (int)D(r["tiles"]));
    double w = D(r["mass"]) / tiles / refMass, d = st.GetValueOrDefault("DamageMax") / tiles / refDurability;
    double pressure = st.GetValueOrDefault("GasPressureMax") / 4000.0;
    double structure = refUnits * w * Math.Sqrt(Math.Max(d, 0.05));
    double strength = 0.6 * refUnits * Math.Max(0, d - w) + (pressure > 1 ? refUnits * 0.2 * (pressure - 1) : 0);
    double aero = st.GetValueOrDefault("AeroLift") / 100.0 / tiles;
    var res = new double[Mats.Length];
    string role;
    bool plastic = fl.Contains("Plastic") || fl.Contains("WallPlastic1x1") || fl.Contains("SealTemp") || fl.Contains("Sheet");
    bool window = fl.Contains("WallWindow1x1") || r["name"].Contains("Window");
    bool wood = r["name"].Contains("Wood");
    if (window) { res[Silicate] += structure * 0.6; res[Base] += structure * 0.4; res[Alloy] += strength; role = "window"; }
    else if (plastic) { res[Polymer] += structure + strength; role = "plastic"; }
    else if (wood) { res[Organic] += structure * 0.7; res[Polymer] += structure * 0.3; res[Alloy] += strength; role = "wood"; }
    else { res[Base] += structure; res[Alloy] += strength; role = "metal"; }
    if (aero > 0) { res[Polymer] += aero; res[Alloy] += aero / 2; role += ", aerodynamic"; }
    if (fl.Contains("Fireproof")) { res[Ceramic] += Math.Max(1, structure * 0.3); role += ", fireproof"; }
    if (!st.ContainsKey("GasPressureMax") && r["isconds"].Contains("Wall")) role += ", unsealed";
    if (fl.Contains("MilSpec") || d >= 1.5) role += ", armoured";
    else if (w <= 0.5 && d >= 0.8) role += ", lightweight";
    var mats = res.Select(x => (int)Math.Round(x * tiles)).ToArray();
    if (mats.Sum() == 0) mats[res.ToList().IndexOf(res.Max())] = 1;
    return (mats, role);
}

bool IsWall(Dictionary<string, string> r) => Is(r).Contains("Wall") || r["name"].StartsWith("ItmWall") || r["name"].StartsWith("ItmSealTemp");
bool IsFloor(Dictionary<string, string> r) => Is(r).Contains("Floor") || Is(r).Contains("FloorGrate") || r["name"].StartsWith("ItmFloor");
var wallRef = by["ItmWall1x1"];
double wallRefMass = D(wallRef["mass"]), wallRefDur = Stats(wallRef)["DamageMax"];
// Every floor in the game is a grate, the plain steel one is the unremarkable one
var floorRef = by["ItmFloorGrate01"];
double floorRefMass = D(floorRef["mass"]), floorRefDur = Stats(floorRef)["DamageMax"];
Console.WriteLine($"wall reference {wallRefMass}kg durability {wallRefDur}; floor reference {floorRefMass}kg durability {floorRefDur}");

// Costs, worked out on demand so scrap outputs are always costed first
var cost = new Dictionary<string, int[]>();
var basis = new Dictionary<string, string>();
var visiting = new HashSet<string>();
int[] Need(string name, string route)
{
    var need = new double[Mats.Length];
    foreach (var (item, count) in Out(by[name][route]))
    {
        if (string.Equals(Key(item), Key(name), StringComparison.OrdinalIgnoreCase) || !by.ContainsKey(item) && !leaves.ContainsKey(item)) continue;
        if (leafContent.TryGetValue(item, out var content)) { for (int i = 0; i < need.Length; i++) need[i] += count * content[i]; continue; }
        var c = Cost(item); if (c == null) continue;
        for (int i = 0; i < need.Length; i++) need[i] += count * c[i];
    }
    return need.Select(x => (int)Math.Ceiling(x - 1e-6)).ToArray();
}
// An undamaged item can be damaged and then scrapped, so it holds what its damaged forms scrap into too
int[] Contain(string name, int[] mats, out bool raised)
{
    raised = false; var res = (int[])mats.Clone();
    var sources = new List<string> { name };
    if (!Damaged(by[name])) sources.AddRange(famMembers[Key(name)].Where(n => Damaged(by[n])));
    foreach (var source in sources)
        foreach (var route in new[] { "dismantle", "destroy" })
        {
            var need = Need(source, route);
            for (int i = 0; i < res.Length; i++) if (need[i] > res[i]) { res[i] = need[i]; raised = true; }
        }
    return res;
}
int[] Cost(string name)
{
    if (cost.TryGetValue(name, out var c)) return c;
    if (leaves.TryGetValue(name, out var leaf)) { basis[name] = "building block"; return cost[name] = leaf; }
    if (!by.TryGetValue(name, out var r) || D(r["mass"]) <= 0 || !visiting.Add(name)) return null;
    try
    {
        int[] mats; string b;
        if (Damaged(r))
        {
            // Damaged: three fifths of the undamaged form, but never less than it scraps into
            var undNames = famMembers[Key(name)].Where(n => !Damaged(by[n])).OrderBy(n => string.Equals(n, Key(name), StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n.Length);
            var und = undNames.Select(Cost).FirstOrDefault(x => x != null);
            mats = und != null ? und.Select(x => (int)Math.Floor(x * 0.6)).ToArray() : Allocate(profiles.GetValueOrDefault(Category(r), profiles["Misc"]), Units(D(r["mass"])) * 0.6);
            b = "60% of undamaged";
        }
        else if (ores.TryGetValue(name, out var ore))
        {
            bool byMass = oresByMass.Contains(name);
            mats = Allocate(ore, byMass ? D(r["mass"]) / UnitMassKg : Units(D(r["mass"])));
            b = byMass ? "ore, by mass" : "ore";
        }
        else if (deposits.TryGetValue(name, out var mix))
        {
            var w = new double[Mats.Length];
            foreach (var (oreName, count) in mix)
            {
                //Each ore weighed on the curve, whatever its own recipe uses, so ice costed by mass doesn't swamp the mix
                var oc = oresByMass.Contains(oreName) && ores.TryGetValue(oreName, out var oreMix) && by.TryGetValue(oreName, out var oreRow)
                    ? Allocate(oreMix, Units(D(oreRow["mass"]))) : Cost(oreName);
                for (int i = 0; i < w.Length; i++) w[i] += count * oc[i];
            }
            mats = Allocate(w, Units(D(r["mass"]))); b = "deposit, its mined ore";
        }
        else if (Category(r) == "Hull" && IsWall(r)) { (mats, var role) = Panel(r, wallRefMass, wallRefDur, Units(wallRefMass)); b = "wall: " + role; }
        else if (Category(r) == "Hull" && IsFloor(r)) { (mats, var role) = Panel(r, floorRefMass, floorRefDur, Units(floorRefMass)); b = "floor: " + role; }
        else
        {
            string cat = Category(r);
            var w = Shift(profiles[cat], cat, D(r["price"]) / D(r["mass"]));
            mats = Allocate(w, Units(D(r["mass"])));
            if (Is(r).Any(f => f.StartsWith("Lock") && f != "Locked")) { mats[Precious] += 1; mats[Silicate] += 1; }
            b = "mass, " + cat;
        }
        mats = Contain(name, mats, out bool raised);
        if (raised) b += ", raised to its scrap";

        // A damaged form costs at least what it scraps into. When that's all an undamaged item holds too, the
        // undamaged one takes a unit more, so damage always costs something.
        if (!Damaged(r))
        {
            int damagedNeed = famMembers[Key(name)].Where(n => Damaged(by[n]) && Usable(by[n]))
                .Select(n => new[] { "dismantle", "destroy" }.Select(route => Need(n, route).Sum()).Max()).DefaultIfEmpty(0).Max();
            if (mats.Sum() <= damagedNeed) { mats[Array.IndexOf(mats, mats.Max())] += damagedNeed - mats.Sum() + 1; b += ", above its damaged scrap"; }
        }
        if (mats.Sum() == 0) mats[Base] = 1;
        basis[name] = b;
        return cost[name] = mats;
    }
    finally { visiting.Remove(name); }
}

// One row for each family state: the loose form to spawn when there is one
string Pick(List<string> names, params string[] suffixes) => suffixes.Select(s => names.FirstOrDefault(n => n.EndsWith(s))).FirstOrDefault(n => n != null) ?? names.OrderBy(n => n.Length).First();
var csv = new StringBuilder();
csv.AppendLine("Item,State,Spawn CO,Other COs,Category,Mass (kg),Base Price," + string.Join(",", Mats) + ",Total,Basis");
int families = 0, conflicts = 0;
var report = new List<string>();
var outRows = new List<OutRow>();
foreach (var fam in famMembers.OrderBy(f => f.Key))
{
    var members = fam.Value.Where(n => Usable(by[n])).ToList();
    if (members.Count == 0) continue;
    families++;
    var und = members.Where(n => !Damaged(by[n])).ToList();
    var dmg = members.Where(n => Damaged(by[n])).ToList();
    int[] undCost = null;
    foreach (var (state, group) in new[] { ("Undamaged", und), ("Damaged", dmg) })
    {
        if (group.Count == 0) continue;
        string spawn = state == "Undamaged" ? Pick(group, "OffLoose", "Loose", fam.Key) : Pick(group, "DmgLoose", "LooseDmg", "PatchLoose", "Dmg", "Patch");
        var r = by[spawn];
        // The installed form carries the stats walls and floors are judged on
        string statsFrom = group.FirstOrDefault(n => new[] { fam.Key, fam.Key + "Patch", fam.Key + "Dmg" }.Contains(n, StringComparer.OrdinalIgnoreCase)) ?? spawn;
        var c = Cost(statsFrom) ?? Cost(spawn);
        if (c == null) continue;
        if (state == "Undamaged") undCost = c;
        else if (undCost != null && c.Sum() >= undCost.Sum())
        {
            conflicts++; report.Add($"{spawn}: damaged costs {c.Sum()}, undamaged {undCost.Sum()} ({basis[statsFrom]})");
        }
        string friendly = r["friendly"].Length > 0 ? r["friendly"] : by[statsFrom]["friendly"];
        csv.AppendLine(string.Join(",", Q(friendly), state, spawn, Q(string.Join(" ", group.Where(n => n != spawn))), Category(by[statsFrom]),
            D(by[statsFrom]["mass"]).ToString("0.###", inv), D(by[statsFrom]["price"]).ToString("0.##", inv), string.Join(",", c), c.Sum(), Q(basis[statsFrom])));
        outRows.Add(new OutRow(fam.Key, state == "Damaged", spawn, group, statsFrom, c, friendly));
    }
}
string Q(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
File.WriteAllText(outCsv, csv.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"), new UTF8Encoding(false));
Console.WriteLine($"{families} families, {ok.Count} obtainable COs, {conflicts} damaged not cheaper");
foreach (var l in report.Take(40)) Console.WriteLine("  " + l);

// Recipe and material data for the mod, when paths for them are given
if (args.Length >= 4) WriteData(args[2], args[3]);

void WriteData(string recipesDir, string materialsPath)
{
    string[] ids = Mats.Select(m => m.Replace(" ", "")).ToArray();
    var blocks = new HashSet<string>(leaves.Keys);
    bool Raw(string cat) => cat == "Ore" || cat == "Deposit";

    // Specialised: the product lines a corporation would guard. High-end branded tech is left to an override list
    // once it's decided, judging it by price caught cheap, dense items like ammunition and medical pens.
    string[] specialisedPrefixes = { "ItmShipWeapon", "ItmDockSys", "ItmSensor", "ItmNavMod", "ItmStationNav" };
    //Per the user, the GDM-X1 decoy, which never fails, is only ever learned from a corporate site, and rarely there
    string[] specialisedNames = { "ItmAmmoDecoyMissile03" };
    bool Specialised(OutRow o) => Category(by[o.StatsFrom]) == "FusionParts" || specialisedPrefixes.Any(p => o.Key.StartsWith(p)) || specialisedNames.Contains(o.Key);

    // Reverse engineering: how many of an Ordinary item have to be recycled (undamaged) to learn its blueprint, 1 for
    // a simple thing up to 10 for complicated technology. More material, precious metals or rare earths, a
    // technology category and a higher price each add to it.
    string[] techCategories = { "Electronics", "ControlSystems", "Sensors", "Weapons", "SpaceSuits", "Medical", "LifeSupport", "HVAC", "Media", "FusionParts" };
    int ReverseEngineer(OutRow o, string cat)
    {
        int units = o.Cost.Sum();
        double price = D(by[o.StatsFrom]["price"]);
        int n = 1;
        if (units >= 20) n++;
        if (units >= 60) n++;
        if (units >= 200) n++;
        if (o.Cost[Precious] > 0 || o.Cost[RareEarth] > 0) n++;
        if (techCategories.Contains(cat)) n += 2;
        if (price >= 2000) n++;
        if (price >= 20000) n++;
        return Math.Clamp(n, 1, 10);
    }

    // Blueprints: colour and design variants share one when they share a base name and a cost
    var trailing = new Regex("_?\\d+[A-Za-z]?$");
    var fabricable = outRows.Where(o => !o.Damaged && !Raw(Category(by[o.StatsFrom]))).ToList();
    var blueprintOf = new Dictionary<OutRow, string>();
    foreach (var byBase in fabricable.GroupBy(o => trailing.Replace(o.Key, "")))
    {
        var byCost = byBase.GroupBy(o => string.Join(",", o.Cost)).ToList();
        foreach (var g in byCost)
        {
            string name = byCost.Count == 1 ? byBase.Key : g.OrderBy(o => o.Key).First().Key;
            foreach (var o in g) blueprintOf[o] = name;
        }
    }

    // One file per top-level category, fz_recipes_<category>.json, so a pack is easier to read and another mod can
    // ignore a whole category. Old recipe files are removed first, including ones from before the fz_ prefix.
    Directory.CreateDirectory(recipesDir);
    foreach (string old in Directory.GetFiles(recipesDir, "*recipes_*.json")) File.Delete(old);
    var files = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);
    foreach (var o in outRows.OrderBy(o => o.Key).ThenBy(o => o.Damaged))
    {
        var r = by[o.StatsFrom]; string cat = Category(r);
        bool fab = blueprintOf.ContainsKey(o);
        bool block = blocks.Contains(o.Key);
        string path = CategoryPath(o, cat, block);
        string top = path.Split('.')[0];
        if (!files.TryGetValue(top, out var json)) files[top] = json = new StringBuilder("[\r\n");
        else json.Append(",\r\n");
        json.Append("  {\r\n");
        json.Append($"    \"strName\": {J(o.Damaged ? o.Key + "Dmg" : o.Key)},\r\n");
        json.Append($"    \"strNameFriendly\": {J(CleanName(o.Friendly))},\r\n");
        json.Append($"    \"strCategory\": {J(path)},\r\n");
        json.Append($"    \"aCOs\": [ {string.Join(", ", o.Group.Prepend(o.Spawn).Distinct().Select(J))} ],\r\n");
        if (fab) json.Append($"    \"strCOFabricate\": {J(o.Spawn)},\r\n");
        json.Append("    \"mapMaterials\": { " + string.Join(", ", o.Cost.Select((n, i) => (n, i)).Where(x => x.n > 0).Select(x => $"{J(ids[x.i])}: {x.n}")) + " },\r\n");
        json.Append($"    \"bFabricate\": {(fab ? "true" : "false")},\r\n");
        json.Append("    \"bRecycle\": true,\r\n");
        json.Append($"    \"fRecycleYield\": {(block || Raw(cat) ? "1.0" : "0.5")},\r\n");
        if (fab) json.Append($"    \"strBlueprint\": {J(blueprintOf[o])},\r\n");
        if (block) json.Append("    \"bStartKnown\": true,\r\n");
        bool ordinary = !(!o.Damaged && fab && Specialised(o));
        if (fab && ordinary && !block) json.Append($"    \"nReverseEngineer\": {ReverseEngineer(o, cat)},\r\n");
        json.Append($"    \"strTier\": {J(ordinary ? "Ordinary" : "Specialised")}\r\n");
        json.Append("  }");
    }
    foreach (var (top, json) in files)
    {
        json.Append("\r\n]\r\n");
        File.WriteAllText(Path.Combine(recipesDir, "fz_recipes_" + top.ToLowerInvariant() + ".json"), json.ToString(), new UTF8Encoding(false));
    }
    Console.WriteLine($"recipe files: {string.Join(", ", files.Select(f => $"{f.Key.ToLowerInvariant()} ({f.Value.ToString().Split("\"strName\"").Length - 1})"))}");

    // A material's unit price: low among the prices per unit of the items mostly made of it, at the 10th percentile.
    // Selling recycled material for more than an item's own price per unit would make buying and recycling it pay,
    // and the low end lines up with the game's own scrap prices, such as scrap steel at 3.6.
    var priced = outRows.Where(o => !o.Damaged && D(by[o.StatsFrom]["price"]) > 0 && o.Cost.Sum() > 0).ToList();
    var unitPrice = new double[Mats.Length];
    for (int i = 0; i < Mats.Length; i++)
    {
        int m = i;
        List<double> PerUnit(double share) => priced.Where(o => o.Cost[m] > 0 && o.Cost[m] >= share * o.Cost.Sum())
            .Select(o => D(by[o.StatsFrom]["price"]) / o.Cost.Sum()).OrderBy(x => x).ToList();
        var v = PerUnit(0.25);
        if (v.Count < 5) v = PerUnit(0.1);
        unitPrice[i] = v.Count > 0 ? v[(int)(v.Count * 0.1)] : 0;
    }
    int loops = priced.Count(o => o.Cost.Select((n, i) => n * unitPrice[i]).Sum() * 0.5 > D(by[o.StatsFrom]["price"]));
    Console.WriteLine($"material prices leave {loops} of {priced.Count} priced items worth more recycled and sold than bought");

    // Shown on silo panels, and the tint of the material's compacted block, in the order of Mats
    string[] colors = { "#9AA0A6", "#4A6FA5", "#D4AF37", "#6FBF73", "#3FB8AF", "#E8E2D6", "#8B5A2B", "#C2B280", "#7FD4FF", "#B39DDB", "#FF4FD8" };
    if (colors.Length != Mats.Length) throw new InvalidOperationException("every material needs a colour");
    // Game seconds a unit takes to recycle and to fabricate. Common, simple stock goes quickest, refined alloys and
    // rare stock slower, and Exotics far slower. Fabricating takes longer than breaking down.
    double[] recycleTimes = { 1, 2, 4, 4.5, 1.5, 1.5, 0.8, 1.2, 1, 6, 30 };
    double[] fabricateTimes = { 1.5, 3, 6, 7, 2, 2.5, 1.2, 1.8, 1.5, 9, 60 };
    // The power drawn while working each material, in kW. The harder a material is to separate or form, the more it
    // draws, so complex items draw more as well as taking longer. Fabricating draws half again as much.
    double[] recycleKw = { 3, 5, 8, 10, 3, 6, 2, 4, 3, 12, 40 };
    const double FabricateKwScale = 1.5;
    if (recycleTimes.Length != Mats.Length || fabricateTimes.Length != Mats.Length || recycleKw.Length != Mats.Length)
        throw new InvalidOperationException("every material needs its times and power");
    // kWh a unit takes: its power over its time, then 25 times that, as the plain figure was too slight to matter
    const double EnergyScale = 25.0;
    double Energy(double kw, double seconds) => Math.Round(kw * seconds / 3600.0 * EnergyScale, 6);
    var mjson = new StringBuilder("[\r\n");
    for (int i = 0; i < Mats.Length; i++)
    {
        double unit = unitPrice[i];
        mjson.Append("  {\r\n");
        mjson.Append($"    \"strName\": {J(ids[i])},\r\n");
        mjson.Append($"    \"strNameFriendly\": {J(Mats[i])},\r\n");
        mjson.Append($"    \"strColor\": {J(colors[i])},\r\n");
        mjson.Append($"    \"fUnitPrice\": {unit.ToString("0.##", inv)},\r\n");
        mjson.Append($"    \"fRecycleTime\": {recycleTimes[i].ToString("0.##", inv)},\r\n");
        mjson.Append($"    \"fFabricateTime\": {fabricateTimes[i].ToString("0.##", inv)},\r\n");
        mjson.Append($"    \"fRecycleEnergy\": {Energy(recycleKw[i], recycleTimes[i]).ToString("0.######", inv)},\r\n");
        mjson.Append($"    \"fFabricateEnergy\": {Energy(recycleKw[i] * FabricateKwScale, fabricateTimes[i]).ToString("0.######", inv)},\r\n");
        mjson.Append($"    \"strBlockCO\": {J(i == Exotic ? "ItmFzExoticCanister" : "ItmFzCompacted" + ids[i])},\r\n");
        mjson.Append($"    \"strSiloCond\": {J("IsFzSilo" + ids[i])}\r\n");
        mjson.Append(i < Mats.Length - 1 ? "  },\r\n" : "  }\r\n");
    }
    mjson.Append("]\r\n");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(materialsPath))!);
    File.WriteAllText(materialsPath, mjson.ToString(), new UTF8Encoding(false));

    var tiers = outRows.Where(o => blueprintOf.ContainsKey(o)).GroupBy(o => Specialised(o)).ToDictionary(g => g.Key, g => g.Count());
    Console.WriteLine($"wrote {outRows.Count} recipes, {fabricable.Count} fabricable in {blueprintOf.Values.Distinct().Count()} blueprints ({tiers.GetValueOrDefault(true)} specialised), {Mats.Length} materials");
}

// Group by role or game category, then the maker, the name before the quoted model in a friendly name
string CategoryPath(OutRow o, string cat, bool block)
{
    var r = by[o.StatsFrom];
    if (block) return o.Key.StartsWith("ItmScrap") ? "Scrap" : "Parts";
    if (cat == "Ore") return "Ore";
    if (cat == "Deposit") return "Deposits." + CleanName(o.Friendly).Split(' ')[0];
    string group = cat;
    if (cat == "Hull") group = IsWall(r) ? "Walls" : IsFloor(r) ? "Floors" : o.Key.StartsWith("ItmDoor") || o.Key.StartsWith("ItmHatch") || Is(r).Contains("Portal") ? "Doors" : "Hull";
    var maker = Regex.Match(CleanName(o.Friendly), "^(?:[^:\"]*:\\s*)?([^\"]+?)\\s+\"");
    return maker.Success ? group + "." + maker.Groups[1].Value.Replace(".", "") : group;
}

// Without the state words a loose, off or damaged form's name carries
string CleanName(string name) => Regex.Replace(name, "\\s*\\((?:(?:Loose|Off|Damaged|Patched|Cracked)(?:, )?)+\\)", "").Trim();

string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

record OutRow(string Key, bool Damaged, string Spawn, List<string> Group, string StatsFrom, int[] Cost, string Friendly);
