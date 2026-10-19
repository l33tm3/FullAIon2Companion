using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FullAIon2Companion;

public sealed record StatValue(string Name, int Value);
public sealed record GearRow(string Slot, string Name, string Grade, int Enchant, string? Icon, long Id = 0, int SlotPos = 0, int Exceed = 0);
public sealed record PlayerCard(string Name, string ClassName, int Level, string? Legion, string Server, long CombatPower, int? ItemLevel,
    string? Title, string? Portrait, IReadOnlyList<GearRow> Gear, IReadOnlyList<string> Rankings, DateTimeOffset FetchedAt,
    string? CharacterId = null, int ServerId = 0, string Region = "la", IReadOnlyDictionary<string, StatValue>? Stats = null,
    IReadOnlyDictionary<int, int>? SkillLevels = null);

/// <summary>Where a lookup stands. The panel polls it: the card fills in as each part arrives.</summary>
public sealed class ProfileState
{
    public volatile string Stage = "search";   // search -> basic -> full | missing | error
    public string? Error;
    public string? ClassName, Server, Portrait;
    public int Level;
    public PlayerCard? Card;
    public bool Stale;
}

/// <summary>
/// A player's official profile straight from NCSOFT's public API (the same endpoints governanceai.me proxies): the
/// search gives class, level and portrait in about a second; profile and equipment then load in parallel (~2.5 s).
/// The proxy's own "character" lookup adds every Daevanion board and item detail - fifteen calls, 14 s on a cold cache.
/// Cards are kept on disk, so a player seen before shows at once and refreshes behind; players appearing nearby are
/// fetched ahead of time, one at a time.
/// </summary>
public static class PlayerProfiles
{
    static readonly HttpClient http = Create();
    static readonly Dictionary<string, ProfileState> states = [];
    static readonly Dictionary<string, (string Id, PlayerCard Card)> disk = Load();
    static readonly Queue<(string Name, int Server, string Region)> prefetch = new();
    static readonly HashSet<string> queued = [];
    static readonly object gate = new();
    static bool working;
    static DateTime lastSave = DateTime.MinValue;
    static string FilePath => Path.Combine(Program.DataDirectory, "player-cache.json");
    static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);

    static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("User-Agent", "FullAIon2Companion/0.1 (+https://governanceai.me)");
        return client;
    }
    static string Key(string region, int server, string name) => $"{region}|{server}|{name.ToLowerInvariant()}";
    public static string RegionOf(int offset) => offset switch { 120 => "eu", -240 => "nae", -420 => "naw", 480 => "as", _ => "la" };

    /// <summary>The lookup for this player, started if needed. A card from disk is shown at once and refreshed when stale.</summary>
    public static ProfileState Get(string name, int server, string region)
    {
        string key = Key(region, server, name);
        lock (gate)
        {
            if (states.TryGetValue(key, out var state) && !(state.Stage is "missing" or "error" && state.Card is null)) return state;
            state = new ProfileState();
            if (disk.TryGetValue(key, out var known))
            {
                state.Card = known.Card; state.Stage = "full";
                // Cards saved before item ids were kept cannot show item details: refresh them too.
                state.Stale = DateTimeOffset.Now - known.Card.FetchedAt > Fresh || known.Card.Gear.Any(g => g.Id == 0) || known.Card.Stats is null || known.Card.SkillLevels is null;
            }
            states[key] = state;
            if (state.Card is null || state.Stale) _ = Fetch(name, server, region, state, known.Id);
            return state;
        }
    }
    /// <summary>Players seen nearby: fetched in the background so their card is ready when targeted.</summary>
    public static void Prefetch(IEnumerable<string> names, int server, string region)
    {
        lock (gate)
        {
            foreach (string name in names)
            {
                string key = Key(region, server, name);
                if (states.ContainsKey(key) || disk.TryGetValue(key, out var d) && DateTimeOffset.Now - d.Card.FetchedAt < Fresh || !queued.Add(key)) continue;
                prefetch.Enqueue((name, server, region));
            }
            if (!working && prefetch.Count > 0) { working = true; _ = Worker(); }
        }
    }
    static async Task Worker()
    {
        while (true)
        {
            (string Name, int Server, string Region) next;
            lock (gate) { if (prefetch.Count == 0) { working = false; return; } next = prefetch.Dequeue(); }
            var state = Get(next.Name, next.Server, next.Region);
            for (int i = 0; i < 100 && state.Stage is "search" or "basic"; i++) await Task.Delay(100);
            await Task.Delay(1500); // gentle on the public API: about 40 players a minute at most
        }
    }
    static async Task Fetch(string name, int server, string region, ProfileState state, string? knownId)
    {
        try
        {
            string? id = knownId;
            if (id is null)
            {
                var search = await Json($"https://api-search.plaync.com/aion2global/search/v2/character?keyword={Uri.EscapeDataString(name)}&region={region}&localeInfo=es-ES&page=1&size=20&serverId={server}");
                // A prefix match with the hit in <strong>: keep the exact name on the exact server.
                var hit = search.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().FirstOrDefault(x => Plain(Str(x, "name")).Equals(name, StringComparison.OrdinalIgnoreCase) && x.TryGetProperty("serverId", out var s) && s.GetInt32() == server)
                    : default;
                if (hit.ValueKind == JsonValueKind.Undefined) { if (state.Card is null) state.Stage = "missing"; return; }
                id = Uri.UnescapeDataString(Str(hit, "characterId"));
                state.Level = hit.TryGetProperty("level", out var lv) ? lv.GetInt32() : 0;
                state.Server = Str(hit, "serverName");
                state.Portrait = Str(hit, "profileImageUrl") is { Length: > 0 } img ? "https://profileimg.plaync.com" + img : null;
                if (state.Card is null) state.Stage = "basic";
            }
            string q = $"region={region}&lang=es-ES&serverId={server}&characterId={Uri.EscapeDataString(id)}";
            var infoTask = Json("https://aion2.plaync.com/api/character/info?" + q);
            var gearTask = Json("https://aion2.plaync.com/api/character/equipment?" + q);
            var info = await infoTask; var equipment = await gearTask;
            var card = Parse(info, equipment) with { CharacterId = id, ServerId = server, Region = region };
            state.Card = card; state.Stale = false; state.Stage = "full";
            lock (gate) { disk[Key(region, server, name)] = (id, card); }
            Save();
        }
        catch (Exception ex) { if (state.Card is null) { state.Error = ex.Message; state.Stage = "error"; } }
    }
    static PlayerCard Parse(JsonElement info, JsonElement equipment)
    {
        var profile = info.GetProperty("profile");
        int? itemLevel = null;
        var attributes = new Dictionary<string, StatValue>();
        if (info.TryGetProperty("stat", out var stat) && stat.TryGetProperty("statList", out var stats))
            foreach (var st in stats.EnumerateArray())
            {
                if (!st.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Number) continue;
                if (Str(st, "type") == "ItemLevel") { if (v.GetInt32() > 0) itemLevel = v.GetInt32(); }
                else attributes[Str(st, "type")] = new(Str(st, "name"), v.GetInt32());
            }
        var gear = new List<GearRow>();
        if (equipment.TryGetProperty("equipment", out var inner) && inner.TryGetProperty("equipmentList", out var items))
            foreach (var it in items.EnumerateArray())
                gear.Add(new(Str(it, "slotPosName"), Str(it, "name"), Str(it, "grade"), it.TryGetProperty("enchantLevel", out var en) ? en.GetInt32() : 0, Str(it, "icon"),
                    it.TryGetProperty("id", out var iid) ? iid.GetInt64() : 0, it.TryGetProperty("slotPos", out var sp) ? sp.GetInt32() : 0,
                    it.TryGetProperty("exceedLevel", out var ex) ? ex.GetInt32() : 0));
        if (equipment.TryGetProperty("petwing", out var pw) && pw.TryGetProperty("wing", out var wing) && wing.ValueKind == JsonValueKind.Object)
            gear.Add(new("Wing", Str(wing, "name"), Str(wing, "grade"), wing.TryGetProperty("enchantLevel", out var we) ? we.GetInt32() : 0, Str(wing, "icon")));
        // The level of each skill learned (the combat assistant's hover card shows that level's text, as the game does).
        var skillLevels = new Dictionary<int, int>();
        if (equipment.TryGetProperty("skill", out var skill))
            foreach (string list in new[] { "skillList", "stigmaList" })
                if (skill.TryGetProperty(list, out var sl) && sl.ValueKind == JsonValueKind.Array)
                    foreach (var sk in sl.EnumerateArray())
                        if (sk.TryGetProperty("id", out var sid) && sid.TryGetInt32(out int skillId) && sk.TryGetProperty("skillLevel", out var slv) && slv.TryGetInt32(out int level) && level > 0)
                            skillLevels[skillId] = level;
        var ranks = new List<string>();
        if (info.TryGetProperty("ranking", out var ranking) && ranking.TryGetProperty("rankingList", out var rl) && rl.ValueKind == JsonValueKind.Array)
            foreach (var r in rl.EnumerateArray())
            {
                string label = Str(r, "rankingContentsName") is { Length: > 0 } n ? n : Str(r, "name");
                string rank = r.TryGetProperty("rank", out var rk) && rk.ValueKind == JsonValueKind.Number ? "#" + rk.GetInt32() : "";
                if (label.Length > 0 || rank.Length > 0) ranks.Add($"{label} {rank}".Trim());
            }
        return new(Str(profile, "characterName"), Str(profile, "className"), profile.TryGetProperty("characterLevel", out var lv) ? lv.GetInt32() : 0,
            Str(profile, "regionName") is { Length: > 0 } legion ? legion : null, Str(profile, "serverName"),
            profile.TryGetProperty("combatPower", out var cp) ? cp.GetInt64() : 0, itemLevel,
            Str(profile, "titleName") is { Length: > 0 } title ? title : null, Str(profile, "profileImage") is { Length: > 0 } img ? img : null,
            gear, ranks, DateTimeOffset.Now, Stats: attributes, SkillLevels: skillLevels);
    }
    static readonly Dictionary<string, Task<JsonElement?>> items = [];
    /// <summary>One equipped item's official detail (stats, mana stones, divine stone), fetched on first hover and kept.</summary>
    public static Task<JsonElement?> ItemDetail(PlayerCard card, GearRow gear)
    {
        if (card.CharacterId is null || gear.Id == 0 || gear.SlotPos == 0) return Task.FromResult<JsonElement?>(null);
        string key = $"{card.CharacterId}|{gear.SlotPos}|{gear.Id}|{gear.Enchant}|{gear.Exceed}";
        lock (gate)
        {
            if (items.TryGetValue(key, out var hit) && !hit.IsFaulted) return hit;
            async Task<JsonElement?> Load() => await Json($"https://aion2.plaync.com/api/character/equipment/item?region={card.Region}&lang=es-ES&serverId={card.ServerId}&characterId={Uri.EscapeDataString(card.CharacterId)}&id={gear.Id}&enchantLevel={gear.Enchant}&exceedLevel={gear.Exceed}&slotPos={gear.SlotPos}");
            return items[key] = Load();
        }
    }
    static async Task<JsonElement> Json(string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data : root).Clone();
    }
    static Dictionary<string, (string, PlayerCard)> Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(FilePath)) is { } saved)
                return saved.ToDictionary(kv => kv.Key, kv => (kv.Value.Id, kv.Value.Card.CharacterId is null
                    ? kv.Value.Card with { CharacterId = kv.Value.Id, ServerId = int.TryParse(kv.Key.Split('|')[1], out int sv) ? sv : 0, Region = kv.Key.Split('|')[0] }
                    : kv.Value.Card));
        }
        catch { }
        return [];
    }
    public sealed record CacheEntry(string Id, PlayerCard Card);
    static void Save()
    {
        lock (gate)
        {
            if (DateTime.Now - lastSave < TimeSpan.FromSeconds(20)) return;
            lastSave = DateTime.Now;
            // Newest 2000 players: a busy town shows hundreds an hour.
            var keep = disk.OrderByDescending(kv => kv.Value.Card.FetchedAt).Take(2000).ToDictionary(kv => kv.Key, kv => new CacheEntry(kv.Value.Id, kv.Value.Card));
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(keep)); } catch { }
        }
    }
    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    static string Plain(string html) => Regex.Replace(html, "<[^>]*>", "");
}
