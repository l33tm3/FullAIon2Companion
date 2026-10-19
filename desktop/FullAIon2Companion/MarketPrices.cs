using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>A market price of one item on one server: kinah per unit, who gave it and when.</summary>
public sealed record MarketQuote(long Price, string Source, DateTime At);

/// <summary>
/// One item of a server's Market as the source last saw it: the lowest price listed now (null when nobody sells it now),
/// the last price seen, how many listings, the change over 24 h (%) and when it was seen.
/// </summary>
public sealed record MarketRow(int Item, string Name, string Section, long? Price, long? LastPrice, int Listings, double? Change, DateTime At);

/// <summary>Everything the source has for one server (the game's server id: 2401 Israphel), and when it was fetched.</summary>
public sealed record MarketBoard(int Server, DateTime FetchedAt, IReadOnlyList<MarketRow> Rows);

/// <summary>A server the source has data for: the game's id, its name, region (LA, EU…) and faction (Elyos, Asmodian).</summary>
public sealed record MarketServer(int Id, string Name, string Region, string RegionName, string Faction);

/// <summary>
/// Where Market prices come from. The game's Market lists travel over HTTPS outside the game connection
/// (docs/market-capture-research.md), so the capture cannot read them: they come from a source like aion2.exchange
/// (Aion2Exchange), which records them per server every hour.
/// </summary>
public interface IMarketSource
{
    /// <summary>Credited in the Mercado tab.</summary>
    string Name { get; }
    Task<IReadOnlyList<MarketServer>> ServersAsync(CancellationToken cancel);
    Task<MarketBoard> BoardAsync(int server, CancellationToken cancel);
}

/// <summary>
/// The prices of the Mercado tab and of the loot HUD, kept on this computer (market-prices.json): each server's whole
/// Market as the source last gave it, asked again after 30 minutes (the source takes a picture every hour), the servers
/// it covers, and the player's own prices, per server, which win over the source's.
/// </summary>
public sealed class MarketPrices
{
    sealed class Book
    {
        public Dictionary<int, Dictionary<int, MarketQuote>> Manual { get; set; } = [];
        public Dictionary<int, MarketBoard> Boards { get; set; } = [];
        public List<MarketServer> Servers { get; set; } = [];
        public DateTime ServersAt { get; set; }
    }
    static readonly TimeSpan Fresh = TimeSpan.FromMinutes(30), Retry = TimeSpan.FromMinutes(2), ServersFresh = TimeSpan.FromDays(1);
    readonly object gate = new();
    readonly Dictionary<int, Dictionary<int, MarketRow>> index = [];
    readonly Dictionary<int, DateTime> tried = [];
    Book book = new();
    string? path;
    bool fetching;
    string status = "";
    /// <summary>The source of Market prices (aion2.exchange in the app; none in tests unless they set one).</summary>
    public static IMarketSource? Source { get; set; }
    /// <summary>The live app's prices, for the overlay.</summary>
    public static MarketPrices? Live { get; set; }
    /// <summary>Raises whenever a price, a board or the server list changes.</summary>
    public int Version { get; private set; }
    /// <summary>Why the last request failed, or empty.</summary>
    public string Status { get { lock (gate) return status; } }

