using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Jellyfin.Plugin.SSO_Auth.TestEnv;

/// <summary>
/// Manages the Jellyfin + Dex container topology. Uses Testcontainers' builder DSL for
/// container creation (with the resource reaper disabled so containers persist beyond the
/// CLI process), and Docker.DotNet directly for process-independent lifecycle operations
/// (stop, remove, restart by name).
/// </summary>
public sealed class ContainerStack(EnvConfig config)
{
    public async Task UpAsync(CancellationToken ct = default)
    {
        // Idempotency: tear down any prior containers with our names so the start is deterministic.
        await StopAndRemoveAllAsync(ct);
        EnsureHostDirsExist();

        Console.Out.WriteLine($"[+] Starting {config.DexContainerName} ...");
        var dex = new ContainerBuilder($"ghcr.io/dexidp/dex:{config.DexVersion}")
            .WithName(config.DexContainerName)
            .WithPortBinding(config.DexHostPort, 5556)
            .WithBindMount(config.DexConfigFile, "/etc/dex/config.yaml", AccessMode.ReadOnly)
            .WithCommand("dex", "serve", "/etc/dex/config.yaml")
            .WithCleanUp(false)
            .WithAutoRemove(false)
            .Build();
        await dex.StartAsync(ct);

        Console.Out.WriteLine($"[+] Starting {config.JellyfinContainerName} ...");
        var jellyfin = new ContainerBuilder($"jellyfin/jellyfin:{config.JellyfinVersion}")
            .WithName(config.JellyfinContainerName)
            .WithPortBinding(config.JellyfinHostPort, 8096)
            .WithBindMount(config.JellyfinConfigDir, "/config")
            .WithBindMount(config.JellyfinCacheDir, "/cache")
            .WithBindMount(config.JellyfinMediaDir, "/media")
            .WithBindMount(config.PublishDir, "/config/plugins/SSO-Auth")
            .WithExtraHost("dex.localtest.me", "host-gateway")
            .WithCleanUp(false)
            .WithAutoRemove(false)
            .Build();
        await jellyfin.StartAsync(ct);
    }

    public Task DownAsync(CancellationToken ct = default) => StopAndRemoveAllAsync(ct);

    /// <summary>
    /// Wipes the Jellyfin config directory by running a short-lived container (using the already-
    /// pulled Jellyfin image) with the config dir bind-mounted and executing <c>rm -rf</c> inside
    /// it as root. This is necessary because the Jellyfin container runs as root, so the bind-
    /// mounted config files are root-owned and cannot be deleted by the current (non-root) host
    /// process.
    /// </summary>
    public async Task WipeConfigDirAsync(CancellationToken ct = default)
    {
        Console.Out.WriteLine($"[+] Wiping config dir via privileged container ...");
        using var client = CreateDockerClient();

        var jellyfinImage = $"jellyfin/jellyfin:{config.JellyfinVersion}";
        var created = await client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = jellyfinImage,
                // Override the Jellyfin entrypoint so this runs as a plain shell one-shot.
                Entrypoint = new[] { "sh" },
                Cmd = new[] { "-c", "rm -rf /target/*" },
                HostConfig = new HostConfig
                {
                    Binds = new[] { $"{config.JellyfinConfigDir}:/target" },
                    AutoRemove = false,
                },
            },
            ct);

        await client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);

        // Wait for the container to finish (it exits immediately after rm -rf).
        var waitResult = await client.Containers.WaitContainerAsync(created.ID, ct);
        if (waitResult.StatusCode != 0)
        {
            throw new OrchestrationException(
                $"Config dir wipe container exited with code {waitResult.StatusCode}.");
        }

        // Clean up the stopped container.
        try
        {
            await client.Containers.RemoveContainerAsync(
                created.ID, new ContainerRemoveParameters(), ct);
        }
        catch (DockerContainerNotFoundException)
        {
            // Already gone.
        }
    }

    public async Task RestartJellyfinAsync(CancellationToken ct = default)
    {
        using var client = CreateDockerClient();
        try
        {
            await client.Containers.RestartContainerAsync(
                config.JellyfinContainerName,
                new ContainerRestartParameters { WaitBeforeKillSeconds = 10 },
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            throw new OrchestrationException($"Container '{config.JellyfinContainerName}' does not exist; run 'up' first.");
        }
    }

    public async Task StopJellyfinAsync(CancellationToken ct = default)
    {
        using var client = CreateDockerClient();
        try
        {
            await client.Containers.StopContainerAsync(
                config.JellyfinContainerName,
                new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            throw new OrchestrationException(
                $"Container '{config.JellyfinContainerName}' does not exist; run 'up' first.");
        }
    }

    public async Task StartJellyfinAsync(CancellationToken ct = default)
    {
        using var client = CreateDockerClient();
        try
        {
            await client.Containers.StartContainerAsync(
                config.JellyfinContainerName,
                new ContainerStartParameters(),
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            throw new OrchestrationException(
                $"Container '{config.JellyfinContainerName}' does not exist; run 'up' first.");
        }
    }

    public async Task WaitForJellyfinAsync(CancellationToken ct = default)
    {
        Console.Out.WriteLine($"[+] Waiting for Jellyfin to respond on {config.JellyfinBaseUrl} ...");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(config.JellyfinReadyTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var response = await http.GetAsync($"{config.JellyfinBaseUrl}/System/Info/Public", ct);
                if (response.IsSuccessStatusCode)
                {
                    Console.Out.WriteLine("[+] Jellyfin is up.");
                    return;
                }
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

        throw new OrchestrationException($"Jellyfin did not become ready within {config.JellyfinReadyTimeoutSeconds} seconds.");
    }

    private async Task StopAndRemoveAllAsync(CancellationToken ct)
    {
        using var client = CreateDockerClient();
        await StopAndRemoveByNameAsync(client, config.JellyfinContainerName, ct);
        await StopAndRemoveByNameAsync(client, config.DexContainerName, ct);
    }

    private static async Task StopAndRemoveByNameAsync(IDockerClient client, string name, CancellationToken ct)
    {
        try
        {
            await client.Containers.StopContainerAsync(
                name,
                new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            return;
        }

        try
        {
            await client.Containers.RemoveContainerAsync(
                name,
                new ContainerRemoveParameters(),
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            // Removed concurrently — fine.
        }
    }

    private void EnsureHostDirsExist()
    {
        Directory.CreateDirectory(config.JellyfinConfigDir);
        Directory.CreateDirectory(config.JellyfinCacheDir);
        Directory.CreateDirectory(config.JellyfinMediaDir);
        Directory.CreateDirectory(config.PublishDir);
    }

    private static IDockerClient CreateDockerClient() =>
        new DockerClientBuilder().Build();
}
