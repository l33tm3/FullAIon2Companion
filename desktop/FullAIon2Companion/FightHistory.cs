using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>One player of a saved fight: damage, healing and the per-skill breakdown.</summary>
public sealed record FightPlayer(string Name, string? Class, long Damage, double Dps, long Healing, double Share, bool Self,
    IReadOnlyList<FightSkill> Skills);
public sealed record FightSkill(string Name, string? Icon, long Damage, int Hits, double CritPct, double Share);
/// <summary>The list entry of a saved fight (history/index.json).</summary>
public sealed record FightSummary(string Id, DateTime Start, DateTime End, double Seconds, string Place, string Target, bool Boss, bool Killed,
    long Damage, double GroupDps, long MyDamage, double MyDps, int Players, string? Character, string? Top);
public sealed record FightRecord(FightSummary Summary, IReadOnlyList<FightPlayer> Players, long? TargetMaxHp, int Kills);

/// <summary>
/// Every fight, kept on this PC when it ends (history/&lt;id&gt;.json plus history/index.json, the last 500): the meter clears
/// a fight as the next one starts, and at the end of an instance the numbers were gone before anyone could read them.
/// Nothing is uploaded.
/// </summary>
public sealed class FightHistory
{
    const int Keep = 500;
    static string Folder => Path.Combine(Program.DataDirectory, "history");
    static string IndexPath => Path.Combine(Folder, "index.json");
    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    readonly object gate = new();
    List<FightSummary> index;
    /// <summary>Changes on every save or delete, so the panel knows when to ask for the list again.</summary>
    public int Version { get; private set; }

    public FightHistory()
    {
        try { index = File.Exists(IndexPath) ? JsonSerializer.Deserialize<List<FightSummary>>(File.ReadAllText(IndexPath)) ?? [] : []; }
        catch { index = []; }
    }

    /// <summary>Saves (or updates: same start, same id) a fight.</summary>
    public void Save(FightRecord fight)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, fight.Summary.Id + ".json"), JsonSerializer.Serialize(fight, Json));
                index.RemoveAll(f => f.Id == fight.Summary.Id);
                index.Add(fight.Summary);
                index.Sort((a, b) => b.Start.CompareTo(a.Start));
                foreach (var old in index.Skip(Keep).ToList()) { index.Remove(old); try { File.Delete(Path.Combine(Folder, old.Id + ".json")); } catch { } }
                File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, Json));
                playerNames.Remove(fight.Summary.Id); Version++;
            }
            catch { }
        }
    }

    /// <summary>The saved fights, newest first, matching the text (target, place, a player, the character) when given.</summary>
    public IReadOnlyList<FightSummary> List(string? query = null, bool bossesOnly = false, int max = 200)
    {
        lock (gate)
        {
            IEnumerable<FightSummary> all = index;
            if (bossesOnly) all = all.Where(f => f.Boss);
            if (!string.IsNullOrWhiteSpace(query))
            {
                string q = query.Trim();
                all = all.Where(f => f.Target.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Place.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (f.Character?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) || (f.Top?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || PlayersOf(f.Id).Any(n => n.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }
            return all.Take(max).ToList();
        }
    }
    public FightRecord? Open(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9]{8}-[0-9]{6}-[0-9]+$")) return null;
        try { return JsonSerializer.Deserialize<FightRecord>(File.ReadAllText(Path.Combine(Folder, id + ".json"))); } catch { return null; }
    }
    public void Delete(string id)
    {
        lock (gate)
        {
            if (Open(id) is null) return;
            index.RemoveAll(f => f.Id == id);
            try { File.Delete(Path.Combine(Folder, id + ".json")); File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, Json)); } catch { }
            Version++;
        }
    }
    readonly Dictionary<string, string[]> playerNames = [];
    IEnumerable<string> PlayersOf(string id)
    {
        if (playerNames.TryGetValue(id, out var names)) return names;
        names = Open(id)?.Players.Select(p => p.Name).ToArray() ?? [];
        playerNames[id] = names;
        return names;
    }
}
