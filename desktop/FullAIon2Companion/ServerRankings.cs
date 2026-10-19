using System.Net.Http;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The combat-power ranking of a server (top 50), from the website's public ranking API (governanceai.me/api/rankings.php,
/// itself built from aion2index). NCSOFT's own profile carries the PvP ranking only for some characters (rankingList is
/// null for most, 2026-10-11), so the player card shows this place when the player is in it. Cached 10 minutes per server.
/// </summary>
public static class ServerRankings
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    static readonly Dictionary<(string, int), (DateTime At, Dictionary<string, int> Ranks)> cache = [];
    static readonly HashSet<(string, int)> loading = [];
    static readonly object gate = new();

    /// <summary>The player's place in his server's combat-power ranking, or null (not in the top 50, or not loaded yet).</summary>
    public static int? RankOf(string name, int serverId, string region)
    {
        var key = (region, serverId);
        lock (gate)
        {
            if (!cache.TryGetValue(key, out var entry) || DateTime.UtcNow - entry.At > TimeSpan.FromMinutes(10)) Load(key);
            return cache.TryGetValue(key, out entry) && entry.Ranks.TryGetValue(name.ToLowerInvariant(), out int rank) ? rank : null;
        }
    }
    static void Load((string Region, int Server) key)
    {
        if (!loading.Add(key)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                string json = await http.GetStringAsync($"https://governanceai.me/api/rankings.php?region={Uri.EscapeDataString(key.Region)}&server={key.Server}");
                var ranks = new Dictionary<string, int>();
                foreach (var e in JsonDocument.Parse(json).RootElement.GetProperty("entries").EnumerateArray())
                    if (e.TryGetProperty("name", out var n) && n.GetString() is { } name && e.TryGetProperty("rank", out var r) && r.TryGetInt32(out int rank))
                        ranks.TryAdd(name.ToLowerInvariant(), rank);
                lock (gate) cache[key] = (DateTime.UtcNow, ranks);
            }
            catch { lock (gate) cache[key] = (DateTime.UtcNow.AddMinutes(-8), []); } // retry in about two minutes
            finally { lock (gate) loading.Remove(key); }
        });
    }
}
