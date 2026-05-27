using Jellyfin.Plugin.SSO_Auth.TestEnv;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Fixtures;

/// <summary>
/// Owns the docker stack for the whole test suite. Wired up via <c>JellyfinCollection</c>
/// as an <c>ICollectionFixture</c>, so xUnit instantiates exactly one of these and shares
/// it across every test class that opts into the collection. InitializeAsync brings the
/// stack up once; DisposeAsync tears it down with --volumes once at suite end. Tests call
/// <see cref="ResetAsync"/> in their own InitializeAsync to get a clean snapshot between
/// methods.
/// </summary>
public sealed class JellyfinFixture : IAsyncLifetime
{
    public EnvConfig Config { get; } = EnvConfig.Default();

    public Orchestrator Orchestrator { get; }

    public string JellyfinBaseUrl => Config.JellyfinBaseUrl;

    public string ProviderName => Config.ProviderName;

    public JellyfinFixture()
    {
        Orchestrator = new Orchestrator(Config);
    }

    public Task InitializeAsync() => Orchestrator.UpAsync();

    public Task DisposeAsync() => Orchestrator.DownAsync(wipeVolumes: true);

    public Task ResetAsync(CancellationToken ct = default) => Orchestrator.ResetAsync(ct);
}
