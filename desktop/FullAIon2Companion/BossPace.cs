using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FullAIon2Companion;

/// <summary>
/// The boss card: the boss and its health, two large figures - the time to kill and the time to enrage (the group's DPS
/// while the enrage is unknown) - a race bar of both, and one line that says whether the group gets there first. Used
/// inside the overlay and in <see cref="BossPaceWindow"/>. Redrawn on 2026-10-18 at the user's request: it had three
/// figures in a row with their labels beside them, and two dashes and a long note while the enrage was unknown.
/// </summary>
public sealed class BossPacePanel : Grid
{
    static readonly Brush Teal = Paint("#71DED6"), Muted = Paint("#95A4B6"), Alert = Paint("#FF8A65"), CellBack = Paint("#131E26"), Track = Paint("#22313C");
    readonly TextBlock title = Label("", 13, Brushes.White), hpPct = Label("", 20, Brushes.White), lost = Label("", 10, Muted),
        killLabel = Label("DERROTA EN", 10, Muted), kill = Label("", 20, Teal), sideLabel = Label("", 10, Muted), side = Label("", 20, Brushes.White),
        status = Label("", 10.5, Muted), pace = Label("", 10.5, Muted), bannerText = Label("", 10.5, Alert);
    readonly DockPanel head = new() { Margin = new Thickness(0, 0, 0, 2) };
    // The boss: its portrait in a ring of its grade's colour, and chips for the grade and level, the fight time and the race.
    readonly Border portrait = new() { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    readonly WrapPanel tags = new() { Margin = new Thickness(0, 2, 0, 0) };
    readonly Border gradeTag = Chip(), fightTag = Chip(), raceTag = Chip();
    readonly TextBlock gradeText = Label("", 10, Brushes.White), fightText = Label("", 10, Paint("#C9D4DD")), raceText = Label("", 10, Brushes.White);
    readonly Image raceIcon = new() { Height = 13, Margin = new Thickness(0, 0, 3, 0) };
    int? shownNpc;
    // Once enraged: a red strip on top, with how long it has been.
    readonly Border banner = new() { Background = Paint("#4A1B0C"), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed };
    // Health bar with its figures inside and a tick where the health will be at the enrage.
    readonly Grid bars = new() { Margin = new Thickness(0, 3, 0, 7), Visibility = Visibility.Collapsed };
    readonly Border hpFill = new() { Height = 18, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left,
        Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#E2473F"), (Color)ColorConverter.ConvertFromString("#A82420"), 90) };
    // A pale stretch of the health just lost, closing in over a few updates; a soft shine on the top half; marks every quarter.
    readonly Border hpLag = new() { Height = 18, CornerRadius = new CornerRadius(5), Background = Paint("#F0A39C"), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border hpShine = new() { Height = 9, CornerRadius = new CornerRadius(5, 5, 0, 0), Background = Paint("#1FFFFFFF"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    readonly Border[] quarters = [Quarter(), Quarter(), Quarter()];
    double lagRatio = 1; int lagEntity;
    readonly Border hpTick = new() { Width = 2, Height = 18, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock hpText = Label("", 10.5, Brushes.White);
    // The race: the time to kill and the time to enrage on one axis; the fill reaches whichever comes first.
    readonly Grid race = new() { Height = 14, Margin = new Thickness(0, 7, 0, 0), Visibility = Visibility.Collapsed };
    readonly Border raceFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    readonly Border killMark = Mark(), enrageMark = Mark();
    readonly DockPanel statusRow = new() { Margin = new Thickness(0, 5, 0, 0) };
    /// <summary>Show the health bar (the window; the overlay has its own health bar).</summary>
    public bool ShowBars { get; set; }

    public BossPacePanel(bool withTitle)
    {
        for (int r = 0; r < 6; r++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var t in new[] { title, hpPct, kill, side, bannerText, hpText }) t.FontWeight = FontWeights.SemiBold;
        banner.Child = bannerText;
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        hpPct.HorizontalAlignment = lost.HorizontalAlignment = HorizontalAlignment.Right; hpPct.Margin = new Thickness(0, -2, 0, 0);
        right.Children.Add(hpPct); right.Children.Add(lost);
        gradeTag.Child = gradeText; fightTag.Child = fightText;
        var raceRow = new StackPanel { Orientation = Orientation.Horizontal }; raceRow.Children.Add(raceIcon); raceRow.Children.Add(raceText); raceTag.Child = raceRow;
        fightTag.BorderBrush = raceTag.BorderBrush = Paint("#2E4A4F"); fightTag.BorderThickness = raceTag.BorderThickness = new Thickness(1);
        foreach (var tag in new[] { gradeTag, fightTag, raceTag }) tags.Children.Add(tag);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; names.Children.Add(title); names.Children.Add(tags);
        DockPanel.SetDock(portrait, Dock.Left); head.Children.Add(portrait);
        DockPanel.SetDock(right, Dock.Right); head.Children.Add(right); head.Children.Add(names);
        head.Visibility = withTitle ? Visibility.Visible : Visibility.Collapsed;
        bars.Children.Add(new Border { Height = 18, CornerRadius = new CornerRadius(5), Background = Track });
        bars.Children.Add(hpLag); bars.Children.Add(hpFill); bars.Children.Add(hpShine);
        foreach (var q in quarters) bars.Children.Add(q);
        bars.Children.Add(hpTick);
        hpText.Margin = new Thickness(8, 0, 8, 0); bars.Children.Add(hpText);
        bars.SizeChanged += (_, _) => Layout();
        race.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Track, VerticalAlignment = VerticalAlignment.Center });
        race.Children.Add(raceFill); race.Children.Add(killMark); race.Children.Add(enrageMark);
        race.SizeChanged += (_, _) => Layout();
        var cells = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        cells.Children.Add(Cell(killLabel, kill, new Thickness(0, 0, 3, 0)));
        cells.Children.Add(Cell(sideLabel, side, new Thickness(3, 0, 0, 0)));
        DockPanel.SetDock(pace, Dock.Right); pace.Margin = new Thickness(8, 0, 0, 0); statusRow.Children.Add(pace); statusRow.Children.Add(status);
        SetRow(banner, 0); SetRow(head, 1); SetRow(bars, 2); SetRow(cells, 3); SetRow(race, 4); SetRow(statusRow, 5);
        foreach (var e in new UIElement[] { banner, head, bars, cells, race, statusRow }) Children.Add(e);
    }
    TargetInfo? current;
    void Layout()
    {
        if (current is not { } t) return;
        if (t.Hp is long hp && t.HighestObservedHp is long max && max > 0 && bars.ActualWidth > 0)
        {
            double width = bars.ActualWidth, ratio = Math.Clamp((double)hp / max, 0, 1);
            hpFill.Width = hpShine.Width = width * ratio; hpLag.Width = width * Math.Max(ratio, lagRatio);
            for (int i = 0; i < quarters.Length; i++) quarters[i].Margin = new Thickness(width * (i + 1) / 4, 0, 0, 0);
            double at = t.HpAtEnragePct ?? 0;
            bool tick = !t.Enraged && t.EnrageIn is double until && until > 0 && at > 0 && at / 100 < ratio;
            hpTick.Visibility = tick ? Visibility.Visible : Visibility.Collapsed;
            hpTick.Margin = new Thickness(Math.Max(0, width * at / 100 - 1), 0, 0, 0);
        }
        if (race.ActualWidth > 0 && t.KillSeconds is double k && t.EnrageIn is double e && e > 0)
        {
            double width = race.ActualWidth, axis = Math.Max(k, e) * 1.12, kx = width * k / axis, ex = width * e / axis;
            raceFill.Width = Math.Max(0, Math.Min(kx, ex));
            killMark.Margin = new Thickness(Math.Max(0, kx - 1), 0, 0, 0);
            enrageMark.Margin = new Thickness(Math.Max(0, ex - 1), 0, 0, 0);
        }
    }
    static Border Mark() => new() { Width = 2, Height = 14, HorizontalAlignment = HorizontalAlignment.Left };
    static Border Quarter() => new() { Width = 1, Height = 18, Background = Paint("#990D141B"), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    static Border Chip() => new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 0, 4, 2), Visibility = Visibility.Collapsed };
    // The ring and the grade chip take the grade's colour; the race chip its crystal (the left end of the game's race badge).
    static readonly Dictionary<string, string> GradeColors = new() { ["EliteMonster"] = "#6FA8FF", ["HeroMonster"] = "#E9C77E", ["LegendMonster"] = "#FF9F43", ["UniqueMonster"] = "#C08BFF", ["BossMonster"] = "#FF6B6B" };
    internal static (string Name, string Color)? RaceOf(string? creature) => creature?.ToLowerInvariant() switch
    {
        "cognis" or "intelecto" or "intellect" or "cogni" => ("Cognis", "#6FA8FF"), "feras" or "salvaje" or "feral" or "fera" => ("Feras", "#FF6B6B"),
        "naturas" or "naturaleza" or "nature" or "natura" => ("Naturas", "#7BD88F"), "varians" or "transformación" or "transformacion" or "trans" or "varian" => ("Varians", "#F2C94C"),
        _ => null,
    };
    void ShowBoss(int? npc)
    {
        if (npc == shownNpc) return;
        shownNpc = npc;
        var record = npc is int id ? Catalog.Npc(id) : null;
        string? Text(string field) => record is { } r && r.TryGetProperty(field, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        var ring = Paint(Text("grade") is { } grade && GradeColors.TryGetValue(grade, out var hex) ? hex : "#95A4B6");
        var picture = OverlayWindow.Icon(Text("icon"));
        portrait.Background = picture is null ? null : new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
        portrait.BorderBrush = ring;
        portrait.Visibility = picture is null ? Visibility.Collapsed : Visibility.Visible;
        int? level = record is { } rl && rl.TryGetProperty("level", out var lv) && lv.ValueKind == System.Text.Json.JsonValueKind.Number ? lv.GetInt32() : null;
        gradeText.Text = string.Join(" · ", new[] { Text("gradeName"), level is int n ? $"Nv. {n}" : null }.Where(p => p is { Length: > 0 }));
        gradeText.Foreground = ring; gradeTag.Background = ring is SolidColorBrush solid ? new SolidColorBrush(Color.FromArgb(46, solid.Color.R, solid.Color.G, solid.Color.B)) : null;
        gradeTag.Visibility = gradeText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var race = RaceOf(Text("creature") ?? Text("category"));
        raceText.Text = race?.Name ?? ""; raceText.Foreground = Paint(race?.Color ?? "#95A4B6");
        raceIcon.Source = OverlayWindow.Icon(OverlayWindow.BadgeOf(race?.Name)) is BitmapSource badge && badge.PixelWidth >= 54
            ? new CroppedBitmap(badge, new Int32Rect(0, 0, 54, badge.PixelHeight)) : null;
        raceIcon.Visibility = raceIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
        raceTag.Visibility = race is null ? Visibility.Collapsed : Visibility.Visible;
    }
    static Border Cell(TextBlock label, TextBlock value, Thickness margin)
    {
        var stack = new StackPanel(); stack.Children.Add(label); stack.Children.Add(value);
        value.Margin = new Thickness(0, -1, 0, 0);
        return new Border { Background = CellBack, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4), Margin = margin, Child = stack };
    }
    /// <summary>Fills the card; false when there is nothing to show (no boss fight going on).</summary>
    public bool Update(TargetInfo? t)
    {
        if (t?.GroupDps is not double groupDps || groupDps <= 0) return false;
        current = t;
        double? enrageIn = !t.Enraged && t.EnrageIn is double until && until > 0 ? until : null;
        // Late: at this pace it dies after the enrage.
        bool late = enrageIn is double e0 && t.KillSeconds is double k0 && k0 > e0;
        bars.Visibility = ShowBars && t.Hp is long && t.HighestObservedHp > 0 ? Visibility.Visible : Visibility.Collapsed;
        hpText.Text = t.Hp is long hpNow && t.HighestObservedHp is long hpMax ? $"{Short(hpNow)} / {Short(hpMax)}" : "";
        title.Text = t.Name;
        hpPct.Text = t.Hp is long hp && t.HighestObservedHp is long max && max > 0 ? $"{(double)hp / max * 100:0.0}%" : "";
        lost.Text = t.Hp is long hpLeft && t.HighestObservedHp is long hpTop && hpTop > hpLeft ? $"{Short(hpTop - hpLeft)} perdidos" : "";
        lost.Visibility = lost.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowBoss(t.NpcId);
        fightText.Text = t.FightSeconds is double fought ? $"⚔ {Clock(fought)}" : "";
        fightTag.Visibility = fightText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // The trailing stretch: back to the health on another boss or when it heals, else it closes a third of the gap each time.
        double ratioNow = t.Hp is long h && t.HighestObservedHp is long top && top > 0 ? Math.Clamp((double)h / top, 0, 1) : 1;
        if (t.EntityId != lagEntity || ratioNow > lagRatio) { lagEntity = t.EntityId; lagRatio = ratioNow; }
        else lagRatio = Math.Max(ratioNow, lagRatio - Math.Max(.004, (lagRatio - ratioNow) * .35));
        banner.Visibility = t.Enraged ? Visibility.Visible : Visibility.Collapsed;
        bannerText.Text = t.EnragedFor is double since ? $"EN FURIA · desde hace {Clock(since)}" : "EN FURIA";
        kill.Text = t.KillSeconds is double killIn ? Clock(killIn) : "—";
        kill.Foreground = t.Enraged || late ? Alert : Teal;
        if (enrageIn is double left)
        {
            sideLabel.Text = "FURIA EN"; side.Text = Clock(left); side.Foreground = left < 30 ? Alert : Brushes.White;
        }
        else { sideLabel.Text = "DPS DEL GRUPO"; side.Text = Short(groupDps); side.Foreground = Brushes.White; }
        race.Visibility = enrageIn is not null && t.KillSeconds is not null ? Visibility.Visible : Visibility.Collapsed;
        raceFill.Background = killMark.Background = late ? Alert : Teal; enrageMark.Background = Brushes.White;
        pace.Text = "";
        if (t.Enraged) status.Text = "";
        else if (enrageIn is double untilEnrage)
        {
            if (t.RequiredDps is double needed && needed > 0) pace.Text = $"{Short(groupDps)} / {Short(needed)} DPS";
            if (late && t.RequiredDps is double need)
            {
                status.Text = $"Faltan {Short(Math.Max(0, need - groupDps))} de DPS · llegará con {t.HpAtEnragePct ?? 0:0} %"; status.Foreground = Alert;
            }
            else if (t.KillSeconds is double ahead) { status.Text = $"Llegáis {Gap(untilEnrage - ahead)} antes de la furia"; status.Foreground = Teal; }
            else status.Text = "";
        }
        else { status.Text = "Furia: aún no vista en este jefe"; status.Foreground = Muted; }
        statusRow.Visibility = status.Text.Length > 0 || pace.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Layout();
        return true;
    }
    static TextBlock Label(string text, double size, Brush color) => new() { Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static string Clock(double seconds) => seconds >= 3600 ? "+1 h" : $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    static string Gap(double seconds) => seconds < 60 ? $"{Math.Max(0, seconds):0} s" : Clock(seconds);
    static string Short(double v) => v >= 1_000_000 ? $"{v / 1_000_000:0.0}M" : v >= 10_000 ? $"{v / 1_000:0.#}k" : v.ToString("N0");
}

/// <summary>
/// The boss card on its own, by default just under the game's boss health bar (top centre of the game window). It never
/// takes the keyboard focus; drag it anywhere and it stays there (settings).
/// </summary>
public sealed class BossPaceWindow : Window
{
    readonly BossPacePanel panel = new(withTitle: true);
    public bool ShowBars { get => panel.ShowBars; set => panel.ShowBars = value; }
    readonly AppSettings settings;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    struct Rect32 { public int Left, Top, Right, Bottom; }
    public event Action? Shown;

    public BossPaceWindow(AppSettings settings)
    {
        this.settings = settings;
        Title = "FullAIon2Companion · Jefe"; Width = 360; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(205, 13, 20, 27)), BorderBrush = new SolidColorBrush(Color.FromArgb(200, 0x33, 0x57, 0x5B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Child = panel,
            Cursor = Cursors.SizeAll, ToolTip = "Arrastra para mover el panel del jefe",
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            DragMove();
            settings.BossPaceLeft = Math.Round(Left); settings.BossPaceTop = Math.Round(Top); settings.Save();
        };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
    }
    /// <summary>For the style previews: the card filled for a target, without showing the window.</summary>
    internal void Preview(TargetInfo t) => panel.Update(t);
    /// <summary>Shows the card for this target, or hides it outside a boss fight.</summary>
    public void Update(TargetInfo? t, bool enabled)
    {
        bool show = enabled && panel.Update(t);
        if (!show) { if (IsVisible) Hide(); return; }
        if (!IsVisible) { Show(); Place(); Shown?.Invoke(); }
    }
    // Where it was left; the first time, centred under the game's boss bar (about 12 % down the game window).
    void Place()
    {
        if (settings.BossPaceLeft is double l && settings.BossPaceTop is double tp) { Left = l; Top = tp; return; }
        UpdateLayout();
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice.M11 ?? 1;
        double left = 0, top = 0, width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight;
        var game = Process.GetProcessesByName("AION2").FirstOrDefault();
        if (game is not null)
            using (game)
                if (game.MainWindowHandle != IntPtr.Zero && GetWindowRect(game.MainWindowHandle, out var r) && r.Right > r.Left)
                { left = r.Left * scale; top = r.Top * scale; width = (r.Right - r.Left) * scale; height = (r.Bottom - r.Top) * scale; }
        Left = left + (width - ActualWidth) / 2;
        Top = top + height * .12;
    }
}
