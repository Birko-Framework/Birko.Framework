using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.OAuth;

namespace Birko.Communication.OAuth.Providers;

/// <summary>
/// The GitHub account a sign-in resolved to. <see cref="Id"/> is GitHub's stable numeric id, the key to store: a login
/// can be renamed and later taken by someone else.
/// </summary>
/// <param name="Id">GitHub's numeric account id.</param>
/// <param name="Login">The current username.</param>
/// <param name="Name">The display name, when set.</param>
/// <param name="Email">The public profile email, when the user made one public; see <see cref="IGitHubUserClient.GetPrimaryEmailAsync"/>.</param>
public sealed record GitHubUser(long Id, string Login, string? Name, string? Email);

/// <summary>Reads the signed-in GitHub user with their access token. The seam a sign-in callback's tests fake.</summary>
public interface IGitHubUserClient
{
    /// <summary><c>GET /user</c>.</summary>
    /// <exception cref="OAuthException">GitHub refused the token or the request (the status code is set).</exception>
    Task<GitHubUser> GetUserAsync(string accessToken, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /user/emails</c>: the primary address when it is verified, or null. Needs the <c>user:email</c> scope.
    /// <c>/user</c>'s <c>email</c> is null for anyone who keeps their address private, which is the GitHub default.
    /// </summary>
    Task<string?> GetPrimaryEmailAsync(string accessToken, CancellationToken ct = default);
}

/// <summary>
/// <see cref="HttpClient"/>-backed <see cref="IGitHubUserClient"/> (register with <c>AddHttpClient</c>). Headers are set
/// per request, so a shared or pooled HttpClient is never modified.
/// </summary>
public sealed class GitHubUserClient : IGitHubUserClient
{
    /// <summary>The API base used by default.</summary>
    public const string DefaultApiBase = "https://api.github.com/";

    private readonly HttpClient _http;
    private readonly Uri _apiBase;
    private readonly string _userAgent;

    /// <param name="http">The HttpClient requests go through.</param>
    /// <param name="userAgent">Sent as User-Agent, which GitHub requires (it answers 403 without one).</param>
    /// <param name="apiBase">The API base, for GitHub Enterprise Server (<c>https://host/api/v3/</c>). Default api.github.com.</param>
    public GitHubUserClient(HttpClient http, string userAgent = "Birko.Communication.OAuth", string? apiBase = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(userAgent)) throw new ArgumentException("A User-Agent is required.", nameof(userAgent));
        _userAgent = userAgent;
        var baseText = apiBase ?? DefaultApiBase;
        _apiBase = new Uri(baseText.EndsWith('/') ? baseText : baseText + "/");
    }

    /// <inheritdoc />
    public async Task<GitHubUser> GetUserAsync(string accessToken, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("user", accessToken, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
            throw new OAuthException("GitHub's user response has no numeric id.");

        return new GitHubUser(
            id.GetInt64(),
            StringOrNull(root, "login") ?? string.Empty,
            StringOrNull(root, "name"),
            StringOrNull(root, "email"));
    }

    /// <inheritdoc />
    public async Task<string?> GetPrimaryEmailAsync(string accessToken, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("user/emails", accessToken, ct).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var primary = entry.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True;
            var verified = entry.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
            if (primary) return verified ? StringOrNull(entry, "email") : null;
        }
        return null;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken)) throw new ArgumentException("An access token is required.", nameof(accessToken));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_apiBase, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The body is GitHub's message ("Bad credentials"); it never echoes the token.
            throw new OAuthException($"GitHub {path} failed ({(int)response.StatusCode}): {Message(body)}",
                null, Message(body), (int)response.StatusCode);
        }
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new OAuthException($"GitHub {path} returned a body that is not JSON.", ex);
        }
    }

    private static string? Message(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? StringOrNull(doc.RootElement, "message") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
