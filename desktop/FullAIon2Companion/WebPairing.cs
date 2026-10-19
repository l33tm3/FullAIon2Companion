using System.Text.Json;
using System.Text.RegularExpressions;

namespace FullAIon2Companion;

/// <summary>
/// Linking without typing a code (Configuración → Cuenta → «Iniciar sesión y vincular»): the panel opens governanceai.me and,
/// once signed in, the page asks the API for a pairing code (its session and CSRF) and hands it to the app through
/// <c>chrome.webview.postMessage</c>; the app redeems it exactly as a typed code. Only a document of the site itself is
/// heard (https://governanceai.me: no other scheme, host, port or user info), only these two messages, and only a code of
/// 12 hexadecimal characters. The page never gets the device token: it learns whether the app is linked and the outcome.
/// </summary>
public static class WebPairing
{
    public const string Site = "https://governanceai.me";
    /// <summary>The user asked to link from the website this long ago at most: the page then opens its sign-in by itself.</summary>
    public static readonly TimeSpan RequestLife = TimeSpan.FromMinutes(15);

    /// <summary>True only for a document of https://governanceai.me.</summary>
    public static bool FromSite(string? source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "governanceai.me" && uri.IsDefaultPort && uri.UserInfo.Length == 0;

    /// <summary>A message of the site: <c>query</c> (is the app linked?) or <c>pair</c> with its code; null for anything else.</summary>
    public static (string Kind, string? Code)? Parse(string? source, string json)
    {
        if (!FromSite(source)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return null;
            switch (type.GetString())
            {
                case "daeva-app-query": return ("query", null);
                case "daeva-pair":
                    string code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()!.Trim().ToUpperInvariant() : "";
                    return Regex.IsMatch(code, "^[A-F0-9]{12}$") ? ("pair", code) : null;
                default: return null;
            }
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The answer to <c>query</c>: linked or not, the version, and whether the user asked to link from the panel.</summary>
    public static string State(bool paired, bool requested) => UiContract.Serialize(new { type = "daeva-app-state", paired, requested, version = AppVersion.Current });
    /// <summary>The outcome of <c>pair</c>: the same text the panel shows, never the token.</summary>
    public static string Result(bool ok, string message) => UiContract.Serialize(new { type = "daeva-pair-result", ok, message });
}
