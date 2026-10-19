using System.Buffers.Binary;

namespace FullAIon2Companion;

/// <summary>
/// The character's counters as the game keeps them: entries, keys, limits and timers, from 0x0B61 (the whole list, at
/// login) and 0x0C61 (one counter that changed). Decoded on 2026-10-13 from the login of 2026-10-12 02:35:
/// <code>
/// 0B 61 | count varint (73) | entries
/// 0C 61 | 00 | one entry | reason u8
/// entry: mask u8 | key u32 | values by mask: 00 none · 01 u64 · 04 varint · 08 varint · 0C varint (+ varint)
/// </code>
/// Keys come in families: 1-15, 101-103 and 201-206 (201-206, 9 and 13 are u64 of 25,200,000 = 7 h in ms: time limits
/// such as the Abyss's), 10,000,001-12 (no value), 60,000,001+ (dungeon and content entries; 60,000,001 is the Od energy,
/// natural + additional), 70,000,001 and 90,000,001-6. Key 3 is a u64 (9,819,000; 6,649,000 a day later), not the bound kinah:
/// Kina (Bound) 930100001 is an inventory item (276,000 at the same login). The game sends the list only at login and then the changes, so the last state is kept per character.
/// </summary>
public sealed class GameResources
{
    public sealed record Counter(uint Key, long Value, long Extra, bool Long);
    const int Full = 0x0B61, Change = 0x0C61;
    readonly object gate = new();
    readonly Dictionary<uint, Counter> counters = [];
    /// <summary>Raises on every change; the state is the authoritative one only after a full list (Complete).</summary>
    public int Version { get; private set; }
    public bool Complete { get; private set; }
    public DateTime? At { get; private set; }
    public IReadOnlyDictionary<uint, Counter> Counters { get { lock (gate) return new Dictionary<uint, Counter>(counters); } }
    public void Add(ReadOnlySpan<byte> frame, DateTime at) => Frames.Walk(frame, f => Parse(f, at));
    /// <summary>A new connection or character: the next login list says everything again.</summary>
    public void Forget() { lock (gate) { counters.Clear(); Complete = false; At = null; Version++; } }

    void Parse(ReadOnlySpan<byte> f, DateTime at)
    {
        if (f.Length < 6) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if (opcode == Full)
        {
            int p = 2;
            if (!Varint(f, ref p, out long count) || count is < 1 or > 2000) return;
            var list = new List<Counter>();
            if (!Entries(f, p, (int)count, list)) return;
            lock (gate) { counters.Clear(); foreach (var c in list) counters[c.Key] = c; Complete = true; At = at; Version++; }
        }
        else if (opcode == Change)
        {
            // One entry after a 00, then a reason byte (02/03 seen).
            var list = new List<Counter>();
            if (!Entries(f, 3, 1, list, allowTail: 1)) return;
            lock (gate) { foreach (var c in list) counters[c.Key] = c; At = at; Version++; }
        }
    }
    // The entries in order; mask 0C carries one or two varints (key 1 at login: one), so both are tried and the reading
    // that ends exactly where the frame ends is kept.
    static bool Entries(ReadOnlySpan<byte> f, int p, int left, List<Counter> list, int allowTail = 0)
    {
        if (left == 0) return f.Length - p >= 0 && f.Length - p <= allowTail;
        if (p + 5 > f.Length) return false;
        byte mask = f[p];
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[(p + 1)..]);
        int q = p + 5;
        switch (mask)
        {
            case 0x00: list.Add(new(key, 0, 0, false)); break;
            case 0x01:
                if (q + 8 > f.Length) return false;
                long big = BinaryPrimitives.ReadInt64LittleEndian(f[q..]); q += 8;
                if (big < 0) return false;
                list.Add(new(key, big, 0, true)); break;
            case 0x04: { if (!Varint(f, ref q, out long v)) return false; list.Add(new(key, v, 0, false)); break; }
            case 0x08: { if (!Varint(f, ref q, out long v)) return false; list.Add(new(key, 0, v, false)); break; }
            case 0x0C:
            {
                if (!Varint(f, ref q, out long a)) return false;
                int one = q;
                if (Varint(f, ref q, out long b))
                {
                    list.Add(new(key, a, b, false));
                    if (Entries(f, q, left - 1, list, allowTail)) return true;
                    list.RemoveAt(list.Count - 1);
                }
                list.Add(new(key, a, 0, false));
                if (Entries(f, one, left - 1, list, allowTail)) return true;
                list.RemoveAt(list.Count - 1);
                return false;
            }
            default: return false;
        }
        if (Entries(f, q, left - 1, list, allowTail)) return true;
        list.RemoveAt(list.Count - 1);
        return false;
    }
    static bool Varint(ReadOnlySpan<byte> f, ref int p, out long value)
    {
        value = 0;
        for (int shift = 0; p < f.Length && shift <= 56; shift += 7) { byte b = f[p++]; value |= (long)(b & 127) << shift; if ((b & 128) == 0) return true; }
        return false;
    }
}
