using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// Your whole official profile for the panel's «Mi perfil» (2026-10-24, the user, with screenshots of Shugo.GG's profile:
/// «en el perfil de la app hay que mejorarlo más»): titles, divine stats with their effects, the gear by group with every
/// piece's detail (base and extra stats, mana stones, divine stone, soul binding, origin), skills, the Daevanion boards
/// (Azphel included) with their nodes and active effects, and appearances. All from NCSOFT's public profile API, the one the
/// player cards already read: the base in two calls, then each board and each piece in the background, so the tab fills in.
/// </summary>
public sealed class ProfileLoad
{
    public readonly PlayerCard Card;
    readonly object gate = new();
    object? info;
    string? error;
    readonly Dictionary<int, object> items = [];
    readonly Dictionary<int, object> boards = [];
    int itemsTotal, boardsTotal;
    /// <summary>Raises with every part that arrives (the panel gets the view again).</summary>
    public int Version { get; private set; }
    public bool Done { get; private set; }

    public ProfileLoad(PlayerCard card) { Card = card; _ = Run(); }

    public object View()
    {
        lock (gate)
        {
            var all = boards.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var (id, view) in ExtraBoards(boards.Keys.ToList()).ToList()) all[id] = view;
            return new { info, error, items = items.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), boards = all.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList(),
                itemsLoaded = items.Count, itemsTotal, boardsLoaded = boards.Count, boardsTotal, done = Done, fetchedAt = Card.FetchedAt, characterId = Card.CharacterId };
        }
    }
    void Changed() { lock (gate) Version++; }
    /// <summary>Your open nodes arrived from the game: the view changes.</summary>
    public void Touch() => Changed();

    // Boards the official profile does not give yet (Ariel, 2026-10-24): the layout from daevanion-extra.json (the Korean
    // profile's, the same for every class; build-companion-daevanion-extra.py) and your open nodes from the game (0x26E2,
    // DaevanionNodes). Their values differ between Korea and here, so the effects carry names only and are counted by node.
    sealed record ExtraNode(int Cell, int Row, int Col, string Type, string Grade, string Name, string[] Effects);
    sealed record ExtraBoard(int Index, string Name, List<ExtraNode> Nodes);
    static readonly Lazy<List<ExtraBoard>> extra = new(() =>
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "daevanion-extra.json")));
            return doc.RootElement.GetProperty("boards").EnumerateArray().Select(b => new ExtraBoard(b.GetProperty("index").GetInt32(), b.GetProperty("name").GetString()!,
                b.GetProperty("nodes").EnumerateArray().Select(n => new ExtraNode(n[0].GetInt32(), n[1].GetInt32(), n[2].GetInt32(), n[3].GetString()!, n[4].GetString()!, n[5].GetString()!,
                    n[6].EnumerateArray().Select(e => e.GetString()!).ToArray())).ToList())).ToList();
        }
        catch { return []; }
    });
    IEnumerable<(int Id, object View)> ExtraBoards(IEnumerable<int> known)
    {
        var mine = DaevanionNodes.For(Card.Name, Card.ServerId);
        // The class's board ids: 11-16 for a Gladiator, 21-26 for a Templar...
        int classBase = known.Concat(mine?.Boards.Keys ?? Enumerable.Empty<int>()).Where(id => id >= 10).Select(id => id / 10).FirstOrDefault();
        if (classBase == 0) yield break;
        foreach (var b in extra.Value)
        {
            int id = classBase * 10 + b.Index;
            if (known.Contains(id)) continue;
            var open = mine?.Boards.GetValueOrDefault(id)?.ToHashSet() ?? [];
            var nodes = b.Nodes.Select(n => new { id = (long)id * 10000 + n.Cell, row = n.Row, col = n.Col, type = n.Type, grade = n.Grade, name = n.Name, open = open.Contains(id * 10000 + n.Cell), effects = n.Effects }).ToList();
            var counted = nodes.Where(n => n.open && n.type != "Start").SelectMany(n => n.effects).GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => new { name = g.Key, nodes = g.Count() }).ToList();
            yield return (id, new { id, name = b.Name, open = nodes.Count(n => n.open && n.type != "Start"), total = nodes.Count(n => n.type != "Start"), nodes,
                statEffects = Array.Empty<string>(), skillEffects = Array.Empty<string>(), counted, estimated = true, capturedAt = mine?.Boards.ContainsKey(id) == true ? mine.At : (DateTime?)null });
        }
    }

    async Task Run()
    {
        try
        {
            var infoTask = PlayerProfiles.Official(Card, "info");
            var gearTask = PlayerProfiles.Official(Card, "equipment");
            JsonElement profile = await infoTask, equipment = await gearTask;
            var gear = Gear(equipment);
            var boardList = profile.TryGetProperty("daevanion", out var dv) && dv.ValueKind == JsonValueKind.Object && dv.TryGetProperty("boardList", out var bl) && bl.ValueKind == JsonValueKind.Array ? bl.EnumerateArray().ToList() : [];
            lock (gate) { info = Base(profile, equipment, gear, boardList); itemsTotal = gear.Count; boardsTotal = boardList.Count; }
            Changed();
            // Each board, then each piece: four requests at a time, as the profile's own page would ask for them.
            using var slots = new SemaphoreSlim(4);
            async Task Board(JsonElement b)
            {
                int id = N(b, "id");
                await slots.WaitAsync();
                try { var detail = await PlayerProfiles.Official(Card, "daevanion/detail", "&boardId=" + id); lock (gate) boards[id] = BoardView(b, detail); }
                catch (Exception e) { lock (gate) boards[id] = new { id, name = S(b, "name"), open = N(b, "openNodeCount"), total = N(b, "totalNodeCount"), error = e.Message }; }
                finally { slots.Release(); }
                Changed();
            }
            async Task Item(GearRow g)
            {
                await slots.WaitAsync();
                try { if (await PlayerProfiles.ItemDetail(Card, g) is { } d) lock (gate) items[g.SlotPos] = ItemView(d, g); }
                catch { lock (gate) items[g.SlotPos] = new { failed = true }; }
                finally { slots.Release(); }
                Changed();
            }
            await Task.WhenAll(boardList.Select(Board).Concat(gear.Select(Item)));
        }
        catch (Exception e) { lock (gate) error = e.Message; }
        Done = true; Changed();
    }

    static List<GearRow> Gear(JsonElement equipment)
    {
        var list = new List<GearRow>();
        if (Obj(equipment, "equipment", out var inner) && inner.TryGetProperty("equipmentList", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var it in items.EnumerateArray())
                list.Add(new(S(it, "slotPosName"), S(it, "name"), S(it, "grade"), N(it, "enchantLevel"), S(it, "icon"), L(it, "id"), N(it, "slotPos"), N(it, "exceedLevel")));
        return list;
    }

    // Weapons, armour, accessories, runes and arcanas, as the game's character window groups them.
    static string GroupOf(string slot) => slot switch
    {
        "MainHand" or "SubHand" => "weapons",
        "Helmet" or "Shoulder" or "Torso" or "Pants" or "Gloves" or "Boots" or "Cape" => "armor",
        _ when slot.StartsWith("Rune", StringComparison.Ordinal) => "runes",
        _ when slot.StartsWith("Arcana", StringComparison.Ordinal) => "arcana",
        _ => "accessories",
    };
    static readonly string[] SlotOrder = ["MainHand", "SubHand", "Helmet", "Shoulder", "Torso", "Pants", "Gloves", "Boots", "Cape",
        "Necklace", "Earring1", "Earring2", "Ring1", "Ring2", "Bracelet1", "Bracelet2", "Belt", "Amulet", "Rune1", "Rune2", "Arcana1", "Arcana2", "Arcana3", "Arcana4", "Arcana5"];

    // A section that is an object: the official API sends some as null (TryGetProperty on null throws and the whole profile fails).
    static bool Obj(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;
    }
    static object Base(JsonElement info, JsonElement equipment, List<GearRow> gear, List<JsonElement> boardList)
    {
        var p = info.GetProperty("profile");
        var stats = new List<object>(); int itemLevel = 0;
        if (Obj(info, "stat", out var st) && st.TryGetProperty("statList", out var sl) && sl.ValueKind == JsonValueKind.Array)
            foreach (var s in sl.EnumerateArray())
            {
                if (S(s, "type") == "ItemLevel") { itemLevel = N(s, "value"); continue; }
                stats.Add(new { type = S(s, "type"), name = S(s, "name"), value = N(s, "value"), effects = Texts(s, "statSecondList") });
            }
        var titles = new List<object>(); int titlesOwned = 0, titlesTotal = 0;
        if (info.TryGetProperty("title", out var tt) && tt.ValueKind == JsonValueKind.Object)
        {
            titlesOwned = N(tt, "ownedCount"); titlesTotal = N(tt, "totalCount");
            if (tt.TryGetProperty("titleList", out var tl) && tl.ValueKind == JsonValueKind.Array)
                foreach (var t in tl.EnumerateArray())
                    titles.Add(new { id = L(t, "id"), name = S(t, "name"), grade = S(t, "grade"), category = S(t, "equipCategory"), owned = N(t, "ownedCount"), total = N(t, "totalCount"),
                        stats = Descs(t, "statList"), equip = Descs(t, "equipStatList") });
        }
        var skills = new List<object>();
        if (Obj(equipment, "skill", out var sk))
            foreach (string list in new[] { "skillList", "stigmaList" })
                if (sk.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array)
                    foreach (var s in items.EnumerateArray())
                        skills.Add(new { id = L(s, "id"), name = S(s, "name"), category = list == "stigmaList" ? "Stigma" : S(s, "category"), level = N(s, "skillLevel"), equip = N(s, "equip") == 1,
                            acquired = N(s, "acquired") == 1, needLevel = N(s, "needLevel"), icon = S(s, "icon") });
        var skins = new List<object>();
        if (Obj(equipment, "equipment", out var eq) && eq.TryGetProperty("skinList", out var skl) && skl.ValueKind == JsonValueKind.Array)
            foreach (var s in skl.EnumerateArray()) skins.Add(new { slot = S(s, "slotPosName"), name = S(s, "name"), grade = S(s, "grade"), icon = S(s, "icon") });
        object? pet = null, wing = null, wingSkin = null;
        if (equipment.TryGetProperty("petwing", out var pw) && pw.ValueKind == JsonValueKind.Object)
        {
            if (pw.TryGetProperty("pet", out var pe) && pe.ValueKind == JsonValueKind.Object) pet = new { name = S(pe, "name"), level = N(pe, "level"), icon = S(pe, "icon") };
            if (pw.TryGetProperty("wing", out var wi) && wi.ValueKind == JsonValueKind.Object) wing = new { name = S(wi, "name"), grade = S(wi, "grade"), enchant = N(wi, "enchantLevel"), icon = S(wi, "icon") };
            if (pw.TryGetProperty("wingSkin", out var ws) && ws.ValueKind == JsonValueKind.Object) wingSkin = new { name = S(ws, "name"), grade = S(ws, "grade"), icon = S(ws, "icon") };
        }
        return new
        {
            name = S(p, "characterName"), className = S(p, "className"), race = S(p, "raceName"), level = N(p, "characterLevel"), server = S(p, "serverName"),
            legion = S(p, "regionName"), title = S(p, "titleName"), titleGrade = S(p, "titleGrade"), portrait = S(p, "profileImage"), combatPower = L(p, "combatPower"), itemLevel,
            stats, titles, titlesOwned, titlesTotal, skills, skins, pet, wing, wingSkin,
            gear = gear.OrderBy(g => Array.IndexOf(SlotOrder, g.Slot) is int i && i >= 0 ? i : 99)
                .Select(g => new { slot = g.Slot, slotPos = g.SlotPos, group = GroupOf(g.Slot), id = g.Id, name = g.Name, grade = g.Grade, enchant = g.Enchant, exceed = g.Exceed, icon = g.Icon }).ToList(),
            boards = boardList.Select(b => new { id = N(b, "id"), name = S(b, "name"), open = N(b, "openNodeCount"), total = N(b, "totalNodeCount") }).ToList(),
        };
    }

    static object BoardView(JsonElement b, JsonElement d)
    {
        var nodes = new List<object>();
        if (d.TryGetProperty("nodeList", out var nl) && nl.ValueKind == JsonValueKind.Array)
            foreach (var n in nl.EnumerateArray())
            {
                if (S(n, "type") is "None" or "") continue;
                nodes.Add(new { id = L(n, "nodeId"), row = N(n, "row"), col = N(n, "col"), type = S(n, "type"), grade = S(n, "grade"), name = S(n, "name"), open = N(n, "open") == 1,
                    effects = Descs(n, "effectList") });
            }
        return new { id = N(b, "id"), name = S(b, "name"), open = N(b, "openNodeCount"), total = N(b, "totalNodeCount"), nodes,
            statEffects = Descs(d, "openStatEffectList"), skillEffects = Descs(d, "openSkillEffectList") };
    }

    static object ItemView(JsonElement d, GearRow g) => new
    {
        name = S(d, "name"), grade = S(d, "grade"), gradeName = S(d, "gradeName"), category = S(d, "categoryName"), level = N(d, "level"), equipLevel = N(d, "equipLevel"),
        enchant = g.Enchant, maxEnchant = N(d, "maxEnchantLevel"), exceed = g.Exceed, maxExceed = N(d, "maxExceedEnchantLevel"), classes = Texts(d, "classNames"),
        main = Arr(d, "mainStats").Select(s => new { name = S(s, "name"), value = S(s, "value"), extra = S(s, "extra"), exceed = s.TryGetProperty("exceed", out var x) && x.ValueKind == JsonValueKind.True }).ToList(),
        sub = Arr(d, "subStats").Select(s => new { name = S(s, "name"), value = S(s, "value") }).ToList(),
        subMax = N(d, "subStatCount"), skillMax = N(d, "subSkillCountMax"),
        skills = Arr(d, "subSkills").Select(s => new { name = S(s, "name"), level = N(s, "level"), icon = S(s, "icon") }).ToList(),
        stones = Arr(d, "magicStoneStat").Select(s => new { name = S(s, "name"), value = S(s, "value"), grade = S(s, "grade"), icon = S(s, "icon") }).ToList(),
        stoneSlots = N(d, "magicStoneSlotCount"),
        godStones = Arr(d, "godStoneStat").Select(s => new { name = S(s, "name"), desc = S(s, "desc"), grade = S(s, "grade"), icon = S(s, "icon") }).ToList(),
        godSlots = N(d, "godStoneSlotCount"),
        soulBind = S(d, "soulBindRate"), costumes = Texts(d, "costumes"), sources = Texts(d, "sources"),
        tradable = d.TryGetProperty("tradable", out var tr) && tr.ValueKind == JsonValueKind.True,
    };

    static IEnumerable<JsonElement> Arr(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
    static List<string> Texts(JsonElement e, string k) => Arr(e, k).Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0).ToList();
    static List<string> Descs(JsonElement e, string k) => Arr(e, k).Select(x => S(x, "desc")).Where(x => x.Length > 0).ToList();
    static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
    static int N(JsonElement e, string k) => int.TryParse(S(e, k), out int n) ? n : 0;
    static long L(JsonElement e, string k) => long.TryParse(S(e, k), out long n) ? n : 0;
}
