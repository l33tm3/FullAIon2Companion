using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// Your open Daevanion nodes as the game sends them (2026-10-24, the user: «me falta Ariel !!!»; NCSOFT's profile API gives
/// no Ariel yet). Frame 0x26E2 lists, for each board, the ids of the nodes you have open, the start node included; seen on
/// 2026-10-12 with Nezekan 57, Zikel 62, Vaizel 58, Triniel 71 and Azphel 36, the same open nodes as the official profile.
/// A node id is the board's id times 10,000 plus its cell in a 15 x 15 grid: (row - 1) * 15 + column.
/// <code>
/// 26 E2 | board count u8 | per board: board id u32 | node count u8 | node ids u32...
/// </code>
/// The counts are taken as one byte, and as a varint if that does not read the frame to its end.
/// </summary>
public sealed class DaevanionWatch
{
    const int Opcode = 0x26E2;
    readonly object gate = new();
    (DateTime At, IReadOnlyDictionary<int, int[]> Boards)? latest;
    DateTime at;
    /// <summary>Raises with every list.</summary>
    public int Version { get; private set; }
    public (DateTime At, IReadOnlyDictionary<int, int[]> Boards)? Latest { get { lock (gate) return latest; } }

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 8 || BinaryPrimitives.ReadUInt16BigEndian(f) != Opcode || Read(f) is not { } boards) return;
        lock (gate) { latest = (at, boards); Version++; }
    }

    public static Dictionary<int, int[]>? Read(ReadOnlySpan<byte> f) => Read(f, false) ?? Read(f, true);
    static Dictionary<int, int[]>? Read(ReadOnlySpan<byte> f, bool varints)
    {
        int p = 2;
        bool Count(ReadOnlySpan<byte> f, out int n)
        {
            n = 0;
            if (!varints) { if (p >= f.Length) return false; n = f[p++]; return true; }
            for (int shift = 0; p < f.Length && shift <= 14; shift += 7) { byte b = f[p++]; n |= (b & 127) << shift; if ((b & 128) == 0) return true; }
            return false;
        }
        if (!Count(f, out int boards) || boards is < 1 or > 32) return null;
        var result = new Dictionary<int, int[]>();
        for (int i = 0; i < boards; i++)
        {
            if (p + 4 > f.Length) return null;
            int board = BinaryPrimitives.ReadInt32LittleEndian(f[p..]); p += 4;
            if (board is < 1 or > 999 || !Count(f, out int count) || p + count * 4 > f.Length) return null;
            var nodes = new int[count];
            for (int k = 0; k < count; k++)
            {
                nodes[k] = BinaryPrimitives.ReadInt32LittleEndian(f[(p + k * 4)..]);
                if (nodes[k] / 10000 != board || nodes[k] % 10000 is < 1 or > 225) return null;
            }
            p += count * 4;
            result[board] = nodes;
        }
        return p == f.Length ? result : null;
    }
}

/// <summary>The last open nodes the game sent for each of your characters (daevanion-nodes.json), for «Mi perfil».</summary>
public static class DaevanionNodes
{
    public sealed record Entry(DateTime At, Dictionary<int, int[]> Boards);
    static readonly object gate = new();
    static Dictionary<string, Entry>? saved;
    static string FilePath => Path.Combine(Program.DataDirectory, "daevanion-nodes.json");
    static string Key(string name, int server) => $"{server}|{name.ToLowerInvariant()}";
    static Dictionary<string, Entry> All()
    {
        if (saved is not null) return saved;
        try { saved = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) : null; } catch { }
        return saved ??= [];
    }
    public static Entry? For(string name, int server) { lock (gate) return All().GetValueOrDefault(Key(name, server)); }
    public static void Save(string name, int server, DateTime at, IReadOnlyDictionary<int, int[]> boards)
    {
        lock (gate)
        {
            All()[Key(name, server)] = new(at, boards.ToDictionary(kv => kv.Key, kv => kv.Value));
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(saved)); } catch { }
        }
    }
}
