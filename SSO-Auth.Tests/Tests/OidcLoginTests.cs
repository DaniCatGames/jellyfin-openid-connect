using System.Net;
using Jellyfin.Plugin.SSO_Auth.Tests.Fixtures;
using Jellyfin.Plugin.SSO_Auth.Tests.Helpers;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Tests;

[Collection("Jellyfin")]
public class OidcLoginTests : IAsyncLifetime
{
    private readonly JellyfinFixture _fixture;

    public OidcLoginTests(JellyfinFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RegularUser_LoginSucceeds_AndIsNotAdmin()
    {
        var flow = new OidcFlow(_fixture.JellyfinBaseUrl, _fixture.ProviderName);

        var result = await flow.LoginAsync("user@test.local", "password");

        Assert.True(result.Succeeded, $"Expected success; got denied: {result.DeniedBody}");
        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.NotNull(result.User);
        Assert.Equal("user", result.User!.Name);
        Assert.False(result.User.IsAdministrator);
    }

    [Fact]
    public async Task AdminUser_LoginSucceeds_AndIsAdmin()
    {
        var flow = new OidcFlow(_fixture.JellyfinBaseUrl, _fixture.ProviderName);

        var result = await flow.LoginAsync("admin@test.local", "password");

        Assert.True(result.Succeeded, $"Expected success; got denied: {result.DeniedBody}");
        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.NotNull(result.User);
        Assert.Equal("admin", result.User!.Name);
        Assert.True(result.User.IsAdministrator);
    }

    [Fact]
    public async Task NoAccessUser_IsDeniedAtRedirect()
    {
        var flow = new OidcFlow(_fixture.JellyfinBaseUrl, _fixture.ProviderName);

        var result = await flow.LoginAsync("noaccess@test.local", "password");

        Assert.False(result.Succeeded);
        Assert.Null(result.AccessToken);
        Assert.Null(result.User);
        Assert.Equal(HttpStatusCode.Unauthorized, result.DeniedStatusCode);
        Assert.Equal("Error. Check permissions.", result.DeniedBody);
    }
}
