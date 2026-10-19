using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace FullAIon2Companion;
public sealed record TimerRow(string Id, string Name, string Color, DateTimeOffset At, double Remaining, bool Alert, string? MapId, string? MarkerId);
public sealed class EventTimers
{
    readonly JsonElement[] events;
    readonly AppSettings settings;
    readonly MediaPlayer player = new(), bossPlayer = new();
    public string? LastAlert { get; private set; }
    /// <summary>An event alert fired: name, colour, start, and whether it starts now (else it is minutes away).</summary>
    public event Action<string, string, DateTimeOffset, bool>? Alerted;
    static readonly Dictionary<string,string> Names = new()
    {
        ["rift"]="Grieta espacio-temporal", ["beritra"]="Invasión dimensional", ["defend-shugo"]="Festival de Shugo",
        ["boss-watcher-kaira"]="Veldor Kaira", ["boss-executor-argo"]="Ejecutante Agro", ["boss-executor-kaira"]="Ejecutante Kaira",
        ["boss-executor-tamasa"]="Ejecutante Tamasa", ["boss-nahma"]="Señor guardián Nahma", ["boss-dramos"]="Sayón Dramos",
        ["boss-marakha"]="Desintegrador Maraka", ["boss-ducal"]="Desleal Ducal", ["abyss"]="Asedio de artefactos",
        ["daily-reset"]="Reinicio diario", ["weekly-reset"]="Reinicio semanal", ["arena-of-tactics"]="Arena de tácticas"
    };
    public EventTimers(AppSettings settings)
    {
        this.settings = settings;
        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "events.json"))).RootElement;
        var groups = new HashSet<string>();
        events = root.GetProperty("events").EnumerateArray().Where(e => !e.TryGetProperty("group",out var g) || groups.Add(g.GetString()!)).Select(e=>e.Clone()).ToArray();
        EnsureChime();
        EnsureBossAlarm();
    }
    public static DateTimeOffset? Next(JsonElement e, DateTimeOffset now, int regionOffset)
    {
        if(e.TryGetProperty("enabled",out var enabled) && !enabled.GetBoolean()) return null;
        bool local = e.TryGetProperty("localSchedule",out var schedule);
        if(!local && !e.TryGetProperty("schedule",out schedule)) return null;
        int offset = local ? regionOffset : 480; // Same published timetable as Daeva Companion.
        var shifted = now.ToOffset(TimeSpan.FromMinutes(offset));
        var midnight = new DateTimeOffset(shifted.Date, shifted.Offset);
        string type = schedule.GetProperty("type").GetString()!;
        if(type is "hourly" or "interval")
        {
            int step = type == "hourly" ? 60 : schedule.GetProperty("everyMin").GetInt32();
            if(step <= 0) return null;
            double anchorMinutes = type == "hourly" ? (schedule.TryGetProperty("minute",out var minute) ? minute.GetDouble() : 0)
                : TimeSpan.Parse(schedule.TryGetProperty("anchor",out var anchor) ? anchor.GetString()! : "00:00").TotalMinutes;
            var at = midnight.AddMinutes(anchorMinutes);
            return at.AddMinutes((Math.Floor((now-at).TotalMinutes/step)+1)*step);
        }
        for(int day = 0; day <= 8; day++)
        {
            var date = midnight.AddDays(day);
            if(type == "weekday" && !schedule.GetProperty("days").EnumerateArray().Any(d => d.GetInt32() == (int)date.DayOfWeek)) continue;
            var times = type == "daily" ? schedule.GetProperty("times").EnumerateArray().Select(t=>t.GetString()!) : [schedule.GetProperty("time").GetString()!];
            foreach(var time in times.Order()) { var at = date.Add(TimeSpan.Parse(time)); if(at>now) return at; }
        }
        return null;
    }
    public IReadOnlyList<TimerRow> Tick(DateTimeOffset now)
    {
        var rows = new List<TimerRow>();
        foreach(var e in events)
        {
            string id = e.GetProperty("id").GetString()!;
            var at = Next(e,now,settings.RegionOffset);
            if(at is null) continue;
            var name = Names.GetValueOrDefault(id) ?? "Evento de Atreia";
            rows.Add(new(id,name,e.GetProperty("color").GetString()!,at.Value,(at.Value-now).TotalSeconds,settings.SelectedEvents.Contains(id),
                e.TryGetProperty("mapId",out var map) ? map.GetString() : null,e.TryGetProperty("markerId",out var marker) ? marker.GetString() : null));
            // Marked events alert with a sound, a corner notification, or both; neither when both are off.
            if(!settings.SelectedEvents.Contains(id) || !settings.Sounds && !settings.EventToasts) continue;
            foreach(var occurrence in new[] {at,Next(e,now.AddSeconds(-15),settings.RegionOffset)}.Distinct())
            {
                if(occurrence is null) continue;
                double left = (occurrence.Value-now).TotalSeconds;
                string? phase = left>0 && left<=settings.LeadMinutes*60 ? "soon" : left<=0 && left>=-15 ? "start" : null;
                if(phase is null) continue;
                string key = $"{id}:{occurrence.Value.ToUnixTimeSeconds()}:{phase}";
                if(!settings.SentAlerts.Add(key)) continue;
                LastAlert = name + (phase=="start" ? ": comienza ahora" : $": en menos de {settings.LeadMinutes} min");
                if(settings.Sounds) Play();
                if(settings.EventToasts) Alerted?.Invoke(name, e.GetProperty("color").GetString()!, occurrence.Value, phase=="start");
                if(settings.SentAlerts.Count>500) settings.SentAlerts = settings.SentAlerts.TakeLast(400).ToHashSet();
                settings.Save();
            }
        }
        return rows.OrderBy(r=>r.At).ToList();
    }
    public void Play() { player.Stop(); player.Open(new Uri(Path.Combine(Program.DataDirectory,"alert.wav"))); player.Volume = Math.Clamp(settings.Volume,0,1); player.Play(); }
    /// <summary>Boss mechanic warning: louder than the event chime so it cuts through the game's own sound.</summary>
    public void PlayBossAlert()
    {
        // A voice line (sounds/boss-es.mp3 / boss-en.mp3, shipped with the app) or the generated klaxon.
        string voice = Path.Combine(AppContext.BaseDirectory,"sounds",$"boss-{settings.BossAlertVoice}.mp3");
        bossPlayer.Stop(); bossPlayer.Open(new Uri(settings.BossAlertVoice is "es" or "en" && File.Exists(voice) ? voice : Path.Combine(Program.DataDirectory,BossAlarmFile))); bossPlayer.Volume = Math.Clamp(settings.Volume*2,.15,1); bossPlayer.Play();
    }
    const string BossAlarmFile = "boss-alert-v1.wav";
    // Raid-warning style klaxon: three fast falling sweeps with odd harmonics (a buzzy, square-like tone), ~0.75 s.
    static void EnsureBossAlarm()
    {
        string path = Path.Combine(Program.DataDirectory,BossAlarmFile);
        if(File.Exists(path)) return;
        const int sampleRate=22050; int count=(int)(sampleRate*.78);
        var samples=new double[count];
        for(int burst=0;burst<3;burst++)
        {
            double start=burst*.22, length=.19, phase=0;
            for(int n=(int)(start*sampleRate);n<Math.Min(count,(int)((start+length)*sampleRate));n++)
            {
                double dt=(double)n/sampleRate-start, f=1180-420*dt/length;
                phase+=2*Math.PI*f/sampleRate;
                double tone=Math.Sin(phase)+Math.Sin(3*phase)/3+Math.Sin(5*phase)/5+Math.Sin(7*phase)/7;
                samples[n]+=tone*Math.Min(1,dt/.006)*Math.Min(1,(length-dt)/.03);
            }
        }
        double peak=samples.Max(Math.Abs);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8.ToArray()); writer.Write(36+count*2); writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate*2); writer.Write((short)2);writer.Write((short)16);
        writer.Write("data"u8.ToArray());writer.Write(count*2);
        foreach(double v in samples) writer.Write((short)(v/peak*.85*short.MaxValue));
    }
    static void EnsureChime()
    {
        string path = Path.Combine(Program.DataDirectory,"alert.wav");
        if(File.Exists(path)) return;
        const int sampleRate=22050, count=33075;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8.ToArray()); writer.Write(36+count*2); writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate*2); writer.Write((short)2);writer.Write((short)16);
        writer.Write("data"u8.ToArray());writer.Write(count*2);
        double[] frequencies=[659.25,830.61,987.77];
        for(int n=0;n<count;n++) { double t=(double)n/sampleRate, value=0;for(int i=0;i<3;i++) { double dt=t-i*.14;if(dt>=0 && dt<.65) value+=Math.Sin(dt*2*Math.PI*frequencies[i])*Math.Min(1,dt/.025)*Math.Exp(-dt*7)*.18; } writer.Write((short)(Math.Clamp(value,-1,1)*short.MaxValue)); }
    }
}
