using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The player's own places in the game's rankings, as the server sends them when the ranking window is opened: opcode
/// 0x578D, one frame per ranking and list (docs/ranking-capture-research.md, 2026-10-18):
/// <code>
/// 57 8D | 00 00 | ranking u8 | list u8 (00 all classes, 02 the class) | rank u32 | character u32 | 00 00 | server u16
///       | name (u8 + UTF-8) | legion (u8 + UTF-8) | u16 | level u16 | 6 bytes | u8 | points u64 | detail (varint + JSON)
///       | change i32 (up is positive) | 3 × i32 | players ranked i32 | next renewal u64 (ms; the rankings renew every hour, at :43)
/// </code>
/// Rankings: 1 Abyss (kill/death/assist), 3 bosses, 4 expedition, 5 and 6 two arenas (plays/wins/losses), 21 awakening
/// (a dungeon id and difficulty). Kept per character on this computer (rankings.json): the last of each ranking and list,
/// and a history with one row per hourly period (its next-renewal time).
/// </summary>
public sealed class RankingWatch
{
    public sealed record Entry(int Type, int List, int Rank, int Change, long Points, int Total, DateTime Snapshot, DateTime SeenAt, JsonElement Detail);
    public sealed record Point(int Type, int List, int Rank, long Points, int Total, DateTime Snapshot);
    sealed class Book
    {
        public Dictionary<string, Entry> Last { get; set; } = [];
        public List<Point> History { get; set; } = [];
    }
    const int Opcode = 0x578D, HistoryPerList = 400;
    readonly object gate = new();
    Dictionary<string, Book> books = [];
    string? path;
    DateTime at;
    /// <summary>Raises with every ranking frame.</summary>
    public int Version { get; private set; }

