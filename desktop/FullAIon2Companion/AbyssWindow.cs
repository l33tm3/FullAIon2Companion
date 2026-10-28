using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FullAIon2Companion;

/// <summary>
/// The Abyss card, opened from the overlay strip's medal: the places in the Abyss ranking (class and all classes, as the game
/// last said them), the ranking points and the Abyss Points now (<see cref="AbyssWatch"/>), and the session farming there:
/// points won and lost, deaths, Abyss Points, kinah and items, with their hourly rates and «Reiniciar». Beside the overlay
/// under the loot card until it is dragged by its header somewhere else (a double click sends it back), and its corner makes
/// it bigger or smaller (the user, 2026-10-26: «la idea es que se pueda mover y hacer más grande»). Takes the mouse, never
/// the keyboard focus.
/// </summary>
public sealed class AbyssWindow : Window
{
    static readonly Brush Muted = Paint("#95A4B6"), Gold = Paint("#E9C77E"), Teal = Paint("#71DED6"), Dim = Paint("#5F7480"),
        Up = Paint("#5FE0A0"), Down = Paint("#FF8A65"), Track = Paint("#22313C"), Line = Paint("#1F2C31");
    readonly StackPanel body = new();
    string shownKey = "";
    double scale = 1;
    /// <summary>The card was dragged to a new place, or a double click on its header sent it back beside the overlay (null).</summary>
    public event Action<Point?>? Placed;
    /// <summary>The corner was dragged: the card's own size (1 = as drawn).</summary>
    public event Action<double>? Resized;
    /// <summary>While the header is being dragged: the overlay leaves the card where the mouse takes it.</summary>
    public bool Dragging { get; private set; }
    // The resize corner, as the overlay's. Out of the body, which is rebuilt while the corner may be in the middle of a drag.
    readonly System.Windows.Controls.Primitives.Thumb corner = new() { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, -10, -8), Cursor = Cursors.SizeNWSE, ToolTip = "Arrastra para cambiar el tamaño de la ficha" };
    /// <summary>«Reiniciar»: a new session (set by the overlay).</summary>
    public Action? ResetRequested { get; set; }
    /// <summary>The × of the card.</summary>
    public event Action? CloseRequested;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public AbyssWindow(double opacity)
    {
        Title = "FullAIon2Companion · Abismo"; Width = 300; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        byte alpha = (byte)Math.Clamp(opacity * 255, 90, 250);
        var mark = new FrameworkElementFactory(typeof(TextBlock));
        mark.SetValue(TextBlock.TextProperty, "◢"); mark.SetValue(TextBlock.ForegroundProperty, Teal); mark.SetValue(TextBlock.FontSizeProperty, 13.0);
        mark.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right); mark.SetValue(VerticalAlignmentProperty, VerticalAlignment.Bottom);
        corner.Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = mark };
        // The corner moves inside the scaled card: its change times the size is the mouse's own, so the corner stays under it.
        corner.DragDelta += (_, e) => Scale(scale + e.HorizontalChange * scale / 300);
        corner.DragCompleted += (_, _) => Resized?.Invoke(scale);
        var face = new Grid(); face.Children.Add(body); face.Children.Add(corner);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(alpha, 13, 20, 27)), BorderBrush = new SolidColorBrush(Color.FromArgb(200, 51, 87, 91)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), Child = face };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
    }
    public void ApplyAppearance(byte alpha, double k)
    {
        ((Border)Content).Background = new SolidColorBrush(Color.FromArgb(alpha, 13, 20, 27));
        Scale(k);
    }
    void Scale(double k)
    {
        scale = Math.Clamp(k, .7, 2.2);
        ((Border)Content).LayoutTransform = new ScaleTransform(scale, scale); Width = 300 * scale;
    }

    /// <summary>Draws the card when something on it changed, and says whether it did (the overlay then gives it its style at
    /// once). Not while a button of the card is held down: a rebuild would swallow the click.</summary>
    public bool Render(AbyssWatch.View v, RankingWatch.Entry? mine, RankingWatch.Entry? all, DateTime now)
    {
        var s = v.Session;
        string key = $"{L.Language}|{v.Rank}|{v.RankLive}|{v.Held}|{v.Earned}|{v.Cap}|{mine?.Rank}|{mine?.Change}|{mine?.SeenAt}|{all?.Rank}|{all?.Change}|{all?.SeenAt}|{now:HH:mm}"
            + (s is null ? "" : $"|{s.Active:0}|{s.RankGained}|{s.RankLost}|{s.Deaths}|{s.ApGained}|{s.Kinah}|{string.Join(",", s.Items.Select(i => $"{i.Key}:{i.Value}"))}");
        if (key == shownKey || IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed) return false;
        shownKey = key;
        body.Children.Clear();
        body.Children.Add(Head());

        // The ranking: the places as the ranking window last showed them, the points live when the server sent them.
        body.Children.Add(Section("CLASIFICACIÓN"));
        var rows = Grid2();
        Run Rest(RankingWatch.Entry e) => new("  " + $"de {e.Total:N0} · top {Math.Max(0.1, e.Rank * 100.0 / Math.Max(1, e.Total)):0.0} %") { Foreground = Muted };
        if (mine is not null) Row(rows, "Tu clase", Strong($"#{mine.Rank:N0}"), Change(mine.Change), Rest(mine));
        if (all is not null) Row(rows, "Todas las clases", Strong($"#{all.Rank:N0}"), Change(all.Change), Rest(all));
        // The points the server sent in this login, else the newest of the ones kept and the ranking window's.
        var latest = all is not null && (mine is null || all.SeenAt > mine.SeenAt) ? all : mine;
        long? points = v.Rank is long kept && (v.RankLive || latest is null || v.RankAt > latest.SeenAt) ? kept : latest?.Points ?? v.Rank;
        if (points is long p) Row(rows, "Puntos de clasificación", Strong($"{p:N0}"));
        body.Children.Add(rows);
        string when = latest is null ? "Abre la clasificación del Abismo en el juego para ver tu puesto"
            : now >= latest.Snapshot ? $"Puesto de las {latest.SeenAt:HH:mm}: ábrela de nuevo en el juego para renovarlo"
            : $"Puesto visto {latest.SeenAt:HH:mm} · se renueva {latest.Snapshot:HH:mm}";
        body.Children.Add(Note(when));

        // The Abyss Points: held (the inventory) and earned against their cap (0xA9FF).
        if (v.Held is not null || v.Earned is not null)
        {
            body.Children.Add(Section("PUNTOS DE ABISMO"));
            var ap = Grid2();
            if (v.Held is long held) Row(ap, "Tienes", Strong($"{held:N0}"));
            if (v.Earned is long earned && v.Cap is long cap) Row(ap, "Obtenidos", Strong($"{earned:N0}"), new Run($" / {cap:N0}") { Foreground = Muted });
            body.Children.Add(ap);
            if (v.Earned is long e2 && v.Cap is long c2 && c2 > 0) body.Children.Add(Bar(Math.Clamp(e2 / (double)c2, 0, 1)));
        }

        // The session farming in the Abyss.
        var head = new DockPanel { Margin = new Thickness(0, 9, 0, 3) };
        var reset = new Button { Content = new TextBlock { Text = "Reiniciar", FontSize = 10.5, Foreground = Teal }, Padding = new Thickness(7, 1, 7, 2), Cursor = Cursors.Hand, Focusable = false,
            Background = Paint("#2213242C"), BorderBrush = Paint("#33575B"), BorderThickness = new Thickness(1), ToolTip = "Empieza una sesión nueva: la próxima recompensa del Abismo la abre", Visibility = s is null ? Visibility.Collapsed : Visibility.Visible };
        reset.Click += (_, _) => { ResetRequested?.Invoke(); shownKey = ""; };
        DockPanel.SetDock(reset, Dock.Right); head.Children.Add(reset);
        var title = Label("ESTA SESIÓN EN EL ABISMO", 10, Teal); title.FontWeight = FontWeights.SemiBold; head.Children.Add(title);
        body.Children.Add(head);
        if (s is null) body.Children.Add(Note("Empieza sola con tu primera recompensa del Abismo: una baja, una misión o un objetivo."));
        else
        {
            double hours = Math.Max(s.Active, 60) / 3600; active = s.Active;
            var g = Grid2();
            Row(g, "Tiempo farmeando", Strong(Span(s.Active)), new Run($"  desde las {s.Start:HH:mm}") { Foreground = Muted });
            Row(g, "Clasificación ganada", Strong($"+{s.RankGained:N0}", Up), PerHour(s.RankGained / hours));
            if (s.Deaths > 0) Row(g, "Perdida al morir", Strong($"−{s.RankLost:N0}", Down), new Run(s.Deaths == 1 ? "  1 vez" : $"  {s.Deaths} veces") { Foreground = Muted });
            long net = s.RankGained - s.RankLost;
            if (s.Deaths > 0) Row(g, "Neto", Strong(net >= 0 ? $"+{net:N0}" : $"−{-net:N0}", net >= 0 ? Up : Down));
            Row(g, "Puntos de Abismo", Strong($"+{s.ApGained:N0}", Gold), PerHour(s.ApGained / hours));
            Row(g, "Kinah", Strong($"+{s.Kinah:N0}"), PerHour(s.Kinah / hours));
            body.Children.Add(g);
            // What the farming gave, the best first: grade, then how many.
            var items = s.Items.Select(i => (Id: i.Key, Count: i.Value, Info: InventoryWatch.Info(i.Key)))
                .OrderBy(i => DropsWindow.Grades.TryGetValue(i.Info.Grade, out var gr) ? gr.Rank : 9).ThenByDescending(i => i.Count).ToList();
            if (items.Count > 0)
            {
                var loot = Label("BOTÍN", 9.5, Dim); loot.Margin = new Thickness(0, 6, 0, 2); body.Children.Add(loot);
                foreach (var i in items.Take(8))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var count = Label($"×{i.Count:N0}", 10.5, Brushes.White); count.FontWeight = FontWeights.SemiBold; count.Margin = new Thickness(6, 0, 0, 0);
                    DockPanel.SetDock(count, Dock.Right); row.Children.Add(count);
                    if (OverlayWindow.Icon(i.Info.Icon) is { } image) { var pic = new Image { Source = image, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) }; DockPanel.SetDock(pic, Dock.Left); row.Children.Add(pic); }
                    row.Children.Add(Label(i.Info.Name, 10.5, Paint(DropsWindow.Grades.TryGetValue(i.Info.Grade, out var gr) ? gr.Color : "#DCE1E9")));
                    body.Children.Add(row);
                }
                if (items.Count > 8) body.Children.Add(Note($"y {items.Count - 8} objetos más"));
            }
        }
        body.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 8, 0, 0) });
        body.Children.Add(Note("Cuenta las recompensas del Abismo con la app abierta y se guarda por personaje."));
        L.Apply(body); // drawn between two passes of L.ApplyAll
        return true;
    }

    UIElement Head()
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Arrastra para mover · doble clic: vuelve junto al overlay" };
        // The × handles its own clicks: the rest of the header moves the card.
        head.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { e.Handled = true; Placed?.Invoke(null); return; }
            double left = Left, top = Top;
            Dragging = true;
            try { DragMove(); } catch (InvalidOperationException) { }
            finally { Dragging = false; }
            if (Left != left || Top != top) Placed?.Invoke(new Point(Left, Top));
        };
        var close = new Button { Content = new TextBlock { Text = "×", FontSize = 16, Foreground = Muted }, Width = 22, Height = 20, Cursor = Cursors.Hand, Focusable = false,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Cerrar", Padding = new Thickness(0, -3, 0, 0) };
        close.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        var medal = new Image { Source = OverlayWindow.Icon("/assets/game/Icon_Exploit_Medal_lv4_01.webp"), Width = 20, Height = 20, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(medal, Dock.Left); head.Children.Add(medal);
        var title = Label("ABISMO", 12.5, Gold); title.FontWeight = FontWeights.Bold; head.Children.Add(title);
        return head;
    }
    static UIElement Section(string text)
    {
        var t = Label(text, 10, Teal); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 7, 0, 3);
        return t;
    }
    static Grid Grid2()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }
    static void Row(Grid g, string label, params Run[] value)
    {
        int at = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = Label(label, 10.5, Muted); name.Margin = new Thickness(0, 1, 14, 1); Grid.SetRow(name, at); g.Children.Add(name);
        var figures = new TextBlock { FontSize = 10.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        foreach (var run in value) figures.Inlines.Add(run);
        Grid.SetRow(figures, at); Grid.SetColumn(figures, 1); g.Children.Add(figures);
    }
    static UIElement Bar(double ratio)
    {
        var bar = new Grid { Height = 5, Margin = new Thickness(0, 3, 0, 1) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
        var track = new Border { Background = Track, CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track); bar.Children.Add(new Border { Background = Gold, CornerRadius = new CornerRadius(3) });
        return bar;
    }
    static TextBlock Note(string text) { var t = Label(text, 10, Dim); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 3, 0, 0); return t; }
    static Run Strong(string text, Brush? color = null) => new(text) { FontWeight = FontWeights.SemiBold, Foreground = color ?? Brushes.White };
    static Run Change(int change) => new(change > 0 ? $" ▲{change}" : change < 0 ? $" ▼{-change}" : "") { Foreground = change >= 0 ? Up : Down };
    // The rate per hour farming, once there are 5 minutes of it: before, one reward makes a figure out of nothing.
    Run PerHour(double value) => new(active >= 300 ? $"  {value:N0} / h" : "") { Foreground = Muted };
    double active;
    static string Span(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} m" : $"{t.Minutes} m {t.Seconds:00} s"; }
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static TextBlock Label(string text, double size, Brush color) => new() { Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
}
