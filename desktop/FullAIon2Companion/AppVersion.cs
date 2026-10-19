using System.Reflection;

namespace FullAIon2Companion;

/// <summary>The build's own version (csproj &lt;Version&gt;), reported in diagnostics and compared with the published releases.</summary>
public static class AppVersion
{
    public static string Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0");
    // "0.1.0+commit" -> "0.1.0" (the API accepts 1.2.3 or 1.2.3-beta.1).
    static string Normalize(string v) => v.Split('+')[0];
    /// <summary>Semantic comparison: 0.2.0 &gt; 0.1.9; a release without a suffix beats its pre-release (1.0.0 &gt; 1.0.0-beta.2).</summary>
    public static bool IsNewer(string candidate, string current)
    {
        static (Version V, string Pre) Parse(string s)
        {
            var parts = s.Split('-', 2);
            return (Version.TryParse(parts[0], out var v) ? v : new Version(0, 0, 0), parts.Length > 1 ? parts[1] : "");
        }
        var (a, ap) = Parse(candidate); var (b, bp) = Parse(current);
        int c = a.CompareTo(b);
        if (c != 0) return c > 0;
        if (ap == bp) return false;
        if (ap.Length == 0) return true;
        if (bp.Length == 0) return false;
        return string.CompareOrdinal(ap, bp) > 0;
    }
}
