using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.SSO_Auth.TestEnv;

public sealed class ProviderProvisioner(EnvConfig config)
{
    private const string ApiKeyAppName = "test-env";
    private const string AuthHeaderTemplate =
        "MediaBrowser Client=\"jellyfin-sso-test\", Device=\"test-env\", DeviceId=\"test-env-script\", Version=\"1.0.0\"";

    /// <summary>
    /// Idempotent: authenticates as admin, reuses an existing "test-env" API key or mints a new one,
    /// POSTs the dex seed to /sso/OID/Add/{providerName}, and verifies via /sso/OID/Get.
    /// </summary>
    public async Task ProvisionAsync(CancellationToken ct = default)
    {
        if (!File.Exists(config.DexSeedFile))
        {
            throw new OrchestrationException($"Missing seed file: {config.DexSeedFile}");
        }

        using var http = new HttpClient { BaseAddress = new Uri(config.JellyfinBaseUrl) };

        Console.Out.WriteLine($"[+] Authenticating as {config.AdminUsername} ...");
        var token = await AuthenticateAdminAsync(http, ct);

        Console.Out.WriteLine("[+] Minting an API key ...");
        var apiKey = await EnsureApiKeyAsync(http, token, ct);

        Console.Out.WriteLine("[+] Waiting for SSO plugin routes ...");
        await WaitForPluginRoutesAsync(http, apiKey, ct);

        Console.Out.WriteLine($"[+] Registering provider '{config.ProviderName}' from {Path.GetFileName(config.DexSeedFile)} ...");
        var seedJson = await File.ReadAllTextAsync(config.DexSeedFile, ct);
        var addResponse = await http.PostAsync(
            $"/sso/OID/Add/{config.ProviderName}?api_key={Uri.EscapeDataString(apiKey)}",
            new StringContent(seedJson, Encoding.UTF8, "application/json"),
            ct);
        addResponse.EnsureSuccessStatusCode();

        Console.Out.WriteLine("[+] Verifying provider registration ...");
        var getResponse = await http.GetAsync(
            $"/sso/OID/Get?api_key={Uri.EscapeDataString(apiKey)}",
            ct);
        getResponse.EnsureSuccessStatusCode();
        var body = await getResponse.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty(config.ProviderName, out _))
        {
            throw new OrchestrationException(
                $"Provider '{config.ProviderName}' did not appear in /sso/OID/Get response:\n{body}");
        }

        Console.Out.WriteLine($"[+] Provider '{config.ProviderName}' registered.");
    }

    /// <summary>
    /// Waits for the SSO plugin's controller routes to be mapped. On a cold start, Jellyfin's core API
    /// (and even /Users/AuthenticateByName) can respond before the plugin finishes loading, so the
    /// /sso/* routes briefly 404. Polls /sso/OID/Get (which requires the just-minted API key) until it
    /// returns 200, treating 404 and 5xx as transient and any other status as a real failure.
    /// </summary>
    private async Task WaitForPluginRoutesAsync(HttpClient http, string apiKey, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(config.PluginReadyTimeoutSeconds);
        var url = $"/sso/OID/Get?api_key={Uri.EscapeDataString(apiKey)}";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                var code = (int)response.StatusCode;
                if (code != 404 && code is < 500 or >= 600)
                {
                    // Not a cold-start race (e.g. 401/403); surface it immediately.
                    response.EnsureSuccessStatusCode();
                }

                Console.Out.WriteLine($"[+] SSO routes not ready ({code}); retrying ...");
            }
            catch (HttpRequestException)
            {
                // Not up yet.
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // GET timeout; retry.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new OrchestrationException(
            $"SSO plugin routes did not become available within {config.PluginReadyTimeoutSeconds} seconds.");
    }

    private async Task<string> AuthenticateAdminAsync(HttpClient http, CancellationToken ct)
    {
        // TODO: Revisit whether a smarter health probe in ContainerStack.WaitForJellyfinAsync
        // (e.g., poll /Users/AuthenticateByName itself, or wait on a more specific endpoint)
        // would remove the need for this retry loop. For now, the loop handles the known cold-start
        // race where /System/Info/Public is 200 but /Users/AuthenticateByName is 503 for several seconds.
        // On a fresh container start, Jellyfin can take 60+ seconds for the auth endpoint to
        // become available, so 30 attempts at 2s = 60s is the minimum safe budget.
        const int maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/Users/AuthenticateByName")
            {
                Content = JsonContent.Create(new
                {
                    Username = config.AdminUsername,
                    Pw = config.AdminPassword,
                }),
            };
            request.Headers.TryAddWithoutValidation("Authorization", AuthHeaderTemplate);

            using var response = await http.SendAsync(request, ct);

            if ((int)response.StatusCode is >= 500 and < 600 && attempt < maxAttempts)
            {
                Console.Out.WriteLine(
                    $"[+] Admin auth returned {(int)response.StatusCode}; retrying in {delay.TotalSeconds}s "
                    + $"({attempt}/{maxAttempts}) ...");
                await Task.Delay(delay, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("AccessToken", out var tokenElement)
                && tokenElement.GetString() is { Length: > 0 } token)
            {
                return token;
            }

            throw new OrchestrationException("Admin authentication response did not contain AccessToken.");
        }

        throw new OrchestrationException(
            $"Admin authentication failed with 5xx after {maxAttempts} attempts.");
    }

    private async Task<string> EnsureApiKeyAsync(HttpClient http, string token, CancellationToken ct)
    {
        var existing = await GetApiKeyAsync(http, token, ct);
        if (existing is not null)
        {
            return existing;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Auth/Keys?App={Uri.EscapeDataString(ApiKeyAppName)}");
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{token}\"");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var afterCreate = await GetApiKeyAsync(http, token, ct);
        return afterCreate
            ?? throw new OrchestrationException("Failed to obtain an API key after creation request.");
    }

    private static async Task<string?> GetApiKeyAsync(HttpClient http, string token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/Auth/Keys");
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{token}\"");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("AppName", out var appName)
                && appName.GetString() == ApiKeyAppName
                && item.TryGetProperty("AccessToken", out var accessToken))
            {
                var value = accessToken.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
