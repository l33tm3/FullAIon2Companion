using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FullAIon2Companion;

/// <summary>
/// The overlay's styles (Configuración → Pantalla → Estilo del overlay): a structure - which parts show and where - and a
/// palette; sixteen named combinations. The palette recolours the shared brushes in place, so a change shows at once; the
/// structure places the same parts, and a few of its own, in the overlay's body. Asked for on 2026-10-19 after drawings of
/// five structures («hazlas todas y pon una opción de estilos en configuración») and then «otros 10 estilos ... con nombres».
/// Structures: clasico (as it was), pulido (the same, tidier), modulos (blocks that fold), minimo (narrow, the essentials),
/// momento (out of a fight: what comes next; in a fight: pulido) and medallon (a band with the character, the DPS in a medal).
/// </summary>
public sealed partial class OverlayWindow
{
    /// <summary>The colours of a style: the accent and its dim tone (an idle DPS), the highlight, the frame, the background,
    /// the text tones, the good-health bar, the corner radius and how much of the background shows (Cristal).</summary>
    public sealed record Palette(string Accent, string AccentDim, string Gold, string Frame, string Back, string Muted, string Dim,
        string Bright, string Row, string HpFrom, string HpTo, double Radius = 8, double Glass = 1);
    /// <summary>What else makes a style its own: the font (Windows' own, nothing shipped) and whether it is italic or bold, a second
    /// background colour (a vertical gradient), a pattern over the background (scan lines, grid, dots, hearts, stars, petals,
    /// diagonals), a glow around the frame, a bar on top (the accent, a colour, «gradient:#a,#b» or «hazard») and the frame's width.</summary>
    public sealed record Look(string Font = "Segoe UI", bool Italic = false, bool Bold = false, string? Back2 = null, string? Pattern = null, string? PatternColor = null,
        double PatternAlpha = .08, double Glow = 0, string? GlowColor = null, double TopBar = 0, string? TopBarColor = null, double Border = 1);
    static readonly Look Plain = new();
    public sealed record OverlayStyle(string Key, string Name, string Layout, string Description, Palette Colors, Look? Extra = null)
    {
        public Look Touches => Extra ?? Plain;
    }

    static readonly Palette Turquesa = new("#71DED6", "#4E9A95", "#E9C77E", "#C833575B", "#0D141B", "#95A4B6", "#5F7480", "#D5DEE6", "#C3CFD9", "#3FB8AF", "#8FE8C8");
    static readonly Palette Dorado = new("#E9C77E", "#B8A06A", "#F2D58A", "#C88C7440", "#0E1014", "#A9A28F", "#7A7260", "#EDE6D3", "#D8D0BC", "#B8913F", "#F2D58A");
    static readonly Palette Celeste = new("#7CC4FF", "#5A8FBF", "#F3E3A0", "#C84A6F95", "#0C1420", "#9DB2C8", "#627A94", "#E2ECF7", "#CAD8E6", "#4A9BE8", "#A6D8FF");
    static readonly Palette Carmesi = new("#FF6B6B", "#B85050", "#F0B37E", "#C87A3A3F", "#160C0F", "#B39A9E", "#7D6266", "#F2DEE0", "#E0C8CB", "#C9302C", "#FF8A80");
    static readonly Palette Violeta = new("#B98CFF", "#8467B8", "#E9C77E", "#C85B4A8A", "#110D1C", "#A59BBE", "#6D6488", "#E6DEF7", "#D2C8EA", "#7D5BD6", "#C9A6FF");
    static readonly Palette Hielo = new("#8FE3FF", "#5FA9C2", "#DDEBFF", "#C8487A8F", "#0A161E", "#9CB9C6", "#5F8494", "#E8F7FC", "#CFE6EE", "#47B5D9", "#B8F1FF");
    static readonly Palette Brasa = new("#FF9F43", "#C27A35", "#FFD27A", "#C88A5A2E", "#180F0A", "#BFA690", "#86705C", "#F7E6D6", "#E8D2BE", "#E06A1F", "#FFB86B");
    static readonly Palette Jade = new("#5FE3A1", "#46A877", "#E4D58A", "#C83F7A5E", "#0A1612", "#97B8A8", "#5F8270", "#DDF2E7", "#C6E2D4", "#2FA36B", "#8CF0BE");
    static readonly Palette Obsidiana = new("#F2F2F2", "#A8A8A8", "#D9C38F", "#C8505050", "#08080A", "#9A9A9A", "#666666", "#EDEDED", "#D0D0D0", "#8A8A8A", "#E6E6E6", Radius: 3);
    static readonly Palette Neon = new("#FF4FD8", "#B83A9C", "#4FF0FF", "#C8732C8F", "#0C0816", "#A99BC4", "#6E6290", "#F2E8FF", "#DCCFF0", "#FF4FD8", "#4FF0FF", Radius: 4);
    static readonly Palette Pergamino = new("#D9B77E", "#A88C5C", "#F2D58A", "#C88A6B3E", "#1E1812", "#B5A58C", "#84765F", "#F1E6D2", "#E0D2BA", "#A87A3A", "#E8C68A", Radius: 6);
    static readonly Palette Cristal = new("#A6F0FF", "#7FB8C4", "#F5E6A8", "#99FFFFFF", "#141C24", "#C0CCD6", "#8796A3", "#FFFFFF", "#E6EEF4", "#6FD3E8", "#C8F6FF", Radius: 10, Glass: .5);

