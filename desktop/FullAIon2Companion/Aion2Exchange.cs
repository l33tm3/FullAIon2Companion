using System.Net.Http;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// aion2.exchange (Aion2Market): the community's record of the game's Market, every server's prices every hour, with the
/// game's item and server ids. Used with its authors' permission (given to the owner on 2026-10-18), credited in the
/// Mercado tab; the app only reads its data and links nowhere. Two requests: the servers (/api/servers, its server_id is
/// 10000 + the game's: 2401 Israphel is 12401) and a server's whole Market in one page (/api/items, about 600 items).
/// Nothing about the player is sent: only the server asked for.
/// </summary>
public sealed class Aion2Exchange : IMarketSource
{
    const string Site = "https://aion2.exchange";
    static readonly HttpClient http = Client();
    readonly Dictionary<int, int> ids = [];
    static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FullAIon2Companion/{AppVersion.Current} (Windows; +https://governanceai.me)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
    public string Name => "aion2.exchange";

    public async Task<IReadOnlyList<MarketServer>> ServersAsync(CancellationToken cancel)
    {
        using var document = await Get("/api/servers", cancel);
        var list = new List<MarketServer>();
        foreach (var s in document.RootElement.GetProperty("servers").EnumerateArray())
        {
            if (s.TryGetProperty("has_data", out var has) && has.ValueKind == JsonValueKind.False) continue;
            if (Number(s, "game_server_key") is not double key || key is < 1000 or > 9999) continue;
            int id = (int)key;
            lock (ids) ids[id] = Number(s, "server_id") is double own ? (int)own : 10000 + id;
            list.Add(new(id, Text(s, "name"), Text(s, "region"), Text(s, "region_name"), Text(s, "faction")));
        }
        return list;
    }

    public async Task<MarketBoard> BoardAsync(int server, CancellationToken cancel)
    {
        int id; lock (ids) id = ids.GetValueOrDefault(server, 10000 + server);
        var rows = new List<MarketRow>();
        // About 600 items fit one page of 1,000; more pages only if it grows (5 at most).
        for (int offset = 0, total = 1; offset < total && offset < 5000; offset += 1000)
        {
            using var document = await Get($"/api/items?server={id}&sort=name&order=asc&window=24h&limit=1000&offset={offset}", cancel);
            var root = document.RootElement;
            total = (int)Math.Clamp(Number(root, "total") ?? 0, 0, 100_000);
            int before = rows.Count;
            foreach (var r in root.GetProperty("items").EnumerateArray()) if (Row(r) is { } row) rows.Add(row);
            if (rows.Count == before) break;
        }
        return new(server, DateTime.UtcNow, rows);
    }

    static MarketRow? Row(JsonElement r)
    {
        if (Number(r, "item_key") is not double key || key is <= 0 or > int.MaxValue) return null;
        int item = (int)key;
        string section = r.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.Object ? Text(c, "section_id") : "";
        DateTime at = r.TryGetProperty("captured_at", out var a) && a.TryGetDateTime(out var when) ? when.ToUniversalTime() : DateTime.UtcNow;
        return new(item, Text(r, "name"), section, Kinah(r, "price"), Kinah(r, "last_price"),
            (int)Math.Clamp(Number(r, "listings") ?? 0, 0, int.MaxValue), Number(r, "change_pct"), at);
    }
    static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    // Fields may be null (price, when nobody sells the item now): only numbers count.
    static double? Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : null;
    static long? Kinah(JsonElement e, string name) => Number(e, name) is double d && d > 0 && d < 1e13 ? (long)Math.Round(d) : null;

    static async Task<JsonDocument> Get(string path, CancellationToken cancel)
    {
        using var answer = await http.GetAsync(Site + path, cancel);
        string type = answer.Content.Headers.ContentType?.MediaType ?? "";
        if (!answer.IsSuccessStatusCode || !type.Contains("json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"no respondió con datos (HTTP {(int)answer.StatusCode}).");
        await using var body = await answer.Content.ReadAsStreamAsync(cancel);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancel);
    }
}
