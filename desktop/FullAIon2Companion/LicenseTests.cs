using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>Offline licenses: fake authenticated service, monotonic clock, and isolated Windows credentials.</summary>
public static class LicenseTests
{
    static readonly CharacterRef Darkni = new("la", 2401, "official_darkni_encrypted_id=");
    static readonly CharacterRef Other = new("la", 2401, "official_other_encrypted_id=");
    const string Account = "11111111111111111111111111111111", Device = "22222222222222222222222222222222";
    static readonly DateTime Server = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
    static LicenseAnswer Active(CharacterRef? character = null) => new("33333333333333333333333333333333", Account, Device, character ?? Darkni,
        "plus", "active", "active", "mercado_pago", Server.AddMonths(1), Server, 60, true, true);
    sealed class Api : ILicenseApi
    {
        public string? DeviceId { get; set; } = Device;
        public string? AccountId { get; set; } = Account;
        public int TokenGeneration { get; set; } = 1;
        public bool Paired { get; set; } = true;
        bool online = true;
        public bool Online { get => online; set { if(value != online) ConnectionGeneration++; online = value; } }
        public int ConnectionGeneration { get; private set; }
        public int Calls;
        public Func<CharacterRef, CancellationToken, Task<LicenseAnswer>> Reply = (c, _) => Task.FromResult(Active(c));
        public Task<LicenseAnswer> LicenseAsync(CharacterRef c, CancellationToken ct) { Calls++; return Reply(c, ct); }
    }
    sealed class Rig : IDisposable
    {
        public long Ms;
        public readonly Api Api = new();
        public readonly LicenseGate Gate;
        public Rig() { Gate = new(Api, () => Ms); Gate.SetCharacter(Darkni); }
        public bool Allowed => Gate.Has("companionPvp") || Gate.Has("companionMinimap");
        public void Dispose() => Gate.Dispose();
    }
    sealed class MemoryStore : ITokenStore
    {
        public string? Token = new('a', 64);
        public string? Read() => Token;
        public void Write(string token) => Token = token;
        public void Delete() => Token = null;
    }
    sealed class Transport : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "";
        public bool Offline;
        public HttpMethod? Method;
        public string? RequestBody, Auth;
        public TaskCompletionSource<HttpResponseMessage>? Pending;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method; Auth = request.Headers.Authorization?.ToString();
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (Offline) throw new HttpRequestException("offline fake");
            if (Pending is {} p) return await p.Task;
            return new(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
    static string Json(LicenseAnswer? a = null) { a ??= Active(); return JsonSerializer.Serialize(new {
        licenseId = a.LicenseId, accountId = a.AccountId, deviceId = a.DeviceId, character = a.Character,
        plan = a.Plan, status = a.Status, reason = a.Reason, source = a.Source,
        expiresAt = a.ExpiresUtc is {} until ? CompanionClient.Utc(until) : null, serverTime = CompanionClient.Utc(a.ServerUtc),
        recheckAfterSeconds = a.RecheckAfterSeconds, offlineGraceSeconds = 0,
        features = new { companionPvp = a.CompanionPvp, companionMinimap = a.CompanionMinimap }
    }); }
    const string Pull = "{\"accountId\":\"" + Account + "\",\"deviceId\":\"" + Device + "\",\"lastSequence\":0,\"activeChallenges\":[],\"linkedCharacters\":[],\"verificationReady\":false,\"subscription\":{\"features\":{\"companionPvp\":true,\"companionMinimap\":true}}}";

    public static async Task<(bool Ok, List<object> Results)> Run()
    {
        var results = new List<object>();
        async Task Case(string name, Func<Task> test) { string? failure = null; try { await test(); } catch(Exception e) { failure = e.GetType().Name + ": " + e.Message; } results.Add(new { name, ok = failure is null, failure }); }
        static void Check(bool ok, string message = "condición falló") { if (!ok) throw new InvalidOperationException(message); }
        await Case("Darkni verificado y pagado: ambos permisos", async () => { using var r = new Rig(); await r.Gate.TickAsync(); Check(r.Gate.Has("companionPvp") && r.Gate.Has("companionMinimap")); Check(!r.Gate.Has("isPlus")); });
        foreach (var (label, change) in new (string, Func<LicenseAnswer, LicenseAnswer>)[] {
            ("otro personaje", a => a with { Character = Other }), ("otra cuenta desde primera respuesta", a => a with { AccountId = new('4', 32) }),
            ("otro dispositivo", a => a with { DeviceId = new('5', 32) }), ("expirada", a => a with { ExpiresUtc = Server }),
            ("sin caducidad", a => a with { ExpiresUtc = null }), ("sin identificador", a => a with { LicenseId = null }),
            ("plan gratuito", a => a with { Plan = "free" }), ("sin flags", a => a with { CompanionPvp = false, CompanionMinimap = false }) })
            await Case(label + " bloquea Plus", async () => { using var r = new Rig(); r.Api.Reply = (_, _) => Task.FromResult(change(Active())); await r.Gate.TickAsync(); Check(!r.Allowed); });
        foreach (string reason in new[] { "email_unverified", "character_unverified", "account_inactive", "license_revoked", "no_active_license", "payment_pending", "refunded", "charged_back", "observed" })
            await Case(reason + " bloquea Plus", async () => { using var r = new Rig(); r.Api.Reply = (_, _) => Task.FromResult(Active() with { Status = "inactive", Plan = "free", Reason = reason, CompanionPvp = false, CompanionMinimap = false }); await r.Gate.TickAsync(); Check(!r.Allowed); });
        await Case("cada flag es independiente", async () => { using var r = new Rig(); r.Api.Reply = (_, _) => Task.FromResult(Active() with { CompanionPvp = false }); await r.Gate.TickAsync(); Check(!r.Gate.Has("companionPvp") && r.Gate.Has("companionMinimap")); });
        await Case("cambio de personaje, región o servidor invalida al instante", async () => { using var r = new Rig(); foreach(var c in new[] { Other, Darkni with { Region = "eu" }, Darkni with { ServerId = 2402 } }) { r.Gate.SetCharacter(Darkni); await r.Gate.TickAsync(); Check(r.Allowed); r.Gate.SetCharacter(c); Check(!r.Allowed); } });
        await Case("identidad desconocida conserva solo modo gratuito", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Gate.SetCharacter(null); await r.Gate.TickAsync(); Check(!r.Allowed && r.Gate.View().State == "no-character"); });
        await Case("TTL monotónico vence a los 60 segundos", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Ms = 59_999; Check(r.Allowed); r.Ms = 60_000; Check(!r.Allowed); });
        await Case("caducidad del periodo corta antes del TTL", async () => { using var r = new Rig(); r.Api.Reply = (_, _) => Task.FromResult(Active() with { ExpiresUtc = Server.AddSeconds(5) }); await r.Gate.TickAsync(); r.Ms = 5000; Check(!r.Allowed); });
        await Case("consulta lenta no extiende permiso", async () => { using var r = new Rig(); r.Api.Reply = (_, _) => { r.Ms = 8000; return Task.FromResult(Active() with { ExpiresUtc = Server.AddSeconds(5) }); }; await r.Gate.TickAsync(); Check(!r.Allowed); });
        await Case("rechequeo automático antes de caducar", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Ms = 49_999; await r.Gate.TickAsync(); Check(r.Api.Calls == 1); r.Ms = 50_000; await r.Gate.TickAsync(); Check(r.Api.Calls == 2); });
        await Case("TTL del servidor nunca supera 60 segundos", async () => { using var r = new Rig(); r.Api.Reply = (_, _) => Task.FromResult(Active() with { RecheckAfterSeconds = 9999 }); await r.Gate.TickAsync(); r.Ms = 60_000; Check(!r.Allowed); });
        await Case("antes de abrir Plus se consulta otra vez", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Gate.RecheckSoon(); Check(!r.Allowed); await r.Gate.TickAsync(); Check(r.Api.Calls == 2 && r.Allowed); });
        foreach(var e in new Exception[] { new HttpRequestException(), new CompanionApiException(401, "revocado"), new CompanionApiException(403, "rechazado"), new JsonException(), new FormatException() })
            await Case(e.GetType().Name + (e is CompanionApiException ce ? " " + ce.Status : "") + " apaga Plus", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Api.Reply = (_, _) => Task.FromException<LicenseAnswer>(e); r.Ms = 50_000; await r.Gate.TickAsync(); Check(!r.Allowed); });
        await Case("pérdida de red detectada por pull apaga de inmediato", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Api.Online = false; Check(!r.Allowed && r.Gate.View().Connectivity == "Sin conexión"); });
        await Case("reconexión exige licencia nueva aunque no haya pasado un tick offline", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Api.Online = false; r.Api.Online = true; Check(!r.Allowed); await r.Gate.TickAsync(); Check(r.Allowed && r.Api.Calls == 2); });
        await Case("revocación y renovación del token invalidan sin tick", async () => { using var r = new Rig(); await r.Gate.TickAsync(); r.Api.TokenGeneration++; Check(!r.Allowed); await r.Gate.TickAsync(); Check(r.Allowed); r.Api.Paired = false; Check(!r.Allowed); });
        foreach(bool tokenChange in new[] { false, true })
            await Case("respuesta tardía tras cambiar " + (tokenChange ? "token" : "personaje"), async () => { using var r = new Rig(); var pending = new TaskCompletionSource<LicenseAnswer>(TaskCreationOptions.RunContinuationsAsynchronously); r.Api.Reply = (_, _) => pending.Task; var old = r.Gate.TickAsync(); if(tokenChange) r.Api.TokenGeneration++; else r.Gate.SetCharacter(Other); pending.SetResult(Active()); await old; Check(!r.Allowed); });
        await Case("respuesta vieja no sobrescribe la nueva identidad", async () => { using var r = new Rig(); var pending = new TaskCompletionSource<LicenseAnswer>(TaskCreationOptions.RunContinuationsAsynchronously); r.Api.Reply = (_, _) => pending.Task; var old = r.Gate.TickAsync(); r.Gate.SetCharacter(Other); r.Api.Reply = (c, _) => Task.FromResult(Active(c)); await r.Gate.TickAsync(); Check(r.Allowed); pending.SetResult(Active()); await old; Check(r.Allowed && r.Gate.View().Character!.SameAs(Other)); });
        await Case("mover reloj Windows no cambia plazo monotónico", async () => { using var r = new Rig(); await r.Gate.TickAsync(); var a = Active() with { ServerUtc = Server.AddYears(-10), ExpiresUtc = Server.AddYears(-10).AddSeconds(60) }; r.Api.Reply = (_, _) => Task.FromResult(a); r.Gate.RecheckSoon(); await r.Gate.TickAsync(); r.Ms = 60_000; Check(!r.Allowed); /* No se altera el reloj real del usuario. */ });
        foreach(string malformed in new[] { "{}", "not-json", Json().Replace("true", "\"true\""), Json().Replace("\"offlineGraceSeconds\":0", "\"offlineGraceSeconds\":10"), Json().Replace("2026-10-12T00:00:00.000Z", "ayer"), Json().Replace(Account, "account"), Json().Replace("\"plan\":\"plus\"", "\"plan\":\"free\"") })
            await Case("JSON inválido no concede acceso " + results.Count, () => { bool failed = false; try { using var d = JsonDocument.Parse(malformed); LicenseAnswer.Parse(d.RootElement); } catch { failed = true; } Check(failed); return Task.CompletedTask; });
        await Case("HTTP real del cliente: POST Bearer y referencia oficial", async () => {
            string folder = Path.Combine(Path.GetTempPath(), "fa2c-license-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try { var net = new Transport { Body = Pull }; var client = new CompanionClient(new(), net, new MemoryStore(), folder, () => {}); await client.Pull(); net.Body = Json(); using var gate = new LicenseGate(client); gate.SetCharacter(Darkni); await gate.TickAsync(); Check(gate.Has("companionPvp")); Check(net.Method == HttpMethod.Post && net.Auth == "Bearer " + new string('a', 64)); Check(net.RequestBody == JsonSerializer.Serialize(new { character = Darkni })); net.Offline = true; try { await client.Pull(); } catch(HttpRequestException) {} Check(!gate.Has("companionPvp")); }
            finally { Directory.Delete(folder, true); }
        });
        await Case("pull Plus y caché no autorizan personaje", async () => {
            string folder = Path.Combine(Path.GetTempPath(), "fa2c-license-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try { File.WriteAllText(Path.Combine(folder, "cloud-progress.json"), Pull); var net = new Transport { Body = Pull }; var client = new CompanionClient(new(), net, new MemoryStore(), folder, () => {}); using var gate = new LicenseGate(client); gate.SetCharacter(Darkni); await gate.TickAsync(); Check(!gate.Has("companionMinimap")); await client.Pull(); Check(client.LastPull!.CompanionMinimap && !gate.Has("companionMinimap")); }
            finally { Directory.Delete(folder, true); }
        });
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
            await Case("transporte HTTP " + (int)code + " apaga licencia previamente activa", async () => {
                string folder = Path.Combine(Path.GetTempPath(), "fa2c-license-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                try { var store = new MemoryStore(); var net = new Transport { Body = Pull }; var client = new CompanionClient(new(), net, store, folder, () => {}); await client.Pull(); net.Body = Json(); using var gate = new LicenseGate(client); gate.SetCharacter(Darkni); await gate.TickAsync(); Check(gate.Has("companionMinimap")); net.Status = code; net.Body = "{\"error\":\"revocado\"}"; gate.RecheckSoon(); await gate.TickAsync(); Check(!gate.Has("companionMinimap")); if(code == HttpStatusCode.Unauthorized) Check(!client.Paired && store.Token is null && client.State == CloudState.Revoked); }
                finally { Directory.Delete(folder, true); }
            });
        await Case("JSON inválido en pull corta licencia y conexión", async () => {
            string folder = Path.Combine(Path.GetTempPath(), "fa2c-license-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try { var net = new Transport { Body = Pull }; var client = new CompanionClient(new(), net, new MemoryStore(), folder, () => {}); await client.Pull(); net.Body = Json(); using var gate = new LicenseGate(client); gate.SetCharacter(Darkni); await gate.TickAsync(); Check(gate.Has("companionPvp")); net.Body = "{}"; try { await client.Pull(); } catch(FormatException) {} Check(!client.Online && !gate.Has("companionPvp")); }
            finally { Directory.Delete(folder, true); }
        });
        await Case("cliente rechaza pull tardío luego de desconectar", async () => {
            string folder = Path.Combine(Path.GetTempPath(), "fa2c-license-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try { var net = new Transport { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var client = new CompanionClient(new(), net, new MemoryStore(), folder, () => {}); var pull = client.Pull(); client.Disconnect(); net.Pending.SetResult(new(HttpStatusCode.OK) { Content = new StringContent(Pull) }); try { await pull; } catch(OperationCanceledException) {} Check(!client.Paired && !client.Online && client.LastPull is null); }
            finally { Directory.Delete(folder, true); }
        });
        await Case("resolución exige personaje propio, servidor y región exactos", () => {
            var card = new PlayerCard("Darkni", "Gladiador", 45, null, "Israphel", 0, null, null, null, [], [], DateTimeOffset.UtcNow, Darkni.CharacterId, 2401, "la");
            Check(OwnLicenseIdentity.Resolve((99, "Darkni", 2401), card, "la")!.SameAs(Darkni));
            Check(OwnLicenseIdentity.Resolve(null, card, "la") is null && OwnLicenseIdentity.Resolve((99, "Darkni", 0), card, "la") is null);
            Check(OwnLicenseIdentity.Resolve((99, "Otro", 2401), card, "la") is null && OwnLicenseIdentity.Resolve((99, "Darkni", 2402), card, "la") is null && OwnLicenseIdentity.Resolve((99, "Darkni", 2401), card, "eu") is null);
            return Task.CompletedTask;
        });
        await Case("Windows Credential Manager: escribir, leer, migrar y borrar credencial aislada", () => {
            string folder = Path.Combine(Path.GetTempPath(), "fa2c-cred-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var store = new CredentialTokenStore(Path.Combine(folder, "legacy.dpapi"), "FullAIon2Companion/test-" + Guid.NewGuid().ToString("N"));
            try { Check(store.Read() is null); var token = new string('b', 64); new FileTokenStore(Path.Combine(folder, "legacy.dpapi")).Write(token); Check(store.Read() == token && !File.Exists(Path.Combine(folder, "legacy.dpapi"))); store.Write(new string('c', 64)); Check(store.Read() == new string('c', 64)); store.Delete(); Check(store.Read() is null); }
            finally { store.Delete(); Directory.Delete(folder, true); }
            return Task.CompletedTask;
        });
        return (results.All(r => (bool)r.GetType().GetProperty("ok")!.GetValue(r)!), results);
    }
}
