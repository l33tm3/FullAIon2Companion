using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

public static class Catalog
{
    static readonly Dictionary<int, JsonElement?> npcs = new();
    /// <summary>
    /// The creature's catalog record: its own, else the record of the creature it is another id of. The source lists 4,478
    /// alternative NPC ids (data/npc-aliases.json, the website resolves them the same way): Gran Vigia Nahma is 2600084 in
    /// the catalog and also spawns as 2600093/2600094, which had no record, no boss panel and no card (2026-10-18).
    /// </summary>
    public static JsonElement? Npc(int id) => Own(id) ?? (AliasOf(id) is int canonical ? Own(canonical) : null);
    static Dictionary<int, int>? aliases;
    public static int? AliasOf(int id)
    {
        aliases ??= LoadAliases();
        return aliases.TryGetValue(id, out int a) ? a : null;
    }
    static Dictionary<int, int> LoadAliases()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "npc-aliases.json")))!.ToDictionary(p => int.Parse(p.Key), p => p.Value); }
        catch { return []; }
    }
    static JsonElement? Own(int id)
    {
        lock(npcs)
        {
            if(npcs.TryGetValue(id, out var cached)) return cached;
            var path = Path.Combine(AppContext.BaseDirectory, "data", "npc", id + ".json");
            try { npcs[id] = File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone() : null; }
            catch { npcs[id] = null; }
            return npcs[id];
        }
    }
    /// <summary>Invisible skill helpers and other non-creature NPCs, identified by catalog type or an internal asset name.</summary>
    public static bool IsHelper(int? id) => id is int npc && (Npc(npc) is { } r && r.TryGetProperty("npcType", out var type) && type.GetString() == "Summon"
        && !(r.TryGetProperty("isMonster", out var monster) && monster.ValueKind == JsonValueKind.True)
        || System.Text.RegularExpressions.Regex.IsMatch(NpcName(npc), @"^[A-Za-z0-9]+(_[A-Za-z0-9]+)+$"));
    public static bool IsElite(int id) => Npc(id) is { } r && r.TryGetProperty("grade", out var grade)
        && grade.GetString() is "HeroMonster" or "LegendMonster" or "UniqueMonster" or "BossMonster";
    // Variants of one creature (same English name, other level) without a loot table -> the variant of nearest level
    // that has one (data/npc-siblings.json, built from the catalog).
    static Dictionary<int, int>? siblings;
    public static int? DropsSibling(int id)
    {
        siblings ??= LoadSiblings();
        return siblings.TryGetValue(id, out int s) ? s : null;
    }
    static Dictionary<int, int> LoadSiblings()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "npc-siblings.json")))!.ToDictionary(p => int.Parse(p.Key), p => p.Value); }
        catch { return []; }
    }
    /// <summary>The creature's name in the interface language: the Spanish catalog, or the game's own table in English or
    /// Portuguese (English when Portuguese is missing), then the catalog's original (English) name.</summary>
    public static string NpcName(int id)
    {
        if (L.Language != "es")
        {
            string? local = AionDPS.Aion2.Protocol.Aion2Npcs.NameOf(id, L.Language);
            if (local is { Length: > 0 }) return local;
            if (Npc(id) is { } r && r.TryGetProperty("originalName", out var o) && o.GetString() is { Length: > 0 } original) return original;
        }
        // An alternative id keeps the game's own name: some are the other gender of the catalog's creature.
        return Own(id)?.GetProperty("name").GetString() ?? AionDPS.Aion2.Protocol.Aion2Npcs.NameOf(id, "es") ?? Npc(id)?.GetProperty("name").GetString() ?? $"Criatura {id}";
    }
    public static string? NpcIcon(int id) => Npc(id) is { } npc && npc.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.String
        ? Path.Combine(AppContext.BaseDirectory, icon.GetString()!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)) : null;
}
