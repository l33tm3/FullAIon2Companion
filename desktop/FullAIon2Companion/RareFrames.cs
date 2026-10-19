using System.Buffers.Binary;
using System.IO;

namespace FullAIon2Companion;

/// <summary>
/// Keeps the infrequent frames of the live session (an opcode's first 50 per session) in rare-frames.log with the time.
/// State, currency, energy, pet and inventory updates travel in those, not in the combat stream; when the user reports
/// a value (Od energy 185/840, a pet soul), the frame carrying it can be found by its numbers afterwards.
/// </summary>
public sealed class RareFrames
{
    readonly Dictionary<int, int> seen = [];
    readonly string path = Path.Combine(Program.DataDirectory, "rare-frames.log");
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Note);
    void Note(ReadOnlySpan<byte> f)
    {
        if (f.Length < 2) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if (opcode == 0xffff || (seen[opcode] = seen.GetValueOrDefault(opcode) + 1) > 50) return;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 0x{opcode:X4} {Convert.ToHexString(f[..Math.Min(f.Length, 4096)])}{Environment.NewLine}");
        }
        catch { }
    }
}
