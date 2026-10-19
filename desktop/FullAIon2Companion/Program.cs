using System.IO;
using System.Text.Json;
using System.Windows;
using AionDPS.Aion2.Capture;

namespace FullAIon2Companion;

public static class Program
{
    static string? testDirectory;
    public static string DataDirectory => testDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FullAIon2Companion");
    [STAThread]
    public static int Main(string[] args)
    {
        // Research recordings can use their own state directory while the normal companion keeps running.
        // Usage: --record <seconds, max 300> <output-directory>.
        if (args.FirstOrDefault() == "--record" && args.Length > 2)
            testDirectory = Path.GetFullPath(args[2]);
        // --replay <recording> <state-directory>: the same, so a replay never writes into the running companion's data.
        if (args.FirstOrDefault() == "--replay" && args.Length > 2)
            testDirectory = Path.GetFullPath(args[2]);
        // --lang es|en|pt, anywhere after the command: the windows' texts (and skill names) in that language, for the tests
        // that draw them. Taken out of the arguments, so each test reads its own as before.
        int langAt = Array.IndexOf(args, "--lang");
        if (langAt > 0 && langAt + 1 < args.Length && args[langAt + 1] is "es" or "en" or "pt")
        {
            L.Language = AionDPS.Aion2.Protocol.Aion2SkillNames.Language = args[langAt + 1];
            args = [.. args.Where((_, i) => i != langAt && i != langAt + 1)];
        }
        Directory.CreateDirectory(DataDirectory);
        try
        {
            if(args.FirstOrDefault()=="--panel-state-dump")
            {
                // Read-only renderer QA: local public profile/pets plus an explicitly labelled UI fixture.
                var snapshot=JsonSerializer.Deserialize<CaptureSnapshot>(File.ReadAllText(Path.Combine(DataDirectory,"live.json")))!;
                using var account=JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory,"account-live.json")));
                JsonElement? title=account.RootElement.TryGetProperty("title",out var t)?t:null;
                var settings=AppSettings.Load();
                var profile=PlayerProfiles.Get(snapshot.Character??"Darkni",snapshot.LocalServerId??settings.LastServerId,PlayerProfiles.RegionOf(settings.RegionOffset));
                var wait=System.Diagnostics.Stopwatch.StartNew();
                while(profile.Card is null&&profile.Stage is "search" or "basic"&&wait.Elapsed<TimeSpan.FromSeconds(25))Thread.Sleep(50);
                var fixture=new{mode="renderer-fixture-from-local-game-data-not-a-license-grant",version=AppVersion.Current,
                    profile=new{stage=profile.Stage,error=profile.Error,card=profile.Card},snapshot,settings,
                    petCollection=new PetCollection().Of(snapshot.Character,snapshot.LocalServerId??settings.LastServerId),
                    petsSync="Colección guardada en este equipo",petList=PetTracker.All.Select(p=>new{id=p.Id,name=p.Name}),
                    events=Array.Empty<object>(),mobs=Array.Empty<object>(),announcements=Array.Empty<object>(),minimapLayers=Array.Empty<object>(),
                    cloud=new{state="Connected",pull=account.RootElement.GetProperty("pull"),title},
                    features=new{pvp=false,minimap=false},paired=true,companion="Dispositivo conectado",message="Panel de pruebas",
                    overlay=false,locked=true,npc=(object?)null,alert="",diagnostics="",
                    license=new LicenseView("no-character","Personaje sin identificar","La captura todavía no identificó al personaje",null,false,false,null,null,"Conectado")};
                File.WriteAllText(Path.GetFullPath(args[1]),UiContract.Serialize(fixture));return 0;
            }
            if(args.FirstOrDefault() == "--license-server-check")
            {
                // Explicit diagnostic only: it never opens a Plus window or grants a local permission.
                var client = new CompanionClient(AppSettings.Load());
                client.Pull().GetAwaiter().GetResult();
                var profile = PlayerProfiles.Get(args[1], int.Parse(args[2]), args[3]);
                var limit = System.Diagnostics.Stopwatch.StartNew();
                while(profile.Card is null && profile.Stage is "search" or "basic" && limit.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(50);
                var reference = OwnLicenseIdentity.Resolve((1,args[1],int.Parse(args[2])),profile.Card,args[3]) ?? throw new InvalidOperationException("No se resolvió la referencia oficial para la prueba HTTPS");
                var answer = client.LicenseAsync(reference,CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(Path.GetFullPath(args[4]), JsonSerializer.Serialize(new { mode = "live-https-contract-only", checkedAt = DateTime.UtcNow,
                    accountMatches = answer.AccountId == client.AccountId, deviceMatches = answer.DeviceId == client.DeviceId, characterMatches = reference.SameAs(answer.Character),
                    status = answer.Status, reason = answer.Reason, pvp = answer.CompanionPvp, minimap = answer.CompanionMinimap, verificationReady = client.LastPull!.VerificationReady }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if(args.FirstOrDefault() == "--identity-replay-test")
            {
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var identities = new List<object>(); int segments = 0;
                    using (var engine = new CaptureEngine())
                    {
                        (int Id, string Name, int ServerId)? previous = null;
                        foreach (var line in File.ReadLines(args[1]))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer)); segments++;
                            if (engine.CapturedIdentity != previous) { previous = engine.CapturedIdentity; identities.Add(new { at = s.Timestamp, own = previous is {} c ? new { c.Id, c.Name, c.ServerId } : null }); }
                        }
                        var snapshot = engine.Snapshot();
                        File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(new { mode = "recorded-game-replay", segments, identities, snapshot.DamageEvents, snapshot.DecodeErrors, snapshot.Character, snapshot.LocalServerId }, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--find-bytes")
            {
                // Diagnostic: every frame (any opcode, inside bundles too) containing a byte pattern, with time, opcode and offset.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    byte[] pattern = Convert.FromHexString(args[2]); var rows = new List<object>(); DateTime at = DateTime.MinValue;
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    engine.Tap = (f, t) => Frames.Walk(f, x =>
                    {
                        if (x.Length < 2 || System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(x) == 0xFFFF) return;
                        int i = x.IndexOf(pattern); if (i < 0) return;
                        rows.Add(new { at = t.ToString("HH:mm:ss.ff"), opcode = $"0x{System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(x):X4}", offset = i, len = x.Length, hex = Convert.ToHexString(x) });
                    });
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                    }
                    File.WriteAllText(Path.GetFullPath(args[3]), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--position-scan")
            {
                // Diagnostic: does the server tell the player where he is while he walks? Replays a recording; the exact own
                // positions come from OwnPosition (own buffs and skills, monster skills on him, arrivals). For every server frame,
                // inside bundles too, it counts by opcode the frames starting with the local entity id, and by opcode and offset
                // the float pairs within 1.5 m of an exact position taken within 1 s (another frame carrying the own place), plus,
                // in the gaps of more than 5 s between exact positions, those within 60 m of the last one (what moves while he walks).
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    DateTime at = DateTime.MinValue, nextSnapshot = DateTime.MinValue; int local = 0;
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    var probe = new OwnPosition();
                    var exact = new List<(DateTime At, float X, float Y, int Op)>();
                    var frames = new List<(DateTime At, int Op, byte[] Data)>();
                    var totals = new Dictionary<int, int>(); var ownId = new Dictionary<int, int>();
                    engine.Tap = (f, t) =>
                    {
                        int notes = probe.Notes; probe.LocalId = local; probe.Add(f);
                        if (probe.Notes != notes && probe.Last is { } now) exact.Add((t, now.X, now.Y, probe.LastOpcode));
                        Frames.Walk(f, x =>
                        {
                            if (x.Length < 4) return; int op = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(x); if (op == 0xFFFF) return;
                            totals[op] = totals.GetValueOrDefault(op) + 1; frames.Add((t, op, x.ToArray()));
                            int p = 2; long id = 0; for (int sh = 0; p < x.Length && sh <= 28; sh += 7) { byte b = x[p++]; id |= (long)(b & 127) << sh; if (b < 128) break; }
                            if (local > 0 && id == local) ownId[op] = ownId.GetValueOrDefault(op) + 1;
                        });
                    };
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        if (at >= nextSnapshot) { local = engine.Snapshot().LocalId; nextSnapshot = at.AddSeconds(2); }
                    }
                    // Float pairs near the own place: within 1.5 m of an exact position at most 1 s away, and, during gaps, within 60 m.
                    var close = new Dictionary<(int Op, int Offset), int>(); var inGaps = new Dictionary<(int Op, int Offset), int>(); var sample = new Dictionary<(int, int), string>();
                    var times = exact.Select(e => e.At).ToList();
                    foreach (var (t, op, x) in frames)
                    {
                        int k = times.BinarySearch(t); if (k < 0) k = ~k;
                        var nearest = new[] { k - 1, k }.Where(i => i >= 0 && i < exact.Count).Select(i => exact[i]).OrderBy(e => (e.At - t).Duration()).FirstOrDefault();
                        var previous = k > 0 ? exact[k - 1] : default; var next = k < exact.Count ? exact[k] : default;
                        bool gap = previous.At != default && (next.At == default || next.At - previous.At > TimeSpan.FromSeconds(5));
                        for (int i = 2; i + 8 <= x.Length; i++)
                        {
                            float fx = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(i)), fy = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(i + 4));
                            if (!float.IsFinite(fx) || !float.IsFinite(fy) || Math.Abs(fx) + Math.Abs(fy) < 2000 || Math.Abs(fx) > 1_000_000 || Math.Abs(fy) > 1_000_000) continue;
                            if (nearest.At != default && (nearest.At - t).Duration() <= TimeSpan.FromSeconds(1) && Math.Abs(fx - nearest.X) < 150 && Math.Abs(fy - nearest.Y) < 150)
                            { close[(op, i)] = close.GetValueOrDefault((op, i)) + 1; sample.TryAdd((op, i), Convert.ToHexString(x.AsSpan(0, Math.Min(x.Length, 48)))); }
                            if (gap && t - previous.At > TimeSpan.FromSeconds(2) && Math.Abs(fx - previous.X) < 6000 && Math.Abs(fy - previous.Y) < 6000)
                                inGaps[(op, i)] = inGaps.GetValueOrDefault((op, i)) + 1;
                        }
                    }
                    var gaps = exact.Zip(exact.Skip(1), (a, b) => (b.At - a.At).TotalSeconds).ToList();
                    string Op(int op) => $"0x{op:X4}";
                    File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(new
                    {
                        recording = Path.GetFileName(args[1]), frames = frames.Count, localId = local, exactPositions = exact.Count,
                        seconds = frames.Count > 0 ? Math.Round((frames[^1].At - frames[0].At).TotalSeconds) : 0,
                        gapsOver5s = gaps.Count(g => g > 5), gapsOver10s = gaps.Count(g => g > 10), longestGap = gaps.Count > 0 ? Math.Round(gaps.Max()) : 0,
                        bySource = exact.GroupBy(e => e.Op).OrderByDescending(g => g.Count()).Select(g => new { opcode = Op(g.Key), count = g.Count() }),
                        ownIdFrames = ownId.OrderByDescending(o => o.Value).Select(o => new { opcode = Op(o.Key), count = o.Value, total = totals[o.Key] }),
                        closeToExact = close.OrderByDescending(c => c.Value).Take(25).Select(c => new { opcode = Op(c.Key.Op), offset = c.Key.Offset, count = c.Value, total = totals[c.Key.Op], sample = sample[c.Key] }),
                        nearWhileWalking = inGaps.OrderByDescending(c => c.Value).Take(25).Select(c => new { opcode = Op(c.Key.Op), offset = c.Key.Offset, count = c.Value, total = totals[c.Key.Op] }),
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--resources-test")
            {
                // Recursos: replays a recording with a login and writes the counters (0B61/0C61) and coins (0x1156/0x1B56) read.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    DateTime at = DateTime.MinValue;
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                    }
                    var coins = engine.Inventory.Holdings(ResourceBook.IsCoin).OrderBy(c => c.Key).Select(c => new { item = c.Key, name = InventoryWatch.Info(c.Key).Name, count = c.Value });
                    File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(new { engine.Resources.Complete, engine.Resources.At, counters = engine.Resources.Counters.Values.OrderBy(c => c.Key), coins },
                        new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--opcode-dump")
            {
                // Diagnostic: every frame of one opcode in a recording, full hex, with the NPC of the entity right after the opcode.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    int wanted = Convert.ToInt32(args[2], 16); var frames = new List<(DateTime At, byte[] Data)>(); DateTime at = DateTime.MinValue;
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    engine.Tap = (f, t) => Frames.Walk(f, x => { if (x.Length >= 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(x) == wanted) frames.Add((t, x.ToArray())); });
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                    }
                    var rows = frames.Select(x =>
                    {
                        int p = 2; long id = 0; for (int sh = 0; p < x.Data.Length && sh <= 28; sh += 7) { byte b = x.Data[p++]; id |= (long)(b & 127) << sh; if (b < 128) break; }
                        int? npc = engine.Entities.NpcIdOf((int)id);
                        return new { at = x.At.ToString("HH:mm:ss.ff"), entity = id, npc, name = npc is int n ? Catalog.NpcName(n) : null, elite = npc is int e && Catalog.IsElite(e), len = x.Data.Length, hex = Convert.ToHexString(x.Data) };
                    }).ToList();
                    File.WriteAllText(Path.GetFullPath(args[3]), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--boss-frames")
            {
                // Diagnostic: the frames naming the main boss of a recorded fight from 5 s before to 15 s after the first hit,
                // with every value that looks like a duration (whole seconds as u16/u32, or whole seconds in ms as u32).
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var frames = new List<(DateTime At, byte[] Data)>(); DateTime at = DateTime.MinValue;
                    var hits = new Dictionary<int, (int Count, DateTime First)>();
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    engine.Tap = (f, t) => Frames.Walk(f, x => frames.Add((t, x.ToArray())));
                    engine.Combat += h => { if (!h.IsHeal) hits[h.TargetObjectId] = hits.TryGetValue(h.TargetObjectId, out var v) ? (v.Count + 1, v.First) : (1, h.Timestamp); };
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                    }
                    var boss = hits.Where(h => engine.Entities.NpcIdOf(h.Key) is int n && Catalog.IsElite(n)).OrderByDescending(h => h.Value.Count).First();
                    int id = boss.Key; DateTime first = boss.Value.First;
                    var varint = new List<byte>(); for (uint v = (uint)id; ; v >>= 7) { if (v < 128) { varint.Add((byte)v); break; } varint.Add((byte)(v & 127 | 128)); }
                    byte[] key = varint.ToArray();
                    var rows = new List<object>();
                    foreach (var (t, f) in frames)
                    {
                        double rel = (t - first).TotalSeconds;
                        if (rel < -5 || rel > 15 || f.AsSpan().IndexOf(key) < 0) continue;
                        var durations = new List<string>();
                        for (int i = 2; i + 2 <= f.Length; i++)
                        {
                            ushort u16 = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(i));
                            if (u16 is >= 60 and <= 1800 && u16 % 15 == 0) durations.Add($"u16@{i}={u16}s");
                            if (i + 4 <= f.Length)
                            {
                                uint u32 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(i));
                                if (u32 is >= 60_000 and <= 1_800_000 && u32 % 1000 == 0) durations.Add($"u32@{i}={u32}ms");
                            }
                        }
                        rows.Add(new { rel = Math.Round(rel, 2), opcode = $"0x{System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f):X4}", len = f.Length, keyAt = f.AsSpan().IndexOf(key), durations, hex = Convert.ToHexString(f.AsSpan(0, Math.Min(f.Length, 96))) });
                    }
                    File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(new { boss = id, npc = engine.Entities.NpcIdOf(id), name = Catalog.NpcName(engine.Entities.NpcIdOf(id)!.Value), first, hits = boss.Value.Count, rows }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--enrage-scan")
            {
                // Diagnostic: every rage-like effect (enrage, berserk, frenzy, fury, rage, wrath) landing on a creature in the
                // recorded captures given, with the creature, the effect name and the seconds since the first hit on it.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var names = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "buff_names_en.json"))).RootElement;
                    var rows = new List<object>();
                    foreach (var file in args.Skip(2))
                    {
                        DateTime at = DateTime.MinValue; var firstHit = new Dictionary<int, DateTime>();
                        using var engine = new CaptureEngine { ReplayClock = () => at };
                        engine.Enrage.ScanLog = [];
                        engine.Combat += h => firstHit.TryAdd(h.TargetObjectId, h.Timestamp);
                        foreach (var line in File.ReadLines(file))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        }
                        foreach (var g in engine.Enrage.ScanLog.GroupBy(x => (x.Entity, x.Effect)))
                        {
                            var x = g.First(); int? npc = engine.Entities.NpcIdOf(x.Entity);
                            if (npc is null) continue; // players' own buffs are not of interest here
                            string key = $"0x{x.Effect:X8}";
                            rows.Add(new { capture = Path.GetFileNameWithoutExtension(file), npc, creature = Catalog.NpcName(npc.Value), elite = Catalog.IsElite(npc.Value),
                                effect = key, effectName = names.TryGetProperty(key, out var n) ? n.GetString() : null, opcode = $"0x{x.Opcode:X4}", times = g.Count(),
                                at = x.At.ToString("HH:mm:ss"), secondsAfterFirstHit = firstHit.TryGetValue(x.Entity, out var fh) ? Math.Round((x.At - fh).TotalSeconds, 1) : (double?)null });
                        }
                    }
                    File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--history-test")
            {
                // Test: replays a recording; every fight that ends is saved to a throwaway history and listed.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var history = new FightHistory(); DateTime at = DateTime.MinValue;
                    using (var engine = new CaptureEngine { ReplayClock = () => at })
                    {
                        engine.FightEnded += history.Save;
                        foreach (var line in File.ReadLines(args[1]))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                            if (engine.EndedFight() is { } quiet) history.Save(quiet);
                        }
                        if (engine.EndedFight(force: true) is { } last) history.Save(last);
                    }
                    Thread.Sleep(500);
                    var list = history.List();
                    var first = list.Count > 0 ? history.Open(list.OrderByDescending(f => f.Damage).First().Id) : null;
                    File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(new { count = list.Count, list, biggest = first, search = history.List("darkni").Count }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--pace-replay")
            {
                // Diagnostic: replays a recorded fight and logs the boss card (HP, group DPS, time to kill) every 5 s of capture time.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var rows = new List<object>(); DateTime next = DateTime.MinValue, at = DateTime.MinValue;
                    using (var engine = new CaptureEngine { ReplayClock = () => at })
                        foreach (var line in File.ReadLines(args[1]))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                            if (s.Timestamp < next) continue;
                            next = s.Timestamp.AddSeconds(5);
                            if (engine.Snapshot().Target is { GroupDps: not null } t)
                                rows.Add(new { at = s.Timestamp.ToString("HH:mm:ss"), t.Name, t.Hp, max = t.HighestObservedHp, dps = Math.Round(t.GroupDps!.Value), kill = t.KillSeconds is double k ? Math.Round(k) : (double?)null, enrageIn = t.EnrageIn is double ei ? Math.Round(ei) : (double?)null, t.Enraged, hpAtEnrage = t.HpAtEnragePct is double pe ? Math.Round(pe, 1) : (double?)null });
                        }
                    File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--license-test")
            {
                var (ok, results) = LicenseTests.Run().GetAwaiter().GetResult();
                string report = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(DataDirectory, "license-test.json");
                File.WriteAllText(report, JsonSerializer.Serialize(new { ok, version = AppVersion.Current, ranAt = DateTime.UtcNow, mode = "offline", results }, new JsonSerializerOptions { WriteIndented = true }));
                return ok ? 0 : 1;
            }
            if(args.FirstOrDefault() == "--record")
            {
                int seconds = Math.Clamp(args.Length > 1 ? int.Parse(args[1]) : 30, 1, 300);
                string file = Path.Combine(DataDirectory, "captures", DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl");
                using var engine = new CaptureEngine(); engine.Start(file);
                for (int wait = 0; engine.Connection is null && wait < 20; wait++) { Thread.Sleep(1000); engine.Start(file); }
                // Why it failed, for the panel's «Grabar 2 minutos» (record-error.txt), not only startup-error.log.
                if(engine.Connection is null)
                {
                    File.WriteAllText(Path.Combine(DataDirectory, "record-error.txt"), "No se encontró la conexión del juego: entra al juego con el personaje y vuelve a grabar. (" + engine.Status + ")");
                    return 1;
                }
                File.WriteAllText(Path.Combine(DataDirectory, "record-status.json"), JsonSerializer.Serialize(new { file, connection = engine.Connection, status = engine.Status }));
                for (int i = 0; i < seconds; i++)
                {
                    Thread.Sleep(1000);
                    File.WriteAllText(Path.Combine(DataDirectory, "record-live.json"), JsonSerializer.Serialize(engine.Snapshot()));
                }
                File.WriteAllText(Path.Combine(DataDirectory, "record-result.json"), JsonSerializer.Serialize(new { file, snapshot = engine.Snapshot() }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if(args.FirstOrDefault() == "--profile")
            {
                // Diagnostic: the PvP panel's lookup for one player, written as JSON.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var state = PlayerProfiles.Get(args[1], args.Length > 2 ? int.Parse(args[2]) : 2401, args.Length > 3 ? args[3] : "la");
                var stages = new List<string>();
                while ((state.Stage is "search" or "basic" || state.Stale) && clock.Elapsed < TimeSpan.FromSeconds(30)) { if (stages.LastOrDefault()?.StartsWith(state.Stage) != true) stages.Add($"{state.Stage} @{clock.ElapsedMilliseconds}ms"); Thread.Sleep(20); }
                stages.Add($"{state.Stage} @{clock.ElapsedMilliseconds}ms");
                System.Text.Json.JsonElement? item = state.Card?.Gear.FirstOrDefault() is { } first ? PlayerProfiles.ItemDetail(state.Card, first).GetAwaiter().GetResult() : null;
                File.WriteAllText(Path.Combine(DataDirectory, "profile-test.json"), JsonSerializer.Serialize(new { stages, state.Error, state.Card, item }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if(args.FirstOrDefault() == "--verify-test")
            {
                // Offline tests of the account client, the title verification and the website pairing (fake clock and transport).
                var (ok, results) = VerificationTests.Run().GetAwaiter().GetResult();
                string report = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(DataDirectory, "verify-test.json");
                File.WriteAllText(report, JsonSerializer.Serialize(new { ok, version = AppVersion.Current, ranAt = DateTime.Now, results }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                return ok ? 0 : 1;
            }
            if(args.FirstOrDefault() == "--toast-test")
            {
                // Diagnostic: shows two event notifications for 6 seconds, as an event alert would.
                var testApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var toastWindow = new EventToast(new AppSettings());
                testApp.Startup += (_, _) =>
                {
                    toastWindow.Notify("Invasión", "#71DED6", DateTimeOffset.Now.AddMinutes(5), false);
                    toastWindow.Notify("Grieta espacio-temporal", "#E9C77E", DateTimeOffset.Now, true);
                    var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                    stop.Tick += (_, _) => testApp.Shutdown(); stop.Start();
                };
                testApp.Run();
                return 0;
            }
            if(args.FirstOrDefault() == "--hit-flags")
            {
                // Diagnostic: every damage frame (0x0438) of a recording with actor, flags, type, the block between type and the
                // hit-count marker, count and amount, plus the own actor id, to work out back / front / perfect / double.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var rows = new List<string>(); DateTime at = DateTime.MinValue;
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    static bool V(ReadOnlySpan<byte> f, ref int p, out long v) { v = 0; for (int sh = 0; p < f.Length && sh <= 56; sh += 7) { byte b = f[p++]; v |= (long)(b & 127) << sh; if ((b & 128) == 0) return true; } return false; }
                    engine.Tap = (raw, t) => Frames.Walk(raw, f =>
                    {
                        if (f.Length < 16 || System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f) != 0x0438) return;
                        int p = 2; if (!V(f, ref p, out long target) || p + 2 > f.Length) return;
                        byte f1 = f[p], f2 = f[p + 1]; p += 2;
                        if ((f1 & 0x04) == 0 || !V(f, ref p, out long actor) || p + 6 > f.Length) return;
                        int skill = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(f[p..]); byte seq = f[p + 4], type = f[p + 5];
                        int marker = -1; for (int i = p + 6; i + 4 <= f.Length; i++) if (f[i] is >= 1 and <= 9 && f[i + 1] == 0 && f[i + 2] == 0 && f[i + 3] == 0) { marker = i; break; }
                        if (marker < 0) return; int q = marker + 6; if (q >= f.Length || !V(f, ref q, out long amount)) return;
                        string block = Convert.ToHexString(f.Slice(p + 6, marker - (p + 6)));
                        string tail = Convert.ToHexString(f.Slice(q, Math.Min(12, f.Length - q)));
                        string mid = Convert.ToHexString(f.Slice(marker + 4, 2));
                        rows.Add($"{t:HH:mm:ss.fff}	{actor}	{target}	{skill}	{f1:X2}	{f2:X2}	{type}	{block}	{f[marker]}	{mid}	{amount}	{tail}");
                    });
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                    }
                    rows.Insert(0, $"#own={engine.CapturedIdentity?.Id}	time	actor	target	skill	f1	f2	type	block	count	mid	amount	tail");
                    File.WriteAllLines(Path.GetFullPath(args[2]), rows);
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--all-frames")
            {
                // Diagnostic: every frame of a recording (inside bundles too) as "time<TAB>opcode<TAB>hex", plus the deaths as "#death".
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    using var output = new StreamWriter(Path.GetFullPath(args[2]));
                    DateTime at = DateTime.MinValue;
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    engine.Tap = (raw, t) => Frames.Walk(raw, f => { if (f.Length >= 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f) != 0xFFFF) output.WriteLine($"{t:HH:mm:ss.fff}	{System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f):X4}	{Convert.ToHexString(f)}"); });
                    engine.Death += (id, npc, when) => output.WriteLine($"{when:HH:mm:ss.fff}	#death	{id}	{npc}");
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                    }
                    output.WriteLine($"#own	{engine.CapturedIdentity?.Id}");
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--kill-correlate")
            {
                // Diagnostic: which opcodes arrive within 1.5 s after each creature death (XP, kinah, loot candidates), with a sample.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var frames = new List<(DateTime At, int Op, byte[] Data)>(); var deaths = new List<DateTime>(); DateTime at = DateTime.MinValue;
                    using (var engine = new CaptureEngine { ReplayClock = () => at })
                    {
                        engine.Tap = (raw, t) => Frames.Walk(raw, f => { if (f.Length >= 2) { int op = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f); if (op != 0xFFFF) frames.Add((t, op, f.ToArray())); } });
                        engine.Death += (_, _, when) => deaths.Add(when);
                        foreach (var line in File.ReadLines(args[1]))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        }
                    }
                    var lines = new List<string> { $"deaths={deaths.Count} frames={frames.Count}" };
                    foreach (var g in frames.GroupBy(f => f.Op))
                    {
                        int total = g.Count();
                        int killsWith = deaths.Count(d => g.Any(f => f.At >= d.AddMilliseconds(-300) && f.At <= d.AddMilliseconds(1500)));
                        if (deaths.Count == 0 || killsWith < Math.Max(2, deaths.Count / 2)) continue;
                        var sample = g.First(f => deaths.Any(d => f.At >= d.AddMilliseconds(-300) && f.At <= d.AddMilliseconds(1500)));
                        lines.Add($"0x{g.Key:X4}	total={total}	killsWith={killsWith}/{deaths.Count}	len={sample.Data.Length}	{Convert.ToHexString(sample.Data.AsSpan(0, Math.Min(64, sample.Data.Length)))}");
                    }
                    File.WriteAllLines(Path.GetFullPath(args[2]), lines);
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--loot-test")
            {
                // Test: replays a recording and lists the kinah and the items the inventory watch saw gained.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    DateTime at = DateTime.MinValue; var inv = new InventoryWatch(); inv.ResetSession();
                    typeof(InventoryWatch).GetField("sessionStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(inv, DateTime.MinValue);
                    using (var engine = new CaptureEngine { ReplayClock = () => at })
                    {
                        engine.Tap = (raw, t) => inv.Add(raw, t);
                        foreach (var line in File.ReadLines(args[1]))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        }
                    }
                    var k = inv.KinahSession;
                    var lines = new List<string> { $"kinah now={k.Now} gained={k.Gained}" };
                    lines.AddRange(inv.Loot.Select(l => $"{l.At:HH:mm:ss} {l.Name} x{l.Quantity} value={l.Value}"));
                    File.WriteAllLines(Path.GetFullPath(args[2]), lines);
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--news-test")
            {
                // Test: renders the news window with an update whose notes come one line per version, to a PNG.
                string notes = "1.0.11: Ventana de Novedades más clara: «Actualizar ahora» arriba y las notas separadas por versión, en puntos.\n1.0.10: Novedades: botón con megáfono en la barra del overlay. «Actualizar ahora»: instala en silencio.\n1.0.9: Salas: tu «Listo» se ve bien. Los paquetes grandes ya no se pierden.\nFullAIon2Companion 1.0.11: medidor de combate y catálogo local.";
                var w = new NewsWindow();
                w.Show([new NewsItem("update:1.0.11", "update", "Versión 1.0.11", notes, "info", "Tienes la 1.0.10 · 97.8 MB · publicada el 13/10/2026"), new NewsItem("notice:1", "notice", "Mantenimiento el jueves", "El servidor estará en mantenimiento de 9:00 a 11:00.", "warning")], "1.0.10");
                w.UpdateLayout();
                if (L.Language != "es") { L.Apply(w); w.UpdateLayout(); }
                var content = (System.Windows.FrameworkElement)w.Content;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(content);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.GetFullPath(args[1]))) png.Save(file);
                w.Close();
                return 0;
            }
            if(args.FirstOrDefault() == "--soul-test")
            {
                // Test: replays a recording and compares the souls the app counts with the game's progress (0x0D90) and the
                // soul items (0x1B56): each soul once, whichever message came.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    DateTime at = DateTime.MinValue; int progress = 0, items = 0;
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    typeof(InventoryWatch).GetField("sessionStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(engine.Inventory, DateTime.MinValue);
                    engine.Inventory.Gained += (item, qty, _) => { if (AionDPS.Aion2.Protocol.Aion2Pets.PetOfSoulItem(item) is not null) items += (int)qty; };
                    engine.Tap = (raw, t) => Frames.Walk(raw, f => { if (f.Length > 3 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f) == 0x0D90) for (int i = 0, p = 3; i < f[2] && p + 8 <= f.Length; i++, p += 8) progress += System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(f[(p + 4)..]); });
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                    }
                    var snap = engine.Snapshot();
                    File.WriteAllLines(Path.GetFullPath(args[2]), [$"progress messages: {progress} souls", $"soul items: {items}", $"counted by the app: {snap.Pet?.SessionSouls}"]);
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--atlas-test")
            {
                // Test: replays a recording and writes, once per second of play, the «Tú estás aquí» message the Atlas page of
                // the panel would receive (AtlasLocator), with the time.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    DateTime at = DateTime.MinValue, next = DateTime.MinValue;
                    var locator = new AtlasLocator(); var messages = new List<object>();
                    using var engine = new CaptureEngine { ReplayClock = () => at };
                    foreach (var line in File.ReadLines(args[1]))
                    {
                        var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                        engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        if (at < next) continue;
                        next = at.AddSeconds(1);
                        messages.Add(new { at = at.ToString("HH:mm:ss"), message = locator.Locate(engine.Snapshot()) });
                    }
                    File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--lootbook-test")
            {
                // Test: replays recordings (args[2..]) and writes what each creature gave (LootBook) to args[1], with how many
                // of the items gained were put down to a creature.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var report = new List<object>();
                    foreach (var file in args.Skip(2))
                    {
                        DateTime at = DateTime.MinValue; int gained = 0;
                        using var engine = new CaptureEngine { ReplayClock = () => at };
                        typeof(InventoryWatch).GetField("sessionStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(engine.Inventory, DateTime.MinValue);
                        engine.Inventory.Gained += (_, _, _) => gained++;
                        foreach (var line in File.ReadLines(file))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        }
                        var book = engine.Observed;
                        report.Add(new { recording = Path.GetFileName(file), gained, learned = book.Learned, creatures = book.Creatures().Select(npc => book.Of(npc) is var (kills, seen)
                            ? new { npc, name = Catalog.NpcName(npc), kills, items = seen.Select(x => new { x.Item, name = InventoryWatch.Info(x.Item).Name, x.Times, x.Quantity }).ToList() } : null).ToList() });
                    }
                    File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--market-test")
            {
                // Test: replays recordings (args[2..]) and writes the Mercado tab (MarketPrices.View) and the loot HUD of each to
                // args[1], with a stand-in source (every item at 1,000 + its id's last three digits; one in seven not on sale
                // now, at its last price) and one price of the player's: theirs wins, it stays on its server, a bound item is
                // not counted, another server's prices do not mix in, and the source is asked once.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                int failures = 0;
                try
                {
                    var report = new List<object>();
                    foreach (var file in args.Skip(2))
                    {
                        DateTime at = DateTime.MinValue;
                        using var engine = new CaptureEngine { ReplayClock = () => at };
                        typeof(InventoryWatch).GetField("sessionStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(engine.Inventory, DateTime.MinValue);
                        foreach (var line in File.ReadLines(file))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        }
                        const int server = 2401, other = 2402;
                        var held = engine.Inventory.Holdings(i => i < 900_000_000 && !InventoryWatch.Bound(i));
                        var source = new TestMarketSource(held.Keys);
                        var prices = new MarketPrices(); prices.Open(Path.Combine(testDirectory, Path.GetFileNameWithoutExtension(file) + "-prices.json"));
                        MarketPrices.Source = null; prices.Want(server);
                        dynamic before = prices.View(engine.Inventory, server, "Prueba");
                        MarketPrices.Source = source;
                        prices.Want(server);
                        for (int wait = 0; wait < 50 && (prices.Board(server) is null || prices.Servers.Count == 0); wait++) Thread.Sleep(100);
                        Thread.Sleep(200);
                        prices.Want(server); // a second look within 30 minutes does not ask again
                        int calls = source.Calls;
                        var checks = new List<(string Name, bool Ok)>
                        {
                            ("llega la lista de servidores", prices.Servers.Count == 2),
                            ("llega el mercado del servidor", prices.Board(server)?.Rows.Count == held.Count),
                            ("la fuente se consulta una vez", calls == 1),
                        };
                        if (held.Count > 0)
                        {
                            int mine = held.Keys.Max();
                            checks.Add(("otro servidor no se mezcla", prices.Market(other, mine) is null));
                            if (held.Keys.FirstOrDefault(i => i % 7 == 0) is int idle and > 0)
                                checks.Add(("sin venta ahora vale el último precio visto", prices.Market(server, idle)?.Price == TestMarketSource.PriceOf(idle) && prices.Row(server, idle)?.Price is null));
                            prices.SetManual(server, mine, 123_456);
                            checks.Add(("tu precio gana al de la fuente", prices.Unit(server, mine) == 123_456 && prices.Market(server, mine)?.Price == TestMarketSource.PriceOf(mine)));
                            checks.Add(("tu precio se queda en su servidor", prices.Manual(other, mine) is null));
                            var reopened = new MarketPrices(); reopened.Open(Path.Combine(testDirectory, Path.GetFileNameWithoutExtension(file) + "-prices.json"));
                            checks.Add(("tu precio se guarda en el archivo", reopened.Unit(server, mine) == 123_456));
                            prices.SetManual(server, mine, 0);
                            checks.Add(("borrar tu precio vuelve al de la fuente", prices.Unit(server, mine) == TestMarketSource.PriceOf(mine)));
                            checks.Add(("la lista completa lleva cada objeto", ((IEnumerable<dynamic>)((dynamic)prices.BoardView(server)).rows).Count() == held.Count));
                        }
                        dynamic after = prices.View(engine.Inventory, server, "Prueba");
                        // The first whole inventory, as the panel gets it (for checking the Mercado tab in a browser).
                        string sample = Path.ChangeExtension(Path.GetFullPath(args[1]), ".view.json");
                        if (engine.Inventory.Complete && !File.Exists(sample)) File.WriteAllText(sample, UiContract.Serialize((object)after));
                        long expected = held.Sum(h => TestMarketSource.PriceOf(h.Key) * h.Value);
                        if (held.Count > 0) checks.Add(("valor = suma de los no vinculados", (long)after.value == expected));
                        checks.Add(("sin fuente no hay precios", (int)before.priced == 0));
                        failures += checks.Count(c => !c.Ok);
                        report.Add(new
                        {
                            recording = Path.GetFileName(file), complete = engine.Inventory.Complete, stacks = held.Count,
                            bound = engine.Inventory.Holdings(i => i < 900_000_000 && InventoryWatch.Bound(i)).Count,
                            kinah = engine.Inventory.KinahSession.Now, value = (long)after.value, npcValue = (long)after.npcValue,
                            top = ((IEnumerable<dynamic>)after.items).Take(8).Select(r => $"{r.name} ×{r.qty} · {r.unit} c/u · {r.total}").ToList(),
                            hud = engine.Inventory.Recent(3).Select(e => $"{e.Name} ×{e.Quantity} · {(prices.Unit(server, e.Item) is long u ? (u * e.Quantity).ToString() : "sin precio")}").ToList(),
                            checks = checks.Select(c => (c.Ok ? "ok " : "FALLA ") + c.Name).ToList(),
                        });
                    }
                    File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { MarketPrices.Source = null; Directory.Delete(testDirectory, true); testDirectory = null; }
                return failures == 0 ? 0 : 1;
            }
            if(args.FirstOrDefault() == "--market-live-test")
            {
                // Test against aion2.exchange itself, the way the app asks (MarketPrices.Want, two requests): its servers and
                // the whole Market of args[2] (2401 by default), written to args[1] with how many items the app's catalog
                // names, and the «Todo el Mercado» list as the panel gets it to <args[1]>.board.json.
                int server = args.Length > 2 && int.TryParse(args[2], out int chosen) ? chosen : 2401;
                string temp = Path.Combine(Path.GetTempPath(), "fa2c-market-" + Guid.NewGuid().ToString("N") + ".json");
                var live = new MarketPrices(); live.Open(temp);
                MarketPrices.Source = new Aion2Exchange();
                live.Want(server);
                for (int wait = 0; wait < 600 && (live.Board(server) is null || live.Servers.Count == 0) && live.Status.Length == 0; wait++) Thread.Sleep(100);
                try { File.Delete(temp); } catch { }
                if (live.Board(server) is not { } board) throw new InvalidOperationException("Sin mercado: " + live.Status);
                var servers = live.Servers;
                var named = board.Rows.Count(r => InventoryWatch.Known(r.Item));
                File.WriteAllText(Path.ChangeExtension(Path.GetFullPath(args[1]), ".board.json"), UiContract.Serialize(live.BoardView(server)));
                File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(new
                {
                    ranAt = DateTime.Now, server, servers = servers.Count, regions = servers.GroupBy(x => x.Region).Select(g => $"{g.Key} {g.Count()}"),
                    home = servers.FirstOrDefault(x => x.Id == server), items = board.Rows.Count, onSale = board.Rows.Count(r => r.Price is not null),
                    named, snapshot = board.Rows.Count > 0 ? board.Rows.Max(r => r.At) : (DateTime?)null,
                    sections = board.Rows.GroupBy(r => r.Section).Select(g => $"{g.Key} {g.Count()}"),
                    sample = board.Rows.Where(r => r.Price is not null).OrderByDescending(r => r.Listings).Take(8)
                        .Select(r => $"{(InventoryWatch.Known(r.Item) ? InventoryWatch.Info(r.Item).Name : r.Name)} · {r.Price:N0} · {r.Listings} en venta · {r.Change:0.#} %"),
                }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                return servers.Count > 0 && board.Rows.Count > 0 ? 0 : 1;
            }
            if(args.FirstOrDefault() == "--marks-test")
            {
                // Test: the pins and routes of the Mapa de Atreia (AtlasMarks) as the panel sends them, with bad entries mixed
                // in, written to args[1]: what is kept, read back from the file, and a map that is not the app's refused.
                string file = Path.Combine(Path.GetTempPath(), "fa2c-marks-" + Guid.NewGuid().ToString("N") + ".json");
                var checks = new List<(string Name, bool Ok)>();
                try
                {
                    var book = new AtlasMarks(); book.Open(file);
                    using var sent = JsonDocument.Parse("""
                        {"pins":[{"x":2100,"y":2300,"name":"Cubo escondido"},{"x":"no","y":1},{"x":99999,"y":10},{"x":10,"y":20,"name":"   "}],
                         "routes":[{"name":"Ruta 1","points":[[2000,2400],[2100,2450],[2200,2420]]},{"name":"corta","points":[[1,1]]},{"points":[[5,5],[6,6],["x",1]]}]}
                        """);
                    var (pins, routes) = book.Set("2", sent.RootElement.GetProperty("pins"), sent.RootElement.GetProperty("routes"));
                    checks.Add(("se guardan los pines válidos", pins == 2));
                    checks.Add(("se guardan las rutas de 2 puntos o más", routes == 2));
                    var again = new AtlasMarks(); again.Open(file);
                    var map = again.Of("2");
                    checks.Add(("se leen del archivo", map is { Pins.Count: 2, Routes.Count: 2 } && map.Pins[0].Name == "Cubo escondido" && map.Routes[0].Points.Length == 3));
                    checks.Add(("un pin sin nombre recibe uno", map?.Pins[1].Name == "Pin 2"));
                    checks.Add(("una ruta sin nombre recibe uno", map?.Routes[1].Name == "Ruta 2" && map.Routes[1].Points.Length == 2));
                    bool refused; try { book.Set("99", sent.RootElement.GetProperty("pins"), sent.RootElement.GetProperty("routes")); refused = false; } catch (InvalidOperationException) { refused = true; }
                    checks.Add(("un mapa que no es de la app se rechaza", refused));
                    using var none = JsonDocument.Parse("""{"pins":[],"routes":[]}""");
                    book.Set("2", none.RootElement.GetProperty("pins"), none.RootElement.GetProperty("routes"));
                    checks.Add(("borrar todo deja el mapa vacío", book.Of("2") is null));
                }
                finally { try { File.Delete(file); } catch { } }
                File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(checks.Select(c => (c.Ok ? "ok " : "FALLA ") + c.Name), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                return checks.All(c => c.Ok) ? 0 : 1;
            }
            if(args.FirstOrDefault() == "--rank-badge-test")
            {
                // Test: the overlay header with the Abyss medal (the 0x578D frames of a rare-frames.log, args[2], the data folder's by
                // default), drawn to args[1], and its tooltip in Spanish and English next to it (.txt).
                string log = args.Length > 2 ? args[2] : Path.Combine(DataDirectory, "rare-frames.log");
                string temp = Path.Combine(Path.GetTempPath(), "fa2c-rankings-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var book = new RankingWatch(); book.Open(temp); book.ImportFrom(log);
                    var overlay = new OverlayWindow(new AppSettings()) { AbyssRank = () => book.Abyss(2401, "Darkni") };
                    var tips = new List<string>();
                    foreach (string language in new[] { "en", "es" })
                    {
                        L.Language = language;
                        var (_, tip) = overlay.RankBadgeForTest();
                        tips.Add($"[{language}]\n{tip}");
                    }
                    var (header, _) = overlay.RankBadgeForTest();
                    ((System.Windows.Controls.Panel)header.Parent).Children.Remove(header);
                    var host = new System.Windows.Controls.Border { Width = 344, Padding = new Thickness(12, 8, 12, 4), Child = header,
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 20, 24)) };
                    host.Measure(new System.Windows.Size(344, double.PositiveInfinity)); host.Arrange(new System.Windows.Rect(host.DesiredSize)); host.UpdateLayout();
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth * 2, (int)host.ActualHeight * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                    bmp.Render(host);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var f = File.Create(Path.GetFullPath(args[1]))) png.Save(f);
                    File.WriteAllText(Path.ChangeExtension(Path.GetFullPath(args[1]), ".txt"), string.Join("\n\n", tips));
                    overlay.Close();
                }
                finally { try { File.Delete(temp); } catch { } }
                return 0;
            }
            if(args.FirstOrDefault() == "--tour-shots")
            {
                // Pictures for the website's tour (TourShots): args[1] a recording, args[2] the folder, args[3] en or pt (the GitHub
                // README's pictures). A temporary data folder.
                if (args.Length > 3 && args[3] is "en" or "pt") L.Language = args[3];
                string real = DataDirectory;
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-tour-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try { return TourShots.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), real); }
                finally { try { Directory.Delete(testDirectory, true); } catch { } testDirectory = null; }
            }
            if(args.FirstOrDefault() == "--skill-card-test")
            {
                // Test: the combat assistant's hover card (SkillCardWindow) for a Gladiator's buff, a cooldown running, one ready and
                // a buff whose level is unknown, in four styles (args[1]/skill-card-<style>.png, texts in skill-card.txt); and the
                // icon under the mouse (AssistantWindow.TileAt) on a bar out of sight: the centre of each icon finds it, a point
                // between two finds none. args[2]: es (default), en or pt.
                string outDir = Path.GetFullPath(args[1]); Directory.CreateDirectory(outDir);
                if (args.Length > 2 && args[2] is "en" or "pt") L.Language = args[2];
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-skillcard-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                var lines = new List<string>(); bool ok = true;
                try
                {
                    TimerTile Tile(int id, double left, double total, bool buff) { var s = CombatAssistant.Skill(id); return new(id, s?.Name ?? id.ToString(), s?.Icon, left, total, buff); }
                    var cases = new (TimerTile Tile, int? Level)[] { (Tile(11250000, 12.3, 20, true), 20), (Tile(11100000, 18, 45, false), 15), (Tile(11400000, 0, 60, false), 20), (Tile(11130000, 6.4, 20, true), null) };
                    static IEnumerable<string> Texts(System.Windows.DependencyObject node)
                    {
                        if (node is System.Windows.Controls.TextBlock tb) { yield return new System.Windows.Documents.TextRange(tb.ContentStart, tb.ContentEnd).Text; yield break; }
                        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<System.Windows.DependencyObject>()) foreach (var t in Texts(child)) yield return t;
                    }
                    foreach (string style in new[] { "clasico", "gamergirl", "cyberpunk", "terminal" })
                    {
                        var overlay = new OverlayWindow(new AppSettings { OverlayVisible = false, OverlayStyle = style }) { Preview = true };
                        var row = new System.Windows.Controls.WrapPanel { Width = 700 };
                        foreach (var (tile, level) in cases)
                        {
                            var card = new SkillCardWindow(); card.Load(tile, level, 1); overlay.ThemeWindow(card);
                            var content = (System.Windows.FrameworkElement)card.Content; card.Content = null; card.Close();
                            content.VerticalAlignment = System.Windows.VerticalAlignment.Top; row.Children.Add(content); L.Apply(content);
                            if (style == "clasico") { lines.Add($"— {tile.Name} (nivel {level?.ToString() ?? "?"})"); lines.AddRange(Texts(content).Select(t => "  " + t)); }
                        }
                        var stage = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(10), Child = row,
                            Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(58, 63, 67), System.Windows.Media.Color.FromRgb(22, 25, 27), 60) };
                        stage.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity)); stage.Arrange(new System.Windows.Rect(stage.DesiredSize)); stage.UpdateLayout();
                        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(stage.ActualWidth * 2), (int)Math.Ceiling(stage.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(stage);
                        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                        using (var f = File.Create(Path.Combine(outDir, $"skill-card-{style}.png"))) png.Save(f);
                        overlay.Close();
                    }
                    // The icon under the mouse, on a bar shown out of sight (and transparent).
                    var bar = new AssistantWindow(new AppSettings { AssistantVisible = true, AssistantLeft = -6000, AssistantTop = -6000, AssistantScale = 1.25 }) { Opacity = 0 };
                    bar.Update([cases[0].Tile, cases[3].Tile], [cases[1].Tile, cases[2].Tile], true);
                    bar.UpdateLayout();
                    var tiles = bar.TilesForTest;
                    var expected = new[] { cases[0].Tile.Skill, cases[3].Tile.Skill, cases[1].Tile.Skill, cases[2].Tile.Skill };
                    for (int i = 0; i < tiles.Count; i++)
                    {
                        var c = tiles[i].PointToScreen(new System.Windows.Point(tiles[i].ActualWidth / 2, tiles[i].ActualHeight / 2));
                        int? found = bar.TileAt((int)c.X, (int)c.Y)?.Data.Skill;
                        bool hit = i < expected.Length && found == expected[i]; ok &= hit;
                        lines.Add($"{(hit ? "ok" : "FALLA")} el centro del icono {i + 1} da {found?.ToString() ?? "nada"}");
                    }
                    var right = tiles[0].PointToScreen(new System.Windows.Point(tiles[0].ActualWidth + 1, tiles[0].ActualHeight / 2));
                    bool none = bar.TileAt((int)right.X + 1, (int)right.Y) is null; ok &= none && tiles.Count == 4;
                    lines.Add($"{(none ? "ok" : "FALLA")} entre dos iconos no hay ninguno");
                    lines.Add($"{(tiles.Count == 4 ? "ok" : "FALLA")} la barra tiene {tiles.Count} iconos");
                    bar.Close();
                    File.WriteAllLines(Path.Combine(outDir, "skill-card.txt"), lines);
                    Console.WriteLine(string.Join(Environment.NewLine, lines.Where(l => l.StartsWith("ok") || l.StartsWith("FALLA"))));
                }
                finally { try { Directory.Delete(testDirectory, true); } catch { } testDirectory = null; }
                return ok ? 0 : 1;
            }
            if(args.FirstOrDefault() == "--overlay-styles")
            {
                // Test and previews: the real overlay in every style (OverlayWindow.Styles), with the numbers of the user's capture of
                // 2026-10-19 (Darkni and San in group 3, two pets followed, a hidden cube, the next field boss, rift and Shugo), out of a
                // fight and fighting Nutah; one PNG each in args[1] (<key>-calma.png, <key>-combate.png). The data folder is a temporary
                // one with a copy of the profile cache, so nothing is saved over the real one and the class icons need no download.
                string outDir = Path.GetFullPath(args[1]); Directory.CreateDirectory(outDir);
                // args[2]: es (default), en or pt - the texts as the overlay shows them in that language.
                if (args.Length > 2 && args[2] is "en" or "pt") L.Language = args[2];
                string realCache = Path.Combine(DataDirectory, "player-cache.json");
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-styles-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    if (File.Exists(realCache)) File.Copy(realCache, Path.Combine(testDirectory, "player-cache.json"));
                    var now = DateTime.Now;
                    var detail = JsonDocument.Parse("{\"kill_count\":194,\"death_count\":269,\"assist_count\":381}").RootElement.Clone();
                    var classRank = new RankingWatch.Entry(1, 2, 30, 1, 261_725, 941, now.AddMinutes(30), now.AddMinutes(-5), detail);
                    var allRank = new RankingWatch.Entry(1, 0, 309, -5, 261_725, 7_092, now.AddMinutes(30), now.AddMinutes(-5), detail);
                    var timers = new List<(string, string, string, DateTime)> { ("♛", "Danar fundido", "#C9B98F", now.AddSeconds(28 * 60 + 55)), ("◈", "Grieta", "#8b5cf6", now.AddSeconds(2 * 3600 + 6 * 60 + 47)), ("✦", "Shugo", "#3b82f6", now.AddSeconds(6 * 60 + 47)) };
                    var cube = new CubeView(new CubeSighting(now.AddMinutes(-2), "", 0, 0, 0, 1, 1301199, LeftRangeAt: now.AddSeconds(-30)), 2971, 20, null, 0, null, null, null, []);
                    var pins = new List<PetStatus> { new(1030, "Guardián guerrero Nagarant", 1, 22, 25, 3, 0, 0, null, null, "Cognis", []), new(1005, "Klaw", 2, 13, 75, 62, 0, 0, null, null, "Cognis", []) };
                    var troop = new TroopInfo(true, 3, [new TroopMember(1, "Darkni", 2401, 3, 1, 5, 45, 0, null, 2713, 139_800), new TroopMember(2, "Lyra", 2401, 3, 2, 33, 45, 0, null, 1933, 95_700)]);
                    var players = new List<ActorRow> { new(325, "Darkni", 1_700_000, 21_808, 29_600), new(77, "Lyra", 1_180_000, 15_100, 0) };
                    var festival = new TimerRow("shugo", "Festival de Shugo", "#E9C77E", DateTimeOffset.Now.AddSeconds(407), 407, false, null, null);
                    CaptureSnapshot Snapshot(TargetInfo? target) => new("Captura activa · Ethernet", 1000, 900, 500, 0, "", 325, "Darkni", 1_700_000, 29_600, 21_808, 75, 7, 0, target, [], players,
                        0, 0, 0, false, LocalServerId: 2401, PingMs: 57, Pet: new PetHunt(null, [], null, null, null, pins, 0), Od: new OdEnergy.State(30, 25, now), Troop: troop, World: 20);
                    var nutah = new TargetInfo(9001, 2400353, "Nutah supervisor jefe", null, 113_200_000, 160_000_000, true, 2_400_000, 47, 17);
                    var written = new List<string>();
                    foreach (var style in OverlayWindow.Styles)
                        foreach (bool calm in new[] { true, false })
                        {
                            var settings = new AppSettings { OverlayVisible = false, OverlayStyle = style.Key, CubeAlerts = true, BossPacePlace = "overlay" };
                            var overlay = new OverlayWindow(settings) { Preview = true, AbyssRank = () => (classRank, allRank) };
                            overlay.SetHeaderTimers(timers);
                            var snapshot = Snapshot(calm ? null : nutah);
                            overlay.Update(snapshot, festival, players);
                            if (calm) { overlay.PreviewCalm(); overlay.Update(snapshot, festival, players); }
                            overlay.UpdateCube(cube);
                            overlay.Update(snapshot, festival, players);
                            var sides = overlay.PreviewSides(calm ? null : nutah);
                            var content = (System.Windows.FrameworkElement)overlay.Content; overlay.Content = null;
                            L.Apply(content); L.Apply(sides.Dock); L.Apply(sides.Toast); if (sides.Drops is { } dropsCard) L.Apply(dropsCard); if (sides.Boss is { } bossCard) L.Apply(bossCard);
                            var host = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(14), Child = content,
                                Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(58, 63, 67), System.Windows.Media.Color.FromRgb(22, 25, 27), 60) };
                            double width = overlay.Width + 28;
                            host.Measure(new System.Windows.Size(width, double.PositiveInfinity)); host.Arrange(new System.Windows.Rect(new System.Windows.Size(width, host.DesiredSize.Height))); host.UpdateLayout();
                            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(host.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(host);
                            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                            string file = Path.Combine(outDir, $"{style.Key}-{(calm ? "calma" : "combate")}.png");
                            using (var f = File.Create(file)) png.Save(f);
                            written.Add(Path.GetFileName(file));
                            if (!calm)
                            {
                                // The set as it looks over the game: the boss card on top, then the overlay, its strip and the loot card.
                                host.Child = null;
                                var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                                row.Children.Add(content);
                                sides.Dock.Margin = new System.Windows.Thickness(4, 0, 4, 0); sides.Dock.VerticalAlignment = System.Windows.VerticalAlignment.Top; row.Children.Add(sides.Dock);
                                if (sides.Drops is { } drop) { drop.Width = 300; drop.VerticalAlignment = System.Windows.VerticalAlignment.Top; row.Children.Add(drop); }
                                var set = new System.Windows.Controls.StackPanel();
                                sides.Toast.Width = 330; sides.Toast.HorizontalAlignment = System.Windows.HorizontalAlignment.Left; set.Children.Add(sides.Toast);
                                if (sides.Boss is { } boss) { boss.Width = 360; boss.HorizontalAlignment = System.Windows.HorizontalAlignment.Right; boss.Margin = new System.Windows.Thickness(0, 0, 0, 10); set.Children.Add(boss); }
                                set.Children.Add(row);
                                var stage = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(14), Child = set,
                                    Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(58, 63, 67), System.Windows.Media.Color.FromRgb(22, 25, 27), 60) };
                                stage.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity)); stage.Arrange(new System.Windows.Rect(stage.DesiredSize)); stage.UpdateLayout();
                                var whole = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(stage.ActualWidth * 2), (int)Math.Ceiling(stage.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32); whole.Render(stage);
                                var wholePng = new System.Windows.Media.Imaging.PngBitmapEncoder(); wholePng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(whole));
                                using (var f = File.Create(Path.Combine(outDir, $"{style.Key}-conjunto.png"))) wholePng.Save(f);
                            }
                            overlay.Close();
                        }
                    File.WriteAllLines(Path.Combine(outDir, "styles.txt"), OverlayWindow.Styles.Select(st => $"{st.Key}\t{st.Name}\t{st.Layout}\t{st.Description}"));
                    Console.WriteLine($"{written.Count} imágenes en {outDir}");
                }
                finally { try { Directory.Delete(testDirectory, true); } catch { } testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--ranking-test")
            {
                // Test: the 0x578D frames of a rare-frames.log (args[2], the data folder's by default) through RankingWatch,
                // with the places the game showed on 2026-10-18 at 19:48 and 20:01 (Darkni: Abyss 300 ▲29 of 7,032 in all
                // classes, 31 ▲1 of 939 Gladiators, 258,526 points, 187/265/374); written to args[1].
                string log = args.Length > 2 ? args[2] : Path.Combine(DataDirectory, "rare-frames.log");
                string temp = Path.Combine(Path.GetTempPath(), "fa2c-rankings-" + Guid.NewGuid().ToString("N") + ".json");
                var checks = new List<(string Name, bool Ok)>();
                try
                {
                    var book = new RankingWatch(); book.Open(temp);
                    int frames = 0, read = 0;
                    foreach (var line in File.ReadLines(log))
                    {
                        var parts = line.Split(' ', 4);
                        if (parts.Length < 4 || parts[2] != "0x578D") continue;
                        // The frames up to the screenshots: later ones (the window opened again) have other places.
                        if (parts[0] == "2026-10-18" && string.CompareOrdinal(parts[1], "20:02") > 0 || string.CompareOrdinal(parts[0], "2026-10-18") > 0) break;
                        frames++;
                        var bytes = Convert.FromHexString(parts[3]);
                        if (book.Read(bytes) is not null) read++;
                        book.Add(bytes, DateTime.Parse(parts[0] + " " + parts[1]));
                    }
                    var (mine, all) = book.Abyss(2401, "Darkni");
                    checks.Add(("se leen todas las tramas", frames > 0 && read == frames));
                    checks.Add(("Abismo general: 300 ▲29 de 7.032", all is { Rank: 300, Change: 29, Total: 7032, Points: 258526 }));
                    checks.Add(("Abismo de la clase: 31 ▲1 de 939", mine is { Rank: 31, Change: 1, Total: 939 }));
                    checks.Add(("el detalle trae 187/265/374", mine?.Detail.GetProperty("kill_count").GetInt32() == 187 && mine.Detail.GetProperty("death_count").GetInt32() == 265 && mine.Detail.GetProperty("assist_count").GetInt32() == 374));
                    dynamic view = book.View(2401, "Darkni");
                    var types = ((IEnumerable<dynamic>)view.rankings).Select(r => (int)r.type).ToList();
                    checks.Add(("seis clasificaciones en orden", string.Join(",", types) == "1,3,4,21,5,6"));
                    var abyss = ((IEnumerable<dynamic>)view.rankings).First();
                    int rows = ((IEnumerable<dynamic>)abyss.history).Count();
                    checks.Add(("historial: una fila por foto horaria y lista", rows >= 4 && rows <= frames));
                    var again = new RankingWatch(); again.Open(temp);
                    checks.Add(("se guarda y se vuelve a leer", again.Abyss(2401, "Darkni").All?.Rank == 300));
                    checks.Add(("una trama rota se ignora", book.Read(new byte[] { 0x57, 0x8D, 0, 0, 1, 0, 1, 0, 0, 0 }) is null));
                    File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(new { frames, read, checks = checks.Select(c => (c.Ok ? "ok " : "FALLA ") + c.Name), view = (object)view },
                        new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { try { File.Delete(temp); } catch { } }
                return checks.All(c => c.Ok) ? 0 : 1;
            }
            if(args.FirstOrDefault() == "--fieldboss-test")
            {
                // Test: a made-up field boss (Altgard's first, 2400017) appears, dies, and the board shows it.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var lines = new List<string>();
                    var board = new FieldBossBoard(new AppSettings());
                    using var engine = new CaptureEngine();
                    engine.FieldBossSeen = (npc, alive, at) => { lines.Add($"seen {npc} alive={alive} {at:HH:mm:ss}"); board.Observe(npc, alive, at); };
                    var spawn = new byte[] { 0x41, 0x36, 0xB1, 0x4E, 0x04, 0x20, 0x00 }.Concat(BitConverter.GetBytes(2400017)).Concat(new byte[20]).ToArray();
                    engine.WatchFieldBoss(spawn, DateTime.Now.AddMinutes(-10));
                    string Row() => board.View().SelectMany(m => m.Bosses).First(b => b.Npc == 2400017) is var r ? $"{r.Name} phase={r.Phase} at={r.At:HH:mm} source={r.Source} estimated={r.Estimated}" : "";
                    lines.Add("after spawn: " + Row());
                    engine.WatchFieldBoss(new byte[] { 0x42, 0x36, 0xB1, 0x4E, 0x00, 0x03 }, DateTime.Now.AddMinutes(-5));
                    lines.Add("after death: " + Row());
                    lines.Add("next: " + board.Next());
                    File.WriteAllLines(Path.GetFullPath(args[1]), lines);
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--cube-test")
            {
                // Test: replays a recording (cubes found, objects logged on cube spots), then a made-up EnvSpawn of a
                // hidden cube 12 m from the player on Altgard and its removal.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var lines = new List<string>(); DateTime at = DateTime.MinValue;
                    using (var engine = new CaptureEngine { ReplayClock = () => at })
                    {
                        engine.Cubes.Found += c => lines.Add($"FOUND {c.At:HH:mm:ss} env={c.EnvId} map={c.Map} {c.X:0},{c.Y:0},{c.Z:0}");
                        foreach (var line in File.ReadLines(args[1]))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                        }
                        lines.Add($"replay sightings={engine.Cubes.View(null, null, at).History.Count}");
                        lines.Add("live objects: " + string.Join(", ", engine.Cubes.Live.GroupBy(o => o.Kind + " " + o.Name).Select(g => $"{g.Key} x{g.Count()}")));
                        string candidates = Path.Combine(Program.DataDirectory, "cube-candidates.log");
                        if (File.Exists(candidates)) lines.AddRange(File.ReadAllLines(candidates).Select(l => "candidate " + l[..Math.Min(120, l.Length)]));
                    }
                    var watch = new CubeWatch { MapNow = () => "2", Persist = false };
                    watch.Found += c => lines.Add($"FOUND env={c.EnvId} {c.X:0},{c.Y:0},{c.Z:0}");
                    static byte[] Frame(int op, params byte[][] parts) { var l = new List<byte> { (byte)(op >> 8), (byte)op }; foreach (var p in parts) l.AddRange(p); return [.. l]; }
                    byte[] F(float v) => BitConverter.GetBytes(v);
                    var spawn = Frame(0x3436, [0xB1, 0x4E], [4, 0], BitConverter.GetBytes(1200036), F(150000), F(170000), F(21800));
                    watch.Add([.. spawn], DateTime.Now);
                    var v = watch.View((148800, 170500), 21400, DateTime.Now);
                    lines.Add($"view active={v.Active?.EnvId} dist={v.Distance:0.0} m bearing={v.Bearing:0} height={v.Height:0.0} m spotsNear={v.SpotsNear} nearestSpot={v.NearestSpot:0.0} m");
                    watch.Add(Frame(0x3536, [0xB1, 0x4E], [4, 3, 0]), DateTime.Now);
                    v = watch.View((148800, 170500), 21400, DateTime.Now);
                    lines.Add($"after remove active={v.Active?.EnvId.ToString() ?? "none"} gone={v.History[0].GoneAt is not null}");
                    File.WriteAllLines(Path.GetFullPath(args[2]), lines);
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--assistant-test")
            {
                // Test: replays a recording with the own combat id (args[3]) and lists the assistant bar every 5 s of play.
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    DateTime at = DateTime.MinValue, next = DateTime.MinValue; var bar = new CombatAssistant { LocalId = int.Parse(args[3]) };
                    var lines = new List<string>();
                    using (var engine = new CaptureEngine { ReplayClock = () => at })
                    {
                        engine.Tap = (raw, t) => bar.Add(raw, t);
                        foreach (var line in File.ReadLines(args[1]))
                        {
                            var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                            engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                            if (at < next) continue;
                            next = at.AddSeconds(5);
                            var (buffs, cds) = bar.View(at);
                            lines.Add($"{at:HH:mm:ss} buffs: {string.Join(", ", buffs.Select(b => $"{b.Name} {b.Left:0}/{b.Total:0}s"))} | cd: {string.Join(", ", cds.Select(c => $"{c.Name} {c.Left:0}/{c.Total:0.#}s"))}");
                        }
                    }
                    File.WriteAllLines(Path.GetFullPath(args[2]), lines);
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--i18n-test")
            {
                // Test: overlay phrases (fixed and with numbers) through the dictionaries, in English and Portuguese.
                string[] samples = ["EN ESPERA", "Sin objetivo · selecciona una criatura", "SESIÓN DE FARMEO ▾", "Tiempo de sesión", "Muertes / h",
                    "Combate · 0:38 · Nv. 54 · Heroico", "Nv. 2 · 13/25", "Sin obtener · 4/5 almas", "faltan 12", "41 / 75 almas · faltan 34 para Nv. 3",
                    "hoy 6 almas · 48 muertes · ~272 más", "TROPA · GRUPO 2 · 5 miembros", "Furia en", "Derrota en", "Vida en furia", "EN FURIA",
                    "DPS necesario 75.7K · faltan 30.3K (60%)", "1,2K daño", "3 criaturas", "12m 30s · 48 muertes · 6 almas", "Buscando tráfico del juego… entra con tu personaje en AION 2"];
                var lines = new List<string>();
                foreach (var lang in new[] { "en", "pt" }) { L.Language = lang; foreach (var x in samples) lines.Add($"{lang}	{x}	=>	{L.T(x)}"); }
                L.Language = "en"; lines.Add("npc 2300218 en: " + Catalog.NpcName(2300218)); lines.Add("pet 1002 en: " + PetTracker.NameOf(1002));
                L.Language = "pt"; lines.Add("npc 2300218 pt: " + Catalog.NpcName(2300218)); lines.Add("pet 1002 pt: " + PetTracker.NameOf(1002));
                AionDPS.Aion2.Protocol.Aion2SkillNames.Language = "pt"; lines.Add("skill pt: " + BossCasts.NameOf(11020340));
                AionDPS.Aion2.Protocol.Aion2SkillNames.Language = "en"; lines.Add("skill en: " + BossCasts.NameOf(11020340));
                File.WriteAllLines(Path.GetFullPath(args[1]), lines);
                return 0;
            }
            if(args.FirstOrDefault() == "--rows-test")
            {
                // Diagnostic: the overlay's DPS rows with sample players, rendered to a PNG.
                var stack = new System.Windows.Controls.StackPanel { Width = 312 };
                var rows = new (string Name, long Dmg, double Dps, string Cls, long Pc, bool Self)[] { ("Krønos", 2_093_795, 7756, "templar", 138_900, false), ("Darkni", 1_770_890, 6560, "gladiator", 106_100, true), ("BigDarlans", 1_390_096, 5150, "elementalist", 98_200, false), ("THzin", 1_300_472, 4818, "ranger", 0, false) };
                stack.Children.Add(OverlayWindow.ColumnsHeader());
                foreach (var (n, cls, pe, pc, me) in new[] { ("Darkni", 0, 2196, 106_100L, true), ("Krønos", 1, 2310, 138_900L, false), ("Silix", 2, 2402, 121_000L, false), ("Masamune", 3, 2380, 119_400L, false), ("BigDarlans", 4, 2050, 98_200L, false), ("Renkhyos", 5, 2561, 116_100L, false), ("Fofinha", 6, 2389, 121_200L, false), ("Sutra", 7, 2484, 142_300L, false) })
                    stack.Children.Add(OverlayWindow.TroopRow(new TroopMember(0, n, 2401, 1, 1, 5 + cls * 4, 45, 0, null, pe, pc), me, new ProfileState()));
                stack.Children.Add(new System.Windows.Controls.Border { Height = 10 });
                long sum = rows.Sum(r => r.Dmg);
                for (int i = 0; i < rows.Length; i++)
                    stack.Children.Add(OverlayWindow.PartyRow(i + 1, new ActorRow(i, rows[i].Name, rows[i].Dmg, rows[i].Dps, 0), rows[i].Self, (double)rows[i].Dmg / rows[0].Dmg, (double)rows[i].Dmg / sum, rows[i].Cls, rows[i].Pc));
                var host = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 20, 27)), Padding = new System.Windows.Thickness(14), Child = stack };
                host.Measure(new System.Windows.Size(340, double.PositiveInfinity)); host.Arrange(new System.Windows.Rect(host.DesiredSize)); host.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth * 2, (int)host.ActualHeight * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(host);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var f = File.Create(Path.GetFullPath(args[1]))) png.Save(f);
                return 0;
            }
            if(args.FirstOrDefault() == "--drops-test")
            {
                // Test: a creature's loot panel (armour and weapons grouped by series) and the pieces of its first group, to PNGs.
                // A creature without a catalog table shows what it gave: here, a made-up book of 40 kills.
                int npcArg = int.Parse(args[1]);
                if (Catalog.Npc(npcArg) is { } r0 && !(r0.TryGetProperty("drops", out var d0) && d0.ValueKind == JsonValueKind.Array && d0.GetArrayLength() > 0))
                {
                    var book = new LootBook(); var t0 = DateTime.Now;
                    for (int k = 0; k < 40; k++)
                    {
                        var t = t0.AddSeconds(k * 30); book.Killed(npcArg, t);
                        if (k % 4 == 0) book.Gained(610530014, 1, t.AddSeconds(1));
                        if (k % 10 == 0) book.Gained(610530001, 2, t.AddSeconds(2));
                        if (k == 7) book.Gained(610530005, 1, t.AddSeconds(1));
                    }
                    LootBook.Live = book;
                }
                var w = new DropsWindow(0.92);
                if (!w.Load(int.Parse(args[1]))) throw new InvalidOperationException("Sin botín en el catálogo: " + args[1]);
                var card = (System.Windows.FrameworkElement)w.Content; w.Content = null; card.Width = 300;
                void Save(System.Windows.FrameworkElement e, string path)
                {
                    var host = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 46, 38)), Padding = new System.Windows.Thickness(12), Child = e };
                    host.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity)); host.Arrange(new System.Windows.Rect(host.DesiredSize)); host.UpdateLayout();
                    if (L.Language != "es") { L.Apply(host); host.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity)); host.Arrange(new System.Windows.Rect(host.DesiredSize)); host.UpdateLayout(); }
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth * 2, (int)host.ActualHeight * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(host);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using var f = File.Create(Path.GetFullPath(path)); png.Save(f);
                }
                Save(card, args[2]);
                if (args.Length > 3 && w.DetailOf(0) is { } detail) Save(detail, args[3]);
                w.Close();
                // The mouse on the group rows, off screen: hovering opens the detail, a click pins it, leaving keeps it,
                // a second click closes it, another group opens and closes with the hover, hiding the panel closes it.
                var live = new DropsWindow(0.92) { Left = -4000, Top = -4000 };
                live.Load(int.Parse(args[1])); live.Show();
                var groupRows = ((System.Windows.Controls.StackPanel)((System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)live.Content).Child).Children[1]).Children
                    .OfType<System.Windows.Controls.DockPanel>().Where(r => r.Cursor == System.Windows.Input.Cursors.Hand).ToList();
                var steps = new List<string> { $"groups={groupRows.Count}" };
                void Act(System.Windows.UIElement row, string what)
                {
                    var device = System.Windows.Input.Mouse.PrimaryDevice;
                    row.RaiseEvent(what == "click" ? new System.Windows.Input.MouseButtonEventArgs(device, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent }
                        : new System.Windows.Input.MouseEventArgs(device, 0) { RoutedEvent = what == "enter" ? System.Windows.Input.Mouse.MouseEnterEvent : System.Windows.Input.Mouse.MouseLeaveEvent });
                    steps.Add($"{what}: open={live.DetailOpen} pinned={live.DetailPinned}");
                }
                if (groupRows.Count > 1)
                {
                    Act(groupRows[0], "enter"); Act(groupRows[0], "click"); Act(groupRows[0], "leave"); Act(groupRows[1], "enter");
                    Act(groupRows[0], "click"); Act(groupRows[1], "enter"); Act(groupRows[1], "leave");
                    Act(groupRows[1], "click"); live.Hide(); steps.Add($"hidden: open={live.DetailOpen} pinned={live.DetailPinned}");
                }
                live.Close();
                File.WriteAllLines(Path.ChangeExtension(Path.GetFullPath(args[2]), ".txt"), steps);
                return 0;
            }
            if(args.FirstOrDefault() == "--pace-test")
            {
                // Test: the boss card in its states, drawn one under another to args[1] (the data folder's pace-test.png by default):
                // the enrage not seen yet (Kashapa as the user saw it on 2026-10-18), the group in time, late, enraged, and in the overlay.
                const long kashapa = 276_000_000;
                // args[2]: en or pt - the card's texts in that language (for the GitHub README's pictures).
                if (args.Length > 2 && args[2] is "en" or "pt") L.Language = args[2];
                var states = new (string Name, TargetInfo Target, bool Window)[]
                {
                    ("sin furia", new TargetInfo(2, 2400353, "Nutah supervisor jefe", null, 113_200_000, 160_000_000, true, 2_400_000, 113_200_000 / 2_400_000.0, 17), true),
                    ("a tiempo", new TargetInfo(1, 2400854, "Gobernante de almas Kashapa", null, 114_000_000, kashapa, true, 1_100_000, 104, 120, 132, 0, false, 114_000_000 / 132.0), true),
                    ("tarde", new TargetInfo(1, 2400854, "Gobernante de almas Kashapa", null, 171_100_000, kashapa, true, 1_100_000, 155.5, 60, 65, (171_100_000 - 1_100_000 * 65) / (double)kashapa * 100, false, 171_100_000 / 65.0), true),
                    ("en furia", new TargetInfo(1, 2400854, "Gobernante de almas Kashapa", null, 50_800_000, kashapa, true, 1_100_000, 46, 200, null, null, true, null, 12), true),
                    ("overlay", new TargetInfo(1, 2400854, "Gobernante de almas Kashapa", null, 114_000_000, kashapa, true, 1_100_000, 104, 120, 132, 0, false, 114_000_000 / 132.0), false),
                };
                var sheet = new System.Windows.Controls.StackPanel { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 38, 44)) };
                foreach (var (name, target, window) in states)
                {
                    var panel = new BossPacePanel(withTitle: window) { ShowBars = window };
                    // A hit just before (6 % of its health) leaves the pale stretch closing in.
                    if (target.Hp is long before && target.HighestObservedHp is long top) panel.Update(target with { Hp = Math.Min(top, before + top * 6 / 100) });
                    panel.Update(target);
                    string label = L.Language == "es" ? name : name switch { "sin furia" => "enrage not seen yet", "a tiempo" => "in time", "tarde" => "late", "en furia" => "enraged", _ => name };
                    sheet.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.Gray, Margin = new System.Windows.Thickness(10, 8, 0, 2) });
                    sheet.Children.Add(new System.Windows.Controls.Border { Width = window ? 360 : 320, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new System.Windows.Thickness(10, 0, 10, 4), CornerRadius = new System.Windows.CornerRadius(8),
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 20, 27)), Padding = new System.Windows.Thickness(12, 8, 12, 8), Child = panel });
                }
                sheet.Measure(new System.Windows.Size(380, double.PositiveInfinity)); sheet.Arrange(new System.Windows.Rect(sheet.DesiredSize)); sheet.UpdateLayout();
                if (L.Language != "es") L.Apply(sheet);
                sheet.Arrange(new System.Windows.Rect(sheet.DesiredSize)); sheet.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)sheet.ActualWidth * 2, (int)sheet.ActualHeight * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(sheet);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(DataDirectory, "pace-test.png"))) png.Save(file);
                return 0;
            }
            if(args.FirstOrDefault() == "--breakdown-test")
            {
                // Diagnostic: replays a recording and renders the breakdown window of its top player to a PNG.
                string outPng = args.Length > 3 ? Path.GetFullPath(args[3]) : Path.Combine(DataDirectory, "breakdown-test.png");
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(testDirectory);
                using var engine = new CaptureEngine();
                AionDPS.Aion2.Protocol.Aion2SkillNames.Language = L.Language; // the engine resets it to Spanish, as MainWindow sets it after
                foreach (var line in File.ReadLines(args[1])) { var seg = JsonSerializer.Deserialize<SegmentLog>(line)!; engine.Ingest(new(seg.Timestamp, seg.Source, seg.Destination, seg.Sequence, Convert.FromBase64String(seg.Payload), seg.FromServer)); }
                var first = engine.Breakdown(0, null, false);
                int who = args.Length > 2 && int.Parse(args[2]) is int asked and not 0 ? asked : first?.Players.FirstOrDefault()?.Id ?? 0;
                var window = new BreakdownWindow(engine.Breakdown, new AppSettings());
                window.Fill(who, null, args.Length > 4 ? args[4] : null);
                var content = (System.Windows.FrameworkElement)window.Content; window.Content = null;
                var host = new System.Windows.Controls.Border { Width = 860, Height = 600, Background = System.Windows.Media.Brushes.DimGray, Child = content };
                host.Measure(new System.Windows.Size(860, 600)); host.Arrange(new System.Windows.Rect(0, 0, 860, 600)); host.UpdateLayout();
                if (L.Language != "es") { L.Apply(host); host.Measure(new System.Windows.Size(860, 600)); host.Arrange(new System.Windows.Rect(0, 0, 860, 600)); host.UpdateLayout(); }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(860, 600, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(host);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(outPng)) png.Save(file);
                File.WriteAllText(Path.Combine(DataDirectory, "breakdown-test.json"), JsonSerializer.Serialize(engine.Breakdown(who, null, false), new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if(args.FirstOrDefault() == "--dump-opcode")
            {
                // Diagnostic: every frame of one opcode in a recording, "time hex" per line.
                int wanted = Convert.ToInt32(args[2], 16);
                var output = new List<string>();
                using var engine = new CaptureEngine();
                engine.Tap = (bytes, at) => Frames.Walk(bytes, f => { if (f.Length >= 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f) == wanted) output.Add($"{at:HH:mm:ss.fff} {Convert.ToHexString(f)}"); });
                foreach (var line in File.ReadLines(args[1])) { var seg = JsonSerializer.Deserialize<SegmentLog>(line)!; engine.Ingest(new(seg.Timestamp, seg.Source, seg.Destination, seg.Sequence, Convert.FromBase64String(seg.Payload), seg.FromServer)); }
                File.WriteAllLines(Path.Combine(DataDirectory, "dump-opcode.txt"), output);
                return 0;
            }
            if(args.FirstOrDefault() == "--dump-pos")
            {
                // Diagnostic: spawn announcements (0x4136 with place) and the player's exact positions, one per line, to
                // calibrate the position estimate.
                var lines = File.ReadAllLines(args[1]);
                int me = int.Parse(args[2]);
                var own = new OwnPosition { LocalId = me };
                var output = new List<string>();
                (float, float, DateTime)? lastOwn = null;
                using var engine = new CaptureEngine();
                engine.Tap = (bytes, at) => Frames.Walk(bytes, f =>
                {
                    if (f.Length >= 40 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f) == 0x4136)
                    {
                        int p = 2; long entity = 0;
                        for (int shift = 0; p < f.Length; shift += 7) { byte b = f[p++]; entity |= (long)(b & 127) << shift; if (b < 128) break; }
                        if (p + 3 < f.Length && f[p + 2] == 1) p += 1 + f[p + 3];
                        p += 3;
                        if (p + 14 <= f.Length) output.Add($"S {at:O} {entity} {System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(f[(p + 6)..])} {System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(f[(p + 10)..])}");
                    }
                    own.Add(f.ToArray());
                    if (own.Last is { } o && o != lastOwn) { lastOwn = o; output.Add($"O {at:O} 0 {o.X} {o.Y}"); }
                });
                foreach (var line in lines) { var seg = JsonSerializer.Deserialize<SegmentLog>(line)!; engine.Ingest(new(seg.Timestamp, seg.Source, seg.Destination, seg.Sequence, Convert.FromBase64String(seg.Payload), seg.FromServer)); }
                File.WriteAllLines(Path.Combine(DataDirectory, "dump-pos.txt"), output);
                return 0;
            }
            if(args.FirstOrDefault() == "--scan-own")
            {
                // Diagnostic: which frames of a recording carry the local player's id followed by a world position.
                var lines = File.ReadAllLines(args[1]);
                int me;
                using (var first = new CaptureEngine())
                {
                    foreach (var line in lines) { var seg = JsonSerializer.Deserialize<SegmentLog>(line)!; first.Ingest(new(seg.Timestamp, seg.Source, seg.Destination, seg.Sequence, Convert.FromBase64String(seg.Payload), seg.FromServer)); }
                    me = args.Length > 2 ? int.Parse(args[2]) : first.Snapshot().LocalId;
                }
                var id = new List<byte>(); for (uint v = (uint)me; ; v >>= 7) { id.Add((byte)(v < 128 ? v : (v & 127) | 128)); if (v < 128) break; }
                var stats = new Dictionary<string, (int Count, List<string> Samples)>();
                using var engine = new CaptureEngine();
                engine.Tap = (bytes, at) => Frames.Walk(bytes, f =>
                {
                    if (f.Length < 12) return;
                    string op = $"0x{System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(f):X4}";
                    for (int i = f.IndexOf(id.ToArray()); i >= 0 && i < f.Length; )
                    {
                        for (int k = i + id.Count; k + 8 <= f.Length && k < i + id.Count + 48; k++)
                        {
                            float x = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(f[k..]), y = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(f[(k + 4)..]);
                            if (!float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) < 1000 || Math.Abs(y) < 1000 || Math.Abs(x) > 400000 || Math.Abs(y) > 400000) continue;
                            string key = $"{op} @id{i}+{k - i}";
                            if (!stats.TryGetValue(key, out var e)) e = (0, []);
                            if (e.Samples.Count < 6) e.Samples.Add($"{at:HH:mm:ss.fff} ({x:0},{y:0}) {Convert.ToHexString(f[..Math.Min(f.Length, 64)])}");
                            stats[key] = (e.Count + 1, e.Samples);
                            break;
                        }
                        int next = f[(i + 1)..].IndexOf(id.ToArray());
                        i = next < 0 ? -1 : i + 1 + next;
                    }
                });
                foreach (var line in lines) { var seg = JsonSerializer.Deserialize<SegmentLog>(line)!; engine.Ingest(new(seg.Timestamp, seg.Source, seg.Destination, seg.Sequence, Convert.FromBase64String(seg.Payload), seg.FromServer)); }
                File.WriteAllText(Path.Combine(DataDirectory, "scan-own.json"), JsonSerializer.Serialize(new { me, stats = stats.OrderByDescending(s => s.Value.Count).Select(s => new { key = s.Key, s.Value.Count, s.Value.Samples }) }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if(args.FirstOrDefault() == "--casts-test")
            {
                // Diagnostic: logged 0x0938 frames through the boss-cast filter, as the boss being the target.
                var casts = new BossCasts(); var output = new List<string>(); int target = int.Parse(args[2]);
                foreach (var line in File.ReadLines(args[1]))
                {
                    var parts = line.Split(' ');
                    if (parts.Length < 4 || parts[2] != "0x0938") continue;
                    var at = DateTime.Parse(parts[0] + " " + parts[1]);
                    casts.Add(Convert.FromHexString(parts[3]), at);
                    foreach (var r in casts.Recent(at, TimeSpan.FromMilliseconds(1), _ => null, target)) output.Add($"{parts[1]} {r.Skill} · {r.CastMs} ms · objetivo={r.IsTarget}");
                }
                File.WriteAllLines(Path.Combine(DataDirectory, "casts-test.txt"), output);
                return 0;
            }
            if(args.FirstOrDefault() == "--troop-test")
            {
                // Diagnostic: replays logged frames ("date time 0xOPCODE HEX") through the troop and room readers.
                var troops = new TroopRoster(); var rooms = new RoomRoster(); var results = new List<object?>();
                foreach (var line in File.ReadLines(args[1]))
                {
                    var parts = line.Split(' ');
                    if (parts.Length < 4) continue;
                    var bytes = Convert.FromHexString(parts[3]);
                    troops.Add(bytes); rooms.Add(bytes);
                    results.Add(new { at = parts[1], frame = parts[2], troop = troops.Current?.Members.Select(m => $"G{m.Group}.{m.Slot} {m.Name} {m.ClassKey} PE{m.GearScore} PC{m.CombatPower}"), room = rooms.Current?.Members.Select(m => $"{m.Slot}:{m.Name}{(m.Ready ? "+" : "")}") });
                }
                File.WriteAllText(Path.Combine(DataDirectory, "troop-test.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                return 0;
            }
            if(args.FirstOrDefault() == "--room-test")
            {
                // Diagnostic: decodes the room frames kept in party-frames.log ("time opcode hex") or room-frames.log ("time
                // 0xOPCODE hex => room as the app had it"), in a throwaway data folder, and writes the room after each one.
                string source = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(DataDirectory, "party-frames.log"));
                string report = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(DataDirectory, "room-test.json");
                testDirectory = Path.Combine(Path.GetTempPath(), "fa2c-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDirectory);
                try
                {
                    var rooms = new RoomRoster(); var results = new List<object?>();
                    foreach (var line in File.ReadLines(source))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length < 3 || parts[2].Length < 8 || !parts[2].All(Uri.IsHexDigit)) continue;
                        rooms.Add(Convert.FromHexString(parts[2]));
                        results.Add(new { at = parts[0], frame = parts[1], logged = line.Split(" => ").ElementAtOrDefault(1), room = rooms.Current });
                    }
                    File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
                finally { Directory.Delete(testDirectory, true); testDirectory = null; }
                return 0;
            }
            if(args.FirstOrDefault() == "--profile-id")
            {
                // Diagnostic: the official reference (region, server, encrypted id) of a character, as the license uses it.
                var state = PlayerProfiles.Get(args[1], args.Length > 2 ? int.Parse(args[2]) : 2401, "la");
                for (int i = 0; i < 300 && state.Stage is "search" or "basic"; i++) { Thread.Sleep(50); state = PlayerProfiles.Get(args[1], args.Length > 2 ? int.Parse(args[2]) : 2401, "la"); }
                File.WriteAllText(Path.GetFullPath(args.Length > 3 ? args[3] : "profile-id.txt"), $"{args[1]} stage={state.Stage} error={state.Error} id={state.Card?.CharacterId} server={state.Card?.ServerId} region={state.Card?.Region}");
                return 0;
            }
            if(args.FirstOrDefault() == "--card-test")
            {
                // Diagnostic: renders the hover card of a player's first item to a PNG.
                var state = PlayerProfiles.Get(args[1], args.Length > 2 ? int.Parse(args[2]) : 2401, "la");
                for (int i = 0; i < 300 && state.Stage is "search" or "basic"; i++) Thread.Sleep(50);
                var gearRow = state.Card!.Gear[args.Length > 3 ? int.Parse(args[3]) : 0];
                var detail = PlayerProfiles.ItemDetail(state.Card, gearRow).GetAwaiter().GetResult();
                var element = (System.Windows.FrameworkElement)PlayerWindow.DetailCard(gearRow, detail, false);
                var host = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(20), Background = System.Windows.Media.Brushes.DimGray, Child = element };
                var until = DateTime.Now.AddSeconds(4);
                while (DateTime.Now < until) { System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); Thread.Sleep(50); }
                host.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity)); host.Arrange(new System.Windows.Rect(host.DesiredSize));
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(host);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(DataDirectory, "card-test.png"))) png.Save(file);
                return 0;
            }
            if(args.FirstOrDefault() == "--replay")
            {
                using var engine = new CaptureEngine();
                foreach(var line in File.ReadLines(args[1]))
                {
                    var segment = JsonSerializer.Deserialize<SegmentLog>(line)!;
                    engine.Ingest(new(segment.Timestamp, segment.Source, segment.Destination, segment.Sequence, Convert.FromBase64String(segment.Payload), segment.FromServer));
                }
                File.WriteAllText(Path.Combine(DataDirectory, "replay-result.json"), JsonSerializer.Serialize(engine.Snapshot(), new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(DataDirectory, "frame-analysis.json"), JsonSerializer.Serialize(engine.Analysis, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            // Exceptions off the UI thread (capture callbacks) end the process without the dispatcher handler seeing them.
            AppDomain.CurrentDomain.UnhandledException += (_, e) => File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), $"{DateTime.Now:o} fatal: {e.ExceptionObject}" + Environment.NewLine);
            var app = new Application();
            app.DispatcherUnhandledException += (_, e) => { File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), e.Exception + "\n"); MessageBox.Show(e.Exception.Message, "FullAIon2Companion"); e.Handled = true; };
            app.Run(new MainWindow());
            return 0;
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(DataDirectory, "startup-error.log"), ex.ToString()); return 1; }
    }
}

/// <summary>
/// A stand-in market source for --market-test: two servers and, on any server, these items at 1,000 + their id's last three
/// digits (one in seven not on sale now, at that as its last price), counting the Markets asked for.
/// </summary>
sealed class TestMarketSource(IEnumerable<int> known) : IMarketSource
{
    readonly int[] items = known.ToArray();
    public string Name => "Prueba";
    public int Calls;
    public static long PriceOf(int item) => 1000 + item % 1000;
    public Task<IReadOnlyList<MarketServer>> ServersAsync(CancellationToken cancel) =>
        Task.FromResult<IReadOnlyList<MarketServer>>([new(2401, "Israphel", "LA", "Latin America", "Asmodian"), new(2402, "Zikel", "LA", "Latin America", "Asmodian")]);
    public Task<MarketBoard> BoardAsync(int server, CancellationToken cancel)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(new MarketBoard(server, DateTime.UtcNow,
            items.Select(i => new MarketRow(i, "Item " + i, "materials", i % 7 == 0 ? null : PriceOf(i), PriceOf(i), i % 7 == 0 ? 0 : 3, 1.5, DateTime.UtcNow)).ToList()));
    }
}
