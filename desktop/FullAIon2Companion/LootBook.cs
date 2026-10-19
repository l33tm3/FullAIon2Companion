using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The loot each creature gave the player, learned from play and kept on this computer (loot-observed.json). Every
/// creature of ours that dies counts a kill; an item that enters the inventory up to 30 s later (the loot is picked up,
/// often from several bodies at once: 2026-10-11, a Crasaur soul came 22 s after it died, with a Slime's) is put down
/// to the kind of creature that died in that time. With several kinds, the catalog decides: the only one whose table
/// has the item, or, when no table has it, the only one without a table. Otherwise, and right after a gathering node or
/// a cube was used up (its items come 0.1 s later), the item is left out. The loot panel shows what is learned for the
/// creatures without a catalog table (three of every four monsters).
/// </summary>
public sealed class LootBook
{
    public sealed record Seen(int Item, int Times, long Quantity);
    sealed class Creature { public int Kills { get; set; } public Dictionary<int, long[]> Items { get; set; } = []; } // item: [kills it came in, quantity]
    static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    static readonly Dictionary<int, HashSet<int>?> tables = [];
    readonly object gate = new();
    Dictionary<int, Creature> creatures = [];
    readonly List<(int Npc, DateTime At)> recent = [];
    readonly HashSet<(int Npc, int Item, DateTime Death)> counted = [];
    string? path;
    DateTime savedAt;
    bool dirty;
    /// <summary>The live app's book, for the loot panel.</summary>
    public static LootBook? Live { get; set; }
    /// <summary>Raises with every kill and every item learned.</summary>
    public int Version { get; private set; }
    /// <summary>Items put down to a creature since the app started (for tests).</summary>
    public int Learned { get; private set; }
    /// <summary>Keeps the book in a file (only the live app: replays and recordings learn in memory).</summary>
    public void Open(string file)
    {
        lock (gate)
        {
            path = file;
            try { creatures = JsonSerializer.Deserialize<Dictionary<int, Creature>>(File.ReadAllText(file)) ?? []; } catch { creatures = []; }
            Version++;
        }
    }
    public void Killed(int? npc, DateTime at)
    {
        if (npc is not int id) return;
        lock (gate)
        {
            (creatures.TryGetValue(id, out var c) ? c : creatures[id] = new()).Kills++;
            recent.Add((id, at));
            recent.RemoveAll(d => at - d.At > Window);
            Changed();
        }
    }
    /// <param name="gathered">A gathering node or a cube was used up just before: the item came from it.</param>
    public void Gained(int item, long quantity, DateTime at, bool gathered = false)
    {
        if (gathered) return;
        lock (gate)
        {
            var near = recent.Where(d => at - d.At is var age && age >= TimeSpan.Zero && age <= Window).ToList();
            if (Source(near.Select(d => d.Npc).Distinct().ToList(), item) is not int npc || !creatures.TryGetValue(npc, out var c)) return;
            if (!c.Items.TryGetValue(item, out var seen)) c.Items[item] = seen = [0, 0];
            // Several stacks of one item from the same kill are one time.
            counted.RemoveWhere(x => at - x.Death > Window);
            if (counted.Add((npc, item, near.Where(d => d.Npc == npc).Max(d => d.At)))) seen[0]++;
            seen[1] += quantity;
            Learned++;
            Changed();
        }
    }
    static int? Source(List<int> kinds, int item)
    {
        if (kinds.Count <= 1) return kinds.Count == 1 ? kinds[0] : null;
        var listing = kinds.Where(k => TableOf(k) is { } t && t.Contains(item)).ToList();
        if (listing.Count == 1) return listing[0];
        var unknown = kinds.Where(k => TableOf(k) is null).ToList();
        return listing.Count == 0 && unknown.Count == 1 ? unknown[0] : null;
    }
    // The item ids of a creature's catalog table (or of the variant the loot panel borrows), null without one.
    static HashSet<int>? TableOf(int npc)
    {
        lock (tables)
        {
            if (tables.TryGetValue(npc, out var known)) return known;
            HashSet<int>? Of(int id) => Catalog.Npc(id) is { } r && r.TryGetProperty("drops", out var d) && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0
                ? d.EnumerateArray().Select(x => x.TryGetProperty("id", out var i) && i.TryGetInt32(out int n) ? n : 0).ToHashSet() : null;
            return tables[npc] = Of(npc) ?? (Catalog.DropsSibling(npc) is int sibling ? Of(sibling) : null);
        }
    }
    /// <summary>The kills of a creature and what it gave, the most frequent first.</summary>
    public (int Kills, IReadOnlyList<Seen> Items) Of(int npc)
    {
        lock (gate)
        {
            if (!creatures.TryGetValue(npc, out var c)) return (0, []);
            return (c.Kills, c.Items.Select(x => new Seen(x.Key, (int)x.Value[0], x.Value[1])).OrderByDescending(x => x.Times).ThenByDescending(x => x.Quantity).ToList());
        }
    }
    /// <summary>Every creature with kills, for tests.</summary>
    public IReadOnlyList<int> Creatures() { lock (gate) return creatures.Keys.ToList(); }
    // Called under the lock. Written at most every 10 s; Flush writes the rest when the app closes.
    void Changed()
    {
        Version++; dirty = true;
        if ((DateTime.Now - savedAt).TotalSeconds >= 10) Save();
    }
    public void Flush() { lock (gate) if (dirty) Save(); }
    void Save()
    {
        if (path is null) return;
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(creatures));
            File.Move(path + ".tmp", path, overwrite: true);
            dirty = false; savedAt = DateTime.Now;
        }
        catch { }
    }
}
