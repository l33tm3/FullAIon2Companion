using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The Abyss as the server tells it, for the overlay's Abyss card (2026-10-26, the user: «un botón de overlay nuevo de ranking
/// abismos con puntaje y todo y si farmeo en abismo que vaya contando… para saber cuánto acumulado llevo»):
/// <code>
/// 73 E2 | 00 | ranking points u64            the Abyss ranking's points now: up with every reward («Has obtenido N puntos de
///                                            clasificación»), down with every death («Has perdido 96 puntos…»). 2026-10-25
///                                            18:30 it said 632,824 and the ranking of 18:34 (0x578D) 632,794
/// A9 FF | Abyss Points earned u64 | cap u64  up by the Abyss Points of every reward, exactly as the item (2026-10-24 12:16:
///                                            +11, +759, +2, +587), never down (spending them does not count): 422,817 of 2,000,000
/// </code>
/// and the Abyss Points held, item 930100003 of the inventory (<see cref="InventoryWatch"/>). 0xA9FF comes at login too, 0x73E2
/// with the first reward. The session counts from the first reward after a reset, per character (abyss-sessions.json): the
/// ranking points won and lost (and how many deaths), the Abyss Points earned, and the kinah and items gained while farming
/// there (up to <see cref="Farming"/> after a reward). Its time is the time farming: pauses over <see cref="Pause"/> left out.
/// <para>
/// At a login the new character's 0xA9FF comes before its character record (2026-10-25 02:45: 0xA9FF, 0x1156 the inventory,
/// 0xA9FF again, then 0x3336 a few ms later), so it would land in the book of the character played before. A 0xA9FF that is
/// not part of a reward (a 0x73E2 in the same second) waits: after a login it becomes the baseline of the character the record
/// names (<see cref="SetCharacter"/> from the engine); otherwise it counts after <see cref="Settle"/> seconds.
/// </para>
/// </summary>
public sealed class AbyssWatch
{
    public const int AbyssPoints = 930100003;
    const int Ranking = 0x73E2, Earned = 0xA9FF, Whole = 0x1156;
    /// <summary>Kinah and items count for the session up to this long after an Abyss reward or death.</summary>
    public static readonly TimeSpan Farming = TimeSpan.FromMinutes(3);
    /// <summary>A longer gap between rewards is a pause: it does not count as time farming.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(5);
    // How long a 0xA9FF that came alone waits for a login before it counts, and a login for its character record.
    static readonly TimeSpan Settle = TimeSpan.FromSeconds(2), RecordWait = TimeSpan.FromSeconds(30);

    public sealed class Session
    {
        public DateTime Start { get; set; }
        public DateTime Last { get; set; }
        /// <summary>Seconds farming: the gaps between rewards, the pauses left out.</summary>
        public double Active { get; set; }
        public long RankGained { get; set; }
        public long RankLost { get; set; }
        public int Deaths { get; set; }
        public int Rewards { get; set; }
        public long ApGained { get; set; }
        public long Kinah { get; set; }
        public Dictionary<int, long> Items { get; set; } = [];
    }
    sealed class Book
    {
        public Session? Session { get; set; }
        public long? Rank { get; set; }
        public DateTime RankAt { get; set; }
        public long? Earned { get; set; }
        public long? Cap { get; set; }
    }
    /// <summary>What the card shows: the totals now and the session (null before the first reward).</summary>
    public sealed record View(long? Rank, bool RankLive, DateTime? RankAt, long? Held, long? Earned, long? Cap, Session? Session, DateTime? LastReward);
    // A 0xA9FF waiting to know whose it is: its frame time, its wall-clock time, and whether a login came with it.
    sealed record Waiting(long Earned, long Cap, DateTime At, DateTime Wall, bool Login);

    readonly object gate = new(), saveGate = new();
    Dictionary<string, Book> books = [];
    string? path;
    string key = "";
    DateTime at;
    // The values this game login sent (−1 until it sends one): a change is only counted against one of them.
    long rankNow = -1, earnedNow = -1;
    DateTime lastReward = DateTime.MinValue, lastRankFrame = DateTime.MinValue, lastSave = DateTime.MinValue;
    Waiting? waiting;
    // A login (0x1156) whose character record has not come yet, and whether any login came since the app started.
    bool awaitingRecord, loginSeen, recordSinceLogin, dirty;
    DateTime loginAt, loginWall;
    /// <summary>The Abyss Points held now (the inventory's stacks of item 930100003), set by the engine.</summary>
    public Func<long?>? Held { get; set; }
    /// <summary>Raises with every change.</summary>
    public int Version { get; private set; }

