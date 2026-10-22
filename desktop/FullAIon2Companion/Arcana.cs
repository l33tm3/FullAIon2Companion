using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The arcanas of a character, as the game's Arcanas window shows them (2026-10-22, the user: «¿podemos construir la parte de
/// las arcanas?»): the five cards of the official profile (slots Arcana1-5) and each one's official detail - its Empyrean Lord
/// stat (the card's base and the enchant's extra), its four skills and its set with the 2- and 4-piece effects. The panel adds
/// them up (Arcanas tab) and plans changes of level and set. Read from NCSOFT's public profile only: nothing goes to the game.
/// </summary>
public static class ArcanaBook
{
    public static bool Has(PlayerCard card) => card.Gear.Any(IsArcana);
    static bool IsArcana(GearRow g) => g.Slot.StartsWith("Arcana", StringComparison.Ordinal);
    static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
    static int N(JsonElement e, string k) => int.TryParse(S(e, k), out int n) ? n : 0;
    static long L(JsonElement e, string k) => long.TryParse(S(e, k), out long n) ? n : 0;

    /// <summary>The panel's view: the cards in slot order and the sets they belong to (with every member, for the planner).</summary>
    public static async Task<object> Load(PlayerCard card)
    {
        var slots = card.Gear.Where(IsArcana).OrderBy(g => g.Slot, StringComparer.Ordinal).ToList();
        var details = await Task.WhenAll(slots.Select(async g => { try { return await PlayerProfiles.ItemDetail(card, g); } catch { return null; } }));
        var cards = new List<object>();
        var sets = new Dictionary<string, object>();
        for (int i = 0; i < slots.Count; i++)
        {
            var g = slots[i];
            object? stat = null;
            var skills = new List<object>();
            string? setId = null, setName = null, category = null, gradeName = null;
            int max = 0;
            if (details[i] is { } d)
            {
                category = S(d, "categoryName"); gradeName = S(d, "gradeName"); max = N(d, "maxEnchantLevel");
                if (d.TryGetProperty("mainStats", out var main) && main.ValueKind == JsonValueKind.Array && main.GetArrayLength() > 0)
                    stat = new { id = S(main[0], "id"), name = S(main[0], "name"), @base = N(main[0], "value"), extra = N(main[0], "extra") };
                if (d.TryGetProperty("subSkills", out var subs) && subs.ValueKind == JsonValueKind.Array)
                    foreach (var k in subs.EnumerateArray()) skills.Add(new { id = L(k, "id"), name = S(k, "name"), level = N(k, "level"), icon = S(k, "icon") });
                if (d.TryGetProperty("set", out var set) && set.ValueKind == JsonValueKind.Object)
                {
                    setId = S(set, "id"); setName = S(set, "name");
                    if (setId.Length > 0 && !sets.ContainsKey(setId))
                    {
                        var bonuses = new List<object>();
                        if (set.TryGetProperty("bonuses", out var bs) && bs.ValueKind == JsonValueKind.Array)
                            foreach (var b in bs.EnumerateArray())
                                bonuses.Add(new { degree = N(b, "degree"), text = b.TryGetProperty("descriptions", out var t) && t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [] });
                        var members = new List<object>();
                        if (set.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                            foreach (var m in items.EnumerateArray()) members.Add(new { id = L(m, "id"), name = S(m, "name") });
                        sets[setId] = new { id = setId, name = setName, bonuses, members };
                    }
                }
            }
            cards.Add(new { slot = g.Slot, id = g.Id, name = g.Name, grade = g.Grade, gradeName, enchant = g.Enchant, max, icon = g.Icon, category, stat, skills, setId, setName, loaded = details[i] is not null });
        }
        return new { name = card.Name, server = card.Server, fetchedAt = card.FetchedAt, cards, sets = sets.Values.ToList() };
    }
}
