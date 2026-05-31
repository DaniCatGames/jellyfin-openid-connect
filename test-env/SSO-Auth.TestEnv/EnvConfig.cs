namespace Jellyfin.Plugin.SSO_Auth.TestEnv;

/// <summary>
/// Carries every value that distinguishes one test-env stack from another (port, project name, data dir, ...).
/// A single-stack run uses <see cref="Default"/>. A future parallel runner constructs N configs with distinct values.
/// </summary>
public sealed record EnvConfig
{
    /// <summary>Absolute path to the repo root (containing SSO-Auth.sln).</summary>
    public required string RepoRoot { get; init; }

    /// <summary>Absolute path to the test-env directory (containing docker-compose.yml).</summary>
    public required string TestEnvDir { get; init; }

    /// <summary>Docker compose project name. Still consulted by the bash snapshot-{create,refresh}.sh scripts.</summary>
    public required string ComposeProjectName { get; init; }

    /// <summary>Path to docker-compose.yml. Still used by the bash snapshot scripts; the C# stack defines containers directly.</summary>
    public required string ComposeFile { get; init; }

    /// <summary>Bind-mounted plugin publish directory (host side).</summary>
    public required string PublishDir { get; init; }

    /// <summary>Bind-mounted Jellyfin data root (host side); contains jellyfin/config, jellyfin/cache, jellyfin/media.</summary>
    public required string DataDir { get; init; }

    /// <summary>Directory holding tar.zst snapshots, one per Jellyfin version.</summary>
    public required string SnapshotsDir { get; init; }

    /// <summary>Seed JSON for the dex OIDC provider, posted to /sso/OID/Add/dex.</summary>
    public required string DexSeedFile { get; init; }

    /// <summary>Pinned Jellyfin image tag. Read from $JELLYFIN_VERSION or test-env/.env at Default() time.</summary>
    public required string JellyfinVersion { get; init; }

    /// <summary>Pinned Dex image tag. Read from $DEX_VERSION or test-env/.env at Default() time.</summary>
    public required string DexVersion { get; init; }

    /// <summary>Container name for the Jellyfin instance. Doubles as the host name humans use with `docker logs`.</summary>
    public required string JellyfinContainerName { get; init; }

    /// <summary>Container name for the Dex instance.</summary>
    public required string DexContainerName { get; init; }

    /// <summary>Host-side port the Jellyfin container binds. Should agree with the host part of <see cref="JellyfinBaseUrl"/>.</summary>
    public required int JellyfinHostPort { get; init; }

    /// <summary>Host-side port the Dex container binds (Dex's discovery / auth endpoints).</summary>
    public required int DexHostPort { get; init; }

    /// <summary>Base URL of the running Jellyfin instance, as visible from the host.</summary>
    public required string JellyfinBaseUrl { get; init; }

    /// <summary>Jellyfin admin username for provisioning.</summary>
    public required string AdminUsername { get; init; }

    /// <summary>Jellyfin admin password for provisioning.</summary>
    public required string AdminPassword { get; init; }

    /// <summary>Provider name used in /sso/OID/Add/{provider} and seed file naming.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Wait limit for Jellyfin health probe (seconds).</summary>
    public int JellyfinReadyTimeoutSeconds { get; init; } = 120;

    /// <summary>Wait limit for the SSO plugin routes to become available after Jellyfin is up (seconds).</summary>
    public int PluginReadyTimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Default config matching the current single-stack setup. Resolves <see cref="RepoRoot"/> by walking up from the
    /// executing assembly until SSO-Auth.sln is found.
    /// </summary>
    public static EnvConfig Default()
    {
        var repoRoot = FindRepoRoot();
        var testEnvDir = Path.Combine(repoRoot, "test-env");

        return new EnvConfig
        {
            RepoRoot = repoRoot,
            TestEnvDir = testEnvDir,
            ComposeProjectName = "jellyfin-sso-test",
            ComposeFile = Path.Combine(testEnvDir, "docker-compose.yml"),
            PublishDir = Path.Combine(testEnvDir, ".publish"),
            DataDir = Path.Combine(testEnvDir, ".data"),
            SnapshotsDir = Path.Combine(testEnvDir, "snapshots"),
            DexSeedFile = Path.Combine(testEnvDir, "seed", "dex-provider.json"),
            JellyfinVersion = ResolveDotEnvValue(testEnvDir, "JELLYFIN_VERSION", "10.11.10"),
            DexVersion = ResolveDotEnvValue(testEnvDir, "DEX_VERSION", "v2.45.1"),
            JellyfinContainerName = "jellyfin-sso-test",
            DexContainerName = "dex-sso-test",
            JellyfinHostPort = 8096,
            DexHostPort = 5556,
            JellyfinBaseUrl = "http://localhost:8096",
            AdminUsername = "admin",
            AdminPassword = "admin",
            ProviderName = "dex",
        };
    }

    /// <summary>The jellyfin config bind-mount target, i.e. where the snapshot must be extracted to.</summary>
    public string JellyfinConfigDir => Path.Combine(DataDir, "jellyfin", "config");

    /// <summary>The jellyfin cache bind-mount target.</summary>
    public string JellyfinCacheDir => Path.Combine(DataDir, "jellyfin", "cache");

    /// <summary>The jellyfin media bind-mount target.</summary>
    public string JellyfinMediaDir => Path.Combine(DataDir, "jellyfin", "media");

    /// <summary>Path to the dex config YAML on the host.</summary>
    public string DexConfigFile => Path.Combine(TestEnvDir, "dex", "config.yaml");

    /// <summary>Snapshot path for the currently pinned Jellyfin version.</summary>
    public string SnapshotPath => Path.Combine(SnapshotsDir, $"jellyfin-{JellyfinVersion}.tar.zst");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SSO-Auth.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate SSO-Auth.sln by walking up from {AppContext.BaseDirectory}.");
    }

    private static string ResolveDotEnvValue(string testEnvDir, string key, string fallback)
    {
        // Match _lib.sh resolution: $KEY env var wins, then test-env/.env, then the fallback.
        var fromEnv = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var dotenv = Path.Combine(testEnvDir, ".env");
        if (File.Exists(dotenv))
        {
            foreach (var rawLine in File.ReadAllLines(dotenv))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var lineKey = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim().Trim('"');
                if (lineKey == key && value.Length > 0)
                {
                    return value;
                }
            }
        }

        return fallback;
    }
}
