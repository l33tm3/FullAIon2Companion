using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullAIon2Companion;

/// <summary>The official reference of a character (region, server, encrypted public id). Not the capture's ObjectId.</summary>
public sealed record CharacterRef(
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("serverId")] int ServerId,
    [property: JsonPropertyName("characterId")] string CharacterId)
{
    public bool SameAs(CharacterRef? other) => other is not null && Region == other.Region && ServerId == other.ServerId && CharacterId == other.CharacterId;
}
/// <summary>A pending title verification: swap the equipped title of one category in the game and the server confirms the
/// change on the official AION 2 profile (docs/companion-api.md, title-challenge).</summary>
public sealed record TitleChallenge(string ChallengeId, CharacterRef Character, string CharacterName, string CategoryLabel, string CurrentTitle, string Status, DateTime? ExpiresAtUtc);
public sealed record TitleCheck(TitleChallenge Challenge, bool Verified, string Message);
/// <summary>A verified character of the account and the name stored when it was verified (empty for older links).</summary>
public sealed record LinkedCharacter(CharacterRef Character, string CharacterName, string Method);
/// <summary>What the last successful <c>pull</c> said.</summary>
public sealed record PullState(DateTime AtUtc, int LastSequence, IReadOnlyList<CharacterRef> LinkedCharacters,
    bool VerificationReady, string PlanName, bool CompanionPvp, bool CompanionMinimap, DateTime? PlanExpiresUtc = null,
    IReadOnlyList<Announcement>? Announcements = null, IReadOnlyDictionary<string, Release>? Releases = null, bool Cached = false,
    IReadOnlyList<TitleChallenge>? TitleChallenges = null, IReadOnlyList<LinkedCharacter>? LinkedDetails = null, int CharacterLimit = 3);
