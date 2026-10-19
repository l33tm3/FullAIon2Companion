using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>One answer of <c>POST companion.php?action=license</c> (contract: docs/claude-companion-license-prompt.md).</summary>
public sealed record LicenseAnswer(string? LicenseId, string AccountId, string DeviceId, CharacterRef? Character, string Plan, string Status,
    string Reason, string? Source, DateTime? ExpiresUtc, DateTime ServerUtc, int RecheckAfterSeconds, bool CompanionPvp, bool CompanionMinimap)
{
    /// <summary>Strict reading: anything missing or of the wrong type throws, so a malformed answer never grants anything.</summary>
    public static LicenseAnswer Parse(JsonElement e)
    {
        static string Need(JsonElement e, string k) => e.GetProperty(k).GetString() ?? throw new FormatException(k);
        static string? Opt(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static DateTime Time(string s) => DateTimeOffset.TryParseExact(s,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFzzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.UtcDateTime : throw new FormatException("Fecha de licencia");
        static bool Flag(JsonElement e, string k) => e.GetProperty(k).ValueKind switch
            { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new FormatException(k) };
        static string Id(JsonElement e, string k) => System.Text.RegularExpressions.Regex.IsMatch(Need(e, k), "^[a-f0-9]{32}$") ? Need(e, k) : throw new FormatException(k);
        var f = e.GetProperty("features");
        if (e.GetProperty("offlineGraceSeconds").GetInt32() != 0) throw new FormatException("offlineGraceSeconds");
        int recheck = e.GetProperty("recheckAfterSeconds").GetInt32();
        if (recheck < 1) throw new FormatException("recheckAfterSeconds");
        CharacterRef? c = e.TryGetProperty("character", out var cv) && cv.ValueKind == JsonValueKind.Object
            ? new(Need(cv, "region"), cv.GetProperty("serverId").GetInt32(), Need(cv, "characterId")) : null;
        string? licenseId = e.GetProperty("licenseId").ValueKind == JsonValueKind.Null ? null : Id(e, "licenseId");
        var answer = new LicenseAnswer(licenseId, Id(e, "accountId"), Id(e, "deviceId"), c, Need(e, "plan"), Need(e, "status"), Need(e, "reason"), Opt(e, "source"),
            Opt(e, "expiresAt") is { } x ? Time(x) : null, Time(Need(e, "serverTime")),
            recheck, Flag(f, "companionPvp"), Flag(f, "companionMinimap"));
        if (answer.Status is not ("active" or "inactive") || answer.Plan is not ("plus" or "free")
            || answer.Status == "active" && (answer.Plan != "plus" || answer.Reason != "active" || licenseId is null || c is null || answer.ExpiresUtc is null)
            || answer.Status == "inactive" && (answer.CompanionPvp || answer.CompanionMinimap)) throw new FormatException("Estado de licencia contradictorio");
        return answer;
    }
}

/// <summary>The license call (an interface so the gate is tested offline with fakes).</summary>
public interface ILicenseApi
{
    /// <summary>The device id the stored token belongs to (from redeem / pull), or null when unknown.</summary>
    string? DeviceId { get; }
    /// <summary>Changes whenever the token changes (paired again, revoked, disconnected).</summary>
    int TokenGeneration { get; }
    bool Paired { get; }
    string? AccountId { get; }
    bool Online { get; }
    int ConnectionGeneration { get; }
    Task<LicenseAnswer> LicenseAsync(CharacterRef character, CancellationToken ct);
}

/// <summary>What the Cuenta panel shows about the license of the character in play.</summary>
public sealed record LicenseView(string State, string Title, string Detail, CharacterRef? Character, bool Pvp, bool Minimap,
    DateTime? ExpiresUtc, DateTime? LastCheckLocal, string Connectivity);

/// <summary>
/// Companion Plus for the character being played, and nothing else. A Plus feature is on only while the last answer of
/// <c>action=license</c>: belongs to this device, to the account first seen with this token and to the character the capture
/// sees now (official reference, never a nickname); says <c>status=active</c> with that flag true; and is younger than its
/// 60 s (and than <c>expiresAt - serverTime</c>), measured with a monotonic clock so changing Windows' time extends nothing.
/// No offline grace: a network error, 401/403, an invalid answer or a different character turns everything off at once.
/// Nothing is persisted: no local "isPlus". The pull's subscription flags never enable anything here.
/// </summary>
public sealed class LicenseGate : IDisposable
{
    readonly ILicenseApi api;
    readonly Func<long> monotonicMs;
    readonly object gate = new();
    CharacterRef? character;          // the own character the capture resolved, or null when not certain
    string? whyNoCharacter;
    int generation;                   // bumps on every change of identity or token: late answers of an older one are dropped
    int tokenGeneration = -1;
    int connectionGeneration = -1;
    LicenseAnswer? answer;
    long validUntilMs, nextCheckMs;
    bool inFlight;
    CancellationTokenSource? cts;
    string connectivity = "Sin comprobar";
    string? error;
    DateTime? lastCheck;

    public LicenseGate(ILicenseApi api, Func<long>? monotonicMs = null)
    {
        this.api = api;
        this.monotonicMs = monotonicMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
    }

    /// <summary>
    /// The character in play, every tick. Null (with the reason) whenever the capture has not resolved it with certainty:
    /// Plus stays off and the free meter goes on. A different character, or a new token, invalidates the last answer.
    /// </summary>
    public void SetCharacter(CharacterRef? current, string? whyNot = null)
    {
        lock (gate)
        {
            whyNoCharacter = whyNot;
            bool tokenChanged = api.TokenGeneration != tokenGeneration;
            bool connectionChanged = api.ConnectionGeneration != connectionGeneration;
            if (!tokenChanged && !connectionChanged && (current?.SameAs(character) ?? character is null)) return;
            if (tokenChanged) tokenGeneration = api.TokenGeneration;
            connectionGeneration = api.ConnectionGeneration;
            character = current;
            Invalidate();
        }
    }
    void Invalidate()
    {
        generation++; answer = null; validUntilMs = 0; nextCheckMs = 0; error = null;
        cts?.Cancel(); cts?.Dispose(); cts = null; inFlight = false;
    }
    /// <summary>Asks again on the next tick (before opening the PvP card or the minimap).</summary>
    public void RecheckSoon() { lock (gate) Invalidate(); }
    public void Dispose() { lock (gate) Invalidate(); }

    /// <summary>Call often (every tick). Starts a check when due: at once after a change, then every 50 s (before the 60 s run out).</summary>
    public async Task TickAsync()
    {
        CharacterRef who; int gen, tokenGen, connectionGen; string device, account; long started; CancellationToken ct;
        lock (gate)
        {
            if (api.TokenGeneration != tokenGeneration || api.ConnectionGeneration != connectionGeneration) { tokenGeneration = api.TokenGeneration; connectionGeneration = api.ConnectionGeneration; Invalidate(); }
            // The device id comes with redeem / pull: until it is known an answer could not be matched to this device.
            if (!api.Paired || !api.Online || api.DeviceId is null || api.AccountId is null || character is null || inFlight || monotonicMs() < nextCheckMs) return;
            who = character; gen = generation; tokenGen = api.TokenGeneration; connectionGen = api.ConnectionGeneration; device = api.DeviceId; account = api.AccountId; started = monotonicMs(); inFlight = true;
            cts?.Dispose();
            cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)); ct = cts.Token;
        }
        LicenseAnswer? got = null; string? fail = null; string net = "Conectado";
        try { got = await api.LicenseAsync(who, ct); }
        catch (CompanionApiException e) { fail = e.Status is 401 or 403 ? "El servidor rechazó el dispositivo" : $"El servidor no respondió bien ({e.Message})"; net = e.Status is 401 or 403 ? "Dispositivo rechazado" : "Error del servidor"; }
        catch (Exception e) when (e is FormatException or KeyNotFoundException or InvalidOperationException or JsonException) { fail = "Respuesta de licencia no válida"; net = "Respuesta no válida"; }
        catch (HtmlAnswerException e) { fail = e.Message; net = "Página en lugar de datos"; }
        catch (Exception) { fail = "Sin conexión con el servidor de licencias"; net = "Sin conexión"; }
        lock (gate)
        {
            if (gen != generation) return;
            if (tokenGen != api.TokenGeneration || connectionGen != api.ConnectionGeneration || !api.Online || device != api.DeviceId || account != api.AccountId) { Invalidate(); return; }
            inFlight = false; lastCheck = DateTime.Now; connectivity = net;
            long now = monotonicMs();
            if (got is null) { answer = null; validUntilMs = 0; error = fail; nextCheckMs = now + 15_000; return; }
            string? mismatch = got.DeviceId != device ? "otro dispositivo"
                : got.AccountId != account ? "otra cuenta"
                : !who.SameAs(got.Character) ? "otro personaje" : null;
            if (mismatch is not null) { answer = null; validUntilMs = 0; error = $"La respuesta corresponde a {mismatch}: Plus desactivado"; nextCheckMs = now + 15_000; return; }
            error = null; answer = got;
            int ttl = Math.Clamp(got.RecheckAfterSeconds, 1, 60);
            long life = ttl * 1000L;
            if (got.ExpiresUtc is DateTime until) life = Math.Min(life, (long)(until - got.ServerUtc).TotalMilliseconds);
            // Count transport latency conservatively; a slow answer cannot extend either deadline.
            validUntilMs = started + Math.Max(0, life);
            nextCheckMs = now + Math.Max(1000, ttl * 1000L - 10_000);
        }
    }

    /// <summary>Whether a Plus feature ("companionPvp", "companionMinimap") is on for the character in play right now.</summary>
    public bool Has(string feature)
    {
        lock (gate)
        {
            if (answer is not { Status: "active", Plan: "plus", Reason: "active", LicenseId: not null } a || character is null || !character.SameAs(a.Character) || !api.Paired || !api.Online
                || api.DeviceId != a.DeviceId || api.AccountId != a.AccountId
                || api.TokenGeneration != tokenGeneration || api.ConnectionGeneration != connectionGeneration || monotonicMs() >= validUntilMs) return false;
            if (a.ExpiresUtc is not DateTime until || a.ServerUtc >= until) return false;
            return feature switch { "companionPvp" => a.CompanionPvp, "companionMinimap" => a.CompanionMinimap, _ => false };
        }
    }

    public LicenseView View()
    {
        lock (gate)
        {
            bool pvp = Has("companionPvp"), map = Has("companionMinimap");
            string net = !api.Paired ? "Sin vincular" : !api.Online ? "Sin conexión" : connectivity;
            if (!api.Paired) return new("unpaired", "Sin vincular", "Vincula este dispositivo con tu cuenta (Mi cuenta → Aplicación de escritorio) para usar Companion Plus.", character, false, false, null, lastCheck, net);
            if (character is null) return new("no-character", "Personaje sin identificar", (whyNoCharacter ?? "La captura aún no identificó tu personaje.") + " Mientras tanto, Plus queda desactivado; el medidor gratuito sigue funcionando.", null, false, false, null, lastCheck, net);
            if (!api.Online) return new("error", "Plus desactivado", "Sin conexión con tu cuenta. Plus no tiene periodo de gracia sin conexión.", character, false, false, null, lastCheck, net);
            if (error is not null) return new("error", "Plus desactivado", error + ". Se reintentará en unos segundos.", character, false, false, null, lastCheck, net);
            if (answer is not { } a) return new("checking", "Comprobando licencia…", "Consultando la licencia de este personaje.", character, false, false, null, lastCheck, net);
            if (a.Status == "active" && !(pvp || map) && monotonicMs() >= validUntilMs)
                return new("stale", "Comprobando licencia…", "La autorización de 60 s caducó; se está renovando.", character, false, false, a.ExpiresUtc, lastCheck, net);
            var (state, title, detail) = a.Reason switch
            {
                "active" => ("active", "Activo", "Companion Plus para este personaje" + (a.Source == "mercado_pago" ? " · pagado con Mercado Pago" : a.Source == "admin-grant" ? " · cortesía" : a.Source == "discount" ? " · descuento" : "") + ". Renovación manual desde la web."),
                "email_unverified" => ("email", "Correo pendiente", "Confirma el correo de tu cuenta en la web para activar Plus."),
                "character_unverified" => ("ownership", "Propiedad pendiente", "Este personaje no tiene una prueba de propiedad verificada por el servidor. Pagar o elegir un nombre no la sustituye."),
                "account_inactive" => ("suspended", "Suspendido", "La cuenta no está activa. Escribe a soporte desde la web."),
                "license_revoked" => ("suspended", "Suspendido", "La licencia de este personaje fue revocada."),
                "no_active_license" => (a.LicenseId is null ? "none" : "expired", a.LicenseId is null ? "Sin suscripción" : "Expirado", a.LicenseId is null
                    ? "Este personaje no tiene Companion Plus. Contrátalo en la web: Cuenta → Suscripción (5.990 CLP por un mes, renovación manual)."
                    : "El periodo de Companion Plus de este personaje terminó. Renuévalo en la web: Cuenta → Suscripción."),
                "character_required" => ("no-character", "Personaje sin identificar", "El servidor no recibió un personaje válido."),
                _ => ("inactive", "Plus desactivado", "Estado de licencia: " + a.Reason),
            };
            return new(state, title, detail, character, pvp, map, a.ExpiresUtc, lastCheck, net);
        }
    }
}
