namespace FullAIon2Companion;

/// <summary>The objective now: what, how far, at what pace and when it ends at this pace; with a time limit
/// ("reto"), the time left and whether the pace is enough.</summary>
public sealed record ObjectiveView(string Kind, long Target, long Progress, double Percent, double PerHour, double? EtaSeconds,
    int Minutes, double? ChallengeLeft, bool? OnPace, bool Done, DateTime Start, double Elapsed);

/// <summary>
/// A farm objective (TeamMate's "Objetivos"): a number of kills, kinah earned or loot value, counted from when it was set
/// or restarted (the farm session counters minus their value at that moment). The definition is kept in the settings;
/// the count starts again with the app, like the farm session.
/// </summary>
public sealed class FarmObjective(AppSettings settings)
{
    public static readonly string[] Kinds = ["kills", "kinah", "loot"];
    DateTime start = DateTime.Now;
    long baseKills, baseKinah, baseLoot;
    bool doneNotified;
    /// <summary>Called once when the objective is reached.</summary>
    public event Action<ObjectiveView>? Reached;
    public void Restart(long kills, long kinah, long loot) { start = DateTime.Now; baseKills = kills; baseKinah = kinah; baseLoot = loot; doneNotified = false; }
    /// <summary>The farm session was reset: its counters start from zero again.</summary>
    public void SessionReset() { baseKills = baseKinah = baseLoot = 0; start = DateTime.Now; doneNotified = false; }
    public ObjectiveView? View(long kills, long kinah, long loot, DateTime now)
    {
        if (settings.GoalTarget <= 0) return null;
        // A counter below its baseline means the session was reset elsewhere: count from zero.
        if (kills < baseKills || kinah < baseKinah || loot < baseLoot) SessionReset();
        long progress = settings.GoalKind switch { "kinah" => kinah - baseKinah, "loot" => loot - baseLoot, _ => kills - baseKills };
        long target = settings.GoalTarget;
        double elapsed = Math.Max(1, (now - start).TotalSeconds);
        double perHour = progress / elapsed * 3600;
        bool done = progress >= target;
        double? eta = done ? 0 : progress > 0 ? (target - progress) / (progress / elapsed) : null;
        double? left = settings.GoalMinutes > 0 ? settings.GoalMinutes * 60 - elapsed : null;
        bool? onPace = left is double l && !done ? eta is double e && e <= l : null;
        var view = new ObjectiveView(settings.GoalKind, target, progress, Math.Min(100, progress * 100.0 / target), perHour, eta,
            settings.GoalMinutes, left, onPace, done, start, elapsed);
        if (done && !doneNotified) { doneNotified = true; Reached?.Invoke(view); }
        return view;
    }
}
