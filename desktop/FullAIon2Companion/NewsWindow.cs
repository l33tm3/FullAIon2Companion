using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FullAIon2Companion;

/// <summary>One entry of the news window: a version to update to (with its notes) or a notice published from the web.</summary>
public sealed record NewsItem(string Key, string Kind, string Title, string Body, string Level, string? Meta = null);

/// <summary>
/// "Novedades": what is new for the player, opened from the overlay strip's megaphone or the panel header. The available
/// update (notes, size and "Actualizar ahora", which installs only on that click) and the notices the site publishes
/// (account pull "announcements"). When nothing is new it shows the notes of the version installed.
/// </summary>
public sealed class NewsWindow : Window
{
    readonly StackPanel list = new();
    readonly TextBlock status = new() { FontSize = 11.5, Foreground = Paint("#95A4B6"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    /// <summary>"Actualizar ahora" (set by the main window).</summary>
    public Func<Task>? Install { get; set; }

    public NewsWindow()
    {
        Title = "FullAIon2Companion · Novedades"; Width = 460; SizeToContent = SizeToContent.Height; MaxHeight = 640;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        var title = new TextBlock { Text = "Novedades", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = "×", FontSize = 18, Width = 30, Height = 28, Foreground = Paint("#95A4B6"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "Cerrar" };
        close.Click += (_, _) => Hide();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close); head.Children.Add(title);
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 };
        var stack = new StackPanel(); stack.Children.Add(head); stack.Children.Add(scroll); stack.Children.Add(status);
        Content = new Border { Background = Paint("#F20B1117"), BorderBrush = Paint("#3F9D84"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 14), Child = stack };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
    }

    string current = AppVersion.Current;
    public void Show(IReadOnlyList<NewsItem> items, string current)
    {
        this.current = current;
        list.Children.Clear();
        foreach (var item in items) list.Children.Add(Card(item));
        if (items.Count == 0) list.Children.Add(new TextBlock { Text = $"Tienes la versión {current}. No hay noticias nuevas.", Foreground = Paint("#95A4B6"), FontSize = 12.5 });
        if (!IsVisible) Show();
        Activate();
    }
    public void SetStatus(string text) => status.Text = text;

    UIElement Card(NewsItem item)
    {
        bool update = item.Kind == "update";
        var accent = update ? Paint("#5FE0A0") : item.Level switch { "warning" => Paint("#E9C77E"), "critical" or "error" => Paint("#FF8A65"), _ => Paint("#71DED6") };
        var stack = new StackPanel();
        var kind = new TextBlock { Text = update ? "ACTUALIZACIÓN DISPONIBLE" : item.Kind == "current" ? "TU VERSIÓN" : "NOTICIA", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = accent };
        stack.Children.Add(kind);
        stack.Children.Add(new TextBlock { Text = item.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        if (item.Meta is { Length: > 0 } meta) stack.Children.Add(new TextBlock { Text = meta, FontSize = 11, Foreground = Paint("#8E9DAF"), Margin = new Thickness(0, 2, 0, 0) });
        if (update)
        {
            // The button first: the notes of several versions can be long.
            var install = new Button { Content = "Actualizar ahora", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = Paint("#0D141B"), Background = Paint("#5FE0A0"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            install.Click += async (_, _) =>
            {
                install.IsEnabled = false;
                try { if (Install is not null) await Install(); }
                catch (Exception ex) { SetStatus(ex.Message); install.IsEnabled = true; }
            };
            stack.Children.Add(install);
            stack.Children.Add(new TextBlock { Text = "Se descarga solo desde governanceai.me, se comprueba su huella SHA-256 y se instala en silencio: la app se cierra y vuelve a abrirse sola. Tus datos se conservan.", FontSize = 10.5, Foreground = Paint("#8E9DAF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        }
        // The site's notes: one line per version ("1.0.11: ..."), newest first. The versions after the installed one open;
        // the older ones wait behind a link. Notes without version lines (before 1.0.11) are one block.
        var blocks = item.Body.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^(\d+\.\d+\.\d+): (.+)$") is { Success: true } m ? (Version: (string?)m.Groups[1].Value, Text: m.Groups[2].Value) : (Version: (string?)null, Text: line.Trim()))
            .Where(b => b.Text.Length > 0).ToList();
        bool Fresh((string? Version, string Text) b) => b.Version is null ? blocks.All(x => x.Version is null) : item.Kind == "current" ? b.Version == current : AppVersion.IsNewer(b.Version, current);
        var older = new StackPanel { Visibility = Visibility.Collapsed };
        foreach (var b in blocks) (Fresh(b) ? stack : older).Children.Add(Block(b.Version, b.Text, accent));
        if (older.Children.Count > 0)
        {
            var more = new TextBlock { Text = $"Ver notas de versiones anteriores ({older.Children.Count})", FontSize = 11.5, Foreground = Paint("#71DED6"), Cursor = Cursors.Hand, Margin = new Thickness(0, 8, 0, 0), TextDecorations = TextDecorations.Underline };
            more.MouseLeftButtonUp += (_, _) => { bool open = older.Visibility == Visibility.Visible; older.Visibility = open ? Visibility.Collapsed : Visibility.Visible; more.Text = open ? $"Ver notas de versiones anteriores ({older.Children.Count})" : "Ocultar notas anteriores"; };
            stack.Children.Add(more); stack.Children.Add(older);
        }
        return new Border { Background = Paint("#101C26"), BorderBrush = accent, BorderThickness = new Thickness(3, 1, 1, 1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 12), Margin = new Thickness(0, 0, 0, 10), Child = stack };
    }
    // One version's notes: its number, then one point per sentence.
    static UIElement Block(string? version, string text, Brush accent)
    {
        var block = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (version is not null) block.Children.Add(new TextBlock { Text = $"Versión {version}", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = accent, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var point in System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+(?=[A-ZÁÉÍÓÚÑ«¿¡])").Where(x => x.Length > 0))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var dot = new TextBlock { Text = "•", Foreground = accent, FontSize = 12.5, Margin = new Thickness(0, 0, 7, 0) };
            DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = point, FontSize = 12.5, Foreground = Paint("#D5DEE6"), TextWrapping = TextWrapping.Wrap, LineHeight = 18 });
            block.Children.Add(row);
        }
        return block;
    }
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
}