    public void Open(string file)
    {
        path = file;
        try { books = JsonSerializer.Deserialize<Dictionary<string, Book>>(File.ReadAllText(file)) ?? []; } catch { books = []; }
    }
    void Save()
    {
        if (path is null) return;
        // One save at a time from taking the state to writing it: the capture thread and the panel's (Reiniciar, the character)
        // share the file, and an older state must not be written after a newer one. gate is never held when saveGate is taken.
        lock (saveGate)
        {
            string json;
            lock (gate) { json = JsonSerializer.Serialize(books); dirty = false; lastSave = at; }
            try { File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true); } catch { }
        }
    }
    /// <summary>On closing: a 0xA9FF still waiting counts (or, after a login, becomes the baseline), and what changed since the
    /// last save (the kinah and items after the last reward) is written.</summary>
    public void Flush()
    {
        if (waiting is not null) Resolve(DateTime.MaxValue, wall: false);
        bool pending; lock (gate) pending = dirty; if (pending) Save();
    }
    Book Current() { if (!books.TryGetValue(key, out var b)) books[key] = b = new(); return b; }

    /// <summary>
    /// The character the frames belong to (server and name). What came before it was known joins its book. From the engine when
    /// the game sends the character record (<paramref name="record"/>): the login's 0xA9FF that waited becomes its baseline.
    /// </summary>
    public void SetCharacter(int? server, string? name, bool record = false)
    {
        if (server is not int s || s <= 0 || string.IsNullOrEmpty(name)) return;
        string next = $"{s}:{name.ToLowerInvariant()}";
        bool changed = false;
        lock (gate)
        {
            if (next != key)
            {
                var before = key; key = next; changed = true;
                if (before.Length == 0 && books.Remove("", out var early))
                {
                    var book = Current();
                    if (early.Session is { } e) book.Session = Merge(book.Session, e);
                    if (early.Rank is not null) { book.Rank = early.Rank; book.RankAt = early.RankAt; }
                    if (early.Earned is not null) { book.Earned = early.Earned; book.Cap = early.Cap; }
                }
            }
            if (record)
            {
                awaitingRecord = false; recordSinceLogin = true;
                if (waiting is { Login: true } w) { Baseline(w); changed = true; }
            }
            if (changed) Version++;
        }
        if (changed) Save();
    }
    static Session Merge(Session? into, Session add)
    {
        if (into is null) return add;
        into.Start = add.Start < into.Start ? add.Start : into.Start; into.Last = add.Last > into.Last ? add.Last : into.Last;
        into.Active += add.Active; into.RankGained += add.RankGained; into.RankLost += add.RankLost; into.Deaths += add.Deaths;
        into.Rewards += add.Rewards; into.ApGained += add.ApGained; into.Kinah += add.Kinah;
        foreach (var (item, n) in add.Items) into.Items[item] = into.Items.GetValueOrDefault(item) + n;
        return into;
    }

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (waiting is not null) Resolve(at, wall: false);
        if (dirty && at - lastSave > TimeSpan.FromSeconds(5)) Save();
        if (f.Length < 2) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        // A login (the whole inventory): the values of the character before are not a baseline for this one, its farming window
        // ends, and a 0xA9FF that came just before belongs to the character that is logging in.
        if (opcode == Whole && f.Length > 40)
        {
            lock (gate)
            {
                rankNow = -1; earnedNow = -1; lastReward = DateTime.MinValue;
                awaitingRecord = true; loginSeen = true; recordSinceLogin = false; loginAt = at; loginWall = DateTime.Now;
                if (waiting is { } w && at - w.At <= Settle) waiting = w with { Login = true };
            }
            return;
        }
        if (opcode == Ranking && f.Length >= 11 && f[2] == 0) Rank(BinaryPrimitives.ReadInt64LittleEndian(f[3..]));
        else if (opcode == Earned && f.Length >= 18) Earn(BinaryPrimitives.ReadInt64LittleEndian(f[2..]), BinaryPrimitives.ReadInt64LittleEndian(f[10..]));
    }
    void Rank(long total)
    {
        if (total is < 0 or > 10_000_000_000) return;
        lock (gate)
        {
            var book = Current();
            long before = rankNow;
            // The first after an app start or a login: the total kept for this character, if it is recent (an app restart or a
            // relog in the middle of farming), once the book is surely this character's (no login since the app started, or its
            // record came after the login). Else it is only the baseline.
            if (before < 0 && (!loginSeen || recordSinceLogin) && book.Rank is long kept && at - book.RankAt < TimeSpan.FromMinutes(30) && Math.Abs(total - kept) <= 10_000) before = kept;
            rankNow = total; book.Rank = total; book.RankAt = at; lastRankFrame = at;
            if (before >= 0 && total != before)
            {
                var s = Reward(book, at);
                if (total > before) { s.RankGained += total - before; s.Rewards++; }
                else { s.RankLost += before - total; s.Deaths++; }
            }
            Version++;
        }
        Save();
    }
    void Earn(long earned, long cap)
    {
        if (earned < 0 || cap is < 1 or > 1_000_000_000 || earned > cap * 4) return;
        bool committed;
        lock (gate)
        {
            // Part of a reward (its 0x73E2 came in the same second, outside a login): this character's, at once. Alone, it waits.
            if (awaitingRecord && at - loginAt > RecordWait) awaitingRecord = false; // its record never came
            bool reward = !awaitingRecord && lastRankFrame != DateTime.MinValue && (at - lastRankFrame).Duration() <= TimeSpan.FromSeconds(1);
            committed = reward;
            if (reward) { waiting = null; Commit(earned, cap, at); }
            else waiting = new(earned, cap, at, DateTime.Now, awaitingRecord);
            Version++;
        }
        if (committed) Save();
    }
    // A waiting 0xA9FF that is old enough: counted for the character now (no login came), or, after a login whose record never
    // came, dropped (the book is still the one of the character before). By frame time while frames come, by the clock when
    // the card asks.
    void Resolve(DateTime now, bool wall)
    {
        bool changed = false;
        lock (gate)
        {
            if (waiting is not { } w) return;
            if (w.Login) { if ((wall ? DateTime.Now - loginWall : now - loginAt) > RecordWait) { awaitingRecord = false; waiting = null; changed = true; } }
            else if ((wall ? DateTime.Now - w.Wall : now - w.At) > Settle) { waiting = null; Commit(w.Earned, w.Cap, w.At); changed = true; }
            if (changed) Version++;
        }
        if (changed) Save();
    }
    void Commit(long earned, long cap, DateTime when)
    {
        var book = Current();
        long before = earnedNow;
        earnedNow = earned; book.Earned = earned; book.Cap = cap;
        if (before >= 0 && earned > before && earned - before <= 1_000_000) Reward(book, when).ApGained += earned - before;
    }
    void Baseline(Waiting w)
    {
        waiting = null;
        var book = Current();
        earnedNow = w.Earned; book.Earned = w.Earned; book.Cap = w.Cap;
    }
    // A reward or a death now: the session goes on (or starts), and the time since the last one counts unless it was a pause.
    Session Reward(Book book, DateTime when)
    {
        var s = book.Session ??= new Session { Start = when, Last = when };
        if (when > s.Last && when - s.Last <= Pause) s.Active += (when - s.Last).TotalSeconds;
        if (when > s.Last) s.Last = when;
        if (when > lastReward) lastReward = when;
        return s;
    }
    bool FarmingNow(DateTime when) => lastReward != DateTime.MinValue && when >= lastReward.AddSeconds(-2) && when - lastReward <= Farming;

    /// <summary>An item entered the inventory (InventoryWatch.Gained): counted while farming in the Abyss.</summary>
    public void ItemGained(int item, long quantity, DateTime when)
    {
        if (item == AbyssPoints || quantity <= 0) return;
        lock (gate)
        {
            if (!FarmingNow(when) || Current().Session is not { } s) return;
            s.Items[item] = s.Items.GetValueOrDefault(item) + quantity;
            Version++; dirty = true;
        }
        SaveSoon(when);
    }
    /// <summary>Kinah earned (InventoryWatch.KinahGained): counted while farming in the Abyss.</summary>
    public void KinahGained(long amount, DateTime when)
    {
        if (amount <= 0) return;
        lock (gate)
        {
            if (!FarmingNow(when) || Current().Session is not { } s) return;
            s.Kinah += amount;
            Version++; dirty = true;
        }
        SaveSoon(when);
    }
    // The loot of a kill comes after its reward was saved: written at most every 5 s, and on closing (Flush).
    void SaveSoon(DateTime when) { bool due; lock (gate) due = dirty && when - lastSave > TimeSpan.FromSeconds(5); if (due) Save(); }

    /// <summary>A new session: the next reward starts it.</summary>
    public void Reset()
    {
        lock (gate) { Current().Session = null; lastReward = DateTime.MinValue; Version++; }
        Save();
    }

    public View Now()
    {
        if (waiting is not null) Resolve(at, wall: true);
        // Outside the lock: the inventory calls in here holding its own (ItemGained), the other way round would deadlock.
        long? held = Held?.Invoke();
        lock (gate)
        {
            books.TryGetValue(key, out var book);
            var s = book?.Session;
            var copy = s is null ? null : new Session { Start = s.Start, Last = s.Last, Active = s.Active, RankGained = s.RankGained, RankLost = s.RankLost,
                Deaths = s.Deaths, Rewards = s.Rewards, ApGained = s.ApGained, Kinah = s.Kinah, Items = new(s.Items) };
            return new(book?.Rank, rankNow >= 0, book?.Rank is null ? null : book.RankAt, held, book?.Earned, book?.Cap, copy, lastReward == DateTime.MinValue ? null : lastReward);
        }
    }
}
