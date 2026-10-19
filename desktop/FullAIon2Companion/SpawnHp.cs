using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDPS.Aion2;

namespace FullAIon2Companion;

/// <summary>
/// The exact maximum HP a creature is announced with. The spawn frame (0x4136) the decoder reads for the NPC id goes on
/// <c>| npc id (u32) | 2 bytes | x, y, z, heading (4 floats) | 3 bytes | current HP (varint) | max HP (varint)</c>, with the
/// owner-name block before the NPC id when there is one (verified 2026-10-11: Kelpina de onda azul entered at
/// 51,716,428 / 160,000,000). Without it the bar could only use the highest HP seen, wrong when joining a fight halfway.
/// </summary>
public sealed class SpawnHp
{
    const int Spawn = 0x4136;
    readonly Dictionary<int, (int Npc, long Current, long Max)> byEntity = [];
    public event Action<int, long>? NpcMaxSeen;
    // Every creature announced in this game session, kept on disk: the game announces a creature once, when it comes
    // into range, so restarting the app left everything already around as "Objetivo sin identificar".
    public sealed record Saved(string Session, Dictionary<int, long[]> Entities);
    static string FilePath => Path.Combine(Program.DataDirectory, "spawns-session.json");
    string? session;
    DateTime lastSave = DateTime.MinValue;
    bool dirty;
    readonly object gate = new();
    /// <summary>Puts this session's creatures back (NPC id and HP); fresh spawns still overwrite them.</summary>
    public void Restore(Aion2EntityDirectory entities, int localPort)
    {
        session = PlayerMemory.SessionKey(localPort);
        lock (gate)
        {
            byEntity.Clear();
            try
            {
                if (session is null || !File.Exists(FilePath) || JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is not { } saved || saved.Session != session) return;
                foreach (var (id, v) in saved.Entities)
                {
                    if (v.Length < 3) continue;
                    byEntity[id] = ((int)v[0], v[1], v[2]);
                    entities.RegisterNpc(id, (int)v[0]);
                }
            }
            catch { }
        }
    }
    /// <summary>Writes the session's creatures every 30 s at most (called from the snapshot).</summary>
    public void SaveIfDue()
    {
        lock (gate)
        {
            if (session is null || !dirty || DateTime.Now - lastSave < TimeSpan.FromSeconds(30)) return;
            dirty = false; lastSave = DateTime.Now;
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(session, byEntity.ToDictionary(kv => kv.Key, kv => new[] { (long)kv.Value.Npc, kv.Value.Current, kv.Value.Max })))); } catch { }
        }
    }
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Parse);
    public long? MaxOf(int entity) => byEntity.TryGetValue(entity, out var e) ? e.Max : null;
    /// <summary>The HP the creature was announced with: a full-health monster gets no HP updates until it is hit.</summary>
    public long? CurrentOf(int entity) => byEntity.TryGetValue(entity, out var e) ? e.Current : null;
    void Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 40 || BinaryPrimitives.ReadUInt16BigEndian(frame) != Spawn) return;
        int p = 2;
        if (!Varint(frame, ref p, out long entity) || p + 3 > frame.Length) return;
        // Same skip as Aion2FrameDecoder.DecodeNpcSpawn: two type bytes, a flag, and the owner's name when flagged.
        if (frame[p + 2] == 1 && p + 3 < frame.Length) p += 1 + frame[p + 3];
        p += 3;
        if (p + 25 > frame.Length) return;
        int npc = BinaryPrimitives.ReadInt32LittleEndian(frame[p..]);
        int q = p + 25;
        if (!Varint(frame, ref q, out long current) || !Varint(frame, ref q, out long max)) return;
        if (max < 1 || max > 100_000_000_000 || current < 0 || current > max) return;
        lock (gate)
        {
            byEntity[(int)entity] = (npc, current, max); dirty = true;
            if (byEntity.Count > 20_000) byEntity.Clear();
        }
        NpcMaxSeen?.Invoke(npc, max);
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
