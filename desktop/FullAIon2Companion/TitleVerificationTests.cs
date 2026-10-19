using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace FullAIon2Companion;

/// <summary>
/// Offline tests of the title verification (part of <c>--verify-test</c>): a fake API and clock for the controller, and a
/// fake HTTP transport for what the client sends and reads. Nothing talks to the real service or touches the real token.
/// </summary>
public static class TitleVerificationTests
{
    static readonly CharacterRef N18 = new("la", 2401, "H8WHJE_su6dZM2K913s8qvNsKI0dKjtVhUZCS6JKSn8=");
    static readonly CharacterRef Darkni = new("la", 2401, "H8WHJE_su6dZM2K913s8qowlE5DugO6YQwS_XG7yRJM=");

    sealed class FakeApi : ITitleApi
    {
        public int Starts, Checks, Cancels;
        public bool VerifyOnCheck;
        public Exception? Fail;
        public TitleChallenge Next = new("cccccccccccccccccccccccccccccccc", N18, "N18", "Ataque", "Hallazgo cotidiano", "pending", null);
        public Task<TitleChallenge> StartTitleChallengeAsync(CharacterRef c, CancellationToken ct = default) { Starts++; if (Fail is { } f) throw f; return Task.FromResult(Next with { Character = c }); }
        public Task<TitleCheck> CheckTitleAsync(string id, CancellationToken ct = default)
        {
            Checks++; if (Fail is { } f) throw f;
            return Task.FromResult(new TitleCheck(Next with { Status = VerifyOnCheck ? "verified" : "pending" }, VerifyOnCheck,
                VerifyOnCheck ? "Personaje verificado. Ya puedes volver a ponerte tu título anterior." : "La web oficial todavía muestra «Hallazgo cotidiano»."));
        }
        public Task CancelTitleAsync(string id, CancellationToken ct = default) { Cancels++; return Task.CompletedTask; }
    }
    static PullState Pull(IReadOnlyList<CharacterRef>? linked = null, IReadOnlyList<TitleChallenge>? pending = null, int limit = 3)
        => new(DateTime.UtcNow, 0, linked ?? [], false, "", false, false, TitleChallenges: pending ?? [],
            LinkedDetails: (linked ?? []).Select(l => new LinkedCharacter(l, l.SameAs(Darkni) ? "Darkni" : "", "official-title-change")).ToList(), CharacterLimit: limit);

