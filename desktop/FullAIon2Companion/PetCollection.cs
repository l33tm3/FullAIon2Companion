using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace FullAIon2Companion;

/// <summary>One owned (or started) pet: level 0 = still locked, collecting the first 5 souls.</summary>
public sealed record OwnedPet(int Id, string Name, string Category, int Level, int Progress, int Threshold);
/// <summary>A character's collection as the app shows it and uploads it.</summary>
public sealed record PetCollectionView(string Character, int ServerId, string Region, DateTime UpdatedAt, DateTime? ListAt, bool Live, int? Active,
    int Owned, int Total, int[] ByLevel, Dictionary<string, int[]> ByCategory, IReadOnlyList<OwnedPet> Pets, IReadOnlyList<PetHistoryDay> History);
/// <summary>The collection at the end of a day: owned pets, how many at levels 1, 2 and 3, and the souls gained that day.</summary>
public sealed record PetHistoryDay(string Date, int Owned, int L1, int L2, int L3, int Souls);

/// <summary>
/// Every pet of each character, kept on disk (pet-collection.json) and per day. The full list (0x0090: <c>| active pet u32 |
/// varint n | n x (pet u32, pet u32, level u32) | varint m | m x (pet u32, progress u32)</c>) only comes when the character
/// enters the game, so it is stored the moment it arrives and handed back to the decoder after a restart of the app; the
/// decoder then keeps it current with the live soul (0x0D90), level (0x1490) and new-pet (0x0990) frames.
/// The list is assigned to the character on screen only after a fresh 0x0090: switching characters must not file one
/// character's pets under another.
/// </summary>
public sealed class PetCollection
{
    const int LoginList = 0x0090;
    public sealed record Entry(string Name, int ServerId, string Region, DateTime UpdatedAt, DateTime? ListAt, int? Active, List<int[]> Pets, SortedDictionary<string, int[]> History);
    readonly Dictionary<string, Entry> entries;
    readonly object gate = new();
    static string FilePath => Path.Combine(Program.DataDirectory, "pet-collection.json");
    bool freshList;
    DateTime? listSeen;
    string? owner;
    DateTime lastWrite = DateTime.MinValue;
    bool dirty;
    /// <summary>Raised (at most once a minute per character) when a character's collection changed: the upload follows it.</summary>
    public event Action<PetCollectionView>? Changed;

    public PetCollection()
    {
        try { entries = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch { entries = []; }
    }
    static string Key(string name, int server) => $"{server}:{name.ToLowerInvariant()}";
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, f =>
    {
        if (f.Length >= 8 && BinaryPrimitives.ReadUInt16BigEndian(f) == LoginList) lock (gate) freshList = true;
    });

    /// <summary>
    /// Called from the snapshot with the character on screen and the decoder's pet list. Stores the list under the
    /// character, or gives the stored one back to the decoder when the app started after the character entered.
    /// </summary>
    public void Update(string? character, int? serverId, string region, Aion2EntityDirectory entities, IReadOnlyDictionary<int, int> soulsToday)
    {
        if (string.IsNullOrEmpty(character) || serverId is not int server) return;
        string key = Key(character, server);
        PetCollectionView? changed = null;
        lock (gate)
        {
            var states = entities.LocalPetStates;
            if (freshList && states.Count > 0) { owner = key; freshList = false; listSeen = DateTime.Now; }
            if (owner is null && states.Count == 0 && entries.TryGetValue(key, out var stored) && stored.Pets.Count > 0)
            {
                // The app started after the login list: the stored list, kept current by the live frames from here on.
                entities.SetLocalPetStates(stored.Pets.Select(p => new Aion2PetState(p[0], p[1], p[2])).ToList());
                owner = key;
                return;
            }
            if (owner != key || states.Count == 0) return;
            var pets = states.OrderBy(p => p.PetId).Select(p => new[] { p.PetId, p.Level, p.Progress }).ToList();
            entries.TryGetValue(key, out var before);
            var history = before?.History ?? new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            int[] levels = Levels(pets);
            int[] day = [levels[1] + levels[2] + levels[3], levels[1], levels[2], levels[3], soulsToday.Values.Sum()];
            bool same = before is not null && before.Pets.Count == pets.Count && before.Pets.Zip(pets).All(x => x.First.SequenceEqual(x.Second))
                && before.Active == entities.LocalActivePet && history.TryGetValue(today, out var was) && was.SequenceEqual(day);
            if (!same)
            {
                history[today] = day;
                while (history.Count > 400) history.Remove(history.Keys.First());
                entries[key] = new(character, server, region, DateTime.Now, listSeen ?? before?.ListAt, entities.LocalActivePet, pets, history);
                dirty = true;
            }
            // A change inside the minute stays pending and is written (and synced) once the minute is over, even when
            // nothing else changes after it (before, the last soul of a session waited until the app closed).
            if (dirty && DateTime.Now - lastWrite > TimeSpan.FromSeconds(60))
            {
                lastWrite = DateTime.Now; dirty = false;
                try { File.WriteAllText(FilePath, JsonSerializer.Serialize(entries)); } catch { }
                changed = View(entries[key], live: true);
            }
        }
        if (changed is not null) Changed?.Invoke(changed);
    }
    /// <summary>Writes what is pending (on exit).</summary>
    public void Flush() { lock (gate) if (dirty) { dirty = false; try { File.WriteAllText(FilePath, JsonSerializer.Serialize(entries)); } catch { } } }

    /// <summary>The character's collection, or null before the game ever sent its list.</summary>
    public PetCollectionView? Of(string? character, int? serverId)
    {
        if (string.IsNullOrEmpty(character) || serverId is not int server) return null;
        lock (gate) return entries.TryGetValue(Key(character, server), out var e) ? View(e, owner == Key(character, server)) : null;
    }
    /// <summary>Every character with a stored collection (for uploading them all after pairing).</summary>
    public IReadOnlyList<PetCollectionView> All() { lock (gate) return entries.Values.Select(e => View(e, owner == Key(e.Name, e.ServerId))).ToList(); }

    static int[] Levels(List<int[]> pets)
    {
        var l = new int[4];
        foreach (var p in pets) l[Math.Clamp(p[1], 0, 3)]++;
        return l;
    }
    static PetCollectionView View(Entry e, bool live)
    {
        var pets = e.Pets.Where(p => PetTracker.NameOf(p[0]) is not null)
            .Select(p => new OwnedPet(p[0], PetTracker.NameOf(p[0])!, PetTracker.CategoryOf(p[0]), p[1], p[2], PetTracker.Threshold(p[1])))
            .OrderByDescending(p => p.Level).ThenBy(p => p.Name).ToList();
        int[] levels = Levels(e.Pets);
        var byCategory = PetTracker.Categories.ToDictionary(c => c, c => new[] { pets.Count(p => p.Category == c && p.Level >= 1), PetTracker.CountIn(c) });
        return new(e.Name, e.ServerId, e.Region, e.UpdatedAt, e.ListAt, live, e.Active, levels[1] + levels[2] + levels[3], PetTracker.All.Count, levels, byCategory, pets,
            e.History.Select(h => new PetHistoryDay(h.Key, h.Value[0], h.Value[1], h.Value[2], h.Value[3], h.Value.Length > 4 ? h.Value[4] : 0)).ToList());
    }
}
