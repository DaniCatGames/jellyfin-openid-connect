using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Helpers;

/// <summary>
/// Drives the full OIDC login flow against the running dex + Jellyfin stack with a single
/// HttpClient + CookieContainer. Mirrors the seven-step walk documented in
/// docs/superpowers/specs/2026-05-27-automated-tests-design.md (the "OidcFlow" section).
/// </summary>
public sealed class OidcFlow(string jellyfinBaseUrl, string providerName)
{
  private static readonly Regex StateInHtml =
      new("var data = '([^']+)'", RegexOptions.Compiled);

  // TODO: Split each step into steps
  public async Task<OidcLoginResult> LoginAsync(
      string email,
      string password,
      CancellationToken ct = default)
  {
    var cookies = new CookieContainer();
    using var handler = new HttpClientHandler
    {
      CookieContainer = cookies,
      AllowAutoRedirect = false,
      UseCookies = true,
    };
    using var http = new HttpClient(handler);

    // Step 1: GET /sso/OID/start/{provider} — expect 302 to dex.
    var startResponse = await http.GetAsync(
        $"{jellyfinBaseUrl}/sso/OID/start/{providerName}", ct);
    if (startResponse.StatusCode != HttpStatusCode.Found)
    {
      return OidcLoginResult.Denied(startResponse.StatusCode, await ReadAsync(startResponse, ct));
    }

    var dexAuthUri = startResponse.Headers.Location
        ?? throw new InvalidOperationException("Step 1 missing Location header.");

    // Step 2: GET dex /auth — expect 302 to /auth/local?...
    var step2 = await http.GetAsync(dexAuthUri, ct);
    var step3Uri = ResolveLocation(dexAuthUri, step2);

    // Step 3: GET /auth/local — expect 302 to /auth/local/login?back=&state=<dex-session>
    var step3 = await http.GetAsync(step3Uri, ct);
    var loginUri = ResolveLocation(step3Uri, step3);

    // Step 4: POST credentials.
    var loginContent = new FormUrlEncodedContent(new[]
    {
            new KeyValuePair<string, string>("login", email),
            new KeyValuePair<string, string>("password", password),
        });
    var step4 = await http.PostAsync(loginUri, loginContent, ct);
    if (step4.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.SeeOther))
    {
      throw new InvalidOperationException(
          $"Dex login POST returned {(int)step4.StatusCode}; expected 302/303. "
          + $"Body: {await ReadAsync(step4, ct)}");
    }

    var redirectUri = ResolveLocation(loginUri, step4);

    // Step 5: GET plugin /redirect endpoint.
    var step5 = await http.GetAsync(redirectUri, ct);

    if (!step5.IsSuccessStatusCode)
    {
      // Denied path: e.g. the no-access user gets 401 with "Error. Check permissions."
      return OidcLoginResult.Denied(step5.StatusCode, await ReadAsync(step5, ct));
    }

    var html = await ReadAsync(step5, ct);
    var stateMatch = StateInHtml.Match(html);
    if (!stateMatch.Success)
    {
      throw new InvalidOperationException(
          "Plugin /redirect HTML did not contain `var data = '<state>'`. "
          + $"Body (first 400 chars): {html[..Math.Min(400, html.Length)]}");
    }

    var stateToken = stateMatch.Groups[1].Value;

    // Step 6: POST /sso/OID/Auth/{provider}.
    var authPayload = new
    {
      deviceId = "oidc-flow-test",
      appName = "oidc-flow-test",
      appVersion = "1.0.0",
      deviceName = "oidc-flow-test",
      data = stateToken,
    };
    var authBody = new StringContent(
        JsonSerializer.Serialize(authPayload),
        System.Text.Encoding.UTF8,
        "application/json");
    var step6 = await http.PostAsync(
        $"{jellyfinBaseUrl}/sso/OID/Auth/{providerName}", authBody, ct);
    step6.EnsureSuccessStatusCode();

    var resultBody = await ReadAsync(step6, ct);
    var result = JsonSerializer.Deserialize<JsonElement>(resultBody);

    var accessToken = result.GetProperty("AccessToken").GetString()
        ?? throw new InvalidOperationException("AuthenticationResult.AccessToken was null.");
    var userElement = result.GetProperty("User");

    return OidcLoginResult.Success(accessToken, JellyfinUser.From(userElement));
  }

  private static Uri ResolveLocation(Uri baseUri, HttpResponseMessage response)
  {
    var location = response.Headers.Location
        ?? throw new InvalidOperationException(
            $"Expected Location header on {(int)response.StatusCode} response; got none.");

    return location.IsAbsoluteUri ? location : new Uri(baseUri, location);
  }

  private static Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
      response.Content.ReadAsStringAsync(ct);
}

public sealed record OidcLoginResult(
    bool Succeeded,
    string? AccessToken,
    JellyfinUser? User,
    HttpStatusCode? DeniedStatusCode,
    string? DeniedBody)
{
  public static OidcLoginResult Success(string accessToken, JellyfinUser user) =>
      new(true, accessToken, user, null, null);

  public static OidcLoginResult Denied(HttpStatusCode statusCode, string body) =>
      new(false, null, null, statusCode, body);
}

public sealed record JellyfinUser(string Id, string Name, bool IsAdministrator)
{
  public static JellyfinUser From(JsonElement user)
  {
    var policy = user.GetProperty("Policy");
    return new JellyfinUser(
        Id: user.GetProperty("Id").GetString() ?? throw new InvalidOperationException("User.Id missing"),
        Name: user.GetProperty("Name").GetString() ?? throw new InvalidOperationException("User.Name missing"),
        IsAdministrator: policy.GetProperty("IsAdministrator").GetBoolean());
  }
}
