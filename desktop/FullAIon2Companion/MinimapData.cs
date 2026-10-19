using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

public sealed record MinimapMap(string Id, string Name, double Ax, double Bx, double Ay, double By);

/// <summary>
/// The open-world atlas maps with their world-to-pixel transform (minimap.json, fitted from 13,000 catalog spawn points
/// against the atlas markers, under 2.5 px median error), and the creatures that are only found on one of them - the
/// creatures around the player tell which map that is.
/// </summary>
public static class MinimapData
{
    sealed record File_(List<MapJson> maps, Dictionary<string, string> npcMaps);
    sealed record MapJson(string id, string name, double ax, double bx, double ay, double by);
    static readonly Dictionary<string, MinimapMap> maps = [];
    static readonly Dictionary<int, string> npcMaps = [];
    static MinimapData()
    {
        try
        {
            var data = JsonSerializer.Deserialize<File_>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "minimap.json")))!;
            foreach (var m in data.maps) maps[m.id] = new(m.id, m.name, m.ax, m.bx, m.ay, m.by);
            foreach (var (npc, map) in data.npcMaps) npcMaps[int.Parse(npc)] = map;
        }
        catch { }
    }
    // The catalog's own map of each creature (dbaion "maps"), by atlas id; the Abyss has no calibrated atlas yet.
    static readonly Dictionary<string, string> slugs = new() { ["altgard"] = "2", ["verteron"] = "1", ["morheim"] = "8", ["eltnen"] = "9", ["ishalgen"] = "5", ["poeta"] = "6",
        ["reshanta"] = "3", ["reshanta-mid"] = "7", ["reshanta-top"] = "10" };
    public sealed record Detection(MinimapMap? Map, string? Region, double Votes, double Share);
    /// <summary>
    /// Which map these creatures belong to: each creature votes with the catalog's map (two votes, when it lists
    /// exactly one) or with the spawn tables (one vote). A map needs 2 votes and 60 % of them; "abyss" when the
    /// Abyss wins (no atlas). Creatures common to several maps do not vote.
    /// </summary>
    public static Detection Vote(IEnumerable<int> npcIds)
    {
        var votes = new Dictionary<string, double>();
        foreach (int npc in npcIds)
        {
            string? key = null; double weight = 1;
            if (Catalog.Npc(npc) is { } r && r.TryGetProperty("maps", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() == 1
                && slugs.TryGetValue(m[0].GetString() ?? "", out var bySlug)) { key = bySlug; weight = 2; }
            else if (npcMaps.TryGetValue(npc, out var byTable)) key = byTable;
            if (key is not null) votes[key] = votes.GetValueOrDefault(key) + weight;
        }
        if (votes.Count == 0) return new(null, null, 0, 0);
        var best = votes.MaxBy(v => v.Value);
        double share = best.Value / votes.Values.Sum();
        if (best.Value < 2 || share < .6) return new(null, null, best.Value, share);
        return maps.TryGetValue(best.Key, out var map) ? new(map, map.Id, best.Value, share) : new(null, best.Key, best.Value, share);
    }
    // World id (0x2136 map load) -> atlas id, learned from confident votes and kept on disk.
    static readonly Dictionary<int, string> worlds = LoadWorlds();
    static string WorldsPath => Path.Combine(Program.DataDirectory, "world-maps.json");
    static Dictionary<int, string> LoadWorlds()
    {
        Dictionary<int, string> learned;
        try { learned = File.Exists(WorldsPath) ? JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(WorldsPath)) ?? [] : []; } catch { learned = []; }
        // World ids known from the vendor's field-boss table (World_L_A = Verteron 1010, World_D_A = Altgard 1110).
        learned.TryAdd(1010, "1"); learned.TryAdd(1110, "2");
        // Drop what the first version learned wrongly: instances, and Abyss worlds mapped to open-world atlases.
        foreach (var bad in learned.Where(p => p.Key >= 100_000 || p.Key is 20).Select(p => p.Key).ToList()) learned.Remove(bad);
        return learned;
    }
    public static string? MapOfWorld(int? world) => world is int w && worlds.TryGetValue(w, out var m) ? m : null;
    public static MinimapMap? Map(string? id) => id is not null && maps.TryGetValue(id, out var m) ? m : null;
    public static void Learn(int? world, Detection d)
    {
        // Only open-world maps, and only worlds below the instance range: an Abyss world may hold several layers, and a
        // dungeon instance id is not a map (2026-10-11: 20 and 600022 were learned as Altgard from stale creatures).
        if (world is not int w || w >= 100_000 || d.Region is not ("1" or "2" or "5" or "6" or "8" or "9" or "3" or "7" or "10") || d.Votes < 8 || d.Share < .85
            || worlds.GetValueOrDefault(w) == d.Region) return;
        worlds[w] = d.Region;
        try { File.WriteAllText(WorldsPath, JsonSerializer.Serialize(worlds)); } catch { }
    }
    /// <summary>The map most of these creatures belong to, or null (a dungeon, or nothing announced yet).</summary>
    public static MinimapMap? Detect(IEnumerable<int> npcIds)
    {
        var vote = npcIds.Select(n => npcMaps.GetValueOrDefault(n)).Where(m => m is not null).GroupBy(m => m!).OrderByDescending(g => g.Count()).FirstOrDefault();
        return vote is not null && vote.Count() >= 2 && maps.TryGetValue(vote.Key, out var map) ? map : null;
    }
    /// <summary>World units to atlas pixels (4096 x 4096 at zoom 4).</summary>
    public static (double X, double Y) Project(MinimapMap map, double x, double y) => (map.Ax * x + map.Bx, map.Ay * y + map.By);
}

