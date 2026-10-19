using System.Buffers.Binary;
using AionDPS.Combat;

namespace FullAIon2Companion;

/// <summary>What the damage frame says beyond the amount: the cast's sequence byte (hits of one cast share it), how many
/// hits the line folds (2+ = a multi-hit), the two flag bytes and the hit type (3 = critical).</summary>
/// <summary>Mod: the first byte of the longer block (flags 1 bit 0x02), a bit set per hit: 0x80 on 78 % of a melee player's hits
/// behind a boss (back attack, likely), 0x04 and 0x08 rarer (perfect / double, to be confirmed with the game's analyzer).</summary>
public readonly record struct HitExtra(byte Sequence, int Count, byte Flags1, byte Flags2, byte Type, byte Mod = 0);

/// <summary>
/// The damage frame 0x0438 read a second time for the breakdown window (the decoder keeps only amount, skill and critical):
/// <c>opcode | target (varint) | flags 1 | flags 2 | actor (varint) | skill u32 | sequence | hit type | block | count u32 | 2 bytes | amount (varint) | …</c>.
/// Matched to the decoder's hit by time, skill and amount. Bit 0x02 of flags 1 comes with a 3-byte longer block and is
/// set on 62 % of a melee player's hits (2026-10-11 capture): most likely the back attack, still to be confirmed against
/// the game's own analyzer.
/// </summary>
public sealed class HitDetails
{
    const int Damage = 0x0438;
    readonly Dictionary<(long Ticks, int Skill, long Amount), HitExtra> recent = [];
    readonly Queue<(long, int, long)> order = new();
    DateTime at;
    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    public HitExtra? Of(DamageEvent hit) => recent.TryGetValue((hit.Timestamp.Ticks, hit.SkillId, hit.Amount), out var e) ? e : null;
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 16 || BinaryPrimitives.ReadUInt16BigEndian(f) != Damage) return;
        int p = 2;
        if (!Varint(f, ref p, out _) || p + 2 > f.Length) return;
        byte flags1 = f[p], flags2 = f[p + 1];
        p += 2;
        if ((flags1 & 0x04) == 0 || !Varint(f, ref p, out _) || p + 6 > f.Length) return;
        int skill = BinaryPrimitives.ReadInt32LittleEndian(f[p..]);
        byte sequence = f[p + 4], type = f[p + 5];
        int marker = -1;
        for (int i = p + 6; i + 4 <= f.Length; i++)
            if (f[i] is >= 1 and <= 9 && f[i + 1] == 0 && f[i + 2] == 0 && f[i + 3] == 0) { marker = i; break; }
        int q = marker + 6;
        if (marker < 0 || q >= f.Length || !Varint(f, ref q, out long amount) || amount <= 0) return;
        var key = (at.Ticks, skill, amount);
        recent[key] = new(sequence, f[marker], flags1, flags2, type, (flags1 & 0x02) != 0 && marker > p + 6 ? f[p + 6] : (byte)0);
        order.Enqueue(key);
        while (order.Count > 4000) recent.Remove(order.Dequeue());
    }
    static bool Varint(ReadOnlySpan<byte> f, ref int p, out long value)
    {
        value = 0;
        for (int shift = 0; p < f.Length && shift <= 56; shift += 7)
        {
            byte b = f[p++];
            value |= (long)(b & 127) << shift;
            if ((b & 128) == 0) return true;
        }
        return false;
    }
}