    public static async Task<List<object>> Run()
    {
        var results = new List<object>();
        async Task Case(string name, Func<Task<string?>> body)
        {
            string? failure;
            try { failure = await body(); } catch (Exception e) { failure = "excepción: " + e.Message; }
            results.Add(new { name, ok = failure is null, failure });
        }
        static string? Expect(bool condition, string what) => condition ? null : what;
        var clock = new DateTime(2026, 10, 14, 23, 0, 0, DateTimeKind.Utc);
        int refreshes = 0;
        TitleVerification Make(FakeApi api) => new(api, () => clock, () => { refreshes++; return Task.CompletedTask; });

        await Case("título: sin vincular, sin personaje y sin verificar", () =>
        {
            var t = Make(new FakeApi());
            t.UpdateContext(false, N18, "N18", null, null); var a = t.View();
            t.UpdateContext(true, null, null, "Buscando la ficha oficial de N18…", Pull()); var b = t.View();
            t.UpdateContext(true, N18, "N18", null, Pull([Darkni])); var c = t.View();
            return Task.FromResult(Expect(a.State == "unpaired" && !a.CanStart, "sin vincular: " + a.State)
                ?? Expect(b.State == "no-character" && b.Text.Contains("Buscando"), "sin personaje: " + b.State)
                ?? Expect(c.State == "unverified" && c.CanStart && c.Linked.SequenceEqual(["Darkni"]) && c.Limit == 3, "sin verificar: " + c.State));
        });
        await Case("título: iniciar muestra el título y lo comprueba solo cada 20 s", async () =>
        {
            var api = new FakeApi(); var t = Make(api); t.UpdateContext(true, N18, "N18", null, Pull());
            await t.StartAsync(); var v = t.View();
            await t.TickAsync(); int early = api.Checks;
            clock = clock.AddSeconds(21); await t.TickAsync(); var after = t.View();
            return Expect(api.Starts == 1 && v.State == "pending" && v.CurrentTitle == "Hallazgo cotidiano" && v.CategoryLabel == "Ataque" && v.AutoChecking && v.CanCheck, "no quedó pendiente: " + v.State)
                ?? Expect(early == 0, "comprobó antes de 20 s")
                ?? Expect(api.Checks == 1 && after.State == "pending" && after.Message.Contains("todavía"), "no comprobó a los 20 s: " + api.Checks);
        });
        await Case("título: el cambio verifica y pide refrescar la cuenta", async () =>
        {
            var api = new FakeApi { VerifyOnCheck = true }; var t = Make(api); t.UpdateContext(true, N18, "N18", null, Pull());
            await t.StartAsync(); int before = refreshes; await t.CheckAsync(); var v = t.View();
            t.UpdateContext(true, N18, "N18", null, Pull([N18])); var w = t.View();
            return Expect(v.State == "verified" && refreshes == before + 1 && v.Message.StartsWith("Personaje verificado"), "no verificó: " + v.State)
                ?? Expect(w.State == "verified" && !w.CanStart && w.Linked.SequenceEqual(["N18"]), "perdió el verificado con el pull: " + w.State);
        });
        await Case("título: una verificación iniciada en la web también se comprueba sola", async () =>
        {
            var api = new FakeApi(); var t = Make(api);
            t.UpdateContext(true, N18, "N18", null, Pull(pending: [api.Next with { ExpiresAtUtc = clock.AddMinutes(30) }]));
            await t.TickAsync(); var v = t.View();
            var old = Make(new FakeApi()); old.UpdateContext(true, N18, "N18", null, Pull(pending: [api.Next with { ExpiresAtUtc = clock.AddMinutes(-1) }]));
            return Expect(api.Checks == 1 && v.State == "pending", "no comprobó la de la web: " + api.Checks)
                ?? Expect(old.View().State == "unverified", "mostró como pendiente una caducada");
        });
        await Case("título: cancelar no la resucita aunque el pull aún la liste", async () =>
        {
            var api = new FakeApi(); var t = Make(api); var p = Pull(pending: [api.Next]); t.UpdateContext(true, N18, "N18", null, p);
            await t.CancelAsync(); t.UpdateContext(true, N18, "N18", null, p); var v = t.View();
            return Expect(api.Cancels == 1 && v.State == "unverified" && v.Message == "Verificación cancelada.", "volvió a pendiente: " + v.State);
        });
        await Case("título: máximo 3 personajes por cuenta", async () =>
        {
            var api = new FakeApi(); var t = Make(api);
            t.UpdateContext(true, N18, "N18", null, Pull([Darkni, new("la", 2402, "AAAAAAAAAAAA"), new("la", 2403, "BBBBBBBBBBBB")])); var v = t.View();
            await t.StartAsync();
            return Expect(v.State == "full" && !v.CanStart && v.Text.Contains("3 personajes"), "no aplicó el límite: " + v.State)
                ?? Expect(api.Starts == 0, "pidió una verificación con el límite lleno")
                ?? Expect(v.Linked.SequenceEqual(["Darkni", "Personaje", "Personaje"]), "nombres: " + string.Join(",", v.Linked));
        });
        await Case("título: caducada o tope de comprobaciones detienen la comprobación automática; una espera corta no", async () =>
        {
            var api = new FakeApi(); var t = Make(api); t.UpdateContext(true, N18, "N18", null, Pull());
            await t.StartAsync(); api.Fail = new CompanionApiException(429, "Demasiadas comprobaciones. Empieza una verificación nueva.");
            clock = clock.AddSeconds(21); await t.TickAsync(); clock = clock.AddSeconds(21); await t.TickAsync(); var v = t.View();
            var api2 = new FakeApi(); var t2 = Make(api2); t2.UpdateContext(true, N18, "N18", null, Pull());
            await t2.StartAsync(); api2.Fail = new CompanionApiException(409, "Esta verificación ya no está activa. Empieza una nueva.");
            clock = clock.AddSeconds(21); await t2.TickAsync(); var w = t2.View();
            var api3 = new FakeApi(); var t3 = Make(api3); t3.UpdateContext(true, N18, "N18", null, Pull());
            await t3.StartAsync(); api3.Fail = new CompanionApiException(429, "Espera unos segundos antes de volver a comprobar.");
            clock = clock.AddSeconds(21); await t3.TickAsync(); api3.Fail = null; clock = clock.AddSeconds(21); await t3.TickAsync();
            return Expect(api.Checks == 1 && !v.AutoChecking && v.Message.StartsWith("Demasiadas"), "siguió comprobando tras el tope: " + api.Checks)
                ?? Expect(w.State == "unverified" && w.Message.Contains("ya no está activa"), "no cerró la caducada: " + w.State)
                ?? Expect(api3.Checks == 2 && t3.View().AutoChecking, "una espera corta detuvo la comprobación automática: " + api3.Checks);
        });
        await Case("título: el cliente envía y lee pull, title-challenge y title-check", async () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "title-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                string tokenFile = Path.Combine(dir, "device.dpapi");
                File.WriteAllBytes(tokenFile, System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(new string('a', 64)), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
                var net = new FakeServer(); var client = new CompanionClient(new AppSettings(), net, tokenFile, () => { });
                var pull = await client.Pull(); var start = await client.StartTitleChallengeAsync(N18); var check = await client.CheckTitleAsync(start.ChallengeId);
                return Expect(pull.CharacterLimit == 3 && pull.LinkedDetails!.Single().Method == "owner-manual" && pull.LinkedDetails![0].CharacterName == ""
                        && pull.TitleChallenges!.Single().CurrentTitle == "Hallazgo cotidiano" && pull.LinkedCharacters.Single().SameAs(Darkni), "pull mal leído")
                    ?? Expect(start.Character.SameAs(N18) && start.CategoryLabel == "Ataque" && start.ExpiresAtUtc is not null, "title-challenge mal leído")
                    ?? Expect(check.Verified && check.Message.StartsWith("Personaje verificado"), "title-check mal leído")
                    ?? Expect(net.Bodies.Contains("title-challenge {\"character\":{\"region\":\"la\",\"serverId\":2401,\"characterId\":\"H8WHJE_su6dZM2K913s8qvNsKI0dKjtVhUZCS6JKSn8=\"}}")
                        && net.Bodies.Contains("title-check {\"challengeId\":\"cccccccccccccccccccccccccccccccc\"}"), "cuerpos enviados: " + string.Join(" | ", net.Bodies))
                    ?? Expect(net.Bearer.All(b => b), "una llamada salió sin token")
                    ?? Expect(client.Status == "Dispositivo conectado", "estado: " + client.Status);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        return results;
    }

    sealed class FakeServer : HttpMessageHandler
    {
        public readonly List<string> Bodies = [];
        public readonly List<bool> Bearer = [];
        const string Challenge = "\"challengeId\":\"cccccccccccccccccccccccccccccccc\",\"character\":{\"region\":\"la\",\"serverId\":2401,\"characterId\":\"H8WHJE_su6dZM2K913s8qvNsKI0dKjtVhUZCS6JKSn8=\"},"
            + "\"characterName\":\"N18\",\"serverName\":\"Israphel\",\"category\":\"Attack\",\"categoryLabel\":\"Ataque\",\"currentTitle\":\"Hallazgo cotidiano\",\"expiresAt\":\"2026-10-15T00:00:00+00:00\"";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string action = Regex.Match(request.RequestUri!.Query, "action=([a-z-]+)").Groups[1].Value;
            Bearer.Add(request.Headers.Authorization?.Scheme == "Bearer");
            if (request.Content is not null) Bodies.Add(action + " " + await request.Content.ReadAsStringAsync(ct));
            string? json = action switch
            {
                "pull" => "{\"accountId\":\"11111111111111111111111111111111\",\"deviceId\":\"22222222222222222222222222222222\",\"lastSequence\":0,\"activeChallenges\":[],"
                    + "\"linkedCharacters\":[{\"region\":\"la\",\"serverId\":2401,\"characterId\":\"H8WHJE_su6dZM2K913s8qowlE5DugO6YQwS_XG7yRJM=\"}],"
                    + "\"linkedDetails\":[{\"character\":{\"region\":\"la\",\"serverId\":2401,\"characterId\":\"H8WHJE_su6dZM2K913s8qowlE5DugO6YQwS_XG7yRJM=\"},\"characterName\":null,\"method\":\"owner-manual\",\"verifiedAt\":\"2026-10-12T04:14:07+00:00\"}],"
                    + "\"titleChallenges\":[{" + Challenge + ",\"status\":\"pending\"}],\"characterLimit\":3,\"verificationReady\":false,\"subscription\":{\"plan\":\"free\"}}",
                "title-challenge" => "{" + Challenge + ",\"status\":\"pending\"}",
                "title-check" => "{" + Challenge + ",\"status\":\"verified\",\"verified\":true,\"message\":\"Personaje verificado. Ya puedes volver a ponerte tu título anterior.\"}",
                _ => null
            };
            return new HttpResponseMessage(json is null ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent(json ?? "{\"error\":\"?\"}", Encoding.UTF8, "application/json") };
        }
    }
}
