using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FullAIon2Companion;

/// <summary>
/// The per-skill breakdown of a player's fight, like the game's combat analyzer: total, DPS, casts and hits, critical,
/// multi-hit and back-attack rates, and per skill the damage, share, casts, multi-hit %, critical %, biggest hit and
/// average per cast. A target filter (all, or one creature), a damage / healing switch and a player switch. It takes the
/// mouse but never the keyboard focus, so the game keeps its keys; drag the title to move it, the corner to resize it.
/// </summary>
public sealed class BreakdownWindow : Window
{
    static readonly Brush Teal = Paint("#71DED6"), Muted = Paint("#8E9DAF"), Dim = Paint("#5E6E7E"), Row = Paint("#D5DEE6"), Gold = Paint("#E9C77E"),
        CardBg = Paint("#141E28"), Line = Paint("#22313C"), Bar = Paint("#2A4A52"), ChipOn = Paint("#1F3A3F"), ChipEdge = Paint("#3F9D84");
    readonly Func<int, int?, bool, BreakdownView?> source;
    readonly AppSettings settings;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock name = Text("", 15, Brushes.White, true), clock = Text("", 12, Muted), total = Text("", 22, Teal, true), totalLabel = Text("", 11.5, Muted),
        summary = Text("", 12, Row, true), rates = Text("", 11.5, Muted), empty = Text("Sin daño registrado en este combate todavía.", 12, Muted);
    readonly WrapPanel targets = new(), players = new(), cards = new(), columns = new();
    static readonly (string Key, string Label)[] ColumnChoices = [("hits", "Golpes"), ("crit", "Crítico"), ("back", "Espalda"), ("front", "Frente"), ("perfect", "Perfecto"), ("double", "Doble"), ("multi", "Multigolpe"), ("avg", "Media"), ("max", "Máx")];
    readonly StackPanel table = new();
    readonly ToggleButton damageTab, healTab;
    int actor;
    string view = "skills";
    readonly WrapPanel views = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock tableTitle = Text("Habilidades", 13, Brushes.White, true);
    FrameworkElement? showRowRef;
    int? target;
    bool healing;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    public BreakdownWindow(Func<int, int?, bool, BreakdownView?> source, AppSettings settings)
    {
        this.source = source; this.settings = settings;
        Title = "FullAIon2Companion · Detalle del combate";
        Width = Math.Clamp(settings.BreakdownWidth, 560, 1400); Height = Math.Clamp(settings.BreakdownHeight, 360, 1200);
        Left = settings.BreakdownLeft ?? SystemParameters.PrimaryScreenWidth / 2 - Width / 2; Top = settings.BreakdownTop ?? 160;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var close = new Button { Content = "×", FontSize = 18, Width = 30, Height = 28, Foreground = Muted, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Focusable = false, ToolTip = "Cerrar" };
        close.Click += (_, _) => Hide();
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        var share = new Button { Content = "📷 Compartir", FontSize = 11.5, Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0), Foreground = Muted, Background = CardBg, BorderBrush = Line, Cursor = Cursors.Hand, Focusable = false, ToolTip = "Guarda el detalle como imagen (Imágenes, carpeta FullAIon2Companion) y la copia al portapapeles" };
        share.Click += (_, _) => Share();
        DockPanel.SetDock(share, Dock.Right); head.Children.Add(share);
        var title = new StackPanel { Orientation = Orientation.Horizontal }; title.Children.Add(name); clock.Margin = new Thickness(8, 3, 0, 0); title.Children.Add(clock);
        head.Children.Add(title);
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) { DragMove(); Save(); } };

        damageTab = Chip("Daño", true, () => { healing = false; target = null; Refresh(); });
        healTab = Chip("Curación", false, () => { healing = true; target = null; Refresh(); });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        tabs.Children.Add(damageTab); tabs.Children.Add(healTab);
        var totals = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(tabs, Dock.Right); totals.Children.Add(tabs);
        var big = new StackPanel { Orientation = Orientation.Horizontal }; big.Children.Add(total); totalLabel.Margin = new Thickness(8, 9, 0, 0); big.Children.Add(totalLabel);
        totals.Children.Add(big);

        targets.Margin = new Thickness(0, 0, 0, 8);
        players.Margin = new Thickness(0, 0, 0, 6);
        var stats = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        cards.Margin = new Thickness(0, 0, 0, 4); stats.Children.Add(cards);
        rates.Margin = new Thickness(2, 2, 0, 6); rates.TextWrapping = TextWrapping.Wrap; stats.Children.Add(rates);
        var showLabel = Text("Mostrar:", 11, Muted); showLabel.Margin = new Thickness(0, 4, 8, 0);
        stats.Children.Add(views);
        var showRow = new DockPanel(); DockPanel.SetDock(showLabel, Dock.Left); showRow.Children.Add(showLabel); showRow.Children.Add(columns); stats.Children.Add(showRow); showRowRef = showRow;

        var scroll = new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var tableCard = new Border { Background = CardBg, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = new DockPanel() };
        var header = tableTitle; header.Margin = new Thickness(12, 9, 12, 4);
        var tableDock = (DockPanel)tableCard.Child;
        DockPanel.SetDock(header, Dock.Top); tableDock.Children.Add(header);
        tableDock.Children.Add(scroll);

        var grip = new Thumb { Width = 16, Height = 16, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Template = GripTemplate() };
        grip.DragDelta += (_, e) => { Width = Math.Clamp(Width + e.HorizontalChange, 560, 1400); Height = Math.Clamp(Height + e.VerticalChange, 360, 1200); };
        grip.DragCompleted += (_, _) => Save();

        var layout = new DockPanel { Margin = new Thickness(16, 10, 16, 14) };
        foreach (var part in new UIElement[] { head, players, totals, targets, stats }) { DockPanel.SetDock(part, Dock.Top); layout.Children.Add(part); }
        layout.Children.Add(tableCard);
        var frame = new Grid(); frame.Children.Add(layout); frame.Children.Add(grip);
        Content = new Border { Background = Paint("#F20B1117"), BorderBrush = Paint("#C833575B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = frame };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
        timer.Tick += (_, _) => { if (IsVisible) Refresh(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); timer.Start(); } else timer.Stop(); };
    }

    /// <summary>Opens the window on a player (0 = the local player), or closes it when it already shows that player.</summary>
    public void Toggle(int actorId = 0)
    {
        if (IsVisible && actorId == actor) { Hide(); return; }
        actor = actorId; target = null; healing = false;
        Show();
        Refresh();
    }
    /// <summary>For the offline render test: fills the window for a player without showing it.</summary>
    internal void Fill(int actorId, int? targetId = null, string? viewKey = null) { actor = actorId; target = targetId; if (viewKey is not null) view = viewKey; Refresh(); }
    void Save()
    {
        settings.BreakdownLeft = Math.Round(Left); settings.BreakdownTop = Math.Round(Top);
        settings.BreakdownWidth = Math.Round(Width); settings.BreakdownHeight = Math.Round(Height); settings.Save();
    }
    void Refresh()
    {
        var v = source(actor, target, healing);
        damageTab.IsChecked = !healing; healTab.IsChecked = healing;
        if (v is null) { name.Text = "Detalle del combate"; total.Text = "—"; totalLabel.Text = ""; summary.Text = ""; rates.Text = ""; cards.Children.Clear(); table.Children.Clear(); table.Children.Add(empty); return; }
        name.Text = v.Actor;
        clock.Text = Clock(v.Duration);
        total.Text = Short(v.Total);
        totalLabel.Text = healing ? "curación" : "daño";
        // Header cards like TeamMate's: total, per second, duration, crit, back, front, double, perfect (* estimated).
        cards.Children.Clear();
        void Card(string label, string value, bool accent = false, string? tip = null)
        {
            var stack = new StackPanel(); stack.Children.Add(Text(label, 10.5, Muted)); stack.Children.Add(Text(value, 17, accent ? Teal : Brushes.White, true));
            cards.Children.Add(new Border { Child = stack, MinWidth = 92, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(9),
                Background = accent ? Paint("#1D4740") : CardBg, BorderBrush = accent ? Paint("#3E8A7E") : Line, BorderThickness = new Thickness(1), ToolTip = tip });
        }
        const string Estimated = "Estimado: se confirmará comparando con el analizador de combate del juego (Ctrl+X)";
        Card(healing ? "Curación total" : "Daño total", Short(v.Total), true);
        Card(healing ? "HPS" : "DPS", Short(v.PerSecond));
        Card("Duración", Clock(v.Duration));
        Card("Crítico", $"{v.CritPct:0}%");
        if (!healing)
        {
            Card("Espalda", $"{v.BackPct:0}%", tip: Estimated); Card("Frente", $"{Math.Max(0, 100 - v.BackPct):0}%", tip: Estimated);
            Card("Doble*", $"{v.DoublePct:0}%", tip: Estimated); Card("Perfecto*", $"{v.PerfectPct:0}%", tip: Estimated);
        }
        summary.Text = "";
        rates.Text = healing ? $"{v.Casts} lanzamientos · {v.Hits} golpes"
            : $"Multigolpe {v.MultiPct:0}% · Parada {v.ParryPct:0}% · Bloqueo {v.BlockPct:0}% · Esquiva {v.EvadePct:0}% · Muertes: {v.Deaths} · Curación: {Short(v.HealingTotal)} · HPS: {Short(v.Hps)} · {v.Casts} lanzamientos · {v.Hits} golpes";
        // Column chips: which numbers each skill shows.
        columns.Children.Clear();
        foreach (var (key, label) in ColumnChoices)
        {
            string k = key;
            columns.Children.Add(Chip(label, settings.BreakdownColumns.Contains(k), () => { if (!settings.BreakdownColumns.Remove(k)) settings.BreakdownColumns.Add(k); settings.Save(); Refresh(); }));
        }
        // Player switch: the players of this fight (the local one first when present).
        players.Children.Clear();
        foreach (var p in v.Players.Take(12))
        {
            int id = p.Id;
            players.Children.Add(Chip($"{p.Name}  {Short(p.Damage)}", id == v.ActorId, () => { actor = id; target = null; Refresh(); }));
        }
        players.Visibility = v.Players.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        // Target filter: everything, or one creature.
        targets.Children.Clear();
        targets.Children.Add(Chip($"Todos  {Short(v.Targets.Sum(t => t.Amount))}", target is null, () => { target = null; Refresh(); }));
        foreach (var t in v.Targets.Take(8))
        {
            int id = t.Id;
            targets.Children.Add(Chip($"{t.Name}  {Short(t.Amount)}", target == id, () => { target = id; Refresh(); }));
        }
        // View tabs: skills, timeline, buffs, deaths, advice.
        views.Children.Clear();
        int deathCount = v.Extra?.Deaths.Count ?? 0;
        foreach (var (key, label) in new[] { ("skills", "Habilidades"), ("timeline", "Cronología"), ("buffs", "Buffs"), ("deaths", deathCount > 0 ? $"Muertes ({deathCount})" : "Muertes"), ("advice", "Consejos") })
        {
            string k = key;
            views.Children.Add(Chip(label, view == k, () => { view = k; Refresh(); }));
        }
        if (showRowRef is not null) showRowRef.Visibility = view == "skills" ? Visibility.Visible : Visibility.Collapsed;
        table.Children.Clear();
        if (view != "skills") { RenderView(v); return; }
        tableTitle.Text = "Habilidades";
        if (v.Skills.Count == 0) { table.Children.Add(empty); return; }
        double top = Math.Max(1, v.Skills.Max(s => s.Amount));
        var show = settings.BreakdownColumns;
        foreach (var s in v.Skills)
        {
            var head = new DockPanel();
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var amount = Text(s.Amount.ToString("N0"), 12.5, Brushes.White, true); amount.HorizontalAlignment = HorizontalAlignment.Right;
            var share = Text($"{s.Share:0.0}%", 11, Muted); share.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(amount); right.Children.Add(share);
            DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);
            var icon = new Image { Source = OverlayWindow.Icon(s.Icon), Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(icon, Dock.Left); head.Children.Add(icon);
            var body = new StackPanel();
            body.Children.Add(Text(s.Name, 12.5, Brushes.White, true));
            var bar = new Grid { Height = 6, Margin = new Thickness(0, 4, 12, 4) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s.Amount / top, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - s.Amount / top, GridUnitType.Star) });
            var track = new Border { Background = Paint("#1A2A33"), CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track); bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(Color.FromRgb(0x3F, 0xB8, 0xAF), Color.FromRgb(0x8F, 0xE8, 0xC8), 0) });
            body.Children.Add(bar);
            var parts = new List<string>();
            if (show.Contains("hits")) parts.Add($"Golpes {s.Hits}");
            if (show.Contains("crit")) parts.Add($"Crít {s.CritPct:0}%");
            if (show.Contains("back")) parts.Add($"Espalda {s.BackPct:0}%");
            if (show.Contains("front")) parts.Add($"Frente {Math.Max(0, 100 - s.BackPct):0}%");
            if (show.Contains("perfect")) parts.Add($"Perfecto {s.PerfectPct:0}%");
            if (show.Contains("double")) parts.Add($"Doble {s.DoublePct:0}%");
            if (show.Contains("multi")) parts.Add($"Multi {s.MultiPct:0}%");
            if (show.Contains("avg")) parts.Add($"Media {s.Average:N0}");
            if (show.Contains("max")) parts.Add($"Máx {s.Max:N0}");
            if (parts.Count > 0) body.Children.Add(Text(string.Join("   ", parts), 10.5, Muted));
            head.Children.Add(body);
            table.Children.Add(new Border { Child = head, Padding = new Thickness(12, 7, 12, 7), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) });
        }
    }

    void RenderView(BreakdownView v)
    {
        var x = v.Extra;
        Border Row_(UIElement child) => new() { Child = child, Padding = new Thickness(12, 8, 12, 8), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        void Note(string text) { var t = Text(text, 12, Muted); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(12, 10, 12, 10); table.Children.Add(t); }
        if (view == "timeline")
        {
            tableTitle.Text = healing ? "Curación por segundo a lo largo del combate" : "DPS a lo largo del combate";
            if (x is null || x.Dps.Count == 0 || x.Dps.Max() <= 0) { Note("Sin datos de este combate."); return; }
            double top = x.Dps.Max();
            var chart = new Grid { Height = 150, Margin = new Thickness(12, 10, 12, 0) };
            for (int i = 0; i < x.Dps.Count; i++)
            {
                chart.ColumnDefinitions.Add(new ColumnDefinition());
                var bar = new Border { Height = Math.Max(1, x.Dps[i] / top * 146), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(.5, 0, .5, 0), CornerRadius = new CornerRadius(2, 2, 0, 0),
                    Background = new LinearGradientBrush(Color.FromRgb(0x8F, 0xE8, 0xC8), Color.FromRgb(0x2F, 0x8F, 0x88), 90), ToolTip = $"{Clock(i * x.Bucket)} · {Short(x.Dps[i])}/s" };
                Grid.SetColumn(bar, i); chart.Children.Add(bar);
            }
            // Event marks over the chart.
            foreach (var e in x.Events)
            {
                int col = Math.Clamp((int)(e.At / x.Bucket), 0, x.Dps.Count - 1);
                var mark = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Background = e.Kind == "death" ? Paint("#FF6B6B") : e.Kind == "hit" ? Gold : Paint("#B98CFF"), Opacity = .9, ToolTip = $"{Clock(e.At)} · {e.Text}" };
                Grid.SetColumn(mark, col); chart.Children.Add(mark);
            }
            table.Children.Add(chart);
            var axis = new DockPanel { Margin = new Thickness(12, 2, 12, 8) };
            var end = Text(Clock(x.Dps.Count * x.Bucket), 10.5, Dim); DockPanel.SetDock(end, Dock.Right); axis.Children.Add(end);
            axis.Children.Add(Text($"0:00 · máx. {Short(top)}/s · barras de {x.Bucket:0} s", 10.5, Dim));
            table.Children.Add(axis);
            foreach (var e in x.Events)
            {
                var row = new DockPanel();
                var time = Text(Clock(e.At), 11.5, Muted); time.Width = 48; DockPanel.SetDock(time, Dock.Left); row.Children.Add(time);
                var dot = Text(e.Kind == "death" ? "✖" : e.Kind == "hit" ? "★" : "◆", 11.5, e.Kind == "death" ? Paint("#FF6B6B") : e.Kind == "hit" ? Gold : Paint("#B98CFF")); dot.Width = 18; DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
                row.Children.Add(Text(e.Text, 12, Row));
                table.Children.Add(Row_(row));
            }
            return;
        }
        if (view == "buffs")
        {
            tableTitle.Text = $"Efectos sobre {v.Actor}: tiempo activo";
            if (x is null || x.Buffs.Count == 0) { Note("No llegó ningún efecto de habilidad sobre este jugador en el combate. Los efectos de otros jugadores solo se ven si el juego los envía (grupo o cerca)."); return; }
            foreach (var b in x.Buffs)
            {
                var row = new DockPanel();
                var right = Text($"{b.Uptime:0} %  ·  {b.Count}×", 12, Brushes.White, true); DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
                var icon = new Image { Source = OverlayWindow.Icon(b.Icon), Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0) }; DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
                var body = new StackPanel(); body.Children.Add(Text(b.Name, 12, Brushes.White, true));
                var bar = new Grid { Height = 6, Margin = new Thickness(0, 4, 12, 0) };
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, b.Uptime), GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.001, 100 - b.Uptime), GridUnitType.Star) });
                var track = new Border { Background = Paint("#1A2A33"), CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(track, 2);
                bar.Children.Add(track); bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = b.Uptime >= 80 ? Teal : b.Uptime >= 50 ? Gold : Paint("#FF8A65") });
                body.Children.Add(bar); row.Children.Add(body);
                table.Children.Add(Row_(row));
            }
            return;
        }
        if (view == "deaths")
        {
            tableTitle.Text = "Muertes de jugadores";
            if (x is null || x.Deaths.Count == 0) { Note("Nadie murió en este combate."); return; }
            foreach (var d in x.Deaths)
            {
                var stack = new StackPanel();
                stack.Children.Add(Text($"{Clock(d.At)} · {d.Victim}" + (d.Killer is null ? "" : $" · por {d.Killer}"), 12.5, Paint("#FF8A8A"), true));
                stack.Children.Add(Text($"Daño recibido en los últimos 5 s: {d.DamageTaken5s:N0}", 11, Muted));
                foreach (var line in d.LastHits) { var t = Text(line, 11.5, Row); t.Margin = new Thickness(10, 2, 0, 0); stack.Children.Add(t); }
                table.Children.Add(Row_(stack));
            }
            return;
        }
        tableTitle.Text = "Consejos";
        foreach (var a in x?.Advice ?? ["Sin datos de este combate."])
        {
            var row = new DockPanel();
            var dot = Text("›", 14, Teal, true); dot.Width = 16; DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
            var t = Text(a, 12, Row); t.TextWrapping = TextWrapping.Wrap; row.Children.Add(t);
            table.Children.Add(Row_(row));
        }
        Note("Los consejos salen solo de lo que se ve en el tráfico: reutilizaciones de la tabla del juego, tiempo activo de los efectos, ataques evitados y muertes.");
    }
    /// <summary>"Compartir": the window as a PNG in Pictures/FullAIon2Companion, also copied to the clipboard.</summary>
    void Share()
    {
        try
        {
            var visual = (FrameworkElement)Content;
            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(visual.ActualWidth * dpi), (int)(visual.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FullAIon2Companion");
            System.IO.Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, $"combate-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = System.IO.File.Create(file)) png.Save(stream);
            Clipboard.SetImage(bitmap);
            clock.Text = "Imagen guardada y copiada";
        }
        catch (Exception ex) { clock.Text = "No se pudo guardar la imagen: " + ex.Message; }
    }
    static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        foreach (double w in new double[] { 76, 58, 66, 66, 62, 66, 66 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        return grid;
    }
    static void AddCells(Grid grid, Brush color, double size, params string?[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] is null) continue;
            var t = Text(cells[i]!, size, i == 1 && size > 11 ? Brushes.White : color, i <= 1);
            t.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, i); grid.Children.Add(t);
        }
    }
    ToggleButton Chip(string label, bool on, Action click)
    {
        var chip = new ToggleButton { Content = new TextBlock { Text = label, FontSize = 11.5 }, IsChecked = on, Focusable = false, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4), Template = ChipTemplate() };
        chip.Click += (_, _) => click();
        return chip;
    }
    static ControlTemplate ChipTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "chip" };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetValue(Border.BackgroundProperty, CardBg);
        border.SetValue(Border.BorderBrushProperty, Line);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, ChipOn, "chip"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, ChipEdge, "chip"));
        on.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.White));
        var off = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = false };
        off.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, Muted));
        template.Triggers.Add(on); template.Triggers.Add(off);
        return template;
    }
    static ControlTemplate GripTemplate()
    {
        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 15,3 L 3,15 M 15,8 L 8,15 M 15,12 L 12,15"));
        path.SetValue(System.Windows.Shapes.Path.StrokeProperty, Muted);
        path.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.4);
        var box = new FrameworkElementFactory(typeof(Grid));
        box.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        box.AppendChild(path);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = box };
    }
    static TextBlock Text(string text, double size, Brush color, bool semibold = false) => new() { Text = text, FontSize = size, Foreground = color, FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static string Clock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    static string Short(double v) => v >= 1_000_000 ? $"{v / 1_000_000:0.0}M" : v >= 1000 ? $"{v / 1000:0.0}K" : v.ToString("N0");
}
