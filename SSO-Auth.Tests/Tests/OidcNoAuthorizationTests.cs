using Jellyfin.Plugin.SSO_Auth.Tests.Fixtures;
using Jellyfin.Plugin.SSO_Auth.Tests.Helpers;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Tests;

[Collection("Jellyfin")]
public class OidcNoAuthorizationTests
{
    private const string NoAuthProvider = "dex-noauth";

    private readonly JellyfinFixture _fixture;

    public OidcNoAuthorizationTests(JellyfinFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FirstLoginWithProviderThatDoesNotAuthorize_StartsWithNoFolderAccess()
    {
        using var jf = new JellyfinClient(_fixture.JellyfinBaseUrl, _fixture.Config);
        await jf.AuthenticateAdminAsync();

        var flow = new OidcFlow(_fixture.JellyfinBaseUrl, NoAuthProvider);
        var result = await flow.LoginAsync("noauthuser@test.local", "password");

        Assert.True(result.Succeeded, $"Expected success; got denied: {result.DeniedBody}");
        Assert.NotNull(result.User);

        var policy = await jf.GetFolderPolicyAsync(Guid.Parse(result.User!.Id));
        Assert.False(policy.EnableAllFolders);
        Assert.Empty(policy.EnabledFolders);
    }
}
