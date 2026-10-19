using System.Buffers.Binary;
using System.Diagnostics;

namespace FullAIon2Companion;

/// <summary>
/// The game's own ping. Every 10 s the client sends a 12-13 byte frame and the server answers with frame 0x0336:
/// <c>len | 03 36 | 00 00 | client send time (i64 LE)</c>. That time is the Windows performance counter in
/// milliseconds plus Unreal's 16,777,216 s offset, so the round trip is the counter now minus it (verified on a
/// 2026-10-11 capture: 53-56 ms, the figure the game and other meters show). The send/answer pair timed at
/// capture is the cross-check: when the two disagree by more than 25 ms twice in a row, the counter is not trusted.
/// The TCP round trip is no use here: the connection ends at a relay a few milliseconds away.
/// </summary>
public sealed class GamePing
{
    const long UnrealOffsetMs = 16_777_216L * 1000;
    static readonly byte[] Signature = [0x03, 0x36, 0x00, 0x00];
    readonly Queue<double> samples = new();
    readonly object gate = new();
    double? requestAt;
    int disagreements;
    bool counterDistrusted;
    double lastAnswer;
    static double NowMs => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>A client segment: the ping request is the only 12-13 byte one.</summary>
    public void ClientSent(int length) { if (length is 12 or 13) lock (gate) requestAt = NowMs; }
    /// <summary>A server segment: looks for the ping answer.</summary>
    public void ServerPayload(ReadOnlySpan<byte> payload)
    {
        int i = payload.IndexOf(Signature);
        if (i < 0 || i + 12 > payload.Length) return;
        double now = NowMs;
        double sent = BinaryPrimitives.ReadInt64LittleEndian(payload[(i + 4)..]) - UnrealOffsetMs;
        double? byCounter = now - sent is var rtt && rtt is >= 0 and < 5000 ? rtt : null;
        lock (gate)
        {
            double? byPair = requestAt is double at && now - at is >= 0 and < 5000 ? now - at : null;
            requestAt = null;
            if (byCounter is double c && byPair is double p)
            {
                disagreements = Math.Abs(c - p) > 25 ? disagreements + 1 : 0;
                if (disagreements >= 2) counterDistrusted = true;
            }
            double? value = counterDistrusted ? byPair : byCounter ?? byPair;
            if (value is not double v) return;
            samples.Enqueue(v); lastAnswer = now;
            while (samples.Count > 3) samples.Dequeue();
        }
    }
    /// <summary>Median of the last three answers (30 s), or null before the first.</summary>
    public int? Milliseconds
    {
        get
        {
            lock (gate)
            {
                // The answer comes every 10 s: none for 35 s means no live figure (loading screen, disconnected).
                if (samples.Count == 0 || NowMs - lastAnswer > 35_000) return null;
                var sorted = samples.Order().ToList();
                return (int)Math.Round(sorted[sorted.Count / 2]);
            }
        }
    }
}