    /// <summary>The sixteen styles, in the order of the selector. The key is what settings.json keeps.</summary>
    public static readonly IReadOnlyList<OverlayStyle> Styles =
    [
        new("clasico", "Clásico", "clasico", "El de siempre", Turquesa),
        new("pulido", "Pulido", "pulido", "La misma forma, más ordenada", Turquesa),
        new("modulos", "Módulos", "modulos", "Bloques que se pliegan", Turquesa),
        new("minimo", "Mínimo", "minimo", "Estrecho, solo lo esencial", Turquesa),
        new("momento", "Según el momento", "momento", "Sin pelea, lo que viene", Turquesa),
        new("aion", "Aion dorado", "medallon", "Marco dorado y medallón", Dorado),
        new("elyos", "Elyos celeste", "pulido", "Azul cielo y oro claro", Celeste),
        new("asmodae", "Asmodae carmesí", "momento", "Rojo sangre", Carmesi),
        new("abismo", "Abismo violeta", "modulos", "El morado del Abismo", Violeta),
        new("escarcha", "Escarcha", "minimo", "Hielo, mínimo y frío", Hielo),
        new("ascuas", "Ascuas", "medallon", "Naranja de brasa", Brasa),
        new("verteron", "Jade de Verteron", "modulos", "Verde jade", Jade),
        new("obsidiana", "Obsidiana", "pulido", "Negro y gris, esquinas rectas", Obsidiana),
        new("neon", "Neón", "minimo", "Rosa y cian eléctricos", Neon),
        new("altgard", "Pergamino de Altgard", "medallon", "Cuero y bronce", Pergamino),
        new("cristal", "Cristal", "clasico", "Fondo translúcido", Cristal),
        // Themed (2026-10-19, «otros 20 estilos ... como CyberPunk, Woman Gamer ... usa fuentes y cosas»): each has its own font and touches.
        new("cyberpunk", "Cyberpunk", "pulido", "Neón amarillo y cian, líneas de pantalla",
            new("#00F0FF", "#0AA8B3", "#FCEE0A", "#C8FCEE0A", "#0A0A12", "#9A9AB0", "#5E5E78", "#F2F2FF", "#D8D8EE", "#FF2A6D", "#FCEE0A", Radius: 0),
            new(Font: "Bahnschrift", Pattern: "scan", PatternAlpha: .07, Glow: 14, TopBar: 3, TopBarColor: "#FCEE0A")),
        new("gamergirl", "Gamer Girl", "modulos", "Rosa intenso, corazones y bordes suaves",
            new("#FF5FC8", "#C2479A", "#FFD1F0", "#C8FF5FC8", "#1A0E1E", "#C9A3C0", "#8A6684", "#FFF0FB", "#F4D6EC", "#FF5FC8", "#C79BFF", Radius: 16),
            new(Font: "Candara", Back2: "#2A1030", Pattern: "hearts", PatternAlpha: .08, Glow: 12)),
        new("synthwave", "Synthwave", "minimo", "Atardecer retro de los 80, en cursiva",
            new("#FF71CE", "#B8509A", "#FFB86C", "#C8B967FF", "#1B0B2E", "#B9A0D0", "#7A6392", "#FFF0FA", "#E9D5F5", "#FF71CE", "#FFB86C", Radius: 6),
            new(Font: "Trebuchet MS", Italic: true, Back2: "#3A0F3F", Pattern: "grid", PatternAlpha: .07, Glow: 16, GlowColor: "#B967FF")),
        new("terminal", "Terminal", "clasico", "Verde fósforo y letra de consola",
            new("#33FF66", "#1F9E40", "#B6FF9E", "#C833FF66", "#020804", "#5FBF7A", "#2F7A45", "#C8FFD6", "#9EEDB4", "#1F9E40", "#33FF66", Radius: 0),
            new(Font: "Consolas", Pattern: "scan", PatternAlpha: .08, Glow: 8)),
        new("arcade", "Arcade", "momento", "Recreativa de 8 bits, bordes gruesos",
            new("#FFD23F", "#C9A22B", "#FF4F4F", "#FF3A6BFF", "#0B0F2E", "#A8B2E8", "#6A74B0", "#FFFFFF", "#DDE3FF", "#3AE07A", "#FFD23F", Radius: 0),
            new(Font: "Lucida Console", Pattern: "dots", PatternColor: "#3A6BFF", PatternAlpha: .16, Border: 3)),
        new("steampunk", "Steampunk", "medallon", "Latón, cobre y letra clásica",
            new("#D9A35A", "#A87A3E", "#F0C987", "#C8B07A3A", "#1C130C", "#BFA88A", "#857059", "#F4E6D0", "#E2CFB2", "#8C5A2B", "#D9A35A", Radius: 4),
            new(Font: "Palatino Linotype", Back2: "#2A1C10", Pattern: "dots", PatternAlpha: .07, Border: 2)),
        new("grimorio", "Grimorio arcano", "medallon", "Violeta, estrellas doradas y cursiva",
            new("#C08BFF", "#8A62C2", "#F2D58A", "#C8F2D58A", "#120B1F", "#B3A3CC", "#776A90", "#F2EAFF", "#DCCFF2", "#7D4FD6", "#F2D58A", Radius: 6),
            new(Font: "Constantia", Italic: true, Back2: "#1E1030", Pattern: "stars", PatternColor: "#F2D58A", PatternAlpha: .08, Glow: 12, GlowColor: "#9B6BFF")),
        new("bushido", "Bushido", "pulido", "Rojo y marfil sobre tinta",
            new("#E23D3D", "#A82E2E", "#F2E8D5", "#C8E23D3D", "#0D0B0B", "#B8ADA6", "#7A706B", "#F7F1E8", "#E6DDD2", "#A82E2E", "#F2E8D5", Radius: 2),
            new(Font: "Cambria", Pattern: "diagonal", PatternColor: "#F2E8D5", PatternAlpha: .03, TopBar: 3)),
        new("kawaii", "Kawaii", "modulos", "Pastel, estrellitas y letra redonda",
            new("#7FE7C4", "#5CB89A", "#FFD27F", "#C8FFB3D1", "#1E1A2A", "#C9BCD9", "#8B7FA0", "#FFF8FF", "#EFE6FA", "#7FE7C4", "#FFB3D1", Radius: 18),
            new(Font: "Comic Sans MS", Back2: "#2A2236", Pattern: "stars", PatternColor: "#FFD27F", PatternAlpha: .09)),
        new("tactico", "Táctico", "momento", "Verde militar con retícula",
            new("#A8C35A", "#7E9442", "#D8C99B", "#C87E8A4E", "#11140C", "#A3AA8E", "#6B7259", "#E8EBDD", "#D2D7C2", "#6B8A2E", "#C9E07A", Radius: 0),
            new(Font: "Bahnschrift", Pattern: "grid", PatternAlpha: .06, TopBar: 2)),
        new("gotico", "Gótico", "clasico", "Negro, rojo sangre y letra con serifa",
            new("#D7263D", "#9E1C2D", "#C9C9D6", "#C88A1C2D", "#09060A", "#A89AA6", "#6E6170", "#EFE6EE", "#D9CCD6", "#7A0F1E", "#D7263D", Radius: 2),
            new(Font: "Georgia", Back2: "#150A10", Glow: 12)),
        new("druida", "Druida", "momento", "Verdes del bosque y oro",
            new("#7FD36A", "#5C9E4D", "#E6C76A", "#C85C7A3A", "#0C140D", "#A6BC9C", "#6B8262", "#E9F5E2", "#D2E6C8", "#3E8A3A", "#B5E66A", Radius: 10),
            new(Font: "Sitka Text", Back2: "#142012", Pattern: "petals", PatternAlpha: .07)),
        new("esports", "Esports", "minimo", "Rojo competitivo, todo en negrita",
            new("#FF3B3B", "#C22E2E", "#FFFFFF", "#C8FF3B3B", "#121316", "#A6A9B3", "#6C707C", "#FFFFFF", "#DDE0E8", "#FF3B3B", "#FF9A3B", Radius: 2),
            new(Bold: true, TopBar: 3, TopBarColor: "gradient:#FF3B3B,#FF9A3B")),
        new("holograma", "Holograma", "modulos", "Cian translúcido con brillo",
            new("#6FF7FF", "#4BB8C2", "#B8FFF5", "#9966F0FF", "#061A24", "#9CCFD8", "#5E909A", "#E6FDFF", "#C8F0F5", "#2FB8D9", "#B8FFF5", Radius: 10, Glass: .55),
            new(Font: "Segoe UI Light", Pattern: "scan", PatternAlpha: .09, Glow: 16)),
        new("radiactivo", "Radiactivo", "pulido", "Verde ácido y franja de peligro",
            new("#B6FF00", "#84B800", "#FFE600", "#C8B6FF00", "#0A0C05", "#A8B88A", "#6E7A55", "#F2FFD6", "#DCEFB4", "#6EA800", "#E6FF5A", Radius: 2),
            new(Font: "Franklin Gothic Medium", Glow: 10, TopBar: 6, TopBarColor: "hazard")),
        new("sakura", "Sakura", "clasico", "Rosa cerezo suave",
            new("#FFB7C9", "#C98A9C", "#FFF0F4", "#C8FFB7C9", "#1C1418", "#D1B3BE", "#94737F", "#FFF7FA", "#F2DEE6", "#E87A9A", "#FFD9E3", Radius: 14),
            new(Font: "Yu Gothic UI", Back2: "#271A20", Pattern: "petals", PatternAlpha: .08)),
        new("desierto", "Desierto", "medallon", "Arena y turquesa",
            new("#3FD0C9", "#2E9A95", "#E8C07A", "#C8B08A4E", "#1C1610", "#C4AE8E", "#8A765C", "#F7EBDA", "#E8D6BE", "#C98A3A", "#E8C07A", Radius: 6),
            new(Font: "Tahoma", Back2: "#2A2016", Pattern: "diagonal", PatternColor: "#E8C07A", PatternAlpha: .05)),
        new("tormenta", "Tormenta", "pulido", "Azul eléctrico con resplandor",
            new("#5CB8FF", "#3F86C2", "#E6F4FF", "#C85CB8FF", "#0A1220", "#A3B8D1", "#677D99", "#F2F8FF", "#D6E5F5", "#2E7BFF", "#A8E1FF", Radius: 8),
            new(Font: "Corbel", Back2: "#101D33", Glow: 18, TopBar: 2, TopBarColor: "gradient:#5CB8FF,#E6F4FF")),
        new("noir", "Noir", "clasico", "Máquina de escribir en blanco y negro",
            new("#E8DCC0", "#A89E86", "#C9A66B", "#C8E8DCC0", "#141210", "#A8A090", "#6E685C", "#F2EDE2", "#DAD3C4", "#6E685C", "#E8DCC0", Radius: 0),
            new(Font: "Courier New", Pattern: "scan", PatternAlpha: .04, Border: 2)),
        new("vikingo", "Vikingo", "minimo", "Acero y hueso",
            new("#8FB8D9", "#6688A6", "#E6DCC3", "#C88FB8D9", "#0F151B", "#A6B3BF", "#6B7885", "#EEF3F7", "#D6E0E8", "#4F7FA8", "#C9E2F2", Radius: 2),
            new(Font: "Sitka Heading", Back2: "#18212A", Pattern: "diagonal", PatternAlpha: .04, Border: 2)),
    ];
    public static OverlayStyle StyleOf(string? key) => Styles.FirstOrDefault(s => s.Key == key) ?? Styles[0];
    string layout = "clasico";
    Palette palette = Turquesa;
    Look look = Plain;
    // Behind the frame its glow; inside it the pattern; over its top edge the bar.
    readonly Border glowLayer = new() { BorderThickness = new Thickness(2), IsHitTestVisible = false, Visibility = Visibility.Collapsed },
        patternLayer = new() { IsHitTestVisible = false }, topBar = new() { VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Grid rootHost = new();

    // The overlay's own parts, kept to be placed by ArrangeLayout.
    readonly DockPanel header = new() { Margin = new Thickness(0, 0, 0, 4) };
    readonly StackPanel bodyPanel = new(), rootLayout = new();
    readonly Border bodyBox = new() { Padding = new Thickness(14, 10, 14, 10) }, footerBox = new();
    readonly DockPanel footer = new(), dpsBlock = new() { Margin = new Thickness(0, 2, 0, 2) };
    readonly StackPanel dpsNumbers = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    StackPanel dpsRow = null!, selfRow = null!, cubeTextStack = null!;
    UIElement targetRow = null!;
    string? selfClassName;
    IReadOnlyList<(string Icon, string Name, string Color, DateTime At)> lastTimers = [];
    CubeView? lastCube;
    /// <summary>For the style previews (--overlay-styles): the cube shows although the window is not on screen.</summary>
    internal bool Preview;
    /// <summary>For the previews: the last damage is old, so the overlay is out of a fight.</summary>
    internal void PreviewCalm() => lastChange = DateTime.MinValue;

    // Pulido and the others: an empty-target box instead of the line, figures in white, a compact cube.
    readonly Border noTarget = new() { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(9, 5, 9, 6), Margin = new Thickness(0, 2, 0, 4) };
    // The cube as a chip (Módulos, Aion) and as a row inside the overlay (Según el momento, out of a fight).
    readonly Border cubePill = new() { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(7, 0, 8, 1), Margin = new Thickness(6, 0, 0, 3), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    readonly TextBlock cubePillText = new() { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
    readonly Border cubeInline = new() { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock cubeInlineText = new() { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock cubeInlineDistance = Label("", 13, Brushes.White);
    // Módulos: blocks with a title that fold with a click (in move mode).
    Border combatModule = null!, groupModule = null!, petsModule = null!, farmBox = null!;
    readonly StackPanel combatBody = new(), groupBody = new(), petsBody = new();
    readonly TextBlock groupTitle = Label("GRUPO", 9.5, Dim), petsTitle = Label("MASCOTAS", 9.5, Dim);
    readonly List<(string Key, TextBlock Caret, StackPanel Body)> modules = [];
    // The followed pets inside the overlay (Módulos, Según el momento, Aion) and on one line (Mínimo).
    readonly StackPanel petList = new();
    readonly WrapPanel petsMini = new() { Margin = new Thickness(0, 6, 0, 0) };
    string petListKey = "";
    // Mínimo: the header with icon chips, and the group as bars of their share.
    readonly DockPanel miniHeader = new() { Margin = new Thickness(0, 0, 0, 2) };
    readonly Ellipse miniDot = new() { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock miniClock = Label("", 10, Muted);
    readonly StackPanel miniChips = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel shareBars = new() { Margin = new Thickness(0, 2, 0, 0) };
    // Según el momento, out of a fight: the last fight in the header, the events as tiles, the Abyss, the pets and the group.
    readonly TextBlock lastFight = new() { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Grid eventTiles = new() { Margin = new Thickness(0, 2, 0, 0) };
    readonly Border abyssRow = new() { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock abyssText = new() { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
    readonly DockPanel petsHeader = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock petsHeaderCount = Label("", 9.5, Dim);
    readonly WrapPanel groupPills = new() { Margin = new Thickness(0, 8, 0, 0) };
    bool momentoCalm;
    string tilesKey = "", pillsKey = "";
    // Aion (medallón): a band with the character, the chips with the cube, the DPS in a medal, golden dividers.
    readonly Border band = new() { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 6, 14, 6) };
    readonly Image bandIcon = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock bandName = Label("", 11, Gold), bandState = Label("", 10, Muted);
    readonly DockPanel chipsRow = new();
    readonly DockPanel medalBlock = new() { Margin = new Thickness(0, 2, 0, 6) };
    readonly Border medal = new() { Width = 66, Height = 66, CornerRadius = new CornerRadius(33), BorderThickness = new Thickness(2) };
    readonly TextBlock medalDps = Label("", 16, Brushes.White), medalUnit = Label("DPS", 9.5, Gold),
        medalDamage = Label("", 10.5, Brushes.White), medalHeal = Label("", 10.5, Brushes.White), medalKills = Label("", 10.5, Brushes.White);
    readonly TextBlock groupDividerText = Label("GRUPO", 9.5, Gold), petsDividerText = Label("MASCOTAS", 9.5, Gold);
    UIElement groupDivider = null!, petsDivider = null!;
    readonly List<Border> dividerLines = [];

    double HpWidth => hpBar.ActualWidth > 0 ? hpBar.ActualWidth : BarWidth;
    double LayoutWidth => layout == "minimo" ? 268 : 344;

    void BuildStyleParts()
    {
        // Empty target.
        noTarget.Background = Paint("#08FFFFFF"); noTarget.BorderBrush = Paint("#4095A4B6");
        var hint = Label("selecciona una criatura", 10, Dim); DockPanel.SetDock(hint, Dock.Right);
        var empty = new DockPanel(); empty.Children.Add(hint); empty.Children.Add(Label("Sin objetivo", 11, Muted));
        noTarget.Child = empty;
        // Cube chip and cube row.
        cubePill.Background = Paint("#E61C1430"); cubePill.BorderBrush = Paint("#B98CFF");
        cubePill.Child = Row(Label("➤ ", 10.5, Paint("#C9A6FF")), cubePillText);
        cubeInline.Background = Paint("#E61C1430"); cubeInline.BorderBrush = Paint("#B98CFF");
        var arrow = new TextBlock { Text = "➤", FontSize = 14, Foreground = Paint("#C9A6FF"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-20) };
        cubeInlineDistance.FontWeight = FontWeights.Bold; cubeInlineDistance.Margin = new Thickness(8, 0, 0, 0);
        var cubeRow = new DockPanel(); DockPanel.SetDock(arrow, Dock.Left); cubeRow.Children.Add(arrow);
        DockPanel.SetDock(cubeInlineDistance, Dock.Right); cubeRow.Children.Add(cubeInlineDistance); cubeRow.Children.Add(cubeInlineText);
        cubeInline.Child = cubeRow;
        // Modules.
        combatModule = Module("combate", Label("COMBATE", 9.5, Dim), combatBody);
        groupModule = Module("grupo", groupTitle, groupBody);
        petsModule = Module("mascotas", petsTitle, petsBody);
        farmBox = new Border { Background = Paint("#09FFFFFF"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 5, 9, 4), Margin = new Thickness(0, 5, 0, 0) };
        // Mínimo.
        DockPanel.SetDock(miniChips, Dock.Right); miniHeader.Children.Add(miniChips); miniHeader.Children.Add(Row(miniDot, miniClock));
        // Según el momento.
        DockPanel.SetDock(lastFight, Dock.Right); header.Children.Insert(1, lastFight);
        abyssRow.BorderBrush = Paint("#6B5A2E"); abyssRow.Background = Paint("#0FE9C77E");
        DockPanel.SetDock(abyssText, Dock.Right);
        var abyss = new DockPanel(); abyss.Children.Add(abyssText);
        abyss.Children.Add(Row(new Image { Source = Icon("/assets/game/Icon_Exploit_Medal_lv4_01.webp"), Width = 14, Height = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center }, Label("Abismo", 10.5, Gold)));
        abyssRow.Child = abyss;
        DockPanel.SetDock(petsHeaderCount, Dock.Right); petsHeader.Children.Add(petsHeaderCount); petsHeader.Children.Add(Label("MASCOTAS QUE SIGUES", 9.5, Dim));
        // Aion: band, medal and dividers.
        bandName.FontWeight = FontWeights.SemiBold;
        var bandRow = new DockPanel(); DockPanel.SetDock(bandState, Dock.Right); bandRow.Children.Add(bandState); bandRow.Children.Add(Row(bandIcon, bandName));
        band.Child = bandRow;
        medalDps.FontWeight = FontWeights.SemiBold; medalDps.HorizontalAlignment = medalUnit.HorizontalAlignment = HorizontalAlignment.Center;
        var inMedal = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; inMedal.Children.Add(medalDps); inMedal.Children.Add(medalUnit);
        medal.Child = inMedal;
        var stats = new Grid { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        stats.ColumnDefinitions.Add(new ColumnDefinition()); stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string[] names = ["Daño", "Curación", "Criaturas"]; TextBlock[] values = [medalDamage, medalHeal, medalKills];
        for (int i = 0; i < names.Length; i++)
        {
            stats.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Label(names[i], 10.5, Muted); name.Margin = new Thickness(0, 1, 0, 1); Grid.SetRow(name, i); stats.Children.Add(name);
            values[i].FontWeight = FontWeights.SemiBold; Grid.SetRow(values[i], i); Grid.SetColumn(values[i], 1); stats.Children.Add(values[i]);
        }
        DockPanel.SetDock(medal, Dock.Left); medalBlock.Children.Add(medal); medalBlock.Children.Add(stats);
        groupDivider = Divider(groupDividerText); petsDivider = Divider(petsDividerText);
    }
    Border Module(string key, TextBlock title, StackPanel body)
    {
        var caret = Label("▾", 9.5, Dim);
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Plegar o desplegar (en modo mover)" };
        DockPanel.SetDock(caret, Dock.Right); head.Children.Add(caret); head.Children.Add(title);
        head.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (ClickThrough) return;
            e.Handled = true;
            if (!settings.OverlayFolded.Remove(key)) settings.OverlayFolded.Add(key);
            settings.Save(); FoldModules();
        };
        body.Margin = new Thickness(0, 2, 0, 0);
        var stack = new StackPanel(); stack.Children.Add(head); stack.Children.Add(body);
        modules.Add((key, caret, body));
        return new Border { Background = Paint("#09FFFFFF"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 5, 0, 0), Child = stack };
    }
    void FoldModules()
    {
        foreach (var (key, caret, body) in modules)
        {
            bool folded = settings.OverlayFolded.Contains(key);
            body.Visibility = folded ? Visibility.Collapsed : Visibility.Visible; caret.Text = folded ? "▸" : "▾";
        }
    }
    UIElement Divider(TextBlock text)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center }; var right = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center };
        dividerLines.Add(left); dividerLines.Add(right);
        text.Margin = new Thickness(8, 0, 8, 0); Grid.SetColumn(text, 1); Grid.SetColumn(right, 2);
        grid.Children.Add(left); grid.Children.Add(text); grid.Children.Add(right);
        return grid;
    }
    static void Detach(UIElement element)
    {
        switch ((element as FrameworkElement)?.Parent)
        {
            case Panel parent: parent.Children.Remove(element); break;
            case Decorator decorator: decorator.Child = null; break;
        }
    }

    /// <summary>Applies the chosen style (Configuración → Pantalla): its colours and its structure.</summary>
    public void ApplyOverlayStyle()
    {
        var style = StyleOf(settings.OverlayStyle);
        layout = style.Layout; palette = style.Colors; look = style.Touches;
        ApplyPalette();
        ApplyLook();
        momentoCalm = false;
        ArrangeLayout();
        ThemeFamily(all: true);
    }
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    void ApplyPalette()
    {
        var p = palette;
        Teal.Color = MoveFrame.Color = C(p.Accent); IdleTeal.Color = C(p.AccentDim); Gold.Color = C(p.Gold); Frame.Color = C(p.Frame);
        Muted.Color = C(p.Muted); Dim.Color = C(p.Dim); Bright.Color = C(p.Bright); RowText.Color = C(p.Row);
        HpGreen.GradientStops[0].Color = C(p.HpFrom); HpGreen.GradientStops[1].Color = C(p.HpTo);
        root.CornerRadius = new CornerRadius(p.Radius);
        double inner = Math.Max(0, p.Radius - 1);
        footerBox.CornerRadius = new CornerRadius(0, 0, inner, inner);
        var gold = C(p.Gold);
        band.Background = new LinearGradientBrush(Color.FromArgb(41, gold.R, gold.G, gold.B), Color.FromArgb(10, gold.R, gold.G, gold.B), 90);
        band.BorderBrush = Frame; band.CornerRadius = new CornerRadius(inner, inner, 0, 0);
        medal.BorderBrush = Gold;
        medal.Background = new RadialGradientBrush(Color.FromArgb(255, (byte)(gold.R / 9), (byte)(gold.G / 9), (byte)(gold.B / 9)), Color.FromArgb(255, 10, 10, 9));
        foreach (var (line, i) in dividerLines.Select((l, i) => (l, i)))
            line.Background = new LinearGradientBrush(i % 2 == 0 ? Colors.Transparent : C(p.Frame), i % 2 == 0 ? C(p.Frame) : Colors.Transparent, 0);
    }

    void ApplyLook()
    {
        var k = look;
        System.Windows.Documents.TextElement.SetFontFamily(contentStack, new FontFamily(k.Font));
        System.Windows.Documents.TextElement.SetFontStyle(contentStack, k.Italic ? FontStyles.Italic : FontStyles.Normal);
        System.Windows.Documents.TextElement.SetFontWeight(contentStack, k.Bold ? FontWeights.SemiBold : FontWeights.Normal);
        double r = palette.Radius, inner = Math.Max(0, r - 1);
        patternLayer.CornerRadius = new CornerRadius(inner);
        patternLayer.Background = Pattern(k.Pattern, C(k.PatternColor ?? palette.Accent), k.PatternAlpha);
        topBar.Height = k.TopBar; topBar.CornerRadius = new CornerRadius(inner, inner, 0, 0);
        topBar.Visibility = k.TopBar > 0 ? Visibility.Visible : Visibility.Collapsed;
        topBar.Background = k.TopBarColor switch
        {
            null => Teal,
            "hazard" => Hazard(C(palette.Gold)),
            string g when g.StartsWith("gradient:") => new LinearGradientBrush(C(g[9..].Split(',')[0]), C(g[9..].Split(',')[1]), 0),
            string hex => new SolidColorBrush(C(hex)),
        };
        // The glow is a frame of its own behind the overlay: an effect on the overlay itself would blur its text.
        glowLayer.CornerRadius = new CornerRadius(r);
        glowLayer.BorderBrush = new SolidColorBrush(C(k.GlowColor ?? palette.Accent));
        glowLayer.Effect = k.Glow > 0 ? new System.Windows.Media.Effects.DropShadowEffect { Color = C(k.GlowColor ?? palette.Accent), BlurRadius = k.Glow, ShadowDepth = 0, Opacity = .75 } : null;
        glowLayer.Visibility = k.Glow > 0 ? Visibility.Visible : Visibility.Collapsed;
        root.BorderThickness = new Thickness(k.Border + (ClickThrough ? 0 : 1));
    }
    /// <summary>A tiled pattern of the accent (or its own colour), faint, over the background.</summary>
    static Brush? Pattern(string? kind, Color c, double alpha)
    {
        if (kind is null) return null;
        var color = Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
        (Geometry shape, double w, double h) = kind switch
        {
            "scan" => ((Geometry)new RectangleGeometry(new Rect(0, 0, 4, 1)), 4.0, 3.0),
            "grid" => (new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 18, 1)), new RectangleGeometry(new Rect(0, 0, 1, 18)) } }, 18.0, 18.0),
            "dots" => (new EllipseGeometry(new Point(2, 2), 1.1, 1.1), 11.0, 11.0),
            "hearts" => (Geometry.Parse("M5,9 C1,6 0,4 1.5,2.5 C3,1 4.5,1.8 5,3 C5.5,1.8 7,1 8.5,2.5 C10,4 9,6 5,9 Z"), 24.0, 22.0),
            "stars" => (Geometry.Parse("M5,0 L6.1,3.9 L10,5 L6.1,6.1 L5,10 L3.9,6.1 L0,5 L3.9,3.9 Z"), 26.0, 24.0),
            "petals" => (Geometry.Parse("M4,0 C6,2 6,5 4,7 C2,5 2,2 4,0 Z"), 22.0, 19.0),
            "diagonal" => (Geometry.Parse("M0,8 L8,0 L9,0 L9,1 L1,9 L0,9 Z"), 9.0, 9.0),
            _ => (Geometry.Empty, 1.0, 1.0),
        };
        var brush = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(color), null, shape)) { TileMode = TileMode.Tile, Stretch = Stretch.None,
            Viewport = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }
    /// <summary>Warning stripes, its colour on black.</summary>
    static Brush Hazard(Color c)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(17, 17, 17)), null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(c), null, Geometry.Parse("M0,12 L6,0 L12,0 L6,12 Z")));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Stretch = Stretch.None, Viewport = new Rect(0, 0, 12, 12), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 12, 12), ViewboxUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    // The other windows of the overlay keep their own colours in code: their usual tones are swapped for the style's. A brush
    // made here remembers the one it replaced, so another style (or Clásico) starts again from the original. Meaningful colours
    // (item grades, classes, alerts, the cube) are not in the table and stay.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Brush, Brush> themedFrom = new();
    static readonly Dictionary<(Color, string), SolidColorBrush> themedCache = [];
    readonly Dictionary<Border, CornerRadius> radiusOf = [];
    static readonly Dictionary<int, string> RoleOf = new()
    {
        [0x71DED6] = "accent", [0x5FD3B4] = "accent", [0x69DFE0] = "accent", [0x9FE0C8] = "accent", [0xA8ECD6] = "accent", [0x78DCD3] = "accent",
        [0x4E9A95] = "accent-dim", [0x3FB8AF] = "accent-dim", [0xE9C77E] = "gold",
        [0x33575B] = "frame", [0x2E4A4F] = "frame", [0x2E5A50] = "frame", [0x2E6F69] = "frame", [0x3A5568] = "frame",
        [0x0D141B] = "back", [0x0B1117] = "back", [0x0A0F14] = "back", [0x111A22] = "back",
        [0x101A21] = "back-up", [0x131E27] = "back-up", [0x131E26] = "back-up", [0x13302A] = "back-up", [0x17343A] = "back-up",
        // The toasts' icon tile (1C2B36) is a background: as a frame it took the frame's colour and hid its symbol (2026-10-19).
        [0x22313C] = "lift", [0x1A2A33] = "lift", [0x2B3C48] = "lift", [0x1C2B36] = "lift",
        [0x95A4B6] = "muted", [0xAAB4BE] = "muted", [0x8FB9B4] = "muted", [0x5F7480] = "dim",
        [0xD5DEE6] = "bright", [0xDCE1E9] = "bright", [0xC9D4DD] = "bright", [0xC9D3DD] = "bright", [0xC3CFD9] = "row",
    };
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    Color RoleColor(string role) => role switch
    {
        "accent" => C(palette.Accent), "accent-dim" => C(palette.AccentDim), "gold" => C(palette.Gold), "frame" => C(palette.Frame),
        "back" => C(palette.Back), "back-up" => Mix(C(palette.Back), C(palette.Muted), .06), "lift" => Mix(C(palette.Back), C(palette.Muted), .14),
        "muted" => C(palette.Muted), "dim" => C(palette.Dim), "bright" => C(palette.Bright), _ => C(palette.Row),
    };
    Brush Themed(SolidColorBrush source)
    {
        var c = source.Color;
        if (palette == Turquesa || !RoleOf.TryGetValue(c.R << 16 | c.G << 8 | c.B, out var role)) return source;
        var key = (c, StyleOf(settings.OverlayStyle).Key);
        if (!themedCache.TryGetValue(key, out var brush))
        {
            var to = RoleColor(role);
            brush = new SolidColorBrush(Color.FromArgb(c.A, to.R, to.G, to.B)); brush.Freeze();
            themedCache[key] = brush; themedFrom.AddOrUpdate(brush, source);
        }
        return brush;
    }
    static bool IsLive(Brush b) => ReferenceEquals(b, Teal) || ReferenceEquals(b, Muted) || ReferenceEquals(b, Dim) || ReferenceEquals(b, Gold) || ReferenceEquals(b, Bright)
        || ReferenceEquals(b, RowText) || ReferenceEquals(b, IdleTeal) || ReferenceEquals(b, Frame) || ReferenceEquals(b, MoveFrame);
    void Recolor(DependencyObject o, DependencyProperty property)
    {
        if (o.ReadLocalValue(property) is not SolidColorBrush current || IsLive(current)) return;
        var original = themedFrom.TryGetValue(current, out var from) && from is SolidColorBrush before ? before : current;
        var brush = Themed(original);
        if (!ReferenceEquals(brush, current)) o.SetValue(property, brush);
    }
    void ThemeTree(DependencyObject node)
    {
        switch (node)
        {
            case TextBlock text:
                Recolor(text, TextBlock.ForegroundProperty); Recolor(text, TextBlock.BackgroundProperty);
                foreach (var inline in text.Inlines) Recolor(inline, System.Windows.Documents.TextElement.ForegroundProperty);
                break;
            case Border border: Recolor(border, Border.BackgroundProperty); Recolor(border, Border.BorderBrushProperty); break;
            case Panel panel: Recolor(panel, Panel.BackgroundProperty); break;
            case Shape shape: Recolor(shape, Shape.FillProperty); Recolor(shape, Shape.StrokeProperty); break;
            case Control control: Recolor(control, Control.ForegroundProperty); Recolor(control, Control.BackgroundProperty); Recolor(control, Control.BorderBrushProperty); break;
        }
        int n = node is Visual ? VisualTreeHelper.GetChildrenCount(node) : 0;
        for (int i = 0; i < n; i++) ThemeTree(VisualTreeHelper.GetChild(node, i));
        if (n == 0) foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) ThemeTree(child);
    }
    /// <summary>The style on every window of the overlay that shows (all of them when the style changes): tones, font, corners.</summary>
    void ThemeFamily(bool all = false)
    {
        var font = new FontFamily(look.Font);
        foreach (Window w in new Window[] { this, dock, drops, player, minimap, centerAlert, bossWindow, toast, assistant, assistant.Card })
        {
            if (!all && !w.IsVisible && !(Preview && w == this)) continue;
            ThemeWindow(w, font);
        }
    }
    /// <summary>The style on one window of the overlay (the assistant's card styles itself with it before it shows).</summary>
    internal void ThemeWindow(Window w, FontFamily? font = null)
    {
        if (w.Content is not DependencyObject content) return;
        if (w != this)
        {
            font ??= new FontFamily(look.Font);
            System.Windows.Documents.TextElement.SetFontFamily(content, font);
            System.Windows.Documents.TextElement.SetFontStyle(content, look.Italic ? FontStyles.Italic : FontStyles.Normal);
            System.Windows.Documents.TextElement.SetFontWeight(content, look.Bold ? FontWeights.SemiBold : FontWeights.Normal);
            if (content is Border frame)
            {
                if (!radiusOf.ContainsKey(frame)) radiusOf[frame] = frame.CornerRadius;
                frame.CornerRadius = palette == Turquesa ? radiusOf[frame] : new CornerRadius(palette.Radius);
            }
        }
        ThemeTree(content);
    }
    /// <summary>For the previews: the strip, the loot card of the target and the boss card, styled and taken out of their windows.</summary>
    internal (FrameworkElement Dock, FrameworkElement? Drops, FrameworkElement? Boss, FrameworkElement Toast) PreviewSides(TargetInfo? target)
    {
        bool loot = target?.NpcId is int npc && drops.Load(npc);
        if (target is not null) bossWindow.Preview(target);
        toast.PreviewMessage("Cubo oculto cerca", "El servidor acaba de mostrar un cubo oculto. Sigue la flecha sobre el overlay.");
        ThemeFamily(all: true);
        static FrameworkElement Take(Window w) { var c = (FrameworkElement)w.Content; w.Content = null; return c; }
        return (Take(dock), loot ? Take(drops) : null, target?.GroupDps is > 0 ? Take(bossWindow) : null, Take(toast));
    }

    /// <summary>Places the parts of the current structure in the overlay's body (and the band, the footer, the width).</summary>
    void ArrangeLayout()
    {
        UIElement[] parts = [header, headerTimers, targetRow, noTarget, hpBar, petCard, bossPace, castBanner, castLog, dpsBlock, detail, party, farmBlock,
            lootFeed, objectiveCard, timer, petLine, notice, hint, combatModule, groupModule, petsModule, farmBox, miniHeader, shareBars, petsMini,
            eventTiles, cubeInline, abyssRow, petsHeader, petList, groupPills, chipsRow, medalBlock, groupDivider, petsDivider, cubePill];
        foreach (var part in parts) Detach(part);
        UIElement[] pulido = [header, headerTimers, noTarget, targetRow, hpBar, petCard, bossPace, castBanner, castLog, dpsBlock, detail, party, farmBlock, lootFeed, objectiveCard, timer, petLine, notice, hint];
        UIElement[] order = layout switch
        {
            "pulido" => pulido,
            "modulos" => [header, headerTimers, noTarget, targetRow, hpBar, petCard, bossPace, castBanner, castLog, combatModule, detail, groupModule, petsModule, farmBox, lootFeed, objectiveCard, timer, petLine, notice, hint],
            "minimo" => [miniHeader, targetRow, hpBar, bossPace, castBanner, dpsBlock, shareBars, petsMini, notice, hint],
            "momento" when momentoCalm => [header, eventTiles, cubeInline, abyssRow, petsHeader, petList, groupPills, farmBlock, lootFeed, objectiveCard, notice, hint],
            "momento" => pulido,
            "medallon" => [chipsRow, noTarget, targetRow, hpBar, petCard, bossPace, castBanner, castLog, medalBlock, detail, groupDivider, party, petsDivider, petList, farmBlock, lootFeed, objectiveCard, timer, petLine, notice, hint],
            _ => [header, headerTimers, targetRow, hpBar, petCard, bossPace, castBanner, castLog, dpsBlock, detail, party, farmBlock, lootFeed, objectiveCard, timer, petLine, notice, hint],
        };
        foreach (var part in order) bodyPanel.Children.Add(part);
        if (layout == "modulos")
        {
            combatBody.Children.Add(dpsBlock); groupBody.Children.Add(party); petsBody.Children.Add(petList); farmBox.Child = farmBlock;
            DockPanel.SetDock(cubePill, Dock.Right); header.Children.Insert(header.Children.Count - 1, cubePill);
        }
        if (layout == "medallon") { DockPanel.SetDock(cubePill, Dock.Right); chipsRow.Children.Add(cubePill); chipsRow.Children.Add(headerTimers); }
        rootLayout.Children.Remove(band);
        if (layout == "medallon") rootLayout.Children.Insert(0, band);
        footerBox.Visibility = layout == "minimo" ? Visibility.Collapsed : Visibility.Visible;
        selfRow.Visibility = layout == "medallon" ? Visibility.Collapsed : Visibility.Visible;
        dpsNumbers.Visibility = layout == "minimo" ? Visibility.Collapsed : Visibility.Visible;
        dpsRow.HorizontalAlignment = layout == "minimo" ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        bodyBox.Padding = layout == "minimo" ? new Thickness(11, 8, 11, 8) : new Thickness(14, 10, 14, 10);
        party.Margin = layout is "modulos" ? new Thickness(0) : new Thickness(0, 6, 0, 0);
        FoldModules();
        ApplyCubeLook(layout != "clasico");
        pinKey = ""; petListKey = ""; tilesKey = ""; pillsKey = "";
        if (lastCube is { } cube) UpdateCube(cube);
        ApplyAppearance();
    }
    void ApplyCubeLook(bool compact)
    {
        cubeTitle.Text = compact ? "Cubo oculto" : "CUBO OCULTO CERCA";
        cubeTitle.FontSize = compact ? 11 : 11.5; cubeDetail.FontSize = compact ? 10.5 : 11;
        cubeTextStack.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
        cubeDetail.Margin = compact ? new Thickness(5, 0, 0, 0) : new Thickness(0);
        cubeArrow.FontSize = compact ? 15 : 22; cubeArrow.Margin = new Thickness(0, 0, compact ? 8 : 10, 0);
        cubeDistance.FontSize = compact ? 14 : 17;
        cubeBar.Padding = compact ? new Thickness(10, 4, 10, 4) : new Thickness(10, 6, 10, 6);
    }

    /// <summary>The parts of the structures other than Clásico, after the overlay's own update.</summary>
    void UpdateStyleParts(CaptureSnapshot s, IReadOnlyList<ActorRow> players, bool fighting, TargetInfo? t)
    {
        bool classic = layout == "clasico";
        bool calm = layout == "momento" && !fighting && t is null;
        if (calm != momentoCalm) { momentoCalm = calm; ArrangeLayout(); }
        noTarget.Visibility = t is null ? Visibility.Visible : Visibility.Collapsed;
        targetRow.Visibility = classic || t is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!classic && s.LocalId > 0)
        {
            Figure(dmgLine, Short(s.Damage), "daño");
            if (s.Healing > 0) Figure(healLine, Short(s.Healing), "curación");
            Figure(killLine, s.ConfirmedKills.ToString("N0"), s.ConfirmedKills == 1 ? "criatura" : "criaturas");
        }
        // The event line repeats a chip of the counters row.
        if (!classic && headerTimers.Children.Count > 1) timer.Visibility = Visibility.Collapsed;
        if (layout == "minimo") dps.Text = s.LocalId > 0 ? Short(s.Dps) : "—";
        UpdatePetList(s.Pet?.Pinned);
        bool pets = petList.Children.Count > 0 && settings.OverlayShowPet;
        petsModule.Visibility = petsDivider.Visibility = petsHeader.Visibility = pets ? Visibility.Visible : Visibility.Collapsed;
        petsMini.Visibility = pets ? Visibility.Visible : Visibility.Collapsed;
        petsTitle.Text = $"MASCOTAS · {petList.Children.Count}"; petsHeaderCount.Text = petList.Children.Count.ToString();
        // A block or a divider says what the group is: its own first line goes.
        groupTitle.Text = "GRUPO"; groupDividerText.Text = s.Troop is { } troopNow ? $"GRUPO {troopNow.OwnGroup}" : "GRUPO";
        if (layout is "modulos" or "medallon" && party.Children.Count > 0 && party.Children[0] is TextBlock first)
        {
            groupTitle.Text = first.Text; party.Children.RemoveAt(0);
        }
        groupModule.Visibility = groupDivider.Visibility = party.Visibility;
        farmBox.Visibility = farmBlock.Visibility;
        miniDot.Fill = stateDot.Fill; miniClock.Text = clock.Text;
        if (layout == "minimo") { BuildMiniChips(); BuildShareBars(s, players); }
        clock.Visibility = momentoCalm ? Visibility.Collapsed : Visibility.Visible;
        lastFight.Visibility = momentoCalm ? Visibility.Visible : Visibility.Collapsed;
        if (momentoCalm)
        {
            state.Text = "FUERA DE COMBATE";
            lastFight.Inlines.Clear();
            if (s.Dps > 0)
            {
                lastFight.Inlines.Add(new Run("último: ") { Foreground = Muted });
                lastFight.Inlines.Add(new Run($"{s.Dps:N0} DPS") { Foreground = IdleTeal, FontWeight = FontWeights.SemiBold });
                if (clock.Text.Length > 0) lastFight.Inlines.Add(new Run(" · " + clock.Text) { Foreground = Muted });
            }
            BuildEventTiles(); UpdateAbyssRow(); BuildGroupPills(s);
        }
        if (layout == "medallon")
        {
            medalDps.Text = s.LocalId > 0 ? Short(s.Dps) : "—"; medalDps.Foreground = fighting ? Brushes.White : Bright;
            medalDamage.Text = Short(s.Damage); medalHeal.Text = Short(s.Healing); medalKills.Text = s.ConfirmedKills.ToString("N0");
            bandIcon.Source = selfIcon.Source; bandIcon.Visibility = bandIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
            bandName.Text = string.Join(" · ", new[] { s.Character, selfClassName }.Where(x => x is { Length: > 0 })).ToUpperInvariant();
            bandState.Text = clock.Text.Length > 0 ? $"{state.Text} · {clock.Text}" : state.Text;
        }
    }
    void Figure(TextBlock line, string value, string label)
    {
        line.Inlines.Clear();
        line.Inlines.Add(new Run(value) { Foreground = Bright, FontWeight = FontWeights.SemiBold });
        line.Inlines.Add(new Run(" " + label));
    }
    string? ClassOfPlayer(string name, CaptureSnapshot s)
    {
        if (s.Troop?.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.ClassKey is { } fromTroop) return fromTroop;
        if (s.Room?.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.ClassKey is { } fromRoom) return fromRoom;
        return PlayerProfiles.Get(name, s.LocalServerId ?? settings.LastServerId, PlayerProfiles.RegionOf(settings.RegionOffset)).Card is { } card && ClassKeys.TryGetValue(card.ClassName, out var key) ? key : null;
    }
    static Color ClassColor(string? key) => key is not null && ClassColors.TryGetValue(key, out var c) ? c : Color.FromRgb(0x3F, 0xB8, 0xAF);
    static string When(DateTime at)
    {
        var left = at - DateTime.Now;
        return left <= TimeSpan.Zero ? "ahora" : left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{left.Minutes}:{left.Seconds:00}";
    }
    (RankingWatch.Entry? Class, RankingWatch.Entry? All) Rank() => settings.OverlayShowRanking && AbyssRank?.Invoke() is { } r ? r : (null, null);

    void BuildMiniChips()
    {
        miniChips.Children.Clear();
        var (mine, all) = Rank();
        if ((mine ?? all) is { } shown)
            miniChips.Children.Add(Row(new Image { Source = Icon("/assets/game/Icon_Exploit_Medal_lv4_01.webp"), Width = 13, Height = 13, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center },
                Strong(Label($"{shown.Rank:N0}", 10.5, Brushes.White))));
        foreach (var (icon, _, color, at) in lastTimers.Where(i => i.Icon is "♛" or "✦" && i.At > DateTime.Now.AddMinutes(-2)).Take(2))
        {
            var chip = Label($"{icon} {When(at)}", 10.5, Paint(color)); chip.Margin = new Thickness(9, 0, 0, 0); miniChips.Children.Add(chip);
        }
        if (settings.CubeAlerts && lastCube is { Active: not null, Distance: double d })
        {
            var chip = Label($"➤ {(d >= 1000 ? $"{d / 1000:0.0} km" : $"{d:0} m")}", 10.5, Paint("#C9A6FF")); chip.Margin = new Thickness(9, 0, 0, 0); miniChips.Children.Add(chip);
        }
    }
    static TextBlock Strong(TextBlock text) { text.FontWeight = FontWeights.SemiBold; return text; }
    void BuildShareBars(CaptureSnapshot s, IReadOnlyList<ActorRow> players)
    {
        shareBars.Children.Clear();
        var top = players.Where(p => p.Damage > 0).Take(6).ToList();
        shareBars.Visibility = top.Count > 0 && settings.OverlayShowParty ? Visibility.Visible : Visibility.Collapsed;
        if (top.Count == 0) return;
        long best = Math.Max(1, top[0].Damage), sum = Math.Max(1, players.Sum(p => p.Damage));
        foreach (var a in top)
        {
            string? cls = ClassOfPlayer(a.Name, s); var c = ClassColor(cls);
            double fill = Math.Clamp((double)a.Damage / best, 0, 1);
            var grid = new Grid { Height = 18, Margin = new Thickness(0, 2, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fill, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fill, GridUnitType.Star) });
            var track = new Border { Background = Track, CornerRadius = new CornerRadius(5) }; Grid.SetColumnSpan(track, 2);
            var bar = new Border { CornerRadius = new CornerRadius(5), Background = new LinearGradientBrush(Color.FromArgb(217, c.R, c.G, c.B), Color.FromArgb(115, c.R, c.G, c.B), 0) };
            bool self = a.Id == s.LocalId;
            var pct = Label($"{(double)a.Damage / sum * 100:0} %", 10.5, Brushes.White); if (self) pct.FontWeight = FontWeights.SemiBold;
            var name = Label(a.Name, 10.5, Brushes.White); if (self) name.FontWeight = FontWeights.SemiBold;
            var text = new DockPanel { Margin = new Thickness(7, 0, 7, 0) }; Grid.SetColumnSpan(text, 2);
            DockPanel.SetDock(pct, Dock.Right); text.Children.Add(pct);
            if (cls is not null) { var icon = new Image { Source = Icon($"/assets/ui/class_icon_{cls}.png"), Width = 13, Height = 13, Margin = new Thickness(0, 0, 5, 0) }; DockPanel.SetDock(icon, Dock.Left); text.Children.Add(icon); }
            text.Children.Add(name);
            grid.Children.Add(track); grid.Children.Add(bar); grid.Children.Add(text);
            shareBars.Children.Add(grid);
        }
    }
    void BuildEventTiles()
    {
        var items = lastTimers.Where(i => i.At > DateTime.Now.AddMinutes(-2)).Take(3).ToList();
        string key = string.Join("|", items.Select(i => $"{i.Name}:{When(i.At)}"));
        eventTiles.Visibility = items.Count > 0 && settings.OverlayShowHeaderTimers ? Visibility.Visible : Visibility.Collapsed;
        if (key == tilesKey) return;
        tilesKey = key; eventTiles.Children.Clear(); eventTiles.ColumnDefinitions.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            var (icon, name, color, at) = items[i];
            var c = C(color);
            eventTiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 && icon == "♛" ? 1.25 : 1, GridUnitType.Star) });
            var stack = new StackPanel();
            stack.Children.Add(Label(icon == "♛" ? "♛ JEFE DE CAMPO" : $"{icon} {name.ToUpperInvariant()}", 9.5, new SolidColorBrush(c)));
            stack.Children.Add(Strong(Label(When(at), 16, Brushes.White)));
            if (icon == "♛") stack.Children.Add(Label(name, 9.5, Muted));
            var tile = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 3, 7, 4), Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B)), Background = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)), Child = stack, ToolTip = $"{name} · {at:HH:mm}" };
            Grid.SetColumn(tile, i); eventTiles.Children.Add(tile);
        }
    }
    void UpdateAbyssRow()
    {
        var (mine, all) = Rank();
        abyssRow.Visibility = mine is null && all is null ? Visibility.Collapsed : Visibility.Visible;
        abyssText.Inlines.Clear();
        void Place(RankingWatch.Entry e, string label)
        {
            if (abyssText.Inlines.Count > 0) abyssText.Inlines.Add(new Run(" · ") { Foreground = Muted });
            abyssText.Inlines.Add(new Run($"#{e.Rank:N0}") { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            if (label.Length > 0) abyssText.Inlines.Add(new Run(" " + label) { Foreground = Muted });
            if (e.Change != 0) abyssText.Inlines.Add(new Run(Arrow(e.Change)) { Foreground = ArrowColor(e.Change) });
        }
        if (mine is not null) Place(mine, "clase");
        if (all is not null) Place(all, "");
        abyssRow.ToolTip = rankPill.ToolTip;
    }
    void BuildGroupPills(CaptureSnapshot s)
    {
        var members = s.Troop is { } troop ? troop.Members.Where(m => m.Group == troop.OwnGroup).Select(m => (m.Name, m.ClassKey)).ToList()
            : (s.Party ?? []).Select(n => (Name: n, ClassKey: (string?)null)).ToList();
        groupPills.Visibility = members.Count > 1 && settings.OverlayShowParty ? Visibility.Visible : Visibility.Collapsed;
        string key = string.Join("|", members.Select(m => m.Name + m.ClassKey)) + s.Character;
        if (key == pillsKey) return;
        pillsKey = key; groupPills.Children.Clear();
        var title = Label(s.Troop is { } tr ? $"GRUPO {tr.OwnGroup}" : "GRUPO", 9.5, Dim); title.Margin = new Thickness(0, 0, 7, 3); groupPills.Children.Add(title);
        foreach (var (name, cls) in members)
        {
            string? key2 = cls ?? ClassOfPlayer(name, s); var c = ClassColor(key2);
            bool self = name.Equals(s.Character, StringComparison.OrdinalIgnoreCase);
            var text = Label(name, 10.5, self ? Teal : RowText);
            var content = key2 is null ? (UIElement)text : Row(new Image { Source = Icon($"/assets/ui/class_icon_{key2}.png"), Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }, text);
            groupPills.Children.Add(new Border { Child = content, CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 1, 7, 1), Margin = new Thickness(0, 0, 4, 3),
                BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = new SolidColorBrush(c), Background = new SolidColorBrush(Color.FromArgb(64, c.R, c.G, c.B)) });
        }
    }

    /// <summary>The followed pets inside the overlay: a round portrait in its race's colour, the name, the progress and the souls left.</summary>
    void UpdatePetList(IReadOnlyList<PetStatus>? pins)
    {
        pins ??= [];
        string key = layout + "|" + string.Join("|", pins.Select(p => $"{p.PetId}:{p.Level}:{p.Progress}:{p.Threshold}"));
        if (key == petListKey) return;
        petListKey = key; petList.Children.Clear(); petsMini.Children.Clear();
        foreach (var p in pins) { petList.Children.Add(PetRow(p)); petsMini.Children.Add(PetChip(p)); }
    }
    Border PetPortrait(PetStatus p, double size)
    {
        var race = BossPacePanel.RaceOf(p.Source);
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, size > 20 ? 8 : 4, 0), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = race is { } r ? Paint(r.Color) : Frame, Background = Icon(petIcons.GetValueOrDefault(p.PetId)) is { } image ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : Track };
    }
    UIElement PetRow(PetStatus p)
    {
        bool max = p.Threshold == 0, near = !max && p.Remaining <= Math.Max(3, p.Threshold / 10);
        double ratio = max ? 1 : Math.Clamp(p.Progress / (double)p.Threshold, 0, 1);
        var name = new TextBlock { FontSize = 11.5, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        name.Inlines.Add(new Run(p.Name));
        if (p.Level > 0) name.Inlines.Add(new Run($"  Nv. {p.Level}") { FontWeight = FontWeights.Normal, FontSize = 10.5, Foreground = Muted });
        var left = Label(max ? "MÁX." : $"{p.Progress}/{p.Threshold} · faltan {p.Remaining}", 10.5, max ? Gold : near ? Teal : Muted);
        if (max || near) left.FontWeight = FontWeights.SemiBold;
        left.Margin = new Thickness(8, 0, 0, 0);
        var top = new DockPanel(); DockPanel.SetDock(left, Dock.Right); top.Children.Add(left); top.Children.Add(name);
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
        var track = new Border { Background = Track, CornerRadius = new CornerRadius(2) }; Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track); bar.Children.Add(new Border { Background = max ? Gold : layout == "medallon" ? Gold : Teal, CornerRadius = new CornerRadius(2) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(top); text.Children.Add(bar);
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var portrait = PetPortrait(p, 26); DockPanel.SetDock(portrait, Dock.Left); row.Children.Add(portrait); row.Children.Add(text);
        return row;
    }
    UIElement PetChip(PetStatus p)
    {
        bool near = p.Threshold > 0 && p.Remaining <= Math.Max(3, p.Threshold / 10);
        var progress = Label(p.Threshold == 0 ? " MÁX." : $" {p.Progress}/{p.Threshold}", 10.5, near ? Teal : Muted); if (near) progress.FontWeight = FontWeights.SemiBold;
        var row = Row(PetPortrait(p, 16), Label(p.Name, 10.5, Muted), progress); row.Margin = new Thickness(0, 0, 10, 2);
        return row;
    }
    /// <summary>The crystal of a race (the left end of its badge), for the race chips.</summary>
    internal static ImageSource? Crystal(string? race)
    {
        if (Icon(BadgeOf(race)) is not BitmapSource badge || badge.PixelWidth < 54) return null;
        var crystal = new CroppedBitmap(badge, new Int32Rect(0, 0, 54, badge.PixelHeight)); crystal.Freeze();
        return crystal;
    }
}
