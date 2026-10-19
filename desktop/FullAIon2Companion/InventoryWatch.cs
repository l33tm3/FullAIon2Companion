using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>One item gained (loot, purchase, reward): when, which, how many, and its NPC sell value.</summary>
public sealed record LootEntry(DateTime At, int Item, string Name, string? Icon, string Grade, long Quantity, long Value);

/// <summary>
/// The inventory as the game updates it: opcode 0x1B56 <c>| 01 01 00 | item handle u32 | 0000 | item id u32 | count u64 | …</c>
/// (2026-10-12: Kina 930100031 showed 1,036,673 exactly as the game's HUD). A count that rises on a handle already seen, or a
/// new handle, is an item gained; Kina ("kina", the tradable kinah) gives the kinah earned. Spending (counts that fall) is not
/// income. Everything is counted from the farm session start.
/// </summary>
public sealed class InventoryWatch
{
    const int Update = 0x1B56, Whole = 0x1156;
    public const int Kinah = 930100031;
    readonly Dictionary<uint, (int Item, long Count)> handles = [];
    readonly List<LootEntry> loot = [];
    readonly object gate = new();
    DateTime at, sessionStart = DateTime.Now, wholeAt = DateTime.MinValue;
    long kinahGained, kinahNow = -1, lootValue;
    static readonly Dictionary<string, JsonElement> items = LoadItems();
    static Dictionary<string, JsonElement> LoadItems()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "items.json"))) ?? []; }
        catch { return []; }
    }
    /// <summary>Name (interface language), icon, grade and NPC sell price of an item, from data/items.json.</summary>
    public static (string Name, string? Icon, string Grade, long Price) Info(int item)
    {
        if (!items.TryGetValue(item.ToString(), out var r) || r.ValueKind != JsonValueKind.Array) return ($"Objeto {item}", null, "", 0);
        string es = r[0].GetString() ?? "", en = r[1].GetString() ?? "";
        string name = L.Language != "es" && en.Length > 0 ? en : es.Length > 0 ? es : en;
        return (name, r[2].GetString() is { Length: > 0 } i ? i : null, r[3].GetString() ?? "", r[4].TryGetInt64(out long p) ? p : 0);
    }

    /// <summary>Whether the item is in data/items.json.</summary>
    public static bool Known(int item) => items.ContainsKey(item.ToString());
    /// <summary>A bound item («(Vinculado)», «(Bound)»): it cannot be sold on the Market.</summary>
    public static bool Bound(int item) => items.TryGetValue(item.ToString(), out var r) && r.ValueKind == JsonValueKind.Array
        && ((r[0].GetString() ?? "").Contains("(Vinculado)", StringComparison.OrdinalIgnoreCase) || (r[1].GetString() ?? "").Contains("(Bound)", StringComparison.OrdinalIgnoreCase));

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length > 40 && BinaryPrimitives.ReadUInt16BigEndian(f) == Whole) { Baseline(f); return; }
        if (f.Length < 25 || BinaryPrimitives.ReadUInt16BigEndian(f) != Update || f[2] != 1) return;
        uint handle = BinaryPrimitives.ReadUInt32LittleEndian(f[5..]);
        int item = BinaryPrimitives.ReadInt32LittleEndian(f[13..]);
        long count = BinaryPrimitives.ReadInt64LittleEndian(f[17..]);
        if (item <= 0 || count < 0 || count > 100_000_000_000) return;
        lock (gate)
        {
            // The byte after 01 says what happened (2026-10-13, souls of 2026-10-11): 00 a new stack (loot, a soul: 1B56
            // 01 00 00 ... count 1), 01 a stack whose count changed, 02 a stack gone (the soul absorbed 0.26 s later).
            // An unknown stack that only changed is old (the app started late): a baseline, not income.
            byte kind = f[3];
            bool known = handles.TryGetValue(handle, out var before);
            bool fresh = !known && kind == 0;
            Version++;
            if (kind == 2) { handles.Remove(handle); return; }
            handles[handle] = (item, count);
            if (item == Kinah)
            {
                if (kinahNow >= 0 && count > kinahNow && at >= sessionStart) kinahGained += count - kinahNow;
                kinahNow = count;
                return;
            }
            long gained = known ? count - before.Count : fresh ? count : 0;
            if (gained <= 0 || at < sessionStart) return;
            var info = Info(item);
            loot.Add(new(at, item, info.Name, info.Icon, info.Grade, gained, gained * info.Price));
            Gained?.Invoke(item, gained, at);
            lootValue += gained * info.Price;
            if (loot.Count > 2000) loot.RemoveRange(0, 1000);
        }
    }
    // 0x1156, the whole inventory at login (2026-10-12 02:35, 11,578 bytes, 246 stacks): stacks of about 35 bytes with
    // the same core as an update, handle u32 | 4 bytes | item id u32 | count u64, followed by a variable tail. They are
    // found by a known item id with a sane count and become the baseline (not income): Kina 5,793,539, Abyss Points 15,858.
    void Baseline(ReadOnlySpan<byte> f)
    {
        lock (gate)
        {
            // A new login (another character, or the same one again) brings the whole inventory anew: the stacks of the
            // one before are gone. Frames of one login come together.
            if (at - wholeAt > TimeSpan.FromSeconds(10)) handles.Clear();
            wholeAt = at; Complete = true;
            for (int i = 10; i + 12 <= f.Length; i++)
            {
                int item = BinaryPrimitives.ReadInt32LittleEndian(f[i..]);
                if (item < 100_000_000 || !items.ContainsKey(item.ToString())) continue;
                long count = BinaryPrimitives.ReadInt64LittleEndian(f[(i + 4)..]);
                uint handle = BinaryPrimitives.ReadUInt32LittleEndian(f[(i - 8)..]);
                if (count is < 1 or > 100_000_000_000 || handle == 0) continue;
                handles[handle] = (item, count);
                if (item == Kinah) kinahNow = count;
                i += 11;
            }
            Version++;
        }
    }
    /// <summary>Raises whenever a stack changes.</summary>
    public int Version { get; private set; }
    /// <summary>Whether the game sent the whole inventory (at login) since the app started: else only the stacks that changed are known.</summary>
    public bool Complete { get; private set; }
    /// <summary>How many of each wanted item the character holds (all its stacks), as the game last said.</summary>
    public Dictionary<int, long> Holdings(Func<int, bool> wanted)
    {
        lock (gate)
        {
            var sum = new Dictionary<int, long>();
            foreach (var (item, count) in handles.Values) if (wanted(item)) sum[item] = sum.GetValueOrDefault(item) + count;
            return sum;
        }
    }
    /// <summary>An item entered the inventory (item id, quantity, frame time): pet souls and the loot of each creature are counted from here too.</summary>
    public event Action<int, long, DateTime>? Gained;
    /// <summary>Kinah earned in the session and the kinah now (null until the game sent it once).</summary>
    public (long Gained, long? Now) KinahSession { get { lock (gate) return (kinahGained, kinahNow >= 0 ? kinahNow : null); } }
    public IReadOnlyList<LootEntry> Loot { get { lock (gate) return loot.ToList(); } }
    /// <summary>The last items gained, oldest first.</summary>
    public IReadOnlyList<LootEntry> Recent(int count) { lock (gate) return loot.TakeLast(count).ToList(); }
    /// <summary>Sell value of everything looted in the session.</summary>
    public long LootValue { get { lock (gate) return lootValue; } }
    public void ResetSession() { lock (gate) { sessionStart = DateTime.Now; kinahGained = 0; lootValue = 0; loot.Clear(); } }
}
