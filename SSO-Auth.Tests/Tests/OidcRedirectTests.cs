using System.Net;
using Jellyfin.Plugin.SSO_Auth.Tests.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Tests;

[Collection("Jellyfin")]
public class OidcRedirectTests : IAsyncLifetime
{
    private readonly JellyfinFixture _fixture;

    public OidcRedirectTests(JellyfinFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Start_RedirectsToDexWithPkce()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler);

        var response = await http.GetAsync(
            $"{_fixture.JellyfinBaseUrl}/sso/OID/start/{_fixture.ProviderName}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal("dex.localtest.me", location!.Host);
        Assert.Equal(5556, location.Port);
        Assert.Equal("/dex/auth", location.AbsolutePath);

        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("jellyfin-oid", query["client_id"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.Equal(
            $"{_fixture.JellyfinBaseUrl}/sso/OID/redirect/{_fixture.ProviderName}",
            query["redirect_uri"]);
    }

    [Fact]
    public async Task Start_UnknownProvider_ReturnsError()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler);

        var response = await http.GetAsync(
            $"{_fixture.JellyfinBaseUrl}/sso/OID/start/notdex");

        Assert.False(
            response.IsSuccessStatusCode,
            $"Expected non-2xx for unknown provider, got {(int)response.StatusCode}");
    }
}
