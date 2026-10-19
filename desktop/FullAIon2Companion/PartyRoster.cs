using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The own party from the frames this client version uses (verified on three 2026-10-11 captures; the protocol
/// file's roster opcodes 0x0297/0x0197 never appear):
/// <list type="bullet">
/// <item>0x0092 <c>| 00 | party id (8) | ... | member entries</c>: the whole party, sent on party changes.</item>
/// <item>0x0A96 <c>| party id (8) | ... | one member entry</c>: a member joining. The same opcode lists members of
/// other parties too (party finder), so it only counts when the id is the own party's.</item>
/// </list>
/// A member entry is <c>24 | account GUID (36 ASCII) | 4 bytes | 00 00 | server (u16) | len | name</c>. Not used:
/// 0x2496 and 0x0D92 also name strangers. Nothing is sent periodically, so the last party is kept on disk.
/// </summary>
public sealed class PartyRoster
{
    const int FullList = 0x0092, Joined = 0x0A96, ExitParty = 0x1D97, MemberLeft = 0x2296, YouLeft = 0x0C96;
    // Frames documented by other Global parsers but not yet verified on our captures (member removal by roster key,
    // one-member update, room roster with gear score / combat power): kept raw in party-frames.log for differential
    // analysis, never interpreted on a guess.
    static readonly HashSet<int> Unverified = [0x1F97, 0x0297];
    /// <summary>Keys: each member's roster key (the u32 after its account GUID), which the leave frame names.</summary>
    public sealed record Saved(string? Id, string[] Names, Dictionary<uint, string>? Keys = null);
    readonly HashSet<string> names = new(StringComparer.Ordinal);
    readonly Dictionary<uint, string> keys = [];
    readonly object gate = new();
    readonly string localName;
    string? partyId;
    static string FilePath => Path.Combine(Program.DataDirectory, "party.json");
    public PartyRoster(string localName)
    {
        this.localName = localName;
        try
        {
            // A party saved hours ago is gone (a missed exit must not show strangers forever).
            if (File.Exists(FilePath) && DateTime.Now - File.GetLastWriteTime(FilePath) < TimeSpan.FromHours(3) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is { } saved)
            { partyId = saved.Id; names.UnionWith(saved.Names); foreach (var (k, n) in saved.Keys ?? []) keys[k] = n; }
        }
        catch { }
    }
    public IReadOnlySet<string> Names { get { lock (gate) return names.ToHashSet(StringComparer.Ordinal); } }
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Parse);
    void Parse(ReadOnlySpan<byte> frame)
    {
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(frame);
        if (Unverified.Contains(opcode)) { Log(opcode, frame); return; }
        if (opcode == MemberLeft && frame.Length >= 18)
        {
            // 22 96 | roster key u32 | 00 00 | server u16 | party id (8) - seen 2026-10-11 with our party's id. Missing
            // from a partial roster never means gone; this frame does.
            Log(opcode, frame);
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(frame[2..]);
            string leftParty = Convert.ToHexString(frame.Slice(10, 8));
            lock (gate)
            {
                if (leftParty != partyId || !keys.TryGetValue(key, out var gone)) return;
                names.Remove(gone); keys.Remove(key);
                SaveLocked();
            }
            return;
        }
        if (opcode == YouLeft && frame.Length == 4 && frame[2] == 0 && frame[3] == 0)
        {
            // 0C 96 00 00: the local player left the party or troop (2026-10-11 17:36:38, right after leaving).
            Log(opcode, frame);
            Forget();
            return;
        }
        if (opcode == ExitParty)
        {
            // The local player left the party (wire 1D 97, "ExitParty" in WaffleMeter): the whole party is gone.
            Log(opcode, frame);
            Forget();
            return;
        }
        if (opcode is not (FullList or Joined) || frame.Length < 60) return;
        string id = Convert.ToHexString(opcode == FullList ? frame.Slice(3, 8) : frame.Slice(2, 8));
        var found = new List<string>();
        var foundKeys = new Dictionary<uint, string>();
        for (int i = 2; i + 46 < frame.Length; i++)
            if (frame[i] == 0x24 && IsGuid(frame.Slice(i + 1, 36)) && Name(frame, i + 45) is { } name)
            { found.Add(name); foundKeys[BinaryPrimitives.ReadUInt32LittleEndian(frame[(i + 37)..])] = name; i += 36; }
        if (found.Count == 0) return;
        lock (gate)
        {
            if (opcode == FullList)
            {
                if (!found.Contains(localName, StringComparer.OrdinalIgnoreCase) || partyId == id && names.SetEquals(found)) return;
                partyId = id; names.Clear(); names.UnionWith(found); keys.Clear();
            }
            else
            {
                if (id != partyId || names.IsSupersetOf(found)) return;
                names.UnionWith(found);
            }
            foreach (var (k, n) in foundKeys) keys[k] = n;
            SaveLocked();
        }
    }
    void SaveLocked() { try { File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(partyId, names.Order().ToArray(), keys))); } catch { } }
    /// <summary>Drops the remembered party (the local player left it, or the user cleared a stale list).</summary>
    public void Forget() { lock (gate) { names.Clear(); keys.Clear(); partyId = null; SaveLocked(); } }
    static void Log(int opcode, ReadOnlySpan<byte> frame)
    {
        try
        {
            string path = Path.Combine(Program.DataDirectory, "party-frames.log");
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) return;
            File.AppendAllText(path, $"{DateTime.Now:O} 0x{opcode:X4} {Convert.ToHexString(frame[..Math.Min(frame.Length, 1024)])}{Environment.NewLine}");
        }
        catch { }
    }
    static bool IsGuid(ReadOnlySpan<byte> s)
    {
        for (int i = 0; i < 36; i++)
        {
            byte c = s[i];
            if (i is 8 or 13 or 18 or 23) { if (c != '-') return false; }
            else if (!(c is >= (byte)'0' and <= (byte)'9' || c is >= (byte)'A' and <= (byte)'F' || c is >= (byte)'a' and <= (byte)'f')) return false;
        }
        return true;
    }
    static string? Name(ReadOnlySpan<byte> frame, int at)
    {
        if (at >= frame.Length) return null;
        int length = frame[at];
        if (length is < 2 or > 36 || at + 1 + length > frame.Length) return null;
        var bytes = frame.Slice(at + 1, length);
        foreach (byte b in bytes) if (b < 0x20) return null;
        try { return new UTF8Encoding(false, true).GetString(bytes); } catch { return null; }
    }
}
