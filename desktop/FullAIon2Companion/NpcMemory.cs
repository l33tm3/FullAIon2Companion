using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// Remembers which elite NPC (field boss) an entity id was, and the highest HP each boss was ever seen with.
/// The game announces a boss once, when its zone is entered; coming back to it, or starting the app mid-session,
/// leaves it as a bare entity id. Entity ids last the whole game session (Kelpina de onda azul kept 55841 across
/// teleports), so the link is kept on disk. Ids are reused after a relog, hence the HP check: an entity seen with far
/// less HP than any field boss has is an ordinary creature. The maximum matters as much: joining a fight halfway, the
/// highest HP observed is the boss's current HP (Axios read 88% at 70M of its 160M).
/// </summary>
public sealed class NpcMemory
{
    public sealed record Entry(int Npc, long MaxHp, DateTime Seen);
    /// <summary>Exact: NPCs whose maximum was read from a spawn frame; the others are the highest HP seen.</summary>
    public sealed record Saved(Dictionary<int, Entry> Entities, Dictionary<int, long> NpcMaxHp, int[]? Exact = null);
    readonly Dictionary<int, Entry> entities = [];
    readonly Dictionary<int, long> npcMax = [];
    readonly HashSet<int> exact = [];
    readonly object gate = new();
    DateTime lastSave = DateTime.MinValue;
    bool dirty;
    static string FilePath => Path.Combine(Program.DataDirectory, "npc-memory.json");
    static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    public NpcMemory()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            string json = File.ReadAllText(FilePath);
            // The first version stored only the entity map.
            var saved = json.Contains("\"Entities\"") ? JsonSerializer.Deserialize<Saved>(json)
                : new Saved(JsonSerializer.Deserialize<Dictionary<int, Entry>>(json) ?? [], []);
            if (saved is null) return;
            foreach (var (id, entry) in saved.Entities ?? [])
            {
                if (DateTime.Now - entry.Seen < Lifetime) entities[id] = entry;
                npcMax[entry.Npc] = Math.Max(npcMax.GetValueOrDefault(entry.Npc), entry.MaxHp);
            }
            foreach (var (npc, max) in saved.NpcMaxHp ?? []) npcMax[npc] = Math.Max(npcMax.GetValueOrDefault(npc), max);
            exact.UnionWith(saved.Exact ?? []);
        }
        catch { }
    }
    /// <summary>Notes an entity known to be this NPC; only elite NPCs are kept.</summary>
    public void Learn(int entity, int npc, long? highestHp)
    {
        if (!Catalog.IsElite(npc)) return;
        lock (gate)
        {
            long max = Math.Max(highestHp ?? 0, entities.TryGetValue(entity, out var known) && known.Npc == npc ? known.MaxHp : 0);
            if (known is null || known.Npc != npc || known.MaxHp != max || DateTime.Now - known.Seen > TimeSpan.FromMinutes(5))
            { entities[entity] = new(npc, max, DateTime.Now); dirty = true; }
            if (!exact.Contains(npc) && max > npcMax.GetValueOrDefault(npc)) { npcMax[npc] = max; dirty = true; }
        }
        Save();
    }
    /// <summary>The remembered NPC of an entity the stream did not announce this time, or null.</summary>
    public int? Recall(int entity, long? highestHp)
    {
        lock (gate)
        {
            return entities.TryGetValue(entity, out var e) && DateTime.Now - e.Seen < Lifetime && (highestHp is not long hp || hp >= 1_000_000) ? e.Npc : null;
        }
    }
    /// <summary>The highest HP this NPC was ever seen with, across fights and sessions; null when never seen.</summary>
    public long? MaxHpOf(int npc) { lock (gate) return npcMax.TryGetValue(npc, out long max) && max > 0 ? max : null; }
    /// <summary>True when the maximum was read from a spawn frame (now or in an earlier session); a remembered highest
    /// HP is only a lower bound of it.</summary>
    public bool MaxFromEarlier(int npc) { lock (gate) return exact.Contains(npc); }
    /// <summary>The maximum the spawn frame announced: exact, so it replaces any observed estimate.</summary>
    public void SetExactMax(int npc, long max)
    {
        if (!Catalog.IsElite(npc)) return;
        lock (gate)
        {
            exact.Add(npc);
            if (npcMax.GetValueOrDefault(npc) == max) return;
            npcMax[npc] = max; dirty = true;
        }
        Save();
    }
    void Save()
    {
        lock (gate)
        {
            if (!dirty || DateTime.Now - lastSave < TimeSpan.FromSeconds(10)) return;
            dirty = false; lastSave = DateTime.Now;
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(entities, npcMax, exact.ToArray()))); } catch { }
        }
    }
}
