using System.Buffers.Binary;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Combat.Sources;
using PacketDotNet;
using SharpPcap;

namespace FullAIon2Companion;

public sealed record SegmentLog(DateTime Timestamp, string Source, string Destination, uint Sequence, string Payload, bool FromServer);
public sealed record SkillRow(int Id, string Name, string? Icon, long Damage, int Hits, int Criticals, double Share);
/// <summary>A row of the damage list. <paramref name="Active"/>: the seconds of its sustained hitting (runs of two hits or more,
/// <see cref="CaptureEngine.SustainedSeconds"/>), which decide whether its DPS orders the list.</summary>
public sealed record ActorRow(int Id, string Name, long Damage, double Dps, long Healing, double Active = 0);
/// <summary>One skill in the breakdown window. Casts: hits grouped by the frame's sequence byte; Average: per cast.</summary>
public sealed record BreakdownSkill(int Id, string Name, string? Icon, long Amount, double Share, int Casts, int Hits, double MultiPct, double CritPct, long Max, long Average,
    double BackPct = 0, double PerfectPct = 0, double DoublePct = 0);
public sealed record BreakdownTarget(int Id, string Name, long Amount);
/// <summary>The per-skill breakdown of one player's damage or healing, like the game's combat analyzer.</summary>
public sealed record BreakdownView(int ActorId, string Actor, bool Healing, double Duration, long Total, double PerSecond, int Casts, int Hits,
    double CritPct, double MultiPct, double BackPct, int? Target, IReadOnlyList<BreakdownTarget> Targets, IReadOnlyList<BreakdownSkill> Skills, IReadOnlyList<ActorRow> Players,
    double PerfectPct = 0, double DoublePct = 0, double ParryPct = 0, double BlockPct = 0, double EvadePct = 0, int Deaths = 0, long HealingTotal = 0, double Hps = 0,
    BreakdownExtra? Extra = null);
/// <summary>A moment of the fight's timeline (seconds from the first hit): a death, the biggest hit, a long-cooldown skill.</summary>
public sealed record TimelineEvent(double At, string Kind, string Text);
/// <summary>A player's death in the fight: when, who, by whom, the last hits taken and the damage of the last 5 s.</summary>
public sealed record DeathInfo(double At, string Victim, string? Killer, IReadOnlyList<string> LastHits, long DamageTaken5s);
/// <summary>The fight details' tabs: DPS over time (one value per bucket), events, deaths, effects uptime and advice.</summary>
public sealed record BreakdownExtra(double Bucket, IReadOnlyList<double> Dps, IReadOnlyList<TimelineEvent> Events, IReadOnlyList<DeathInfo> Deaths,
    IReadOnlyList<BuffUptime> Buffs, IReadOnlyList<string> Advice);
/// <summary>MaxHpKnown: the maximum comes from a boss seen at that HP before, not just the highest HP seen in this fight.</summary>
public sealed record TargetInfo(int EntityId, int? NpcId, string Name, int? ServerId, long? Hp, long? HighestObservedHp, bool MaxHpKnown = false,
    double? GroupDps = null, double? KillSeconds = null, double? FightSeconds = null, double? EnrageIn = null, double? HpAtEnragePct = null, bool Enraged = false,
    double? RequiredDps = null, double? EnragedFor = null);
/// <summary>Exact: the game reported the player's own position lately; else the middle of the creatures announced around.</summary>
public sealed record MapPosition(float X, float Y, bool Exact);
public sealed record MobDot(int Npc, float X, float Y, bool Elite);
/// <summary>TargetDamage, TargetDps: yours on the target you are hitting, as the game's own analyzer counts them (see Snapshot).</summary>
public sealed record CaptureSnapshot(string Status, long Packets, long Frames, long DamageEvents, int DecodeErrors,
    string Zone, int LocalId, string? Character, long Damage, long Healing, double Dps, double Duration,
    int ConfirmedKills, int NearbyMobs, TargetInfo? Target, IReadOnlyList<SkillRow> Skills, IReadOnlyList<ActorRow> Actors,
    long UnknownOpcodes, long Gaps, long Retransmissions, bool Paused, bool IdentityInferred = false, IReadOnlyList<CastRow>? Casts = null, IReadOnlyList<string>? Party = null, int? LocalServerId = null, int? PingMs = null, MapPosition? Position = null, IReadOnlyList<MobDot>? Mobs = null, PetHunt? Pet = null, RoomInfo? Room = null, OdEnergy.State? Od = null, TroopInfo? Troop = null, int? World = null, int MapLoads = 0,
    long TargetDamage = 0, double TargetDps = 0);

