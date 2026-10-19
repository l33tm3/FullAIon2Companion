using System.Net.Http;

namespace FullAIon2Companion;

/// <summary>What the panel shows of the title verification (serialized for the WebView).</summary>
public sealed record TitleView(string State, string Badge, string? CharacterName, CharacterRef? Character, string Text, string? CurrentTitle, string? CategoryLabel,
    DateTime? ExpiresAtUtc, IReadOnlyList<string> Linked, int Limit, string Message, bool CanStart, bool CanCheck, bool AutoChecking);

/// <summary>
/// Ownership by title change, the same flow as Mi perfil on the web: the server names an equipped title of the character in
/// play to swap in the game and confirms the change itself on the official AION 2 profile, so nothing the capture says is
/// taken as proof. While a challenge is pending it is checked again every 20 s (the server allows one every 10 s and 60 in
/// all), also when it was started on the web, so the player only has to change the title. Up to
/// <see cref="PullState.CharacterLimit"/> verified characters per account; removing one is done on the web.
/// </summary>
public sealed class TitleVerification(ITitleApi api, Func<DateTime> now, Func<Task> refresh)
{
    public static readonly TimeSpan AutoEvery = TimeSpan.FromSeconds(20);
    bool online;
    CharacterRef? own;
    string? ownName, why;
    PullState? pull;
    TitleChallenge? started;               // started or checked here, ahead of the next pull
    readonly HashSet<string> closed = [];  // cancelled, expired or verified here: the pull may list them for a while longer
    CharacterRef? verifiedHere;            // verified by a check here, until the pull lists the link
    DateTime nextAuto = DateTime.MinValue;
    bool busy, autoStopped;
    string message = "";

    public void UpdateContext(bool online, CharacterRef? own, string? ownName, string? why, PullState? pull)
    {
        if (!Same(this.own, own)) { started = null; message = ""; autoStopped = false; nextAuto = DateTime.MinValue; }
        (this.online, this.own, this.ownName, this.why, this.pull) = (online, own, ownName, why, pull);
        if (verifiedHere is not null && pull?.LinkedCharacters.Any(l => l.SameAs(verifiedHere)) == true) verifiedHere = null;
    }
    static bool Same(CharacterRef? a, CharacterRef? b) => a is null ? b is null : a.SameAs(b);
    bool Verified => own is not null && (pull?.LinkedCharacters.Any(l => l.SameAs(own)) == true || own.SameAs(verifiedHere));
    int LinkedCount => (pull?.LinkedCharacters.Count ?? 0) + (verifiedHere is not null && pull?.LinkedCharacters.Any(l => l.SameAs(verifiedHere)) != true ? 1 : 0);
    int Limit => pull is { CharacterLimit: > 0 } p ? p.CharacterLimit : 3;
    TitleChallenge? Pending()
    {
        if (own is null || Verified) return null;
        var t = started is { Status: "pending" } s && s.Character.SameAs(own) && !closed.Contains(s.ChallengeId) ? s
            : pull?.TitleChallenges?.FirstOrDefault(c => c.Character.SameAs(own) && c.Status == "pending" && !closed.Contains(c.ChallengeId));
        return t is not null && (t.ExpiresAtUtc is null || t.ExpiresAtUtc > now()) ? t : null;
    }

    public TitleView View()
    {
        var pending = Pending();
        var (state, badge, text) =
            !online ? ("unpaired", "Sin conectar", "Vincula este dispositivo con tu cuenta para verificar tu personaje.")
            : own is null ? ("no-character", "Esperando personaje", why ?? "Entra al juego con tu personaje para verificarlo.")
            : Verified ? ("verified", "Verificado", "Este personaje está verificado en tu cuenta.")
            : pending is not null ? ("pending", "En curso", "En el juego, cambia este título por cualquier otro de la misma categoría. La app lo comprueba sola cada 20 segundos; cuando quede verificado, vuelve a ponerte el que prefieras.")
            : LinkedCount >= Limit ? ("full", "Límite alcanzado", $"Ya tienes {Limit} personajes verificados, el máximo por cuenta. Quita uno en Mi perfil de la web para añadir este.")
            : ("unverified", "Sin verificar", "¿Es tu personaje? Verifícalo para vincularlo a tu cuenta: te pediremos cambiar un título en el juego y el servidor lo comprobará en la web oficial de AION 2.");
        var linked = (pull?.LinkedDetails ?? []).Select(l => l.CharacterName.Length > 0 ? l.CharacterName : Same(l.Character, own) && ownName is { Length: > 0 } ? ownName : "Personaje").ToList();
        return new(state, badge, ownName, own, text, pending?.CurrentTitle, pending?.CategoryLabel, pending?.ExpiresAtUtc, linked, Limit, message,
            state == "unverified" && !busy, state == "pending" && !busy, state == "pending" && !autoStopped);
    }

    public async Task StartAsync()
    {
        if (busy || own is not { } who || View().State != "unverified") return;
        busy = true;
        try
        {
            started = await api.StartTitleChallengeAsync(who);
            closed.Remove(started.ChallengeId); autoStopped = false; nextAuto = now() + AutoEvery;
            message = "Cambia el título en el juego. La app lo comprobará sola.";
        }
        catch (CompanionApiException ex) { message = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { message = ex is HtmlAnswerException ? ex.Message : "Sin conexión con el servidor. Inténtalo de nuevo."; }
        finally { busy = false; }
    }

    public async Task CheckAsync(bool auto = false)
    {
        if (busy || !online || Pending() is not { } pending) return;
        busy = true;
        try
        {
            var r = await api.CheckTitleAsync(pending.ChallengeId);
            nextAuto = now() + AutoEvery; message = r.Message;
            if (r.Verified) { verifiedHere = r.Challenge.Character; closed.Add(pending.ChallengeId); started = null; await Refresh(); }
            else started = r.Challenge;
        }
        catch (CompanionApiException ex)
        {
            nextAuto = now() + (ex.RetryAfter is { } wait && wait > TimeSpan.Zero ? wait : AutoEvery);
            // 404/409: cancelled on the web, expired or gone. "Demasiadas comprobaciones": the server stopped counting this one.
            if (ex.Status is 404 or 409) { closed.Add(pending.ChallengeId); started = null; message = ex.Message; await Refresh(); }
            else if (ex.Status == 429 && ex.Message.StartsWith("Demasiadas comprobaciones")) { autoStopped = true; message = ex.Message; }
            else if (!auto) message = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { nextAuto = now() + AutoEvery; if (!auto || ex is HtmlAnswerException) message = ex is HtmlAnswerException ? ex.Message : "Sin conexión con el servidor. Se reintentará."; }
        finally { busy = false; }
    }

    public async Task CancelAsync()
    {
        if (busy || Pending() is not { } pending) return;
        busy = true;
        try { await api.CancelTitleAsync(pending.ChallengeId); closed.Add(pending.ChallengeId); started = null; message = "Verificación cancelada."; await Refresh(); }
        catch (CompanionApiException ex) { message = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { message = ex is HtmlAnswerException ? ex.Message : "Sin conexión con el servidor. Inténtalo de nuevo."; }
        finally { busy = false; }
    }

    /// <summary>Called with the panel's refresh: checks a pending challenge again every 20 s until it is verified or closed.</summary>
    public async Task TickAsync()
    {
        if (busy || !online || autoStopped || now() < nextAuto || Pending() is null) return;
        await CheckAsync(auto: true);
    }

    async Task Refresh() { try { await refresh(); } catch { } }
}