/// <summary>
/// «Tú estás aquí» for the Atlas page of the website in the panel (src/scripts/atlas.ts), placed as the minimap places the
/// player: the map the game named (its world id) or else the creatures' vote, and the last place known while standing
/// still with nothing around (same map load). The atlas pixels are the website's map units too (same 4096 atlas).
/// Free for everyone (the user's choice, 2026-10-17), and since 2026-10-19 the minimap too: the whole app is free.
/// </summary>
public sealed class AtlasLocator
{
    MinimapMap? lastMap;
    MapPosition? lastPos;
    int lastLoads = -1;
    /// <summary>The daeva-position message: the map, atlas pixels, exact or approximate (with a ring of about 12 m), or why not.</summary>
    public object Locate(CaptureSnapshot s)
    {
        if (s.MapLoads != lastLoads) { lastLoads = s.MapLoads; lastMap = null; lastPos = null; }
        var vote = MinimapData.Vote((s.Mobs ?? []).Select(m => m.Npc));
        string? known = MinimapData.MapOfWorld(s.World);
        if (known is not null && vote.Region is not null && vote.Region != known && vote.Votes >= 4 && vote.Share >= .8) known = null;
        var map = known is not null ? MinimapData.Map(known) : vote.Map;
        string? region = known ?? vote.Region;
        var pos = s.Position;
        bool stale = false;
        if (map is not null && pos is not null) { lastMap = map; lastPos = pos; }
        else if (map is null && region is null && lastMap is not null && lastPos is not null) { map = lastMap; pos = lastPos; stale = true; }
        if (map is null || pos is null)
            return new { type = "daeva-position", map = (string?)null, note = region == "abyss" ? "Abismo: el atlas de esta zona todavía no está calibrado para la posición"
                : s.World is > 100_000 ? "Mazmorra o instancia: no hay atlas de esta zona" : pos is null ? "Esperando tu posición: muévete o usa una habilidad" : "Reconociendo el mapa por las criaturas de alrededor…" };
        var (x, y) = MinimapData.Project(map, pos.X, pos.Y);
        if (x is < -200 or > 4296 || y is < -200 or > 4296) return new { type = "daeva-position", map = (string?)null, note = "Tu posición queda fuera del atlas de este mapa" };
        return new { type = "daeva-position", map = (string?)map.Id, mapName = map.Name, x = Math.Round(x, 1), y = Math.Round(y, 1), exact = pos.Exact, stale, ring = Math.Round(12 * Math.Abs(map.Ax) * 100, 1) };
    }
}
