using System.Buffers.Binary;

namespace FullAIon2Companion;

/// <summary>
/// The player's own position from the combat stream. The game's own position report (0x3C37) comes only on teleports
/// and corrections, so the minimap fell back to the middle of the creatures around (tens of metres off). Three frames
/// carry the player's place far more often (verified 2026-10-11 against each other, within half a metre):
/// <list type="bullet">
/// <item>0x2A38, a buff the player put on himself: <c>| target | … | caster | … | x, y, z</c>, the position closing the frame;</item>
/// <item>0x0938, a monster's skill aimed at the player: <c>| caster | 00 | skill u32 | seq | 02 | target | heading | x, y, z</c>;</item>
/// <item>0x0238, the player's skill aimed at himself, same layout (kind 00 has no position; small floats are a direction).</item>
/// </list>
/// While he only walks the server sends none of them, and what the game sends about his movement is encrypted: checked
/// 2026-10-16 with <c>--position-scan</c> over 13 recordings (docs/own-position-research.md). 0x2B38, the update of an own buff,
/// also ends with his place but adds positions only where these already give some, so it is left out.
/// </summary>
public sealed class OwnPosition
{
    const int Buff = 0x2A38, OwnSkill = 0x0238, MonsterSkill = 0x0938, MapLoad = 0x2136, Arrival = 0x2336;
    readonly object gate = new();
    (float X, float Y, DateTime At)? last;
    /// <summary>The local player's combat id (0 while unknown), refreshed by the snapshot.</summary>
    public int LocalId { get; set; }
    public (float X, float Y, DateTime At)? Last { get { lock (gate) return last; } }
    /// <summary>The world (map) id of the last map load (0x2136 <c>| counter u32 | world u32 | …</c>: 1110 most of 2026-10-11, 600022 a dungeon), and how many loads so far.</summary>
    public int? World { get; private set; } = LoadWorld();
    // The last world survives a restart of the app (a map load is the only frame that names it).
    static string WorldPath => System.IO.Path.Combine(Program.DataDirectory, "last-world.txt");
    static int? LoadWorld() { try { return int.TryParse(System.IO.File.ReadAllText(WorldPath), out int w) ? w : null; } catch { return null; } }
    public int Loads { get; private set; }
    /// <summary>A new game connection (instance or zone transfer): its map load may have gone before the capture moved to
    /// it (2026-10-12: leaving the Abyss left the minimap on Reshanta), so the world is unknown until the next one.</summary>
    public void ForgetWorld() { World = null; Loads++; }
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Parse);
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 16) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f), p = 2;
        if (opcode == MapLoad && f.Length >= 10) { int world = BinaryPrimitives.ReadInt32LittleEndian(f[6..]); if (world > 0) { World = world; Loads++; try { System.IO.File.WriteAllText(WorldPath, world.ToString()); } catch { } } return; }
        // Arriving after a teleport or a map load: | 8 zero bytes | x, y, z | … - the player's own place.
        if (opcode == Arrival && f.Length >= 22 && BinaryPrimitives.ReadInt64LittleEndian(f[2..]) == 0) { Note(f[10..], opcode); return; }
        int local = LocalId;
        if (local <= 0) return;
        if (!Varint(f, ref p, out long first)) return;
        if (opcode == Buff)
        {
            // Target and caster both the player; the caster id sits just before its short block and the position.
            if (first != local || f.Length < 30 || !Contains(f[(p + 1)..^12], local)) return;
            Note(f[^12..], opcode);
        }
        else if (opcode is OwnSkill or MonsterSkill)
        {
            if (opcode == OwnSkill && first != local || opcode == MonsterSkill && first == local) return;
            int q = p + 1 + 4 + 1;
            if (q + 1 > f.Length || f[q] != 2) return;
            q++;
            if (!Varint(f, ref q, out long target) || target != local || q + 16 > f.Length) return;
            Note(f[(q + 4)..], opcode);
        }
    }
    /// <summary>The opcode of the frame that gave the last position (diagnostics: <c>--position-scan</c>).</summary>
    public int LastOpcode { get; private set; }
    /// <summary>How many positions were noted (diagnostics).</summary>
    public int Notes { get; private set; }
    void Note(ReadOnlySpan<byte> xyz, int opcode)
    {
        float x = BinaryPrimitives.ReadSingleLittleEndian(xyz), y = BinaryPrimitives.ReadSingleLittleEndian(xyz[4..]);
        // World coordinates are large; a direction vector or an offset is not.
        if (!float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000 || Math.Abs(x) + Math.Abs(y) < 2000) return;
        lock (gate) last = (x, y, DateTime.UtcNow);
        LastOpcode = opcode; Notes++;
        if (xyz.Length >= 12 && BinaryPrimitives.ReadSingleLittleEndian(xyz[8..]) is float z && float.IsFinite(z) && Math.Abs(z) < 1_000_000) LastZ = z;
    }
    /// <summary>The player's height (world units) from the last own position.</summary>
    public float? LastZ { get; private set; }
    static bool Contains(ReadOnlySpan<byte> span, int id)
    {
        Span<byte> encoded = stackalloc byte[5];
        int n = 0;
        for (uint v = (uint)id; ; v >>= 7)
        {
            encoded[n++] = (byte)(v < 128 ? v : (v & 127) | 128);
            if (v < 128) break;
        }
        return span.IndexOf(encoded[..n]) >= 0;
    }
    static bool Varint(ReadOnlySpan<byte> frame, ref int p, out long value)
    {
        value = 0;
        for (int shift = 0; p < frame.Length && shift <= 56; shift += 7)
        {
            byte b = frame[p++];
            value |= (long)(b & 127) << shift;
            if ((b & 128) == 0) return true;
        }
        return false;
    }
}
