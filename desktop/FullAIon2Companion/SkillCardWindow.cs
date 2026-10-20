using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FullAIon2Companion;

/// <summary>
/// The text of each class skill at each level (data/skill-texts.json, made by scripts/build-companion-skill-texts.py from the
/// website's skill tables, in Spanish). A level whose text is the one before is stored as "".
/// </summary>
public static class SkillTexts
{
    static Dictionary<int, string[]>? texts;
    static Dictionary<int, string[]> All()
    {
        if (texts is not null) return texts;
        var map = new Dictionary<int, string[]>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "skill-texts.json")));
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, out int id)) map[id] = p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        }
        catch { }
        return texts = map;
    }
    /// <summary>The skill's text at that level (the highest level's when the level is unknown), or null.</summary>
    public static string? Of(int skill, int? level)
    {
        if (!All().TryGetValue(skill, out var levels) || levels.Length == 0) return null;
        int i = Math.Clamp((level ?? levels.Length) - 1, 0, levels.Length - 1);
        for (; i >= 0; i--) if (levels[i].Length > 0) return levels[i];
        return null;
    }
}

/// <summary>
/// The card the combat assistant shows while the mouse rests on one of its icons (2026-10-19, the user: «pasar el mouse y que
/// diga los buffos que tengo, algo similar al drop», with the game's own tooltip as the model): the skill, its level from the
/// official profile, the time left and the skill's text at that level, its figures highlighted as the game does. The bar stays
/// click-through, so the game keeps every click: the assistant follows the cursor and this window never takes the mouse.
/// </summary>
public sealed class SkillCardWindow : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    // The overlay's default tones: the style of the overlay recolours them (OverlayWindow.ThemeWindow).
    static readonly Brush Accent = Paint("#71DED6"), Gold = Paint("#E9C77E"), Muted = Paint("#95A4B6"), Dim = Paint("#5F7480"), Bright = Paint("#D5DEE6"),
        Lift = Paint("#2B3C48"), Frame = Paint("#33575B");
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    // The parts that Tick updates: new ones for every card, because an element can only sit in one panel and the
    // previous card still holds the old ones (reusing them threw «ya es el elemento secundario lógico de otro elemento»).
    TextBlock state; Grid track; Border fill;
    static (TextBlock State, Grid Track, Border Fill) Parts() =>
        (new TextBlock { FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap },
         new Grid { Height = 3, Margin = new Thickness(0, 8, 0, 0) },
         new Border { Height = 3, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) });
    /// <summary>The skill on show (0 = none) and its level, to rebuild the card only when the skill changes.</summary>
    public int Skill { get; private set; }
    int? level;
    const double CardWidth = 300;

    public SkillCardWindow()
    {
        (state, track, fill) = Parts();
        Title = "FullAIon2Companion · Habilidad"; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false; Focusable = false; IsHitTestVisible = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        // Never takes the mouse or the focus: transparent to clicks, no activation, no taskbar button.
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000); };
    }

    /// <summary>Builds the card for this tile (level from the official profile, null while unknown).</summary>
    public void Load(TimerTile tile, int? skillLevel, double scale)
    {
        Skill = tile.Skill; level = skillLevel;
        (state, track, fill) = Parts();
        Content = Card(tile, skillLevel, scale, state, track, fill);
        Tick(tile);
    }
    /// <summary>The countdown and the bar, every refresh while the card shows.</summary>
    public void Tick(TimerTile tile) => Fill(tile, state, track, fill);
    public void Clear() { Skill = 0; level = null; if (IsVisible) Hide(); }

    static string Secs(double s) => s >= 60 ? $"{(int)(s / 60)}:{(int)Math.Ceiling(s % 60) % 60:00}" : s >= 10 ? $"{Math.Ceiling(s)} s" : $"{s:0.0} s";
    static void Fill(TimerTile t, TextBlock state, Grid track, Border fill)
    {
        double ratio = t.Total > 0 ? Math.Clamp(t.Left / t.Total, 0, 1) : 0;
        if (t.Buff) { state.Text = L.T($"Efecto activo · quedan {Secs(t.Left)} de {Secs(t.Total)}"); state.Foreground = t.Left < 3 ? Gold : Accent; fill.Background = Accent; }
        else if (t.Left > 0) { state.Text = L.T($"En recarga · faltan {Secs(t.Left)} de {Secs(t.Total)}"); state.Foreground = Muted; fill.Background = Muted; }
        else { state.Text = L.T($"Lista para usar · recarga de {Secs(t.Total)}"); state.Foreground = Gold; fill.Background = Gold; ratio = 1; }
        // Buffs: what is left; cooldowns: how far along they are.
        fill.Width = CardWidth * (t.Buff ? ratio : 1 - ratio);
    }

    static FrameworkElement Card(TimerTile t, int? level, double scale, TextBlock state, Grid track, Border fill)
    {
        var stack = new StackPanel { Width = CardWidth };
        // Head: icon, name and level, and the time left.
        var head = new DockPanel();
        var icon = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.5), BorderBrush = t.Buff ? Accent : Frame,
            Margin = new Thickness(0, 0, 10, 0), ClipToBounds = true, Child = new Image { Source = OverlayWindow.Icon(t.Icon), Stretch = Stretch.UniformToFill } };
        icon.Clip = new RectangleGeometry(new Rect(0, 0, 38, 38), 6, 6);
        DockPanel.SetDock(icon, Dock.Left); head.Children.Add(icon);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Bright, TextWrapping = TextWrapping.Wrap };
        name.Inlines.Add(new Run(t.Name));
        if (level is int lv) name.Inlines.Add(new Run("  " + L.T($"Nv. {lv}")) { Foreground = Gold, FontSize = 12 });
        titles.Children.Add(name);
        titles.Children.Add(state);
        head.Children.Add(titles);
        stack.Children.Add(head);
        track.Children.Add(new Border { Background = Lift, CornerRadius = new CornerRadius(2) });
        track.Children.Add(fill);
        stack.Children.Add(track);
        // The skill's text at the player's level, in Spanish (the only language of the tables): figures as in the game.
        string? text = L.Language == "es" ? SkillTexts.Of(t.Skill, level) : null;
        if (text is not null)
        {
            stack.Children.Add(new Border { Height = 1, Background = Lift, Margin = new Thickness(0, 10, 0, 8) });
            bool gap = false;
            // «9260-9260»: a range with both ends equal is one figure, as the game writes it.
            text = Same.Replace(text, "$1");
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) { gap = true; continue; }
                var block = new TextBlock { FontSize = 12, Foreground = Bright, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, gap ? 7 : 1, 0, 1) };
                foreach (var run in Runs(line)) block.Inlines.Add(run);
                stack.Children.Add(block);
                gap = false;
            }
            if (level is null)
                stack.Children.Add(new TextBlock { Text = "Texto del nivel máximo: tu nivel aún no llegó del perfil oficial", FontSize = 10, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        }
        return new Border
        {
            Background = Paint("#F50B1117"), BorderBrush = t.Buff ? Accent : Frame, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(13, 11, 13, 12), Child = stack, Margin = new Thickness(8),
            LayoutTransform = new ScaleTransform(scale, scale), Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .55, Color = Colors.Black },
        };
    }
    // «**17011-17011**» is the figure that grows with the level (bold, as in the tables); other figures and their units
    // (20 %, 100, 7.5m, 20s) are highlighted too, like the game's green numbers.
    static readonly Regex Same = new(@"\b(\d+(?:[.,]\d+)?)-\1\b"), Bold = new(@"\*\*(.+?)\*\*"), Figure = new(@"(?<![\p{L}\d])(\d+(?:[.,]\d+)?(?:-\d+(?:[.,]\d+)?)?(?:\s?%|s\b|m\b)?)");
    static IEnumerable<Inline> Runs(string line)
    {
        int at = 0;
        foreach (Match b in Bold.Matches(line))
        {
            foreach (var r in Plain(line[at..b.Index])) yield return r;
            yield return new Run(b.Groups[1].Value) { Foreground = Accent, FontWeight = FontWeights.SemiBold };
            at = b.Index + b.Length;
        }
        foreach (var r in Plain(line[at..])) yield return r;
    }
    static IEnumerable<Inline> Plain(string text)
    {
        int at = 0;
        foreach (Match m in Figure.Matches(text))
        {
            if (m.Index > at) yield return new Run(text[at..m.Index]);
            yield return new Run(m.Value) { Foreground = Accent };
            at = m.Index + m.Length;
        }
        if (at < text.Length) yield return new Run(text[at..]);
    }

    /// <summary>For the test: the card as drawn, out of any window.</summary>
    internal static FrameworkElement Preview(TimerTile t, int? level, double scale = 1)
    {
        var (state, track, fill) = Parts();
        var card = Card(t, level, scale, state, track, fill);
        Fill(t, state, track, fill);
        return card;
    }
}