/// <summary>A notice from the administration (plain text, never HTML), shown between its start and end.</summary>
public sealed record Announcement(string Id, string Title, string Body, string Level, DateTime? StartsUtc, DateTime? EndsUtc)
{
    public bool ActiveAt(DateTime utc) => (StartsUtc is null || StartsUtc <= utc) && (EndsUtc is null || EndsUtc > utc);
}
/// <summary>A published build of one channel. Url is relative to the site; the file is checked against Bytes and Sha256.</summary>
public sealed record Release(string Id, string Version, string Channel, string Notes, string Sha256, long Bytes, DateTime? CreatedUtc, string Url);
/// <summary>What the voluntary diagnostics report (contract: docs/claude-companion-operations-prompt.md). No packets, tokens, IPs or paths.</summary>
public sealed record Diagnostics(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("captureState")] string CaptureState,
    [property: JsonPropertyName("syncState")] string SyncState,
    [property: JsonPropertyName("errorCode")] string ErrorCode)
{
    static readonly System.Text.RegularExpressions.Regex Code = new(@"^[A-Z0-9_.-]{1,80}$");
    /// <summary>The same rules as the server, so an invalid report is never sent.</summary>
    [JsonIgnore] public bool Valid => Channel is "stable" or "beta" && CaptureState is "idle" or "capturing" or "unavailable" or "error"
        && SyncState is "ok" or "pending" or "error" && (ErrorCode.Length == 0 || Code.IsMatch(ErrorCode))
        && System.Text.RegularExpressions.Regex.IsMatch(Version, @"^\d+\.\d+\.\d+(-[a-z0-9.-]+)?$");
}
/// <summary>An answer of the API that is not a success: its HTTP status, the server's message and its Retry-After.</summary>
public sealed class CompanionApiException(int status, string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public int Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
/// <summary>
/// A web page came back instead of the API's JSON, so the request never reached the API: the hosting's anti-bot challenge
/// (Imunify360 shows one to IPs on its grey list: a client on 2026-10-14, HTTP 2xx «One moment, please…»), a captive
/// portal or a proxy. A network failure for every caller: never a revocation, a rejected code or a malformed answer.
/// </summary>
public sealed class HtmlAnswerException(int status, string? title)
    : HttpRequestException(Describe(status, title), null, (HttpStatusCode)status)
{
    public string? Title { get; } = title;
    static string Describe(int status, string? title) => "El servidor de seguridad del hosting o tu red devolvió una página en lugar de datos (HTTP " + status + (string.IsNullOrEmpty(title) ? ")" : $", «{title}»)");
}
/// <summary>The three calls of the title verification (an interface so <see cref="TitleVerification"/> can be tested offline).</summary>
public interface ITitleApi
{
    Task<TitleChallenge> StartTitleChallengeAsync(CharacterRef character, CancellationToken ct = default);
    Task<TitleCheck> CheckTitleAsync(string challengeId, CancellationToken ct = default);
    Task CancelTitleAsync(string challengeId, CancellationToken ct = default);
}
/// <summary>Where the device stands with the account: three different things, never one implying another.</summary>
public enum CloudState { NotPaired, TokenStored, Connected, Offline, Revoked }

/// <summary>
/// The account side of FullAIon2Companion (contract: docs/companion-api.md). A device token from <c>redeem</c> kept in
/// Windows Credential Manager and sent only as Bearer; <c>pull</c> every 20 s proves it is still valid and brings the sequence,
/// the verified characters, the pending title verifications and the plan. A 401 anywhere clears the local session and stops every upload. Telemetry goes in
/// serialized batches kept until the server acknowledges them; a lost answer is reconciled through <c>pull.lastSequence</c>.
/// A web page instead of JSON throws <see cref="HtmlAnswerException"/> and is noted in network.log (never a token or a body).
/// </summary>
public sealed class CompanionClient : ILicenseApi, ITitleApi
{
    const string Base = "https://governanceai.me/api/companion.php?action=";
    /// <summary>Every request names the app: firewalls treat a request without a User-Agent as a bot.</summary>
    public static string UserAgent => $"FullAIon2Companion/{AppVersion.Current} (Windows)";
    static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    readonly HttpClient http;
    readonly string cachePath, logPath;
    readonly ITokenStore store;
    readonly Action saveSettings;
    readonly ConcurrentQueue<object> queue = new();
    readonly SemaphoreSlim telemetryGate = new(1, 1);
    readonly AppSettings settings;
    string? token;
    int? sequence;
    List<object>? inflight;
    int inflightSequence;
    DateTime retryNotBefore = DateTime.MinValue;
    int failures;
    public string Status { get; private set; } = "Solo en este equipo · sin vincular";
    public CloudState State { get; private set; } = CloudState.NotPaired;
    public PullState? LastPull { get; private set; }
    /// <summary>A token is stored (not proof that the server still accepts it: see <see cref="State"/>).</summary>
    public bool Paired => token is not null;
    /// <summary>The device id of the stored token (from redeem and every pull).</summary>
    public string? DeviceId { get; private set; }
    public string? AccountId { get; private set; }
    public bool Online => State == CloudState.Connected;
    public int ConnectionGeneration { get; private set; }
    /// <summary>Bumps whenever the token changes, so a license answer for the previous token is never used.</summary>
    public int TokenGeneration { get; private set; }
    /// <summary>Raised after a 401 cleared the session (revoked from the web, password reset).</summary>
    public event Action? Revoked;

    public CompanionClient(AppSettings settings) : this(settings, new HttpClientHandler(), new CredentialTokenStore(Path.Combine(Program.DataDirectory, "device.dpapi")), Program.DataDirectory, settings.Save) { }
    /// <summary>For the offline tests: a fake transport, a throwaway token file (the pull cache goes next to it) and no
    /// writes to the real settings or the real credential.</summary>
    internal CompanionClient(AppSettings settings, HttpMessageHandler handler, string tokenPath, Action saveSettings)
        : this(settings, handler, new FileTokenStore(tokenPath), Path.GetDirectoryName(tokenPath)!, saveSettings) { }
    internal CompanionClient(AppSettings settings, HttpMessageHandler handler, ITokenStore store, string folder, Action saveSettings)
    {
        this.settings = settings; this.store = store; this.saveSettings = saveSettings;
        cachePath = Path.Combine(folder, "cloud-progress.json");
        logPath = Path.Combine(folder, "network.log");
        http = new(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        try
        {
            if (store.Read() is { Length: > 0 } stored)
            {
                token = stored;
                State = CloudState.TokenStored;
                Status = "Token guardado · comprobando conexión con tu cuenta";
                // Cached progress is display only; licenses always require a fresh authenticated response.
                if (File.Exists(cachePath)) LastPull = ParsePull(JsonDocument.Parse(File.ReadAllText(cachePath)).RootElement.Clone(), cached: true);
            }
        }
        catch { Status = "Vuelve a vincular este dispositivo"; }
    }

    /// <summary>UTC with "Z" and at most three decimals, as the API requires ("o" gives seven and is rejected).</summary>
    public static string Utc(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public async Task Pair(string code)
    {
        code = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (code.Length != 12 || !code.All(Uri.IsHexDigit)) throw new InvalidOperationException("El código debe tener 12 caracteres");
        using var request = new HttpRequestMessage(HttpMethod.Post, Base + "redeem") { Content = Body(new { code }) };
        using var response = await http.SendAsync(request);
        var body = await Read(response, "redeem");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Error(body) ?? $"No se pudo vincular (HTTP {(int)response.StatusCode})");
        string? receivedToken = Str(body, "accessToken"), receivedDevice = Str(body, "deviceId"), receivedAccount = Str(body, "accountId");
        if (receivedToken is null || !System.Text.RegularExpressions.Regex.IsMatch(receivedToken, "^[a-f0-9]{64}$")
            || receivedDevice is null || !System.Text.RegularExpressions.Regex.IsMatch(receivedDevice, "^[a-f0-9]{32}$")
            || receivedAccount is null || !System.Text.RegularExpressions.Regex.IsMatch(receivedAccount, "^[a-f0-9]{32}$")) throw new FormatException("El servidor respondió sin los datos de la vinculación. Genera un código nuevo y vuelve a intentarlo.");
        store.Write(receivedToken);
        token = receivedToken; DeviceId = receivedDevice; AccountId = receivedAccount; TokenGeneration++;
        LastPull = null;
        sequence = null; inflight = null;
        State = CloudState.TokenStored;
        await Pull();
    }
    /// <summary>Forgets the token on this PC. It does not revoke it on the server: that is done from the web.</summary>
    public void Disconnect()
    {
        Clear();
        Status = "Desconectado en este equipo · para revocar el token usa Mi cuenta → Aplicación de escritorio en la web";
    }
    void Clear()
    {
        token = null; sequence = null; inflight = null; LastPull = null; DeviceId = null; AccountId = null; TokenGeneration++;
        settings.ShareTelemetry = false; saveSettings();
        try { store.Delete(); } catch { }
        try { if (File.Exists(cachePath)) File.Delete(cachePath); } catch { }
        while (queue.TryDequeue(out _)) { }
        State = CloudState.NotPaired;
    }

    /// <summary><c>GET pull</c>: validates the token, updates the device's activity and brings sequence, challenges and plan.</summary>
    public async Task<PullState> Pull(CancellationToken ct = default)
    {
        try
        {
            var body = await Send(HttpMethod.Get, "pull", null, ct);
            var pull = ParsePull(body, cached: false);
            string device = Str(body, "deviceId") ?? throw new FormatException("deviceId"), account = Str(body, "accountId") ?? throw new FormatException("accountId");
            if (!System.Text.RegularExpressions.Regex.IsMatch(device, "^[a-f0-9]{32}$") || !System.Text.RegularExpressions.Regex.IsMatch(account, "^[a-f0-9]{32}$")
                || DeviceId is not null && DeviceId != device || AccountId is not null && AccountId != account)
                throw new FormatException("La cuenta o el dispositivo no coinciden");
            LastPull = pull; DeviceId = device; AccountId = account;
            // Resume the sequence from the server, never behind it (a batch in flight keeps its number until answered).
            if (inflight is null) sequence = Math.Max(sequence ?? 0, pull.LastSequence);
            if (State != CloudState.Connected) ConnectionGeneration++;
            State = CloudState.Connected;
            Status = "Dispositivo conectado";
            try { File.WriteAllText(cachePath, body.GetRawText()); } catch { }
            return pull;
        }
        catch (Exception ex) when (ex is not CompanionApiException { Status: 401 })
        {
            if (State != CloudState.Revoked && token is not null) { if(State != CloudState.Offline) ConnectionGeneration++; State = CloudState.Offline; Status = ex is HtmlAnswerException ? ex.Message : "Sin conexión con tu cuenta · se reintentará"; }
            throw;
        }
    }

    static PullState ParsePull(JsonElement body, bool cached)
    {
        var sub = body.TryGetProperty("subscription", out var sv) && sv.ValueKind == JsonValueKind.Object ? sv : default;
        DateTime? expires = sub.ValueKind == JsonValueKind.Object && DateTime.TryParse(Str(sub, "expiresAt"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var e) ? e : null;
        var announcements = new List<Announcement>();
        if (body.TryGetProperty("announcements", out var an) && an.ValueKind == JsonValueKind.Array)
            foreach (var a in an.EnumerateArray())
                if (a.ValueKind == JsonValueKind.Object && Str(a, "id") is { Length: > 0 } id)
                    announcements.Add(new(id, Str(a, "title") ?? "", Str(a, "body") ?? "", Str(a, "level") ?? "info", Time(a, "startsAt"), Time(a, "endsAt")));
        var releases = new Dictionary<string, Release>();
        if (body.TryGetProperty("releases", out var rl) && rl.ValueKind == JsonValueKind.Object)
            foreach (var ch in rl.EnumerateObject())
                if (ch.Value.ValueKind == JsonValueKind.Object && Str(ch.Value, "version") is { Length: > 0 } version && Str(ch.Value, "url") is { } url)
                    releases[ch.Name] = new(Str(ch.Value, "id") ?? "", version, Str(ch.Value, "channel") ?? ch.Name, Str(ch.Value, "notes") ?? "", (Str(ch.Value, "sha256") ?? "").ToLowerInvariant(),
                        ch.Value.TryGetProperty("bytes", out var b) && b.TryGetInt64(out long bytes) ? bytes : 0, Time(ch.Value, "createdAt"), url);
        return new(DateTime.UtcNow, body.TryGetProperty("lastSequence", out var ls) && ls.TryGetInt32(out int last) ? last : 0,
            body.TryGetProperty("linkedCharacters", out var lc) && lc.ValueKind == JsonValueKind.Array ? lc.EnumerateArray().Select(ParseRef).ToList() : [],
            body.TryGetProperty("verificationReady", out var vr) && vr.ValueKind == JsonValueKind.True,
            sub.ValueKind == JsonValueKind.Object ? Str(sub, "planName") ?? "" : "",
            Feature(body, "companionPvp"), Feature(body, "companionMinimap"), expires, announcements, releases, cached,
            body.TryGetProperty("titleChallenges", out var tc) && tc.ValueKind == JsonValueKind.Array ? tc.EnumerateArray().Where(HasCharacter).Select(ParseTitle).ToList() : [],
            body.TryGetProperty("linkedDetails", out var ld) && ld.ValueKind == JsonValueKind.Array
                ? ld.EnumerateArray().Where(HasCharacter).Select(l => new LinkedCharacter(ParseRef(l.GetProperty("character")), Str(l, "characterName") ?? "", Str(l, "method") ?? "")).ToList() : [],
            body.TryGetProperty("characterLimit", out var cl) && cl.TryGetInt32(out int limit) && limit > 0 ? limit : 3);
    }
    static bool HasCharacter(JsonElement e) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("character", out var c) && c.ValueKind == JsonValueKind.Object;
    static TitleChallenge ParseTitle(JsonElement e) => new(Str(e, "challengeId") ?? "", ParseRef(e.GetProperty("character")), Str(e, "characterName") ?? "",
        Str(e, "categoryLabel") ?? "", Str(e, "currentTitle") ?? "", Str(e, "status") ?? "", Time(e, "expiresAt"));
    static DateTime? Time(JsonElement e, string key) => DateTime.TryParse(Str(e, key), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
    /// <summary>
    /// <c>POST license</c> for the character in play. The only thing that enables Plus (see <see cref="LicenseGate"/>); the
    /// pull's subscription flags are informative and never grant anything. A malformed answer throws.
    /// </summary>
    public async Task<LicenseAnswer> LicenseAsync(CharacterRef character, CancellationToken ct)
        => LicenseAnswer.Parse(await Send(HttpMethod.Post, "license", new { character }, ct));

    DateTime lastDiagnostics = DateTime.MinValue;
    Diagnostics? lastSent;
    public string DiagnosticsStatus { get; private set; } = "";
    /// <summary>
    /// Voluntary diagnostics (<c>POST diagnostics</c>): sent when the state changes, else every 30 s; never more than
    /// 6 a minute (the server's limit). Invalid reports are not sent.
    /// </summary>
    public async Task SendDiagnosticsAsync(Diagnostics d, CancellationToken ct = default)
    {
        if (!Paired || !d.Valid) return;
        bool changed = d != lastSent;
        if (DateTime.UtcNow - lastDiagnostics < TimeSpan.FromSeconds(changed ? 10 : 30)) return;
        lastDiagnostics = DateTime.UtcNow;
        try { await Send(HttpMethod.Post, "diagnostics", d, ct); lastSent = d; DiagnosticsStatus = $"Diagnóstico enviado · {DateTime.Now:HH:mm:ss}"; }
        catch (CompanionApiException e) { DiagnosticsStatus = "Diagnóstico rechazado: " + e.Message; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { DiagnosticsStatus = "Diagnóstico pendiente · sin conexión"; }
    }

    /// <summary>
    /// Downloads a release to the updates folder and checks its size and SHA-256 before handing back the path. Only the
    /// site's own download address is accepted (a relative URL to operations.php?action=download&amp;id=…); a mismatch
    /// deletes the file. The checksum proves integrity, not who published it. Nothing is executed here.
    /// </summary>
    public async Task<string> DownloadReleaseAsync(Release r, string folder, CancellationToken ct = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.Url, @"^/api/operations\.php\?action=download&id=[a-f0-9]{32}$"))
            throw new InvalidOperationException("La dirección de descarga no es la del sitio: no se descarga.");
        if (r.Bytes <= 0 || !System.Text.RegularExpressions.Regex.IsMatch(r.Sha256, "^[0-9a-f]{64}$"))
            throw new InvalidOperationException("La versión publicada no trae tamaño o huella SHA-256 válidos.");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"FullAIon2Companion-{System.Text.RegularExpressions.Regex.Replace(r.Version, "[^0-9A-Za-z.-]", "")}.exe"), part = file + ".part";
        using (var response = await http.GetAsync("https://governanceai.me" + r.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml") throw Page("download", response, await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode == HttpStatusCode.NotFound ? "La descarga ya no está disponible; se conserva tu versión actual." : $"No se pudo descargar (HTTP {(int)response.StatusCode}).");
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(part);
            await input.CopyToAsync(output, ct);
        }
        var info = new FileInfo(part);
        string hash;
        using (var stream = File.OpenRead(part)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (info.Length != r.Bytes || hash != r.Sha256)
        {
            File.Delete(part);
            throw new InvalidOperationException($"El archivo descargado no coincide con la versión publicada (tamaño o SHA-256): se descartó.");
        }
        File.Move(part, file, true);
        return file;
    }

    // Support tickets from the app (companion.php support-*): the same tickets as the website's support page, authenticated by
    // the device token. Nothing from the capture is sent; the message carries only what the user writes plus the app version
    // and capture state.
    public Task<JsonElement> SupportTicketsAsync(CancellationToken ct = default) => Send(HttpMethod.Get, "support-tickets", null, ct);
    public Task<JsonElement> SupportTicketAsync(string id, CancellationToken ct = default) => Send(HttpMethod.Get, "support-ticket", null, ct, "&id=" + Uri.EscapeDataString(id));
    public Task<JsonElement> SupportCreateAsync(string subject, string category, string message, CharacterRef? character, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "support-create", character is null ? new { subject, category, message } : new { subject, category, message, character }, ct);
    public Task<JsonElement> SupportReplyAsync(string id, string message, CancellationToken ct = default) => Send(HttpMethod.Post, "support-reply", new { id, message }, ct);

    /// <summary><c>POST title-challenge</c>: which equipped title to swap for this character (or the one already pending).</summary>
    public async Task<TitleChallenge> StartTitleChallengeAsync(CharacterRef character, CancellationToken ct = default)
        => ParseTitle(await Send(HttpMethod.Post, "title-challenge", new { character }, ct));
    /// <summary><c>POST title-check</c>: the server reads the official profile again; Verified once the title changed.</summary>
    public async Task<TitleCheck> CheckTitleAsync(string challengeId, CancellationToken ct = default)
    {
        var body = await Send(HttpMethod.Post, "title-check", new { challengeId }, ct);
        return new(ParseTitle(body), body.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True, Str(body, "message") ?? "");
    }
    public async Task CancelTitleAsync(string challengeId, CancellationToken ct = default)
        => await Send(HttpMethod.Post, "title-cancel", new { challengeId }, ct);

    public string PetsStatus { get; private set; } = "Sin subir";
    /// <summary>The last upload failed (no connection, server error): the refresh loop retries every 5 minutes.</summary>
    public bool PetsPending { get; private set; }
    public bool PetsUploadedOnce { get; private set; }
    /// <summary>Sends a character's pet collection and its daily history to the account (action "pets").</summary>
    public async Task UploadPets(PetCollectionView v)
    {
        if (!Paired) { PetsStatus = "Vincula el dispositivo en Mi cuenta para guardarlo en la web"; return; }
        try
        {
            await Send(HttpMethod.Post, "pets", new
            {
                character = new { name = v.Character, serverId = v.ServerId, region = v.Region },
                capturedAt = v.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                listAt = v.ListAt?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                active = v.Active, pets = v.Pets.Select(p => new[] { p.Id, p.Level, p.Progress }),
                history = v.History.TakeLast(400).Select(h => new object[] { h.Date, h.Owned, h.L1, h.L2, h.L3, h.Souls })
            }, CancellationToken.None);
            PetsStatus = $"Subida a tu cuenta · {DateTime.Now:HH:mm}"; PetsPending = false; PetsUploadedOnce = true;
        }
        catch (CompanionApiException e) { PetsStatus = $"No se pudo subir · {e.Message}" + (e.Status == 401 ? "" : " · reintento en 5 min"); PetsPending = e.Status != 401; }
        catch { PetsStatus = "Sin conexión · reintento en 5 min"; PetsPending = true; }
    }

    /// <summary>Queues a telemetry event (only with the user's consent and a stored token).</summary>
    public void Add(object e) { if (!settings.ShareTelemetry || !Paired) return; queue.Enqueue(e); while (queue.Count > 500) queue.TryDequeue(out _); }
    /// <summary>Sends one batch of up to 50 events. Serialized; the batch is kept until the server acknowledges its sequence.</summary>
    public async Task Flush()
    {
        if (!Paired || !settings.ShareTelemetry || DateTime.UtcNow < retryNotBefore || !await telemetryGate.WaitAsync(0)) return;
        try
        {
            if (sequence is null) await Pull();
            if (inflight is null)
            {
                var batch = new List<object>();
                while (batch.Count < 50 && queue.TryDequeue(out var e)) batch.Add(e);
                if (batch.Count == 0) return;
                inflight = batch; inflightSequence = (sequence ?? 0) + 1;
            }
            try
            {
                await Send(HttpMethod.Post, "telemetry", new { sequence = inflightSequence, events = inflight }, CancellationToken.None);
                sequence = inflightSequence; inflight = null; failures = 0;
                Status = "Telemetría enviada · captura sin verificar";
            }
            catch (CompanionApiException e) when (e.Status == 409)
            {
                // Sequence or event already received: the answer to an earlier try was lost, or the numbering fell behind.
                // Either way the batch is done: if lastSequence reached it, it did arrive; if not, an eventId in it was
                // already stored and resending it can never succeed. Numbering resumes from the server's.
                var pull = await Pull();
                inflight = null;
                sequence = pull.LastSequence;
                Status = pull.LastSequence >= inflightSequence ? "Telemetría enviada · captura sin verificar" : "Lote con eventos ya recibidos descartado";
            }
            catch (CompanionApiException e) when (e.Status == 429) { retryNotBefore = DateTime.UtcNow + (e.RetryAfter ?? TimeSpan.FromSeconds(60)); Status = "Demasiadas solicitudes · esperando para reenviar"; }
            catch (CompanionApiException e) when (e.Status == 400) { inflight = null; Status = "Lote de telemetría rechazado: " + e.Message; }
        }
        catch (CompanionApiException e) when (e.Status == 401) { }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CompanionApiException)
        {
            // 503 or no connection: the batch stays; growing wait (5 s, 10 s … 5 min).
            failures++;
            retryNotBefore = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(6, failures - 1))));
            Status = "Sin conexión · el lote se reenviará";
        }
        finally { telemetryGate.Release(); }
    }

    async Task<JsonElement> Send(HttpMethod method, string action, object? body, CancellationToken ct, string query = "")
    {
        string current = token ?? throw new CompanionApiException(401, "Vincula el dispositivo primero");
        int generation = TokenGeneration;
        using var request = new HttpRequestMessage(method, Base + action + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current);
        if (body is not null) request.Content = Body(body);
        using var response = await http.SendAsync(request, ct);
        // A web page (even one with status 401) is not the API's answer: it throws before the revocation below.
        var json = await Read(response, action);
        if (generation != TokenGeneration || token != current) throw new OperationCanceledException("Respuesta de una vinculación anterior descartada");
        if (response.IsSuccessStatusCode) return json;
        int code = (int)response.StatusCode;
        string message = Error(json) ?? $"HTTP {code}";
        if (code == 401 && token == current)
        {
            // Revoked from the web, or the account's password changed: forget the session and stop all uploads.
            Clear();
            State = CloudState.Revoked;
            Status = "El dispositivo fue desconectado desde tu cuenta · genera un código nuevo en la web para volver a vincular";
            Revoked?.Invoke();
        }
        throw new CompanionApiException(code, message, response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is DateTimeOffset d ? d - DateTimeOffset.UtcNow : null));
    }
    static StringContent Body(object body) => new(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
    /// <summary>The answer as JSON (default when it is empty or not JSON); a web page throws <see cref="HtmlAnswerException"/>.</summary>
    async Task<JsonElement> Read(HttpResponseMessage response, string action)
    {
        string text = await response.Content.ReadAsStringAsync();
        // JSON never starts with '<'; an HTML content type with any other body is a page too.
        if (text.AsSpan().TrimStart().StartsWith("<") || response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            throw Page(action, response, text);
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch { return default; }
    }
    HtmlAnswerException Page(string action, HttpResponseMessage response, string html)
    {
        var page = new HtmlAnswerException((int)response.StatusCode, PageTitle(html));
        LogPage(action, response, page.Title, html.Length);
        return page;
    }
    /// <summary>The page's &lt;title&gt;, decoded and cut to 80 characters (null without one).</summary>
    public static string? PageTitle(string html)
    {
        var m = System.Text.RegularExpressions.Regex.Match(html, @"<title\b[^>]*>(.*?)</title\s*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return null;
        string title = System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(m.Groups[1].Value), @"[\s\p{C}]+", " ").Trim();
        return title.Length == 0 ? null : title.Length > 80 ? title[..79] + "…" : title;
    }
    readonly Dictionary<string, DateTime> logged = [];
    /// <summary>
    /// One line per page received (the same action, status and title at most every 10 minutes) for support: the action, the
    /// status, the content type, the title, the Server header and the size. Never the request, its token, the URL or the body.
    /// </summary>
    void LogPage(string action, HttpResponseMessage response, string? title, int length)
    {
        string key = action + "|" + (int)response.StatusCode + "|" + title;
        lock (logged)
        {
            if (logged.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(10)) return;
            logged[key] = DateTime.UtcNow;
        }
        static string Clean(string? s) { string c = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\p{C}+", " ").Trim(); return c.Length > 80 ? c[..80] : c; }
        string line = $"{Utc(DateTime.UtcNow)} {action} HTTP {(int)response.StatusCode} {Clean(response.Content.Headers.ContentType?.MediaType)} «{Clean(title)}» server={Clean(string.Join(" ", response.Headers.Server.Select(s => s.ToString())))} bytes={length} {UserAgent}\n";
        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 256 * 1024) File.Move(logPath, logPath + ".1", true);
            File.AppendAllText(logPath, line);
        }
        catch { }
    }
    static string? Error(JsonElement e) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("error", out var m) ? m.GetString() : null;
    static string? Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static bool Feature(JsonElement body, string name) => body.TryGetProperty("subscription", out var s) && s.TryGetProperty("features", out var f) && f.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    static CharacterRef ParseRef(JsonElement e) => new(Str(e, "region") ?? "", e.TryGetProperty("serverId", out var s) && s.TryGetInt32(out int id) ? id : int.TryParse(Str(e, "serverId"), out int parsed) ? parsed : 0, Str(e, "characterId") ?? "");
}
