using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Fixtures;

/// <summary>
/// xUnit collection definition that shares a single <see cref="JellyfinFixture"/> instance
/// across all test classes in the "Jellyfin" collection. This ensures only one docker stack
/// is started (and torn down) for the full test run, avoiding concurrent UpAsync races.
/// </summary>
[CollectionDefinition("Jellyfin")]
public sealed class JellyfinCollection : ICollectionFixture<JellyfinFixture>
{
}
