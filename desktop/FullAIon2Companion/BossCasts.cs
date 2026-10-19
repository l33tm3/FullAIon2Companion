using System.Buffers.Binary;
using AionDPS.Aion2.Protocol;

namespace FullAIon2Companion;
public sealed record CastRow(int CasterId, string Caster, int SkillId, string Skill, double Age, bool IsTarget, bool Elite, int CastMs = 0, int? NpcId = null);

/// <summary>
/// Monster skill casts from opcode 0x0938: <c>opcode | caster (varint) | flag | skill id (u32 LE) | 01 02 | target (varint) | heading, x, y, z | cast time ms (varint) | …</c>.
/// The flag is 02 for a mechanic (with a trailing <c>01 | varint 10000</c>), 00 for a monster's plain attack (2026-10-11: Kelpina's
/// Compression / Annihilation / Absorb Odyle and Barthien verdugo's three unnamed skills 1238740/50/60, 1.35/1.2/1.0 s, all 02).
/// It arrives when the cast starts, before any hit lands (verified on a Kelpina de onda azul capture, 2026-10-11:
/// 1502830 Smite Ground, 1502850 Spirit Wave), so it can warn ahead of the blow.
/// </summary>
public sealed class BossCasts
{
    const int Opcode = 0x0938;
    readonly List<(int Caster, int Skill, DateTime At, bool Mechanic, int CastMs)> recent = [];
    readonly Dictionary<(int Caster, int Skill), int> seen = [];
    DateTime at;
    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(frame) != Opcode) return;
        int p = 2; long caster = 0; int shift = 0;
        while (true) { if (p >= frame.Length || shift > 28) return; byte b = frame[p++]; caster |= (long)(b & 127) << shift; if ((b & 128) == 0) break; shift += 7; }
        if (caster <= 0 || p + 5 > frame.Length) return;
        bool mechanic = frame[p] == 2;
        int skill = BinaryPrimitives.ReadInt32LittleEndian(frame[(p + 1)..]);
        if (skill is < 100_000 or > 99_999_999) return;
        // Cast time: after 01 02 | target varint | four floats.
        int castMs = 0, q = p + 7;
        while (q < frame.Length && (frame[q] & 128) != 0) q++;
        q += 1 + 16;
        for (int sh = 0; q < frame.Length && sh <= 21; sh += 7) { byte b = frame[q++]; castMs |= (b & 127) << sh; if ((b & 128) == 0) break; }
        if (castMs is < 0 or > 60_000) castMs = 0;
        recent.Add(((int)caster, skill, at, mechanic, castMs));
        seen[((int)caster, skill)] = seen.GetValueOrDefault(((int)caster, skill)) + 1;
        if (recent.Count > 300) recent.RemoveRange(0, 100);
    }
    public static string NameOf(int skill)
    {
        string english = Aion2SkillNames.NameOf(skill);
        return english == skill.ToString() ? $"Habilidad {skill}" : Aion2SkillNames.Display(english);
    }
    /// <summary>Casts of the last <paramref name="window"/> by monsters (never players, skill helpers or plain auto-attacks), newest first.</summary>
    public IReadOnlyList<CastRow> Recent(DateTime now, TimeSpan window, Func<int, int?> npcOf, int target)
    {
        var rows = new List<CastRow>();
        for (int i = recent.Count - 1; i >= 0 && now - recent[i].At <= window; i--)
        {
            var (caster, skill, when, mechanic, castMs) = recent[i];
            // Unnamed skills ("Attack") count only when the game flags them as a mechanic: plain attacks never warn.
            bool unnamed = Aion2SkillNames.NameOf(skill) == "Attack";
            if (unnamed && !mechanic) continue;
            // A boss announced before the capture started has no NPC id yet; its casts still count while it is the target.
            int? npc = npcOf(caster);
            if (npc is null && caster != target || npc is int helper && Catalog.IsHelper(helper)) continue;
            string name = unnamed ? "Ataque del jefe" : NameOf(skill);
            rows.Add(new(caster, npc is int n ? Catalog.NpcName(n) : "Tu objetivo", skill, name, (now - when).TotalSeconds, caster == target, npc is int e && Catalog.IsElite(e), castMs, npc));
        }
        return rows;
    }
    public object Summary(Func<int, int?> npcOf) => seen.OrderByDescending(s => s.Value).Select(s => new
    {
        caster = s.Key.Caster, npc = npcOf(s.Key.Caster), skill = s.Key.Skill, name = NameOf(s.Key.Skill), count = s.Value
    });
}
