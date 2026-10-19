using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FullAIon2Companion;

/// <summary>
/// Offline tests of the account client, the website pairing and the title verification (<c>--verify-test</c>): a simulated
/// clock and a fake HTTP transport. Nothing here talks to the real service, touches the real token or the real settings.
/// </summary>
public static class VerificationTests
{
    static readonly CharacterRef Darkni = new("la", 2401, "H8WHJE_su6dZM2K913s8qowlE5DugO6YQwS_XG7yRJM=");
    const string Imunify = "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>One moment, please...</title></head><body><h1>Please wait while your request is being verified...</h1></body></html>";

    public static async Task<(bool Ok, List<object> Results)> Run()
    {
        var results = new List<object>();
        async Task Case(string name, Func<Task<string?>> body)
        {
            string? failure;
            try { failure = await body(); } catch (Exception e) { failure = "excepción: " + e.Message; }
            results.Add(new { name, ok = failure is null, failure });
        }
        static string? Expect(bool condition, string what) => condition ? null : what;

        // Account client against a fake transport.
        await Case("timestamp UTC con Z y tres decimales", () =>
        {
            string t = CompanionClient.Utc(new DateTime(2026, 10, 11, 17, 5, 3, 123, DateTimeKind.Local).AddTicks(4567));
            return Task.FromResult(Expect(Regex.IsMatch(t, @"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$"), "formato " + t));
        });
        await Case("secuencia retomada desde pull y lote conservado ante fallo de red", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                await client.Pull();
                client.Add(new { type = "heartbeat", at = CompanionClient.Utc(DateTime.UtcNow) });
                net.FailNext = 1; await client.Flush(); // lost
                net.RetryNow(client); await client.Flush();
                var sent = net.Telemetry.Select(t => t.Sequence).ToList();
                return Expect(sent.SequenceEqual([42, 42]), "secuencias " + string.Join(",", sent)) ?? Expect(net.Telemetry[0].Body == net.Telemetry[1].Body, "el lote cambió");
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("409 de secuencia se reconcilia con pull", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                await client.Pull();
                client.Add(new { type = "heartbeat", at = CompanionClient.Utc(DateTime.UtcNow) });
                net.Conflict = true; net.LastSequence = 42; await client.Flush(); // the earlier try had arrived
                net.Conflict = false;
                client.Add(new { type = "heartbeat", at = CompanionClient.Utc(DateTime.UtcNow) }); await client.Flush();
                return Expect(net.Telemetry.Select(t => t.Sequence).SequenceEqual([43]), "secuencias " + string.Join(",", net.Telemetry.Select(t => t.Sequence)));
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("revocación (401) borra la sesión y detiene envíos", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                bool raised = false; client.Revoked += () => raised = true;
                net.Revoked = true;
                try { await client.Pull(); } catch (CompanionApiException) { }
                client.Add(new { type = "heartbeat", at = CompanionClient.Utc(DateTime.UtcNow) }); await client.Flush();
                return Expect(raised && !client.Paired && client.State == CloudState.Revoked, "sesión no borrada")
                    ?? Expect(!File.Exists(Path.Combine(dir, "device.dpapi")), "token sigue en disco") ?? Expect(net.Telemetry.Count == 0, "siguió enviando");
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("Retry-After de un 429 se respeta", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                await client.Pull(); client.Add(new { type = "heartbeat", at = CompanionClient.Utc(DateTime.UtcNow) });
                net.TooMany = true; await client.Flush(); net.TooMany = false; await client.Flush();
                return Expect(net.Telemetry.Count == 0, "reintentó antes de Retry-After: " + net.Telemetry.Count);
            }
            finally { Directory.Delete(dir, true); }
        });
        // Operations: diagnostics, notices, releases and plan features (docs/claude-companion-operations-prompt.md).
        await Case("diagnósticos: solo informes válidos y sin datos sensibles", () =>
        {
            var ok = new Diagnostics("0.1.0", "beta", "capturing", "ok", "");
            var bad = new[] { ok with { Channel = "nightly" }, ok with { CaptureState = "running" }, ok with { ErrorCode = "C:\\Users\\nanos" }, ok with { ErrorCode = "token abc" }, ok with { Version = "dev" } };
            string json = System.Text.Json.JsonSerializer.Serialize(ok);
            return Task.FromResult(Expect(ok.Valid && (ok with { ErrorCode = "CAPTURE_NO_GAME" }).Valid, "rechazó un informe válido") ?? Expect(bad.All(b => !b.Valid), "aceptó un informe inválido")
                ?? Expect(json == "{\"version\":\"0.1.0\",\"channel\":\"beta\",\"captureState\":\"capturing\",\"syncState\":\"ok\",\"errorCode\":\"\"}", json));
        });
        await Case("diagnósticos se envían y respetan el intervalo", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                var d = new Diagnostics("0.1.0", "beta", "capturing", "ok", "");
                await client.SendDiagnosticsAsync(d); await client.SendDiagnosticsAsync(d); await client.SendDiagnosticsAsync(d);
                return Expect(net.Diagnostics.Count == 1, "envíos: " + net.Diagnostics.Count) ?? Expect(!net.Diagnostics[0].Contains("aaaa"), "el token viajó en el cuerpo");
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("avisos: solo los vigentes; versiones por canal; permisos con caducidad", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                net.Extra = ",\"announcements\":[{\"id\":\"a1\",\"title\":\"Mantenimiento\",\"body\":\"<b>hoy</b>\",\"level\":\"maintenance\",\"startsAt\":\"2020-01-01T00:00:00Z\",\"endsAt\":\"2099-01-01T00:00:00Z\"},{\"id\":\"a2\",\"title\":\"Viejo\",\"body\":\"x\",\"level\":\"info\",\"startsAt\":\"2020-01-01T00:00:00Z\",\"endsAt\":\"2020-02-01T00:00:00Z\"}],"
                    + "\"releases\":{\"beta\":{\"id\":\"0123456789abcdef0123456789abcdef\",\"version\":\"0.2.0-beta.1\",\"channel\":\"beta\",\"notes\":\"n\",\"sha256\":\"" + new string('a', 64) + "\",\"bytes\":10,\"createdAt\":\"2026-10-11T20:00:00Z\",\"url\":\"/api/operations.php?action=download&id=0123456789abcdef0123456789abcdef\"}}";
                net.Plan = "\"expiresAt\":\"2020-01-01T00:00:00Z\",";
                var pull = await client.Pull();
                var active = pull.Announcements!.Where(a => a.ActiveAt(DateTime.UtcNow)).ToList();
                using var license = new LicenseGate(client); license.SetCharacter(Darkni);
                bool expiredPlan = !license.Has("companionMinimap");
                net.Plan = "";
                await client.Pull();
                return Expect(active.Count == 1 && active[0].Body == "<b>hoy</b>", "avisos vigentes: " + active.Count) ?? Expect(pull.Releases!.ContainsKey("beta") && !pull.Releases.ContainsKey("stable"), "canales")
                    ?? Expect(expiredPlan && !license.Has("companionMinimap") && !license.Has("companionPvp"), "el pull habilitó Plus sin licencia por personaje");
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("comparación de versiones", () => Task.FromResult(
            Expect(AppVersion.IsNewer("0.2.0", "0.1.0") && AppVersion.IsNewer("0.1.10", "0.1.9") && AppVersion.IsNewer("1.0.0", "1.0.0-beta.2") && !AppVersion.IsNewer("0.1.0", "0.1.0") && !AppVersion.IsNewer("0.1.0-beta.1", "0.1.0"), "orden incorrecto")));
        await Case("descarga: verifica tamaño y SHA-256, rechaza otras direcciones", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                byte[] file = Encoding.UTF8.GetBytes("instalador!");
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).ToLowerInvariant();
                net.Download = file;
                var good = new Release("0123456789abcdef0123456789abcdef", "0.2.0", "beta", "", hash, file.Length, null, "/api/operations.php?action=download&id=0123456789abcdef0123456789abcdef");
                string path = await client.DownloadReleaseAsync(good, Path.Combine(dir, "u"));
                string? badHash = null, external = null, gone = null;
                try { await client.DownloadReleaseAsync(good with { Sha256 = new string('b', 64) }, Path.Combine(dir, "u")); } catch (InvalidOperationException e) { badHash = e.Message; }
                try { await client.DownloadReleaseAsync(good with { Url = "https://evil.example/x.exe" }, Path.Combine(dir, "u")); } catch (InvalidOperationException e) { external = e.Message; }
                net.Download = null;
                try { await client.DownloadReleaseAsync(good, Path.Combine(dir, "v")); } catch (InvalidOperationException e) { gone = e.Message; }
                return Expect(File.ReadAllBytes(path).SequenceEqual(file), "archivo verificado distinto") ?? Expect(badHash is not null && !Directory.EnumerateFiles(Path.Combine(dir, "u"), "*.part").Any(), "aceptó una huella incorrecta")
                    ?? Expect(external is not null && net.Requests.All(r => r.Contains("governanceai.me")), "descargó de otra dirección") ?? Expect(gone is not null && !Directory.Exists(Path.Combine(dir, "v")) || gone is not null && !Directory.EnumerateFiles(Path.Combine(dir, "v")).Any(), "archivo desaparecido mal tratado");
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("revocación quita los permisos del plan", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                await client.Pull(); bool before = client.Online;
                net.Revoked = true; try { await client.Pull(); } catch (CompanionApiException) { }
                return Expect(before && !client.Online && !client.Paired && !File.Exists(Path.Combine(dir, "cloud-progress.json")), "permisos tras revocar");
            }
            finally { Directory.Delete(dir, true); }
        });

        // A web page instead of the API's JSON (Imunify360's challenge for IPs on its grey list, 2026-10-14) and the User-Agent.
        await Case("User-Agent FullAIon2Companion/versión (Windows) en cada solicitud", async () =>
        {
            var (client, net, dir) = Client(paired: false);
            try
            {
                await client.Pair("aaaa-bbbb-cccc"); await client.Pull(); await client.SendDiagnosticsAsync(new Diagnostics("0.1.0", "beta", "capturing", "ok", ""));
                string expected = "FullAIon2Companion/" + AppVersion.Current + " (Windows)";
                return Expect(CompanionClient.UserAgent == expected, "User-Agent: " + CompanionClient.UserAgent)
                    ?? Expect(net.Agents.Count >= 3 && net.Agents.All(a => a == expected), "enviados: " + string.Join(" | ", net.Agents.Distinct()));
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("página HTML al vincular: mensaje claro, sin vincular y registrada sin el código", async () =>
        {
            var (client, net, dir) = Client(paired: false);
            try
            {
                net.PageFor = "redeem";
                string? message = null; Exception? other = null;
                try { await client.Pair("ABCDEF012345"); } catch (HtmlAnswerException e) { message = e.Message; } catch (Exception e) { other = e; }
                string log = File.Exists(Path.Combine(dir, "network.log")) ? File.ReadAllText(Path.Combine(dir, "network.log")) : "";
                return Expect(other is null, "otra excepción: " + other?.GetType().Name + " " + other?.Message)
                    ?? Expect(message == "El servidor de seguridad del hosting o tu red devolvió una página en lugar de datos (HTTP 200, «One moment, please...»)", "mensaje: " + message)
                    ?? Expect(!client.Paired && client.State == CloudState.NotPaired && !File.Exists(Path.Combine(dir, "device.dpapi")), "quedó vinculado")
                    ?? Expect(log.Contains(" redeem HTTP 200 text/html «One moment, please...» ") && log.Contains("server=imunify360-webshield/1.21"), "registro: " + log)
                    ?? Expect(!log.Contains("ABCDEF012345") && !log.Contains("Please wait"), "el registro guardó el código o la página");
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("página HTML en pull, aunque traiga 401: no revoca ni guarda el token en el registro", async () =>
        {
            var (client, net, dir) = Client();
            try
            {
                await client.Pull(); bool revoked = false; client.Revoked += () => revoked = true;
                net.PageFor = "pull"; net.PageStatus = HttpStatusCode.Unauthorized;
                Exception? got = null; try { await client.Pull(); } catch (Exception e) { got = e; }
                for (int i = 0; i < 2; i++) try { await client.Pull(); } catch (HtmlAnswerException) { } // the same page again: one line every 10 minutes
                string log = File.ReadAllText(Path.Combine(dir, "network.log"));
                return Expect(got is HtmlAnswerException { StatusCode: HttpStatusCode.Unauthorized }, "excepción: " + got?.GetType().Name)
                    ?? Expect(!revoked && client.Paired && client.State == CloudState.Offline && File.Exists(Path.Combine(dir, "device.dpapi")), "la página revocó el dispositivo")
                    ?? Expect(client.Status == got!.Message && client.Status.Contains("(HTTP 401, «One moment, please...»)"), "estado: " + client.Status)
                    ?? Expect(Regex.Matches(log, " pull HTTP 401 ").Count == 1, "líneas: " + log)
                    ?? Expect(!log.Contains(new string('a', 64)) && !log.Contains("Bearer") && !log.Contains("governanceai.me"), "el registro guardó el token o la dirección");
            }
            finally { Directory.Delete(dir, true); }
        });
        await Case("título de la página: entidades, espacios y largo", () =>
        {
            string? a = CompanionClient.PageTitle("<html><head><TITLE lang=\"es\">\n  Un momento,&nbsp;por&#32;favor&hellip;\n</TITLE>"), b = CompanionClient.PageTitle("<html><body>503</body></html>"),
                c = CompanionClient.PageTitle("<title>" + new string('x', 200) + "</title>"), d = CompanionClient.PageTitle("<title> </title>");
            return Task.FromResult(Expect(a == "Un momento, por favor…", "título: " + a) ?? Expect(b is null && d is null, "sin título") ?? Expect(c?.Length == 80 && c.EndsWith('…'), "largo: " + c?.Length)
                ?? Expect(new HtmlAnswerException(503, null).Message == "El servidor de seguridad del hosting o tu red devolvió una página en lugar de datos (HTTP 503)", "sin título: " + new HtmlAnswerException(503, null).Message));
        });

        // Linking from the website open in the panel (WebPairing): only the site's own documents, only a 12-character code.
        await Case("web: solo el origen https://governanceai.me puede enviar un código", () =>
        {
            const string pair = "{\"type\":\"daeva-pair\",\"code\":\"abcdef012345\"}";
            var good = WebPairing.Parse("https://governanceai.me/#home", pair);
            string[] bad = ["https://governanceai.me.evil.example/", "http://governanceai.me/", "https://governanceai.me:8443/", "https://user@governanceai.me/",
                "https://companion.local/ui/index.html", "https://evil.example/?https://governanceai.me", "https://aion2.plaync.com/", "", "governanceai.me"];
            return Task.FromResult(Expect(good is ("pair", "ABCDEF012345"), "rechazó el sitio: " + good)
                ?? Expect(WebPairing.Parse("https://GOVERNANCEAI.ME/support/", pair)?.Code == "ABCDEF012345", "mayúsculas en el dominio")
                ?? Expect(bad.All(s => WebPairing.Parse(s, pair) is null && !WebPairing.FromSite(s)), "aceptó: " + string.Join(", ", bad.Where(s => WebPairing.Parse(s, pair) is not null))));
        });
        await Case("web: solo mensajes conocidos y códigos de 12 caracteres hexadecimales", () =>
        {
            const string site = "https://governanceai.me/";
            string[] bad = ["{\"type\":\"daeva-pair\",\"code\":\"ABCDEF01234\"}", "{\"type\":\"daeva-pair\",\"code\":\"ABCDEF01234G\"}", "{\"type\":\"daeva-pair\",\"code\":\"ABCD-EF01-2345\"}",
                "{\"type\":\"daeva-pair\"}", "{\"type\":\"daeva-pair\",\"code\":123456789012}", "{\"type\":\"daeva-token\"}", "{\"command\":\"pair\",\"code\":\"ABCDEF012345\"}",
                "\"daeva-app-query\"", "[1]", "no es json"];
            return Task.FromResult(Expect(WebPairing.Parse(site, "{\"type\":\"daeva-app-query\"}") is ("query", null), "consulta rechazada")
                ?? Expect(bad.All(j => WebPairing.Parse(site, j) is null), "aceptó: " + string.Join(" | ", bad.Where(j => WebPairing.Parse(site, j) is not null))));
        });
        await Case("web: el código se canjea como uno escrito y la página nunca recibe el token", async () =>
        {
            var (client, net, dir) = Client(paired: false);
            try
            {
                var m = WebPairing.Parse("https://governanceai.me/#home", "{\"type\":\"daeva-pair\",\"code\":\"abcdef012345\"}")!.Value;
                string before = WebPairing.State(client.Paired, true);
                await client.Pair(m.Code!);
                string token = new('b', 64), after = WebPairing.State(client.Paired, false), result = WebPairing.Result(true, "App vinculada con tu cuenta de Daeva Companion.");
                using var state = JsonDocument.Parse(after);
                var keys = state.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
                return Expect(net.Redeemed.SequenceEqual(["{\"code\":\"ABCDEF012345\"}"]), "canje: " + string.Join(" | ", net.Redeemed))
                    ?? Expect(client.Paired && client.Online && client.DeviceId == new string('2', 32), "no quedó vinculado")
                    ?? Expect(before.Contains("\"paired\":false") && before.Contains("\"requested\":true") && after.Contains("\"paired\":true"), "estado: " + before + " → " + after)
                    ?? Expect(keys.SequenceEqual(["type", "paired", "requested", "version"]), "campos del estado: " + string.Join(",", keys))
                    ?? Expect(!after.Contains(token) && !result.Contains(token) && !before.Contains(token), "un mensaje para la página lleva el token");
            }
            finally { Directory.Delete(dir, true); }
        });
        results.AddRange(await TitleVerificationTests.Run());
        return (results.All(r => (bool)r.GetType().GetProperty("ok")!.GetValue(r)!), results);
    }

    static (CompanionClient, FakeServer, string) Client(bool paired = true)
    {
        string dir = Path.Combine(Path.GetTempPath(), "fa2c-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string tokenFile = Path.Combine(dir, "device.dpapi");
        if (paired) File.WriteAllBytes(tokenFile, System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(new string('a', 64)), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        var net = new FakeServer();
        var client = new CompanionClient(new AppSettings { ShareTelemetry = true }, net, tokenFile, () => { });
        return (client, net, dir);
    }
    sealed class FakeServer : HttpMessageHandler
    {
        public int LastSequence = 41, FailNext;
        public bool Conflict, Revoked, TooMany;
        public string Extra = "", Plan = "";
        public byte[]? Download;
        /// <summary>This action gets Imunify360's challenge page instead of the API (with <see cref="PageStatus"/>).</summary>
        public string? PageFor;
        public HttpStatusCode PageStatus = HttpStatusCode.OK;
        public readonly List<string> Diagnostics = [], Requests = [], Agents = [], Redeemed = [];
        public readonly List<(int Sequence, string Body)> Telemetry = [];
        public void RetryNow(CompanionClient c) => typeof(CompanionClient).GetField("retryNotBefore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(c, DateTime.MinValue);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            Agents.Add(request.Headers.UserAgent.ToString());
            if (request.RequestUri.AbsolutePath == "/api/operations.php")
                return Download is null ? Answer(HttpStatusCode.NotFound, "{\"error\":\"Descarga no disponible.\"}") : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Download) };
            string action = Regex.Match(request.RequestUri!.Query, "action=([a-z-]+)").Groups[1].Value;
            if (action == PageFor)
            {
                var page = new HttpResponseMessage(PageStatus) { Content = new StringContent(Imunify, Encoding.UTF8, "text/html") };
                page.Headers.Server.ParseAdd("imunify360-webshield/1.21");
                return page;
            }
            if (action == "redeem")
            {
                Redeemed.Add(await request.Content!.ReadAsStringAsync(ct));
                return Answer(HttpStatusCode.OK, "{\"accountId\":\"11111111111111111111111111111111\",\"deviceId\":\"22222222222222222222222222222222\",\"accessToken\":\"" + new string('b', 64) + "\",\"tokenType\":\"Bearer\"}");
            }
            if (request.Headers.Authorization?.Scheme != "Bearer") return Answer(HttpStatusCode.Unauthorized, "{\"error\":\"Token de dispositivo no válido.\"}");
            if (Revoked) return Answer(HttpStatusCode.Unauthorized, "{\"error\":\"Dispositivo desconectado.\"}");
            if (action == "pull") return Answer(HttpStatusCode.OK, $"{{\"accountId\":\"11111111111111111111111111111111\",\"deviceId\":\"22222222222222222222222222222222\",\"lastSequence\":{LastSequence},\"activeChallenges\":[],\"linkedCharacters\":[],\"verificationReady\":false,\"subscription\":{{{Plan}\"planName\":\"Plus\",\"features\":{{\"companionPvp\":false,\"companionMinimap\":true}}}}{Extra}}}");
            if (action == "diagnostics") { Diagnostics.Add(await request.Content!.ReadAsStringAsync(ct)); return Answer(HttpStatusCode.OK, "{\"saved\":true}"); }
            if (action == "telemetry")
            {
                string body = await request.Content!.ReadAsStringAsync(ct);
                int seq = JsonDocument.Parse(body).RootElement.GetProperty("sequence").GetInt32();
                if (TooMany) { var r = Answer((HttpStatusCode)429, "{\"error\":\"Demasiadas solicitudes.\"}"); r.Headers.RetryAfter = new(TimeSpan.FromSeconds(60)); return r; }
                if (Conflict) return Answer(HttpStatusCode.Conflict, "{\"error\":\"Secuencia ya recibida.\"}");
                Telemetry.Add((seq, JsonDocument.Parse(body).RootElement.GetProperty("events").GetRawText()));
                if (FailNext-- > 0) throw new HttpRequestException("respuesta perdida");
                LastSequence = seq;
                return Answer(HttpStatusCode.OK, $"{{\"accepted\":1,\"sequence\":{seq}}}");
            }
            return Answer(HttpStatusCode.NotFound, "{\"error\":\"?\"}");
        }
        static HttpResponseMessage Answer(HttpStatusCode code, string json) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
