namespace Jellyfin.Plugin.SSO_Auth.TestEnv;

/// <summary>
/// Top-level orchestration entry. Composes <see cref="PluginPublisher"/>, <see cref="SnapshotStore"/>,
/// <see cref="ContainerStack"/>, and <see cref="ProviderProvisioner"/> into the workflows the bash scripts
/// expose: up, down, reload, provision.
/// </summary>
public sealed class Orchestrator(EnvConfig config)
{
    private readonly PluginPublisher _publisher = new(config);
    private readonly SnapshotStore _snapshots = new(config);
    private readonly ContainerStack _stack = new(config);
    private readonly ProviderProvisioner _provisioner = new(config);

    public async Task UpAsync(CancellationToken ct = default)
    {
        await _publisher.PublishAsync(ct);
        await _snapshots.RestoreIfEmptyAsync(ct);
        await _stack.UpAsync(ct);
        await _stack.WaitForJellyfinAsync(ct);
        Console.Out.WriteLine("[+] Provisioning SSO provider ...");
        await _provisioner.ProvisionAsync(ct);
        PrintUpBanner();
    }

    public async Task DownAsync(bool wipeVolumes, CancellationToken ct = default)
    {
        await _stack.DownAsync(ct);

        if (wipeVolumes)
        {
            Console.Out.WriteLine($"[+] Wiping {config.DataDir} and {config.PublishDir} ...");
            if (Directory.Exists(config.DataDir))
            {
                Directory.Delete(config.DataDir, recursive: true);
            }

            if (Directory.Exists(config.PublishDir))
            {
                Directory.Delete(config.PublishDir, recursive: true);
            }
        }

        Console.Out.WriteLine("[+] Done.");
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _publisher.PublishAsync(ct);
        Console.Out.WriteLine("[+] Restarting Jellyfin ...");
        await _stack.RestartJellyfinAsync(ct);
        await _stack.WaitForJellyfinAsync(ct);
        Console.Out.WriteLine("[+] Reload complete.");
    }

    /// <summary>
    /// Per-test reset for the xUnit fixture in Plan 2. Stops Jellyfin so the bind-mounted config
    /// dir can be safely wiped + extracted fresh, then starts Jellyfin and re-provisions the SSO
    /// provider. Faster than a full down/up cycle (~5-10s vs ~15-20s) because containers, network,
    /// and image cache stay in place.
    /// </summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        Console.Out.WriteLine("[+] Resetting test environment ...");
        await _stack.StopJellyfinAsync(ct);
        await _stack.WipeConfigDirAsync(ct);
        await _snapshots.ForceRestoreAsync(ct);
        await _stack.StartJellyfinAsync(ct);
        await _stack.WaitForJellyfinAsync(ct);
        Console.Out.WriteLine("[+] Re-provisioning SSO provider ...");
        await _provisioner.ProvisionAsync(ct);
        Console.Out.WriteLine("[+] Reset complete.");
    }

    public Task ProvisionAsync(CancellationToken ct = default) => _provisioner.ProvisionAsync(ct);

    private void PrintUpBanner()
    {
        Console.Out.WriteLine();
        Console.Out.WriteLine("================================================================================");
        Console.Out.WriteLine("  Test environment is up.");
        Console.Out.WriteLine();
        Console.Out.WriteLine($"  Jellyfin:  {config.JellyfinBaseUrl}");
        Console.Out.WriteLine($"  Admin:     {config.AdminUsername} / {config.AdminPassword}");
        Console.Out.WriteLine();
        Console.Out.WriteLine("  Seeded test users (password is \"password\" for all):");
        Console.Out.WriteLine("    admin@test.local      groups: jellyfin-admin");
        Console.Out.WriteLine("    user@test.local       groups: jellyfin-users");
        Console.Out.WriteLine("    noaccess@test.local   groups: (none)");
        Console.Out.WriteLine();
        Console.Out.WriteLine("  To start the OIDC login flow, open:");
        Console.Out.WriteLine($"    {config.JellyfinBaseUrl}/sso/OID/start/{config.ProviderName}");
        Console.Out.WriteLine();
        Console.Out.WriteLine("  Useful commands:");
        Console.Out.WriteLine("    scripts/reload.sh           # rebuild plugin + restart Jellyfin");
        Console.Out.WriteLine("    scripts/down.sh             # stop containers");
        Console.Out.WriteLine("    scripts/down.sh --volumes   # stop + wipe Jellyfin state");
        Console.Out.WriteLine("================================================================================");
    }
}
