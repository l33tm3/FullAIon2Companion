using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FullAIon2Companion;

/// <summary>
/// Interface language for the WPF windows (overlay, boss panel, notifications, loot, player card, breakdown, minimap,
/// verification guide). The code writes Spanish; every visible text is looked up in data/i18n.json (exact phrases, from
/// i18n/panel.tsv and i18n/overlay.tsv) and, failing that, in data/i18n-patterns.tsv (phrases with numbers or names).
/// <see cref="ApplyAll"/> walks the open windows twice a second; the original Spanish is kept per element, so switching
/// back to Spanish restores it. The panel (WebView) has its own copy of the same mechanism in ui/app.js.
/// </summary>
public static class L
{
    public static string Language { get; set; } = "es";
    static readonly Dictionary<string, Dictionary<string, string>> exact = LoadExact();
    static readonly List<(Regex Re, string En, string Pt, string Fr)> patterns = LoadPatterns();
    // The text of the current language among English, Portuguese and French (Spanish never gets here).
    static string Pick(string en, string pt, string fr) => Language switch { "en" => en, "fr" => fr, _ => pt };
    sealed class Seen { public string Source = "", Output = ""; }
    static readonly ConditionalWeakTable<object, Seen> seen = new();

    static Dictionary<string, Dictionary<string, string>> LoadExact()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "i18n.json"))) ?? []; }
        catch { return []; }
    }
    static List<(Regex, string, string, string)> LoadPatterns()
    {
        var list = new List<(Regex, string, string, string)>();
        try
        {
            foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "data", "i18n-patterns.tsv")))
                if (line.Split('\t') is { Length: 3 or 4 } p) list.Add((new Regex(p[0], RegexOptions.Compiled), p[1], p[2], p.Length == 4 && p[3].Length > 0 ? p[3] : p[1]));
        }
        catch { }
        return list;
    }

    /// <summary>The text in the current language (Spanish when there is no translation).</summary>
    public static string T(string spanish)
    {
        if (Language == "es" || string.IsNullOrWhiteSpace(spanish)) return spanish;
        string key = spanish.Trim();
        if (exact.TryGetValue(Language, out var d) && d.TryGetValue(key, out var hit)) return spanish.Replace(key, hit);
        foreach (var (re, en, pt, fr) in patterns)
            if (re.IsMatch(key)) return Fragments(spanish.Replace(key, re.Replace(key, Pick(en, pt, fr))));
        Miss(key);
        return Fragments(spanish);
    }
    // Diagnostic: with FA2C_I18N_MISSES naming a file, each text without a translation is added to it once, so drawing the
    // windows in English or Portuguese (the --lang tests) lists what is left in Spanish. Names and figures turn up too.
    static readonly string? missFile = Environment.GetEnvironmentVariable("FA2C_I18N_MISSES");
    static readonly HashSet<string> missed = [];
    static void Miss(string key)
    {
        if (missFile is null || !missed.Add(key)) return;
        try { File.AppendAllText(missFile, key.ReplaceLineEndings(" | ") + Environment.NewLine); } catch { }
    }
    // Pieces that turn up inside composed lines ("Combate · 0:38 · Nv. 54 · Heroico", "… · ~272 más").
    static readonly (Regex Re, string En, string Pt, string Fr)[] fragments =
    [
        (new(@"\bNv\. (\d+)"), "Lv. $1", "Nv. $1", "Niv. $1"),
        (new(@"~(\d+) más\b"), "~$1 more", "~$1 mais", "~$1 de plus"),
        (new(@"\bHeroico\b"), "Heroic", "Heroico", "Héroïque"), (new(@"\bLegendario\b"), "Legendary", "Lendário", "Légendaire"),
        (new(@"\bÉlite\b"), "Elite", "Elite", "Élite"), (new(@"\bÚnico\b"), "Unique", "Único", "Unique"),
    ];
    static string Fragments(string text)
    {
        foreach (var (re, en, pt, fr) in fragments) text = re.Replace(text, Pick(en, pt, fr));
        return text;
    }

    /// <summary>Translates every open window except the panel (which is a web page).</summary>
    public static void ApplyAll(Window? except)
    {
        if (Application.Current is null) return;
        foreach (Window w in Application.Current.Windows) if (w != except && w.IsVisible) Apply(w);
    }
    internal static void Apply(DependencyObject node)
    {
        switch (node)
        {
            // Text made of runs (even one) keeps them: setting tb.Text would drop each run's own colour and size
            // (2026-10-13: a party row's white name became black - invisible - when it had no combat power after it).
            case TextBlock tb when tb.Inlines.Count > 1 || tb.Inlines.FirstInline is Run { } only && only.ReadLocalValue(System.Windows.Documents.TextElement.ForegroundProperty) != DependencyProperty.UnsetValue:
                // Over a copy: a run whose text changes can rebuild the collection under the enumeration (2026-10-19, the styles'
                // figures «1,7M daño» in English stopped the app with «Collection was modified»).
                foreach (var run in tb.Inlines.OfType<Run>().ToList()) { string text = Swap(run, run.Text); if (text != run.Text) run.Text = text; }
                break;
            case TextBlock tb:
                tb.Text = Swap(tb, tb.Text);
                break;
        }
        if (node is FrameworkElement fe && fe.ToolTip is string tip) fe.ToolTip = Swap(fe, tip, "tip");
        int n = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
        for (int i = 0; i < n; i++) Apply(VisualTreeHelper.GetChild(node, i));
        // Collapsed parts have no visual children yet; their logical children still need the language.
        if (n == 0) foreach (var child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject d) Apply(d);
    }
    static readonly ConditionalWeakTable<object, Seen> tips = new();
    static string Swap(object owner, string current, string kind = "")
    {
        var table = kind == "tip" ? tips : seen;
        var s = table.GetValue(owner, _ => new Seen { Source = current, Output = current });
        if (current != s.Output) s.Source = current; // the code wrote a new (Spanish) text
        s.Output = T(s.Source);
        return s.Output;
    }
}