    public void Open(string file)
    {
        path = file;
        try { books = JsonSerializer.Deserialize<Dictionary<string, Book>>(File.ReadAllText(file)) ?? []; } catch { books = []; }
        foreach (var b in books.Values) { b.Last ??= []; b.History ??= []; }
    }
    void Save()
    {
        if (path is null) return;
        string json; lock (gate) json = JsonSerializer.Serialize(books);
        try { File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true); } catch { }
    }
    static string Key(int server, string name) => $"{server}:{name.ToLowerInvariant()}";

    /// <summary>
    /// The first time (no rankings.json yet): the ranking frames the app already kept in rare-frames.log, so the tab starts
    /// with the history of the last days instead of empty.
    /// </summary>
    public void ImportFrom(string rareLog)
    {
        lock (gate) if (books.Count > 0) return;
        try
        {
            foreach (var line in File.ReadLines(rareLog))
            {
                var parts = line.Split(' ', 4);
                if (parts.Length < 4 || parts[2] != "0x578D" || !DateTime.TryParse(parts[0] + " " + parts[1], out var when)) continue;
                try { Add(Convert.FromHexString(parts[3]), when); } catch { }
            }
        }
        catch { }
    }

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 60 || BinaryPrimitives.ReadUInt16BigEndian(f) != Opcode) return;
        if (Read(f) is not { } read) return;
        var (server, name, entry) = read;
        lock (gate)
        {
            if (!books.TryGetValue(Key(server, name), out var book)) books[Key(server, name)] = book = new();
            book.Last[$"{entry.Type}:{entry.List}"] = entry;
            // One row per hourly snapshot and list; the same snapshot seen again only refreshes it.
            int same = book.History.FindLastIndex(p => p.Type == entry.Type && p.List == entry.List && p.Snapshot == entry.Snapshot);
            var row = new Point(entry.Type, entry.List, entry.Rank, entry.Points, entry.Total, entry.Snapshot);
            if (same >= 0) book.History[same] = row; else book.History.Add(row);
            var mine = book.History.Where(p => p.Type == entry.Type && p.List == entry.List).ToList();
            if (mine.Count > HistoryPerList) foreach (var old in mine.Take(mine.Count - HistoryPerList)) book.History.Remove(old);
            Version++;
        }
        Save();
    }

    /// <summary>Decodes one 0x578D frame, or null when it does not read as one.</summary>
    public (int Server, string Name, Entry Entry)? Read(ReadOnlySpan<byte> f)
    {
        try
        {
            int type = f[4], list = f[5];
            int rank = BinaryPrimitives.ReadInt32LittleEndian(f[6..]);
            int server = BinaryPrimitives.ReadUInt16LittleEndian(f[16..]);
            int i = 18;
            string name = Text(f, ref i), legion = Text(f, ref i);
            i += 4 + 6 + 1; // u16, level u16, 6 bytes, u8
            long points = BinaryPrimitives.ReadInt64LittleEndian(f[i..]); i += 8;
            int length = Varint(f, ref i);
            if (length is < 2 or > 4000 || i + length + 28 > f.Length) return null;
            using var detail = JsonDocument.Parse(f.Slice(i, length).ToArray()); i += length;
            int change = BinaryPrimitives.ReadInt32LittleEndian(f[i..]); i += 4 + 12;
            int total = BinaryPrimitives.ReadInt32LittleEndian(f[i..]); i += 4;
            long stamp = BinaryPrimitives.ReadInt64LittleEndian(f[i..]);
            if (name.Length == 0 || rank < 0 || rank > 10_000_000 || total < 0 || points < 0 || server is < 1000 or > 9999) return null;
            if (stamp is < 1_700_000_000_000 or > 2_200_000_000_000) return null;
            var snapshot = DateTimeOffset.FromUnixTimeMilliseconds(stamp).LocalDateTime;
            return (server, name, new Entry(type, list, rank, change, points, total, snapshot, at == default ? DateTime.Now : at, detail.RootElement.Clone()));
        }
        catch { return null; }
    }
    static string Text(ReadOnlySpan<byte> f, ref int i)
    {
        int n = f[i];
        if (i + 1 + n > f.Length) throw new FormatException();
        string s = Encoding.UTF8.GetString(f.Slice(i + 1, n)); i += 1 + n; return s;
    }
    static int Varint(ReadOnlySpan<byte> f, ref int i)
    {
        int value = 0, shift = 0;
        while (true) { byte b = f[i++]; value |= (b & 0x7F) << shift; if ((b & 0x80) == 0) return value; shift += 7; if (shift > 21) throw new FormatException(); }
    }

    /// <summary>The Abyss places of a character (class, all classes) for the overlay's line.</summary>
    public (Entry? Class, Entry? All) Abyss(int? server, string? name)
    {
        if (server is not int s || string.IsNullOrEmpty(name)) return (null, null);
        lock (gate)
            return books.TryGetValue(Key(s, name), out var b) ? (b.Last.GetValueOrDefault("1:2"), b.Last.GetValueOrDefault("1:0")) : (null, null);
    }

    /// <summary>The Clasificación tab: every ranking of the character with both lists and their history.</summary>
    public object View(int? server, string? name)
    {
        Book? book = null;
        lock (gate)
        {
            if (server is int s && !string.IsNullOrEmpty(name)) books.TryGetValue(Key(s, name), out book);
            // Before the character is known: the one seen last.
            book ??= books.Values.OrderByDescending(b => b.Last.Values.Select(e => e.SeenAt).DefaultIfEmpty().Max()).FirstOrDefault();
            if (book is null) return new { character = name, rankings = Array.Empty<object>() };
            var types = book.Last.Values.Select(e => e.Type).Distinct().OrderBy(t => Array.IndexOf(Order, t) is int k && k >= 0 ? k : 99).ThenBy(t => t);
            return new
            {
                character = name,
                rankings = types.Select(t => new
                {
                    type = t,
                    @class = Shape(book.Last.GetValueOrDefault($"{t}:2")),
                    all = Shape(book.Last.GetValueOrDefault($"{t}:0")),
                    history = book.History.Where(p => p.Type == t).OrderBy(p => p.Snapshot).Select(p => new { list = p.List, rank = p.Rank, points = p.Points, total = p.Total, at = p.Snapshot }).ToList(),
                }).ToList(),
            };
        }
    }
    static readonly int[] Order = [1, 3, 4, 21, 5, 6];
    static object? Shape(Entry? e) => e is null ? null : new { rank = e.Rank, change = e.Change, points = e.Points, total = e.Total, snapshot = e.Snapshot, seen = e.SeenAt, detail = e.Detail };
}
