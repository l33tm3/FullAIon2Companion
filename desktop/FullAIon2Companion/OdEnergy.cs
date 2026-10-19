using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// Od energy, the currency dungeons are entered with, from the resource-state frames 0x0B61 / 0x0C61 (WaffleMeter's
/// 0x610B / 0x610C). Several resources travel together, so the Od entry is found by its signature
/// <c>[mask] 01 87 93 03</c> (resource key 01, Od group id) and the mask says which amounts follow, as varints:
/// 00 = none (0 and 0), 04 = natural only, 08 = additional only, 0C = natural then additional. Each frame is the
/// authoritative state and replaces the last one (2026-10-11: 185 (+100) at 15:10, 215 (+100) at 19:00 with mask 0C).
/// The game sends it only on changes, so the last value is kept on disk. The maximum (840) is a setting.
/// </summary>
public sealed class OdEnergy
{
    public sealed record State(int Current, int Bonus, DateTime At);
    State? state;
    static string FilePath => Path.Combine(Program.DataDirectory, "od-energy.json");
    public OdEnergy() { try { if (File.Exists(FilePath)) state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)); } catch { } }
    public State? Current => state;
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Parse);
    static ReadOnlySpan<byte> Signature => [0x01, 0x87, 0x93, 0x03];
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 8) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if (opcode is not (0x0B61 or 0x0C61)) return;
        for (int from = 2; from < f.Length;)
        {
            int at = f[from..].IndexOf(Signature);
            if (at < 0) return;
            at += from;
            from = at + 1;
            if (at < 1) continue;
            byte mask = f[at - 1];
            int p = at + Signature.Length;
            long natural = 0, extra = 0;
            switch (mask)
            {
                case 0x00: break;
                case 0x04: if (!Varint(f, ref p, out natural)) continue; break;
                case 0x08: if (!Varint(f, ref p, out extra)) continue; break;
                case 0x0C: if (!Varint(f, ref p, out natural) || !Varint(f, ref p, out extra)) continue; break;
                default: continue;
            }
            if (natural is < 0 or > 5000 || extra is < 0 or > 5000) continue;
            state = new((int)natural, (int)extra, DateTime.Now);
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(state)); } catch { }
            return;
        }
    }
    static bool Varint(ReadOnlySpan<byte> f, ref int p, out long value)
    {
        value = 0;
        for (int shift = 0; p < f.Length && shift <= 28; shift += 7) { byte b = f[p++]; value |= (long)(b & 127) << shift; if ((b & 128) == 0) return true; }
        return false;
    }
}