public sealed class CaptureEngine : IDisposable
{
    readonly object gate = new();
    readonly Aion2Protocol protocol = Aion2Protocol.Load();
    readonly Aion2EntityDirectory entities = new();
    TcpReassembler stream = new();
    readonly Aion2FrameDecoder decoder;
    readonly List<ILiveDevice> devices = [];
    readonly List<DamageEvent> fight = [];
    // The fight as it was when its hits came: the ids that were yours then and each actor's name then. A map load gives you
    // a new id and hands the old ids to other entities (2026-10-23: after one, the last fight showed «0 daño» for you while
    // the list had «Darkni» with 2.7 M under the old id, and MemberTwo's id 1537 read «MemberThree»). Under the gate.
    readonly Dictionary<int, DateTime> fightOwnUntil = []; // each id that was yours, until its last hit while it was
    readonly Dictionary<int, string> fightNames = [];
    readonly Dictionary<int, int> fightNpcs = [];
    readonly Dictionary<int, string> fightClasses = [];
    int fightLoads;
    readonly HashSet<(int, DateTime)> deaths = [];
    // The monsters hit in this fight and who hit them: a death counts as a kill of ours only if one of them was us (Ours).
    readonly Dictionary<int, HashSet<int>> engaged = [];
    readonly FrameAnalysis analysis=new();
    readonly BossCasts casts=new();
    readonly PartyRoster party;
    readonly NpcMemory memory=new();
    readonly SpawnHp spawnHp=new();
    readonly PlayerMemory playerMemory=new();
    readonly GamePing ping=new();
    readonly PetTracker pets=new();
    readonly InventoryWatch inventory=new();
    readonly LootBook observed=new();
    /// <summary>What each creature gave the player when it died (in memory until the app opens its file).</summary>
    public LootBook Observed => observed;
    readonly GameResources resources=new();
    public GameResources Resources => resources;
    readonly RankingWatch rankings=new();
    /// <summary>The player's places in the game's rankings (0x578D, sent when the ranking window is opened).</summary>
    public RankingWatch Rankings => rankings;
    readonly RoomBoard board = new();
    /// <summary>The party finder's room lists (0x0197, while the Expedición or Trascendencia window shows a dungeon).</summary>
    public RoomBoard Board => board;
    readonly DaevanionWatch daevanion = new();
    readonly AbyssWatch abyss = new();
    (int, DateTime) lastRecord;
    /// <summary>The Abyss ranking points and Abyss Points (0x73E2, 0xA9FF) and the session farming there.</summary>
    public AbyssWatch Abyss => abyss;
    /// <summary>Your open Daevanion nodes as the game last sent them (0x26E2).</summary>
    public DaevanionWatch Daevanion => daevanion;
    public InventoryWatch Inventory => inventory;
    readonly CombatAssistant assistant=new();
    public CombatAssistant Assistant => assistant;
    readonly CubeWatch cubes=new();
    public CubeWatch Cubes => cubes;
    /// <summary>A field boss seen around the player: NPC id, alive (appeared) or dead (left with reason 3), when.</summary>
    public Action<int, bool, DateTime>? FieldBossSeen { get; set; }
    readonly Dictionary<int, int> fieldBossEntities = [];
    // 0x4136: entity varint | 2 type bytes | flag (1 = a name follows) | npc u32 ...; 0x4236: entity varint | 00 | reason (3 = dead).
    internal void WatchFieldBoss(ReadOnlySpan<byte> f, DateTime at)
    {
        if (FieldBossSeen is null || f.Length < 5) return;
        int opcode = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f), p = 2;
        if (opcode is not (0x4136 or 0x4236)) return;
        long entity = 0;
        for (int shift = 0; p < f.Length && shift <= 28; shift += 7) { byte b = f[p++]; entity |= (long)(b & 127) << shift; if (b < 128) break; }
        if (entity <= 0) return;
        if (opcode == 0x4236)
        {
            if (f.Length == p + 2 && f[^1] == 3 && fieldBossEntities.Remove((int)entity, out int dead)) FieldBossSeen(dead, false, at);
            return;
        }
        if (f.Length < p + 7) return;
        if (f[p + 2] == 1 && p + 3 < f.Length) p += 1 + f[p + 3];
        p += 3;
        if (f.Length < p + 4) return;
        int npc = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(f[p..]);
        if (!FieldBossBoard.Npcs.Contains(npc) || fieldBossEntities.ContainsKey((int)entity)) return;
        fieldBossEntities[(int)entity] = npc;
        FieldBossSeen(npc, true, at);
    }
    /// <summary>The atlas map the player is on: the one learned for the loaded world, else the creatures' vote.</summary>
    RoomInfo? RoomNow() { room.CheckLeft(); return room.Current; }
    public string? MapId() => MinimapData.MapOfWorld(ownPos.World) ?? MinimapData.Vote(entities.LiveMobs().Select(m => m.NpcId)).Map?.Id;
    public CubeView CubeView() { var p = Position(); return cubes.View(p is null ? null : ((float)p.X, (float)p.Y), ownPos.LastZ, DateTime.Now, p?.Exact == true); }
    readonly RoomRoster room=new();
    readonly RareFrames rare=new();
    readonly OdEnergy od=new();
    readonly HitDetails hitDetails=new();
    readonly EnrageWatch enrage=new();
    public EnrageWatch Enrage => enrage;
    /// <summary>Pets pinned in the overlay (from the settings).</summary>
    public IReadOnlyCollection<int> PinnedPets { get; set; } = [];
    // The farm session: since the app started or the last reset (kills counted by CountDeath, souls by PetTracker).
    DateTime sessionStart = DateTime.Now; int sessionKills;
    public (DateTime Start, int Kills) Session { get { lock(gate) return (sessionStart, sessionKills); } }
    public void ResetSession() { lock(gate) { sessionStart = DateTime.Now; sessionKills = 0; } pets.ResetSession(); inventory.ResetSession(); }
    readonly Dictionary<DamageEvent, HitExtra> extras=[];
    readonly TroopRoster troop=new();
    readonly OwnPosition ownPos=new();
    public PetTracker Pets => pets;
    /// <summary>Diagnostics: every reassembled frame with its time (replay analysis only).</summary>
    public Action<byte[], DateTime>? Tap;
    /// <summary>
    /// The local character as the capture itself saw it this session (its own 0x3336 record), or null. Not the configured
    /// name, not a record restored from disk and not an id inferred from the stats frames: the ownership challenge needs this.
    /// </summary>
    (int Id, string Name, int ServerId)? sessionIdentity;
    // After a map load or a new connection of the same game process: the identity before it, waiting to be confirmed.
    (int Id, string Name, int ServerId)? carriedIdentity;
    // The class the own entity plays (learned from its skills) and the game process the identity belongs to.
    string? identityClass;
    int identityPid;
    static string OwnIdentityPath => Path.Combine(Program.DataDirectory, "own-identity.json");
    sealed record SavedIdentity(string Name, int ServerId, string Class, int Pid, DateTime GameStartUtc);
    public (int Id, string Name, int ServerId)? CapturedIdentity { get { lock(gate) { ConfirmCarried(); return sessionIdentity; } } }
    /// <summary>
    /// The character record is not resent on every map load. Same connection, and the entity that alone receives the
    /// detailed-stats frames on this map has the same name (and server): it is the same character; a different name drops it.
    /// Started inside the game, the character saved from an earlier record is only a candidate, confirmed the same way.
    /// Never the configured name, never the top damage dealer. Call under the gate.
    /// </summary>
    /// <summary>Why the own identity is (not) confirmed, for account-live.json.</summary>
    public string IdentityDebug { get { lock(gate) { int? st = entities.LocalFromDetailedStats(); var c = entities.LocalCharacter; return $"session={sessionIdentity} carried={carriedIdentity} stats={st} statsName={(st is int i ? entities.NameOf(i) : null)} statsServer={(st is int j ? entities.ServerIdOf(j) : null)} record={c?.Name}/{c?.ServerId}/restored={c?.Restored}/id={c?.CombatId}"; } } }
    void ConfirmCarried()
    {
        if (sessionIdentity is { } own)
        {
            // Learn the class of the confirmed character once its skills are seen, and remember it for this game process.
            if (identityClass is null && entities.ClassOf(own.Id) is { } cls) { identityClass = cls; identityPid = Connection?.Pid ?? 0; SaveIdentity(own); }
            return;
        }
        carriedIdentity ??= LoadIdentity();
        if (carriedIdentity is not { } carried || identityClass is null) return;
        if (entities.LocalFromDetailedStats() is not int statsId) return;
        string? statsName = entities.NameOf(statsId), statsClass = entities.ClassOf(statsId);
        bool differentName = statsName is not null && !statsName.Equals(carried.Name, StringComparison.OrdinalIgnoreCase);
        bool differentServer = entities.ServerIdOf(statsId) is int sv && sv != carried.ServerId;
        if (differentName || differentServer || statsClass is not null && statsClass != identityClass) { carriedIdentity = null; identityClass = null; DeleteIdentity(); return; }
        if (statsClass is null && statsName is null) return; // not enough seen on this map yet
        sessionIdentity = (statsId, carried.Name, carried.ServerId);
        carriedIdentity = null;
    }
    void SaveIdentity((int Id, string Name, int ServerId) own)
    {
        try
        {
            using var game = System.Diagnostics.Process.GetProcessById(identityPid);
            File.WriteAllText(OwnIdentityPath, System.Text.Json.JsonSerializer.Serialize(new SavedIdentity(own.Name, own.ServerId, identityClass!, identityPid, game.StartTime.ToUniversalTime())));
        }
        catch { }
    }
    /// <summary>The identity saved by an earlier run of the app, only while the very same game process is still running.</summary>
    (int Id, string Name, int ServerId)? LoadIdentity()
    {
        try
        {
            if (Connection is null || !File.Exists(OwnIdentityPath)) return null;
            var saved = System.Text.Json.JsonSerializer.Deserialize<SavedIdentity>(File.ReadAllText(OwnIdentityPath));
            using var game = System.Diagnostics.Process.GetProcessById(Connection.Pid);
            if (saved is null || saved.Pid != Connection.Pid || Math.Abs((game.StartTime.ToUniversalTime() - saved.GameStartUtc).TotalSeconds) > 2) return null;
            identityClass = saved.Class; identityPid = saved.Pid;
            return (0, saved.Name, saved.ServerId);
        }
        catch { return null; }
    }
    static void DeleteIdentity() { try { File.Delete(OwnIdentityPath); } catch { } }
    readonly PetCollection collection=new();
    public PetCollection Collection => collection;
    /// <summary>Files the pet list under the character on screen (see <see cref="PetCollection"/>).</summary>
    public void UpdateCollection(string? character, int? serverId, string region) { lock(gate) collection.Update(character, serverId, region, entities, pets.SoulsToday()); }
    public void ForgetParty() => party.Forget();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly DateTime clockOrigin = DateTime.Now;
    /// <summary>The capture's monotonic clock, the one hit timestamps use.</summary>
    /// <summary>Replays only: the capture's own time instead of the wall clock.</summary>
    public Func<DateTime>? ReplayClock { get; set; }
    public DateTime Now => ReplayClock?.Invoke() ?? clockOrigin + clock.Elapsed;
    StreamWriter? recording;
    DateTime? started, lastHit;
    int realTarget, fightFocus;
    /// <summary>The creature or player you have selected in the game (0 = nothing).</summary>
    public int SelectedTarget => realTarget;
    // Own selection from 0x3438: 34 38 | 00 00 00 | entity varint (0 = nothing selected). The decoder's "TargetSelect"
    // (0x3538) is a different thing - "entity X now targets Y" for any creature or player (35 38 | X | 00 | Y) - so it
    // made whoever changed target nearby (a player, a skill helper) look like ours (2026-10-11 captures: 3438 ... A1B403
    // when Kelpina was clicked, 3438 00000000 on deselect).
    void SelectFrame(ReadOnlySpan<byte> f)
    {
        if (f.Length < 6 || BinaryPrimitives.ReadUInt16BigEndian(f) != 0x3438 || f[2] != 0 || f[3] != 0 || f[4] != 0) return;
        int p = 5; long entity = 0; int shift = 0;
        while (p < f.Length && shift <= 28) { byte b = f[p++]; entity |= (long)(b & 127) << shift; if ((b & 128) == 0) break; shift += 7; }
        // Skills spawn invisible helpers on the target and the client may mark one: keep the creature underneath.
        if (entity != 0 && Catalog.IsHelper(entities.NpcIdOf((int)entity))) return;
        realTarget = (int)entity;
    }
    long packets, damageEvents;
    int errors, kills;
    bool paused;
    readonly string localName;
    public string Status { get; private set; } = "Esperando conexión de AION 2";
    public GameConnection? Connection { get; private set; }
    public Aion2EntityDirectory Entities => entities;
    public object Analysis=>new{frames=analysis.Result(),casts=casts.Summary(entities.NpcIdOf)};
    public event Action<DamageEvent>? Combat;
    /// <summary>Seconds without any hit after which the next hit starts a new encounter.</summary>
    public int IdleSeconds { get; set; } = 15;
    /// <summary>Count only damage dealt to elite NPCs (field bosses) - Configuración > Combate > Modo de objetivo.</summary>
    public bool BossOnly { get; set; }
    public event Action<int, int?, DateTime>? Death;

    public CaptureEngine(string name = "Darkni")
    {
        Aion2SkillNames.Language = "es";
        PetFarmLog.PathOverride = Path.Combine(Program.DataDirectory, "pet-farm.log");
        localName = name;
        party = new(name);
        spawnHp.NpcMaxSeen += memory.SetExactMax;
        entities.SetConfiguredLocalName(name);
        decoder = new(protocol, entities);
        cubes.MapNow = MapId;
        room.LocalName = () => entities.LocalCharacter?.Name ?? localName;
        room.World = () => ownPos.World;
        inventory.Gained += (item, quantity, at) => { pets.ItemGained(item, quantity); observed.Gained(item, quantity, at, gathered: cubes.UsedUpWithin(at, 3)); abyss.ItemGained(item, quantity, at); };
        inventory.KinahGained += abyss.KinahGained;
        abyss.Held = () => inventory.Holdings(i => i == AbyssWatch.AbyssPoints) is { Count: > 0 } held ? held.Values.Sum() : inventory.Complete ? 0 : null;
        cubes.OwnNow = () => Position() is { } me ? (me.X, me.Y) : null;
        enrage.NpcOf = id => entities.NpcIdOf(id);
        enrage.HpPctOf = id => entities.HitPoints.Latest(id) is { } h && entities.HitPoints.HighestSeen(id) is long top && top > 0 ? h.Hp * 100.0 / top : null;
        enrage.CandidateLog = Path.Combine(Program.DataDirectory, "enrage-candidates.log");
        entities.CharacterChanged += c => { lock(gate) { if (!c.Restored && c.CombatId > 0) { carriedIdentity = null; identityClass = null; } if (!c.Restored && c.CombatId > 0) sessionIdentity = (c.CombatId, c.Name, c.ServerId > 0 ? c.ServerId : entities.ServerIdOf(c.CombatId) ?? 0); }
            // The Abyss values of a login belong to the character this record names (they come just before it). The vendor also
            // raises this event with the stored record for every list it receives (equipment, which is 0x1156 the login inventory
            // itself, skills, titles...): only a new record (another id or receive time) is the login's.
            if (!c.Restored && c.CombatId > 0)
            {
                bool fresh = (c.CombatId, c.ReceivedAt) != lastRecord; lastRecord = (c.CombatId, c.ReceivedAt);
                abyss.SetCharacter(c.ServerId > 0 ? c.ServerId : entities.ServerIdOf(c.CombatId), c.Name, record: fresh);
            } };
    }
    public void Start(string? recordPath = null)
    {
        lock(gate)
        {
            if (devices.Count != 0) return;
            Connection = Pick(GameConnection.Find());
            if (Connection is null) { Status = "Abre AION 2 y entra con tu personaje"; return; }
            startedAt = clock.Elapsed;
            // Live capture only (not replays): names of players already around, from earlier in this game session.
            playerMemory.Restore(entities, Connection.LocalPort);
            spawnHp.Restore(entities, Connection.LocalPort);
            if (!protocol.IsCalibrated) throw new InvalidOperationException("Falta el archivo de protocolo");
            // The adapter the game's traffic goes through. With a gaming VPN it may be a virtual adapter (TAP, Wintun), the
            // loopback (a proxy on this PC) or one whose address does not match: then every adapter is listened to with the
            // connection's filter, so only the game's packets arrive whichever it is.
            var all = CaptureDeviceList.Instance.ToList();
            var nic = CaptureAdapters.List().FirstOrDefault(a => a.Ipv4 == Connection.LocalAddress);
            List<ILiveDevice> candidates;
            string where;
            if (Connection.ViaProxy || Connection.LocalAddress.StartsWith("127.", StringComparison.Ordinal))
            {
                candidates = all.Where(d => d.Name.Contains("NPF_Loopback", StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 0) throw new InvalidOperationException("Tu VPN pasa el juego por un proxy local: reinstala Npcap marcando «Support loopback traffic» para poder leerlo");
                where = "VPN (proxy local)";
            }
            else if (nic is not null && all.Where(d => d.Name.Contains(nic.Id, StringComparison.OrdinalIgnoreCase)).ToList() is { Count: 1 } exact)
            { candidates = exact; where = nic.Name; }
            else { candidates = all.Where(d => !d.Name.Contains("NPF_Loopback", StringComparison.OrdinalIgnoreCase)).ToList(); where = "todos los adaptadores (VPN o red virtual)"; }
            if (candidates.Count == 0) throw new InvalidOperationException("Npcap no muestra ningún adaptador de red: reinstala Npcap");
            if (recordPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(recordPath))!);
                recording = new StreamWriter(recordPath, false) { AutoFlush = true };
            }
            try
            {
                // What a new connection said before the app switched to it (a relog: the inventory and the counters come in
                // its first second), in arrival order, before anything live.
                foreach (var early in TakeEarly(Connection.Key)) Ingest(early);
                foreach (var device in candidates)
                {
                    try
                    {
                        // MaxResponsiveness (Npcap immediate mode): packets as they arrive, not in 250 ms batches - the ping is timed at arrival.
                        device.Open(new DeviceConfiguration { Mode = DeviceModes.MaxResponsiveness, ReadTimeout = 250 });
                        device.Filter = Connection.PortFilter;
                        device.OnPacketArrival += Arrived;
                        device.StartCapture();
                        devices.Add(device);
                    }
                    // Listening on every adapter, one that cannot be opened (a disconnected VPN, Bluetooth) is skipped.
                    catch when (candidates.Count > 1) { try { device.OnPacketArrival -= Arrived; device.Close(); } catch { } }
                }
                if (devices.Count == 0) throw new InvalidOperationException("No se pudo abrir el adaptador de red del juego");
                Status = "Captura activa · " + where;
            }
            catch { Stop(); throw; }
        }
    }
    void Arrived(object sender, PacketCapture capture)
    {
        try
        {
            var raw = capture.GetPacket();
            var parsed = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            var tcp = parsed.Extract<TcpPacket>();
            var ip = parsed.Extract<IPPacket>();
            if (tcp is null || ip is null || Connection is null) return;
            byte[] payload = PayloadOf(raw.Data, ip, tcp);
            if (payload.Length == 0) return;
            string src = ip.SourceAddress.ToString(), dst = ip.DestinationAddress.ToString();
            bool ours = src == Connection.RemoteAddress && tcp.SourcePort == Connection.RemotePort && dst == Connection.LocalAddress && tcp.DestinationPort == Connection.LocalPort
                || src == Connection.LocalAddress && tcp.SourcePort == Connection.LocalPort && dst == Connection.RemoteAddress && tcp.DestinationPort == Connection.RemotePort;
            if (!ours)
            {
                // Another connection to the game server (a relog): kept in memory for a minute, never written anywhere.
                bool serverSide = tcp.SourcePort == Connection.RemotePort;
                string key = serverSide ? $"{dst}:{tcp.DestinationPort}>{src}:{tcp.SourcePort}" : $"{src}:{tcp.SourcePort}>{dst}:{tcp.DestinationPort}";
                Early(key, new(clockOrigin + clock.Elapsed, $"{src}:{tcp.SourcePort}", $"{dst}:{tcp.DestinationPort}", tcp.SequenceNumber, payload, serverSide));
                return;
            }
            bool fromServer = ip.SourceAddress.ToString() == Connection.RemoteAddress && tcp.SourcePort == Connection.RemotePort;
            if (fromServer) { ping.ServerPayload(payload); lastServerPacket = clock.Elapsed; } else ping.ClientSent(payload.Length);
            // Npcap on this machine returned frozen, days-old timeval values. Use a monotonic
            // reception clock for durations; preserve these normalized timestamps in replay logs.
            Ingest(new(clockOrigin + clock.Elapsed, $"{ip.SourceAddress}:{tcp.SourcePort}",
                $"{ip.DestinationAddress}:{tcp.DestinationPort}", tcp.SequenceNumber, payload, fromServer));
        }
        catch (Exception ex) { lock(gate) { errors++; File.AppendAllText(Path.Combine(Program.DataDirectory,"capture-errors.log"),ex+"\n"); } }
    }
    // The TCP payload. PacketDotNet throws when the IP header's length does not match the bytes captured: segments the
    // network card coalesced (or, sending, will split) carry a total length of 0 or a different one (2026-10-13: 27 game
    // packets lost that way, "Source array was not long enough"). Then the payload is cut from the captured bytes.
    static byte[] PayloadOf(byte[] data, IPPacket ip, TcpPacket tcp)
    {
        try { return tcp.PayloadData ?? []; } catch (ArgumentException) { }
        int ipStart = ip.BytesSegment.Offset, start = tcp.BytesSegment.Offset + tcp.DataOffset * 4;
        int declared = ip is IPv4Packet v4 ? v4.TotalLength : ip.TotalLength;
        int end = declared > 0 && ipStart + declared <= data.Length ? ipStart + declared : data.Length;
        if (++recovered <= 200) try { File.AppendAllText(Path.Combine(Program.DataDirectory, "capture-errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} recovered payload {Math.Max(0, end - start)} bytes (ip length {declared}, captured {data.Length - ipStart}){Environment.NewLine}"); } catch { }
        return start < end ? data[start..end] : [];
    }
    static int recovered;
    // Packets of other connections to the game server, by connection, for at most a minute and 16 MB in all.
    readonly Dictionary<string, List<TcpSegment>> early = [];
    readonly object earlyGate = new();
    long earlyBytes;
    void Early(string key, TcpSegment segment)
    {
        lock (earlyGate)
        {
            var limit = segment.Timestamp.AddSeconds(-60);
            foreach (var k in early.Keys.ToList())
                if (early[k][^1].Timestamp < limit) { earlyBytes -= early[k].Sum(x => (long)x.Payload.Length); early.Remove(k); }
            if (earlyBytes + segment.Payload.Length > 16_000_000) return;
            if (!early.TryGetValue(key, out var list)) early[key] = list = [];
            list.Add(segment); earlyBytes += segment.Payload.Length;
        }
    }
    List<TcpSegment> TakeEarly(string key)
    {
        lock (earlyGate)
        {
            if (!early.Remove(key, out var list)) return [];
            earlyBytes -= list.Sum(x => (long)x.Payload.Length);
            return list;
        }
    }
    public void Ingest(TcpSegment segment)
    {
        lock(gate)
        {
            packets++;
            // A recording contains only the selected game's gameplay stream. Bounded via the record command.
            recording?.WriteLine(JsonSerializer.Serialize(new SegmentLog(segment.Timestamp, segment.Source, segment.Destination,
                segment.Sequence, Convert.ToBase64String(segment.Payload.Span), segment.FromServer)));
            if (!segment.FromServer) return;
            foreach(var frame in stream.Push(segment, protocol.FrameLayout, protocol.SyncOpcodes))
            {
                Tap?.Invoke(frame.ToArray(), segment.Timestamp);
                try
                {
                    analysis.Add(frame.Span);
                    casts.Add(frame.Span, segment.Timestamp);
                    party.Add(frame.Span);
                    spawnHp.Add(frame.Span);
                    pets.Add(frame.Span);
                    room.Add(frame.Span);
                    troop.Add(frame.Span);
                    od.Add(frame.Span);
                    resources.Add(frame.Span, segment.Timestamp);
                    rankings.Add(frame.Span, segment.Timestamp);
                    board.Add(frame.Span, segment.Timestamp);
                    daevanion.Add(frame.Span, segment.Timestamp);
                    abyss.Add(frame.Span, segment.Timestamp);
                    int loadsBefore = ownPos.Loads;
                    ownPos.Add(frame.Span);
                    // A map load: the monsters of the previous map must not vote for (or place the player on) the new one.
                    if (ownPos.Loads != loadsBefore) { lastOwnId = 0; ownKnown = false; entities.ForgetMap(); cubes.ForgetLive(); carriedIdentity = sessionIdentity ?? carriedIdentity; sessionIdentity = null; }
                    collection.Add(frame.Span);
                    if (devices.Count > 0) rare.Add(frame.Span); // live sessions only, not replays
                    Frames.Walk(frame.Span, SelectFrame);
                    hitDetails.Add(frame.Span, segment.Timestamp);
                    enrage.Add(frame.Span, segment.Timestamp);
                    // The cubes first: a node used up in this frame explains the items that come with it.
                    cubes.Add(frame.Span, segment.Timestamp);
                    inventory.Add(frame.Span, segment.Timestamp);
                    assistant.Add(frame.Span, segment.Timestamp);
                    { var ts = segment.Timestamp; Frames.Walk(frame.Span, x => WatchFieldBoss(x, ts)); }
                    int? localNow = null;
                    foreach(var hit in decoder.Decode(frame.Span, segment.Timestamp))
                    {
                        damageEvents++;
                        if (paused || hit.Amount < 0 || hit.Amount > 100_000_000_000) continue;
                        // The encounter is ours: it starts, goes on and goes quiet with the damage you or your group deal or take.
                        // Before 2026-10-22 any hit nearby kept it going (304 s of «fight» in 5 min of a busy farm, so your DPS
                        // was your damage spread over all of it). Others' hits still join a fight of ours while it lasts (the
                        // boss panel needs everyone's damage). Until the identity is known, every hit counts, as before.
                        localNow ??= OwnId();
                        if (localNow > 0 && !ownKnown) { ownKnown = true; RebaseFight(localNow.Value); }
                        // Only damage keeps a fight alive: heals (regeneration, heals over time) went on for minutes after the
                        // group stopped fighting in the Abyss on 2026-10-22, the fight never ended and your DPS kept falling.
                        // They still count inside a fight.
                        // Once you were known, a moment without your id (a map load clears the detailed-stats frames until new
                        // ones come) is not «everything counts»: only your group's hits do. On 2026-10-23 a stranger's hits in
                        // those seconds stretched your fight by 5 s and your DPS fell from 37,012 to 29,972 with no new damage.
                        if (localNow > 0) identitySeen = true;
                        bool mine = !hit.IsHeal && (localNow > 0 ? Ours(hit.SourceObjectId, localNow.Value) || Ours(hit.TargetObjectId, localNow.Value)
                            : !identitySeen || Ours(hit.SourceObjectId, 0) || Ours(hit.TargetObjectId, 0));
                        bool active = lastHit is DateTime previous && (hit.Timestamp - previous).TotalSeconds <= IdleSeconds;
                        if (!mine && !active) { Combat?.Invoke(hit); continue; }
                        if (mine)
                        {
                            // Start a new encounter after a quiet interval; old fights are exported explicitly.
                            if (lastHit is not null && !active) ResetFight();
                            if (started is null) { started = hit.Timestamp; fightLoads = ownPos.Loads; }
                            lastHit = hit.Timestamp;
                        }
                        if (fight.Count >= 100_000) fight.RemoveRange(0, 50_000);
                        fight.Add(hit);
                        if (localNow > 0) fightOwnUntil[localNow.Value] = hit.Timestamp;
                        if (!fightNames.ContainsKey(hit.SourceObjectId) && entities.NameOf(hit.SourceObjectId) is string named) fightNames[hit.SourceObjectId] = named;
                        if (!fightNpcs.ContainsKey(hit.SourceObjectId) && entities.NpcIdOf(hit.SourceObjectId) is int creature) fightNpcs[hit.SourceObjectId] = creature;
                        if (!fightClasses.ContainsKey(hit.SourceObjectId) && entities.ClassOf(hit.SourceObjectId) is string cls) fightClasses[hit.SourceObjectId] = cls;
                        if (hitDetails.Of(hit) is { } extra) { if (extras.Count > 200_000) extras.Clear(); extras[hit] = extra; }
                        if (entities.IsKnownMonster(hit.TargetObjectId))
                        {
                            if (!engaged.TryGetValue(hit.TargetObjectId, out var hitters)) engaged[hit.TargetObjectId] = hitters = [];
                            if (hitters.Count < 64) hitters.Add(hit.SourceObjectId);
                        }
                        Combat?.Invoke(hit);
                    }
                    decoder.DrainSkillUses();
                    foreach (var avoid in decoder.DrainAvoids()) { if (avoids.Count > 20_000) avoids.RemoveRange(0, 10_000); avoids.Add(avoid); }
                    foreach(var death in decoder.DrainKills().ToList().Where(d => { if (d.VictimIsPlayer) playerDeaths.Add(d); return true; }))
                        if (!death.VictimIsPlayer && engaged.ContainsKey(death.VictimObjectId)) CountDeath(death.VictimObjectId, death.Timestamp);
                    foreach (int id in engaged.Keys.ToArray())
                        if (entities.RemovalOf(id) is { Reason: Aion2EntityDirectory.RemovedDead } removal) CountDeath(id, removal.At);
                }
                catch { errors++; }
            }
        }
    }
    // Kills count only for monsters you or your group hit: party, your troop group or the dungeon room. In the Abyss on
    // 2026-10-22 every monster anyone hit was counted, and the overlay said «4,407 criaturas» next to 0 damage; the farm
    // session, the souls per kill and the loot book took those kills of others as yours. Under the gate.
    HashSet<string> groupNames = new(StringComparer.OrdinalIgnoreCase);
    DateTime groupNamesAt;
    bool ownKnown, identitySeen;
    // Until the identity was known every hit counted, so strangers could have started the fight: when it becomes known,
    // the fight starts at your (or your group's) first damage, dealt or taken, or it is dropped if you were not in it. Under the gate.
    void RebaseFight(int local)
    {
        var ours = fight.Where(h => !h.IsHeal && (Ours(h.SourceObjectId, local) || Ours(h.TargetObjectId, local))).ToList();
        if (ours.Count == 0) { fight.Clear(); extras.Clear(); avoids.Clear(); fightOwnUntil.Clear(); fightNames.Clear(); fightNpcs.Clear(); fightClasses.Clear(); started = lastHit = null; return; }
        // Your last run of hits: an earlier one, more than the idle time before, was another fight.
        int from = 0;
        for (int i = 1; i < ours.Count; i++) if ((ours[i].Timestamp - ours[i - 1].Timestamp).TotalSeconds > IdleSeconds) from = i;
        started = ours[from].Timestamp; lastHit = ours[^1].Timestamp;
        fight.RemoveAll(h => h.Timestamp < ours[from].Timestamp);
    }
    // The local id for telling what is ours: the confirmed one, else the one the detailed-stats frames point to, else the
    // last one known on this map (0 = unknown). The guess comes and goes between frames: on 2026-10-11 it was there at
    // 03:29:30 and gone at 03:29:57, and meanwhile every hit nearby counted as yours. A map load gives you a new id.
    int lastOwnId;
    // Who you are when the game has not said it: only the detailed-stats frames, which the server sends to you alone.
    // The vendor's fallback (the unnamed player with the most skills seen) picked strangers in crowded places: in Balaur on
    // 2026-10-12 it took five other players for you in a minute, and the overlay showed their damage as yours.
    int? Guess() => entities.LocalFromDetailedStats();
    int OwnId()
    {
        int id = LocalCombatId() is int local and > 0 ? local : Guess() is int guess and > 0 ? guess : 0;
        if (id > 0) lastOwnId = id;
        return lastOwnId;
    }
    // A kill is decided when the monster dies, not at the hit: by then the identity is usually known.
    bool Ours(int source, int local)
    {
        if (local > 0 && source == local) return true;
        if (entities.NameOf(source) is not { Length: > 0 } name) return false;
        if ((Now - groupNamesAt).Duration().TotalSeconds >= 2)
        {
            groupNamesAt = Now;
            groupNames = new HashSet<string>(party.Names, StringComparer.OrdinalIgnoreCase);
            if (troop.Current is { } t) foreach (var m in t.Members) if (m.Group == t.OwnGroup) groupNames.Add(m.Name);
            if (room.Current is { } r) foreach (var m in r.Members) groupNames.Add(m.Name);
            if (localName is { Length: > 0 }) groupNames.Add(localName);
            if (entities.LearnedLocalName is { Length: > 0 } learned) groupNames.Add(learned);
        }
        return groupNames.Contains(name);
    }
    void CountDeath(int id, DateTime at)
    {
        if (!deaths.Add((id, at))) return;
        // Somebody else's kill (nobody of ours hit it): not counted for the fight, the farm session, the souls or the loot.
        int own = OwnId();
        if (!engaged.Remove(id, out var hitters) || !hitters.Any(h => Ours(h, own) || fightOwnUntil.ContainsKey(h))) return;
        kills++; sessionKills++;
        pets.Killed(entities.NpcIdOf(id));
        observed.Killed(entities.NpcIdOf(id), at);
        Death?.Invoke(id, entities.NpcIdOf(id), at);
    }
    // The announced NPC id, else the one remembered for this entity (bosses are announced only on zone entry).
    int? NpcOf(int id)
    {
        long? highest = entities.HitPoints.HighestSeen(id);
        int? known = entities.NpcIdOf(id) ?? memory.Recall(id, highest);
        if (known is int npc) memory.Learn(id, npc, highest);
        return known;
    }
    /// <summary>
    /// The local character's combat id: its own record of this session, else the identity the detailed-stats frames
    /// confirmed on this map, else the id the configured name was registered under last.
    /// The first id with the name came first before 2026-10-18: the own character gets a new id on every map load, and the
    /// names of this game session restored from disk (PlayerMemory) held 14 ids for «Darkni». At Gran Vigía Nahma the old
    /// 9580 was taken for the player (325), and the overlay counted 0 damage while the player had done 2.9 M. Under the gate.
    /// </summary>
    /// <summary>Pauses longer than this, in seconds, do not count for DPS (see Snapshot).</summary>
    public const double ActiveGap = 5;
    /// <summary>The same for the DPS on your target, as the game's analyzer pauses its clock (see Snapshot).</summary>
    public const double TargetGap = 4;
    /// <summary>
    /// Seconds of sustained hitting a row needs before its DPS orders the list. A run counts at least 1 s for the DPS, so one 90k
    /// hit at the end of a fight was 90k/s and went first, and a Cleric hitting every 6–8 s showed his hit size as DPS (reviews of
    /// 2026-10-27). Only runs of two hits or more count here, at their real length. The rows below it go after the ones that rank,
    /// by damage (then healing), with a bar of their damage and their DPS dimmed, and do not set the bars' scale. At least half of
    /// the time the DPS counts must be that sustained hitting: a few pairs of hits do not lift a hitter whose other hits each count
    /// 1 s. The same for you: your row is always in the list, but ranks only by the same rule.
    /// </summary>
    public const double RankMinActive = ActiveGap;
    public static bool Ranks(ActorRow a) => a.Active >= RankMinActive && a.Dps > 0 && a.Active >= .5 * (a.Damage / a.Dps);
    public static List<ActorRow> ByDps(IEnumerable<ActorRow> rows) =>
        rows.OrderByDescending(Ranks).ThenByDescending(a => Ranks(a) ? a.Dps : a.Damage).ThenByDescending(a => a.Healing).ToList();
    /// <summary>The seconds of the runs of two hits or more in these times (pauses over the gap split runs), at their real length.</summary>
    public static double SustainedSeconds(IEnumerable<DateTime> times, double gap = ActiveGap)
    {
        double total = 0; DateTime? start = null, previous = null; int hits = 0;
        foreach (var t in times.Order())
        {
            if (previous is DateTime p && (t - p).TotalSeconds > gap) { if (hits > 1) total += (p - start!.Value).TotalSeconds; start = t; hits = 0; }
            start ??= t; previous = t; hits++;
        }
        if (previous is DateTime last && hits > 1) total += (last - start!.Value).TotalSeconds;
        return total;
    }
    /// <summary>A row's bar, 0 to 1: its DPS against the best that ranks, or, below the ranked ones, its damage against the most.</summary>
    public static double BarOf(ActorRow a, double bestDps, long mostDamage) => Math.Clamp(Ranks(a) ? a.Dps / Math.Max(1, bestDps) : (double)a.Damage / Math.Max(1, mostDamage), 0, 1);
    /// <summary>The rows a list shows: the <paramref name="n"/> with the most damage, yours always among them, ordered by DPS.</summary>
    public static List<ActorRow> TopByDps(IEnumerable<ActorRow> rows, int n, int local)
    {
        var all = rows.ToList();
        var top = all.OrderByDescending(a => a.Damage).Take(n).ToList();
        if (local > 0 && top.Count == n && top.All(a => a.Id != local) && all.FirstOrDefault(a => a.Id == local) is { } me) top[^1] = me;
        return ByDps(top);
    }
    /// <summary>The scale of the DPS bars: the best DPS among the rows that rank.</summary>
    public static double BestDps(IEnumerable<ActorRow> rows) => Math.Max(1, rows.Where(Ranks).Select(a => a.Dps).DefaultIfEmpty(1).Max());
    /// <summary>The seconds of hitting in these times: each run without a pause over the gap, at least 1 s each.</summary>
    public static double ActiveSeconds(IEnumerable<DateTime> times, double gap = ActiveGap)
    {
        double total = 0; DateTime? start = null, previous = null;
        foreach (var t in times.Order())
        {
            if (previous is DateTime p && (t - p).TotalSeconds > gap) { total += Math.Max(1, (p - start!.Value).TotalSeconds); start = t; }
            start ??= t; previous = t;
        }
        if (previous is DateTime last) total += Math.Max(1, (last - start!.Value).TotalSeconds);
        return Math.Max(1, total);
    }
    // A hit of yours in this fight: by your id now, or by an id that was yours when the hit came (an earlier map). Under the gate.
    bool WasMine(DamageEvent h, int local) => h.SourceObjectId == local || fightOwnUntil.TryGetValue(h.SourceObjectId, out var until) && h.Timestamp <= until;
    // An actor's name and kind as they were in this fight: once a map load has handed its ids to others, today's directory is
    // only right for your own current id. Under the gate.
    string FightName(int id, int local, Func<int, string?> nameNow)
    {
        bool sameMap = fightLoads == ownPos.Loads;
        return (id == local ? nameNow(id) ?? fightNames.GetValueOrDefault(id) ?? localName : null) ?? fightNames.GetValueOrDefault(id)
            ?? (sameMap ? nameNow(id) : null) ?? (FightNpc(id) is int npc ? Catalog.NpcName(npc) : $"Entidad {id}");
    }
    int? FightNpc(int id) => fightLoads == ownPos.Loads ? NpcOf(id) : fightNpcs.TryGetValue(id, out int npc) ? npc : null;
    int LocalCombatId()
    {
        if (entities.LocalCharacter is { Restored: false } record) return record.CombatId;
        if (sessionIdentity is { } own) return own.Id;
        return entities.IdOfName(localName) is int id and > 0 ? id : 0;
    }
    public CaptureSnapshot Snapshot()
    {
        lock(gate)
        {
            var directory = entities.Snapshot();
            playerMemory.Remember(entities, directory.Names);
            ConfirmCarried();
            spawnHp.SaveIfDue();
            // Never infer the local owner just because they caused most damage. Require the configured name or character record.
            int local = LocalCombatId();
            // The character record only arrives on some zone changes. The detailed-stats frames are sent to the
            // local player alone (hundreds vs. 0-5 for anyone else); accept that only when one id clearly dominates.
            bool inferred = false;
            if (local <= 0 && Guess() is int guess && guess > 0) { local = guess; inferred = true; }
            // The guess can drop out for a moment: keep the id known on this map, so your DPS does not fall to 0 meanwhile.
            if (local <= 0 && lastOwnId > 0) local = lastOwnId;
            if (local > 0) lastOwnId = local;
            ownPos.LocalId = local; assistant.LocalId = local;
            double duration = started is null ? 0 : Math.Max(1, ((lastHit ?? started.Value) - started.Value).TotalSeconds);
            var bossTargets = new Dictionary<int, bool>();
            bool Counts(DamageEvent h) => !BossOnly || (bossTargets.TryGetValue(h.TargetObjectId, out bool boss) ? boss
                : bossTargets[h.TargetObjectId] = NpcOf(h.TargetObjectId) is int n && Catalog.IsElite(n));
            var counted = BossOnly ? fight.Where(Counts).ToList() : fight;
            var own = counted.Where(h => WasMine(h, local)).ToList();
            long total = own.Where(h => !h.IsHeal).Sum(h => h.Amount), healing = own.Where(h => h.IsHeal).Sum(h => h.Amount);
            var skills = own.Where(h => !h.IsHeal).GroupBy(h => h.SkillId).Select(g => new SkillRow(g.Key,
                Aion2SkillNames.Display(g.First().Skill), Aion2SkillIcons.PathFor(g.Key),
                g.Sum(h => h.Amount), g.Count(), g.Count(h => h.IsCritical), total == 0 ? 0 : (double)g.Sum(h => h.Amount) / total * 100)).OrderByDescending(s => s.Damage).ToList();
            // Each one's DPS counts only the time they are hitting: their runs of damage, a pause of more than ActiveGap seconds
            // (running to the next pack, waiting) not counted. 2026-10-23: from the first hit to the fight's end, a dungeon run
            // kept going for 3:38 and showed 29,845 while the game's own analyzer said 44,900 on its target (and before that,
            // the user: «mi DPS contando desde mi primer golpe», a fight joined 40 s late showed 18.2K instead of ~27.7K).
            // Each one's DPS over their own time hitting, as the game's analyzer runs its clock: pauses of more than ActiveGap
            // seconds left out, from their first hit. 2026-10-25 the user, with the analyzer on Lakshmi de ensueño at «1,38M
            // (105,31A)» in 0:13 and the overlay at 34,409 («104 vs 29»): the group's time for everyone (the night before, for
            // a ranking in order) spread your damage over the 41 s the group had been fighting before you came. The list is
            // ordered by DPS instead, so it always goes down (see Actors).
            double Since(IEnumerable<DamageEvent> hits) => ActiveSeconds(hits.Where(h => !h.IsHeal).Select(h => h.Timestamp));
            // Players only, then the top 16: monsters (their damage on you) and nameless entities used to take places first and
            // leave players out of big fights, and an unowned summon showed by its internal name («Hechicero_Skill020_A1_Invisible»,
            // 2nd place in the Abyss on 2026-10-22). No player name has an underscore (3,161 seen); internal ones do.
            // Your hits under an earlier id count as yours, in one row (see fightOwnUntil).
            var actors = TopByDps(counted.GroupBy(h => local > 0 && WasMine(h, local) ? local : h.SourceObjectId)
                .Select(g => (Hits: g, Name: FightName(g.Key, local, id => directory.Names.GetValueOrDefault(id))))
                .Where(a => a.Hits.Key == local || FightNpc(a.Hits.Key) is null && !a.Name.StartsWith("Entidad ", StringComparison.Ordinal) && !a.Name.Contains('_'))
                .Select(a =>
                {
                    long damage = a.Hits.Where(h => !h.IsHeal).Sum(h => h.Amount);
                    var times = a.Hits.Where(h => !h.IsHeal).Select(h => h.Timestamp).ToList();
                    return new ActorRow(a.Hits.Key, a.Name, damage, duration == 0 || damage == 0 ? 0 : damage / ActiveSeconds(times), a.Hits.Where(h => h.IsHeal).Sum(h => h.Amount), SustainedSeconds(times));
                }), 16, local);
            // Your DPS on your target, as the game's own analyzer counts it (Ctrl+X, «Objetivo»): your hits on the one you have
            // selected, over your time hitting it, pauses of more than TargetGap seconds left out. It is the target once you hit
            // it in this fight, and it stays after you clear the selection, as the analyzer keeps the last one. 2026-10-24: the
            // game counts only that target («Danar fundido 01:53 · 3,51M» while the overlay said 4.8M with the adds). Its clock
            // matched within 2 %: Berk 130.6 s against 2:10, Danar fundido 115.4 s against 1:53. The damage on Danar matched the
            // other meter's to the hit (4,186,251 in 581); the analyzer's own 3.51M is lower, for reasons not known.
            if (realTarget != 0 && own.Any(h => h.TargetObjectId == realTarget && !h.IsHeal)) fightFocus = realTarget;
            var onFocus = fightFocus == 0 ? [] : own.Where(h => h.TargetObjectId == fightFocus && !h.IsHeal).ToList();
            long focusDamage = onFocus.Sum(h => h.Amount);
            double focusDps = onFocus.Count == 0 ? 0 : focusDamage / ActiveSeconds(onFocus.Select(h => h.Timestamp), TargetGap);
            TargetInfo? target = null;
            if (realTarget != 0)
            {
                int? targetNpc = NpcOf(realTarget);
                long? observed = entities.HitPoints.HighestSeen(realTarget), announced = spawnHp.MaxOf(realTarget), remembered = targetNpc is int n ? memory.MaxHpOf(n) : null;
                // The spawn frame's maximum is exact. Without it (announced before the app started), the boss's remembered
                // maximum; joined halfway, the highest HP seen is only where the fight was.
                long? max = announced ?? (remembered is long r && r > (observed ?? 0) ? r : observed);
                target = new(realTarget, targetNpc, targetNpc is int npc ? Catalog.NpcName(npc) : directory.Names.GetValueOrDefault(realTarget) ?? $"Entidad {realTarget}",
                    entities.ServerIdOf(realTarget), entities.HitPoints.Latest(realTarget)?.Hp ?? spawnHp.CurrentOf(realTarget), max, announced is not null || targetNpc is int seen && memory.MaxFromEarlier(seen));
                // Boss panel: the players' damage on it per second since their first hit, the time to kill at that pace, and
                // the enrage countdown once this boss was seen enraging (EnrageWatch).
                if (targetNpc is int bossNpc && Catalog.IsElite(bossNpc))
                {
                    var onBoss = fight.Where(h => h.TargetObjectId == realTarget && !h.IsHeal && entities.NpcIdOf(h.SourceObjectId) is null).ToList();
                    // The encounter is the last run of hits without a 10 s gap: after a wipe the boss resets to full health
                    // and the group comes back later (2026-10-11 Kromede: the countdown kept running after the reset).
                    int startAt = 0;
                    for (int i = 1; i < onBoss.Count; i++) if ((onBoss[i].Timestamp - onBoss[i - 1].Timestamp).TotalSeconds > 10) startAt = i;
                    onBoss = onBoss.Skip(startAt).ToList();
                    DateTime clockNow = Now;
                    bool reset = onBoss.Count > 0 && ((clockNow - onBoss[^1].Timestamp).TotalSeconds > 10
                        || target.Hp is long full && target.HighestObservedHp is long top && full >= top && (clockNow - onBoss[^1].Timestamp).TotalSeconds > 2);
                    if (onBoss.Count > 0 && !reset)
                    {
                        DateTime first = onBoss[0].Timestamp, now = clockNow;
                        double span = Math.Max(1, (onBoss[^1].Timestamp - first).TotalSeconds), elapsed = (now - first).TotalSeconds;
                        double groupDps = onBoss.Sum(h => h.Amount) / span;
                        // The boss's own health over the last 30 s: it falls with everyone's damage, also players whose hits
                        // the capture does not see (open world), and it stops falling when the group stops. Preferred when known.
                        long? hpForKill = target.Hp; double sinceSample = 0;
                        var samples = entities.HitPoints.SamplesAround(realTarget, first > now.AddSeconds(-30) ? first : now.AddSeconds(-30), now);
                        if (samples.Count >= 2 && (samples[^1].At - samples[0].At).TotalSeconds >= 3)
                        {
                            double drop = samples[0].Hp - samples[^1].Hp, secs = (samples[^1].At - samples[0].At).TotalSeconds;
                            if (drop > 0) groupDps = drop / secs;
                            hpForKill = samples[^1].Hp; sinceSample = Math.Max(0, (now - samples[^1].At).TotalSeconds);
                        }
                        enrage.NoteFight(realTarget, bossNpc, first);
                        var enragedAt = enrage.EnragedAt(realTarget);
                        if (enragedAt is DateTime e && e >= first) enrage.Learn(bossNpc, (e - first).TotalSeconds);
                        double? enrageIn = enrage.EnrageAfter(bossNpc) is double after && enragedAt is null ? after - elapsed : null;
                        // The server's own enrage moment for this boss (0x4136 record), when it came for this fight: exact.
                        if (enrage.EnrageAtClock(realTarget) is DateTime serverAt && serverAt > first.AddMinutes(-1))
                        {
                            enrage.Learn(bossNpc, (serverAt - first).TotalSeconds);
                            if (serverAt <= now) enragedAt ??= serverAt; else enrageIn = (serverAt - now).TotalSeconds;
                        }
                        double? atEnrage = enrageIn is double left && left > 0 && target.Hp is long hpNow && target.HighestObservedHp is long hpMax && hpMax > 0
                            ? Math.Max(0, hpNow - groupDps * left) / hpMax * 100 : null;
                        target = target with { GroupDps = groupDps, KillSeconds = hpForKill is long h && groupDps > 0 ? Math.Max(0, h / groupDps - sinceSample) : null, FightSeconds = elapsed,
                            EnrageIn = enrageIn, HpAtEnragePct = atEnrage, Enraged = enragedAt is not null, EnragedFor = enragedAt is DateTime since ? Math.Max(0, (now - since).TotalSeconds) : null,
                            RequiredDps = enrageIn is double untilEnrage && untilEnrage > 0 && target.Hp is long hpLeft ? hpLeft / untilEnrage : null };
                    }
                }
            }
            return new(Status, packets, stream.Frames, damageEvents, errors, decoder.CurrentZone, local,
                entities.LearnedLocalName ?? (local > 0 ? directory.Names.GetValueOrDefault(local) : null) ?? (inferred ? localName : null),
                total, healing, duration == 0 ? 0 : total / Since(own), duration, kills, entities.LiveMobs().Count, target, skills, actors,
                decoder.UnknownOpcodes, stream.Gaps, stream.Retransmissions, paused, inferred,
                casts.Recent(clockOrigin + clock.Elapsed, TimeSpan.FromSeconds(20), NpcOf, realTarget), party.Names.Order().ToList(), local > 0 ? entities.ServerIdOf(local) : null, ping.Milliseconds, Position(),
                entities.LiveMobs().Where(m => !Catalog.IsHelper(m.NpcId)).Select(m => new MobDot(m.NpcId, m.X, m.Y, Catalog.IsElite(m.NpcId))).ToList(), pets.Hunt(entities.LocalPetStates, target?.NpcId, PinnedPets), RoomNow(), od.Current, troop.Current, ownPos.World, ownPos.Loads,
                focusDamage, focusDps);
        }
    }
    /// <summary>Players the game named lately (newest last), for the PvP panel to load their profiles ahead of a target.</summary>
    public IReadOnlyList<string> RecentPlayers(int count)
    {
        lock(gate)
        {
            var directory = entities.Snapshot();
            return directory.Names.Where(p => p.Key > 0 && entities.NpcIdOf(p.Key) is null && !p.Value.Equals(localName, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Value).Distinct().TakeLast(count).ToList();
        }
    }
    MapPosition? Position()
    {
        // The player's own place when the game told it lately (own buffs and skills, monsters' skills on him, the
        // position report); else the middle of the creatures around, which follows him while he walks.
        var own = new[] { ownPos.Last, entities.LocalPosition }.Where(o => o is not null).Select(o => o!.Value).OrderByDescending(o => o.At).FirstOrDefault();
        if (own.At != default && DateTime.UtcNow - own.At < TimeSpan.FromSeconds(8)) return new(own.X, own.Y, true);
        if (entities.BestPosition is not { } best) return own.At != default ? new(own.X, own.Y, false) : null;
        return new(best.X, best.Y, false);
    }
    /// <summary>A fight that ended (the next one starting, or 15 s quiet, or the app closing), for the local history.</summary>
    public event Action<FightRecord>? FightEnded;
    DateTime? savedLastHit;
    readonly List<AvoidEvent> avoids = [];
    readonly List<KillEvent> playerDeaths = [];
    public void ResetFight()
    {
        FightRecord? ended = null;
        lock(gate)
        {
            if (lastHit is not null && lastHit != savedLastHit) ended = BuildFight();
            // The kills shown beside the fight's damage are this fight's (they used to add up since the app opened).
            fight.Clear(); extras.Clear(); avoids.Clear(); playerDeaths.Clear(); started = lastHit = null; engaged.Clear(); savedLastHit = null; kills = 0;
            fightOwnUntil.Clear(); fightNames.Clear(); fightNpcs.Clear(); fightClasses.Clear(); fightFocus = 0;
        }
        if (ended is not null) _ = Task.Run(() => FightEnded?.Invoke(ended));
    }
    /// <summary>The current fight once it has been quiet for the idle time (or at once with force, when the app closes),
    /// only once per fight; null otherwise.</summary>
    public FightRecord? EndedFight(bool force = false)
    {
        lock(gate)
        {
            if (lastHit is not DateTime last || last == savedLastHit || !force && (Now - last).TotalSeconds < IdleSeconds) return null;
            var record = BuildFight();
            savedLastHit = last;
            return record;
        }
    }
    // Under the gate. Players only (no creatures), the main target is the creature that took the most damage.
    FightRecord? BuildFight()
    {
        if (started is not DateTime start || lastHit is not DateTime end || fight.Count == 0) return null;
        var s = Snapshot();
        double seconds = Math.Max(1, (end - start).TotalSeconds);
        var players = TopByDps(s.Actors.Where(a => a.Damage + a.Healing > 0 && !a.Name.StartsWith("Entidad ", StringComparison.Ordinal)), 16, s.LocalId); // by DPS, as the overlay
        long total = players.Sum(p => p.Damage);
        if (seconds < 5 || total <= 0) return null;
        var rows = players.Select(a =>
        {
            var skills = (Breakdown(a.Id, null, a.Damage == 0)?.Skills ?? []).Take(12)
                .Select(k => new FightSkill(k.Name, k.Icon, k.Amount, k.Hits, Math.Round(k.CritPct, 1), Math.Round(k.Share, 1))).ToList();
            return new FightPlayer(a.Name, a.Id == s.LocalId || fightLoads == ownPos.Loads ? entities.ClassOf(a.Id) : fightClasses.GetValueOrDefault(a.Id), a.Damage, Math.Round(a.Dps), a.Healing, // over each one's time hitting, as shown
                total > 0 ? Math.Round(a.Damage * 100.0 / total, 1) : 0, a.Id == s.LocalId || sessionIdentity is { } own && (a.Id == own.Id || a.Name.Equals(own.Name, StringComparison.OrdinalIgnoreCase)) || s.Character is { } me && a.Name.Equals(me, StringComparison.OrdinalIgnoreCase), skills, Ranks(a));
        }).ToList();
        var main = fight.Where(h => !h.IsHeal && NpcOf(h.TargetObjectId) is not null).GroupBy(h => h.TargetObjectId)
            .OrderByDescending(g => g.Sum(h => h.Amount)).Select(g => g.Key).FirstOrDefault();
        int? npc = main > 0 ? NpcOf(main) : null;
        string target = npc is int n ? Catalog.NpcName(n) : "Combate";
        bool boss = npc is int e && Catalog.IsElite(e);
        bool killed = main > 0 && deaths.Any(d => d.Item1 == main);
        string place = PlaceOf(npc, ownPos.World);
        var me = rows.FirstOrDefault(r => r.Self);
        var summary = new FightSummary($"{start:yyyyMMdd-HHmmss}-{Math.Max(0, main)}", start, end, Math.Round(seconds, 1), place, target, boss, killed,
            total, Math.Round(total / seconds), me?.Damage ?? 0, me?.Dps ?? 0, rows.Count, s.Character, rows.FirstOrDefault()?.Name);
        return new(summary, rows, main > 0 ? entities.HitPoints.HighestSeen(main) : null, kills);
    }
    static string PlaceOf(int? npc, int? world)
    {
        // A boss's dungeon from the catalog ("urugugu_canyon" -> "Urugugu Canyon"); else the open-world map; else the kind.
        if (npc is int n && Catalog.Npc(n) is { } r && r.TryGetProperty("dungeons", out var d) && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0
            && d[0].GetString() is { Length: > 0 } slug)
            return string.Join(' ', slug.Split('_').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
        if (MinimapData.Map(MinimapData.MapOfWorld(world)) is { } map) return map.Name;
        return world is > 100_000 ? "Mazmorra" : world is int w ? $"Zona {w}" : "Zona desconocida";
    }
    /// <summary>
    /// The breakdown of one player's damage (or healing) in the current fight, optionally on one target: per skill the
    /// amount, share, casts (hits sharing the frame's sequence byte, within 3 s), hits, multi-hit and critical rates,
    /// biggest hit and average per cast. actorId 0 = the local player.
    /// </summary>
    public BreakdownView? Breakdown(int actorId, int? targetId, bool healing)
    {
        lock(gate)
        {
            var directory = entities.Snapshot();
            int local = LocalCombatId();
            if (local <= 0 && Guess() is int guess) local = guess;
            int actor = actorId > 0 ? actorId : local;
            string NameOf(int id) => FightName(id, local, id => directory.Names.GetValueOrDefault(id));
            // Your hits under an earlier id of this fight are yours too (see fightOwnUntil).
            bool By(DamageEvent h, int who) => who == local && local > 0 ? WasMine(h, local) : h.SourceObjectId == who;
            int KeyOf(DamageEvent h) => local > 0 && WasMine(h, local) ? local : h.SourceObjectId;
            // Players only: named, or the local one (an unannounced boss is "Entidad n" and hits too).
            var players = fight.Where(h => !h.IsHeal).GroupBy(KeyOf).Where(g => g.Key == local || FightNpc(g.Key) is null && !NameOf(g.Key).StartsWith("Entidad ", StringComparison.Ordinal) && !NameOf(g.Key).Contains('_'))
                .Select(g => new ActorRow(g.Key, NameOf(g.Key), g.Sum(h => h.Amount), 0, fight.Where(h => h.IsHeal && By(h, g.Key)).Sum(h => h.Amount)))
                .OrderByDescending(a => a.Damage).Take(24).ToList();
            if (actor <= 0) return null;
            var mine = fight.Where(h => By(h, actor) && h.IsHeal == healing).ToList();
            var targets = mine.GroupBy(h => h.TargetObjectId).Select(g => new BreakdownTarget(g.Key, NameOf(g.Key), g.Sum(h => h.Amount))).OrderByDescending(t => t.Amount).Take(12).ToList();
            if (targetId is int only) mine = mine.Where(h => h.TargetObjectId == only).ToList();
            double duration = mine.Count == 0 ? 0 : Math.Max(1, (mine[^1].Timestamp - mine[0].Timestamp).TotalSeconds);
            // Per second over the time hitting (pauses over ActiveGap left out), as the overlay's row and the game's analyzer.
            double active = mine.Count == 0 ? 0 : ActiveSeconds(mine.Select(h => h.Timestamp));
            long total = mine.Sum(h => h.Amount);
            var rows = new List<BreakdownSkill>();
            int allCasts = 0;
            foreach (var g in mine.GroupBy(h => h.SkillId))
            {
                int casts = 0, multi = 0; byte? seq = null; DateTime last = DateTime.MinValue;
                foreach (var h in g)
                {
                    var x = extras.TryGetValue(h, out var e) ? e : (HitExtra?)null;
                    if (x is { Count: > 1 }) multi++;
                    if (h.IsTick) continue;
                    if (x is not { } known || seq != known.Sequence || (h.Timestamp - last).TotalSeconds > 3) casts++;
                    seq = x?.Sequence; last = h.Timestamp;
                }
                allCasts += casts;
                long amount = g.Sum(h => h.Amount);
                var first = g.First();
                var flagged = g.Where(h => !h.IsTick).Select(h => extras.TryGetValue(h, out var e) ? e : (HitExtra?)null).OfType<HitExtra>().ToList();
                double Pct(Func<HitExtra, bool> test) => flagged.Count == 0 ? 0 : (double)flagged.Count(test) / flagged.Count * 100;
                rows.Add(new(g.Key, Aion2SkillNames.Display(first.Skill ?? Aion2SkillNames.NameOf(g.Key)), Aion2SkillIcons.PathFor(g.Key), amount, total == 0 ? 0 : (double)amount / total * 100,
                    casts, g.Count(), (double)multi / g.Count() * 100, (double)g.Count(h => h.IsCritical) / g.Count() * 100, g.Max(h => h.Amount), amount / Math.Max(1, casts > 0 ? casts : g.Count()),
                    Pct(e => (e.Mod & 0x80) != 0), Pct(e => (e.Mod & 0x04) != 0), Pct(e => (e.Mod & 0x08) != 0)));
            }
            var direct = mine.Where(h => !h.IsTick).ToList();
            // Attacks of this player the target avoided (parry, block, dodge), as a share of its attacks.
            var avoided = avoids.Where(a => a.SourceObjectId == actor && (targetId is not int t || a.TargetObjectId == t)).ToList();
            double AvoidPct(AvoidKind kind) => direct.Count + avoided.Count == 0 ? 0 : (double)avoided.Count(a => a.Kind == kind) / (direct.Count + avoided.Count) * 100;
            long heals = fight.Where(h => h.IsHeal && By(h, actor)).Sum(h => h.Amount);
            double Rate(Func<HitExtra, bool> test) { var known = direct.Select(h => extras.TryGetValue(h, out var e) ? e : (HitExtra?)null).Where(e => e is not null).ToList(); return known.Count == 0 ? 0 : (double)known.Count(e => test(e!.Value)) / known.Count * 100; }
            return new(actor, NameOf(actor), healing, duration, total, total / Math.Max(1, active), allCasts, mine.Count,
                direct.Count == 0 ? 0 : (double)direct.Count(h => h.IsCritical) / direct.Count * 100, Rate(e => e.Count > 1), Rate(e => (e.Mod & 0x80) != 0),
                targetId, targets, rows.OrderByDescending(r => r.Amount).ToList(), players,
                Rate(e => (e.Mod & 0x04) != 0), Rate(e => (e.Mod & 0x08) != 0),
                AvoidPct(AvoidKind.Parry), AvoidPct(AvoidKind.Block), AvoidPct(AvoidKind.Dodge),
                playerDeaths.Count(d => d.VictimObjectId == actor), heals, heals / Math.Max(1, healing ? active : ActiveSeconds(fight.Where(h => h.IsHeal && By(h, actor)).Select(h => h.Timestamp))),
                Extra(actor, mine, rows, direct.Count, avoided, NameOf, healing));
        }
    }
    // The tabs of the fight details, from the hits of this fight and the effects seen on the player.
    BreakdownExtra? Extra(int actor, List<DamageEvent> mine, List<BreakdownSkill> rows, int directCount, List<AvoidEvent> avoided, Func<int, string> nameOf, bool healing)
    {
        if (fight.Count == 0) return null;
        DateTime from = fight[0].Timestamp, to = fight[^1].Timestamp;
        double span = Math.Max(1, (to - from).TotalSeconds);
        // DPS over time: about 60 buckets, never under 1 s.
        double bucket = Math.Max(1, Math.Ceiling(span / 60));
        var dps = new double[(int)Math.Ceiling(span / bucket) + 1];
        foreach (var h in mine) dps[Math.Min(dps.Length - 1, (int)((h.Timestamp - from).TotalSeconds / bucket))] += h.Amount / bucket;
        var events = new List<TimelineEvent>();
        if (mine.Count > 0 && mine.MaxBy(h => h.Amount) is { } big)
            events.Add(new((big.Timestamp - from).TotalSeconds, "hit", $"Golpe más grande: {Aion2SkillNames.Display(big.Skill ?? Aion2SkillNames.NameOf(big.SkillId))} {big.Amount:N0}"));
        // Long-cooldown skills (a minute or more): when each one was used.
        foreach (var g in mine.Where(h => !h.IsTick && CombatAssistant.CooldownSec(h.SkillId) >= 60).GroupBy(h => h.SkillId - h.SkillId % 10000))
        {
            DateTime last = DateTime.MinValue;
            foreach (var h in g.OrderBy(h => h.Timestamp))
            {
                if ((h.Timestamp - last).TotalSeconds < 5) continue;
                last = h.Timestamp;
                events.Add(new((h.Timestamp - from).TotalSeconds, "skill", CombatAssistant.Skill(g.Key)?.Name ?? Aion2SkillNames.Display(h.Skill ?? "")));
            }
        }
        var deaths = new List<DeathInfo>();
        foreach (var d in playerDeaths.Where(d => d.Timestamp >= from.AddSeconds(-2) && d.Timestamp <= to.AddSeconds(10)))
        {
            var taken = fight.Where(h => !h.IsHeal && h.TargetObjectId == d.VictimObjectId && h.Timestamp <= d.Timestamp && h.Timestamp >= d.Timestamp.AddSeconds(-10)).ToList();
            var last5 = taken.Where(h => h.Timestamp >= d.Timestamp.AddSeconds(-5)).Sum(h => h.Amount);
            var lines = taken.TakeLast(5).Reverse().Select(h => $"-{(d.Timestamp - h.Timestamp).TotalSeconds:0.0} s · {nameOf(h.SourceObjectId)} · {Aion2SkillNames.Display(h.Skill ?? Aion2SkillNames.NameOf(h.SkillId))} · {h.Amount:N0}").ToList();
            string victim = nameOf(d.VictimObjectId);
            string? killer = d.KillerObjectId is int k ? nameOf(k) : taken.LastOrDefault() is { SourceObjectId: > 0 } lastHit ? nameOf(lastHit.SourceObjectId) : null;
            deaths.Add(new((d.Timestamp - from).TotalSeconds, victim, killer, lines, last5));
            events.Add(new((d.Timestamp - from).TotalSeconds, "death", $"Muere {victim}" + (killer is null ? "" : $" ({killer})")));
        }
        var buffs = assistant.Uptime(actor, from, to);
        // Advice: only what the data shows for sure.
        var advice = new List<string>();
        // The player's own active time (first to last hit): the time spent away from the fight is not a missed cast.
        double active = mine.Count < 2 ? 0 : (mine[^1].Timestamp - mine[0].Timestamp).TotalSeconds;
        var ownSkills = rows.Select(r => r.Id - r.Id % 10000).ToHashSet();
        if (!healing && active >= 45)
        {
            foreach (var g in rows.GroupBy(r => r.Id - r.Id % 10000))
            {
                double cd = CombatAssistant.CooldownSec(g.Key);
                if (cd < 30) continue;
                int used = g.Max(r => r.Casts), possible = (int)Math.Floor(active / cd) + 1;
                if (used > 0 && used <= possible - 2)
                    advice.Add($"{CombatAssistant.Skill(g.Key)?.Name ?? g.First().Name}: la usaste {used} de {possible} veces posibles (reutilización {cd:0} s). Úsala en cuanto esté lista.");
            }
            // Only the player's own buffs (a skill of theirs): another player's buff is not theirs to keep up.
            // ...and only buffs that can be kept up (lasting about as long as their cooldown): a 20 s buff on a 90 s cooldown tops out at 22 %.
            foreach (var b in buffs.Where(b => b.Uptime < 50 && ownSkills.Contains(b.Skill) && CombatAssistant.CooldownSec(b.Skill) is double cd && cd > 0 && b.AverageSeconds >= cd * .8).Take(3))
                advice.Add($"{b.Name} estuvo activo solo el {b.Uptime:0} % del combate.");
            int total = directCount + avoided.Count;
            if (total >= 20)
            {
                double parried = avoided.Count(a => a.Kind is AvoidKind.Parry or AvoidKind.Block) * 100.0 / total, dodged = avoided.Count(a => a.Kind == AvoidKind.Dodge) * 100.0 / total;
                if (parried >= 10) advice.Add($"El objetivo paró o bloqueó el {parried:0} % de tus ataques: atacar por la espalda lo evita.");
                if (dodged >= 10) advice.Add($"El objetivo esquivó el {dodged:0} % de tus ataques: sube la precisión.");
            }
        }
        int myDeaths = playerDeaths.Count(d => d.VictimObjectId == actor && d.Timestamp >= from && d.Timestamp <= to.AddSeconds(10));
        if (myDeaths > 0) advice.Add($"Moriste {myDeaths} {(myDeaths == 1 ? "vez" : "veces")}: mira en «Muertes» qué te golpeó al final.");
        if (advice.Count == 0) advice.Add("Nada que mejorar a la vista: usaste tus habilidades largas a tiempo y no hubo muertes.");
        return new(bucket, dps, events.OrderBy(e => e.At).ToList(), deaths, buffs, advice);
    }
    public void SetPaused(bool value) { lock(gate) paused = value; }
    public void Export(string path) { lock(gate) File.WriteAllText(path, JsonSerializer.Serialize(new { capturedAt = DateTimeOffset.Now, summary = Snapshot(), events = fight }, new JsonSerializerOptions { WriteIndented = true })); }
    /// <summary>
    /// Follows the game to a new connection. It opens one after a relog, a reconnection or some instance transfers
    /// (2026-10-11: from local port 7568 to 56105 at 13:06), and the capture kept listening to the old one - "Buscando
    /// tráfico del juego" while playing. Called every few seconds; restarts the capture only when the connection changed.
    /// </summary>
    // When the game server last sent a packet on the captured connection (monotonic).
    TimeSpan lastServerPacket = TimeSpan.MinValue;
    double ServerSilentSeconds => lastServerPacket == TimeSpan.MinValue ? double.MaxValue : (clock.Elapsed - lastServerPacket).TotalSeconds;
    public bool CheckConnection()
    {
        lock(gate)
        {
            // Through a VPN proxy the game may hold several local connections: one that brings no game data in 20 s is not it.
            if (Connection is { ViaProxy: true } tried && devices.Count > 0 && lastServerPacket == TimeSpan.MinValue && (clock.Elapsed - startedAt).TotalSeconds > 20)
            { notGame.Add(tried.Key); Stop(); stream = new(); }
        }
        var current = Pick(GameConnection.Find());
        lock(gate)
        {
            // Windows' TCP table is not reliable on every PC (2026-10-12: the game's connection vanished from it for seconds,
            // and showed FIN_WAIT_1 then ESTABLISHED on the same port, while its packets kept arriving). The packets decide:
            // a connection that still delivers is alive, whatever the table says.
            bool alive = Connection is not null && devices.Count > 0 && ServerSilentSeconds < 20;
            if (current is null)
            {
                if (Connection is not null && !alive)
                {
                    var keep = sessionIdentity ?? carriedIdentity; int pid = Connection.Pid;
                    Stop(); carriedIdentity = keep; identityPid = pid;
                    Status = "El juego no tiene conexión con el servidor (desconexión, carga o selección de personaje) · se reconectará sola";
                }
                return false;
            }
            if (Connection is { } known && devices.Count > 0
                && known.LocalPort == current.LocalPort && known.RemoteAddress == current.RemoteAddress && known.RemotePort == current.RemotePort) return false;
            // Another connection in the table while ours still delivers: an instance transfer closes the old one first.
            if (alive && ServerSilentSeconds < 3) return false;
            // Instance transfers open a new connection of the same game process: the identity is carried, to be confirmed.
            var carry = (Connection?.Pid ?? identityPid) == current.Pid ? sessionIdentity ?? carriedIdentity : null;
            bool transfer = Connection is not null || identityPid == current.Pid;
            Stop();
            carriedIdentity = carry;
            if (transfer) { ownPos.ForgetWorld(); entities.ForgetMap(); cubes.ForgetLive(); }
            stream = new(); // a new TCP stream: sequence numbers start over
            lastServerPacket = TimeSpan.MinValue;
            Start();
            Status = "Captura activa · conectado a la nueva conexión del juego";
            return true;
        }
    }
    // Proxy connections already tried that carried no game traffic.
    readonly HashSet<string> notGame = [];
    TimeSpan startedAt;
    GameConnection? Pick(IReadOnlyList<GameConnection> found)
    {
        var usable = found.Where(c => !c.ViaProxy || !notGame.Contains(c.Key)).ToList();
        if (usable.Count == 0 && found.Any(c => c.ViaProxy)) { notGame.Clear(); usable = found.ToList(); }
        return usable.FirstOrDefault();
    }
    public void Stop()
    {
        lock(gate) { sessionIdentity = null; carriedIdentity = null; Connection = null; }
        foreach(var device in devices) { device.OnPacketArrival -= Arrived; try { device.StopCapture(); device.Close(); } catch { } }
        devices.Clear(); recording?.Dispose(); recording = null;
        Status = "Captura detenida";
    }
    public void Dispose() => Stop();
}