    public void Open(string file)
    {
        path = file;
        try { book = JsonSerializer.Deserialize<Book>(File.ReadAllText(file)) ?? new(); } catch { book = new(); }
        book.Manual ??= []; book.Boards ??= []; book.Servers ??= [];
        lock (gate) foreach (var (server, board) in book.Boards) Index(server, board);
    }
    void Index(int server, MarketBoard board) => index[server] = board.Rows.GroupBy(r => r.Item).ToDictionary(g => g.Key, g => g.First());
    // One save at a time, each writing the book as it is then: a price typed while a board is being saved is not lost under it.
    readonly object saving = new();
    void Save()
    {
        if (path is null) return;
        lock (saving)
        {
            string json; lock (gate) json = JsonSerializer.Serialize(book);
            try { File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true); } catch { }
        }
    }

    public MarketQuote? Manual(int server, int item) { lock (gate) return book.Manual.GetValueOrDefault(server)?.GetValueOrDefault(item); }
    /// <summary>The source's row for an item on a server, if its last board has it.</summary>
    public MarketRow? Row(int server, int item) { lock (gate) return index.GetValueOrDefault(server)?.GetValueOrDefault(item); }
    /// <summary>The source's price: the lowest listed now, else the last one seen.</summary>
    public MarketQuote? Market(int server, int item) => Row(server, item) is { } r && (r.Price ?? r.LastPrice) is long p and > 0 ? new(p, Source?.Name ?? "Mercado", r.At) : null;
    /// <summary>The price used for values: the player's own, else the Market's.</summary>
    public long? Unit(int server, int item) => (Manual(server, item) ?? Market(server, item))?.Price;
    public MarketBoard? Board(int server) { lock (gate) return book.Boards.GetValueOrDefault(server); }
    public IReadOnlyList<MarketServer> Servers { get { lock (gate) return book.Servers.ToList(); } }

    /// <summary>The player's own price of an item on a server; 0 or less forgets it.</summary>
    public void SetManual(int server, int item, long price)
    {
        if (server <= 0) throw new InvalidOperationException("Entra al juego con el personaje para saber en qué servidor guardar el precio.");
        if (!InventoryWatch.Known(item)) throw new InvalidOperationException("Objeto desconocido.");
        lock (gate)
        {
            if (price <= 0) book.Manual.GetValueOrDefault(server)?.Remove(item);
            else
            {
                if (!book.Manual.TryGetValue(server, out var prices)) book.Manual[server] = prices = [];
                prices[item] = new(Math.Min(price, 1_000_000_000_000), "tú", DateTime.Now);
            }
            Version++;
        }
        Save();
    }

    /// <summary>
    /// Asks the source, in the background and one request at a time, for the server's Market when the last one is over
    /// 30 minutes old (and for its server list once a day); a failure waits 2 minutes before trying that server again.
    /// </summary>
    public void Want(int server)
    {
        if (Source is not { } source || server <= 0) return;
        bool board, servers;
        lock (gate)
        {
            if (fetching) return;
            board = !book.Boards.TryGetValue(server, out var b) || DateTime.UtcNow - b.FetchedAt > Fresh;
            servers = book.Servers.Count == 0 || DateTime.UtcNow - book.ServersAt > ServersFresh;
            if (!board && !servers) return;
            if (tried.TryGetValue(server, out var t) && DateTime.UtcNow - t < Retry) return;
            tried[server] = DateTime.UtcNow; fetching = true;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                if (servers)
                {
                    var list = await source.ServersAsync(cancel.Token);
                    lock (gate) { book.Servers = list.ToList(); book.ServersAt = DateTime.UtcNow; Version++; }
                }
                if (board)
                {
                    var got = await source.BoardAsync(server, cancel.Token);
                    lock (gate) { book.Boards[server] = got; Index(server, got); Version++; }
                }
                lock (gate) status = "";
                Save();
            }
            catch (Exception ex) { lock (gate) { status = $"{source.Name}: {ex.Message}"; Version++; } }
            finally { lock (gate) fetching = false; }
        });
    }

    /// <summary>
    /// «Mi inventario»: the character's inventory (currencies apart) with each item's price and value on that server, the
    /// most valuable first. Bound items cannot be sold, so they are listed but not counted.
    /// </summary>
    public object View(InventoryWatch inventory, int? server, string? character, int home = 0)
    {
        int s = server ?? 0;
        var held = inventory.Holdings(i => i < 900_000_000);
        var rows = held.Select(h =>
        {
            var info = InventoryWatch.Info(h.Key);
            var own = s > 0 ? Manual(s, h.Key) : null;
            var row = s > 0 ? Row(s, h.Key) : null;
            long? market = row is null ? null : row.Price ?? row.LastPrice;
            long? unit = own?.Price ?? market;
            return new
            {
                item = h.Key, name = info.Name, icon = info.Icon, grade = info.Grade, qty = h.Value, bound = InventoryWatch.Bound(h.Key),
                npc = info.Price, own = own?.Price, market = row is null || market is null ? null : new { price = market, listed = row.Price is not null, listings = row.Listings, at = row.At },
                unit, total = unit * h.Value,
            };
        }).OrderBy(r => r.bound).ThenByDescending(r => r.total ?? -1).ThenByDescending(r => r.npc * r.qty).ThenBy(r => r.name).ToList();
        var sellable = rows.Where(r => !r.bound).ToList();
        return new
        {
            server = s > 0 ? (int?)s : null, home = home > 0 ? (int?)home : null, character, complete = inventory.Complete, kinah = inventory.KinahSession.Now,
            value = sellable.Sum(r => r.total ?? 0), priced = sellable.Count(r => r.unit is not null), unpriced = sellable.Count(r => r.unit is null),
            npcValue = sellable.Sum(r => r.npc * r.qty), source = Source?.Name, status = Status, items = rows,
        };
    }

    /// <summary>What changes the «Todo el Mercado» list: sent to the panel only when this changes.</summary>
    public string BoardKey(int server) { lock (gate) return $"{server}:{book.Boards.GetValueOrDefault(server)?.FetchedAt.Ticks}:{book.Servers.Count}:{status}"; }

    /// <summary>
    /// «Todo el Mercado»: every item the source follows on that server, named and drawn from the app's catalog when it has
    /// it (else the source's English name), and the servers to choose from.
    /// </summary>
    public object BoardView(int server)
    {
        var board = Board(server);
        return new
        {
            server, source = Source?.Name, status = Status, fetchedAt = board?.FetchedAt, snapshotAt = board is { Rows.Count: > 0 } b ? b.Rows.Max(r => r.At) : (DateTime?)null,
            servers = Servers.Select(x => new { id = x.Id, name = x.Name, region = x.Region, regionName = x.RegionName, faction = x.Faction }),
            rows = board?.Rows.Select(r =>
            {
                bool known = InventoryWatch.Known(r.Item);
                var info = known ? InventoryWatch.Info(r.Item) : default;
                return new
                {
                    item = r.Item, name = known ? info.Name : r.Name, en = known && info.Name != r.Name ? r.Name : null, icon = info.Icon, grade = info.Grade ?? "",
                    section = r.Section, price = r.Price, last = r.LastPrice, listings = r.Listings, change = r.Change is double c ? Math.Round(c, 1) : (double?)null, at = r.At,
                };
            }),
        };
    }
}
